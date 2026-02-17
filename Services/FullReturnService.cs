using ERP.Data;
using ERP.Models;
using Microsoft.EntityFrameworkCore;

namespace ERP.Services
{
    /// <summary>
    /// خدمة إنشاء مرتجع فاتورة بالكامل (مبيعات أو مشتريات) مع الترحيل التلقائي.
    /// تنشئ المرتجع، تنسخ كل السطور، تحدث المخزون، تحسب الإجماليات، وترحّل الحسابات.
    /// </summary>
    public interface IFullReturnService
    {
        /// <summary>إنشاء مرتجع بيع كامل من فاتورة مبيعات + ترحيل. يرجع أيضاً هل تم إعادة ترحيل الفاتورة وحالتها الجديدة.</summary>
        Task<(int SalesReturnId, string Message, bool InvoiceReposted, string? InvoiceStatus)> CreateFullSalesReturnFromInvoiceAsync(int salesInvoiceId, string? postedBy);

        /// <summary>إنشاء مرتجع شراء كامل من فاتورة مشتريات + ترحيل. يرجع أيضاً هل تم إعادة ترحيل الفاتورة وحالتها الجديدة.</summary>
        Task<(int PurchaseReturnId, string Message, bool InvoiceReposted, string? InvoiceStatus)> CreateFullPurchaseReturnFromInvoiceAsync(int purchaseInvoiceId, string? postedBy);
    }

    public class FullReturnService : IFullReturnService
    {
        private readonly AppDbContext _db;
        private readonly DocumentTotalsService _docTotals;
        private readonly ILedgerPostingService _ledgerPosting;

        public FullReturnService(AppDbContext db, DocumentTotalsService docTotals, ILedgerPostingService ledgerPosting)
        {
            _db = db;
            _docTotals = docTotals;
            _ledgerPosting = ledgerPosting;
        }

