using System;
using System.Linq;                      // أوامر LINQ: Where / Sum / ToList
using System.Threading.Tasks;           // async / Task
using ERP.Data;                         // سياق قاعدة البيانات AppDbContext
using ERP.Models;                       // الموديلات
using Microsoft.EntityFrameworkCore;    // أوامر EF Core: FirstOrDefaultAsync / ToListAsync

namespace ERP.Services
{
    /// <summary>
    /// خدمة مركزية لإعادة حساب إجماليات المستندات من جداول السطور (Lines)
    /// إلى جداول الهيدر (Headers).
    ///
    /// المستندات المدعومة حالياً:
    /// 1) طلب الشراء        PurchaseRequest      + PRLine
    /// 2) فاتورة الشراء     PurchaseInvoice      + PILine
    /// 3) مرتجع الشراء      PurchaseReturn       + PurchaseReturnLine
    /// 4) أمر البيع          SalesOrder           + SOLine
    /// 5) فاتورة البيع       SalesInvoice         + SalesInvoiceLine
    /// 6) مرتجع البيع        SalesReturn          + SalesReturnLine
    /// </summary>
    public class DocumentTotalsService
    {
        // متغير: كائن الاتصال بقاعدة البيانات
        private readonly AppDbContext _context;

        /// <summary>
        /// الكونستركتور: يستقبل AppDbContext من الـ DI.
        /// </summary>
        public DocumentTotalsService(AppDbContext context)
        {
            _context = context;
        }

        // ============================================================
        // 1) طلب الشراء PurchaseRequest + PRLines
        // ============================================================

        /// <summary>
        /// يعيد حساب إجماليات طلب الشراء:
        /// - TotalQtyRequested      = مجموع الكميات فى السطور
        /// - ExpectedItemsTotal     = مجموع (الكمية × التكلفة المتوقعة)
        /// </summary>
        public async Task RecalcPurchaseRequestTotalsAsync(int prId)
        {
            // نجيب هيدر طلب الشراء
            var header = await _context.PurchaseRequests
                .FirstOrDefaultAsync(r => r.PRId == prId);

            if (header == null)
                return;    // لو الطلب مش موجود نخرج بهدوء

            // نجيب سطور الطلب
            var lines = await _context.PRLines
                .Where(l => l.PRId == prId)
                .ToListAsync();

            if (!lines.Any())
            {
                // لو مفيش سطور نخلى الإجماليات بصفر
                header.TotalQtyRequested = 0;
                header.ExpectedItemsTotal = 0m;
            }
            else
            {
                // إجمالي الكمية المطلوبة
                var totalQty = lines.Sum(l => l.QtyRequested);

                // إجمالي التكلفة المتوقعة = مجموع (الكمية × سعر الجمهور × (1 - نسبة الخصم))
                // هذا يطابق حساب totalAfterDiscount في صفحة Show.cshtml
                var totalExpected = lines.Sum(l => 
                    l.QtyRequested * l.PriceRetail * (1m - (l.PurchaseDiscountPct / 100m))
                );

                header.TotalQtyRequested = totalQty;
                header.ExpectedItemsTotal = totalExpected;
            }

            header.UpdatedAt = DateTime.UtcNow;   // آخر تعديل
            await _context.SaveChangesAsync();    // حفظ
        }

        // ============================================================
        // 2) فاتورة الشراء PurchaseInvoice + PILines
        // ============================================================

