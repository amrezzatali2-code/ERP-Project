using ERP.Data;                 // AppDbContext
using ERP.Models;               // PurchaseInvoice, LedgerEntry, LedgerSourceType
using Microsoft.EntityFrameworkCore;

namespace ERP.Services
{
    /// <summary>
    /// خدمة الترحيل المحاسبي (دفتر الأستاذ).
    /// الفكرة: كل مستند "يُرحّل" = يُنشئ صفّين أو أكثر داخل LedgerEntries.
    /// كل صف يمثل "سطر قيد" لحساب واحد (Debit أو Credit).
    ///
    /// ✅ تحديث مهم حسب اتفاقنا:
    /// - لو الفاتورة اتفتحت بعد الترحيل واتعدّلت ثم تم ترحيلها مرة أخرى:
    ///   نقوم أولاً بعمل "قيود عكسية" لآخر ترحيل سابق (مرحلة سابقة)
    ///   ثم نضيف القيود الجديدة (مرحلة جديدة).
    /// - وبذلك يظل عندنا تاريخ مراحل (Stage 1 / Stage 2 / Stage 3 ...) داخل دفتر الأستاذ.
    /// </summary>
    public interface ILedgerPostingService
    {
        /// <summary>
        /// ترحيل فاتورة مشتريات إلى دفتر الأستاذ.
        /// </summary>
        /// <param name="purchaseInvoiceId">متغير: رقم فاتورة المشتريات</param>
        /// <param name="postedBy">متغير: اسم المستخدم الذي رحّل</param>
        Task PostPurchaseInvoiceAsync(int purchaseInvoiceId, string? postedBy);
    }

    public class LedgerPostingService : ILedgerPostingService
    {
        private readonly AppDbContext _db;  // متغير: كائن قاعدة البيانات EF