        public async Task<(int SalesReturnId, string Message, bool InvoiceReposted, string? InvoiceStatus)> CreateFullSalesReturnFromInvoiceAsync(int salesInvoiceId, string? postedBy)
        {
            var invoice = await _db.SalesInvoices
                .Include(si => si.Customer)
                .Include(si => si.Lines)
                .FirstOrDefaultAsync(si => si.SIId == salesInvoiceId);

            if (invoice == null)
                throw new Exception("فاتورة المبيعات غير موجودة.");
            // منع المرتجعات المكررة: التحقق من وجود مرتجع سابق من نفس الفاتورة
            var existingReturn = await _db.SalesReturns
                .FirstOrDefaultAsync(sr => sr.SalesInvoiceId == salesInvoiceId);
            if (existingReturn != null)
                throw new Exception($"تم إنشاء مرتجع بيع رقم {existingReturn.SRId} من هذه الفاتورة مسبقاً. لا يمكن إنشاء مرتجع آخر.");
            if (invoice.Lines == null || !invoice.Lines.Any())
                throw new Exception("فاتورة المبيعات لا تحتوي على أصناف.");

            var now = DateTime.UtcNow;
                var today = DateTime.Today;

                var ret = new SalesReturn
                {
                    SRDate = today,
                    SRTime = now.TimeOfDay,
                    CustomerId = invoice.CustomerId,
                    WarehouseId = invoice.WarehouseId,
                    SalesInvoiceId = salesInvoiceId,
                    Status = "Draft",
                    IsPosted = false,
                    CreatedBy = postedBy ?? "SYSTEM",
                    CreatedAt = now
                };
                _db.SalesReturns.Add(ret);
                await _db.SaveChangesAsync();

                int lineNo = 0;
                foreach (var invLine in invoice.Lines.OrderBy(l => l.LineNo))
                {
                    lineNo++;
                    var batchNo = string.IsNullOrWhiteSpace(invLine.BatchNo) ? null : invLine.BatchNo.Trim();
                    var exp = invLine.Expiry?.Date;
                    var disc1 = Math.Max(0, Math.Min(100, invLine.Disc1Percent));
                    var unitPrice = Math.Max(0, invLine.PriceRetail);
                    var totalBefore = invLine.Qty * unitPrice;
                    var discVal = totalBefore * (disc1 / 100m);
                    var totalAfter = totalBefore - discVal;
                    var netLine = totalAfter + (invLine.TaxValue);

                    var line = new SalesReturnLine
                    {
                        SRId = ret.SRId,
                        LineNo = lineNo,
                        ProdId = invLine.ProdId,
                        Qty = invLine.Qty,
                        PriceRetail = unitPrice,
                        Disc1Percent = disc1,
                        Disc2Percent = invLine.Disc2Percent,
                        Disc3Percent = invLine.Disc3Percent,
                        DiscountValue = discVal,
                        UnitSalePrice = invLine.UnitSalePrice,
                        LineTotalAfterDiscount = totalAfter,
                        TaxPercent = invLine.TaxPercent,
                        TaxValue = invLine.TaxValue,
                        LineNetTotal = netLine,
                        BatchNo = batchNo,
                        Expiry = exp,
                        SalesInvoiceId = salesInvoiceId,
                        SalesInvoiceLineNo = invLine.LineNo
                    };
                    _db.SalesReturnLines.Add(line);
                    await _db.SaveChangesAsync();

                    var avgCost = await GetAverageCostAsync(invLine.ProdId, ret.WarehouseId);
                    _db.StockLedger.Add(new StockLedger
                    {
                        TranDate = now,
                        WarehouseId = ret.WarehouseId,
                        ProdId = invLine.ProdId,
                        BatchNo = batchNo ?? "",
                        Expiry = exp,
                        QtyIn = invLine.Qty,
                        QtyOut = 0,
                        UnitCost = avgCost,
                        RemainingQty = invLine.Qty,
                        SourceType = "SalesReturn",
                        SourceId = ret.SRId,
                        SourceLine = lineNo,
                        Note = "Sales Return Line (Full)"
                    });
                    await _db.SaveChangesAsync();

                    if (!string.IsNullOrWhiteSpace(batchNo) && exp.HasValue)
                    {
                        var sb = await _db.StockBatches.FirstOrDefaultAsync(b =>
                            b.WarehouseId == ret.WarehouseId && b.ProdId == invLine.ProdId &&
                            b.BatchNo == batchNo && b.Expiry.HasValue && b.Expiry.Value.Date == exp.Value.Date);
                        if (sb != null)
                        {
                            sb.QtyOnHand += invLine.Qty;
                            sb.UpdatedAt = now;
                            sb.Note = $"SR:{ret.SRId} Line:{lineNo} (+{invLine.Qty})";
                        }
                        else
                        {
                            _db.StockBatches.Add(new StockBatch
                            {
                                WarehouseId = ret.WarehouseId,
                                ProdId = invLine.ProdId,
                                BatchNo = batchNo,
                                Expiry = exp.Value,
                                QtyOnHand = invLine.Qty,
                                UpdatedAt = now,
                                Note = $"SR:{ret.SRId} Line:{lineNo}"
                            });
                        }
                        await _db.SaveChangesAsync();
                    }
                }

                await _docTotals.RecalcSalesReturnTotalsAsync(ret.SRId);
                await _db.SaveChangesAsync();

                await _ledgerPosting.PostSalesReturnAsync(ret.SRId, postedBy);

            // إعادة ترحيل الفاتورة الأصلية إذا كانت مفتوحة (مفتوحة للتعديل) وإغلاقها
            bool invoiceReposted = false;
            string? invoiceStatus = null;
            if (!invoice.IsPosted)
            {
                await _ledgerPosting.PostSalesInvoiceAsync(salesInvoiceId, postedBy);
                invoiceReposted = true;
                var updated = await _db.SalesInvoices.AsNoTracking().FirstOrDefaultAsync(si => si.SIId == salesInvoiceId);
                invoiceStatus = updated?.Status;
            }

            return (ret.SRId, $"تم إنشاء مرتجع بيع رقم {ret.SRId} وترحيله بنجاح.", invoiceReposted, invoiceStatus);
        }

