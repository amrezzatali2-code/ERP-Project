using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using ERP.Filters;
using ERP.Infrastructure;
using ERP.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ERP.Controllers
{
    public partial class ReportsController
    {
        [HttpGet]
        [RequirePermission("Reports.BonusReport")]
        public async Task<IActionResult> BonusReport(
            DateTime? fromDate,
            DateTime? toDate,
            int? warehouseId,
            bool loadReport = false,
            bool groupByUser = false,
            string? sort = null,
            string? dir = "desc",
            int page = 1,
            int pageSize = 10,
            string? filterCol_user = null,
            string? filterCol_prodname = null,
            string? filterCol_bonusgroup = null,
            string? filterCol_bonusperunitExpr = null,
            string? filterCol_qtyExpr = null,
            string? filterCol_salesExpr = null,
            string? filterCol_bonusExpr = null)
        {
            var pageSizeQuery = Request.Query["pageSize"].LastOrDefault();
            if (!string.IsNullOrEmpty(pageSizeQuery) && int.TryParse(pageSizeQuery, out var psVal))
                pageSize = psVal;
            if (pageSize < 0)
                pageSize = 10;
            if (pageSize > 0 && pageSize != 10 && pageSize != 25 && pageSize != 50 && pageSize != 100 && pageSize != 200)
                pageSize = 10;

            var warehouses = await _context.Warehouses
                .AsNoTracking()
                .OrderBy(w => w.WarehouseName)
                .Select(w => new SelectListItem
                {
                    Value = w.WarehouseId.ToString(),
                    Text = w.WarehouseName
                })
                .ToListAsync();
            ViewBag.Warehouses = warehouses;
            ViewBag.FromDate = fromDate;
            ViewBag.ToDate = toDate;
            ViewBag.WarehouseId = warehouseId;
            ViewBag.GroupByUser = groupByUser;
            ViewBag.Sort = sort ?? "TotalSalesValue";
            ViewBag.Dir = dir ?? "desc";
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.FilterCol_User = filterCol_user;
            ViewBag.FilterCol_Prodname = filterCol_prodname;
            ViewBag.FilterCol_Bonusgroup = filterCol_bonusgroup;
            ViewBag.FilterCol_BonusperunitExpr = filterCol_bonusperunitExpr;
            ViewBag.FilterCol_QtyExpr = filterCol_qtyExpr;
            ViewBag.FilterCol_SalesExpr = filterCol_salesExpr;
            ViewBag.FilterCol_BonusExpr = filterCol_bonusExpr;

            if (!loadReport)
            {
                ViewBag.ReportLoaded = false;
                ViewBag.ReportDataDetail = new List<BonusReportDto>();
                ViewBag.ReportDataByUser = new List<BonusReportByUserDto>();
                ViewBag.TotalCount = 0;
                ViewBag.TotalPages = 0;
                ViewBag.TotalQtyFiltered = 0m;
                ViewBag.TotalSalesFiltered = 0m;
                ViewBag.TotalBonusFiltered = 0m;
                return View("BonusReport");
            }

            ViewBag.ReportLoaded = true;

            var today = DateTime.Today;
            if (!fromDate.HasValue && !toDate.HasValue)
            {
                fromDate = new DateTime(today.Year, today.Month, 1);
                toDate = today;
                ViewBag.FromDate = fromDate;
                ViewBag.ToDate = toDate;
            }

            var from = fromDate?.Date ?? DateTime.MinValue;
            var toExclusive = (toDate?.Date ?? DateTime.MaxValue).AddDays(1);

            var baseLines = BonusReportQuery.BuildBaseLines(_context, from, toExclusive, warehouseId);

            if (groupByUser)
            {
                var qUser = BonusReportQuery.ToByUserGrouped(baseLines);
                qUser = BonusReportQuery.ApplyByUserTextFilters(qUser, filterCol_user);
                qUser = BonusReportQuery.ApplyByUserNumericFilters(qUser, filterCol_qtyExpr, filterCol_salesExpr, filterCol_bonusExpr);
                qUser = BonusReportQuery.ApplyByUserSort(qUser, sort, dir);

                var totalCount = await qUser.CountAsync();
                var totalQtyFiltered = (decimal)await qUser.SumAsync(x => x.TotalQty);
                var totalSalesFiltered = await qUser.SumAsync(x => x.TotalSalesValue);
                var totalBonusFiltered = await qUser.SumAsync(x => x.TotalBonusAmount);

                var skip = Math.Max(0, (page - 1) * pageSize);
                var take = pageSize;
                if (pageSize == 0)
                {
                    skip = 0;
                    take = totalCount == 0 ? 10 : Math.Min(totalCount, 100_000);
                    page = 1;
                }

                var rows = await qUser.Skip(skip).Take(take).ToListAsync();

                ViewBag.ReportDataDetail = new List<BonusReportDto>();
                ViewBag.ReportDataByUser = rows;
                ViewBag.TotalCount = totalCount;
                ViewBag.TotalPages = pageSize == 0 ? 1 : Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
                ViewBag.TotalQtyFiltered = totalQtyFiltered;
                ViewBag.TotalSalesFiltered = totalSalesFiltered;
                ViewBag.TotalBonusFiltered = totalBonusFiltered;
            }
            else
            {
                var qDet = BonusReportQuery.ToDetailGrouped(baseLines);
                qDet = BonusReportQuery.ApplyDetailTextFilters(qDet, filterCol_user, filterCol_prodname, filterCol_bonusgroup);
                qDet = BonusReportQuery.ApplyDetailNumericFilters(qDet, filterCol_bonusperunitExpr, filterCol_qtyExpr, filterCol_salesExpr, filterCol_bonusExpr);
                qDet = BonusReportQuery.ApplyDetailSort(qDet, sort, dir);

                var totalCount = await qDet.CountAsync();
                var totalQtyFiltered = await qDet.SumAsync(x => (decimal)x.TotalQty);
                var totalSalesFiltered = await qDet.SumAsync(x => x.TotalSalesValue);
                var totalBonusFiltered = await qDet.SumAsync(x => x.TotalBonusAmount);

                var skip = Math.Max(0, (page - 1) * pageSize);
                var take = pageSize;
                if (pageSize == 0)
                {
                    skip = 0;
                    take = totalCount == 0 ? 10 : Math.Min(totalCount, 100_000);
                    page = 1;
                }

                var rows = await qDet.Skip(skip).Take(take).ToListAsync();

                ViewBag.ReportDataDetail = rows;
                ViewBag.ReportDataByUser = new List<BonusReportByUserDto>();
                ViewBag.TotalCount = totalCount;
                ViewBag.TotalPages = pageSize == 0 ? 1 : Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
                ViewBag.TotalQtyFiltered = totalQtyFiltered;
                ViewBag.TotalSalesFiltered = totalSalesFiltered;
                ViewBag.TotalBonusFiltered = totalBonusFiltered;
            }

            ViewBag.Page = page;
            return View("BonusReport");
        }

        [HttpGet]
        [RequirePermission("Reports.BonusReport")]
        public async Task<IActionResult> GetBonusReportColumnValues(
            string column,
            string? search = null,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? warehouseId = null,
            bool groupByUser = false,
            string? filterCol_user = null,
            string? filterCol_prodname = null,
            string? filterCol_bonusgroup = null,
            string? filterCol_bonusperunitExpr = null,
            string? filterCol_qtyExpr = null,
            string? filterCol_salesExpr = null,
            string? filterCol_bonusExpr = null)
        {
            var col = (column ?? "").Trim().ToLowerInvariant();
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();

            var today = DateTime.Today;
            if (!fromDate.HasValue && !toDate.HasValue)
            {
                fromDate = new DateTime(today.Year, today.Month, 1);
                toDate = today;
            }

            var from = fromDate?.Date ?? DateTime.MinValue;
            var toExclusive = (toDate?.Date ?? DateTime.MaxValue).AddDays(1);

            var baseLines = BonusReportQuery.BuildBaseLines(_context, from, toExclusive, warehouseId);

            if (groupByUser)
            {
                var q = BonusReportQuery.ToByUserGrouped(baseLines);
                q = BonusReportQuery.ApplyByUserTextFilters(q, filterCol_user);
                q = BonusReportQuery.ApplyByUserNumericFilters(q, filterCol_qtyExpr, filterCol_salesExpr, filterCol_bonusExpr);

                if (col == "user")
                {
                    var list = await q.Select(x => x.UserName).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                    if (!string.IsNullOrEmpty(searchTerm))
                        list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                    return Json(list.Select(v => new { value = v, display = string.IsNullOrEmpty(v) ? "—" : v }));
                }

                return Json(Array.Empty<object>());
            }

            var qd = BonusReportQuery.ToDetailGrouped(baseLines);
            qd = BonusReportQuery.ApplyDetailTextFilters(qd, filterCol_user, filterCol_prodname, filterCol_bonusgroup);
            qd = BonusReportQuery.ApplyDetailNumericFilters(qd, filterCol_bonusperunitExpr, filterCol_qtyExpr, filterCol_salesExpr, filterCol_bonusExpr);

            if (col == "user")
            {
                var list = await qd.Select(x => x.UserName).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm))
                    list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = string.IsNullOrEmpty(v) ? "—" : v }));
            }

            if (col == "prodname")
            {
                var list = await qd.Where(x => x.ProdName != null && x.ProdName != "").Select(x => x.ProdName).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm))
                    list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v.Length > 60 ? v.Substring(0, 60) + "…" : v }));
            }

            if (col == "bonusgroup")
            {
                var list = await qd.Where(x => x.ProductBonusGroupName != null && x.ProductBonusGroupName != "").Select(x => x.ProductBonusGroupName).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm))
                    list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }

            return Json(Array.Empty<object>());
        }

        [HttpGet]
        [RequirePermission("Reports.BonusReport")]
        public async Task<IActionResult> ExportBonusReport(
            DateTime? fromDate,
            DateTime? toDate,
            int? warehouseId,
            bool groupByUser = false,
            string? sort = null,
            string? dir = "desc",
            string? filterCol_user = null,
            string? filterCol_prodname = null,
            string? filterCol_bonusgroup = null,
            string? filterCol_bonusperunitExpr = null,
            string? filterCol_qtyExpr = null,
            string? filterCol_salesExpr = null,
            string? filterCol_bonusExpr = null)
        {
            var today = DateTime.Today;
            if (!fromDate.HasValue && !toDate.HasValue)
            {
                fromDate = new DateTime(today.Year, today.Month, 1);
                toDate = today;
            }

            var from = fromDate?.Date ?? DateTime.MinValue;
            var toExclusive = (toDate?.Date ?? DateTime.MaxValue).AddDays(1);

            var baseLines = BonusReportQuery.BuildBaseLines(_context, from, toExclusive, warehouseId);

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("تقرير البونص"));
            int row = 1;

            if (groupByUser)
            {
                var q = BonusReportQuery.ToByUserGrouped(baseLines);
                q = BonusReportQuery.ApplyByUserTextFilters(q, filterCol_user);
                q = BonusReportQuery.ApplyByUserNumericFilters(q, filterCol_qtyExpr, filterCol_salesExpr, filterCol_bonusExpr);
                q = BonusReportQuery.ApplyByUserSort(q, sort, dir);
                var data = await q.Take(100_000).ToListAsync();

                ws.Cell(row, 1).Value = "المستخدم";
                ws.Cell(row, 2).Value = "الكمية";
                ws.Cell(row, 3).Value = "إجمالي المبيعات";
                ws.Cell(row, 4).Value = "قيمة البونص";
                row++;
                foreach (var x in data)
                {
                    ws.Cell(row, 1).Value = x.UserName;
                    ws.Cell(row, 2).Value = x.TotalQty;
                    ws.Cell(row, 3).Value = x.TotalSalesValue;
                    ws.Cell(row, 4).Value = x.TotalBonusAmount;
                    row++;
                }
            }
            else
            {
                var q = BonusReportQuery.ToDetailGrouped(baseLines);
                q = BonusReportQuery.ApplyDetailTextFilters(q, filterCol_user, filterCol_prodname, filterCol_bonusgroup);
                q = BonusReportQuery.ApplyDetailNumericFilters(q, filterCol_bonusperunitExpr, filterCol_qtyExpr, filterCol_salesExpr, filterCol_bonusExpr);
                q = BonusReportQuery.ApplyDetailSort(q, sort, dir);
                var data = await q.Take(100_000).ToListAsync();

                ws.Cell(row, 1).Value = "المستخدم";
                ws.Cell(row, 2).Value = "اسم الصنف";
                ws.Cell(row, 3).Value = "مجموعة البونص";
                ws.Cell(row, 4).Value = "البونص لكل علبة";
                ws.Cell(row, 5).Value = "الكمية";
                ws.Cell(row, 6).Value = "إجمالي المبيعات";
                ws.Cell(row, 7).Value = "قيمة البونص";
                row++;
                foreach (var x in data)
                {
                    ws.Cell(row, 1).Value = x.UserName;
                    ws.Cell(row, 2).Value = x.ProdName;
                    ws.Cell(row, 3).Value = x.ProductBonusGroupName;
                    ws.Cell(row, 4).Value = x.BonusAmountPerUnit;
                    ws.Cell(row, 5).Value = x.TotalQty;
                    ws.Cell(row, 6).Value = x.TotalSalesValue;
                    ws.Cell(row, 7).Value = x.TotalBonusAmount;
                    row++;
                }
            }

            using var stream = new System.IO.MemoryStream();
            workbook.SaveAs(stream);
            var fileName = ExcelExportNaming.ArabicTimestampedFileName("تقرير_البونص", ".xlsx");
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }
    }
}
