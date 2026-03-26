using ERP.Data;                 // AppDbContext
using ERP.Models;               // PurchaseInvoice, LedgerEntry, LedgerSourceType
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace ERP.Services
{
    /// <summary>
    /// خدمة الترحيل المحاسبي (دفتر الأستاذ).
    ///
    /// ✅ إصلاح مهم:
    /// - عدم الاعتماد على CreatedAt في ربط سطر المدين بسطر الدائن (لأنه قد يختلف بجزء من الثانية)
    /// - الربط الآن يتم عن طريق "رقم المرحلة" المكتوب داخل Description
    ///   مثال: "ترحيل فاتورة مشتريات رقم 1104 (مرحلة 1)"
    ///
    /// منطق إعادة الترحيل بعد فتح الفاتورة:
    /// 1) نعكس قيود آخر مرحلة (مرحلة سابقة)
    /// 2) نخصم رصيد المورد القديم
    /// 3) ننشئ قيود مرحلة جديدة بالقيمة الجديدة
    /// 4) نضيف رصيد المورد الحالي
    /// </summary>
    public interface ILedgerPostingService
    {
        Task ReverseForHeaderDeleteAsync(LedgerSourceType sourceType, int sourceId, string? postedBy, string? reason = null);
        Task PostPurchaseInvoiceAsync(int purchaseInvoiceId, string? postedBy);
        /// <summary>ترحيل فاتورة مشتريات. عند تمرير transaction يستخدمه ولا يفتح واحداً جديداً (للاستدعاء من داخل Convert مثلاً).</summary>
        Task PostPurchaseInvoiceAsync(int purchaseInvoiceId, string? postedBy, IDbContextTransaction? existingTransaction);
        Task PostSalesInvoiceAsync(int salesInvoiceId, string? postedBy);
        Task PostCashReceiptAsync(int cashReceiptId, string? postedBy);
        Task PostCashPaymentAsync(int cashPaymentId, string? postedBy);
        Task PostSalesReturnAsync(int salesReturnId, string? postedBy);
        Task PostPurchaseReturnAsync(int purchaseReturnId, string? postedBy);
        Task PostStockAdjustmentAsync(int stockAdjustmentId, string? postedBy);
        Task ReverseStockAdjustmentAsync(int stockAdjustmentId, string? postedBy);
        Task PostStockTransferAsync(int stockTransferId, string? postedBy);
        Task ReverseStockTransferAsync(int stockTransferId, string? postedBy);
        Task PostDebitNoteAsync(int debitNoteId, string? postedBy);
        Task PostCreditNoteAsync(int creditNoteId, string? postedBy);
        Task RecalcAllCustomerBalancesAsync();




    }

    public class LedgerPostingService : ILedgerPostingService
    {
        private readonly AppDbContext _db; // متغير: سياق قاعدة البيانات
        private readonly ILogger<LedgerPostingService> _logger;

        public LedgerPostingService(AppDbContext db, ILogger<LedgerPostingService> logger)
        {
            _db = db;
            _logger = logger;
        }

        private async Task ExecuteWithLedgerPerfAsync(string operation, object? id, Func<Task> action)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await action();
                sw.Stop();
                _logger.LogInformation(
                    "ERP.Technical.Perf LedgerPosting {Operation} Id={Id} DurationMs={DurationMs} Success=true",
                    operation, id, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex,
                    "ERP.Technical.Perf LedgerPosting {Operation} Id={Id} DurationMs={DurationMs} Success=false",
                    operation, id, sw.ElapsedMilliseconds);
                throw;
            }
        }






        public Task PostPurchaseInvoiceAsync(int purchaseInvoiceId, string? postedBy)
            => PostPurchaseInvoiceAsync(purchaseInvoiceId, postedBy, null);

        public Task PostPurchaseInvoiceAsync(int purchaseInvoiceId, string? postedBy, IDbContextTransaction? existingTransaction)
            => ExecuteWithLedgerPerfAsync(nameof(PostPurchaseInvoiceAsync), purchaseInvoiceId,
                () => PostPurchaseInvoiceTransactionAsync(purchaseInvoiceId, postedBy, existingTransaction));

        private async Task PostPurchaseInvoiceTransactionAsync(int purchaseInvoiceId, string? postedBy, IDbContextTransaction? existingTransaction)
        {
            IDbContextTransaction? tx = null;
            if (existingTransaction == null)
                tx = await _db.Database.BeginTransactionAsync();

            try
            {
                await PostPurchaseInvoiceCoreAsync(purchaseInvoiceId, postedBy);

                if (tx != null)
                    await tx.CommitAsync();
            }
            catch
            {
                if (tx != null)
                    await tx.RollbackAsync();
                throw;
            }
            finally
            {
                if (tx != null)
                    await tx.DisposeAsync();
            }
        }

        private async Task PostPurchaseInvoiceCoreAsync(int purchaseInvoiceId, string? postedBy)
        {
            // ================================
            // 1) تحميل الفاتورة + المورد
            // ================================
                var invoice = await _db.PurchaseInvoices
                    .Include(x => x.Customer) // مهم: علشان AccountId للمورد
                    .FirstOrDefaultAsync(x => x.PIId == purchaseInvoiceId);

                if (invoice == null)
                    throw new Exception("الفاتورة غير موجودة.");

                // ================================
                // 2) منع الترحيل لو الفاتورة مقفولة
                // ================================
                // تعليق: الترحيل يقفل الفاتورة - لازم زر الفتح هو اللي يحولها false قبل إعادة الترحيل
                if (invoice.IsPosted)
                    throw new Exception("هذه الفاتورة مرحلة بالفعل. افتح الفاتورة أولاً قبل إعادة الترحيل.");

                // ================================
                // 3) تأكد أن المورد مرتبط بحساب محاسبي
                // ================================
                if (invoice.Customer == null)
                    throw new Exception("بيانات المورد غير محمّلة.");

                if (invoice.Customer.AccountId == null || invoice.Customer.AccountId <= 0)
                    throw new Exception("هذا المورد ليس مرتبطًا بحساب محاسبي داخل شجرة الحسابات.");

                // ================================
                // 4) حساب المخزن (ثابت = 1105)
                // ================================
                int inventoryAccountId = await ResolveAccountIdByCodeAsync("1105"); // متغير: AccountId لحساب المخزن

                // ================================
                // 5) قيم مشتركة
                // ================================
                var now = DateTime.UtcNow;                  // متغير: وقت التنفيذ الحالي (UTC)
                decimal newAmount = invoice.NetTotal;        // متغير: صافي الفاتورة بعد أي تعديل
                string voucherNo = invoice.PIId.ToString();  // متغير: رقم الفاتورة كنص

                // =========================================================
                // 6) تحديد آخر مرحلة مُرحّلة سابقاً (بدون الاعتماد على CreatedAt)
                // =========================================================
                // تعليق: نقرأ رقم المرحلة من Description لسطر المدين فقط (LineNo = 1)
                // لأن كل مرحلة ترحيل لها سطر مدين ثابت + سطر دائن ثابت.
                int lastStage = await GetLastPurchaseInvoiceStageAsync(invoice.PIId); // متغير: آخر مرحلة موجودة

                // المرحلة الجديدة = آخر مرحلة + 1
                int newStage = lastStage + 1;

                // =========================================================
                // 7) لو فيه مرحلة سابقة => اعكسها قبل إنشاء المرحلة الجديدة
                // =========================================================
                if (lastStage > 0)
                {
                    // 7.1) جلب سطر المدين للمرحلة السابقة
                    var lastDebit = await _db.LedgerEntries
                        .Where(e =>
                            e.SourceType == LedgerSourceType.PurchaseInvoice &&
                            e.SourceId == invoice.PIId &&
                            e.LineNo == 1 &&
                            e.Description != null &&
                            e.Description.Contains($"(مرحلة {lastStage})"))
                        .OrderByDescending(e => e.Id)
                        .FirstOrDefaultAsync();

                    // 7.2) جلب سطر الدائن للمرحلة السابقة
                    var lastCredit = await _db.LedgerEntries
                        .Where(e =>
                            e.SourceType == LedgerSourceType.PurchaseInvoice &&
                            e.SourceId == invoice.PIId &&
                            e.LineNo == 2 &&
                            e.Description != null &&
                            e.Description.Contains($"(مرحلة {lastStage})"))
                        .OrderByDescending(e => e.Id)
                        .FirstOrDefaultAsync();

                    // لو لأي سبب ما لقيناش السطور (بيانات غير متسقة) نوقف حفاظاً على الدقة
                    if (lastDebit == null || lastCredit == null)
                        throw new Exception("تعذر تحديد آخر ترحيل سابق للفواتورة (بيانات القيود غير مكتملة).");

                    decimal oldAmount = lastDebit.Debit; // متغير: قيمة المرحلة السابقة

                    // ================================
                    // 7.3) إنشاء القيود العكسية (مرحلة عكس قبل مرحلة جديدة)
                    // ================================
                    // عكس المدين -> دائن
                    var reverseDebit = new LedgerEntry
                    {
                        EntryDate = invoice.PIDate,
                        SourceType = LedgerSourceType.PurchaseInvoice,
                        VoucherNo = voucherNo,
                        SourceId = invoice.PIId,

                        LineNo = 9001, // رقم عالي للتمييز
                        PostVersion = lastStage, // متغير: نُسند العكس لنفس مرحلة الترحيل القديمة

                        AccountId = lastDebit.AccountId,
                        CustomerId = lastDebit.CustomerId,

                        Debit = 0m,
                        Credit = oldAmount,

                        Description = $"عكس ترحيل فاتورة مشتريات رقم {invoice.PIId} (عكس مرحلة {lastStage})",
                        CreatedAt = now
                    };

                    // عكس الدائن -> مدين
                    var reverseCredit = new LedgerEntry
                    {
                        EntryDate = invoice.PIDate,
                        SourceType = LedgerSourceType.PurchaseInvoice,
                        VoucherNo = voucherNo,
                        SourceId = invoice.PIId,

                        LineNo = 9002,
                        PostVersion = lastStage, // ✅
                        AccountId = lastCredit.AccountId,
                        CustomerId = lastCredit.CustomerId,

                        Debit = oldAmount,
                        Credit = 0m,

                        Description = $"عكس ترحيل فاتورة مشتريات رقم {invoice.PIId} (عكس مرحلة {lastStage})",
                        CreatedAt = now
                    };

                    _db.LedgerEntries.Add(reverseDebit);
                    _db.LedgerEntries.Add(reverseCredit);

                    // ================================
                    // 7.4) خصم رصيد المورد القديم (المذكور في سطر الدائن للمرحلة السابقة)
                    // ================================
                    if (lastCredit.CustomerId.HasValue && lastCredit.CustomerId.Value > 0)
                    {
                        var oldSupplier = await _db.Customers
                            .FirstOrDefaultAsync(c => c.CustomerId == lastCredit.CustomerId.Value);

                        if (oldSupplier != null)
                            oldSupplier.CurrentBalance -= oldAmount;
                    }
                    else
                    {
                        // حل احتياطي: لو CustomerId مش موجود في قيد الدائن لأي سبب
                        invoice.Customer.CurrentBalance -= oldAmount;
                    }
                }

                // =========================================================
                // 8) إنشاء قيود المرحلة الجديدة (قيدين طبيعيين)
                // =========================================================
                int supplierAccountId = invoice.Customer.AccountId.Value; // حساب المورد الحالي

                // (1) مدين: المخزن
                var debitRow = new LedgerEntry
                {
                    EntryDate = invoice.PIDate,
                    SourceType = LedgerSourceType.PurchaseInvoice,
                    VoucherNo = voucherNo,
                    SourceId = invoice.PIId,
                    LineNo = 1,
                    PostVersion = newStage, // ✅ متغير: مرحلة الترحيل لهذه القيود


                    AccountId = inventoryAccountId,
                    CustomerId = null,

                    Debit = newAmount,
                    Credit = 0m,

                    Description = $"ترحيل فاتورة مشتريات رقم {invoice.PIId} (مرحلة {newStage})",
                    CreatedAt = now
                };

                // (2) دائن: المورد
                var creditRow = new LedgerEntry
                {
                    EntryDate = invoice.PIDate,
                    SourceType = LedgerSourceType.PurchaseInvoice,
                    VoucherNo = voucherNo,
                    SourceId = invoice.PIId,
                    LineNo = 2,
                    PostVersion = newStage, // ✅ مرحلة الترحيل


                    AccountId = supplierAccountId,
                    CustomerId = invoice.CustomerId,

                    Debit = 0m,
                    Credit = newAmount,

                    Description = $"ترحيل فاتورة مشتريات رقم {invoice.PIId} (مرحلة {newStage})",
                    CreatedAt = now
                };

                _db.LedgerEntries.Add(debitRow);
                _db.LedgerEntries.Add(creditRow);

                // =========================================================
                // 9) تحديث حالة الفاتورة (قفل) — مع الحفاظ على وقت الإنشاء الأصلي
                // =========================================================
                var originalCreatedAt = invoice.CreatedAt;   // لا نغيّر وقت الإنشاء بعد الترحيل
                invoice.IsPosted = true;
                invoice.Status = $"مرحلة {newStage}";
                invoice.PostedAt = now;
                invoice.PostedBy = postedBy;
                invoice.CreatedAt = originalCreatedAt;
                _db.Entry(invoice).Property(x => x.CreatedAt).IsModified = true; // إجبار كتابة وقت الإنشاء في UPDATE

                // =========================================================
                // 10) حفظ القيود + حالة الفاتورة أولاً
                // ✅ لازم SaveChanges هنا قبل حساب الرصيد
                // لأن RecalcCustomerCurrentBalanceAsync بيقرأ من LedgerEntries من قاعدة البيانات.
                // =========================================================
                await _db.SaveChangesAsync();

                // =========================================================
                // 11) تحديث رصيد المورد/العميل (مصدر الحقيقة = LedgerEntries)
                // =========================================================
                if (invoice.CustomerId > 0)
                {
                    await RecalcCustomerCurrentBalanceAsync(invoice.CustomerId);
                    await _db.SaveChangesAsync(); // تعليق: حفظ رصيد العميل بعد إعادة الحساب
                }
        }







        // =========================================================
        // دالة: تحديد آخر مرحلة ترحيل لفاتورة مشتريات من Description
        // =========================================================
        private async Task<int> GetLastPurchaseInvoiceStageAsync(int piId)
        {
            // تعليق: نقرأ فقط سطور الترحيل الطبيعية (LineNo=1) لأن كل مرحلة لها سطر مدين واحد
            var entries = await _db.LedgerEntries
                .Where(e =>
                    e.SourceType == LedgerSourceType.PurchaseInvoice &&
                    e.SourceId == piId &&
                    e.LineNo == 1 &&
                    e.Description != null &&
                    e.Description.Contains("ترحيل فاتورة مشتريات رقم"))
                .Select(e => e.Description!)
                .ToListAsync();

            int maxStage = 0; // متغير: أكبر مرحلة وجدناها

            // Regex لالتقاط رقم المرحلة من النص: (مرحلة 1) أو (مرحلة 12)
            var rx = new Regex(@"\(مرحلة\s+(\d+)\)", RegexOptions.Compiled);

            foreach (var d in entries)
            {
                var m = rx.Match(d);
                if (m.Success && int.TryParse(m.Groups[1].Value, out int stage))
                {
                    if (stage > maxStage) maxStage = stage;
                }
            }

            return maxStage;
        }

        // =========================================================
        // دالة: تحويل AccountCode (مثل 1105) إلى AccountId
        // =========================================================
        private async Task<int> ResolveAccountIdByCodeAsync(string accountCode)
        {
            int accountId = await _db.Accounts
                .Where(a => a.AccountCode == accountCode)
                .Select(a => a.AccountId)
                .FirstOrDefaultAsync();

            if (accountId > 0) return accountId;

            throw new Exception($"لم يتم العثور على حساب بالكود ({accountCode}) داخل شجرة الحسابات. برجاء إضافته.");
        }








        // =========================================================
        // ✅ دالة عامة: عكس آخر ترحيل لمستند بهدف الحذف من قوائم الهيدر
        // - لا تنشئ ترحيل جديد
        // - تعكس آخر مرحلة مُرحّلة فقط (LineNo 1 و 2)
        // - تُعدّل رصيد الطرف (Customer.CurrentBalance) بإلغاء أثر آخر ترحيل
        // ✅ مهم: لا تبدأ Transaction ولا تعمل SaveChanges هنا
        //        لأن DeleteConfirmed هو الذي يدير Transaction و SaveChanges مرة واحدة
        // =========================================================
        public async Task ReverseForHeaderDeleteAsync(
            LedgerSourceType sourceType,
            int sourceId,
            string? postedBy,
            string? reason = null)
        {
            // ================================
            // 1) تحديد آخر PostVersion (آخر مرحلة مُرحّلة)
            // ✅ تعديل مهم: كتابة الاستعلام بشكل قابل للترجمة في EF Core
            // - نتجنب DefaultIfEmpty(0).MaxAsync() لأنها أحياناً لا تُترجم
            // - نستخدم OrderByDescending + FirstOrDefaultAsync بدل Max
            // - نستخدم EF.Functions.Like بدل Contains لثبات أفضل في SQL
            // ================================
            int lastStage = await _db.LedgerEntries
                .Where(e =>
                    e.SourceType == sourceType &&
                    e.SourceId == sourceId &&
                    (e.LineNo == 1 || e.LineNo == 2) &&
                    e.PostVersion > 0 &&
                    e.Description != null &&
                    EF.Functions.Like(e.Description, "%ترحيل%"))
                .OrderByDescending(e => e.PostVersion)          // متغير: ترتيب من الأكبر للأصغر
                .Select(e => e.PostVersion)                     // متغير: قيمة نسخة الترحيل
                .FirstOrDefaultAsync();                         // متغير: 0 لو مفيش نتائج

            // تعليق: لو مفيش ترحيل سابق => مفيش حاجة تتعمل
            if (lastStage <= 0)
                return;


            // ================================
            // 2) جلب سطر المدين/الدائن للمرحلة الأخيرة
            // ================================
            var lastDebit = await _db.LedgerEntries
                .Where(e =>
                    e.SourceType == sourceType &&
                    e.SourceId == sourceId &&
                    e.LineNo == 1 &&
                    e.PostVersion == lastStage)
                .OrderByDescending(e => e.Id)
                .FirstOrDefaultAsync();

            var lastCredit = await _db.LedgerEntries
                .Where(e =>
                    e.SourceType == sourceType &&
                    e.SourceId == sourceId &&
                    e.LineNo == 2 &&
                    e.PostVersion == lastStage)
                .OrderByDescending(e => e.Id)
                .FirstOrDefaultAsync();

            if (lastDebit == null || lastCredit == null)
                throw new Exception("تعذر تحديد آخر مرحلة مُرحّلة (قيود غير مكتملة).");

            // متغير: قيمة المرحلة الأخيرة
            decimal amount = lastDebit.Debit > 0 ? lastDebit.Debit : lastCredit.Credit;

            // ================================
            // 3) تجهيز سبب العكس
            // ================================
            var now = DateTime.UtcNow;

            string finalReason = string.IsNullOrWhiteSpace(reason)
                ? $"عكس ترحيل بسبب حذف من قائمة الهيدر (SourceType={sourceType}, SourceId={sourceId}, Stage={lastStage})"
                : reason.Trim();

            // ================================
            // 4) إنشاء القيود العكسية (Reverse)
            // ================================
            var reverseDebit = new LedgerEntry
            {
                EntryDate = lastDebit.EntryDate,
                SourceType = sourceType,
                VoucherNo = lastDebit.VoucherNo,
                SourceId = sourceId,

                LineNo = 9001,
                PostVersion = lastStage,

                AccountId = lastDebit.AccountId,
                CustomerId = lastDebit.CustomerId,

                Debit = 0m,
                Credit = amount,

                Description = $"{finalReason} | (عكس مرحلة {lastStage})",
                CreatedAt = now
            };

            var reverseCredit = new LedgerEntry
            {
                EntryDate = lastCredit.EntryDate,
                SourceType = sourceType,
                VoucherNo = lastCredit.VoucherNo,
                SourceId = sourceId,

                LineNo = 9002,
                PostVersion = lastStage,

                AccountId = lastCredit.AccountId,
                CustomerId = lastCredit.CustomerId,

                Debit = amount,
                Credit = 0m,

                Description = $"{finalReason} | (عكس مرحلة {lastStage})",
                CreatedAt = now
            };

            _db.LedgerEntries.Add(reverseDebit);
            _db.LedgerEntries.Add(reverseCredit);

            // ================================
            // 5) إلغاء أثر الرصيد للطرف (Customer.CurrentBalance)
            // ================================
            var balanceDeltas = new Dictionary<int, decimal>();

            if (lastDebit.CustomerId.HasValue && lastDebit.CustomerId.Value > 0 && lastDebit.Debit > 0)
            {
                int cid = lastDebit.CustomerId.Value;
                balanceDeltas[cid] = balanceDeltas.ContainsKey(cid)
                    ? balanceDeltas[cid] + lastDebit.Debit
                    : lastDebit.Debit;
            }

            if (lastCredit.CustomerId.HasValue && lastCredit.CustomerId.Value > 0 && lastCredit.Credit > 0)
            {
                int cid = lastCredit.CustomerId.Value;
                balanceDeltas[cid] = balanceDeltas.ContainsKey(cid)
                    ? balanceDeltas[cid] + lastCredit.Credit
                    : lastCredit.Credit;
            }

            foreach (var kv in balanceDeltas)
            {
                int customerId = kv.Key;
                decimal delta = kv.Value;

                var party = await _db.Customers.FirstOrDefaultAsync(c => c.CustomerId == customerId);
                if (party != null)
                    party.CurrentBalance -= delta;
            }

            // ================================
            // 6) إعادة حساب رصيد العميل/المورد من القيود (مصدر الحقيقة)
            // ✅ مهم: نستخدم RecalcCustomerCurrentBalanceAsync لضمان الدقة
            // ================================
            foreach (var kv in balanceDeltas)
            {
                int customerId = kv.Key;
                await RecalcCustomerCurrentBalanceAsync(customerId);
            }

            // ❌ لا SaveChanges هنا
            // ✅ SaveChanges + Commit يتموا مرة واحدة في DeleteConfirmed داخل نفس Transaction
        }






        // ================================
        // ثوابت أكواد الحسابات (حسب شجرة الحسابات عندك)
        // ================================
        private const string SalesRevenueAccountCode = "4100"; // متغير: كود حساب إيرادات المبيعات
        private const string InventoryAccountCode = "1105"; // متغير: كود حساب المخزون
        private const string CogsAccountCode = "5100"; // متغير: كود حساب تكلفة البضاعة المباعة
        private const string TransferProfitAccountCode = "4200"; // إيرادات أخرى / أرباح التحويلات الداخلية

        // ================================
        // ثابت: SourceType المستخدم في StockLedger للمبيعات
        // (لازم يطابق اللي بتسجله في حركات المخزون عند البيع)
        // ================================
        private const string StockSourceType_Sales = "Sales"; // متغير: قيمة SourceType للمبيعات في StockLedger


        // =========================================================
        // ترحيل فاتورة مبيعات إلى LedgerEntries + تحديث رصيد العميل
        // ✅ نفس منطق المشتريات:
        // - لو فيه مرحلة قديمة => يعمل عكس (Reverse) للمرحلة الأخيرة
        // - ثم ينشئ مرحلة جديدة
        // - ويحدّث Customer.CurrentBalance
        //
        // ✅ قيد المبيعات الصحيح (آجل):
        // (1) العميل    مدين   = صافي الفاتورة (NetTotal)
        // (2) إيرادات   دائن   = صافي الفاتورة
        //
        // ✅ قيد تكلفة المبيعات (من StockLedger):
        // (3) COGS      مدين   = إجمالي تكلفة الخروج (costTotal)
        // (4) مخزون     دائن   = إجمالي تكلفة الخروج
        // =========================================================
        public Task PostSalesInvoiceAsync(int salesInvoiceId, string? postedBy)
            => ExecuteWithLedgerPerfAsync(nameof(PostSalesInvoiceAsync), salesInvoiceId, () => PostSalesInvoiceCoreAsync(salesInvoiceId, postedBy));

        private async Task PostSalesInvoiceCoreAsync(int salesInvoiceId, string? postedBy)
        {
            // ================================
            // 0) Transaction لضمان سلامة العملية
            // ================================
            await using var tx = await _db.Database.BeginTransactionAsync();

            try
            {
                // ================================
                // 1) تحميل الفاتورة + العميل
                // ================================
                var invoice = await _db.SalesInvoices
                    .Include(x => x.Customer) // تعليق: نحتاج العميل + AccountId (للتأكد)
                    .FirstOrDefaultAsync(x => x.SIId == salesInvoiceId);

                if (invoice == null)
                    throw new Exception("الفاتورة غير موجودة.");

                // ================================
                // 2) منع الترحيل لو مترحّلة بالفعل
                // ================================
                // تعليق: إعادة الترحيل تكون بعد "فتح الفاتورة" (Open) وإرجاع IsPosted = false
                if (invoice.IsPosted)
                    throw new Exception("هذه الفاتورة مترحّلة بالفعل. افتح الفاتورة أولاً قبل إعادة الترحيل.");

                // ================================
                // 3) تأكد أن العميل مربوط بحساب محاسبي
                // ================================
                if (invoice.Customer == null)
                    throw new Exception("بيانات العميل غير محمّلة.");

                if (invoice.Customer.AccountId == null || invoice.Customer.AccountId <= 0)
                    throw new Exception("هذا العميل ليس مرتبطًا بحساب محاسبي داخل شجرة الحسابات.");

                // ================================
                // 4) حل AccountId من الأكواد
                // ================================
                int salesRevenueAccountId = await ResolveAccountIdByCodeAsync(SalesRevenueAccountCode); // متغير: حساب الإيرادات
                int inventoryAccountId = await ResolveAccountIdByCodeAsync(InventoryAccountCode);   // متغير: حساب المخزون
                int cogsAccountId = await ResolveAccountIdByCodeAsync(CogsAccountCode);        // متغير: حساب تكلفة المبيعات

                // ================================
                // 5) قيم مشتركة
                // ================================
                var now = DateTime.UtcNow;                 // متغير: وقت التنفيذ الحالي
                decimal newAmount = invoice.NetTotal;      // متغير: صافي الفاتورة
                string voucherNo = invoice.SIId.ToString();// متغير: رقم المستند

                // ================================
                // 6) حساب تكلفة البضاعة المباعة من StockLedger
                // ================================
                decimal costTotal = await GetSalesInvoiceCostTotalAsync(invoice.SIId); // متغير: إجمالي التكلفة (COGS)

                // =========================================================
                // 7) تحديد آخر مرحلة مُرحّلة سابقاً (PostVersion)
                // - نعتمد على سطور المرحلة الأساسية 1..4 فقط
                // =========================================================
                int lastStage = await _db.LedgerEntries
                    .Where(e =>
                        e.SourceType == LedgerSourceType.SalesInvoice &&
                        e.SourceId == invoice.SIId &&
                        (e.LineNo == 1 || e.LineNo == 2 || e.LineNo == 3 || e.LineNo == 4) &&
                        e.PostVersion > 0)
                    .OrderByDescending(e => e.PostVersion)
                    .Select(e => e.PostVersion)
                    .FirstOrDefaultAsync(); // 0 لو مفيش

                int newStage = lastStage + 1; // متغير: المرحلة الجديدة

                // =========================================================
                // 8) لو فيه مرحلة سابقة => اعكسها قبل إنشاء المرحلة الجديدة
                // =========================================================
                if (lastStage > 0)
                {
                    // --------------------------
                    // (A) قيود البيع (العميل / الإيراد)
                    // --------------------------
                    var lastDebitCustomer = await _db.LedgerEntries
                        .Where(e =>
                            e.SourceType == LedgerSourceType.SalesInvoice &&
                            e.SourceId == invoice.SIId &&
                            e.LineNo == 1 &&
                            e.PostVersion == lastStage)
                        .OrderByDescending(e => e.Id)
                        .FirstOrDefaultAsync();

                    var lastCreditRevenue = await _db.LedgerEntries
                        .Where(e =>
                            e.SourceType == LedgerSourceType.SalesInvoice &&
                            e.SourceId == invoice.SIId &&
                            e.LineNo == 2 &&
                            e.PostVersion == lastStage)
                        .OrderByDescending(e => e.Id)
                        .FirstOrDefaultAsync();

                    if (lastDebitCustomer == null || lastCreditRevenue == null)
                        throw new Exception("تعذر تحديد آخر ترحيل سابق للفاتورة (قيود البيع غير مكتملة).");

                    // متغير: صافي البيع في المرحلة السابقة
                    decimal oldSalesAmount = lastDebitCustomer.Debit > 0
                        ? lastDebitCustomer.Debit
                        : lastCreditRevenue.Credit;

                    // (1) عكس سطر العميل (كان مدين -> يصبح دائن)
                    _db.LedgerEntries.Add(new LedgerEntry
                    {
                        EntryDate = invoice.SIDate,
                        SourceType = LedgerSourceType.SalesInvoice,
                        VoucherNo = voucherNo,
                        SourceId = invoice.SIId,

                        LineNo = 9001,
                        PostVersion = lastStage,

                        AccountId = lastDebitCustomer.AccountId,
                        CustomerId = lastDebitCustomer.CustomerId,

                        Debit = 0m,
                        Credit = oldSalesAmount,

                        Description = $"عكس ترحيل فاتورة مبيعات رقم {invoice.SIId} (عكس مرحلة {lastStage})",
                        CreatedAt = now
                    });

                    // (2) عكس سطر الإيراد (كان دائن -> يصبح مدين)
                    _db.LedgerEntries.Add(new LedgerEntry
                    {
                        EntryDate = invoice.SIDate,
                        SourceType = LedgerSourceType.SalesInvoice,
                        VoucherNo = voucherNo,
                        SourceId = invoice.SIId,

                        LineNo = 9002,
                        PostVersion = lastStage,

                        AccountId = lastCreditRevenue.AccountId,
                        CustomerId = null,

                        Debit = oldSalesAmount,
                        Credit = 0m,

                        Description = $"عكس ترحيل فاتورة مبيعات رقم {invoice.SIId} (عكس مرحلة {lastStage})",
                        CreatedAt = now
                    });

                    // ================================
                    // خصم رصيد العميل القديم (المذكور في سطر المدين للمرحلة السابقة)
                    // ================================
                    if (lastDebitCustomer.CustomerId.HasValue && lastDebitCustomer.CustomerId.Value > 0)
                    {
                        var oldCustomer = await _db.Customers
                            .FirstOrDefaultAsync(c => c.CustomerId == lastDebitCustomer.CustomerId.Value);

                        if (oldCustomer != null)
                            oldCustomer.CurrentBalance -= oldSalesAmount;
                    }
                    else
                    {
                        // حل احتياطي: لو CustomerId مش موجود في قيد المدين لأي سبب
                        invoice.Customer.CurrentBalance -= oldSalesAmount;
                    }

                    // --------------------------
                    // (B) قيود التكلفة (COGS / Inventory) — لو موجودة
                    // --------------------------
                    var lastDebitCogs = await _db.LedgerEntries
                        .Where(e =>
                            e.SourceType == LedgerSourceType.SalesInvoice &&
                            e.SourceId == invoice.SIId &&
                            e.LineNo == 3 &&
                            e.PostVersion == lastStage)
                        .OrderByDescending(e => e.Id)
                        .FirstOrDefaultAsync();

                    var lastCreditInv = await _db.LedgerEntries
                        .Where(e =>
                            e.SourceType == LedgerSourceType.SalesInvoice &&
                            e.SourceId == invoice.SIId &&
                            e.LineNo == 4 &&
                            e.PostVersion == lastStage)
                        .OrderByDescending(e => e.Id)
                        .FirstOrDefaultAsync();

                    // تعليق: ممكن تكون مش موجودة لو كنت بتجرب قبل ما نضيف التكلفة
                    if (lastDebitCogs != null && lastCreditInv != null)
                    {
                        decimal oldCostAmount = lastDebitCogs.Debit > 0
                            ? lastDebitCogs.Debit
                            : lastCreditInv.Credit;

                        // (3) عكس COGS (كان مدين -> يصبح دائن)
                        _db.LedgerEntries.Add(new LedgerEntry
                        {
                            EntryDate = invoice.SIDate,
                            SourceType = LedgerSourceType.SalesInvoice,
                            VoucherNo = voucherNo,
                            SourceId = invoice.SIId,

                            LineNo = 9003,
                            PostVersion = lastStage,

                            AccountId = lastDebitCogs.AccountId,
                            CustomerId = null,

                            Debit = 0m,
                            Credit = oldCostAmount,

                            Description = $"عكس تكلفة فاتورة مبيعات رقم {invoice.SIId} (عكس مرحلة {lastStage})",
                            CreatedAt = now
                        });

                        // (4) عكس المخزون (كان دائن -> يصبح مدين)
                        _db.LedgerEntries.Add(new LedgerEntry
                        {
                            EntryDate = invoice.SIDate,
                            SourceType = LedgerSourceType.SalesInvoice,
                            VoucherNo = voucherNo,
                            SourceId = invoice.SIId,

                            LineNo = 9004,
                            PostVersion = lastStage,

                            AccountId = lastCreditInv.AccountId,
                            CustomerId = null,

                            Debit = oldCostAmount,
                            Credit = 0m,

                            Description = $"عكس تكلفة فاتورة مبيعات رقم {invoice.SIId} (عكس مرحلة {lastStage})",
                            CreatedAt = now
                        });
                    }
                }

                // =========================================================
                // 9) إنشاء قيود المرحلة الجديدة
                // =========================================================
                int customerAccountId = invoice.Customer.AccountId.Value; // متغير: حساب العميل

                // (1) مدين: العميل
                _db.LedgerEntries.Add(new LedgerEntry
                {
                    EntryDate = invoice.SIDate,
                    SourceType = LedgerSourceType.SalesInvoice,
                    VoucherNo = voucherNo,
                    SourceId = invoice.SIId,

                    LineNo = 1,
                    PostVersion = newStage,

                    AccountId = customerAccountId,
                    CustomerId = invoice.CustomerId,

                    Debit = newAmount,
                    Credit = 0m,

                    Description = $"ترحيل فاتورة مبيعات رقم {invoice.SIId} (مرحلة {newStage})",
                    CreatedAt = now
                });

                // (2) دائن: الإيراد
                _db.LedgerEntries.Add(new LedgerEntry
                {
                    EntryDate = invoice.SIDate,
                    SourceType = LedgerSourceType.SalesInvoice,
                    VoucherNo = voucherNo,
                    SourceId = invoice.SIId,

                    LineNo = 2,
                    PostVersion = newStage,

                    AccountId = salesRevenueAccountId,
                    CustomerId = null,

                    Debit = 0m,
                    Credit = newAmount,

                    Description = $"ترحيل فاتورة مبيعات رقم {invoice.SIId} (مرحلة {newStage})",
                    CreatedAt = now
                });

                // (3) مدين: COGS + (4) دائن: Inventory لو التكلفة > 0
                if (costTotal > 0m)
                {
                    _db.LedgerEntries.Add(new LedgerEntry
                    {
                        EntryDate = invoice.SIDate,
                        SourceType = LedgerSourceType.SalesInvoice,
                        VoucherNo = voucherNo,
                        SourceId = invoice.SIId,

                        LineNo = 3,
                        PostVersion = newStage,

                        AccountId = cogsAccountId,
                        CustomerId = null,

                        Debit = costTotal,
                        Credit = 0m,

                        Description = $"إثبات تكلفة مبيعات فاتورة رقم {invoice.SIId} (مرحلة {newStage})",
                        CreatedAt = now
                    });

                    _db.LedgerEntries.Add(new LedgerEntry
                    {
                        EntryDate = invoice.SIDate,
                        SourceType = LedgerSourceType.SalesInvoice,
                        VoucherNo = voucherNo,
                        SourceId = invoice.SIId,

                        LineNo = 4,
                        PostVersion = newStage,

                        AccountId = inventoryAccountId,
                        CustomerId = null,

                        Debit = 0m,
                        Credit = costTotal,

                        Description = $"خصم مخزون مقابل مبيعات فاتورة رقم {invoice.SIId} (مرحلة {newStage})",
                        CreatedAt = now
                    });
                }

                // =========================================================
                // 10) تحديث حالة الفاتورة
                // =========================================================
                invoice.IsPosted = true;
                invoice.Status = $"مرحلة {newStage}";
                invoice.PostedAt = now;
                invoice.PostedBy = string.IsNullOrWhiteSpace(postedBy) ? "SYSTEM" : postedBy;

                // =========================================================
                // 11) حفظ القيود + حالة الفاتورة أولاً
                // (مهم جدًا) لأن Recalc سيقرأ من DB ولا يرى الإضافات قبل SaveChanges
                // =========================================================
                await _db.SaveChangesAsync();

                // =========================================================
                // 12) تحديث رصيد العميل (مصدر الحقيقة = LedgerEntries)
                // =========================================================
                if (invoice.CustomerId > 0)
                {
                    await RecalcCustomerCurrentBalanceAsync(invoice.CustomerId);
                    await _db.SaveChangesAsync(); // تعليق: حفظ تحديث الرصيد فقط
                }

                // =========================================================
                // 13) Commit
                // =========================================================
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }












        // =========================================================
        // دالة: حساب إجمالي تكلفة فاتورة المبيعات من StockLedger (FIFO/FEFO)
        // - نجمع تكلفة كل حركات الخروج المرتبطة بالفاتورة
        // - مصدر التكلفة الحقيقي: TotalCost أو (QtyOut * UnitCost)
        // =========================================================
        private async Task<decimal> GetSalesInvoiceCostTotalAsync(int siId)
        {
            // =========================
            // 0) تحقق من المدخلات
            // =========================
            if (siId <= 0) return 0m;

            // =========================
            // 1) جمع تكلفة الخروج من StockLedger
            // =========================
            decimal costTotal = await _db.StockLedger
                .Where(x =>
                    x.SourceType == StockSourceType_Sales && // تعليق: لازم تساوي "Sales" أو القيمة الفعلية عندك
                    x.SourceId == siId &&                    // تعليق: ربط الحركة برقم الفاتورة
                    x.QtyOut > 0)                            // تعليق: نتأكد أنها حركة خروج
                .SumAsync(x => x.TotalCost ?? (x.QtyOut * x.UnitCost));

            return costTotal;
        }




        // =========================================================
        // دالة: إعادة حساب رصيد العميل/المورد من القيود LedgerEntries
        // الرصيد = مجموع المدين - مجموع الدائن
        // ✅ مهم: نستخدم CustomerId وليس AccountId لأن:
        // - AccountId قد يشمل قيود عملاء آخرين على نفس الحساب
        // - CustomerId يربط القيود بالعميل المحدد مباشرة
        // =========================================================
        private async Task RecalcCustomerCurrentBalanceAsync(int customerId)
        {
            // تعليق: تحميل العميل
            var customer = await _db.Customers
                .FirstOrDefaultAsync(c => c.CustomerId == customerId);

            if (customer == null)
                return;

            // ✅ حساب الصافي من LedgerEntries باستخدام CustomerId (مصدر الحقيقة)
            // - نعتمد على CustomerId فقط لتفادي اختلاف AccountId
            // - هذا يضمن أننا نحسب فقط القيود المرتبطة بهذا العميل المحدد
            decimal balance = await _db.LedgerEntries
                .AsNoTracking()
                .Where(e => e.CustomerId == customerId)
                .SumAsync(e => (decimal?)(e.Debit - e.Credit)) ?? 0m;

            // تعليق: تحديث الرصيد داخل جدول العملاء
            customer.CurrentBalance = balance;
            customer.UpdatedAt = DateTime.UtcNow;
        }




        // =========================================================
        // ترحيل إذن استلام نقدية إلى LedgerEntries + تحديث رصيد العميل
        // ✅ نفس منطق الفواتير:
        // - لو فيه مرحلة قديمة => يعمل عكس (Reverse) للمرحلة الأخيرة
        // - ثم ينشئ مرحلة جديدة
        // - ويحدّث Customer.CurrentBalance
        //
        // ✅ قيد إذن الاستلام الصحيح:
        // (1) الصندوق/البنك    مدين   = المبلغ
        // (2) حساب العميل      دائن   = المبلغ
        // =========================================================
        public async Task PostCashReceiptAsync(int cashReceiptId, string? postedBy)
        {
            // ================================
            // 0) Transaction لضمان سلامة العملية
            // ================================
            await using var tx = await _db.Database.BeginTransactionAsync();

            try
            {
                // ================================
                // 1) تحميل الإذن + العميل
                // ================================
                var receipt = await _db.CashReceipts
                    .Include(r => r.Customer)
                    .FirstOrDefaultAsync(r => r.CashReceiptId == cashReceiptId);

                if (receipt == null)
                    throw new Exception("إذن الاستلام غير موجود.");

                // ================================
                // 2) منع الترحيل لو مترحّل بالفعل
                // ================================
                if (receipt.IsPosted)
                    throw new Exception("هذا الإذن مترحّل بالفعل. افتح الإذن أولاً قبل إعادة الترحيل.");

                // ================================
                // 3) التحقق من الحسابات المطلوبة
                // ================================
                if (receipt.CashAccountId <= 0)
                    throw new Exception("حساب الصندوق/البنك غير محدد.");

                if (receipt.CounterAccountId <= 0)
                    throw new Exception("حساب الطرف غير محدد.");

                // ✅ إذا كان هناك عميل، نتحقق من أن حسابه موجود ومتطابق مع CounterAccountId
                if (receipt.CustomerId.HasValue && receipt.CustomerId.Value > 0)
                {
                    if (receipt.Customer == null)
                        throw new Exception("بيانات العميل غير محمّلة.");

                    // ملاحظة: CounterAccountId هو الحساب الفعلي المستخدم في الترحيل
                    // يمكن التحقق من تطابقه مع حساب العميل، لكن لا نجعله إلزامياً
                    // لأنه قد يكون حساب مختلف للعميل
                }

                // ================================
                // 4) قيم مشتركة
                // ================================
                var now = DateTime.UtcNow;
                decimal amount = receipt.Amount;
                
                // ✅ التأكد من أن ReceiptNumber موجود (يجب أن يكون قد تم توليده)
                if (string.IsNullOrWhiteSpace(receipt.ReceiptNumber))
                {
                    // إذا لم يكن موجوداً، نستخدم CashReceiptId كرقم مستند
                    receipt.ReceiptNumber = receipt.CashReceiptId.ToString();
                    await _db.SaveChangesAsync();
                }
                
                string voucherNo = receipt.ReceiptNumber;

                // =========================================================
                // 5) تحديد آخر مرحلة مُرحّلة سابقاً
                // =========================================================
                int lastStage = await _db.LedgerEntries
                    .Where(e =>
                        e.SourceType == LedgerSourceType.Receipt &&
                        e.SourceId == receipt.CashReceiptId &&
                        (e.LineNo == 1 || e.LineNo == 2) &&
                        e.PostVersion > 0)
                    .OrderByDescending(e => e.PostVersion)
                    .Select(e => e.PostVersion)
                    .FirstOrDefaultAsync();

                int newStage = lastStage + 1;

                // =========================================================
                // 6) لو فيه مرحلة سابقة => اعكسها قبل إنشاء المرحلة الجديدة
                // =========================================================
                if (lastStage > 0)
                {
                    var lastDebitCash = await _db.LedgerEntries
                        .Where(e =>
                            e.SourceType == LedgerSourceType.Receipt &&
                            e.SourceId == receipt.CashReceiptId &&
                            e.LineNo == 1 &&
                            e.PostVersion == lastStage)
                        .OrderByDescending(e => e.Id)
                        .FirstOrDefaultAsync();

                    var lastCreditCustomer = await _db.LedgerEntries
                        .Where(e =>
                            e.SourceType == LedgerSourceType.Receipt &&
                            e.SourceId == receipt.CashReceiptId &&
                            e.LineNo == 2 &&
                            e.PostVersion == lastStage)
                        .OrderByDescending(e => e.Id)
                        .FirstOrDefaultAsync();

                    if (lastDebitCash == null || lastCreditCustomer == null)
                        throw new Exception("تعذر تحديد آخر ترحيل سابق للإذن (بيانات القيود غير مكتملة).");

                    decimal oldAmount = lastDebitCash.Debit > 0 ? lastDebitCash.Debit : lastCreditCustomer.Credit;

                    // (1) عكس سطر الصندوق (كان مدين -> يصبح دائن)
                    _db.LedgerEntries.Add(new LedgerEntry
                    {
                        EntryDate = receipt.ReceiptDate,
                        SourceType = LedgerSourceType.Receipt,
                        VoucherNo = voucherNo,
                        SourceId = receipt.CashReceiptId,
                        LineNo = 9001,
                        PostVersion = lastStage,
                        AccountId = lastDebitCash.AccountId,
                        CustomerId = null,
                        Debit = 0m,
                        Credit = oldAmount,
                        Description = $"عكس ترحيل إذن استلام رقم {receipt.CashReceiptId} (عكس مرحلة {lastStage})",
                        CreatedAt = now
                    });

                    // (2) عكس سطر العميل (كان دائن -> يصبح مدين)
                    _db.LedgerEntries.Add(new LedgerEntry
                    {
                        EntryDate = receipt.ReceiptDate,
                        SourceType = LedgerSourceType.Receipt,
                        VoucherNo = voucherNo,
                        SourceId = receipt.CashReceiptId,
                        LineNo = 9002,
                        PostVersion = lastStage,
                        AccountId = lastCreditCustomer.AccountId,
                        CustomerId = lastCreditCustomer.CustomerId,
                        Debit = oldAmount,
                        Credit = 0m,
                        Description = $"عكس ترحيل إذن استلام رقم {receipt.CashReceiptId} (عكس مرحلة {lastStage})",
                        CreatedAt = now
                    });

                    // خصم رصيد العميل القديم
                    if (lastCreditCustomer.CustomerId.HasValue && lastCreditCustomer.CustomerId.Value > 0)
                    {
                        var oldCustomer = await _db.Customers
                            .FirstOrDefaultAsync(c => c.CustomerId == lastCreditCustomer.CustomerId.Value);

                        if (oldCustomer != null)
                            oldCustomer.CurrentBalance -= oldAmount;
                    }
                    else if (receipt.CustomerId.HasValue)
                    {
                        receipt.Customer.CurrentBalance -= oldAmount;
                    }
                }

                // =========================================================
                // 7) إنشاء قيود المرحلة الجديدة
                // =========================================================
                int cashAccountId = receipt.CashAccountId;
                int customerAccountId = receipt.CounterAccountId; // حساب العميل

                // (1) مدين: الصندوق/البنك
                _db.LedgerEntries.Add(new LedgerEntry
                {
                    EntryDate = receipt.ReceiptDate,
                    SourceType = LedgerSourceType.Receipt,
                    VoucherNo = voucherNo,
                    SourceId = receipt.CashReceiptId,
                    LineNo = 1,
                    PostVersion = newStage,
                    AccountId = cashAccountId,
                    CustomerId = null,
                    Debit = amount,
                    Credit = 0m,
                    Description = $"ترحيل إذن استلام رقم {receipt.CashReceiptId} (مرحلة {newStage})",
                    CreatedAt = now
                });

                // (2) دائن: حساب العميل
                _db.LedgerEntries.Add(new LedgerEntry
                {
                    EntryDate = receipt.ReceiptDate,
                    SourceType = LedgerSourceType.Receipt,
                    VoucherNo = voucherNo,
                    SourceId = receipt.CashReceiptId,
                    LineNo = 2,
                    PostVersion = newStage,
                    AccountId = customerAccountId,
                    CustomerId = receipt.CustomerId,
                    Debit = 0m,
                    Credit = amount,
                    Description = $"ترحيل إذن استلام رقم {receipt.CashReceiptId} (مرحلة {newStage})",
                    CreatedAt = now
                });

                // =========================================================
                // 8) تحديث حالة الإذن
                // =========================================================
                receipt.IsPosted = true;
                receipt.Status = $"مرحلة {newStage}";
                receipt.PostedAt = now;
                receipt.PostedBy = string.IsNullOrWhiteSpace(postedBy) ? "SYSTEM" : postedBy;

                // =========================================================
                // 9) حفظ القيود + حالة الإذن أولاً
                // =========================================================
                await _db.SaveChangesAsync();

                // =========================================================
                // 10) تحديث رصيد العميل (مصدر الحقيقة = LedgerEntries)
                // =========================================================
                if (receipt.CustomerId.HasValue && receipt.CustomerId.Value > 0)
                {
                    await RecalcCustomerCurrentBalanceAsync(receipt.CustomerId.Value);
                    await _db.SaveChangesAsync();
                }

                // =========================================================
                // 11) Commit
                // =========================================================
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // =========================================================
        // PostCashPaymentAsync — ترحيل إذن دفع نقدية
        // =========================================================
        // ✅ قيد إذن الدفع الصحيح (عكس إذن الاستلام):
        // (1) حساب العميل      مدين   = المبلغ (نزيد رصيد العميل)
        // (2) الصندوق/البنك    دائن   = المبلغ (نصرف من الصندوق)
        // =========================================================
        public async Task PostCashPaymentAsync(int cashPaymentId, string? postedBy)
        {
            // ================================
            // 0) Transaction لضمان سلامة العملية
            // ================================
            await using var tx = await _db.Database.BeginTransactionAsync();

            try
            {
                // ================================
                // 1) تحميل الإذن + العميل
                // ================================
                var payment = await _db.CashPayments
                    .Include(p => p.Customer)
                    .FirstOrDefaultAsync(p => p.CashPaymentId == cashPaymentId);

                if (payment == null)
                    throw new Exception("إذن الدفع غير موجود.");

                // ================================
                // 2) منع الترحيل لو مترحّل بالفعل
                // ================================
                if (payment.IsPosted)
                    throw new Exception("هذا الإذن مترحّل بالفعل. افتح الإذن أولاً قبل إعادة الترحيل.");

                // ================================
                // 3) التحقق من الحسابات المطلوبة
                // ================================
                if (payment.CashAccountId <= 0)
                    throw new Exception("حساب الصندوق/البنك غير محدد.");

                if (payment.CounterAccountId <= 0)
                    throw new Exception("حساب الطرف غير محدد.");

                // ================================
                // 4) قيم مشتركة
                // ================================
                var now = DateTime.UtcNow;
                decimal amount = payment.Amount;
                
                // ✅ التأكد من أن PaymentNumber موجود
                if (string.IsNullOrWhiteSpace(payment.PaymentNumber))
                {
                    payment.PaymentNumber = payment.CashPaymentId.ToString();
                    await _db.SaveChangesAsync();
                }
                
                string voucherNo = payment.PaymentNumber;

                // =========================================================
                // 5) تحديد آخر مرحلة مُرحّلة سابقاً
                // =========================================================
                int lastStage = await _db.LedgerEntries
                    .Where(e =>
                        e.SourceType == LedgerSourceType.Payment &&
                        e.SourceId == payment.CashPaymentId &&
                        (e.LineNo == 1 || e.LineNo == 2) &&
                        e.PostVersion > 0)
                    .OrderByDescending(e => e.PostVersion)
                    .Select(e => e.PostVersion)
                    .FirstOrDefaultAsync();

                int newStage = lastStage + 1;

                // =========================================================
                // 6) لو فيه مرحلة سابقة => اعكسها قبل إنشاء المرحلة الجديدة
                // =========================================================
                if (lastStage > 0)
                {
                    var lastDebitCustomer = await _db.LedgerEntries
                        .Where(e =>
                            e.SourceType == LedgerSourceType.Payment &&
                            e.SourceId == payment.CashPaymentId &&
                            e.LineNo == 1 &&
                            e.PostVersion == lastStage)
                        .OrderByDescending(e => e.Id)
                        .FirstOrDefaultAsync();

                    var lastCreditCash = await _db.LedgerEntries
                        .Where(e =>
                            e.SourceType == LedgerSourceType.Payment &&
                            e.SourceId == payment.CashPaymentId &&
                            e.LineNo == 2 &&
                            e.PostVersion == lastStage)
                        .OrderByDescending(e => e.Id)
                        .FirstOrDefaultAsync();

                    if (lastDebitCustomer == null || lastCreditCash == null)
                        throw new Exception("تعذر تحديد آخر ترحيل سابق للإذن (بيانات القيود غير مكتملة).");

                    decimal oldAmount = lastDebitCustomer.Debit > 0 ? lastDebitCustomer.Debit : lastCreditCash.Credit;

                    // (1) عكس سطر العميل (كان مدين -> يصبح دائن)
                    _db.LedgerEntries.Add(new LedgerEntry
                    {
                        EntryDate = payment.PaymentDate,
                        SourceType = LedgerSourceType.Payment,
                        VoucherNo = voucherNo,
                        SourceId = payment.CashPaymentId,
                        LineNo = 9001,
                        PostVersion = lastStage,
                        AccountId = lastDebitCustomer.AccountId,
                        CustomerId = lastDebitCustomer.CustomerId,
                        Debit = 0m,
                        Credit = oldAmount,
                        Description = $"عكس ترحيل إذن دفع رقم {payment.CashPaymentId} (عكس مرحلة {lastStage})",
                        CreatedAt = now
                    });

                    // (2) عكس سطر الصندوق (كان دائن -> يصبح مدين)
                    _db.LedgerEntries.Add(new LedgerEntry
                    {
                        EntryDate = payment.PaymentDate,
                        SourceType = LedgerSourceType.Payment,
                        VoucherNo = voucherNo,
                        SourceId = payment.CashPaymentId,
                        LineNo = 9002,
                        PostVersion = lastStage,
                        AccountId = lastCreditCash.AccountId,
                        CustomerId = null,
                        Debit = oldAmount,
                        Credit = 0m,
                        Description = $"عكس ترحيل إذن دفع رقم {payment.CashPaymentId} (عكس مرحلة {lastStage})",
                        CreatedAt = now
                    });

                    // خصم رصيد العميل القديم
                    if (lastDebitCustomer.CustomerId.HasValue && lastDebitCustomer.CustomerId.Value > 0)
                    {
                        var oldCustomer = await _db.Customers
                            .FirstOrDefaultAsync(c => c.CustomerId == lastDebitCustomer.CustomerId.Value);

                        if (oldCustomer != null)
                            oldCustomer.CurrentBalance -= oldAmount;
                    }
                    else if (payment.CustomerId.HasValue)
                    {
                        payment.Customer.CurrentBalance -= oldAmount;
                    }
                }

                // =========================================================
                // 7) إنشاء قيود المرحلة الجديدة
                // =========================================================
                int customerAccountId = payment.CounterAccountId; // حساب العميل
                int cashAccountId = payment.CashAccountId;

                // (1) مدين: حساب العميل (نزيد رصيده)
                _db.LedgerEntries.Add(new LedgerEntry
                {
                    EntryDate = payment.PaymentDate,
                    SourceType = LedgerSourceType.Payment,
                    VoucherNo = voucherNo,
                    SourceId = payment.CashPaymentId,
                    LineNo = 1,
                    PostVersion = newStage,
                    AccountId = customerAccountId,
                    CustomerId = payment.CustomerId,
                    Debit = amount,
                    Credit = 0m,
                    Description = $"ترحيل إذن دفع رقم {payment.CashPaymentId} (مرحلة {newStage})",
                    CreatedAt = now
                });

                // (2) دائن: الصندوق/البنك (نصرف منه)
                _db.LedgerEntries.Add(new LedgerEntry
                {
                    EntryDate = payment.PaymentDate,
                    SourceType = LedgerSourceType.Payment,
                    VoucherNo = voucherNo,
                    SourceId = payment.CashPaymentId,
                    LineNo = 2,
                    PostVersion = newStage,
                    AccountId = cashAccountId,
                    CustomerId = null,
                    Debit = 0m,
                    Credit = amount,
                    Description = $"ترحيل إذن دفع رقم {payment.CashPaymentId} (مرحلة {newStage})",
                    CreatedAt = now
                });

                // =========================================================
                // 8) تحديث حالة الإذن
                // =========================================================
                payment.IsPosted = true;
                payment.PostedAt = now;
                payment.PostedBy = string.IsNullOrWhiteSpace(postedBy) ? "SYSTEM" : postedBy;

                // =========================================================
                // 9) حفظ القيود + حالة الإذن أولاً
                // =========================================================
                await _db.SaveChangesAsync();

                // =========================================================
                // 10) تحديث رصيد العميل (مصدر الحقيقة = LedgerEntries)
                // =========================================================
                if (payment.CustomerId.HasValue && payment.CustomerId.Value > 0)
                {
                    await RecalcCustomerCurrentBalanceAsync(payment.CustomerId.Value);
                    await _db.SaveChangesAsync();
                }

                // =========================================================
                // 11) Commit
                // =========================================================
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // =========================================================
        // ترحيل إشعار الخصم إلى LedgerEntries + تحديث رصيد العميل
        // قيد إشعار الخصم: مدين حساب العميل (نقلل ديون العميل)، دائن حساب مقابل (خصم مسموح/مصروف)
        // =========================================================
        public async Task PostDebitNoteAsync(int debitNoteId, string? postedBy)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var note = await _db.DebitNotes
                    .Include(d => d.Customer)
                    .FirstOrDefaultAsync(d => d.DebitNoteId == debitNoteId);
                if (note == null) throw new Exception("إشعار الخصم غير موجود.");
                if (note.IsPosted) throw new Exception("هذا الإشعار مترحّل بالفعل.");
                if (note.AccountId <= 0) throw new Exception("حساب الطرف غير محدد.");

                int offsetAccountId = note.OffsetAccountId ?? await ResolveAccountIdByCodeAsync("5200");
                if (offsetAccountId <= 0) throw new Exception("حساب مقابل غير محدد. يرجى اختيار حساب مقابل أو إضافة حساب بكود 5200 (خصم مسموح به).");

                var now = DateTime.UtcNow;
                decimal amount = note.Amount;
                string voucherNo = note.DebitNoteId.ToString();

                _db.LedgerEntries.Add(new LedgerEntry
                {
                    EntryDate = note.NoteDate,
                    SourceType = LedgerSourceType.DebitNote,
                    VoucherNo = voucherNo,
                    SourceId = debitNoteId,
                    LineNo = 1,
                    PostVersion = 1,
                    AccountId = note.AccountId,
                    CustomerId = note.CustomerId,
                    Debit = amount,
                    Credit = 0m,
                    Description = $"ترحيل إشعار خصم رقم {debitNoteId}",
                    CreatedAt = now
                });
                _db.LedgerEntries.Add(new LedgerEntry
                {
                    EntryDate = note.NoteDate,
                    SourceType = LedgerSourceType.DebitNote,
                    VoucherNo = voucherNo,
                    SourceId = debitNoteId,
                    LineNo = 2,
                    PostVersion = 1,
                    AccountId = offsetAccountId,
                    CustomerId = null,
                    Debit = 0m,
                    Credit = amount,
                    Description = $"ترحيل إشعار خصم رقم {debitNoteId}",
                    CreatedAt = now
                });

                note.IsPosted = true;
                note.PostedAt = now;
                note.PostedBy = string.IsNullOrWhiteSpace(postedBy) ? "SYSTEM" : postedBy;
                await _db.SaveChangesAsync();

                if (note.CustomerId.HasValue && note.CustomerId.Value > 0)
                {
                    await RecalcCustomerCurrentBalanceAsync(note.CustomerId.Value);
                    await _db.SaveChangesAsync();
                }
                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }
        }

        // =========================================================
        // ترحيل إشعار الإضافة إلى LedgerEntries + تحديث رصيد العميل
        // قيد إشعار الإضافة: مدين حساب مقابل (إيراد)، دائن حساب العميل (نزيد ما له)
        // =========================================================
        public async Task PostCreditNoteAsync(int creditNoteId, string? postedBy)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var note = await _db.CreditNotes
                    .Include(c => c.Customer)
                    .FirstOrDefaultAsync(c => c.CreditNoteId == creditNoteId);
                if (note == null) throw new Exception("إشعار الإضافة غير موجود.");
                if (note.IsPosted) throw new Exception("هذا الإشعار مترحّل بالفعل.");
                if (note.AccountId <= 0) throw new Exception("حساب الطرف غير محدد.");

                int offsetAccountId = note.OffsetAccountId ?? await ResolveAccountIdByCodeAsync("4100");
                if (offsetAccountId <= 0) throw new Exception("حساب مقابل غير محدد. يرجى اختيار حساب مقابل أو إضافة حساب بكود 4100 (إيرادات).");

                var now = DateTime.UtcNow;
                decimal amount = note.Amount;
                string voucherNo = note.CreditNoteId.ToString();

                _db.LedgerEntries.Add(new LedgerEntry
                {
                    EntryDate = note.NoteDate,
                    SourceType = LedgerSourceType.CreditNote,
                    VoucherNo = voucherNo,
                    SourceId = creditNoteId,
                    LineNo = 1,
                    PostVersion = 1,
                    AccountId = offsetAccountId,
                    CustomerId = null,
                    Debit = amount,
                    Credit = 0m,
                    Description = $"ترحيل إشعار إضافة رقم {creditNoteId}",
                    CreatedAt = now
                });
                _db.LedgerEntries.Add(new LedgerEntry
                {
                    EntryDate = note.NoteDate,
                    SourceType = LedgerSourceType.CreditNote,
                    VoucherNo = voucherNo,
                    SourceId = creditNoteId,
                    LineNo = 2,
                    PostVersion = 1,
                    AccountId = note.AccountId,
                    CustomerId = note.CustomerId,
                    Debit = 0m,
                    Credit = amount,
                    Description = $"ترحيل إشعار إضافة رقم {creditNoteId}",
                    CreatedAt = now
                });

                note.IsPosted = true;
                note.PostedAt = now;
                note.PostedBy = string.IsNullOrWhiteSpace(postedBy) ? "SYSTEM" : postedBy;
                await _db.SaveChangesAsync();

                if (note.CustomerId.HasValue && note.CustomerId.Value > 0)
                {
                    await RecalcCustomerCurrentBalanceAsync(note.CustomerId.Value);
                    await _db.SaveChangesAsync();
                }
                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }
        }

        // =========================================================
        // دالة: إعادة حساب جميع أرصدة العملاء/الموردين من القيود LedgerEntries
        // ✅ مهم: تستخدم CustomerId وليس AccountId لضمان الدقة
        // - تحسب الرصيد لكل عميل من قيوده في LedgerEntries
        // - تحدث CurrentBalance لكل عميل
        // =========================================================
        public Task RecalcAllCustomerBalancesAsync()
            => ExecuteWithLedgerPerfAsync(nameof(RecalcAllCustomerBalancesAsync), "all", RecalcAllCustomerBalancesCoreAsync);

        private async Task RecalcAllCustomerBalancesCoreAsync()
        {
            // ================================
            // 1) حساب الرصيد لكل عميل من LedgerEntries في استعلام واحد (أفضل أداء)
            // ================================
            var balances = await _db.LedgerEntries
                .AsNoTracking()
                .Where(e => e.CustomerId.HasValue && e.CustomerId.Value > 0)
                .GroupBy(e => e.CustomerId!.Value)
                .Select(g => new
                {
                    CustomerId = g.Key,
                    Balance = g.Sum(e => (decimal?)(e.Debit - e.Credit)) ?? 0m
                })
                .ToDictionaryAsync(x => x.CustomerId, x => x.Balance);

            // ================================
            // 2) جلب جميع العملاء/الموردين وتحديث أرصدتهم
            // ================================
            var customers = await _db.Customers.ToListAsync();

            if (!customers.Any())
                return;

            var now = DateTime.UtcNow;
            foreach (var customer in customers)
            {
                // ✅ إذا كان العميل له قيود في LedgerEntries، نستخدم الرصيد المحسوب
                // ✅ إذا لم يكن له قيود، نضع الرصيد = 0
                customer.CurrentBalance = balances.TryGetValue(customer.CustomerId, out decimal balance) 
                    ? balance 
                    : 0m;
                customer.UpdatedAt = now;
            }

            // ================================
            // 3) حفظ التغييرات
            // ================================
            await _db.SaveChangesAsync();
        }

        // =========================================================
        // ترحيل مرتجع البيع إلى LedgerEntries + StockLedger + StockBatch + تحديث رصيد العميل
        // ✅ مرتجع البيع يزيد المخزون (QtyIn في StockLedger)
        // ✅ قيد المرتجع:
        // (1) إيرادات   مدين   = صافي المرتجع (عكس المبيعات)
        // (2) العميل    دائن   = صافي المرتجع (نقصان دين العميل)
        // (3) مخزون     مدين   = تكلفة المرتجع (عكس COGS)
        // (4) COGS      دائن   = تكلفة المرتجع (عكس تكلفة المبيعات)
        // =========================================================
        public async Task PostSalesReturnAsync(int salesReturnId, string? postedBy)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();

            try
            {
                // 1) تحميل المرتجع + العميل + السطور
                var salesReturn = await _db.SalesReturns
                    .Include(sr => sr.Customer)
                    .Include(sr => sr.Lines)
                    .FirstOrDefaultAsync(sr => sr.SRId == salesReturnId);

                if (salesReturn == null)
                    throw new Exception("مرتجع البيع غير موجود.");

                if (salesReturn.IsPosted)
                    throw new Exception("هذا المرتجع مترحّل بالفعل.");

                if (salesReturn.Customer == null || salesReturn.Customer.AccountId == null || salesReturn.Customer.AccountId <= 0)
                    throw new Exception("العميل غير مرتبط بحساب محاسبي.");

                // 2) حل AccountId من الأكواد
                int salesRevenueAccountId = await ResolveAccountIdByCodeAsync(SalesRevenueAccountCode);
                int inventoryAccountId = await ResolveAccountIdByCodeAsync(InventoryAccountCode);
                int cogsAccountId = await ResolveAccountIdByCodeAsync(CogsAccountCode);

                var now = DateTime.UtcNow;
                decimal returnAmount = salesReturn.NetTotal;
                string voucherNo = salesReturn.SRId.ToString();

                // 3) حساب تكلفة المرتجع (من تكلفة البيع الأصلية أو متوسط التكلفة)
                decimal costTotal = 0m;
                foreach (var line in salesReturn.Lines)
                {
                    // استخدام تكلفة الوحدة من الفاتورة الأصلية أو متوسط التكلفة
                    var avgCost = await GetAverageCostAsync(line.ProdId, salesReturn.WarehouseId);
                    costTotal += line.Qty * avgCost;
                }

                // 4) تحديد آخر مرحلة
                int lastStage = await _db.LedgerEntries
                    .Where(e =>
                        e.SourceType == LedgerSourceType.SalesReturn &&
                        e.SourceId == salesReturnId &&
                        (e.LineNo == 1 || e.LineNo == 2 || e.LineNo == 3 || e.LineNo == 4) &&
                        e.PostVersion > 0)
                    .OrderByDescending(e => e.PostVersion)
                    .Select(e => e.PostVersion)
                    .FirstOrDefaultAsync();

                int newStage = lastStage + 1;

                // 5) عكس المرحلة السابقة إن وجدت (بنفس أسلوب فاتورة المبيعات: 9001، 9002، 9003، 9004 + تحديث رصيد العميل)
                if (lastStage > 0)
                {
                    await ReverseSalesReturnEntriesLikeSalesAsync(salesReturn, lastStage, now);
                }

                // 6) إنشاء قيود المرتجع (عكس المبيعات)
                // (1) مدين: إيرادات (عكس المبيعات)
                _db.LedgerEntries.Add(new LedgerEntry
                {
                    EntryDate = salesReturn.SRDate,
                    SourceType = LedgerSourceType.SalesReturn,
                    VoucherNo = voucherNo,
                    SourceId = salesReturnId,
                    LineNo = 1,
                    PostVersion = newStage,
                    AccountId = salesRevenueAccountId,
                    CustomerId = null,
                    Debit = returnAmount,
                    Credit = 0m,
                    Description = $"ترحيل مرتجع بيع رقم {salesReturnId} (مرحلة {newStage})",
                    CreatedAt = now
                });

                // (2) دائن: العميل (نقصان دين العميل)
                _db.LedgerEntries.Add(new LedgerEntry
                {
                    EntryDate = salesReturn.SRDate,
                    SourceType = LedgerSourceType.SalesReturn,
                    VoucherNo = voucherNo,
                    SourceId = salesReturnId,
                    LineNo = 2,
                    PostVersion = newStage,
                    AccountId = salesReturn.Customer.AccountId.Value,
                    CustomerId = salesReturn.CustomerId,
                    Debit = 0m,
                    Credit = returnAmount,
                    Description = $"ترحيل مرتجع بيع رقم {salesReturnId} (مرحلة {newStage})",
                    CreatedAt = now
                });

                // (3) مدين: مخزون + (4) دائن: COGS (لو التكلفة > 0)
                if (costTotal > 0m)
                {
                    _db.LedgerEntries.Add(new LedgerEntry
                    {
                        EntryDate = salesReturn.SRDate,
                        SourceType = LedgerSourceType.SalesReturn,
                        VoucherNo = voucherNo,
                        SourceId = salesReturnId,
                        LineNo = 3,
                        PostVersion = newStage,
                        AccountId = inventoryAccountId,
                        CustomerId = null,
                        Debit = costTotal,
                        Credit = 0m,
                        Description = $"إرجاع مخزون مرتجع بيع رقم {salesReturnId} (مرحلة {newStage})",
                        CreatedAt = now
                    });

                    _db.LedgerEntries.Add(new LedgerEntry
                    {
                        EntryDate = salesReturn.SRDate,
                        SourceType = LedgerSourceType.SalesReturn,
                        VoucherNo = voucherNo,
                        SourceId = salesReturnId,
                        LineNo = 4,
                        PostVersion = newStage,
                        AccountId = cogsAccountId,
                        CustomerId = null,
                        Debit = 0m,
                        Credit = costTotal,
                        Description = $"عكس تكلفة مبيعات مرتجع بيع رقم {salesReturnId} (مرحلة {newStage})",
                        CreatedAt = now
                    });
                }

                // 8) StockLedger + StockBatch يُسجّلان عند إضافة السطر (AddLineJson)، لا عند الترحيل.

                // 9) تحديث حالة المرتجع
                salesReturn.IsPosted = true;
                salesReturn.Status = "Posted";
                salesReturn.PostedAt = now;
                salesReturn.PostedBy = string.IsNullOrWhiteSpace(postedBy) ? "SYSTEM" : postedBy;

                // 10) حفظ التغييرات
                await _db.SaveChangesAsync();

                // 11) تحديث رصيد العميل
                await RecalcCustomerCurrentBalanceAsync(salesReturn.CustomerId);
                await _db.SaveChangesAsync();

                // 12) Commit
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // =========================================================
        // ترحيل مرتجع الشراء إلى LedgerEntries + StockLedger + StockBatch + تحديث رصيد المورد
        // ✅ مرتجع الشراء يقلل المخزون (QtyOut في StockLedger)
        // ✅ قيد المرتجع = عكس قيد فاتورة المشتريات (بنفس منطقها):
        // فاتورة المشتريات: (1) مدين مخزون (2) دائن مورد
        // مرتجع المشتريات:  (1) مدين مورد  (2) دائن مخزون
        // =========================================================
        public async Task PostPurchaseReturnAsync(int purchaseReturnId, string? postedBy)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();

            try
            {
                // 1) تحميل المرتجع + المورد + فاتورة الشراء المرجعية
                var purchaseReturn = await _db.PurchaseReturns
                    .Include(pr => pr.Customer)
                    .Include(pr => pr.RefPurchaseInvoice)
                    .FirstOrDefaultAsync(pr => pr.PRetId == purchaseReturnId);

                if (purchaseReturn == null)
                    throw new Exception("مرتجع الشراء غير موجود.");

                if (purchaseReturn.IsPosted)
                    throw new Exception("هذا المرتجع مترحّل بالفعل.");

                // لو المرتجع مرتبط بفاتورة شراء، لازم الفاتورة تكون مرحّلة أولاً
                if (purchaseReturn.RefPIId.HasValue && purchaseReturn.RefPIId.Value > 0)
                {
                    var refInvoice = purchaseReturn.RefPurchaseInvoice
                        ?? await _db.PurchaseInvoices.FirstOrDefaultAsync(pi => pi.PIId == purchaseReturn.RefPIId.Value);
                    if (refInvoice == null)
                        throw new Exception($"فاتورة الشراء رقم {purchaseReturn.RefPIId} غير موجودة.");
                    if (!refInvoice.IsPosted)
                        throw new Exception($"لا يمكن ترحيل المرتجع — فاتورة الشراء رقم {purchaseReturn.RefPIId} غير مرحّلة. يرجى ترحيل الفاتورة أولاً.");
                }

                if (purchaseReturn.Customer == null || purchaseReturn.Customer.AccountId == null || purchaseReturn.Customer.AccountId <= 0)
                    throw new Exception("المورد غير مرتبط بحساب محاسبي.");

                // 2) حساب المخزون (1105) — نفس فاتورة المشتريات
                int inventoryAccountId = await ResolveAccountIdByCodeAsync(InventoryAccountCode);

                var now = DateTime.UtcNow;
                decimal returnAmount = purchaseReturn.NetTotal;
                string voucherNo = purchaseReturn.PRetId.ToString();

                // 3) تحديد آخر مرحلة
                int lastStage = await _db.LedgerEntries
                    .Where(e =>
                        e.SourceType == LedgerSourceType.PurchaseReturn &&
                        e.SourceId == purchaseReturnId &&
                        (e.LineNo == 1 || e.LineNo == 2) &&
                        e.PostVersion > 0)
                    .OrderByDescending(e => e.PostVersion)
                    .Select(e => e.PostVersion)
                    .FirstOrDefaultAsync();

                int newStage = lastStage + 1;

                // 4) عكس المرحلة السابقة إن وجدت (9001، 9002 + تحديث رصيد المورد)
                if (lastStage > 0)
                {
                    await ReversePurchaseReturnEntriesLikePurchaseAsync(purchaseReturn, lastStage, now);
                }

                // 5) إنشاء قيود المرتجع (عكس فاتورة المشتريات)
                // (1) مدين: المورد (زيادة دين المورد)
                _db.LedgerEntries.Add(new LedgerEntry
                {
                    EntryDate = purchaseReturn.PRetDate,
                    SourceType = LedgerSourceType.PurchaseReturn,
                    VoucherNo = voucherNo,
                    SourceId = purchaseReturnId,
                    LineNo = 1,
                    PostVersion = newStage,
                    AccountId = purchaseReturn.Customer.AccountId.Value,
                    CustomerId = purchaseReturn.CustomerId,
                    Debit = returnAmount,
                    Credit = 0m,
                    Description = $"ترحيل مرتجع شراء رقم {purchaseReturnId} (مرحلة {newStage})",
                    CreatedAt = now
                });

                // (2) دائن: المخزون (نقصان المخزون — عكس فاتورة المشتريات)
                _db.LedgerEntries.Add(new LedgerEntry
                {
                    EntryDate = purchaseReturn.PRetDate,
                    SourceType = LedgerSourceType.PurchaseReturn,
                    VoucherNo = voucherNo,
                    SourceId = purchaseReturnId,
                    LineNo = 2,
                    PostVersion = newStage,
                    AccountId = inventoryAccountId,
                    CustomerId = null,
                    Debit = 0m,
                    Credit = returnAmount,
                    Description = $"ترحيل مرتجع شراء رقم {purchaseReturnId} (مرحلة {newStage})",
                    CreatedAt = now
                });

                // StockLedger + StockBatch يُسجّلان عند إضافة السطر (AddLineJson)، لا عند الترحيل.

                // 9) تحديث حالة المرتجع
                purchaseReturn.IsPosted = true;
                purchaseReturn.Status = "Posted";
                purchaseReturn.PostedAt = now;
                purchaseReturn.PostedBy = string.IsNullOrWhiteSpace(postedBy) ? "SYSTEM" : postedBy;

                // 10) حفظ التغييرات
                await _db.SaveChangesAsync();

                // 11) تحديث رصيد المورد
                await RecalcCustomerCurrentBalanceAsync(purchaseReturn.CustomerId);
                await _db.SaveChangesAsync();

                // 12) Commit
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // =========================================================
        // دوال مساعدة للترحيل
        // =========================================================
        private async Task<decimal> GetAverageCostAsync(int prodId, int warehouseId)
        {
            var avgCost = await _db.StockLedger
                .Where(sl => sl.ProdId == prodId && sl.WarehouseId == warehouseId && sl.QtyIn > 0)
                .AverageAsync(sl => (decimal?)sl.UnitCost) ?? 0m;

            return avgCost > 0m ? avgCost : 0m;
        }

        /// <summary>
        /// عكس مرحلة سابقة من مرتجع البيع — بنفس أسلوب فاتورة المبيعات: قيود 9001، 9002، 9003، 9004 + تحديث رصيد العميل.
        /// </summary>
        private async Task ReverseSalesReturnEntriesLikeSalesAsync(SalesReturn salesReturn, int lastStage, DateTime now)
        {
            int salesReturnId = salesReturn.SRId;
            string voucherNo = salesReturn.SRId.ToString();

            // (1) و (2): إيرادات مدين، عميل دائن — نعكسهما
            var lastLine1 = await _db.LedgerEntries
                .Where(e => e.SourceType == LedgerSourceType.SalesReturn && e.SourceId == salesReturnId && e.LineNo == 1 && e.PostVersion == lastStage)
                .OrderByDescending(e => e.Id)
                .FirstOrDefaultAsync();
            var lastLine2 = await _db.LedgerEntries
                .Where(e => e.SourceType == LedgerSourceType.SalesReturn && e.SourceId == salesReturnId && e.LineNo == 2 && e.PostVersion == lastStage)
                .OrderByDescending(e => e.Id)
                .FirstOrDefaultAsync();

            if (lastLine1 == null || lastLine2 == null)
                throw new Exception("تعذر تحديد آخر ترحيل سابق للمرتجع (قيود غير مكتملة).");

            decimal oldReturnAmount = lastLine1.Debit > 0 ? lastLine1.Debit : lastLine2.Credit;

            // 9001: عكس سطر الإيرادات (كان مدين -> يصبح دائن)
            _db.LedgerEntries.Add(new LedgerEntry
            {
                EntryDate = salesReturn.SRDate,
                SourceType = LedgerSourceType.SalesReturn,
                VoucherNo = voucherNo,
                SourceId = salesReturnId,
                LineNo = 9001,
                PostVersion = lastStage,
                AccountId = lastLine1.AccountId,
                CustomerId = null,
                Debit = 0m,
                Credit = oldReturnAmount,
                Description = $"عكس ترحيل مرتجع بيع رقم {salesReturnId} (عكس مرحلة {lastStage})",
                CreatedAt = now
            });

            // 9002: عكس سطر العميل (كان دائن -> يصبح مدين)
            _db.LedgerEntries.Add(new LedgerEntry
            {
                EntryDate = salesReturn.SRDate,
                SourceType = LedgerSourceType.SalesReturn,
                VoucherNo = voucherNo,
                SourceId = salesReturnId,
                LineNo = 9002,
                PostVersion = lastStage,
                AccountId = lastLine2.AccountId,
                CustomerId = lastLine2.CustomerId,
                Debit = oldReturnAmount,
                Credit = 0m,
                Description = $"عكس ترحيل مرتجع بيع رقم {salesReturnId} (عكس مرحلة {lastStage})",
                CreatedAt = now
            });

            // إعادة رصيد العميل (عكس المرتجع = زيادة دين العميل مرة أخرى)
            if (lastLine2.CustomerId.HasValue && lastLine2.CustomerId.Value > 0)
            {
                var cust = await _db.Customers.FirstOrDefaultAsync(c => c.CustomerId == lastLine2.CustomerId.Value);
                if (cust != null)
                    cust.CurrentBalance += oldReturnAmount;
            }

            // (3) و (4): مخزون و COGS إن وجدا
            var lastLine3 = await _db.LedgerEntries
                .Where(e => e.SourceType == LedgerSourceType.SalesReturn && e.SourceId == salesReturnId && e.LineNo == 3 && e.PostVersion == lastStage)
                .OrderByDescending(e => e.Id)
                .FirstOrDefaultAsync();
            var lastLine4 = await _db.LedgerEntries
                .Where(e => e.SourceType == LedgerSourceType.SalesReturn && e.SourceId == salesReturnId && e.LineNo == 4 && e.PostVersion == lastStage)
                .OrderByDescending(e => e.Id)
                .FirstOrDefaultAsync();

            if (lastLine3 != null && lastLine4 != null)
            {
                decimal oldCostAmount = lastLine3.Debit > 0 ? lastLine3.Debit : lastLine4.Credit;
                _db.LedgerEntries.Add(new LedgerEntry
                {
                    EntryDate = salesReturn.SRDate,
                    SourceType = LedgerSourceType.SalesReturn,
                    VoucherNo = voucherNo,
                    SourceId = salesReturnId,
                    LineNo = 9003,
                    PostVersion = lastStage,
                    AccountId = lastLine3.AccountId,
                    CustomerId = null,
                    Debit = 0m,
                    Credit = oldCostAmount,
                    Description = $"عكس تكلفة مرتجع بيع رقم {salesReturnId} (عكس مرحلة {lastStage})",
                    CreatedAt = now
                });
                _db.LedgerEntries.Add(new LedgerEntry
                {
                    EntryDate = salesReturn.SRDate,
                    SourceType = LedgerSourceType.SalesReturn,
                    VoucherNo = voucherNo,
                    SourceId = salesReturnId,
                    LineNo = 9004,
                    PostVersion = lastStage,
                    AccountId = lastLine4.AccountId,
                    CustomerId = null,
                    Debit = oldCostAmount,
                    Credit = 0m,
                    Description = $"عكس تكلفة مرتجع بيع رقم {salesReturnId} (عكس مرحلة {lastStage})",
                    CreatedAt = now
                });
            }
        }

        /// <summary>
        /// عكس مرحلة سابقة من مرتجع الشراء — بنفس منطق فاتورة المشتريات: قيدان فقط (9001، 9002) + تحديث رصيد المورد.
        /// </summary>
        private async Task ReversePurchaseReturnEntriesLikePurchaseAsync(PurchaseReturn purchaseReturn, int lastStage, DateTime now)
        {
            int purchaseReturnId = purchaseReturn.PRetId;
            string voucherNo = purchaseReturn.PRetId.ToString();

            // (1) المورد مدين، (2) المخزون دائن — نعكسهما
            var lastLine1 = await _db.LedgerEntries
                .Where(e => e.SourceType == LedgerSourceType.PurchaseReturn && e.SourceId == purchaseReturnId && e.LineNo == 1 && e.PostVersion == lastStage)
                .OrderByDescending(e => e.Id)
                .FirstOrDefaultAsync();
            var lastLine2 = await _db.LedgerEntries
                .Where(e => e.SourceType == LedgerSourceType.PurchaseReturn && e.SourceId == purchaseReturnId && e.LineNo == 2 && e.PostVersion == lastStage)
                .OrderByDescending(e => e.Id)
                .FirstOrDefaultAsync();

            if (lastLine1 == null || lastLine2 == null)
                throw new Exception("تعذر تحديد آخر ترحيل سابق لمرتجع الشراء (قيود غير مكتملة).");

            decimal oldReturnAmount = lastLine1.Debit > 0 ? lastLine1.Debit : lastLine2.Credit;

            // 9001: عكس سطر المورد (كان مدين -> يصبح دائن)
            _db.LedgerEntries.Add(new LedgerEntry
            {
                EntryDate = purchaseReturn.PRetDate,
                SourceType = LedgerSourceType.PurchaseReturn,
                VoucherNo = voucherNo,
                SourceId = purchaseReturnId,
                LineNo = 9001,
                PostVersion = lastStage,
                AccountId = lastLine1.AccountId,
                CustomerId = lastLine1.CustomerId,
                Debit = 0m,
                Credit = oldReturnAmount,
                Description = $"عكس ترحيل مرتجع شراء رقم {purchaseReturnId} (عكس مرحلة {lastStage})",
                CreatedAt = now
            });

            // 9002: عكس سطر المخزون (كان دائن -> يصبح مدين)
            _db.LedgerEntries.Add(new LedgerEntry
            {
                EntryDate = purchaseReturn.PRetDate,
                SourceType = LedgerSourceType.PurchaseReturn,
                VoucherNo = voucherNo,
                SourceId = purchaseReturnId,
                LineNo = 9002,
                PostVersion = lastStage,
                AccountId = lastLine2.AccountId,
                CustomerId = null,
                Debit = oldReturnAmount,
                Credit = 0m,
                Description = $"عكس ترحيل مرتجع شراء رقم {purchaseReturnId} (عكس مرحلة {lastStage})",
                CreatedAt = now
            });

            // إعادة رصيد المورد (عكس المرتجع = نقصان دين المورد مرة أخرى)
            if (lastLine1.CustomerId.HasValue && lastLine1.CustomerId.Value > 0)
            {
                var cust = await _db.Customers.FirstOrDefaultAsync(c => c.CustomerId == lastLine1.CustomerId.Value);
                if (cust != null)
                    cust.CurrentBalance -= oldReturnAmount;
            }
        }

        // =========================
        // PostStockAdjustmentAsync — ترحيل تسوية الجرد
        // =========================
        public async Task PostStockAdjustmentAsync(int stockAdjustmentId, string? postedBy)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();

            try
            {
                var adjustment = await _db.StockAdjustments
                    .Include(a => a.Lines)
                        .ThenInclude(l => l.Product)
                    .Include(a => a.Lines)
                        .ThenInclude(l => l.Batch)
                    .FirstOrDefaultAsync(a => a.Id == stockAdjustmentId);

                if (adjustment == null)
                    throw new Exception("التسوية غير موجودة.");

                if (adjustment.IsPosted)
                    throw new Exception("هذه التسوية مترحلة بالفعل.");

                if (!adjustment.Lines.Any())
                    throw new Exception("لا يمكن ترحيل تسوية بدون سطور.");

                var now = DateTime.UtcNow;
                int newStage = 1;

                // حساب إجمالي فرق التكلفة
                decimal totalCostDiff = adjustment.Lines
                    .Where(l => l.CostDiff.HasValue)
                    .Sum(l => l.CostDiff.Value);

                // إنشاء قيود محاسبية (تؤثر في الربح)
                // - زيادة مخزون: مدين مخزون / دائن إيرادات أخرى (فائض جرد)
                // - نقصان مخزون: مدين تكلفة البضاعة / دائن مخزون (عجز جرد)
                int inventoryAccountId = await ResolveAccountIdByCodeAsync(InventoryAccountCode); // 1105
                int cogsAccountId = await ResolveAccountIdByCodeAsync(CogsAccountCode);          // 5100 عجز
                int otherRevenueAccountId = await ResolveAccountIdByCodeAsync("4200");          // 4200 فائض

                string voucherNo = adjustment.Id.ToString();
                int lineNo = 1;
                foreach (var line in adjustment.Lines.OrderBy(l => l.Id))
                {
                    if (line.QtyDiff == 0) continue; // تخطي السطور بدون فرق

                    // تحديث StockLedger
                    var ledger = new StockLedger
                    {
                        TranDate = adjustment.AdjustmentDate,
                        WarehouseId = adjustment.WarehouseId,
                        ProdId = line.ProductId,
                        BatchNo = line.Batch?.BatchNo,
                        BatchId = line.BatchId,
                        Expiry = line.Batch?.Expiry,
                        SourceType = "Adjustment",
                        SourceId = adjustment.Id,
                        SourceLine = lineNo,
                        AdjustmentReason = adjustment.Reason,
                        Note = $"تسوية جرد: {line.Product?.ProdName ?? $"صنف #{line.ProductId}"}"
                    };

                    if (line.QtyDiff > 0)
                    {
                        // زيادة مخزون
                        ledger.QtyIn = line.QtyDiff;
                        ledger.QtyOut = 0;
                        ledger.UnitCost = line.CostPerUnit ?? 0m;
                        ledger.RemainingQty = line.QtyDiff;
                    }
                    else
                    {
                        // نقصان مخزون
                        ledger.QtyIn = 0;
                        ledger.QtyOut = Math.Abs(line.QtyDiff);
                        ledger.UnitCost = line.CostPerUnit ?? 0m;
                        ledger.RemainingQty = null;
                    }

                    _db.StockLedger.Add(ledger);

                    // قيد محاسبي: مدين مخزون / دائن إيرادات (فائض) أو مدين تكلفة / دائن مخزون (عجز)
                    decimal costDiff = (line.CostDiff ?? 0m);
                    if (costDiff != 0)
                    {
                        if (costDiff > 0)
                        {
                            _db.LedgerEntries.Add(new LedgerEntry
                            {
                                EntryDate = adjustment.AdjustmentDate,
                                SourceType = LedgerSourceType.StockAdjustment,
                                VoucherNo = voucherNo,
                                SourceId = adjustment.Id,
                                LineNo = lineNo,
                                PostVersion = newStage,
                                AccountId = inventoryAccountId,
                                CustomerId = null,
                                Debit = costDiff,
                                Credit = 0m,
                                Description = $"زيادة مخزون تسوية جرد رقم {adjustment.Id}",
                                CreatedAt = now
                            });
                            _db.LedgerEntries.Add(new LedgerEntry
                            {
                                EntryDate = adjustment.AdjustmentDate,
                                SourceType = LedgerSourceType.StockAdjustment,
                                VoucherNo = voucherNo,
                                SourceId = adjustment.Id,
                                LineNo = lineNo + 1000,
                                PostVersion = newStage,
                                AccountId = otherRevenueAccountId,
                                CustomerId = null,
                                Debit = 0m,
                                Credit = costDiff,
                                Description = $"فائض جرد تسوية رقم {adjustment.Id}",
                                CreatedAt = now
                            });
                        }
                        else
                        {
                            decimal absDiff = Math.Abs(costDiff);
                            _db.LedgerEntries.Add(new LedgerEntry
                            {
                                EntryDate = adjustment.AdjustmentDate,
                                SourceType = LedgerSourceType.StockAdjustment,
                                VoucherNo = voucherNo,
                                SourceId = adjustment.Id,
                                LineNo = lineNo,
                                PostVersion = newStage,
                                AccountId = cogsAccountId,
                                CustomerId = null,
                                Debit = absDiff,
                                Credit = 0m,
                                Description = $"عجز جرد تسوية رقم {adjustment.Id}",
                                CreatedAt = now
                            });
                            _db.LedgerEntries.Add(new LedgerEntry
                            {
                                EntryDate = adjustment.AdjustmentDate,
                                SourceType = LedgerSourceType.StockAdjustment,
                                VoucherNo = voucherNo,
                                SourceId = adjustment.Id,
                                LineNo = lineNo + 1000,
                                PostVersion = newStage,
                                AccountId = inventoryAccountId,
                                CustomerId = null,
                                Debit = 0m,
                                Credit = absDiff,
                                Description = $"نقصان مخزون تسوية جرد رقم {adjustment.Id}",
                                CreatedAt = now
                            });
                        }
                    }

                    // تحديث StockBatch
                    if (line.Batch != null && !string.IsNullOrWhiteSpace(line.Batch.BatchNo))
                    {
                        var stockBatch = await _db.StockBatches
                            .FirstOrDefaultAsync(b => 
                                b.BatchNo.Trim() == line.Batch.BatchNo.Trim() && 
                                b.ProdId == line.ProductId && 
                                b.WarehouseId == adjustment.WarehouseId &&
                                (b.Expiry.HasValue ? b.Expiry.Value.Date : (DateTime?)null) == (line.Batch.Expiry.Date));

                        if (stockBatch != null)
                        {
                            stockBatch.QtyOnHand = line.QtyAfter;
                            stockBatch.UpdatedAt = DateTime.UtcNow;
                        }
                        else if (line.QtyAfter > 0)
                        {
                            // إنشاء StockBatch جديد
                            var newStockBatch = new StockBatch
                            {
                                WarehouseId = adjustment.WarehouseId,
                                ProdId = line.ProductId,
                                BatchNo = line.Batch.BatchNo.Trim(),
                                Expiry = line.Batch.Expiry,
                                QtyOnHand = line.QtyAfter,
                                UpdatedAt = DateTime.UtcNow
                            };
                            _db.StockBatches.Add(newStockBatch);
                        }
                    }
                    else
                    {
                        // بدون تشغيلة: تحديث أو إنشاء StockBatch عام (BatchNo = "")
                        var stockBatch = await _db.StockBatches
                            .FirstOrDefaultAsync(b =>
                                b.ProdId == line.ProductId &&
                                b.WarehouseId == adjustment.WarehouseId &&
                                (b.BatchNo == null || b.BatchNo.Trim() == ""));
                        if (stockBatch != null)
                        {
                            stockBatch.QtyOnHand = line.QtyAfter;
                            stockBatch.UpdatedAt = DateTime.UtcNow;
                        }
                        else if (line.QtyAfter > 0)
                        {
                            _db.StockBatches.Add(new StockBatch
                            {
                                WarehouseId = adjustment.WarehouseId,
                                ProdId = line.ProductId,
                                BatchNo = "",
                                Expiry = null,
                                QtyOnHand = line.QtyAfter,
                                UpdatedAt = DateTime.UtcNow
                            });
                        }
                    }

                    lineNo++;
                }

                // تحديث حالة التسوية
                adjustment.IsPosted = true;
                adjustment.Status = "مترحلة";
                adjustment.PostedAt = now;
                adjustment.PostedBy = postedBy ?? "SYSTEM";

                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // =========================
        // ReverseStockAdjustmentAsync — عكس ترحيل تسوية الجرد
        // =========================
        public async Task ReverseStockAdjustmentAsync(int stockAdjustmentId, string? postedBy)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();

            try
            {
                var adjustment = await _db.StockAdjustments
                    .Include(a => a.Lines)
                        .ThenInclude(l => l.Batch)
                    .FirstOrDefaultAsync(a => a.Id == stockAdjustmentId);

                if (adjustment == null)
                    throw new Exception("التسوية غير موجودة.");

                if (!adjustment.IsPosted)
                    throw new Exception("هذه التسوية غير مترحلة.");

                // حذف قيود LedgerEntries المرتبطة بالتسوية
                var ledgerEntries = await _db.LedgerEntries
                    .Where(e => e.SourceType == LedgerSourceType.StockAdjustment && e.SourceId == adjustment.Id)
                    .ToListAsync();
                _db.LedgerEntries.RemoveRange(ledgerEntries);

                // حذف حركات StockLedger المرتبطة
                var ledgers = await _db.StockLedger
                    .Where(l => l.SourceType == "Adjustment" && l.SourceId == adjustment.Id)
                    .ToListAsync();

                _db.StockLedger.RemoveRange(ledgers);

                // استعادة الكميات في StockBatch (يجب أن تتطابق مع PostStockAdjustment)
                foreach (var line in adjustment.Lines)
                {
                    if (line.Batch != null && !string.IsNullOrWhiteSpace(line.Batch.BatchNo))
                    {
                        var stockBatch = await _db.StockBatches
                            .FirstOrDefaultAsync(b => 
                                b.BatchNo.Trim() == line.Batch.BatchNo.Trim() && 
                                b.ProdId == line.ProductId && 
                                b.WarehouseId == adjustment.WarehouseId &&
                                (b.Expiry.HasValue ? b.Expiry.Value.Date : (DateTime?)null) == (line.Batch.Expiry.Date));

                        if (stockBatch != null)
                        {
                            if (line.QtyBefore > 0)
                            {
                                stockBatch.QtyOnHand = line.QtyBefore;
                                stockBatch.UpdatedAt = DateTime.UtcNow;
                            }
                            else
                                _db.StockBatches.Remove(stockBatch);
                        }
                    }
                    else
                    {
                        // بدون تشغيلة: تحديث StockBatch العام (BatchNo فارغ)
                        var stockBatch = await _db.StockBatches
                            .FirstOrDefaultAsync(b =>
                                b.ProdId == line.ProductId &&
                                b.WarehouseId == adjustment.WarehouseId &&
                                (b.BatchNo == null || b.BatchNo.Trim() == ""));
                        if (stockBatch != null)
                        {
                            if (line.QtyBefore > 0)
                            {
                                stockBatch.QtyOnHand = line.QtyBefore;
                                stockBatch.UpdatedAt = DateTime.UtcNow;
                            }
                            else
                                _db.StockBatches.Remove(stockBatch);
                        }
                    }
                }

                // تحديث حالة التسوية
                adjustment.IsPosted = false;
                adjustment.Status = "مسودة";
                adjustment.PostedAt = null;
                adjustment.PostedBy = null;

                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // =========================
        // PostStockTransferAsync — ترحيل تحويل بين المخازن
        // =========================
        public async Task PostStockTransferAsync(int stockTransferId, string? postedBy)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();

            try
            {
                var transfer = await _db.StockTransfers
                    .Include(t => t.Lines)
                        .ThenInclude(l => l.Product)
                    .Include(t => t.Lines)
                        .ThenInclude(l => l.Batch)
                    .FirstOrDefaultAsync(t => t.Id == stockTransferId);

                if (transfer == null)
                    throw new Exception("التحويل غير موجود.");

                if (transfer.IsPosted)
                    throw new Exception("هذا التحويل مترحل بالفعل.");

                if (!transfer.Lines.Any())
                    throw new Exception("لا يمكن ترحيل تحويل بدون سطور.");

                var now = DateTime.UtcNow;
                int movementGroupId = transfer.Id; // استخدام ID التحويل كرقم مجموعة
                decimal totalProfit = 0m;
                string voucherNo = $"تحويل-{transfer.Id}";

                int lineNo = 1;
                foreach (var line in transfer.Lines.OrderBy(l => l.LineNo))
                {
                    // تكلفة الوحدة (من الشراء)
                    decimal costPerUnit = line.UnitCost;
                    // سعر التحويل: سعر الجمهور × (1 - خصم التحويل%)
                    decimal transferPricePerUnit = costPerUnit;
                    if (line.PriceRetail.HasValue && line.PriceRetail.Value > 0 && line.DiscountPct.HasValue)
                    {
                        transferPricePerUnit = line.PriceRetail.Value * (1m - (line.DiscountPct.Value / 100m));
                    }
                    // ربح السطر: عندما الخصم < المرجح → سعر التحويل > التكلفة
                    decimal lineProfit = 0m;
                    if (transferPricePerUnit > costPerUnit)
                    {
                        lineProfit = (transferPricePerUnit - costPerUnit) * line.Qty;
                        totalProfit += lineProfit;
                    }
                    decimal inUnitCost = lineProfit > 0 ? transferPricePerUnit : costPerUnit;

                    // حركة خروج من المخزن المصدر (بالتكلفة)
                    var outLedger = new StockLedger
                    {
                        TranDate = transfer.TransferDate,
                        WarehouseId = transfer.FromWarehouseId,
                        ProdId = line.ProductId,
                        BatchNo = line.Batch?.BatchNo,
                        BatchId = line.BatchId,
                        Expiry = line.Batch?.Expiry,
                        QtyIn = 0,
                        QtyOut = line.Qty,
                        UnitCost = costPerUnit,
                        RemainingQty = null,
                        SourceType = "TransferOut",
                        SourceId = transfer.Id,
                        SourceLine = lineNo,
                        MovementGroupId = movementGroupId,
                        CounterWarehouseId = transfer.ToWarehouseId,
                        Note = $"تحويل مخزني: {line.Product?.ProdName ?? $"صنف #{line.ProductId}"}"
                    };
                    _db.StockLedger.Add(outLedger);

                    // حركة دخول للمخزن الوجهة (بسعر التحويل عند وجود ربح)
                    var inLedger = new StockLedger
                    {
                        TranDate = transfer.TransferDate,
                        WarehouseId = transfer.ToWarehouseId,
                        ProdId = line.ProductId,
                        BatchNo = line.Batch?.BatchNo,
                        BatchId = line.BatchId,
                        Expiry = line.Batch?.Expiry,
                        QtyIn = line.Qty,
                        QtyOut = 0,
                        UnitCost = inUnitCost,
                        RemainingQty = line.Qty,
                        SourceType = "TransferIn",
                        SourceId = transfer.Id,
                        SourceLine = lineNo,
                        MovementGroupId = movementGroupId,
                        CounterWarehouseId = transfer.FromWarehouseId,
                        Note = $"تحويل مخزني: {line.Product?.ProdName ?? $"صنف #{line.ProductId}"}"
                    };
                    _db.StockLedger.Add(inLedger);

                    // تحديث StockBatch للمخزن المصدر (نقصان)
                    if (line.Batch != null && !string.IsNullOrWhiteSpace(line.Batch.BatchNo))
                    {
                        var fromStockBatch = await _db.StockBatches
                            .FirstOrDefaultAsync(b => 
                                b.BatchNo.Trim() == line.Batch.BatchNo.Trim() && 
                                b.ProdId == line.ProductId && 
                                b.WarehouseId == transfer.FromWarehouseId &&
                                (b.Expiry.HasValue ? b.Expiry.Value.Date : (DateTime?)null) == (line.Batch.Expiry.Date));

                        if (fromStockBatch != null)
                        {
                            fromStockBatch.QtyOnHand = Math.Max(0, fromStockBatch.QtyOnHand - line.Qty);
                            fromStockBatch.UpdatedAt = DateTime.UtcNow;
                        }
                    }

                    // تحديث/إنشاء StockBatch للمخزن الوجهة (زيادة)
                    if (line.Batch != null && !string.IsNullOrWhiteSpace(line.Batch.BatchNo))
                    {
                        var toStockBatch = await _db.StockBatches
                            .FirstOrDefaultAsync(b => 
                                b.BatchNo.Trim() == line.Batch.BatchNo.Trim() && 
                                b.ProdId == line.ProductId && 
                                b.WarehouseId == transfer.ToWarehouseId &&
                                (b.Expiry.HasValue ? b.Expiry.Value.Date : (DateTime?)null) == (line.Batch.Expiry.Date));

                        if (toStockBatch != null)
                        {
                            toStockBatch.QtyOnHand += line.Qty;
                            toStockBatch.UpdatedAt = DateTime.UtcNow;
                        }
                        else
                        {
                            var newStockBatch = new StockBatch
                            {
                                WarehouseId = transfer.ToWarehouseId,
                                ProdId = line.ProductId,
                                BatchNo = line.Batch.BatchNo.Trim(),
                                Expiry = line.Batch.Expiry,
                                QtyOnHand = line.Qty,
                                UpdatedAt = DateTime.UtcNow
                            };
                            _db.StockBatches.Add(newStockBatch);
                        }
                    }

                    lineNo++;
                }

                // قيد محاسبي للربح: عند التحويل بخصم أقل من المرجح → ربح
                if (totalProfit > 0)
                {
                    int inventoryAccountId = await ResolveAccountIdByCodeAsync(InventoryAccountCode);
                    int transferProfitAccountId = await ResolveAccountIdByCodeAsync(TransferProfitAccountCode);
                    _db.LedgerEntries.Add(new LedgerEntry
                    {
                        EntryDate = transfer.TransferDate,
                        SourceType = LedgerSourceType.StockTransfer,
                        VoucherNo = voucherNo,
                        SourceId = transfer.Id,
                        LineNo = 1,
                        PostVersion = 1,
                        AccountId = inventoryAccountId,
                        CustomerId = null,
                        Debit = totalProfit,
                        Credit = 0m,
                        Description = $"ربح تحويل مخزني رقم {transfer.Id} (خصم أقل من المرجح)",
                        CreatedAt = now
                    });
                    _db.LedgerEntries.Add(new LedgerEntry
                    {
                        EntryDate = transfer.TransferDate,
                        SourceType = LedgerSourceType.StockTransfer,
                        VoucherNo = voucherNo,
                        SourceId = transfer.Id,
                        LineNo = 2,
                        PostVersion = 1,
                        AccountId = transferProfitAccountId,
                        CustomerId = null,
                        Debit = 0m,
                        Credit = totalProfit,
                        Description = $"ربح تحويل مخزني رقم {transfer.Id} (خصم أقل من المرجح)",
                        CreatedAt = now
                    });
                }

                // تحديث حالة التحويل
                transfer.IsPosted = true;
                transfer.Status = "مترحل";
                transfer.PostedAt = now;
                transfer.PostedBy = postedBy ?? "SYSTEM";

                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // =========================
        // ReverseStockTransferAsync — عكس ترحيل تحويل بين المخازن
        // =========================
        public async Task ReverseStockTransferAsync(int stockTransferId, string? postedBy)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();

            try
            {
                var transfer = await _db.StockTransfers
                    .Include(t => t.Lines)
                        .ThenInclude(l => l.Batch)
                    .FirstOrDefaultAsync(t => t.Id == stockTransferId);

                if (transfer == null)
                    throw new Exception("التحويل غير موجود.");

                if (!transfer.IsPosted)
                    throw new Exception("هذا التحويل غير مترحل.");

                // حذف حركات StockLedger المرتبطة
                var ledgers = await _db.StockLedger
                    .Where(l => (l.SourceType == "TransferOut" || l.SourceType == "TransferIn") && l.SourceId == transfer.Id)
                    .ToListAsync();

                _db.StockLedger.RemoveRange(ledgers);

                // عكس قيد الربح (إن وُجد): قيد عكسي
                var profitEntries = await _db.LedgerEntries
                    .Where(e => e.SourceType == LedgerSourceType.StockTransfer && e.SourceId == transfer.Id && e.PostVersion > 0)
                    .OrderBy(e => e.LineNo)
                    .ToListAsync();
                if (profitEntries.Any())
                {
                    string voucherNo = $"تحويل-{transfer.Id}-عكس";
                    var now = DateTime.UtcNow;
                    foreach (var e in profitEntries)
                    {
                        _db.LedgerEntries.Add(new LedgerEntry
                        {
                            EntryDate = transfer.TransferDate,
                            SourceType = LedgerSourceType.StockTransfer,
                            VoucherNo = voucherNo,
                            SourceId = transfer.Id,
                            LineNo = e.LineNo == 1 ? 9001 : 9002,
                            PostVersion = 0,
                            AccountId = e.AccountId,
                            CustomerId = null,
                            Debit = e.Credit,
                            Credit = e.Debit,
                            Description = $"عكس ربح تحويل مخزني رقم {transfer.Id}",
                            CreatedAt = now
                        });
                    }
                }

                // استعادة الكميات في StockBatch
                foreach (var line in transfer.Lines)
                {
                    if (line.Batch != null && !string.IsNullOrWhiteSpace(line.Batch.BatchNo))
                    {
                        // استعادة للمخزن المصدر (زيادة)
                        var fromStockBatch = await _db.StockBatches
                            .FirstOrDefaultAsync(b => 
                                b.BatchNo.Trim() == line.Batch.BatchNo.Trim() && 
                                b.ProdId == line.ProductId && 
                                b.WarehouseId == transfer.FromWarehouseId &&
                                (b.Expiry.HasValue ? b.Expiry.Value.Date : (DateTime?)null) == (line.Batch.Expiry.Date));

                        if (fromStockBatch != null)
                        {
                            fromStockBatch.QtyOnHand += line.Qty;
                            fromStockBatch.UpdatedAt = DateTime.UtcNow;
                        }

                        // استعادة للمخزن الوجهة (نقصان)
                        var toStockBatch = await _db.StockBatches
                            .FirstOrDefaultAsync(b => 
                                b.BatchNo.Trim() == line.Batch.BatchNo.Trim() && 
                                b.ProdId == line.ProductId && 
                                b.WarehouseId == transfer.ToWarehouseId &&
                                (b.Expiry.HasValue ? b.Expiry.Value.Date : (DateTime?)null) == (line.Batch.Expiry.Date));

                        if (toStockBatch != null)
                        {
                            toStockBatch.QtyOnHand = Math.Max(0, toStockBatch.QtyOnHand - line.Qty);
                            toStockBatch.UpdatedAt = DateTime.UtcNow;
                        }
                    }
                }

                // تحديث حالة التحويل
                transfer.IsPosted = false;
                transfer.Status = "مسودة";
                transfer.PostedAt = null;
                transfer.PostedBy = null;

                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

    }
}