        /// <summary>
        /// يعيد حساب إجماليات فاتورة الشراء (حسب واجهتك الحالية):
        /// - ItemsTotal      = مجموع (الكمية × سعر الجمهور PriceRetail)
        /// - DiscountTotal   = مجموع (الكمية × سعر الجمهور × خصم الشراء %)
        /// - TaxTotal        = قيمة الضريبة المخزنة في الهيدر (لا يتم تصفيرها هنا)
        /// - NetTotal        = ItemsTotal - DiscountTotal + TaxTotal
        /// </summary>
        public async Task RecalcPurchaseInvoiceTotalsAsync(int piId)
        {
            // =========================
            // 1) الهيدر
            // =========================
            var header = await _context.PurchaseInvoices
                .FirstOrDefaultAsync(p => p.PIId == piId);

            if (header == null)
                return;

            // =========================
            // 2) سطور الفاتورة (Tracking)
            // =========================
            var lines = await _context.PILines
                .Where(l => l.PIId == piId)
                .ToListAsync();

            // متغير: الضريبة الحالية (لا نصفرها)
            var taxTotal = header.TaxTotal;

            // =========================
            // 3) لو مفيش سطور
            // =========================
            if (!lines.Any())
            {
                header.ItemsTotal = 0m;       // إجمالي الجمهور قبل الخصم
                header.DiscountTotal = 0m;    // إجمالي الخصم
                header.NetTotal = taxTotal;   // الصافي = الضريبة فقط لو موجودة
                header.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return;
            }

            // =========================
            // 4) تحديث تكلفة الوحدة UnitCost لكل سطر
            // تكلفة الوحدة = قيمة السطر / الكمية
            // =========================
            foreach (var l in lines)
            {
                // متغير: الكمية
                var qty = l.Qty;

                // متغير: الخصم بين 0 و 100
                var disc = l.PurchaseDiscountPct;
                if (disc < 0) disc = 0;
                if (disc > 100) disc = 100;

                // متغير: قيمة السطر بعد الخصم
                var lineValue = qty * l.PriceRetail * (1m - (disc / 100m));

                // متغير: تكلفة الوحدة = قيمة السطر / الكمية
                // (لو الكمية 0 نكتب 0 علشان ما يحصلش قسمة على صفر)
                l.UnitCost = (qty > 0) ? (lineValue / qty) : 0m;
            }

            // =========================
            // 5) إجماليات الهيدر
            // =========================

            // إجمالي سعر الجمهور = مجموع (Qty × PriceRetail)
            var itemsTotal = lines.Sum(l => l.Qty * l.PriceRetail);

            // إجمالي خصم الشراء = مجموع (Qty × PriceRetail × Disc%)
            var discountTotal = lines.Sum(l =>
                l.Qty * l.PriceRetail * (l.PurchaseDiscountPct / 100m));

            // الصافي = بعد الخصم + الضريبة
            var netTotal = itemsTotal - discountTotal + taxTotal;

            header.ItemsTotal = itemsTotal;
            header.DiscountTotal = discountTotal;
            header.NetTotal = netTotal;

            header.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }









        // ============================================================
        // 3) مرتجع الشراء PurchaseReturn + PurchaseReturnLines
        // ============================================================

