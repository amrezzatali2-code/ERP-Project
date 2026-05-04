using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ERP.Data;
using ERP.Filters;
using ERP.Models;
using ERP.ViewModels;
using ERP.Infrastructure;

namespace ERP.Controllers
{
    [RequirePermission("SalesInvoiceRoutes.Index")]
    public class SalesInvoiceRoutesController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IUserActivityLogger _activityLogger;

        public SalesInvoiceRoutesController(AppDbContext db, IUserActivityLogger activityLogger)
        {
            _db = db;
            _activityLogger = activityLogger;
        }

        [HttpGet]
        public async Task<IActionResult> Index(
            DateTime? fromDate,
            DateTime? toDate,
            int? routeId,
            int? warehouseId,
            string? sort = "SIDate",
            string? dir = "desc",
            int page = 1,
            int pageSize = 25)
        {
            var routes = await _db.Routes
                .AsNoTracking()
                .Where(r => r.IsActive)
                .OrderBy(r => r.SortOrder).ThenBy(r => r.Name)
                .Select(r => new SelectListItem(r.Name ?? r.Id.ToString(), r.Id.ToString(), routeId == r.Id))
                .ToListAsync();
            var warehouses = await _db.Warehouses
                .AsNoTracking()
                .OrderBy(w => w.WarehouseName)
                .Select(w => new SelectListItem(w.WarehouseName ?? w.WarehouseId.ToString(), w.WarehouseId.ToString(), warehouseId == w.WarehouseId))
                .ToListAsync();

            ViewBag.Routes = routes;
            ViewBag.Warehouses = warehouses;
            ViewBag.FromDate = fromDate;
            ViewBag.ToDate = toDate;
            ViewBag.RouteId = routeId;
            ViewBag.WarehouseId = warehouseId;
            ViewBag.Sort = sort;
            ViewBag.Dir = dir;

            var q = _db.SalesInvoices
                .AsNoTracking()
                .Include(si => si.Customer).ThenInclude(c => c!.Route)
                .Include(si => si.Warehouse)
                .Include(si => si.Route)
                .AsQueryable();

            if (fromDate.HasValue)
                q = q.Where(si => si.SIDate >= fromDate.Value.Date);
            if (toDate.HasValue)
                q = q.Where(si => si.SIDate <= toDate.Value.Date);
            if (routeId.HasValue && routeId.Value > 0)
                q = q.Where(si => si.Customer != null && si.Customer.RouteId == routeId.Value);
            if (warehouseId.HasValue && warehouseId.Value > 0)
                q = q.Where(si => si.WarehouseId == warehouseId.Value);

            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            q = (sort ?? "SIDate").ToLowerInvariant() switch
            {
                "id" => desc ? q.OrderByDescending(si => si.SIId) : q.OrderBy(si => si.SIId),
                "customer" => desc ? q.OrderByDescending(si => si.Customer != null ? si.Customer.CustomerName : "") : q.OrderBy(si => si.Customer != null ? si.Customer.CustomerName : ""),
                "route" => desc ? q.OrderByDescending(si => si.Customer != null && si.Customer.Route != null ? si.Customer.Route.Name : "") : q.OrderBy(si => si.Customer != null && si.Customer.Route != null ? si.Customer.Route.Name : ""),
                "warehouse" => desc ? q.OrderByDescending(si => si.Warehouse != null ? si.Warehouse.WarehouseName : "") : q.OrderBy(si => si.Warehouse != null ? si.Warehouse.WarehouseName : ""),
                _ => desc ? q.OrderByDescending(si => si.SIDate).ThenByDescending(si => si.SIId) : q.OrderBy(si => si.SIDate).ThenBy(si => si.SIId),
            };

            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 25;
            int total = await q.CountAsync();
            var list = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            var rows = new List<RouteReportRowDto>();
            foreach (var si in list)
            {
                var routeData = si.Route;
                rows.Add(new RouteReportRowDto
                {
                    SIId = si.SIId,
                    SIDate = si.SIDate,
                    CustomerName = si.Customer?.CustomerName ?? "",
                    RouteId = si.Customer?.RouteId,
                    RouteName = si.Customer?.Route?.Name ?? "",
                    WarehouseName = si.Warehouse?.WarehouseName ?? "",
                    BagsCount = routeData?.BagsCount ?? 0,
                    PacketsCount = routeData?.PacketsCount ?? 0,
                    CartonsCount = routeData?.CartonsCount ?? 0,
                    FridgeItemsCount = routeData?.FridgeItemsCount ?? 0,
                    FridgeBoxesCount = routeData?.FridgeBoxesCount ?? 0,
                    Notes = routeData?.Notes
                });
            }

            var model = new PagedResult<RouteReportRowDto>(rows, page, pageSize, total);
            ViewBag.TotalCount = total;
            ViewBag.TotalPages = model.TotalPages;
            return View(model);
        }

