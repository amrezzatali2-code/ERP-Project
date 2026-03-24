using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ERP.Data;
using ERP.Infrastructure;
using ERP.Models;

namespace ERP.Controllers
{
    /// <summary>
    /// قائمة سياسات الشراء (PurchasePolicyRules) — عرض بيانات الجدول بنمط قوائم الـ ERP.
    /// </summary>
    public class PurchasePolicyRulesController : Controller
    {
        private readonly AppDbContext _db;
        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

        public PurchasePolicyRulesController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "all",
            string? sort = "Id",
            string? dir = "desc",
            int page = 1,
            int pageSize = 50,
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_id = null,
            string? filterCol_enabled = null)
        {
            IQueryable<PurchasePolicyRule> q = _db.PurchasePolicyRules.AsNoTracking();

            var s = (search ?? "").Trim();
            var so = (sort ?? "Id").Trim();
            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(s))
            {
                if (searchBy == "id" && int.TryParse(s, out int idVal))
                    q = q.Where(x => x.Id == idVal);
                else
                    q = q.Where(x => x.Id.ToString().Contains(s) || (x.Enabled && s.Contains("نعم")) || (!x.Enabled && s.Contains("لا")));
            }

            if (fromCode.HasValue) q = q.Where(x => x.Id >= fromCode.Value);
            if (toCode.HasValue) q = q.Where(x => x.Id <= toCode.Value);

            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null).Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0) q = q.Where(x => ids.Contains(x.Id));
            }

            q = so switch
            {
                "Id" => desc ? q.OrderByDescending(x => x.Id) : q.OrderBy(x => x.Id),
                "Enabled" => desc ? q.OrderByDescending(x => x.Enabled) : q.OrderBy(x => x.Enabled),
                "RuleType" => desc ? q.OrderByDescending(x => x.RuleType) : q.OrderBy(x => x.RuleType),
                "SortOrder" => desc ? q.OrderByDescending(x => x.SortOrder) : q.OrderBy(x => x.SortOrder),
                "CreatedAt" => desc ? q.OrderByDescending(x => x.CreatedAt) : q.OrderBy(x => x.CreatedAt),
                _ => desc ? q.OrderByDescending(x => x.Id) : q.OrderBy(x => x.Id)
            };

            var model = await PagedResult<PurchasePolicyRule>.CreateAsync(q, page, pageSize, so, desc, s, searchBy ?? "all");
            ViewBag.Search = s;
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.Sort = so;
            ViewBag.Dir = desc ? "desc" : "asc";
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.FilterCol_Id = filterCol_id;
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var q = _db.PurchasePolicyRules.AsNoTracking();
            var col = (column ?? "").Trim().ToLowerInvariant();
            if (col == "id")
            {
                var ids = await q.Select(x => x.Id).Distinct().OrderBy(v => v).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (col == "enabled")
                return Json(new[] { new { value = "true", display = "نعم" }, new { value = "false", display = "لا" } });
            return Json(Array.Empty<object>());
        }

        [HttpGet]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();
            var item = await _db.PurchasePolicyRules.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
            if (item == null) return NotFound();
            return View(item);
        }
    }
}
