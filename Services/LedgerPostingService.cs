using ERP.Data;                 // AppDbContext
using ERP.Models;               // PurchaseInvoice, LedgerEntry, LedgerSourceType
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
        Task RecalcAllCustomerBalancesAsync();




    }

    public class LedgerPostingService : ILedgerPostingService
    {
        private readonly AppDbContext _db; // متغير: سياق قاعدة البيانات

        public LedgerPostingService(AppDbContext db)
        {
            _db = db;
        }






        public async Task PostPurchaseInvoiceAsync(int purchaseInvoiceId, string? postedBy)
            => await PostPurchaseInvoiceAsync(purchaseInvoiceId, postedBy, null);

        public async Task PostPurchaseInvoiceAsync(int purchaseInvoiceId, string? postedBy, IDbContextTransaction? existingTransaction)
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
                // 9) تحديث حالة الفاتورة (قفل)
                // =========================================================
                invoice.IsPosted = true;
                invoice.Status = $"مرحلة {newStage}";
                invoice.PostedAt = now;
                invoice.PostedBy = postedBy;

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
        public async Task PostSalesInvoiceAsync(int salesInvoiceId, string? postedBy)
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
        // دالة: إعادة حساب جميع أرصدة العملاء/الموردين من القيود LedgerEntries
        // ✅ مهم: تستخدم CustomerId وليس AccountId لضمان الدقة
        // - تحسب الرصيد لكل عميل من قيوده في LedgerEntries
        // - تحدث CurrentBalance لكل عميل
        // =========================================================
        public async Task RecalcAllCustomerBalancesAsync()
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




    }
}