        [HttpGet]
        [RequirePermission("SalesInvoiceRoutes.Edit")]
        public async Task<IActionResult> Edit(int id)
        {
            var invoice = await _db.SalesInvoices
                .AsNoTracking()
                .Include(si => si.Customer).ThenInclude(c => c!.Route)
                .Include(si => si.Warehouse)
                .FirstOrDefaultAsync(si => si.SIId == id);
            if (invoice == null) return NotFound();

            var routeData = await _db.SalesInvoiceRoutes.FindAsync(id);
            var vm = new SalesInvoiceRouteEditVm
            {
                SIId = invoice.SIId,
                SIDate = invoice.SIDate,
                CustomerName = invoice.Customer?.CustomerName ?? "",
                RouteName = invoice.Customer?.Route?.Name ?? "",
                WarehouseName = invoice.Warehouse?.WarehouseName ?? "",
                BagsCount = routeData?.BagsCount ?? 0,
                PacketsCount = routeData?.PacketsCount ?? 0,
                CartonsCount = routeData?.CartonsCount ?? 0,
                FridgeItemsCount = routeData?.FridgeItemsCount ?? 0,
                FridgeBoxesCount = routeData?.FridgeBoxesCount ?? 0,
                Notes = routeData?.Notes ?? ""
            };
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("SalesInvoiceRoutes.Edit")]
        public async Task<IActionResult> Edit(int id, [Bind("SIId,BagsCount,PacketsCount,CartonsCount,FridgeItemsCount,FridgeBoxesCount,Notes")] SalesInvoiceRouteEditVm vm)
        {
            if (id != vm.SIId) return NotFound();
            var invoice = await _db.SalesInvoices.AsNoTracking().AnyAsync(si => si.SIId == id);
            if (!invoice) return NotFound();

            var existing = await _db.SalesInvoiceRoutes.FindAsync(id);
            if (existing == null)
            {
                existing = new SalesInvoiceRoute { SIId = id };
                _db.SalesInvoiceRoutes.Add(existing);
            }
            existing.BagsCount = vm.BagsCount;
            existing.PacketsCount = vm.PacketsCount;
            existing.CartonsCount = vm.CartonsCount;
            existing.FridgeItemsCount = vm.FridgeItemsCount;
            existing.FridgeBoxesCount = vm.FridgeBoxesCount;
            existing.Notes = vm.Notes?.Trim();
            existing.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            TempData["Ok"] = "تم حفظ بيانات خط السير للفاتورة.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>شاشة إدخال بيانات خط السير — رقم فاتورة ثم تسلسل الحقول مع Enter.</summary>
        [HttpGet]
        [RequirePermission("SalesInvoiceRoutes.Index")]
        public async Task<IActionResult> Entry(int? siId)
        {
            ViewBag.InitialSIId = siId;
            return View();
        }

        /// <summary>جلب بيانات خط السير المحفوظة لفاتورة (لتعبيئة شاشة الإدخال عند التعديل).</summary>
        [HttpGet]
        [RequirePermission("SalesInvoiceRoutes.GetInvoiceInfo")]
        public async Task<IActionResult> GetRouteEntryData(int siId)
        {
            var route = await _db.SalesInvoiceRoutes.AsNoTracking().FirstOrDefaultAsync(r => r.SIId == siId);
            if (route == null)
                return Json(new { found = false });

            var fridgeLines = await _db.SalesInvoiceRouteFridgeLines
                .AsNoTracking()
                .Where(fl => fl.SIId == siId)
                .Include(fl => fl.Product)
                .OrderBy(fl => fl.Product != null ? fl.Product.ProdName : "")
                .Select(fl => new
                {
                    productId = fl.ProductId,
                    name = fl.Product != null ? (fl.Product.ProdName ?? ("صنف #" + fl.ProductId)) : ("صنف #" + fl.ProductId),
                    qty = fl.Qty
                })
                .ToListAsync();

            // fallback للبيانات القديمة/الناقصة: لو سطور الثلاجة غير محفوظة،
            // نستخدم أصناف الثلاجة من سطور الفاتورة نفسها.
            if (fridgeLines.Count == 0)
            {
                fridgeLines = await _db.SalesInvoiceLines
                    .AsNoTracking()
                    .Where(l => l.SIId == siId)
                    .Join(_db.Products, l => l.ProdId, p => p.ProdId, (l, p) => new { l, p })
                    .Join(_db.ProductClassifications, x => x.p.ClassificationId, c => c.Id, (x, c) => new { x.l, x.p, c })
                    .Where(x => x.c.Name != null && x.c.Name.Contains("ثلاجة"))
                    .GroupBy(x => new { x.p.ProdId, x.p.ProdName })
                    .Select(g => new
                    {
                        productId = g.Key.ProdId,
                        name = g.Key.ProdName ?? ("صنف #" + g.Key.ProdId),
                        qty = g.Sum(x => x.l.Qty)
                    })
                    .OrderBy(x => x.name)
                    .Take(20)
                    .ToListAsync();
            }

            return Json(new
            {
                found = true,
                controlEmployeeId = route.ControlEmployeeId,
                preparerEmployeeId = route.PreparerEmployeeId,
                distributorEmployeeId = route.DistributorEmployeeId,
                bagsCount = route.BagsCount,
                packetsCount = route.PacketsCount,
                cartonsCount = route.CartonsCount,
                notes = route.Notes ?? "",
                fridgeLines
            });
        }

        /// <summary>جلب بيانات الفاتورة (عميل، إجمالي، عدد الأصناف) حسب رقم الفاتورة.</summary>
        [HttpGet]
        [RequirePermission("SalesInvoiceRoutes.GetInvoiceInfo")]
        public async Task<IActionResult> GetInvoiceInfo(int siId)
        {
            var inv = await _db.SalesInvoices
                .AsNoTracking()
                .Include(si => si.Customer)
                .Where(si => si.SIId == siId)
                .Select(si => new { si.SIId, si.NetTotal, CustomerName = si.Customer != null ? si.Customer.CustomerName : "" })
                .FirstOrDefaultAsync();
            if (inv == null)
                return Json(new { found = false });
            int linesCount = await _db.SalesInvoiceLines.AsNoTracking().CountAsync(l => l.SIId == siId);
            bool alreadyInRoute = await _db.SalesInvoiceRoutes.AsNoTracking().AnyAsync(r => r.SIId == siId);
            return Json(new { found = true, customerName = inv.CustomerName, netTotal = inv.NetTotal, linesCount, alreadyInRoute });
        }

        /// <summary>موظفون حسب اسم الوظيفة (مثل كونترول، محضر).</summary>
        [HttpGet]
        [RequirePermission("SalesInvoiceRoutes.GetEmployeesByJob")]
        public async Task<IActionResult> GetEmployeesByJob(string jobName)
        {
            if (string.IsNullOrWhiteSpace(jobName))
                return Json(new List<object>());
            var list = await _db.Employees
                .AsNoTracking()
                .Include(e => e.Job)
                .Where(e => e.IsActive && e.Job != null && e.Job.Name != null && e.Job.Name.Trim().ToLower().Contains(jobName.Trim().ToLower()))
                .OrderBy(e => e.FullName)
                .Select(e => new { id = e.Id, fullName = e.FullName ?? "" })
                .ToListAsync();
            return Json(list);
        }

        /// <summary>أصناف الثلاجة الموجودة في فاتورة العميل (سطور الفاتورة التي صنفها ثلاجة).</summary>
        [HttpGet]
        [RequirePermission("SalesInvoiceRoutes.GetInvoiceInfo")]
        public async Task<IActionResult> GetFridgeProductsByInvoice(int siId)
        {
            var list = await _db.SalesInvoiceLines
                .AsNoTracking()
                .Where(l => l.SIId == siId)
                .Join(_db.Products, l => l.ProdId, p => p.ProdId, (l, p) => new { l, p })
                .Join(_db.ProductClassifications, x => x.p.ClassificationId, c => c.Id, (x, c) => new { x.l, x.p, c })
                .Where(x => x.c.Name != null && x.c.Name.Contains("ثلاجة"))
                .GroupBy(x => new { x.p.ProdId, x.p.ProdName })
                .Select(g => new { id = g.Key.ProdId, name = g.Key.ProdName ?? "", qty = g.Sum(x => x.l.Qty) })
                .OrderBy(x => x.name)
                .ToListAsync();
            return Json(list);
        }

        /// <summary>أصناف تصنيفها يحتوي على "ثلاجة" للبحث.</summary>
        [HttpGet]
        [RequirePermission("SalesInvoiceRoutes.GetFridgeProducts")]
        public async Task<IActionResult> GetFridgeProducts(string? search)
        {
            var term = (search ?? "").Trim();
            var q = _db.Products
                .AsNoTracking()
                .Include(p => p.Classification)
                .Where(p => p.Classification != null && p.Classification.Name != null && p.Classification.Name.Contains("ثلاجة"));
            if (!string.IsNullOrEmpty(term))
                q = q.Where(p => (p.ProdName != null && p.ProdName.Contains(term)) || (p.Barcode != null && p.Barcode.Contains(term)));
            var list = await q
                .OrderBy(p => p.ProdName)
                .Take(50)
                .Select(p => new { id = p.ProdId, name = p.ProdName ?? p.Barcode ?? "", code = p.Barcode ?? "" })
                .ToListAsync();
            return Json(list);
        }

        /// <summary>حفظ بيانات خط السير من شاشة الإدخال (مع الكونترول، المحضر، وأصناف الثلاجة).</summary>
        [HttpPost]
        [RequirePermission("SalesInvoiceRoutes.Edit")]
        public async Task<IActionResult> SaveRouteEntry([FromBody] SalesInvoiceRouteEntryDto? dto)
        {
            if (dto == null || dto.SIId <= 0)
                return BadRequest(new { success = false, message = "بيانات غير صالحة." });
            try
            {
                try { _db.Database.SetCommandTimeout(30); } catch { }

                if (!await _db.SalesInvoices.AsNoTracking().AnyAsync(si => si.SIId == dto.SIId))
                    return Json(new { success = false, message = "الفاتورة غير موجودة." });

                var existing = await _db.SalesInvoiceRoutes.FirstOrDefaultAsync(r => r.SIId == dto.SIId);
                if (existing == null)
                {
                    existing = new SalesInvoiceRoute { SIId = dto.SIId };
                    _db.SalesInvoiceRoutes.Add(existing);
                }

                existing.ControlEmployeeId = dto.ControlEmployeeId > 0 ? dto.ControlEmployeeId : null;
                existing.PreparerEmployeeId = dto.PreparerEmployeeId > 0 ? dto.PreparerEmployeeId : null;
                existing.DistributorEmployeeId = dto.DistributorEmployeeId > 0 ? dto.DistributorEmployeeId : null;
                existing.BagsCount = dto.BagsCount;
                existing.PacketsCount = dto.PacketsCount;
                existing.CartonsCount = dto.CartonsCount;
                existing.Notes = dto.Notes?.Trim();
                existing.UpdatedAt = DateTime.UtcNow;

                var normalizedFridgeLines = (dto.FridgeLines ?? new List<SalesInvoiceRouteFridgeLineDto>())
                    .Where(l => l != null && l.ProductId > 0 && l.Qty > 0)
                    .GroupBy(l => l.ProductId)
                    .Select(g => new { ProductId = g.Key, Qty = g.Sum(x => x.Qty) })
                    .ToList();

                int fridgeItemsCount = normalizedFridgeLines.Count;
                int fridgeBoxesCount = normalizedFridgeLines.Sum(x => x.Qty);
                existing.FridgeItemsCount = fridgeItemsCount;
                existing.FridgeBoxesCount = fridgeBoxesCount;

                await _db.SaveChangesAsync();

                // حفظ سطور أصناف الثلاجة (الاسم يظهر في التقارير من هذا الجدول)
                var oldLines = await _db.SalesInvoiceRouteFridgeLines
                    .Where(x => x.SIId == dto.SIId)
                    .ToListAsync();
                if (oldLines.Count > 0)
                    _db.SalesInvoiceRouteFridgeLines.RemoveRange(oldLines);

                if (normalizedFridgeLines.Count > 0)
                {
                    foreach (var line in normalizedFridgeLines)
                    {
                        _db.SalesInvoiceRouteFridgeLines.Add(new SalesInvoiceRouteFridgeLine
                        {
                            SIId = dto.SIId,
                            ProductId = line.ProductId,
                            Qty = line.Qty
                        });
                    }
                }

                await _db.SaveChangesAsync();

                _ = Task.Run(async () =>
                {
                    try { await _activityLogger.LogAsync(UserActionType.Edit, "SalesInvoiceRoute", existing.SIId, $"تسجيل خط السير لفاتورة {dto.SIId}"); } catch { }
                });
                return Json(new { success = true, routeId = existing.SIId });
            }
            catch (Exception ex)
            {
                var msg = ex.InnerException?.Message ?? ex.Message;
                return Json(new { success = false, message = "خطأ في الحفظ: " + msg });
            }
        }

        [HttpPost]
        [RequirePermission("SalesInvoiceRoutes.SaveRouteJson")]
        public async Task<IActionResult> SaveRouteJson([FromBody] SalesInvoiceRouteJsonDto? dto)
        {
            if (dto == null || dto.SIId <= 0)
                return BadRequest(new { success = false, message = "بيانات غير صالحة." });
            var invoice = await _db.SalesInvoices.AsNoTracking().AnyAsync(si => si.SIId == dto.SIId);
            if (!invoice)
                return NotFound(new { success = false, message = "الفاتورة غير موجودة." });

            var existing = await _db.SalesInvoiceRoutes.FindAsync(dto.SIId);
            if (existing == null)
            {
                existing = new SalesInvoiceRoute { SIId = dto.SIId };
                _db.SalesInvoiceRoutes.Add(existing);
            }
            existing.BagsCount = dto.BagsCount;
            existing.PacketsCount = dto.PacketsCount;
            existing.CartonsCount = dto.CartonsCount;
            existing.FridgeItemsCount = dto.FridgeItemsCount;
            existing.FridgeBoxesCount = dto.FridgeBoxesCount;
            existing.Notes = dto.Notes?.Trim();
            existing.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Json(new { success = true });
        }
    }

