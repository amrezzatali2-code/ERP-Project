using ERP.Filters;
using ERP.Infrastructure;
using ERP.Models;
using ERP.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ERP.Controllers
{
    public partial class ReportsController
    {
        private static readonly string[] CustomerBalancesPrintColumnOrder =
        {
            "code", "externalcode", "name", "account", "category", "phone",
            "debit", "credit", "creditlimit", "sales", "purchases", "returns", "availablecredit"
        };

        /// <summary>نفس منطق تصدير Excel — قائمة كاملة مفلترة ومرتبة. null = لا يوجد عملاء مطابقون.</summary>
        private async Task<List<CustomerBalanceReportDto>?> BuildCustomerBalancesReportDataForExportAsync(
            string? search,
            string? searchMode,
            string? partyCategory,
            int? governorateId,
            DateTime? fromDate,
            DateTime? toDate,
            bool includeZeroBalance,
            string? sortBy,
            string? sortDir,
            string? filterCol_code,
            string? filterCol_name,
            string? filterCol_category,
            string? filterCol_phone,
            string? filterCol_account,
            string? filterCol_debit,
            string? filterCol_credit,
            string? filterCol_creditlimit,
            string? filterCol_sales,
            string? filterCol_purchases,
            string? filterCol_returns,
            string? filterCol_availablecredit)
        {
            var customersQuery = _context.Customers.AsNoTracking().AsQueryable();
            customersQuery = await _accountVisibilityService.ApplyCustomerVisibilityFilterAsync(customersQuery);
            customersQuery = ApplyCustomerBalancesSearchFilter(customersQuery, search, searchMode);

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
                return null;

            var customersDict = await customersQuery
                .Select(c => new
                {
                    c.CustomerId,
                    c.CustomerName,
                    c.PartyCategory,
                    c.Phone1,
                    c.CreditLimit,
                    c.ExternalCode,
                    c.AccountId,
                    AccountCode = c.Account != null ? c.Account.AccountCode : null,
                    AccountName = c.Account != null ? c.Account.AccountName : null
                })
                .ToDictionaryAsync(c => c.CustomerId);

            var ledgerForBalanceExport = _context.LedgerEntries
                .AsNoTracking()
                .Include(e => e.Account)
                .Include(e => e.Customer)
                .Where(e => e.CustomerId.HasValue && customerIds.Contains(e.CustomerId.Value));
            ledgerForBalanceExport = await _accountVisibilityService.ApplyLedgerEntryListVisibilityFilterAsync(ledgerForBalanceExport);
            var balanceByCustomer = await ledgerForBalanceExport
                .GroupBy(e => e.CustomerId!.Value)
                .Select(g => new { CustomerId = g.Key, Balance = g.Sum(e => (decimal?)(e.Debit - e.Credit)) ?? 0m })
                .ToDictionaryAsync(x => x.CustomerId, x => x.Balance);

            Dictionary<int, decimal> salesTotals = new Dictionary<int, decimal>();
            if (fromDate.HasValue || toDate.HasValue)
            {
                var salesQuery = _context.LedgerEntries
                    .AsNoTracking()
                    .Where(e =>
                        e.CustomerId.HasValue &&
                        customerIds.Contains(e.CustomerId.Value) &&
                        e.SourceType == LedgerSourceType.SalesInvoice &&
                        e.LineNo == 1 &&
                        e.PostVersion > 0 &&
                        !_context.LedgerEntries.Any(rev =>
                            rev.CustomerId == e.CustomerId &&
                            rev.SourceType == LedgerSourceType.SalesInvoice &&
                            rev.SourceId == e.SourceId &&
                            rev.LineNo == 9001));

                if (fromDate.HasValue)
                {
                    var from = fromDate.Value.Date;
                    salesQuery = salesQuery.Where(e => e.EntryDate >= from);
                }

                if (toDate.HasValue)
                {
                    var to = toDate.Value.Date.AddDays(1);
                    salesQuery = salesQuery.Where(e => e.EntryDate < to);
                }

                salesTotals = await salesQuery
                    .GroupBy(e => e.CustomerId!.Value)
                    .Select(g => new { CustomerId = g.Key, TotalSales = g.Sum(e => e.Debit) })
                    .ToDictionaryAsync(x => x.CustomerId, x => x.TotalSales);
            }

            Dictionary<int, decimal> purchasesTotals = new Dictionary<int, decimal>();
            if (fromDate.HasValue || toDate.HasValue)
            {
                var maxPostVersions = await _context.LedgerEntries
                    .AsNoTracking()
                    .Where(e =>
                        e.CustomerId.HasValue &&
                        customerIds.Contains(e.CustomerId.Value) &&
                        e.SourceType == LedgerSourceType.PurchaseInvoice &&
                        e.LineNo == 2 &&
                        e.LineNo < 9000 &&
                        e.PostVersion > 0 &&
                        e.Description != null &&
                        !e.Description.Contains("عكس"))
                    .GroupBy(e => e.SourceId)
                    .Select(g => new { SourceId = g.Key, MaxPostVersion = g.Max(e => e.PostVersion) })
                    .ToDictionaryAsync(x => x.SourceId, x => x.MaxPostVersion);

                var sourceIds = maxPostVersions.Keys.ToList();
                if (sourceIds.Count > 0)
                {
                    var purchasesQuery = _context.LedgerEntries
                        .AsNoTracking()
                        .Where(e =>
                            e.CustomerId.HasValue &&
                            customerIds.Contains(e.CustomerId.Value) &&
                            e.SourceType == LedgerSourceType.PurchaseInvoice &&
                            e.LineNo == 2 &&
                            e.LineNo < 9000 &&
                            e.PostVersion > 0 &&
                            e.Description != null &&
                            !e.Description.Contains("عكس") &&
                            sourceIds.Contains(e.SourceId!.Value));

                    if (fromDate.HasValue)
                    {
                        var from = fromDate.Value.Date;
                        purchasesQuery = purchasesQuery.Where(e => e.EntryDate >= from);
                    }

                    if (toDate.HasValue)
                    {
                        var to = toDate.Value.Date.AddDays(1);
                        purchasesQuery = purchasesQuery.Where(e => e.EntryDate < to);
                    }

                    var allPurchasesEntries = await purchasesQuery.ToListAsync();

                    var filteredEntries = allPurchasesEntries
                        .Where(e =>
                            e.SourceId.HasValue &&
                            maxPostVersions.ContainsKey(e.SourceId.Value) &&
                            maxPostVersions[e.SourceId.Value] == e.PostVersion)
                        .GroupBy(e => e.CustomerId!.Value)
                        .Select(g => new { CustomerId = g.Key, TotalPurchases = g.Sum(e => e.Credit) })
                        .ToList();

                    purchasesTotals = filteredEntries.ToDictionary(x => x.CustomerId, x => x.TotalPurchases);
                }
            }

            Dictionary<int, decimal> returnsTotals = new Dictionary<int, decimal>();
            {
                var salesReturnQ = _context.LedgerEntries.AsNoTracking()
                    .Where(e => e.CustomerId.HasValue && customerIds.Contains(e.CustomerId.Value) &&
                        e.SourceType == LedgerSourceType.SalesReturn && e.LineNo == 2 && e.PostVersion > 0);
                if (fromDate.HasValue) salesReturnQ = salesReturnQ.Where(e => e.EntryDate >= fromDate.Value.Date);
                if (toDate.HasValue) salesReturnQ = salesReturnQ.Where(e => e.EntryDate < toDate.Value.Date.AddDays(1));
                var srByCustomer = await salesReturnQ
                    .GroupBy(e => e.CustomerId!.Value)
                    .Select(g => new { CustomerId = g.Key, Sum = g.Sum(e => e.Credit) })
                    .ToDictionaryAsync(x => x.CustomerId, x => x.Sum);
                var purchaseReturnQ = _context.LedgerEntries.AsNoTracking()
                    .Where(e => e.CustomerId.HasValue && customerIds.Contains(e.CustomerId.Value) &&
                        e.SourceType == LedgerSourceType.PurchaseReturn && e.LineNo == 1 && e.PostVersion > 0);
                if (fromDate.HasValue) purchaseReturnQ = purchaseReturnQ.Where(e => e.EntryDate >= fromDate.Value.Date);
                if (toDate.HasValue) purchaseReturnQ = purchaseReturnQ.Where(e => e.EntryDate < toDate.Value.Date.AddDays(1));
                var prByCustomer = await purchaseReturnQ
                    .GroupBy(e => e.CustomerId!.Value)
                    .Select(g => new { CustomerId = g.Key, Sum = g.Sum(e => e.Debit) })
                    .ToDictionaryAsync(x => x.CustomerId, x => x.Sum);
                foreach (var cid in customerIds)
                {
                    decimal sr = srByCustomer.TryGetValue(cid, out var s) ? s : 0m;
                    decimal pr = prByCustomer.TryGetValue(cid, out var p) ? p : 0m;
                    returnsTotals[cid] = sr + pr;
                }
            }

            var reportData = new List<CustomerBalanceReportDto>();

            foreach (var customerId in customerIds)
            {
                if (!customersDict.TryGetValue(customerId, out var customer)) continue;

                decimal currentBalance = balanceByCustomer.TryGetValue(customerId, out var bal) ? bal : 0m;
                decimal creditLimit = customer.CreditLimit;

                if (!includeZeroBalance && currentBalance == 0)
                    continue;

                decimal totalSales = salesTotals.TryGetValue(customerId, out var sales) ? sales : 0m;
                decimal totalPurchases = purchasesTotals.TryGetValue(customerId, out var purchases) ? purchases : 0m;
                decimal totalReturns = returnsTotals.TryGetValue(customerId, out var ret) ? ret : 0m;
                decimal availableCredit = creditLimit == 0 ? 0m : (creditLimit - currentBalance);

                reportData.Add(new CustomerBalanceReportDto
                {
                    CustomerId = customerId,
                    CustomerCode = customerId.ToString(),
                    ExternalCode = customer.ExternalCode,
                    CustomerName = customer.CustomerName ?? "",
                    AccountId = customer.AccountId,
                    AccountCode = customer.AccountCode,
                    AccountName = customer.AccountName,
                    PartyCategory = customer.PartyCategory ?? "",
                    Phone1 = customer.Phone1 ?? "",
                    CurrentBalance = currentBalance,
                    CreditLimit = creditLimit,
                    TotalSales = totalSales,
                    TotalPurchases = totalPurchases,
                    TotalReturns = totalReturns,
                    AvailableCredit = availableCredit
                });
            }

            var sep = new[] { '|', ',' };
            if (!string.IsNullOrWhiteSpace(filterCol_code))
            {
                reportData = reportData.Where(r =>
                    CustomerBalancesNumericFilter.MatchesDecimal(r.CustomerId, filterCol_code)).ToList();
            }
            if (!string.IsNullOrWhiteSpace(filterCol_name))
            {
                var vals = filterCol_name.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    reportData = reportData.Where(r => vals.Any(v => (r.CustomerName ?? "").Contains(v))).ToList();
            }
            if (!string.IsNullOrWhiteSpace(filterCol_category))
            {
                var vals = filterCol_category.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    reportData = reportData.Where(r => vals.Any(v => (r.PartyCategory ?? "").Contains(v))).ToList();
            }
            if (!string.IsNullOrWhiteSpace(filterCol_phone))
            {
                var vals = filterCol_phone.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    reportData = reportData.Where(r => vals.Any(v => (r.Phone1 ?? "").Contains(v))).ToList();
            }
            if (!string.IsNullOrWhiteSpace(filterCol_account))
            {
                var vals = filterCol_account.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    reportData = reportData.Where(r => vals.Any(v => (r.AccountDisplay ?? "").Contains(v))).ToList();
            }
            if (!string.IsNullOrWhiteSpace(filterCol_debit))
            {
                reportData = reportData.Where(r =>
                    CustomerBalancesNumericFilter.MatchesDecimal(
                        r.CurrentBalance > 0 ? r.CurrentBalance : 0m,
                        filterCol_debit)).ToList();
            }
            if (!string.IsNullOrWhiteSpace(filterCol_credit))
            {
                reportData = reportData.Where(r =>
                    CustomerBalancesNumericFilter.MatchesDecimal(
                        r.CurrentBalance < 0 ? Math.Abs(r.CurrentBalance) : 0m,
                        filterCol_credit)).ToList();
            }
            if (!string.IsNullOrWhiteSpace(filterCol_creditlimit))
            {
                reportData = reportData.Where(r =>
                    CustomerBalancesNumericFilter.MatchesDecimal(r.CreditLimit, filterCol_creditlimit)).ToList();
            }
            if (!string.IsNullOrWhiteSpace(filterCol_sales))
            {
                reportData = reportData.Where(r =>
                    CustomerBalancesNumericFilter.MatchesDecimal(r.TotalSales, filterCol_sales)).ToList();
            }
            if (!string.IsNullOrWhiteSpace(filterCol_purchases))
            {
                reportData = reportData.Where(r =>
                    CustomerBalancesNumericFilter.MatchesDecimal(r.TotalPurchases, filterCol_purchases)).ToList();
            }
            if (!string.IsNullOrWhiteSpace(filterCol_returns))
            {
                reportData = reportData.Where(r =>
                    CustomerBalancesNumericFilter.MatchesDecimal(r.TotalReturns, filterCol_returns)).ToList();
            }
            if (!string.IsNullOrWhiteSpace(filterCol_availablecredit))
            {
                reportData = reportData.Where(r =>
                    CustomerBalancesNumericFilter.MatchesDecimal(r.AvailableCredit, filterCol_availablecredit)).ToList();
            }

            bool isDesc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
            switch (sortBy?.ToLowerInvariant())
            {
                case "code":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.CustomerId).ToList()
                        : reportData.OrderBy(r => r.CustomerId).ToList();
                    break;
                case "category":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.PartyCategory ?? "").ToList()
                        : reportData.OrderBy(r => r.PartyCategory ?? "").ToList();
                    break;
                case "phone":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.Phone1 ?? "").ToList()
                        : reportData.OrderBy(r => r.Phone1 ?? "").ToList();
                    break;
                case "account":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.AccountDisplay ?? "").ToList()
                        : reportData.OrderBy(r => r.AccountDisplay ?? "").ToList();
                    break;
                case "debit":
                    decimal DebitDisplayValueExport(CustomerBalanceReportDto r) => r.CurrentBalance > 0 ? r.CurrentBalance : 0m;
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => DebitDisplayValueExport(r)).ToList()
                        : reportData.OrderBy(r => DebitDisplayValueExport(r)).ToList();
                    break;
                case "balance":
                case "credit":
                    decimal CreditDisplayValueExport(CustomerBalanceReportDto r) => r.CurrentBalance < 0 ? Math.Abs(r.CurrentBalance) : 0m;
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => CreditDisplayValueExport(r)).ToList()
                        : reportData.OrderBy(r => CreditDisplayValueExport(r)).ToList();
                    break;
                case "creditlimit":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.CreditLimit).ToList()
                        : reportData.OrderBy(r => r.CreditLimit).ToList();
                    break;
                case "sales":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.TotalSales).ToList()
                        : reportData.OrderBy(r => r.TotalSales).ToList();
                    break;
                case "purchases":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.TotalPurchases).ToList()
                        : reportData.OrderBy(r => r.TotalPurchases).ToList();
                    break;
                case "returns":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.TotalReturns).ToList()
                        : reportData.OrderBy(r => r.TotalReturns).ToList();
                    break;
                case "availablecredit":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.AvailableCredit).ToList()
                        : reportData.OrderBy(r => r.AvailableCredit).ToList();
                    break;
                default:
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.CustomerName).ToList()
                        : reportData.OrderBy(r => r.CustomerName).ToList();
                    break;
            }

            return reportData;
        }

        [HttpGet]
        [RequirePermission("Reports.CustomerBalances")]
        public async Task<IActionResult> PrintCustomerBalances(
            string? search,
            string? searchMode,
            string? partyCategory,
            int? governorateId,
            DateTime? fromDate,
            DateTime? toDate,
            bool includeZeroBalance = false,
            string? sortBy = "name",
            string? sortDir = "asc",
            string? filterCol_code = null,
            string? filterCol_name = null,
            string? filterCol_category = null,
            string? filterCol_phone = null,
            string? filterCol_account = null,
            string? filterCol_debit = null,
            string? filterCol_credit = null,
            string? filterCol_creditlimit = null,
            string? filterCol_sales = null,
            string? filterCol_purchases = null,
            string? filterCol_returns = null,
            string? filterCol_availablecredit = null,
            string? printCols = null)
        {
            string? includeZeroBalanceStr = Request.Query["includeZeroBalance"].FirstOrDefault();
            if (string.IsNullOrEmpty(includeZeroBalanceStr))
                includeZeroBalance = false;

            var reportData = await BuildCustomerBalancesReportDataForExportAsync(
                search, searchMode, partyCategory, governorateId, fromDate, toDate, includeZeroBalance,
                sortBy, sortDir,
                filterCol_code, filterCol_name, filterCol_category, filterCol_phone, filterCol_account,
                filterCol_debit, filterCol_credit, filterCol_creditlimit, filterCol_sales, filterCol_purchases, filterCol_returns, filterCol_availablecredit);

            if (reportData == null)
                reportData = new List<CustomerBalanceReportDto>();

            const int maxRows = 100_000;
            var totalBeforeCap = reportData.Count;
            if (reportData.Count > maxRows)
                reportData = reportData.Take(maxRows).ToList();

            decimal totalDebit = reportData.Sum(r => r.CurrentBalance > 0 ? r.CurrentBalance : 0m);
            decimal totalCredit = reportData.Sum(r => r.CurrentBalance < 0 ? Math.Abs(r.CurrentBalance) : 0m);
            decimal totalCreditLimit = reportData.Sum(r => r.CreditLimit);
            decimal totalSales = reportData.Sum(r => r.TotalSales);
            decimal totalPurchases = reportData.Sum(r => r.TotalPurchases);
            decimal totalReturns = reportData.Sum(r => r.TotalReturns);
            decimal totalAvailableCredit = reportData.Sum(r => r.AvailableCredit);

            ViewBag.TotalDebit = totalDebit;
            ViewBag.TotalCredit = totalCredit;
            ViewBag.TotalCreditLimit = totalCreditLimit;
            ViewBag.TotalSales = totalSales;
            ViewBag.TotalPurchases = totalPurchases;
            ViewBag.TotalReturns = totalReturns;
            ViewBag.TotalAvailableCredit = totalAvailableCredit;
            ViewBag.TotalMatching = totalBeforeCap;
            ViewBag.PrintedCount = reportData.Count;
            ViewBag.Capped = totalBeforeCap > maxRows;
            ViewBag.MaxRows = maxRows;
            ViewBag.PrintColumnKeys = ListPrintColumnParser.ParsePrintColumns(printCols, CustomerBalancesPrintColumnOrder, null);
            ViewBag.PrintColumnsFromList = !string.IsNullOrWhiteSpace(printCols);
            ViewBag.SortBy = sortBy ?? "name";
            ViewBag.SortDir = sortDir ?? "asc";

            return View("CustomerBalancesPrint", reportData);
        }
    }
}
