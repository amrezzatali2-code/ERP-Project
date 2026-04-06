using ERP.Data;
using ERP.Filters;
using ERP.Infrastructure;
using ERP.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ERP.Controllers
{
    public partial class CustomersController
    {
        /// <summary>نفس فلاتر وترتيب قائمة العملاء (Index) بدون ترقيم — للطباعة والاستعلامات.</summary>
        private async Task<IQueryable<Customer>> BuildCustomerListOrderedQueryAsync(
            string? search,
            string? searchBy,
            string? searchMode,
            string? sort,
            string? dir,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            int? fromCode,
            int? toCode,
            string? filterCol_id,
            string? filterCol_idExpr,
            string? filterCol_name,
            string? filterCol_type,
            string? filterCol_phone,
            string? filterCol_Address,
            string? filterCol_governorate,
            string? filterCol_district,
            string? filterCol_area,
            string? filterCol_taxid,
            string? filterCol_recordnumber,
            string? filterCol_licensenumber,
            string? filterCol_segment,
            string? filterCol_account,
            string? filterCol_PolicyId,
            string? filterCol_credit,
            string? filterCol_isactive,
            string? filterCol_ordercontact,
            string? filterCol_created,
            string? filterCol_updated,
            string? filterCol_quota)
        {
            var sep = new[] { '|', ',' };
            IQueryable<Customer> q = _context.Customers
                .Include(c => c.Account)
                .Include(c => c.Governorate)
                .Include(c => c.District)
                .Include(c => c.Area)
                .AsNoTracking();

            q = await _accountVisibilityService.ApplyCustomerVisibilityFilterAsync(q);

            var s = (search ?? string.Empty).Trim();
            var sb = (searchBy ?? "all").Trim().ToLowerInvariant();
            var sm = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (sm != "starts" && sm != "ends") sm = "contains";
            var so = (sort ?? "name").Trim().ToLowerInvariant();
            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            q = ApplyCustomerListSearch(q, s, sb, sm);

            if (fromCode.HasValue)
                q = q.Where(c => c.CustomerId >= fromCode.Value);

            if (toCode.HasValue)
                q = q.Where(c => c.CustomerId <= toCode.Value);

            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var id) ? id : (int?)null).Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    q = q.Where(c => ids.Contains(c.CustomerId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_idExpr))
            {
                var expr = filterCol_idExpr.Trim();
                if (expr.StartsWith("<") && int.TryParse(expr.AsSpan(1).Trim(), out var maxId))
                    q = q.Where(c => c.CustomerId < maxId);
                else if (expr.StartsWith(">") && int.TryParse(expr.AsSpan(1).Trim(), out var minId))
                    q = q.Where(c => c.CustomerId > minId);
                else if (expr.Contains(":") && int.TryParse(expr.Split(':')[0].Trim(), out var fromId) && int.TryParse(expr.Split(':')[1].Trim(), out var toId))
                    q = q.Where(c => c.CustomerId >= fromId && c.CustomerId <= toId);
                else if (int.TryParse(expr, out var exactId))
                    q = q.Where(c => c.CustomerId == exactId);
            }
            if (!string.IsNullOrWhiteSpace(filterCol_name))
            {
                var vals = filterCol_name.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    q = q.Where(c => c.CustomerName != null && vals.Any(v => c.CustomerName.Contains(v)));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_type))
            {
                var vals = filterCol_type.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    q = q.Where(c => c.PartyCategory != null && vals.Contains(c.PartyCategory));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_phone))
            {
                var vals = filterCol_phone.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    q = q.Where(c => vals.Any(v => (c.Phone1 != null && c.Phone1.Contains(v)) || (c.Phone2 != null && c.Phone2.Contains(v)) || (c.Whatsapp != null && c.Whatsapp.Contains(v))));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_Address))
            {
                var vals = filterCol_Address.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    q = q.Where(c => c.Address != null && vals.Any(v => c.Address.Contains(v)));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_governorate))
            {
                var vals = filterCol_governorate.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    q = q.Where(c => c.Governorate != null && c.Governorate.GovernorateName != null && vals.Any(v => c.Governorate.GovernorateName.Contains(v)));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_district))
            {
                var vals = filterCol_district.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    q = q.Where(c => c.District != null && c.District.DistrictName != null && vals.Any(v => c.District.DistrictName.Contains(v)));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_area))
            {
                var vals = filterCol_area.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    q = q.Where(c => (c.Area != null && c.Area.AreaName != null && vals.Any(v => c.Area.AreaName.Contains(v)))
                        || (c.RegionName != null && vals.Any(v => c.RegionName.Contains(v))));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_taxid))
            {
                var vals = filterCol_taxid.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    q = q.Where(c => c.TaxIdOrNationalId != null && vals.Any(v => c.TaxIdOrNationalId.Contains(v)));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_recordnumber))
            {
                var vals = filterCol_recordnumber.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    q = q.Where(c => c.RecordNumber != null && vals.Any(v => c.RecordNumber.Contains(v)));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_licensenumber))
            {
                var vals = filterCol_licensenumber.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    q = q.Where(c => c.LicenseNumber != null && vals.Any(v => c.LicenseNumber.Contains(v)));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_segment))
            {
                var vals = filterCol_segment.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    q = q.Where(c => c.Segment != null && vals.Any(v => c.Segment.Contains(v)));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_account))
            {
                // GetColumnValues وعمود الجدول يعرضان "كود — اسم" (نفس فاصل PopulateDropDowns في العملاء).
                // الفلتر كان يطبّق Contains على الكود والاسم منفصلين فلا يطابق القيمة الكاملة من لوحة الفلتر.
                var vals = filterCol_account.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    q = q.Where(c => c.Account != null && vals.Any(v =>
                        (c.Account.AccountCode ?? "") + " — " + (c.Account.AccountName ?? "") == v
                        || (c.Account.AccountCode != null && c.Account.AccountCode.Contains(v))
                        || (c.Account.AccountName != null && c.Account.AccountName.Contains(v))));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_PolicyId))
            {
                var vals = filterCol_PolicyId.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    q = q.Where(c => c.PolicyId.HasValue && vals.Contains(c.PolicyId.Value.ToString()));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_credit))
            {
                var vals = filterCol_credit.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                {
                    var decimals = vals.Select(x => decimal.TryParse(x, out var d) ? d : (decimal?)null).Where(x => x.HasValue).Select(x => x!.Value).ToList();
                    if (decimals.Count > 0)
                        q = q.Where(c => decimals.Contains(c.CreditLimit));
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_isactive))
            {
                var vals = filterCol_isactive.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                var activeList = new List<bool>();
                foreach (var v in vals)
                {
                    if (new[] { "نشط", "1", "yes", "true" }.Contains(v, StringComparer.OrdinalIgnoreCase)) activeList.Add(true);
                    else if (new[] { "موقوف", "0", "no", "false" }.Contains(v, StringComparer.OrdinalIgnoreCase)) activeList.Add(false);
                }
                if (activeList.Count > 0)
                    q = q.Where(c => activeList.Contains(c.IsActive));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_ordercontact))
            {
                var vals = filterCol_ordercontact.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    q = q.Where(c => c.OrderContactName != null && vals.Any(v => c.OrderContactName.Contains(v)));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_created))
            {
                var dateParts = filterCol_created.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                foreach (var part in dateParts)
                {
                    if (part.Length >= 7 && part.Contains("-") && int.TryParse(part.AsSpan(0, 4), out var y) && int.TryParse(part.AsSpan(5, 2), out var m))
                    {
                        var from = new DateTime(y, m, 1, 0, 0, 0);
                        var to = from.AddMonths(1).AddTicks(-1);
                        q = q.Where(c => c.CreatedAt >= from && c.CreatedAt <= to);
                        break;
                    }
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_updated))
            {
                var dateParts = filterCol_updated.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                foreach (var part in dateParts)
                {
                    if (part.Length >= 7 && part.Contains("-") && int.TryParse(part.AsSpan(0, 4), out var y) && int.TryParse(part.AsSpan(5, 2), out var m))
                    {
                        var from = new DateTime(y, m, 1, 0, 0, 0);
                        var to = from.AddMonths(1).AddTicks(-1);
                        q = q.Where(c => c.UpdatedAt >= from && c.UpdatedAt <= to);
                        break;
                    }
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_quota))
            {
                var vals = filterCol_quota.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                {
                    var hasMulti = vals.Any(v => new[] { "مفعّل", "مفعّلة", "نعم", "yes", "1" }.Contains(v, StringComparer.OrdinalIgnoreCase));
                    var hasNone = vals.Any(v => new[] { "غير مفعّلة", "لا", "no", "0" }.Contains(v, StringComparer.OrdinalIgnoreCase));
                    if (hasMulti && !hasNone) q = q.Where(c => c.IsQuotaMultiplierEnabled);
                    else if (hasNone && !hasMulti) q = q.Where(c => !c.IsQuotaMultiplierEnabled);
                }
            }

            if (useDateRange)
            {
                if (fromDate.HasValue)
                    q = q.Where(c => c.CreatedAt >= fromDate.Value);

                if (toDate.HasValue)
                    q = q.Where(c => c.CreatedAt <= toDate.Value);
            }

            q = so switch
            {
                "id" => desc ? q.OrderByDescending(c => c.CustomerId)
                             : q.OrderBy(c => c.CustomerId),

                "name" => desc ? q.OrderByDescending(c => c.CustomerName)
                               : q.OrderBy(c => c.CustomerName),

                "type" => desc ? q.OrderByDescending(c => c.PartyCategory)
                               : q.OrderBy(c => c.PartyCategory),

                "governorate" => desc ? q.OrderByDescending(c => c.Governorate != null ? c.Governorate.GovernorateName : "")
                                      : q.OrderBy(c => c.Governorate != null ? c.Governorate.GovernorateName : ""),
                "district" => desc ? q.OrderByDescending(c => c.District != null ? c.District.DistrictName : "")
                                   : q.OrderBy(c => c.District != null ? c.District.DistrictName : ""),
                "area" => desc ? q.OrderByDescending(c => c.RegionName ?? (c.Area != null ? c.Area.AreaName : "") ?? "")
                              : q.OrderBy(c => c.RegionName ?? (c.Area != null ? c.Area.AreaName : "") ?? ""),

                "taxid" => desc ? q.OrderByDescending(c => c.TaxIdOrNationalId ?? "")
                                 : q.OrderBy(c => c.TaxIdOrNationalId ?? ""),
                "recordnumber" => desc ? q.OrderByDescending(c => c.RecordNumber ?? "")
                                       : q.OrderBy(c => c.RecordNumber ?? ""),
                "licensenumber" => desc ? q.OrderByDescending(c => c.LicenseNumber ?? "")
                                        : q.OrderBy(c => c.LicenseNumber ?? ""),
                "segment" => desc ? q.OrderByDescending(c => c.Segment ?? "")
                                  : q.OrderBy(c => c.Segment ?? ""),

                "account" => desc
                    ? q.OrderByDescending(c => c.Account != null ? c.Account.AccountCode : "")
                    : q.OrderBy(c => c.Account != null ? c.Account.AccountCode : ""),

                "isactive" => desc ? q.OrderByDescending(c => c.IsActive)
                                   : q.OrderBy(c => c.IsActive),

                "created" => desc ? q.OrderByDescending(c => c.CreatedAt)
                                  : q.OrderBy(c => c.CreatedAt),

                "updated" => desc ? q.OrderByDescending(c => c.UpdatedAt)
                                  : q.OrderBy(c => c.UpdatedAt),

                _ => desc ? q.OrderByDescending(c => c.CustomerName)
                          : q.OrderBy(c => c.CustomerName),
            };

            return q;
        }

        /// <summary>ترتيب ثابت لأعمدة الطباعة (مطابق لعناوين القائمة). يُفلتر بـ printCols من الطلب.</summary>
        private static readonly string[] CustomerPrintColumnOrder =
        {
            "id", "name", "type", "phone", "Address", "governorate", "district", "area",
            "taxid", "recordnumber", "licensenumber", "segment", "account", "PolicyId",
            "credit", "isactive", "ordercontact", "created", "updated", "quota"
        };

        /// <summary>أسماء بديلة لـ printCols (مثلاً policy → PolicyId).</summary>
        private static readonly IReadOnlyDictionary<string, string> CustomerPrintKeyAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["policy"] = "PolicyId" };

        /// <summary>طباعة قائمة العملاء بكل النتائج المطابقة للفلتر الحالي (وليس الصفحة المعروضة فقط).</summary>
        [HttpGet]
        [RequirePermission("Customers.Index")]
        public async Task<IActionResult> Print(
            string? search,
            string? searchBy = "all",
            string? searchMode = "contains",
            string? sort = "name",
            string? dir = "asc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_id = null,
            string? filterCol_idExpr = null,
            string? filterCol_name = null,
            string? filterCol_type = null,
            string? filterCol_phone = null,
            string? filterCol_Address = null,
            string? filterCol_governorate = null,
            string? filterCol_district = null,
            string? filterCol_area = null,
            string? filterCol_account = null,
            string? filterCol_PolicyId = null,
            string? filterCol_credit = null,
            string? filterCol_isactive = null,
            string? filterCol_ordercontact = null,
            string? filterCol_created = null,
            string? filterCol_updated = null,
            string? filterCol_quota = null,
            string? filterCol_taxid = null,
            string? filterCol_recordnumber = null,
            string? filterCol_licensenumber = null,
            string? filterCol_segment = null,
            string? printCols = null)
        {
            var q = await BuildCustomerListOrderedQueryAsync(
                search, searchBy, searchMode, sort, dir,
                useDateRange, fromDate, toDate, fromCode, toCode,
                filterCol_id, filterCol_idExpr, filterCol_name, filterCol_type, filterCol_phone,
                filterCol_Address, filterCol_governorate, filterCol_district, filterCol_area,
                filterCol_taxid, filterCol_recordnumber, filterCol_licensenumber, filterCol_segment,
                filterCol_account, filterCol_PolicyId, filterCol_credit, filterCol_isactive,
                filterCol_ordercontact, filterCol_created, filterCol_updated, filterCol_quota);

            var totalMatching = await q.CountAsync();
            const int maxRows = 100_000;
            var items = await q.Take(maxRows).ToListAsync();

            ViewBag.TotalMatching = totalMatching;
            ViewBag.PrintedCount = items.Count;
            ViewBag.Capped = totalMatching > maxRows;
            ViewBag.MaxRows = maxRows;
            ViewBag.Sort = (sort ?? "name").Trim().ToLowerInvariant();
            ViewBag.Dir = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase) ? "desc" : "asc";
            ViewBag.SearchSummary = (search ?? string.Empty).Trim();
            ViewBag.PartyCategoryDisplayNames = PartyCategoryDisplay.ArabicByKey;
            ViewBag.PrintColumnKeys = ListPrintColumnParser.ParsePrintColumns(
                printCols, CustomerPrintColumnOrder, CustomerPrintKeyAliases);
            ViewBag.PrintColumnsFromList = !string.IsNullOrWhiteSpace(printCols);

            return View("Print", items);
        }
    }
}
