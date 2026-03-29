using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ERP.Data;
using ERP.Infrastructure;
using ERP.Models;
using OfficeOpenXml;

namespace ERP.Controllers
{
    /// <summary>
    /// قائمة مطابقة أصناف العميل (VendorProductMappings) — عرض بيانات الجدول بنمط قوائم الـ ERP.
    /// </summary>
    public class VendorProductMappingsController : Controller
    {
        private readonly AppDbContext _db;
        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

        public VendorProductMappingsController(AppDbContext db)
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
            string? filterCol_vendorName = null,
            string? filterCol_productName = null,
            string? filterCol_created = null)
        {
            IQueryable<VendorProductMapping> q = _db.VendorProductMappings
                .AsNoTracking()
                .Include(x => x.Customer)
                .Include(x => x.Product);

            var s = (search ?? "").Trim();
            var sb = (searchBy ?? "all").Trim().ToLower();
            var so = (sort ?? "Id").Trim();
            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(s))
            {
                switch (sb)
                {
                    case "id":
                        if (int.TryParse(s, out int idVal))
                            q = q.Where(x => x.Id == idVal);
                        else
                            q = q.Where(x => 1 == 0);
                        break;
                    case "vendor":
                        q = q.Where(x => x.Customer != null && x.Customer.CustomerName != null && x.Customer.CustomerName.Contains(s));
                        break;
                    case "product":
                        q = q.Where(x => (x.VendorProductName != null && x.VendorProductName.Contains(s)) || (x.Product != null && x.Product.ProdName != null && x.Product.ProdName.Contains(s)));
                        break;
                    case "all":
                    default:
                        q = q.Where(x =>
                            (x.VendorProductName != null && x.VendorProductName.Contains(s)) ||
                            (x.VendorProductCode != null && x.VendorProductCode.Contains(s)) ||
                            (x.Customer != null && x.Customer.CustomerName != null && x.Customer.CustomerName.Contains(s)) ||
                            (x.Product != null && x.Product.ProdName != null && x.Product.ProdName.Contains(s)) ||
                            x.Id.ToString().Contains(s));
                        break;
                }
            }

