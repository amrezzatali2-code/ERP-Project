using System;
using System.Collections.Generic;
using System.Linq;
using ERP.Data;
using ERP.Models;
using ERP.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure
{
    /// <summary>
    /// بناء استعلامات تقرير البونص (تفصيلي / تجميع حسب مستخدم).
    /// </summary>
    public static class BonusReportQuery
    {
        private static readonly char[] FilterSep = { '|', ',', ';' };

        public static List<string> ParseFilterStrings(string? filterCol)
        {
            if (string.IsNullOrWhiteSpace(filterCol)) return new List<string>();
            return filterCol.Split(FilterSep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
        }

        public static IQueryable<SalesInvoiceLine> BuildBaseLines(AppDbContext db, DateTime from, DateTime toExclusive, int? warehouseId)
        {
            var query = db.SalesInvoiceLines
                .AsNoTracking()
                .Include(sil => sil.SalesInvoice)
                .Include(sil => sil.Product)
                    .ThenInclude(p => p!.ProductBonusGroup)
                .Where(sil =>
                    sil.SalesInvoice != null &&
                    sil.SalesInvoice.IsPosted &&
                    sil.Product != null &&
                    sil.Product.ProductBonusGroupId != null);

            if (warehouseId.HasValue && warehouseId.Value > 0)
                query = query.Where(sil => sil.SalesInvoice!.WarehouseId == warehouseId.Value);

            query = query.Where(sil => sil.SalesInvoice!.SIDate >= from && sil.SalesInvoice.SIDate < toExclusive);
            return query;
        }

        public static IQueryable<BonusReportDto> ToDetailGrouped(IQueryable<SalesInvoiceLine> lines)
        {
            return lines
                .GroupBy(sil => new { UserName = sil.SalesInvoice!.CreatedBy ?? "", ProdId = sil.ProdId })
                .Select(g => new BonusReportDto
                {
                    UserName = g.Key.UserName,
                    ProdName = g.Max(sil => sil.Product != null ? (sil.Product.ProdName ?? "") : ""),
                    ProductBonusGroupName = g.Max(sil => sil.Product != null && sil.Product.ProductBonusGroup != null ? sil.Product.ProductBonusGroup.Name : ""),
                    BonusAmountPerUnit = g.Max(sil => sil.Product != null && sil.Product.ProductBonusGroup != null ? sil.Product.ProductBonusGroup.BonusAmount : 0m),
                    TotalQty = g.Sum(sil => sil.Qty),
                    TotalSalesValue = g.Sum(sil => sil.LineNetTotal),
                    TotalBonusAmount = g.Sum(sil => sil.Qty * (sil.Product != null && sil.Product.ProductBonusGroup != null ? sil.Product.ProductBonusGroup.BonusAmount : 0m))
                });
        }

        public static IQueryable<BonusReportByUserDto> ToByUserGrouped(IQueryable<SalesInvoiceLine> lines)
        {
            return lines
                .GroupBy(sil => sil.SalesInvoice!.CreatedBy ?? "")
                .Select(g => new BonusReportByUserDto
                {
                    UserName = g.Key,
                    TotalQty = g.Sum(sil => sil.Qty),
                    TotalSalesValue = g.Sum(sil => sil.LineNetTotal),
                    TotalBonusAmount = g.Sum(sil => sil.Qty * (sil.Product != null && sil.Product.ProductBonusGroup != null ? sil.Product.ProductBonusGroup.BonusAmount : 0m))
                });
        }

        public static IQueryable<BonusReportDto> ApplyDetailTextFilters(
            IQueryable<BonusReportDto> q,
            string? filterCol_user,
            string? filterCol_prodname,
            string? filterCol_bonusgroup)
        {
            var userVals = ParseFilterStrings(filterCol_user);
            if (userVals.Count > 0)
                q = q.Where(x => userVals.Contains(x.UserName));

            var prodVals = ParseFilterStrings(filterCol_prodname);
            if (prodVals.Count > 0)
                q = q.Where(x => x.ProdName != null && prodVals.Any(v => x.ProdName.Contains(v)));

            var bgVals = ParseFilterStrings(filterCol_bonusgroup);
            if (bgVals.Count > 0)
                q = q.Where(x => x.ProductBonusGroupName != null && bgVals.Any(v => x.ProductBonusGroupName.Contains(v)));

            return q;
        }

        public static IQueryable<BonusReportDto> ApplyDetailNumericFilters(
            IQueryable<BonusReportDto> q,
            string? filterCol_bonusperunitExpr,
            string? filterCol_qtyExpr,
            string? filterCol_salesExpr,
            string? filterCol_bonusExpr)
        {
            q = BonusReportListNumericExpr.ApplyBonusPerUnitExpr(q, filterCol_bonusperunitExpr);
            q = BonusReportListNumericExpr.ApplyQtyExpr(q, filterCol_qtyExpr);
            q = BonusReportListNumericExpr.ApplySalesExpr(q, filterCol_salesExpr);
            q = BonusReportListNumericExpr.ApplyBonusValueExpr(q, filterCol_bonusExpr);
            return q;
        }

        public static IQueryable<BonusReportDto> ApplyDetailSort(IQueryable<BonusReportDto> q, string? sort, string? dir)
        {
            var key = (sort ?? "TotalSalesValue").Trim();
            var asc = string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase);
            return key.ToLowerInvariant() switch
            {
                "username" => asc ? q.OrderBy(x => x.UserName).ThenBy(x => x.ProdName) : q.OrderByDescending(x => x.UserName).ThenByDescending(x => x.ProdName),
                "prodname" => asc ? q.OrderBy(x => x.ProdName).ThenBy(x => x.UserName) : q.OrderByDescending(x => x.ProdName).ThenByDescending(x => x.UserName),
                "bonusgroup" or "productbonusgroupname" => asc ? q.OrderBy(x => x.ProductBonusGroupName).ThenBy(x => x.UserName) : q.OrderByDescending(x => x.ProductBonusGroupName).ThenByDescending(x => x.UserName),
                "bonusamountperunit" or "bonusperunit" => asc ? q.OrderBy(x => x.BonusAmountPerUnit).ThenByDescending(x => x.TotalSalesValue) : q.OrderByDescending(x => x.BonusAmountPerUnit).ThenByDescending(x => x.TotalSalesValue),
                "totalqty" or "qty" => asc ? q.OrderBy(x => x.TotalQty).ThenByDescending(x => x.TotalSalesValue) : q.OrderByDescending(x => x.TotalQty).ThenByDescending(x => x.TotalSalesValue),
                "totalsalesvalue" or "sales" => asc ? q.OrderBy(x => x.TotalSalesValue) : q.OrderByDescending(x => x.TotalSalesValue),
                "totalbonusamount" or "bonus" => asc ? q.OrderBy(x => x.TotalBonusAmount) : q.OrderByDescending(x => x.TotalBonusAmount),
                _ => asc ? q.OrderBy(x => x.TotalSalesValue) : q.OrderByDescending(x => x.TotalSalesValue)
            };
        }

        public static IQueryable<BonusReportByUserDto> ApplyByUserTextFilters(IQueryable<BonusReportByUserDto> q, string? filterCol_user)
        {
            var userVals = ParseFilterStrings(filterCol_user);
            if (userVals.Count > 0)
                q = q.Where(x => userVals.Contains(x.UserName));
            return q;
        }

        public static IQueryable<BonusReportByUserDto> ApplyByUserNumericFilters(
            IQueryable<BonusReportByUserDto> q,
            string? filterCol_qtyExpr,
            string? filterCol_salesExpr,
            string? filterCol_bonusExpr)
        {
            q = BonusReportListNumericExpr.ApplyQtyExprUser(q, filterCol_qtyExpr);
            q = BonusReportListNumericExpr.ApplySalesExprUser(q, filterCol_salesExpr);
            q = BonusReportListNumericExpr.ApplyBonusValueExprUser(q, filterCol_bonusExpr);
            return q;
        }

        public static IQueryable<BonusReportByUserDto> ApplyByUserSort(IQueryable<BonusReportByUserDto> q, string? sort, string? dir)
        {
            var key = (sort ?? "TotalSalesValue").Trim();
            var asc = string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase);
            return key.ToLowerInvariant() switch
            {
                "username" => asc ? q.OrderBy(x => x.UserName) : q.OrderByDescending(x => x.UserName),
                "totalqty" or "qty" => asc ? q.OrderBy(x => x.TotalQty) : q.OrderByDescending(x => x.TotalQty),
                "totalsalesvalue" or "sales" => asc ? q.OrderBy(x => x.TotalSalesValue) : q.OrderByDescending(x => x.TotalSalesValue),
                "totalbonusamount" or "bonus" => asc ? q.OrderBy(x => x.TotalBonusAmount) : q.OrderByDescending(x => x.TotalBonusAmount),
                _ => asc ? q.OrderBy(x => x.TotalSalesValue) : q.OrderByDescending(x => x.TotalSalesValue)
            };
        }
    }
}