        /// <summary>
        /// يعيد حساب إجماليات مرتجع الشراء:
        /// - ItemsTotal      = مجموع (الكمية × تكلفة الوحدة)
        /// - DiscountTotal   = مجموع خصم الشراء على السطور
        /// - TaxTotal        = حالياً 0
        /// - NetTotal        = ItemsTotal - DiscountTotal + TaxTotal
        /// </summary>
        public async Task RecalcPurchaseReturnTotalsAsync(int pretId)
        {
            var header = await _context.PurchaseReturns
                .FirstOrDefaultAsync(r => r.PRetId == pretId);

            if (header == null)
                return;

            var lines = await _context.PurchaseReturnLines
                .Where(l => l.PRetId == pretId)
                .ToListAsync();

            if (!lines.Any())
            {
                header.ItemsTotal = 0m;
                header.DiscountTotal = 0m;
                header.TaxTotal = 0m;
                header.NetTotal = 0m;
            }
            else
            {
                var itemsTotal = lines.Sum(l => l.Qty * l.UnitCost);

                var discountTotal = lines.Sum(l =>
                    l.Qty * l.UnitCost * (l.PurchaseDiscountPct / 100m));

                var taxTotal = 0m; // لو أضفنا ضريبة للمرتجع نعدلها هنا
                var netTotal = itemsTotal - discountTotal + taxTotal;

                header.ItemsTotal = itemsTotal;
                header.DiscountTotal = discountTotal;
                header.TaxTotal = taxTotal;
                header.NetTotal = netTotal;
            }

            header.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        // ============================================================
        // 4) أمر البيع SalesOrder + SOLines
        // ============================================================

        /// <summary>
        /// يعيد حساب إجماليات أمر البيع (لو مضاف فى الهيدر):
        /// - TotalQtyRequested      = مجموع الكميات المطلوبة
        /// - ExpectedItemsTotal     = مجموع (الكمية × السعر/التكلفة المتوقعة)
        /// </summary>
        public async Task RecalcSalesOrderTotalsAsync(int soId)
        {
            var header = await _context.SalesOrders
                .FirstOrDefaultAsync(o => o.SOId == soId);

            if (header == null)
                return;

            var lines = await _context.SOLines
                .Where(l => l.SOId == soId)
                .ToListAsync();

            if (!lines.Any())
            {
                header.TotalQtyRequested = 0;
                header.ExpectedItemsTotal = 0m;
            }
            else
            {
                var totalQty = lines.Sum(l => l.QtyRequested);
                var totalExpected = lines.Sum(l => l.QtyRequested * l.ExpectedUnitPrice);

                header.TotalQtyRequested = totalQty;
                header.ExpectedItemsTotal = totalExpected;
            }

            header.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        // ============================================================
        // 5) فاتورة البيع SalesInvoice + SalesInvoiceLines
        // ============================================================

        /// <summary>
        /// يعيد حساب إجماليات فاتورة البيع:
        /// - TotalBeforeDiscount           = مجموع (Qty × PriceRetail) قبل أي خصم
        /// - TotalAfterDiscountBeforeTax   = مجموع LineTotalAfterDiscount - خصم الهيدر
        /// - TaxAmount                     = مجموع TaxValue
        /// - NetTotal                      = TotalAfterDiscountBeforeTax + TaxAmount
        /// </summary>
        public async Task RecalcSalesInvoiceTotalsAsync(int siId)
        {
            var header = await _context.SalesInvoices
                .FirstOrDefaultAsync(s => s.SIId == siId);

            if (header == null)
                return;

            var lines = await _context.SalesInvoiceLines
                .Where(l => l.SIId == siId)
                .ToListAsync();

            if (!lines.Any())
            {
                header.TotalBeforeDiscount = 0m;
                header.TotalAfterDiscountBeforeTax = 0m;
                header.TaxAmount = 0m;
                header.NetTotal = 0m;
            }
            else
            {
                var totalBefore = lines.Sum(l => l.Qty * l.PriceRetail);
                var linesAfterDiscount = lines.Sum(l => l.LineTotalAfterDiscount);
                var headerDisc = header.HeaderDiscountValue;

                var totalAfterBeforeTax = linesAfterDiscount - headerDisc;
                if (totalAfterBeforeTax < 0) totalAfterBeforeTax = 0m;

                var taxAmount = lines.Sum(l => l.TaxValue);
                var netTotal = totalAfterBeforeTax + taxAmount;

                header.TotalBeforeDiscount = totalBefore;
                header.TotalAfterDiscountBeforeTax = totalAfterBeforeTax;
                header.TaxAmount = taxAmount;
                header.NetTotal = netTotal;
            }

            header.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        // ============================================================
        // 6) مرتجع البيع SalesReturn + SalesReturnLines
        // ============================================================

        /// <summary>
        /// يعيد حساب إجماليات مرتجع البيع:
        /// - TotalBeforeDiscount           = مجموع (Qty × PriceRetail)
        /// - TotalAfterDiscountBeforeTax   = مجموع LineTotalAfterDiscount - خصم الهيدر
        /// - TaxAmount                     = مجموع TaxValue
        /// - NetTotal                      = TotalAfterDiscountBeforeTax + TaxAmount
        /// </summary>
        public async Task RecalcSalesReturnTotalsAsync(int srId)
        {
            var header = await _context.SalesReturns
                .FirstOrDefaultAsync(r => r.SRId == srId);

            if (header == null)
                return;

            var lines = await _context.SalesReturnLines
                .Where(l => l.SRId == srId)
                .ToListAsync();

            if (!lines.Any())
            {
                header.TotalBeforeDiscount = 0m;
                header.TotalAfterDiscountBeforeTax = 0m;
                header.TaxAmount = 0m;
                header.NetTotal = 0m;
            }
            else
            {
                var totalBefore = lines.Sum(l => l.Qty * l.PriceRetail);
                var linesAfterDiscount = lines.Sum(l => l.LineTotalAfterDiscount);
                var headerDisc = header.HeaderDiscountValue;

                var totalAfterBeforeTax = linesAfterDiscount - headerDisc;
                if (totalAfterBeforeTax < 0) totalAfterBeforeTax = 0m;

                var taxAmount = lines.Sum(l => l.TaxValue);
                var netTotal = totalAfterBeforeTax + taxAmount;

                header.TotalBeforeDiscount = totalBefore;
                header.TotalAfterDiscountBeforeTax = totalAfterBeforeTax;
                header.TaxAmount = taxAmount;
                header.NetTotal = netTotal;
            }

            header.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }
}
