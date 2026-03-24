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
    public class PurchasingOrderLinesController : Controller
    {
        private readonly AppDbContext _db;
        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

        public PurchasingOrderLinesController(AppDbContext db) => _db = db;

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
            string? filterCol_id = null)
        {
            IQueryable<PurchasingOrderLine> q = _db.PurchasingOrderLines.AsNoTracking()
                .Include(x => x.PurchasingOrder).Include(x => x.Product);
            var s = (search ?? "").Trim();
            var so = (sort ?? "Id").Trim();
            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(s))
            {
                if (searchBy == "id" && int.TryParse(s, out int idVal))
                    q = q.Where(x => x.Id == idVal);
                else
                    q = q.Where(x => x.Id.ToString().Contains(s) || (x.ProductName != null && x.ProductName.Contains(s)) || (x.VendorProductCode != null && x.VendorProductCode.Contains(s)));
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
                "PurchasingOrderId" => desc ? q.OrderByDescending(x => x.PurchasingOrderId) : q.OrderBy(x => x.PurchasingOrderId),
                "LineNo" => desc ? q.OrderByDescending(x => x.LineNo) : q.OrderBy(x => x.LineNo),
                "ProductName" => desc ? q.OrderByDescending(x => x.ProductName) : q.OrderBy(x => x.ProductName),
                "Qty" => desc ? q.OrderByDescending(x => x.Qty) : q.OrderBy(x => x.Qty),
                "CreatedAt" => desc ? q.OrderByDescending(x => x.CreatedAt) : q.OrderBy(x => x.CreatedAt),
                _ => desc ? q.OrderByDescending(x => x.Id) : q.OrderBy(x => x.Id)
            };

            var model = await PagedResult<PurchasingOrderLine>.CreateAsync(q, page, pageSize, so, desc, s, searchBy ?? "all");
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
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();
            var item = await _db.PurchasingOrderLines.AsNoTracking()
                .Include(x => x.PurchasingOrder).Include(x => x.Product)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (item == null) return NotFound();
            return View(item);
        }
    }
}