        public LedgerPostingService(AppDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// ترحيل فاتورة مشتريات (آجل) إلى LedgerEntries:
        /// - المخزن (حساب 1105): مدين
        /// - المورد: دائن (له فلوس عندنا)
        ///
        /// ✅ لو وجدنا ترحيل سابق لنفس الفاتورة (لأنها اتفتحت واتعدّلت):
        ///   1) نعمل قيود عكسية لآخر ترحيل سابق
        ///   2) نطرح قيمته من CurrentBalance للمورد القديم
        ///   3) ثم نرحّل الفاتورة بالقيمة الجديدة
        ///   4) ونضيف القيمة الجديدة لـ CurrentBalance للمورد الحالي
        /// </summary>
        public async Task PostPurchaseInvoiceAsync(int purchaseInvoiceId, string? postedBy)
        {
            // ================================
            // 0) Transaction (ثابت مشروع)
            // ================================
            // علشان نضمن: يا كل شيء يتم، يا كل شيء يتراجع
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
                // 2) منع الترحيل لو الفاتورة مقفولة (IsPosted = true)
                // ================================
                // الفكرة: الترحيل يقفل الفاتورة، فلازم "زر الفتح" هو اللي يحوّلها false للتعديل
                if (invoice.IsPosted)
                    throw new Exception("هذه الفاتورة مترحّلة بالفعل. افتح الفاتورة أولاً قبل إعادة الترحيل.");

                // ================================
                // 3) تأكد أن المورد مرتبط بحساب محاسبي
                // ================================
                if (invoice.Customer == null)
                    throw new Exception("بيانات المورد غير محمّلة.");

                if (invoice.Customer.AccountId == null || invoice.Customer.AccountId <= 0)
                    throw new Exception("هذا المورد ليس مرتبطًا بحساب محاسبي داخل شجرة الحسابات.");

                // ================================
                // 4) تحديد حساب المخزن (ثابت = 1105)
                // ================================
                // أنت قررت توحيد حساب المخزون كله في حساب واحد (1105).
                int inventoryAccountId = await ResolveAccountIdByCodeAsync("1105"); // متغير: AccountId لحساب المخزن

                // ================================
                // 5) قيم مشتركة
                // ================================
                var now = DateTime.UtcNow;         // متغير: وقت التنفيذ الحالي (UTC)
                decimal newAmount = invoice.NetTotal; // متغير: صافي الفاتورة (بعد أي تعديل)
                string voucherNo = invoice.PIId.ToString(); // متغير: رقم الفاتورة كنص

                // ================================
                // 6) تحديد رقم المرحلة (Stage)
                // ================================
                // نعدّ مرات الترحيل السابقة عبر "سطر المدين رقم 1" فقط
                // (لأنه ثابت في كل ترحيل جديد)
                int previousPostsCount = await _db.LedgerEntries
                    .Where(e =>
                        e.SourceType == LedgerSourceType.PurchaseInvoice &&
                        e.SourceId == invoice.PIId &&
                        e.LineNo == 1) // سطر المدين الأساسي في كل مرحلة
                    .CountAsync();

                int stage = previousPostsCount + 1; // المرحلة الجديدة

                // ================================
                // 7) لو فيه ترحيل سابق (يعني invoice اتفتحت واتعدلت قبل كده)
                // ================================
                // هنا هنجيب آخر "مرحلة" اتترحلت ونعمل لها عكس قبل ما نضيف الجديد
                var lastPostedDebit = await _db.LedgerEntries
                    .Where(e =>
                        e.SourceType == LedgerSourceType.PurchaseInvoice &&
                        e.SourceId == invoice.PIId &&
                        e.LineNo == 1) // آخر سطر مدين أساسي
                    .OrderByDescending(e => e.CreatedAt)
                    .FirstOrDefaultAsync();

                if (lastPostedDebit != null)
                {
                    // متغير: وقت آخر ترحيل سابق (علشان نجيب سطر الدائن المقابل)
                    var lastPostTime = lastPostedDebit.CreatedAt;

                    // سطر الدائن المقابل (LineNo = 2) لنفس وقت الترحيل
                    var lastPostedCredit = await _db.LedgerEntries
                        .Where(e =>
                            e.SourceType == LedgerSourceType.PurchaseInvoice &&
                            e.SourceId == invoice.PIId &&
                            e.LineNo == 2 &&
                            e.CreatedAt == lastPostTime)
                        .FirstOrDefaultAsync();

                    // متغير: قيمة المرحلة السابقة (المبلغ القديم)
                    decimal oldAmount = lastPostedDebit.Debit;

                    // ================================
                    // 7.1) عمل قيود عكسية للمرحلة السابقة
                    // ================================
                    // عكس المدين: يصبح دائن بنفس المبلغ
                    var reverseDebit = new LedgerEntry
                    {
                        EntryDate = invoice.PIDate, // نثبتها بتاريخ الفاتورة (مش وقت الفتح)
                        SourceType = LedgerSourceType.PurchaseInvoice,
                        VoucherNo = voucherNo,
                        SourceId = invoice.PIId,

                        LineNo = 9001, // رقم سطر عالي حتى لا يتلخبط مع 1/2
                        AccountId = lastPostedDebit.AccountId,
                        CustomerId = lastPostedDebit.CustomerId,

                        Debit = 0m,
                        Credit = oldAmount,

                        Description = $"عكس ترحيل فاتورة مشتريات رقم {invoice.PIId} (قبل مرحلة {stage})",
                        CreatedAt = now
                    };

                    // عكس الدائن: يصبح مدين بنفس المبلغ
                    if (lastPostedCredit != null)
                    {
                        var reverseCredit = new LedgerEntry
                        {
                            EntryDate = invoice.PIDate,
                            SourceType = LedgerSourceType.PurchaseInvoice,
                            VoucherNo = voucherNo,
                            SourceId = invoice.PIId,

                            LineNo = 9002,
                            AccountId = lastPostedCredit.AccountId,
                            CustomerId = lastPostedCredit.CustomerId,

                            Debit = oldAmount,
                            Credit = 0m,

                            Description = $"عكس ترحيل فاتورة مشتريات رقم {invoice.PIId} (قبل مرحلة {stage})",
                            CreatedAt = now
                        };

                        _db.LedgerEntries.Add(reverseCredit);

                        // ================================
                        // 7.2) طرح الرصيد من المورد القديم (لو موجود)
                        // ================================
                        // مهم جدًا: لو المورد اتغير في الفاتورة قبل إعادة الترحيل
                        // لازم نطرح من المورد اللي كان مترحل عليه سابقًا (CustomerId في سطر الدائن)
                        if (lastPostedCredit.CustomerId.HasValue && lastPostedCredit.CustomerId.Value > 0)
                        {
                            var oldSupplier = await _db.Customers
                                .FirstOrDefaultAsync(c => c.CustomerId == lastPostedCredit.CustomerId.Value);

                            if (oldSupplier != null)
                            {
                                oldSupplier.CurrentBalance -= oldAmount;
                            }
                        }
                    }

                    _db.LedgerEntries.Add(reverseDebit);

                    // لو لأي سبب ما لقيناش سطر الدائن، هنحاول طرح من المورد الحالي (كحل احتياطي)
                    if (lastPostedCredit == null)
                    {
                        invoice.Customer.CurrentBalance -= oldAmount;
                    }
                }

                // ================================
                // 8) إنشاء قيود المرحلة الجديدة (القيد المزدوج)
                // ================================
                int supplierAccountId = invoice.Customer.AccountId.Value; // حساب المورد الحالي

                // (1) مدين: المخزن (1105)
                var debitRow = new LedgerEntry
                {
                    EntryDate = invoice.PIDate,                         // تاريخ القيد = تاريخ الفاتورة
                    SourceType = LedgerSourceType.PurchaseInvoice,
                    VoucherNo = voucherNo,
                    SourceId = invoice.PIId,
                    LineNo = 1,

                    AccountId = inventoryAccountId,
                    CustomerId = null,

                    Debit = newAmount,
                    Credit = 0m,

                    Description = $"ترحيل فاتورة مشتريات رقم {invoice.PIId} (مرحلة {stage})",
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

                    AccountId = supplierAccountId,
                    CustomerId = invoice.CustomerId,

                    Debit = 0m,
                    Credit = newAmount,

                    Description = $"ترحيل فاتورة مشتريات رقم {invoice.PIId} (مرحلة {stage})",
                    CreatedAt = now
                };

                _db.LedgerEntries.Add(debitRow);
                _db.LedgerEntries.Add(creditRow);

                // ================================
                // 9) تحديث حالة الفاتورة (قفل)
                // ================================
                invoice.IsPosted = true;               // تم الترحيل
                invoice.Status = $"مرحلة {stage}";     // نص الحالة: مرحلة 1 / مرحلة 2 / ...
                invoice.PostedAt = now;                // وقت الترحيل
                invoice.PostedBy = postedBy;           // المستخدم

                // ================================
                // 10) تحديث رصيد المورد الحالي
                // ================================
                // بما أن فاتورة مشتريات آجل => المورد له فلوس عندنا => رصيده يزيد
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

        /// <summary>
        /// دالة: تحويل AccountCode (مثل 1105) إلى AccountId (المفتاح الأساسي داخل جدول Accounts).
        /// مهم: LedgerEntries يخزن AccountId وليس AccountCode.
        /// </summary>
        private async Task<int> ResolveAccountIdByCodeAsync(string accountCode)
        {
            int accountId = await _db.Accounts
                .Where(a => a.AccountCode == accountCode)
                .Select(a => a.AccountId)
                .FirstOrDefaultAsync();

            if (accountId > 0) return accountId;

            throw new Exception($"لم يتم العثور على حساب بالكود ({accountCode}) داخل شجرة الحسابات. برجاء إضافته.");
        }
    }
}
