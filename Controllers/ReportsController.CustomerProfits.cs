using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ERP.Filters;
using ERP.Infrastructure;
using ERP.Models;
using ERP.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Controllers
{
    public partial class ReportsController
    {
        private async Task<List<CustomerProfitReportDto>> BuildCustomerProfitsReportRowsAsync(
            string? mainSearch,
            string? partyCategory,
            int? governorateId,
            DateTime? fromDate,
            DateTime? toDate,
            bool includeZeroQty)
        {
            // =========================================================
            // 4) بناء الاستعلام الأساسي للعملاء
            // =========================================================
            var customersQuery = _context.Customers
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(mainSearch))
            {
                var s = mainSearch.Trim();
                customersQuery = customersQuery.Where(c =>
                    (c.CustomerName != null && c.CustomerName.Contains(s)) ||
                    (c.Phone1 != null && c.Phone1.Contains(s)) ||
                    (c.CustomerId.ToString() == s));
            }

            if (!string.IsNullOrWhiteSpace(partyCategory))
            {
                customersQuery = customersQuery.Where(c => c.PartyCategory == partyCategory);
            }

            if (governorateId.HasValue && governorateId.Value > 0)
            {
                customersQuery = customersQuery.Where(c => c.GovernorateId == governorateId.Value);
            }

            customersQuery = customersQuery.Where(c => c.IsActive == true);

            var customerIds = await customersQuery.Select(c => c.CustomerId).ToListAsync();

            if (customerIds.Count == 0)
            {
                return new List<CustomerProfitReportDto>();
            }

            // =========================================================
            // 5) حساب الربح من البيع — من نفس مصدر الميزانية ليتطابق الرقمان
            // - الإيراد: من SalesInvoices.NetTotal (نفس القيمة المُرحّلة في القيد)
            // - التكلفة: من StockLedger (نفس GetSalesInvoiceCostTotal عند الترحيل)
            // =========================================================
            var salesInvoicesInScope = _context.SalesInvoices
                .AsNoTracking()
                .Where(si =>
                    customerIds.Contains(si.CustomerId) &&
                    si.IsPosted);

            if (fromDate.HasValue)
            {
                var from = fromDate.Value.Date;
                salesInvoicesInScope = salesInvoicesInScope.Where(si => si.SIDate >= from);
            }

            if (toDate.HasValue)
            {
                var to = toDate.Value.Date.AddDays(1);
                salesInvoicesInScope = salesInvoicesInScope.Where(si => si.SIDate < to);
            }

            var salesInvoiceIdsList = await salesInvoicesInScope.Select(si => si.SIId).ToListAsync();

            // إيراد وتعداد من الفواتير (NetTotal = نفس المُرحّل في الميزانية)
            var revenueAndCount = await salesInvoicesInScope
                .GroupBy(si => si.CustomerId)
                .Select(g => new { CustomerId = g.Key, SalesRevenue = g.Sum(si => si.NetTotal), InvoiceCount = g.Count() })
                .ToDictionaryAsync(x => x.CustomerId);

            // تكلفة من StockLedger (نفس منطق الترحيل) ثم تجميع حسب العميل
            var stockSourceTypeSales = "Sales";
            var costPerInvoice = salesInvoiceIdsList.Count > 0
                ? await _context.StockLedger
                    .AsNoTracking()
                    .Where(x =>
                        x.SourceType == stockSourceTypeSales &&
                        salesInvoiceIdsList.Contains(x.SourceId) &&
                        x.QtyOut > 0)
                    .GroupBy(x => x.SourceId)
                    .Select(g => new { SIId = g.Key, Cost = g.Sum(x => x.TotalCost ?? (x.QtyOut * x.UnitCost)) })
                    .ToDictionaryAsync(x => x.SIId, x => x.Cost)
                : new Dictionary<int, decimal>();

            var siToCustomer = await salesInvoicesInScope
                .Select(si => new { si.SIId, si.CustomerId })
                .ToDictionaryAsync(x => x.SIId, x => x.CustomerId);

            Dictionary<int, decimal> salesCostByCustomer = new Dictionary<int, decimal>();
            foreach (var siId in salesInvoiceIdsList)
            {
                if (!siToCustomer.TryGetValue(siId, out int custId)) continue;
                decimal cost = costPerInvoice.TryGetValue(siId, out var c) ? c : 0m;
                if (salesCostByCustomer.ContainsKey(custId))
                    salesCostByCustomer[custId] += cost;
                else
                    salesCostByCustomer[custId] = cost;
            }

            var salesProfitData = revenueAndCount.ToDictionary(
                x => x.Key,
                x => new
                {
                    CustomerId = x.Key,
                    SalesRevenue = x.Value.SalesRevenue,
                    SalesCost = salesCostByCustomer.TryGetValue(x.Key, out var sc) ? sc : 0m,
                    InvoiceCount = x.Value.InvoiceCount
                });

            // =========================================================
            // 6) حساب الربح من الميزانية (LedgerEntries)
            // =========================================================
            var salesRevenueAccount = await _context.Accounts
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.AccountCode == "4100");

            var cogsAccount = await _context.Accounts
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.AccountCode == "5100");

            Dictionary<int, decimal> ledgerRevenueData = new Dictionary<int, decimal>();
            Dictionary<int, decimal> ledgerCostData = new Dictionary<int, decimal>();

            if (salesRevenueAccount != null && cogsAccount != null)
            {
                // الحصول على فواتير المبيعات المرتبطة بالعملاء المحددين (نفس نطاق الفلاتر أعلاه)
                var salesInvoiceIds = salesInvoiceIdsList;

                if (salesInvoiceIds.Any())
                {
                    // تحميل الفواتير دفعة واحدة
                    var invoicesDict = await _context.SalesInvoices
                        .AsNoTracking()
                        .Where(si => salesInvoiceIds.Contains(si.SIId))
                        .Select(si => new { si.SIId, si.CustomerId })
                        .ToDictionaryAsync(x => x.SIId, x => x.CustomerId);

                    // الإيرادات من الميزانية - نستخدم SourceId (رقم الفاتورة) ثم نربطه بالعميل
                    var revenueQuery = _context.LedgerEntries
                        .AsNoTracking()
                        .Where(e =>
                            e.AccountId == salesRevenueAccount.AccountId &&
                            e.SourceType == LedgerSourceType.SalesInvoice &&
                            e.LineNo == 2 &&
                            e.PostVersion > 0 &&
                            e.SourceId.HasValue &&
                            salesInvoiceIds.Contains(e.SourceId.Value) &&
                            !_context.LedgerEntries.Any(rev =>
                                rev.SourceType == LedgerSourceType.SalesInvoice &&
                                rev.SourceId == e.SourceId &&
                                rev.LineNo == 9001));

                    if (fromDate.HasValue)
                    {
                        var from = fromDate.Value.Date;
                        revenueQuery = revenueQuery.Where(e => e.EntryDate >= from);
                    }

                    if (toDate.HasValue)
                    {
                        var to = toDate.Value.Date.AddDays(1);
                        revenueQuery = revenueQuery.Where(e => e.EntryDate < to);
                    }

                    var revenueEntries = await revenueQuery.ToListAsync();

                    // ربط الإيرادات بالعملاء عبر SalesInvoices
                    foreach (var entry in revenueEntries)
                    {
                        if (!entry.SourceId.HasValue) continue;
                        if (!invoicesDict.TryGetValue(entry.SourceId.Value, out int customerId)) continue;
                        if (!customerIds.Contains(customerId)) continue;

                        if (ledgerRevenueData.ContainsKey(customerId))
                            ledgerRevenueData[customerId] += entry.Credit;
                        else
                            ledgerRevenueData[customerId] = entry.Credit;
                    }

                    // COGS من الميزانية - نستخدم SourceId (رقم الفاتورة) ثم نربطه بالعميل
                    var cogsQuery = _context.LedgerEntries
                        .AsNoTracking()
                        .Where(e =>
                            e.AccountId == cogsAccount.AccountId &&
                            e.SourceType == LedgerSourceType.SalesInvoice &&
                            e.LineNo == 3 &&
                            e.PostVersion > 0 &&
                            e.SourceId.HasValue &&
                            salesInvoiceIds.Contains(e.SourceId.Value) &&
                            !_context.LedgerEntries.Any(rev =>
                                rev.SourceType == LedgerSourceType.SalesInvoice &&
                                rev.SourceId == e.SourceId &&
                                rev.LineNo == 9001));

                    if (fromDate.HasValue)
                    {
                        var from = fromDate.Value.Date;
                        cogsQuery = cogsQuery.Where(e => e.EntryDate >= from);
                    }

                    if (toDate.HasValue)
                    {
                        var to = toDate.Value.Date.AddDays(1);
                        cogsQuery = cogsQuery.Where(e => e.EntryDate < to);
                    }

                    var cogsEntries = await cogsQuery.ToListAsync();

                    // ربط COGS بالعملاء عبر SalesInvoices
                    foreach (var entry in cogsEntries)
                    {
                        if (!entry.SourceId.HasValue) continue;
                        if (!invoicesDict.TryGetValue(entry.SourceId.Value, out int customerId)) continue;
                        if (!customerIds.Contains(customerId)) continue;

                        if (ledgerCostData.ContainsKey(customerId))
                            ledgerCostData[customerId] += entry.Debit;
                        else
                            ledgerCostData[customerId] = entry.Debit;
                    }
                }
            }

            // =========================================================
            // 6.5) حساب إشعارات الخصم والإضافة من LedgerEntries
            // ✅ إشعارات الخصم (DebitNote): تكلفة/مصروف (يقلل الربح)
            // ✅ إشعارات الإضافة (CreditNote): إيراد (يزيد الربح)
            // 
            // المنطق: عند ترحيل إشعار الخصم/الإضافة، يُنشأ قيدان:
            // - DebitNote: مدين حساب العميل، دائن حساب OffsetAccount
            // - CreditNote: مدين حساب OffsetAccount، دائن حساب العميل
            // 
            // لذلك: نحسب التأثير من القيد الذي يحتوي على OffsetAccount:
            // - إذا كان AccountType = Expense → مصروف (يقلل الربح)
            // - إذا كان AccountType = Revenue → إيراد (يزيد الربح)
            // =========================================================
            var notesQuery = _context.LedgerEntries
                .AsNoTracking()
                .Include(e => e.Account)
                .Where(e =>
                    e.CustomerId.HasValue &&
                    customerIds.Contains(e.CustomerId.Value) &&
                    (e.SourceType == LedgerSourceType.DebitNote || 
                     e.SourceType == LedgerSourceType.CreditNote) &&
                    e.PostVersion > 0);

            if (fromDate.HasValue)
            {
                var from = fromDate.Value.Date;
                notesQuery = notesQuery.Where(e => e.EntryDate >= from);
            }

            if (toDate.HasValue)
            {
                var to = toDate.Value.Date.AddDays(1);
                notesQuery = notesQuery.Where(e => e.EntryDate < to);
            }

            var notesEntries = await notesQuery.ToListAsync();

            Dictionary<int, decimal> debitNotesAmount = new Dictionary<int, decimal>();
            Dictionary<int, decimal> creditNotesAmount = new Dictionary<int, decimal>();

            // جلب إشعارات الخصم والإضافة للحصول على OffsetAccountId
            var debitNoteIds = notesEntries
                .Where(e => e.SourceType == LedgerSourceType.DebitNote && e.SourceId.HasValue)
                .Select(e => e.SourceId!.Value)
                .Distinct()
                .ToList();

            var creditNoteIds = notesEntries
                .Where(e => e.SourceType == LedgerSourceType.CreditNote && e.SourceId.HasValue)
                .Select(e => e.SourceId!.Value)
                .Distinct()
                .ToList();

            var debitNotesDict = await _context.DebitNotes
                .AsNoTracking()
                .Where(dn => debitNoteIds.Contains(dn.DebitNoteId))
                .Select(dn => new { dn.DebitNoteId, dn.OffsetAccountId })
                .ToDictionaryAsync(x => x.DebitNoteId);

            var creditNotesDict = await _context.CreditNotes
                .AsNoTracking()
                .Where(cn => creditNoteIds.Contains(cn.CreditNoteId))
                .Select(cn => new { cn.CreditNoteId, cn.OffsetAccountId })
                .ToDictionaryAsync(x => x.CreditNoteId);

            foreach (var entry in notesEntries)
            {
                if (!entry.CustomerId.HasValue || !entry.SourceId.HasValue) continue;

                int custId = entry.CustomerId.Value;
                int? offsetAccountId = null;

                // تحديد OffsetAccountId
                if (entry.SourceType == LedgerSourceType.DebitNote)
                {
                    if (debitNotesDict.TryGetValue(entry.SourceId.Value, out var dn))
                        offsetAccountId = dn.OffsetAccountId;
                }
                else if (entry.SourceType == LedgerSourceType.CreditNote)
                {
                    if (creditNotesDict.TryGetValue(entry.SourceId.Value, out var cn))
                        offsetAccountId = cn.OffsetAccountId;
                }

                // إذا كان AccountId في القيد = OffsetAccountId → هذا هو القيد الذي يؤثر على الربح
                if (!offsetAccountId.HasValue || entry.AccountId != offsetAccountId.Value)
                    continue;

                decimal amount = 0m;

                // تحديد المبلغ حسب نوع الإشعار ونوع القيد
                if (entry.SourceType == LedgerSourceType.DebitNote)
                {
                    // DebitNote: دائن OffsetAccount (Credit)
                    amount = entry.Credit;
                }
                else if (entry.SourceType == LedgerSourceType.CreditNote)
                {
                    // CreditNote: مدين OffsetAccount (Debit)
                    amount = entry.Debit;
                }

                if (amount <= 0m) continue;

                // تحديد التأثير حسب نوع الحساب
                if (entry.Account.AccountType == AccountType.Expense)
                {
                    // مصروف → يقلل الربح
                    if (debitNotesAmount.ContainsKey(custId))
                        debitNotesAmount[custId] += amount;
                    else
                        debitNotesAmount[custId] = amount;
                }
                else if (entry.Account.AccountType == AccountType.Revenue)
                {
                    // إيراد → يزيد الربح
                    if (creditNotesAmount.ContainsKey(custId))
                        creditNotesAmount[custId] += amount;
                    else
                        creditNotesAmount[custId] = amount;
                }
            }

            // =========================================================
            // 6.75) مرتجعات البيع: ربح المرتجعات لكل عميل (ReturnRevenue - ReturnCost)
            // =========================================================
            var salesReturnLinesQ = _context.SalesReturnLines
                .AsNoTracking()
                .Include(srl => srl.SalesReturn)
                .Where(srl => srl.SalesReturn != null && srl.SalesReturn.IsPosted && customerIds.Contains(srl.SalesReturn.CustomerId));

            if (fromDate.HasValue)
                salesReturnLinesQ = salesReturnLinesQ.Where(srl => srl.SalesReturn!.SRDate >= fromDate.Value.Date);
            if (toDate.HasValue)
                salesReturnLinesQ = salesReturnLinesQ.Where(srl => srl.SalesReturn!.SRDate < toDate.Value.Date.AddDays(1));

            var returnRevenueByCustomer = await salesReturnLinesQ
                .GroupBy(srl => srl.SalesReturn!.CustomerId)
                .Select(g => new { CustomerId = g.Key, ReturnRevenue = g.Sum(x => x.LineNetTotal) })
                .ToDictionaryAsync(x => x.CustomerId, x => x.ReturnRevenue);

            var returnCostByCustomer = await (from sl in _context.StockLedger.AsNoTracking()
                                              join sr in _context.SalesReturns.AsNoTracking() on sl.SourceId equals sr.SRId
                                              where sl.SourceType == "SalesReturn"
                                                    && sr.IsPosted
                                                    && customerIds.Contains(sr.CustomerId)
                                              select new { sr.CustomerId, sr.SRDate, sl.UnitCost, sl.QtyIn })
                .Where(x => (!fromDate.HasValue || x.SRDate >= fromDate.Value.Date) &&
                            (!toDate.HasValue || x.SRDate < toDate.Value.Date.AddDays(1)))
                .GroupBy(x => x.CustomerId)
                .Select(g => new { CustomerId = g.Key, ReturnCost = g.Sum(x => x.UnitCost * x.QtyIn) })
                .ToDictionaryAsync(x => x.CustomerId, x => x.ReturnCost);

            // =========================================================
            // 7) بناء reportData
            // =========================================================
            var customersDict = await customersQuery
                .Select(c => new
                {
                    c.CustomerId,
                    c.CustomerName,
                    c.PartyCategory,
                    c.Phone1
                })
                .ToDictionaryAsync(c => c.CustomerId);

            var reportData = new List<CustomerProfitReportDto>();

            foreach (var customerId in customerIds)
            {
                if (!customersDict.TryGetValue(customerId, out var customer)) continue;

                // الربح من البيع (نفس منطق ProductProfits)
                decimal salesRevenue = salesProfitData.TryGetValue(customerId, out var salesData) ? salesData.SalesRevenue : 0m;
                decimal salesCost = salesProfitData.TryGetValue(customerId, out var salesData2) ? salesData2.SalesCost : 0m;
                decimal salesProfit = salesRevenue - salesCost;
                decimal salesProfitPercent = salesRevenue > 0 ? (salesProfit / salesRevenue) * 100m : 0m;
                int invoiceCount = salesProfitData.TryGetValue(customerId, out var salesData3) ? salesData3.InvoiceCount : 0;
                decimal avgInvoiceValue = invoiceCount > 0 ? salesRevenue / invoiceCount : 0m;

                // مرتجعات البيع + صافي الربح
                decimal returnRevenue = returnRevenueByCustomer.TryGetValue(customerId, out var rr) ? rr : 0m;
                decimal returnCost = returnCostByCustomer.TryGetValue(customerId, out var rc) ? rc : 0m;
                decimal returnProfit = returnRevenue - returnCost;
                decimal netProfit = salesProfit - returnProfit;

                // عرض الصفر: عند عدم التفعيل نستبعد العملاء الذين ليس لهم مبيعات ولا مرتجعات في الفترة
                if (!includeZeroQty && salesRevenue == 0m && returnRevenue == 0m)
                    continue;

                // الربح من الميزانية
                decimal ledgerRevenue = ledgerRevenueData.TryGetValue(customerId, out var rev) ? rev : 0m;
                decimal ledgerCost = ledgerCostData.TryGetValue(customerId, out var cost) ? cost : 0m;
                decimal ledgerProfit = ledgerRevenue - ledgerCost;
                decimal ledgerProfitPercent = ledgerRevenue > 0 ? (ledgerProfit / ledgerRevenue) * 100m : 0m;

                // إشعارات الخصم والإضافة
                decimal debitNotes = debitNotesAmount.TryGetValue(customerId, out var dn) ? dn : 0m;
                decimal creditNotes = creditNotesAmount.TryGetValue(customerId, out var cn) ? cn : 0m;
                decimal netNotesAdjustment = creditNotes - debitNotes; // صافي الإشعارات (الإضافة - الخصم)
                decimal adjustedProfit = netProfit + netNotesAdjustment; // الربح المعدل (بعد المرتجعات)
                decimal adjustedProfitPercent = salesRevenue > 0 ? (adjustedProfit / salesRevenue) * 100m : 0m;

                // الربح من أرصدة الحسابات (Account Balance) - سيتم حسابه لاحقاً لكل عميل
                decimal accountBalanceRevenue = 0m;
                decimal accountBalanceCost = 0m;
                decimal accountBalanceProfit = 0m;
                decimal accountBalanceProfitPercent = 0m;

                reportData.Add(new CustomerProfitReportDto
                {
                    CustomerId = customerId,
                    CustomerCode = customerId.ToString(),
                    CustomerName = customer.CustomerName ?? "",
                    PartyCategory = customer.PartyCategory ?? "",
                    Phone1 = customer.Phone1 ?? "",
                    SalesRevenue = salesRevenue,
                    SalesCost = salesCost,
                    SalesProfit = salesProfit,
                    SalesProfitPercent = salesProfitPercent,
                    ReturnProfit = returnProfit,
                    NetProfit = netProfit,
                    LedgerRevenue = ledgerRevenue,
                    LedgerCost = ledgerCost,
                    LedgerProfit = ledgerProfit,
                    LedgerProfitPercent = ledgerProfitPercent,
                    DebitNotesAmount = debitNotes,
                    CreditNotesAmount = creditNotes,
                    NetNotesAdjustment = netNotesAdjustment,
                    AdjustedProfit = adjustedProfit,
                    AdjustedProfitPercent = adjustedProfitPercent,
                    AccountBalanceRevenue = accountBalanceRevenue,
                    AccountBalanceCost = accountBalanceCost,
                    AccountBalanceProfit = accountBalanceProfit,
                    AccountBalanceProfitPercent = accountBalanceProfitPercent,
                    InvoiceCount = invoiceCount,
                    AvgInvoiceValue = avgInvoiceValue
                });
            }

            // =========================================================
            // 7.1) حساب الربح من أرصدة الحسابات (Account Balance) للعملاء
            // =========================================================
            if (salesRevenueAccount != null && cogsAccount != null)
            {
                // فواتير المبيعات في النطاق (نفس القائمة المستخدمة أعلاه)
                var salesInvoiceIds = salesInvoiceIdsList;

                if (salesInvoiceIds.Any())
                {
                    // تحميل الفواتير دفعة واحدة
                    var invoicesDict = await _context.SalesInvoices
                        .AsNoTracking()
                        .Where(si => salesInvoiceIds.Contains(si.SIId))
                        .Select(si => new { si.SIId, si.CustomerId })
                        .ToDictionaryAsync(x => x.SIId, x => x.CustomerId);

                    // حساب رصيد حساب الإيرادات (4100) = Credit - Debit
                    var revenueBalanceEntries = await _context.LedgerEntries
                        .AsNoTracking()
                        .Where(e =>
                            e.AccountId == salesRevenueAccount.AccountId &&
                            e.SourceType == LedgerSourceType.SalesInvoice &&
                            e.LineNo == 2 &&
                            e.PostVersion > 0 &&
                            e.SourceId.HasValue &&
                            salesInvoiceIds.Contains(e.SourceId.Value) &&
                            !_context.LedgerEntries.Any(rev =>
                                rev.SourceType == LedgerSourceType.SalesInvoice &&
                                rev.SourceId == e.SourceId &&
                                rev.LineNo == 9001))
                        .ToListAsync();

                    // تطبيق فلتر التاريخ
                    if (fromDate.HasValue || toDate.HasValue)
                    {
                        var from = fromDate?.Date ?? DateTime.MinValue;
                        var to = toDate?.Date.AddDays(1) ?? DateTime.MaxValue;
                        revenueBalanceEntries = revenueBalanceEntries
                            .Where(e => e.EntryDate >= from && e.EntryDate < to)
                            .ToList();
                    }

                    // ربط الإيرادات بالعملاء عبر SalesInvoices
                    Dictionary<int, decimal> revenueBalanceByCustomer = new Dictionary<int, decimal>();
                    foreach (var entry in revenueBalanceEntries)
                    {
                        if (!entry.SourceId.HasValue) continue;
                        if (!invoicesDict.TryGetValue(entry.SourceId.Value, out int customerId)) continue;
                        if (!customerIds.Contains(customerId)) continue;

                        if (revenueBalanceByCustomer.ContainsKey(customerId))
                        {
                            revenueBalanceByCustomer[customerId] += (entry.Credit - entry.Debit);
                        }
                        else
                        {
                            revenueBalanceByCustomer[customerId] = entry.Credit - entry.Debit;
                        }
                    }

                    // حساب رصيد حساب COGS (5100) = Debit - Credit
                    var cogsBalanceEntries = await _context.LedgerEntries
                        .AsNoTracking()
                        .Where(e =>
                            e.AccountId == cogsAccount.AccountId &&
                            e.SourceType == LedgerSourceType.SalesInvoice &&
                            e.LineNo == 3 &&
                            e.PostVersion > 0 &&
                            e.SourceId.HasValue &&
                            salesInvoiceIds.Contains(e.SourceId.Value) &&
                            !_context.LedgerEntries.Any(rev =>
                                rev.SourceType == LedgerSourceType.SalesInvoice &&
                                rev.SourceId == e.SourceId &&
                                rev.LineNo == 9001))
                        .ToListAsync();

                    // تطبيق فلتر التاريخ
                    if (fromDate.HasValue || toDate.HasValue)
                    {
                        var from = fromDate?.Date ?? DateTime.MinValue;
                        var to = toDate?.Date.AddDays(1) ?? DateTime.MaxValue;
                        cogsBalanceEntries = cogsBalanceEntries
                            .Where(e => e.EntryDate >= from && e.EntryDate < to)
                            .ToList();
                    }

                    // ربط COGS بالعملاء عبر SalesInvoices
                    Dictionary<int, decimal> cogsBalanceByCustomer = new Dictionary<int, decimal>();
                    foreach (var entry in cogsBalanceEntries)
                    {
                        if (!entry.SourceId.HasValue) continue;
                        if (!invoicesDict.TryGetValue(entry.SourceId.Value, out int customerId)) continue;
                        if (!customerIds.Contains(customerId)) continue;

                        if (cogsBalanceByCustomer.ContainsKey(customerId))
                        {
                            cogsBalanceByCustomer[customerId] += (entry.Debit - entry.Credit);
                        }
                        else
                        {
                            cogsBalanceByCustomer[customerId] = entry.Debit - entry.Credit;
                        }
                    }

                    // تحديث بيانات كل عميل
                    foreach (var item in reportData)
                    {
                        decimal revenueBalance = revenueBalanceByCustomer.TryGetValue(item.CustomerId, out var revBal) ? revBal : 0m;
                        decimal cogsBalance = cogsBalanceByCustomer.TryGetValue(item.CustomerId, out var cogsBal) ? cogsBal : 0m;
                        
                        item.AccountBalanceRevenue = revenueBalance;
                        item.AccountBalanceCost = cogsBalance;
                        item.AccountBalanceProfit = revenueBalance - cogsBalance;
                        item.AccountBalanceProfitPercent = revenueBalance > 0 
                            ? (item.AccountBalanceProfit / revenueBalance) * 100m 
                            : 0m;
                    }
                }
            }

            return reportData;
        }
        private static string CustomerProfitCategoryCellText(CustomerProfitReportDto r) =>
            string.IsNullOrEmpty(r.PartyCategory) ? "—" : PartyCategoryDisplay.ToArabic(r.PartyCategory);

        private static string CustomerProfitPhoneCellText(CustomerProfitReportDto r) =>
            string.IsNullOrWhiteSpace(r.Phone1) ? "—" : r.Phone1!.Trim();

        private List<CustomerProfitReportDto> ApplyCustomerProfitsColumnFilters(
            List<CustomerProfitReportDto> reportData,
            string? filterCol_code,
            string? filterCol_name,
            string? filterCol_category,
            string? filterCol_phone,
            string? filterCol_salesrevenueExpr,
            string? filterCol_salescostExpr,
            string? filterCol_salesprofitExpr,
            string? filterCol_salesprofitpctExpr,
            string? filterCol_returnprofitExpr,
            string? filterCol_netprofitExpr,
            string? omitTextColumnFilter)
        {
            var omit = (omitTextColumnFilter ?? "").Trim().ToLowerInvariant();

            if (omit != "code" && !string.IsNullOrWhiteSpace(filterCol_code))
            {
                var codeFilter = filterCol_code.Trim();
                if (codeFilter.Contains('|'))
                {
                    var parts = codeFilter.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    reportData = reportData
                        .Where(r => parts.Contains(r.CustomerCode ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                        .ToList();
                }
                else
                {
                    reportData = reportData.Where(r => (r.CustomerCode ?? "").Contains(codeFilter, StringComparison.OrdinalIgnoreCase)).ToList();
                }
            }
            if (omit != "name" && !string.IsNullOrWhiteSpace(filterCol_name))
            {
                var nameFilter = filterCol_name.Trim();
                if (nameFilter.Contains('|'))
                {
                    var parts = nameFilter.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    reportData = reportData
                        .Where(r => parts.Contains(r.CustomerName ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                        .ToList();
                }
                else
                {
                    reportData = reportData.Where(r => (r.CustomerName ?? "").Contains(nameFilter, StringComparison.OrdinalIgnoreCase)).ToList();
                }
            }
            if (omit != "category" && !string.IsNullOrWhiteSpace(filterCol_category))
            {
                var catFilter = filterCol_category.Trim();
                if (catFilter.Contains('|'))
                {
                    var parts = catFilter.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    reportData = reportData
                        .Where(r => parts.Contains(CustomerProfitCategoryCellText(r), StringComparer.OrdinalIgnoreCase))
                        .ToList();
                }
                else
                {
                    reportData = reportData.Where(r => CustomerProfitCategoryCellText(r).Contains(catFilter, StringComparison.OrdinalIgnoreCase)).ToList();
                }
            }
            if (omit != "phone" && !string.IsNullOrWhiteSpace(filterCol_phone))
            {
                var phFilter = filterCol_phone.Trim();
                if (phFilter.Contains('|'))
                {
                    var parts = phFilter.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    reportData = reportData
                        .Where(r => parts.Contains(CustomerProfitPhoneCellText(r), StringComparer.OrdinalIgnoreCase))
                        .ToList();
                }
                else
                {
                    reportData = reportData.Where(r => CustomerProfitPhoneCellText(r).Contains(phFilter, StringComparison.OrdinalIgnoreCase)).ToList();
                }
            }

            var inv = CultureInfo.InvariantCulture;
            bool ApplyDecimalExpr(string? expr, Func<CustomerProfitReportDto, decimal> selector)
            {
                if (string.IsNullOrWhiteSpace(expr)) return false;
                var e = expr.Trim();
                if (e.StartsWith("<=") && e.Length > 2 && decimal.TryParse(e.AsSpan(2), NumberStyles.Any, inv, out var max))
                {
                    reportData = reportData.Where(r => selector(r) <= max).ToList();
                    return true;
                }
                if (e.StartsWith(">=") && e.Length > 2 && decimal.TryParse(e.AsSpan(2), NumberStyles.Any, inv, out var min))
                {
                    reportData = reportData.Where(r => selector(r) >= min).ToList();
                    return true;
                }
                if (e.StartsWith("<") && !e.StartsWith("<=") && e.Length > 1 && decimal.TryParse(e.AsSpan(1), NumberStyles.Any, inv, out var max2))
                {
                    reportData = reportData.Where(r => selector(r) < max2).ToList();
                    return true;
                }
                if (e.StartsWith(">") && !e.StartsWith(">=") && e.Length > 1 && decimal.TryParse(e.AsSpan(1), NumberStyles.Any, inv, out var min2))
                {
                    reportData = reportData.Where(r => selector(r) > min2).ToList();
                    return true;
                }
                if ((e.Contains(':') || (e.Contains('-') && !e.StartsWith("-"))) && !e.StartsWith("-"))
                {
                    var sep = e.Contains(':') ? ':' : '-';
                    var parts = e.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2 &&
                        decimal.TryParse(parts[0].Trim(), NumberStyles.Any, inv, out var fromR) &&
                        decimal.TryParse(parts[1].Trim(), NumberStyles.Any, inv, out var toR))
                    {
                        if (fromR > toR) (fromR, toR) = (toR, fromR);
                        reportData = reportData.Where(r => selector(r) >= fromR && selector(r) <= toR).ToList();
                        return true;
                    }
                }
                if (decimal.TryParse(e, NumberStyles.Any, inv, out var exact))
                {
                    reportData = reportData.Where(r => selector(r) == exact).ToList();
                    return true;
                }
                return false;
            }
            ApplyDecimalExpr(filterCol_salesrevenueExpr, r => r.SalesRevenue);
            ApplyDecimalExpr(filterCol_salescostExpr, r => r.SalesCost);
            ApplyDecimalExpr(filterCol_salesprofitExpr, r => r.SalesProfit);
            ApplyDecimalExpr(filterCol_salesprofitpctExpr, r => r.SalesProfitPercent);
            ApplyDecimalExpr(filterCol_returnprofitExpr, r => r.ReturnProfit);
            ApplyDecimalExpr(filterCol_netprofitExpr, r => r.NetProfit);

            return reportData;
        }

        private static List<CustomerProfitReportDto> SortCustomerProfitsReportList(
            List<CustomerProfitReportDto> reportData,
            string? sort,
            string? dir)
        {
            var sortKey = sort ?? "name";
            bool isDesc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            switch (sortKey.ToLowerInvariant())
            {
                case "code":
                    return isDesc
                        ? reportData.OrderByDescending(r => r.CustomerCode).ToList()
                        : reportData.OrderBy(r => r.CustomerCode).ToList();
                case "salesprofit":
                    return isDesc
                        ? reportData.OrderByDescending(r => r.SalesProfit).ToList()
                        : reportData.OrderBy(r => r.SalesProfit).ToList();
                case "ledgerprofit":
                    return isDesc
                        ? reportData.OrderByDescending(r => r.LedgerProfit).ToList()
                        : reportData.OrderBy(r => r.LedgerProfit).ToList();
                case "salesrevenue":
                    return isDesc
                        ? reportData.OrderByDescending(r => r.SalesRevenue).ToList()
                        : reportData.OrderBy(r => r.SalesRevenue).ToList();
                case "salescost":
                    return isDesc
                        ? reportData.OrderByDescending(r => r.SalesCost).ToList()
                        : reportData.OrderBy(r => r.SalesCost).ToList();
                case "salesprofitpct":
                    return isDesc
                        ? reportData.OrderByDescending(r => r.SalesProfitPercent).ToList()
                        : reportData.OrderBy(r => r.SalesProfitPercent).ToList();
                case "returnprofit":
                    return isDesc
                        ? reportData.OrderByDescending(r => r.ReturnProfit).ToList()
                        : reportData.OrderBy(r => r.ReturnProfit).ToList();
                case "netprofit":
                    return isDesc
                        ? reportData.OrderByDescending(r => r.NetProfit).ToList()
                        : reportData.OrderBy(r => r.NetProfit).ToList();
                case "category":
                case "partycategory":
                    return isDesc
                        ? reportData.OrderByDescending(r => r.PartyCategory ?? "").ToList()
                        : reportData.OrderBy(r => r.PartyCategory ?? "").ToList();
                case "phone":
                    return isDesc
                        ? reportData.OrderByDescending(r => r.Phone1 ?? "").ToList()
                        : reportData.OrderBy(r => r.Phone1 ?? "").ToList();
                default:
                    return isDesc
                        ? reportData.OrderByDescending(r => r.CustomerName).ToList()
                        : reportData.OrderBy(r => r.CustomerName).ToList();
            }
        }

        /// <summary>قيم مميزة لأعمدة نصية من كامل النتائج المفلترة (للفلترة الموحّدة).</summary>
        [HttpGet]
        [RequirePermission("Reports.CustomerProfits")]
        public async Task<IActionResult> GetCustomerProfitsColumnValues(
            string column,
            string? search,
            string? mainSearch,
            string? sort,
            string? dir,
            string? partyCategory,
            int? governorateId,
            DateTime? fromDate,
            DateTime? toDate,
            bool includeZeroQty = false,
            string? filterCol_code = null,
            string? filterCol_name = null,
            string? filterCol_category = null,
            string? filterCol_phone = null,
            string? filterCol_salesrevenueExpr = null,
            string? filterCol_salescostExpr = null,
            string? filterCol_salesprofitExpr = null,
            string? filterCol_salesprofitpctExpr = null,
            string? filterCol_returnprofitExpr = null,
            string? filterCol_netprofitExpr = null)
        {
            var col = (column ?? "").Trim().ToLowerInvariant();
            if (col != "code" && col != "name" && col != "category" && col != "phone")
                return Json(Array.Empty<object>());

            var rows = await BuildCustomerProfitsReportRowsAsync(mainSearch, partyCategory, governorateId, fromDate, toDate, includeZeroQty);
            rows = ApplyCustomerProfitsColumnFilters(
                rows,
                filterCol_code,
                filterCol_name,
                filterCol_category,
                filterCol_phone,
                filterCol_salesrevenueExpr,
                filterCol_salescostExpr,
                filterCol_salesprofitExpr,
                filterCol_salesprofitpctExpr,
                filterCol_returnprofitExpr,
                filterCol_netprofitExpr,
                omitTextColumnFilter: col);

            var term = (search ?? "").Trim();
            if (col == "code")
            {
                var list = rows.Select(r => r.CustomerCode ?? "").Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Take(2000).ToList();
                if (!string.IsNullOrEmpty(term))
                {
                    var t = term.ToLowerInvariant();
                    list = list.Where(x => (x ?? "").ToLowerInvariant().Contains(t)).ToList();
                }
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (col == "name")
            {
                var list = rows.Select(r => r.CustomerName ?? "").Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Take(2000).ToList();
                if (!string.IsNullOrEmpty(term))
                {
                    var t = term.ToLowerInvariant();
                    list = list.Where(x => (x ?? "").ToLowerInvariant().Contains(t)).ToList();
                }
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (col == "category")
            {
                var list = rows.Select(CustomerProfitCategoryCellText).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Take(2000).ToList();
                if (!string.IsNullOrEmpty(term))
                {
                    var t = term.ToLowerInvariant();
                    list = list.Where(x => (x ?? "").ToLowerInvariant().Contains(t)).ToList();
                }
                return Json(list.Select(v => new { value = v, display = string.IsNullOrEmpty(v) ? "—" : v }));
            }
            var phoneList = rows.Select(CustomerProfitPhoneCellText).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Take(2000).ToList();
            if (!string.IsNullOrEmpty(term))
            {
                var t = term.ToLowerInvariant();
                phoneList = phoneList.Where(x => (x ?? "").ToLowerInvariant().Contains(t)).ToList();
            }
            return Json(phoneList.Select(v => new { value = v, display = string.IsNullOrEmpty(v) ? "—" : v }));
        }
    }
}