        public async Task<(int PurchaseReturnId, string Message, bool InvoiceReposted, string? InvoiceStatus)> CreateFullPurchaseReturnFromInvoiceAsync(int purchaseInvoiceId, string? postedBy)
        {
            var invoice = await _db.PurchaseInvoices
                .Include(pi => pi.Customer)
                .Include(pi => pi.Lines)
                .FirstOrDefaultAsync(pi => pi.PIId == purchaseInvoiceId);

            if (invoice == null)
                throw new Exception("فاتورة المشتريات غير موجودة.");
            // منع المرتجعات المكررة: التحقق من وجود مرتجع سابق من نفس الفاتورة
            var existingReturn = await _db.PurchaseReturns
                .FirstOrDefaultAsync(pr => pr.RefPIId == purchaseInvoiceId);
            if (existingReturn != null)
                throw new Exception($"تم إنشاء مرتجع شراء رقم {existingReturn.PRetId} من هذه الفاتورة مسبقاً. لا يمكن إنشاء مرتجع آخر.");
            if (invoice.Lines == null || !invoice.Lines.Any())
                throw new Exception("فاتورة المشتريات لا تحتوي على أصناف.");

            // إعادة ترحيل الفاتورة أولاً إذا كانت مفتوحة (ترحيل المرتجع يتطلب أن تكون الفاتورة مرحّلة)
            bool invoiceReposted = false;
            string? invoiceStatus = null;
            if (!invoice.IsPosted)
            {
                await _ledgerPosting.PostPurchaseInvoiceAsync(purchaseInvoiceId, postedBy);
                invoiceReposted = true;
                var updatedInv = await _db.PurchaseInvoices.AsNoTracking().FirstOrDefaultAsync(pi => pi.PIId == purchaseInvoiceId);
                invoiceStatus = updatedInv?.Status;
                invoice = await _db.PurchaseInvoices.Include(pi => pi.Lines).FirstOrDefaultAsync(pi => pi.PIId == purchaseInvoiceId)
                    ?? throw new Exception("فاتورة المشتريات غير موجودة.");
            }

            var now = DateTime.UtcNow;
                var today = DateTime.Today;

                var ret = new PurchaseReturn
                {
                    PRetDate = today,
                    CustomerId = invoice.CustomerId,
                    WarehouseId = invoice.WarehouseId,
                    RefPIId = purchaseInvoiceId,
                    Status = "Draft",
                    IsPosted = false,
                    CreatedBy = postedBy ?? "SYSTEM",
                    CreatedAt = now
                };
                _db.PurchaseReturns.Add(ret);
                await _db.SaveChangesAsync();

                int lineNo = 0;
                foreach (var invLine in invoice.Lines.OrderBy(l => l.LineNo))
                {
                    lineNo++;
                    var batchNo = string.IsNullOrWhiteSpace(invLine.BatchNo) ? null : invLine.BatchNo.Trim();
                    var exp = invLine.Expiry?.Date;
                    var unitCost = Math.Max(0, invLine.UnitCost);
                    var discPct = Math.Max(0, Math.Min(100, invLine.PurchaseDiscountPct));

                    if (!string.IsNullOrWhiteSpace(batchNo) && exp.HasValue)
                    {
                        var sb = await _db.StockBatches.FirstOrDefaultAsync(b =>
                            b.WarehouseId == ret.WarehouseId && b.ProdId == invLine.ProdId &&
                            b.BatchNo == batchNo && b.Expiry.HasValue && b.Expiry.Value.Date == exp.Value.Date);
                        if (sb == null || sb.QtyOnHand < invLine.Qty)
                            throw new Exception($"التشغيلة غير متوفرة أو الكمية غير كافية للصنف في سطر {lineNo}. تأكد أن المخزن صحيح.");
                    }

                    var line = new PurchaseReturnLine
                    {
                        PRetId = ret.PRetId,
                        LineNo = lineNo,
                        ProdId = invLine.ProdId,
                        Qty = invLine.Qty,
                        UnitCost = unitCost,
                        PurchaseDiscountPct = discPct,
                        PriceRetail = Math.Max(0, invLine.PriceRetail),
                        BatchNo = batchNo,
                        Expiry = exp,
                        RefPIId = purchaseInvoiceId,
                        RefPILineNo = invLine.LineNo
                    };
                    _db.PurchaseReturnLines.Add(line);
                    _db.Entry(line).Property("ProductProdId").CurrentValue = invLine.ProdId;
                    _db.Entry(line).Property("PurchaseReturnPRetId").CurrentValue = ret.PRetId;
                    await _db.SaveChangesAsync();

                    _db.StockLedger.Add(new StockLedger
                    {
                        TranDate = now,
                        WarehouseId = ret.WarehouseId,
                        ProdId = invLine.ProdId,
                        BatchNo = batchNo ?? "",
                        Expiry = exp,
                        QtyIn = 0,
                        QtyOut = invLine.Qty,
                        UnitCost = unitCost,
                        RemainingQty = null,
                        SourceType = "PurchaseReturn",
                        SourceId = ret.PRetId,
                        SourceLine = lineNo,
                        Note = "Purchase Return Line (Full)"
                    });
                    await _db.SaveChangesAsync();

                    if (!string.IsNullOrWhiteSpace(batchNo) && exp.HasValue)
                    {
                        var sb = await _db.StockBatches.FirstOrDefaultAsync(b =>
                            b.WarehouseId == ret.WarehouseId && b.ProdId == invLine.ProdId &&
                            b.BatchNo == batchNo && b.Expiry.HasValue && b.Expiry.Value.Date == exp.Value.Date);
                        if (sb != null)
                        {
                            sb.QtyOnHand -= invLine.Qty;
                            sb.UpdatedAt = now;
                            sb.Note = $"PR:{ret.PRetId} Line:{lineNo} (-{invLine.Qty})";
                        }
                        await _db.SaveChangesAsync();
                    }
                }

                await _docTotals.RecalcPurchaseReturnTotalsAsync(ret.PRetId);
                await _db.SaveChangesAsync();

                await _ledgerPosting.PostPurchaseReturnAsync(ret.PRetId, postedBy);

                return (ret.PRetId, $"تم إنشاء مرتجع شراء رقم {ret.PRetId} وترحيله بنجاح.", invoiceReposted, invoiceStatus);
        }

        private async Task<decimal> GetAverageCostAsync(int prodId, int warehouseId)
        {
            var avg = await _db.StockLedger
                .Where(sl => sl.ProdId == prodId && sl.WarehouseId == warehouseId && sl.QtyIn > 0)
                .AverageAsync(sl => (decimal?)sl.UnitCost);
            return avg ?? 0m;
        }
    }
}
