using ERP.Data;                 // AppDbContext
using ERP.Models;               // PurchaseInvoice, LedgerEntry, LedgerSourceType
using Microsoft.EntityFrameworkCore;

namespace ERP.Services
{
    /// <summary>
    /// خدمة الترحيل المحاسبي (دفتر الأستاذ).
    /// الفكرة: كل مستند "يُرحّل" = يُنشئ صفّين أو أكثر داخل LedgerEntries.
    /// كل صف يمثل "سطر قيد" لحساب واحد (Debit أو Credit).
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
        /// </summary>
        public async Task PostPurchaseInvoiceAsync(int purchaseInvoiceId, string? postedBy)
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
            // 2) منع الترحيل لو مترحّلة بالفعل
            // ================================
            if (invoice.IsPosted)
                throw new Exception("هذه الفاتورة تم ترحيلها مسبقًا.");

            // ================================
            // 3) تأكد أن المورد مرتبط بحساب محاسبي
            // ================================
            if (invoice.Customer == null)
                throw new Exception("بيانات المورد غير محمّلة.");

            // ملاحظة: Customer.AccountId عندك nullable، فلازم نتأكد
            if (invoice.Customer.AccountId == null || invoice.Customer.AccountId <= 0)
                throw new Exception("هذا المورد ليس مرتبطًا بحساب محاسبي داخل شجرة الحسابات.");

            // ================================
            // 4) تحديد حساب المخزن (ثابت = 1105)
            // ================================
            // ملاحظة مهمة:
            // أنت قررت توحيد حساب المخزون كله في حساب واحد (1105).
            // لذلك الترحيل المحاسبي دائمًا يذهب إلى هذا الحساب فقط، بدون النظر للمخزن (دواء/إكسسوار).
            int inventoryAccountId = await ResolveAccountIdByCodeAsync("1105"); // متغير: AccountId لحساب المخزن

            // ================================
            // 5) تجهيز قيم مشتركة
            // ================================
            var now = DateTime.UtcNow;                  // متغير: وقت التنفيذ الحالي (UTC)
            decimal amount = invoice.NetTotal;          // متغير: صافي الفاتورة
            int supplierAccountId = invoice.Customer.AccountId.Value; // متغير: AccountId للمورد

            // رقم المستند الظاهر في الدفتر (VoucherNo) كنص
            string voucherNo = invoice.PIId.ToString(); // متغير: رقم الفاتورة كنص

            // ================================
            // 6) تأمين إضافي: منع تكرار القيود لنفس الفاتورة
            // ================================
            bool alreadyHasLedger = await _db.LedgerEntries.AnyAsync(e =>
                e.SourceType == LedgerSourceType.PurchaseInvoice &&
                e.SourceId == invoice.PIId);

            if (alreadyHasLedger)
                throw new Exception("تم العثور على قيود سابقة لهذه الفاتورة داخل دفتر الأستاذ.");

            // ================================
            // 7) إنشاء صفّين LedgerEntry (القيد المزدوج)
            // ================================

            // (1) مدين: المخزن (1105)
            var debitRow = new LedgerEntry
            {
                EntryDate = invoice.PIDate,                         // متغير: تاريخ القيد = تاريخ الفاتورة
                SourceType = LedgerSourceType.PurchaseInvoice,      // متغير: نوع المصدر
                VoucherNo = voucherNo,                              // متغير: رقم المستند للمستخدم
                SourceId = invoice.PIId,                            // متغير: رقم الفاتورة كمصدر
                LineNo = 1,                                         // متغير: ترتيب السطر داخل القيد

                AccountId = inventoryAccountId,                     // ✅ حساب المخزن (1105) -> AccountId
                CustomerId = null,                                  // هذا السطر ليس له طرف (اختياري)

                Debit = amount,                                     // ✅ مدين
                Credit = 0m,                                        // ❌ ليس دائن

                Description = $"ترحيل فاتورة مشتريات رقم {invoice.PIId}", // متغير: البيان
                CreatedAt = now                                     // متغير: وقت إنشاء السطر
            };

            // (2) دائن: المورد (له فلوس)
            var creditRow = new LedgerEntry
            {
                EntryDate = invoice.PIDate,
                SourceType = LedgerSourceType.PurchaseInvoice,
                VoucherNo = voucherNo,
                SourceId = invoice.PIId,
                LineNo = 2,

                AccountId = supplierAccountId,                      // ✅ حساب المورد (AccountId)
                CustomerId = invoice.CustomerId,                    // متغير: الطرف نفسه

                Debit = 0m,                                         // ❌ ليس مدين
                Credit = amount,                                    // ✅ دائن (له فلوس)

                Description = $"ترحيل فاتورة مشتريات رقم {invoice.PIId}",
                CreatedAt = now
            };

            // ================================
            // 8) حفظ القيد + تحديث حالة الفاتورة
            // ================================
            _db.LedgerEntries.Add(debitRow);
            _db.LedgerEntries.Add(creditRow);

            // تحديث حالة الترحيل على الفاتورة
            invoice.IsPosted = true;               // متغير: تم الترحيل
            invoice.Status = "Posted";             // متغير: الحالة النصية
            invoice.PostedAt = now;                // متغير: وقت الترحيل
            invoice.PostedBy = postedBy;           // متغير: اسم المستخدم الذي رحّل

            // ================================
            // 9) تحديث رصيد المورد (اختياري)
            // ================================
            // أنت قلت: "دائن يعني له فلوس" => المورد رصيده يزيد عندنا
            invoice.Customer.CurrentBalance += amount;

            // حفظ كل شيء
            await _db.SaveChangesAsync();
        }

        /// <summary>
        /// دالة: تحويل AccountCode (مثل 1105) إلى AccountId (المفتاح الأساسي داخل جدول Accounts).
        /// مهم: LedgerEntries يخزن AccountId وليس AccountCode.
        /// </summary>
        /// <param name="accountCode">متغير: كود الحساب المحاسبي (مثل 1105)</param>
        private async Task<int> ResolveAccountIdByCodeAsync(string accountCode)
        {
            // متغير: AccountId الناتج من البحث
            int accountId = await _db.Accounts
                .Where(a => a.AccountCode == accountCode) // ✅ بحث بالكود فقط
                .Select(a => a.AccountId)
                .FirstOrDefaultAsync();

            if (accountId > 0) return accountId;

            // لو مش موجود ندي رسالة واضحة
            throw new Exception($"لم يتم العثور على حساب بالكود ({accountCode}) داخل شجرة الحسابات. برجاء إضافته.");
        }
    }
}