            if (fromCode.HasValue) q = q.Where(x => x.Id >= fromCode.Value);
            if (toCode.HasValue) q = q.Where(x => x.Id <= toCode.Value);

            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0) q = q.Where(x => ids.Contains(x.Id));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_vendorName))
            {
                var vals = filterCol_vendorName.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    q = q.Where(x => x.Customer != null && x.Customer.CustomerName != null && vals.Contains(x.Customer.CustomerName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_productName))
            {
                var vals = filterCol_productName.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    q = q.Where(x => x.VendorProductName != null && vals.Contains(x.VendorProductName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_created))
            {
                var parts = filterCol_created.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length >= 7).ToList();
                var dateFilters = new List<(int Y, int M)>();
                foreach (var p in parts)
                {
                    if (p.Length == 7 && p[4] == '-' && int.TryParse(p.AsSpan(0, 4), out var y) && int.TryParse(p.AsSpan(5, 2), out var m) && m >= 1 && m <= 12)
                        dateFilters.Add((y, m));
                }
                if (dateFilters.Count > 0)
                    q = q.Where(x => dateFilters.Any(df => x.CreatedAt.Year == df.Y && x.CreatedAt.Month == df.M));
            }

            q = so switch
            {
                "Id" => desc ? q.OrderByDescending(x => x.Id) : q.OrderBy(x => x.Id),
                "CustomerName" => desc ? q.OrderByDescending(x => x.Customer != null ? x.Customer.CustomerName : null) : q.OrderBy(x => x.Customer != null ? x.Customer.CustomerName : null),
                "VendorProductName" => desc ? q.OrderByDescending(x => x.VendorProductName) : q.OrderBy(x => x.VendorProductName),
                "VendorProductCode" => desc ? q.OrderByDescending(x => x.VendorProductCode) : q.OrderBy(x => x.VendorProductCode),
                "PriceRetail" => desc ? q.OrderByDescending(x => x.PriceRetail) : q.OrderBy(x => x.PriceRetail),
                "CreatedAt" => desc ? q.OrderByDescending(x => x.CreatedAt) : q.OrderBy(x => x.CreatedAt),
                _ => desc ? q.OrderByDescending(x => x.Id) : q.OrderBy(x => x.Id)
            };

            var model = await PagedResult<VendorProductMapping>.CreateAsync(q, page, pageSize, so, desc, s, sb);

            model.UseDateRange = false;
            ViewBag.Search = s;
            ViewBag.SearchBy = sb;
            ViewBag.Sort = so;
            ViewBag.Dir = desc ? "desc" : "asc";
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.FilterCol_Id = filterCol_id;
            ViewBag.FilterCol_VendorName = filterCol_vendorName;
            ViewBag.FilterCol_ProductName = filterCol_productName;
            ViewBag.FilterCol_Created = filterCol_created;
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var q = _db.VendorProductMappings.AsNoTracking().Include(x => x.Customer).Include(x => x.Product);
            var col = (column ?? "").Trim().ToLowerInvariant();

            if (col == "id")
            {
                var ids = await q.Select(x => x.Id).Distinct().OrderBy(v => v).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (col == "vendorname")
            {
                var names = await q.Where(x => x.Customer != null).Select(x => x.Customer!.CustomerName ?? "").Distinct().Where(n => n != "").OrderBy(v => v).Take(500).ToListAsync();
                return Json(names.Select(v => new { value = v, display = v }));
            }
            if (col == "productname")
            {
                var names = await q.Where(x => x.VendorProductName != null).Select(x => x.VendorProductName!).Distinct().OrderBy(v => v).Take(500).ToListAsync();
                return Json(names.Select(v => new { value = v, display = v }));
            }
            if (col == "created")
            {
                var dates = await q.Select(x => new { x.CreatedAt.Year, x.CreatedAt.Month }).Distinct()
                    .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).Take(100).ToListAsync();
                return Json(dates.Select(x => new { value = $"{x.Year}-{x.Month:D2}", display = $"{x.Year}/{x.Month:D2}" }));
            }
            return Json(Array.Empty<object>());
        }

        [HttpGet]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();
            var item = await _db.VendorProductMappings
                .AsNoTracking()
                .Include(x => x.Customer)
                .Include(x => x.Product)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (item == null) return NotFound();
            return View(item);
        }

        /// <summary>
        /// شاشة استيراد مطابقة أصناف العميل من Excel.
        /// الأعمدة المتوقعة: اسم العميل، اسم الصنف، الكود، سعر الجمهور.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Import()
        {
            ViewBag.Success = TempData["VpmImportSuccess"];
            ViewBag.Error = TempData["VpmImportError"];
            var customers = await _db.Customers
                .AsNoTracking()
                .Where(c => c.IsActive == true)
                .OrderBy(c => c.CustomerName)
                .Select(c => new { c.CustomerId, c.CustomerName })
                .ToListAsync();
            ViewBag.Customers = new SelectList(
                customers.Select(c => new SelectListItem { Value = c.CustomerId.ToString(), Text = c.CustomerName ?? "" }),
                "Value", "Text");
            return View();
        }

        private const int ImportBatchSize = 3000;
        private const int ImportMaxErrorsReported = 100;
        private const int ImportCommandTimeoutSeconds = 600;

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Import(IFormFile excelFile, int? customerIdOverride = null)
        {
            if (excelFile == null || excelFile.Length == 0)
            {
                TempData["VpmImportError"] = "من فضلك اختر ملف Excel أولاً.";
                return RedirectToAction(nameof(Import));
            }

            try
            {
                _db.Database.SetCommandTimeout(TimeSpan.FromSeconds(ImportCommandTimeoutSeconds));
                ExcelPackage.License.SetNonCommercialPersonal("Amr ERP Dev");
                using var stream = new MemoryStream();
                await excelFile.CopyToAsync(stream);
                stream.Position = 0;

                using var package = new ExcelPackage(stream);
                var sheet = package.Workbook.Worksheets[0];
                if (sheet.Dimension == null)
                {
                    TempData["VpmImportError"] = "الملف لا يحتوي على بيانات.";
                    return RedirectToAction(nameof(Import));
                }

                int lastRow = sheet.Dimension.End.Row;
                int lastCol = sheet.Dimension.End.Column;
                var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int col = 1; col <= lastCol; col++)
                {
                    var text = sheet.Cells[1, col].Text?.Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                        headers[text] = col;
                }

                int GetCol(params string[] names)
                {
                    foreach (var n in names)
                        if (headers.TryGetValue(n, out int c)) return c;
                    return -1;
                }

                int colCustomer = GetCol("اسم العميل", "CustomerName");
                int colProductName = GetCol("اسم الصنف", "ProductName", "الصنف");
                int colCode = GetCol("الكود", "Code", "كود العميل", "VendorProductCode");
                int colPrice = GetCol("سعر الجمهور", "PriceRetail", "السعر");

                if (colProductName <= 0)
                {
                    TempData["VpmImportError"] = "الملف يجب أن يحتوي على عمود \"اسم الصنف\".";
                    return RedirectToAction(nameof(Import));
                }

                int? fixedCustomerId = customerIdOverride > 0 ? customerIdOverride : null;
                if (!fixedCustomerId.HasValue && colCustomer <= 0)
                {
                    TempData["VpmImportError"] = "يجب اختيار عميل من القائمة أو وجود عمود \"اسم العميل\" في الملف.";
                    return RedirectToAction(nameof(Import));
                }
                var customerByName = await _db.Customers
                    .AsNoTracking()
                    .Where(c => c.IsActive == true)
                    .ToDictionaryAsync(c => c.CustomerName?.Trim() ?? "", c => c.CustomerId, StringComparer.OrdinalIgnoreCase);

                var productsByProdId = await _db.Products.AsNoTracking().ToDictionaryAsync(p => p.ProdId);
                var productsByExtCode = await _db.Products
                    .AsNoTracking()
                    .Where(p => p.ExternalCode != null && p.ExternalCode != "")
                    .ToDictionaryAsync(p => p.ExternalCode!.Trim(), p => p.ProdId, StringComparer.OrdinalIgnoreCase);

                string GetCell(int row, int col)
                {
                    if (col <= 0) return "";
                    return sheet.Cells[row, col].Text?.Trim() ?? "";
                }

                // استبدال كامل: مسح المطابقات السابقة للعملاء المعنيين ثم إدراج من الملف فقط (آخر صف يفوز عند تكرار نفس العميل+الصنف).
                if (fixedCustomerId.HasValue)
                {
                    var oldFixed = await _db.VendorProductMappings.Where(x => x.CustomerId == fixedCustomerId.Value).ToListAsync();
                    if (oldFixed.Count > 0)
                    {
                        _db.VendorProductMappings.RemoveRange(oldFixed);
                        await _db.SaveChangesAsync();
                    }
                }
                else
                {
                    var customerIdsToClear = new HashSet<int>();
                    for (int row = 2; row <= lastRow; row++)
                    {
                        var pn = GetCell(row, colProductName);
                        if (string.IsNullOrWhiteSpace(pn)) continue;
                        var cn = GetCell(row, colCustomer).Trim();
                        if (string.IsNullOrWhiteSpace(cn)) continue;
                        if (customerByName.TryGetValue(cn, out var cid))
                            customerIdsToClear.Add(cid);
                    }
                    if (customerIdsToClear.Count > 0)
                    {
                        var oldMulti = await _db.VendorProductMappings.Where(x => customerIdsToClear.Contains(x.CustomerId)).ToListAsync();
                        if (oldMulti.Count > 0)
                        {
                            _db.VendorProductMappings.RemoveRange(oldMulti);
                            await _db.SaveChangesAsync();
                        }
                    }
                }

                int inserted = 0, skipped = 0;
                var errors = new List<string>();
                var fileRows = new Dictionary<(int CustomerId, int ProductId), (int Row, int CustomerId, int ProductId, string ProductName, string CodeStr, decimal? Price)>();

                for (int row = 2; row <= lastRow; row++)
                {
                    string productName = GetCell(row, colProductName);
                    if (string.IsNullOrWhiteSpace(productName)) { skipped++; continue; }

                    int customerId;
                    if (fixedCustomerId.HasValue)
                        customerId = fixedCustomerId.Value;
                    else
                    {
                        string custName = GetCell(row, colCustomer).Trim();
                        if (string.IsNullOrWhiteSpace(custName))
                        {
                            if (errors.Count < ImportMaxErrorsReported) errors.Add($"صف {row}: اسم العميل فارغ");
                            skipped++;
                            continue;
                        }
                        if (!customerByName.TryGetValue(custName, out customerId))
                        {
                            if (errors.Count < ImportMaxErrorsReported) errors.Add("صف " + row + ": عميل غير موجود (\"" + custName + "\")");
                            skipped++;
                            continue;
                        }
                    }

                    string codeStr = colCode > 0 ? GetCell(row, colCode) : "";
                    decimal? price = null;
                    if (colPrice > 0)
                    {
                        var priceText = GetCell(row, colPrice);
                        if (decimal.TryParse(priceText, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal p) && p >= 0)
                            price = p;
                    }

                    int productId = 0;
                    if (!string.IsNullOrWhiteSpace(codeStr))
                    {
                        if (int.TryParse(codeStr, out int codeInt) && productsByProdId.TryGetValue(codeInt, out var prod))
                            productId = prod.ProdId;
                        else if (productsByExtCode.TryGetValue(codeStr, out int pid))
                            productId = pid;
                    }
                    if (productId == 0)
                    {
                        if (errors.Count < ImportMaxErrorsReported) errors.Add("صف " + row + ": الكود \"" + codeStr + "\" لا يطابق صنفاً — تخطي.");
                        skipped++;
                        continue;
                    }

                    fileRows[(customerId, productId)] = (row, customerId, productId, productName, codeStr, price);
                }

                var rowsToInsert = fileRows.Values.ToList();
                for (int i = 0; i < rowsToInsert.Count; i += ImportBatchSize)
                {
                    foreach (var (_, customerId, productId, productName, codeStr, price) in rowsToInsert.Skip(i).Take(ImportBatchSize))
                    {
                        _db.VendorProductMappings.Add(new VendorProductMapping
                        {
                            CustomerId = customerId,
                            VendorProductName = productName,
                            VendorProductCode = codeStr,
                            ProductId = productId,
                            PriceRetail = price,
                            CreatedAt = DateTime.UtcNow
                        });
                        inserted++;
                    }
                    await _db.SaveChangesAsync();
                    _db.ChangeTracker.Clear();
                }

                var msg = inserted > 0
                    ? $"تم استيراد {inserted} سطر مطابقة (استبدال كامل للمطابقات السابقة للعملاء المعنيين)."
                    : "لم يتم إضافة أي سطر صالح.";
                if (skipped > 0) msg += $" تم تخطي {skipped} صفاً.";
                if (errors.Count > 0 && errors.Count <= 5)
                    msg += " " + string.Join(" ", errors);
                else if (errors.Count > 5)
                    msg += " (أول 5 أخطاء: " + string.Join("؛ ", errors.Take(5)) + "…)";
                TempData["VpmImportSuccess"] = msg;
            }
            catch (Exception ex)
            {
                TempData["VpmImportError"] = "خطأ أثناء الاستيراد: " + ex.Message;
            }

            return RedirectToAction(nameof(Import));
        }
    }
}