    public class SalesInvoiceRouteEditVm
    {
        public int SIId { get; set; }
        public DateTime SIDate { get; set; }
        public string CustomerName { get; set; } = "";
        public string RouteName { get; set; } = "";
        public string WarehouseName { get; set; } = "";
        public int BagsCount { get; set; }
        public int PacketsCount { get; set; }
        public int CartonsCount { get; set; }
        public int FridgeItemsCount { get; set; }
        public int FridgeBoxesCount { get; set; }
        [System.ComponentModel.DataAnnotations.StringLength(500)]
        public string? Notes { get; set; }
    }

    public class SalesInvoiceRouteJsonDto
    {
        public int SIId { get; set; }
        public int BagsCount { get; set; }
        public int PacketsCount { get; set; }
        public int CartonsCount { get; set; }
        public int FridgeItemsCount { get; set; }
        public int FridgeBoxesCount { get; set; }
        public string? Notes { get; set; }
    }

    public class SalesInvoiceRouteEntryDto
    {
        public int SIId { get; set; }
        public int? ControlEmployeeId { get; set; }
        public int? PreparerEmployeeId { get; set; }
        public int? DistributorEmployeeId { get; set; }
        public int BagsCount { get; set; }
        public int PacketsCount { get; set; }
        public int CartonsCount { get; set; }
        public string? Notes { get; set; }
        public List<SalesInvoiceRouteFridgeLineDto>? FridgeLines { get; set; }
    }

    public class SalesInvoiceRouteFridgeLineDto
    {
        public int ProductId { get; set; }
        public int Qty { get; set; }
    }
}
