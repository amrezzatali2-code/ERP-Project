using ERP.Data;                 // AppDbContext
using ERP.Models;               // PurchaseInvoice, LedgerEntry, LedgerSourceType
using Microsoft.EntityFrameworkCore;
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


    }

    public class LedgerPostingService : ILedgerPostingService
    {
        private readonly AppDbContext _db; // متغير: سياق قاعدة البيانات

        public LedgerPostingService(AppDbContext db)
        {
            _db = db;
        }

        public async Task PostPurchaseInvoiceAsync(int purchaseInvoiceId, string? postedBy)
        {
            // ================================
            // 0) Transaction لضمان سلامة العملية
            // ================================
            await using var tx = await _db.Database.BeginTransactionAsync();

            try
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
                // 10) تحديث رصيد المورد الحالي
                // =========================================================
                invoice.Customer.CurrentBalance += newAmount;

                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
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

            // ❌ لا SaveChanges هنا
            // ✅ SaveChanges + Commit يتموا مرة واحدة في DeleteConfirmed داخل نفس Transaction
        }





    }
}
