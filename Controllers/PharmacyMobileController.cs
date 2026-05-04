using ERP.Data;
using ERP.Infrastructure;
using ERP.Models;
using ERP.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace ERP.Controllers
{
    [AllowAnonymous]
    public class PharmacyMobileController : Controller
    {
        private const string MobileCookieName = "erp_pharmacy_auth";
        private static readonly TimeSpan MobileSessionTtl = TimeSpan.FromHours(12);
        private static readonly ConcurrentDictionary<string, MobileSessionTicket> _mobileSessions = new(StringComparer.Ordinal);

        private readonly AppDbContext _db;
        private readonly StockAnalysisService _stockAnalysisService;
        private readonly DocumentTotalsService _docTotals;
        private readonly MobileAppProgramCodeService _mobileProgramCodeService;
        private readonly ILedgerPostingService _ledgerPostingService;

        public PharmacyMobileController(
            AppDbContext db,
            StockAnalysisService stockAnalysisService,
            DocumentTotalsService docTotals,
            MobileAppProgramCodeService mobileProgramCodeService,
            ILedgerPostingService ledgerPostingService)
        {
            _db = db;
            _stockAnalysisService = stockAnalysisService;
            _docTotals = docTotals;
            _mobileProgramCodeService = mobileProgramCodeService;
            _ledgerPostingService = ledgerPostingService;
        }

        [HttpGet]
        public IActionResult Index() => View();

        [HttpGet]
        public IActionResult Invoice() => View();

        [HttpGet]
        public IActionResult Collector() => View();

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> AppLogin([FromBody] AppLoginRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new { ok = false, message = "اسم المستخدم وكلمة السر مطلوبان." });

            var userName = request.UserName.Trim();
            var user = await _db.Users
                .FirstOrDefaultAsync(u => u.IsActive && u.UserName == userName);

            if (user == null)
                return Unauthorized(new { ok = false, message = "اسم المستخدم أو كلمة السر غير صحيحة." });

            var passwordOk = PasswordHasher.VerifyPassword(request.Password, user.PasswordHash);
            if (!passwordOk && string.Equals(user.PasswordHash, request.Password, StringComparison.Ordinal))
            {
                // ترحيل فوري للحسابات القديمة التي كانت محفوظة بدون تشفير.
                user.PasswordHash = PasswordHasher.HashPassword(request.Password);
                await _db.SaveChangesAsync();
                passwordOk = true;
            }

            if (!passwordOk)
                return Unauthorized(new { ok = false, message = "اسم المستخدم أو كلمة السر غير صحيحة." });

            var token = CreateSessionToken();
            _mobileSessions[token] = new MobileSessionTicket
            {
                UserId = user.UserId,
                ExpiresAtUtc = DateTime.UtcNow.Add(MobileSessionTtl)
            };

            Response.Cookies.Append(
                MobileCookieName,
                token,
                new CookieOptions
                {
                    HttpOnly = true,
                    IsEssential = true,
                    SameSite = SameSiteMode.Lax,
                    Expires = DateTimeOffset.UtcNow.Add(MobileSessionTtl),
                    Secure = false
                });

            return Json(new
            {
                ok = true,
                user = new
                {
                    user.UserId,
                    user.UserName,
                    DisplayName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName : user.DisplayName,
                    PortalRole = NormalizePortalRole(user.PortalRole)
                }
            });
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public IActionResult AppLogout()
        {
            if (Request.Cookies.TryGetValue(MobileCookieName, out var token) && !string.IsNullOrWhiteSpace(token))
                _mobileSessions.TryRemove(token, out _);

            Response.Cookies.Delete(MobileCookieName);
            return Json(new { ok = true });
        }

        [HttpGet]
        public async Task<IActionResult> AppSession()
        {
            var user = await GetAuthenticatedMobileUserAsync();
            if (user == null)
                return Json(new { ok = true, authenticated = false });

            return Json(new
            {
                ok = true,
                authenticated = true,
                user = new
                {
                    user.UserId,
                    user.UserName,
                    DisplayName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName : user.DisplayName,
                    PortalRole = NormalizePortalRole(user.PortalRole)
                }
            });
        }

        [HttpGet]
        public async Task<IActionResult> BootstrapData()
        {
            var user = await GetAuthenticatedMobileUserAsync();
            if (user == null)
                return MobileUnauthorized();
            var appSettings = await _mobileProgramCodeService.GetSettingsAsync();

            var warehouseIds = await _db.StockBatches
                .AsNoTracking()
                .Where(sb => sb.QtyOnHand > 0)
                .Select(sb => sb.WarehouseId)
                .Distinct()
                .ToListAsync();

            var warehouses = await _db.Warehouses
                .AsNoTracking()
                .Where(w => warehouseIds.Contains(w.WarehouseId))
                .OrderBy(w => w.WarehouseName)
                .Select(w => new
                {
                    w.WarehouseId,
                    w.WarehouseName
                })
                .ToListAsync();

            var suppliers = await _db.Customers
                .AsNoTracking()
                .Where(c =>
                    c.IsActive &&
                    (c.PartyCategory == "Supplier" || c.PartyCategory == "مورد") &&
                    c.ExternalCode != null &&
                    c.ExternalCode != "")
                .OrderBy(c => c.CustomerName)
                .Select(c => new
                {
                    SupplierId = c.CustomerId,
                    SupplierName = c.CustomerName,
                    SupplierCode = c.ExternalCode
                })
                .ToListAsync();

            return Json(new
            {
                ok = true,
                user = new
                {
                    user.UserId,
                    user.UserName,
                    DisplayName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName : user.DisplayName,
                    PortalRole = NormalizePortalRole(user.PortalRole)
                },
                programCode = appSettings.ProgramCode,
                companyName = appSettings.CompanyName,
                warehouses,
                suppliers
            });
        }

        [HttpGet]
        public async Task<IActionResult> CollectorBootstrapData()
        {
            var user = await GetAuthenticatedMobileUserAsync();
            if (user == null)
                return MobileUnauthorized();

            if (!IsCollectorUser(user))
                return Unauthorized(new { ok = false, message = "هذا الحساب ليس موزعًا." });

            var assignedCustomerCount = await _db.CustomerCollectors
                .AsNoTracking()
                .CountAsync(x => x.UserId == user.UserId);

            return Json(new
            {
                ok = true,
                user = new
                {
                    user.UserId,
                    user.UserName,
                    DisplayName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName : user.DisplayName,
                    PortalRole = NormalizePortalRole(user.PortalRole)
                },
                assignedCustomerCount
            });
        }

        [HttpGet]
        public async Task<IActionResult> CollectorCustomers(string? search = null)
        {
            var user = await GetAuthenticatedMobileUserAsync();
            if (user == null)
                return MobileUnauthorized();

            if (!IsCollectorUser(user))
                return Unauthorized(new { ok = false, message = "هذا الحساب ليس موزعًا." });

            var assignedIds = await _db.CustomerCollectors
                .AsNoTracking()
                .Where(x => x.UserId == user.UserId)
                .Select(x => x.CustomerId)
                .Distinct()
                .ToListAsync();

            if (assignedIds.Count == 0)
                return Json(new { ok = true, items = Array.Empty<object>() });

            var query = _db.Customers
                .AsNoTracking()
                .Include(x => x.Area)
                .Include(x => x.Route)
                .Where(x => x.IsActive && assignedIds.Contains(x.CustomerId));

            var term = (search ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(term))
            {
                query = query.Where(x =>
                    (x.CustomerName != null && EF.Functions.Like(x.CustomerName, $"%{term}%")) ||
                    (x.ExternalCode != null && EF.Functions.Like(x.ExternalCode, $"%{term}%")) ||
                    x.CustomerId.ToString().Contains(term));
            }

            var customers = await query
                .OrderBy(x => x.CustomerName)
                .Select(x => new
                {
                    x.CustomerId,
                    x.CustomerName,
                    x.ExternalCode,
                    AreaName = x.Area != null ? x.Area.AreaName : null,
                    RouteName = x.Route != null ? x.Route.Name : null,
                    x.CreditLimit,
                    x.CreditLimitTemporaryIncrease,
                    x.CreditLimitTemporaryUntil
                })
                .ToListAsync();

            var customerIds = customers.Select(x => x.CustomerId).ToList();
            var balances = await _db.LedgerEntries
                .AsNoTracking()
                .Where(x => x.CustomerId.HasValue && customerIds.Contains(x.CustomerId.Value))
                .GroupBy(x => x.CustomerId!.Value)
                .Select(g => new { CustomerId = g.Key, Balance = g.Sum(x => x.Debit - x.Credit) })
                .ToDictionaryAsync(x => x.CustomerId, x => x.Balance);

            var items = customers.Select(x =>
            {
                var currentBalance = balances.TryGetValue(x.CustomerId, out var balance) ? balance : 0m;
                var effectiveLimit = CustomerCreditLimitCalculator.GetEffectiveCreditLimit(
                    x.CreditLimit,
                    x.CreditLimitTemporaryIncrease,
                    x.CreditLimitTemporaryUntil,
                    DateTime.Now);

                return new
                {
                    x.CustomerId,
                    x.CustomerName,
                    CustomerCode = x.ExternalCode,
                    x.AreaName,
                    x.RouteName,
                    CurrentBalance = currentBalance,
                    x.CreditLimit,
                    EffectiveCreditLimit = effectiveLimit,
                    RemainingCredit = effectiveLimit > 0m ? Math.Max(0m, effectiveLimit - currentBalance) : 0m
                };
            }).ToList();

            return Json(new { ok = true, items });
        }

        [HttpGet]
        public async Task<IActionResult> CollectorCustomerVolume(int customerId)
        {
            var user = await GetAuthenticatedMobileUserAsync();
            if (user == null)
                return MobileUnauthorized();

            if (!IsCollectorUser(user))
                return Unauthorized(new { ok = false, message = "هذا الحساب ليس موزعًا." });

            if (!await CanCollectorAccessCustomerAsync(user.UserId, customerId))
                return Unauthorized(new { ok = false, message = "هذا العميل غير مربوط بهذا الموزع." });

            var customer = await _db.Customers
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.CustomerId == customerId && x.IsActive);

            if (customer == null)
                return NotFound(new { ok = false, message = "العميل غير موجود." });

            var totalSales = await _db.SalesInvoices
                .AsNoTracking()
                .Where(x => x.CustomerId == customerId)
                .SumAsync(x => (decimal?)x.NetTotal) ?? 0m;

            var totalSalesReturns = await _db.SalesReturns
                .AsNoTracking()
                .Where(x => x.CustomerId == customerId)
                .SumAsync(x => (decimal?)x.NetTotal) ?? 0m;

            var totalReceipts = await _db.CashReceipts
                .AsNoTracking()
                .Where(x => x.CustomerId == customerId && x.IsPosted)
                .SumAsync(x => (decimal?)x.Amount) ?? 0m;

            var totalPayments = await _db.CashPayments
                .AsNoTracking()
                .Where(x => x.CustomerId == customerId && x.IsPosted)
                .SumAsync(x => (decimal?)x.Amount) ?? 0m;

            var currentBalance = await GetCustomerCurrentBalanceAsync(customerId);

            return Json(new
            {
                ok = true,
                summary = new
                {
                    customer.CustomerId,
                    customer.CustomerName,
                    TotalSales = totalSales,
                    TotalSalesReturns = totalSalesReturns,
                    TotalReceipts = totalReceipts,
                    TotalPayments = totalPayments,
                    CurrentBalance = currentBalance
                }
            });
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> CollectorCreateReceipt([FromBody] CollectorCreateReceiptRequest request)
        {
            var user = await GetAuthenticatedMobileUserAsync();
            if (user == null)
                return MobileUnauthorized();

            if (!IsCollectorUser(user))
                return Unauthorized(new { ok = false, message = "هذا الحساب ليس موزعًا." });

            if (request == null || request.CustomerId <= 0 || request.Amount <= 0m)
                return BadRequest(new { ok = false, message = "بيانات الإيصال غير مكتملة." });

            if (!await CanCollectorAccessCustomerAsync(user.UserId, request.CustomerId))
                return Unauthorized(new { ok = false, message = "هذا العميل غير مربوط بهذا الموزع." });

            var customer = await _db.Customers
                .AsNoTracking()
                .Include(x => x.Area)
                .FirstOrDefaultAsync(x => x.CustomerId == request.CustomerId && x.IsActive);

            if (customer == null)
                return NotFound(new { ok = false, message = "العميل غير موجود أو غير نشط." });

            if (!customer.AccountId.HasValue || customer.AccountId.Value <= 0)
                return BadRequest(new { ok = false, message = "العميل غير مربوط بحساب طرف محاسبي." });

            var cashAccountId = await TreasuryCashAccounts.QueryTreasuryCashBoxes(_db.Accounts.AsNoTracking())
                .OrderBy(a => a.AccountCode == "1101" ? 0 : 1)
                .ThenBy(a => a.AccountCode)
                .ThenBy(a => a.AccountName)
                .Select(a => (int?)a.AccountId)
                .FirstOrDefaultAsync();

            if (!cashAccountId.HasValue || cashAccountId.Value <= 0)
                return BadRequest(new { ok = false, message = "لا توجد خزينة/صندوق متاح للحفظ." });

            var collectorName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName : user.DisplayName;
            var areaName = customer.Area?.AreaName ?? customer.RegionName ?? "-";

            var receipt = new CashReceipt
            {
                ReceiptDate = DateTime.Now,
                CustomerId = customer.CustomerId,
                CashAccountId = cashAccountId.Value,
                CounterAccountId = customer.AccountId.Value,
                Amount = request.Amount,
                Description = $"[DISTU:{user.UserId}] إذن استلام من الموقع - المنطقة: {areaName} - الموزع: {collectorName}" +
                              (string.IsNullOrWhiteSpace(request.Notes) ? "" : $" - ملاحظات: {request.Notes!.Trim()}"),
                Status = "غير مرحلة",
                IsPosted = false,
                CreatedAt = DateTime.Now,
                CreatedBy = $"PORTAL:{collectorName}"
            };

            _db.CashReceipts.Add(receipt);
            await _db.SaveChangesAsync();

            receipt.ReceiptNumber = receipt.CashReceiptId.ToString();
            await _db.SaveChangesAsync();

            await _ledgerPostingService.PostCashReceiptAsync(receipt.CashReceiptId, $"PORTAL:{collectorName}");

            var currentBalance = await GetCustomerCurrentBalanceAsync(customer.CustomerId);

            return Json(new
            {
                ok = true,
                message = "تم تسجيل إذن الاستلام بنجاح.",
                receipt = new
                {
                    receipt.CashReceiptId,
                    receipt.ReceiptNumber,
                    receipt.Amount,
                    receipt.ReceiptDate
                },
                customer = new
                {
                    customer.CustomerId,
                    customer.CustomerName,
                    CurrentBalance = currentBalance
                }
            });
        }

        [HttpGet]
        public async Task<IActionResult> ValidateProgramCode(string? code)
        {
            var user = await GetAuthenticatedMobileUserAsync();
            if (user == null)
                return MobileUnauthorized();

            var input = (code ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(input))
                return BadRequest(new { ok = false, message = "اكتب كود البرنامج." });

            var currentProgramCode = await _mobileProgramCodeService.GetProgramCodeAsync();
            if (!string.Equals(input, currentProgramCode, StringComparison.OrdinalIgnoreCase))
                return Unauthorized(new { ok = false, message = "كود البرنامج غير صحيح." });

            return Json(new { ok = true, message = "تم ربط البرنامج بنجاح." });
        }

        [HttpGet]
        public async Task<IActionResult> ResolveCustomerByCode(string? customerCode)
        {
            var user = await GetAuthenticatedMobileUserAsync();
            if (user == null)
                return MobileUnauthorized();

            if (IsCollectorUser(user))
                return Unauthorized(new { ok = false, message = "هذا المسار مخصص للصيدلي وليس للموزع." });

            var code = (customerCode ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(code))
                return BadRequest(new { ok = false, message = "اكتب كود الصيدلية." });

            var customer = await _db.Customers
                .AsNoTracking()
                .Where(c => c.IsActive)
                .Select(c => new
                {
                    c.CustomerId,
                    c.CustomerName,
                    c.ExternalCode,
                    c.CreditLimit,
                    c.CreditLimitTemporaryIncrease,
                    c.CreditLimitTemporaryUntil
                })
                .FirstOrDefaultAsync(c =>
                    (c.ExternalCode != null && c.ExternalCode.Trim() == code) ||
                    c.CustomerId.ToString() == code);

            if (customer == null)
                return NotFound(new { ok = false, message = "كود الصيدلية غير موجود." });

            var currentBalance = await GetCustomerCurrentBalanceAsync(customer.CustomerId);

            var effectiveLimit = CustomerCreditLimitCalculator.GetEffectiveCreditLimit(
                customer.CreditLimit,
                customer.CreditLimitTemporaryIncrease,
                customer.CreditLimitTemporaryUntil,
                DateTime.Now);

            var remainingCredit = effectiveLimit > 0m
                ? Math.Max(0m, effectiveLimit - currentBalance)
                : 0m;

            return Json(new
            {
                ok = true,
                customer = new
                {
                    customer.CustomerId,
                    customer.CustomerName,
                    CustomerCode = customer.ExternalCode,
                    CurrentBalance = currentBalance,
                    customer.CreditLimit,
                    EffectiveCreditLimit = effectiveLimit,
                    RemainingCredit = remainingCredit
                }
            });
        }

        [HttpGet]
        public async Task<IActionResult> ResolveSupplierByCode(string? code)
        {
            var user = await GetAuthenticatedMobileUserAsync();
            if (user == null)
                return MobileUnauthorized();

            if (IsCollectorUser(user))
                return Unauthorized(new { ok = false, message = "هذا المسار مخصص للصيدلي وليس للموزع." });

            var codeNorm = (code ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(codeNorm))
                return BadRequest(new { ok = false, message = "اكتب كود الشركة." });

            var supplier = await _db.Customers
                .AsNoTracking()
                .Where(c => c.IsActive && c.ExternalCode != null)
                .Select(c => new
                {
                    c.CustomerId,
                    c.CustomerName,
                    c.ExternalCode
                })
                .FirstOrDefaultAsync(c => c.ExternalCode!.Trim() == codeNorm);

            if (supplier == null)
                return NotFound(new { ok = false, message = "كود الشركة غير موجود." });

            return Json(new
            {
                ok = true,
                supplier = new
                {
                    SupplierId = supplier.CustomerId,
                    SupplierName = supplier.CustomerName,
                    SupplierCode = supplier.ExternalCode
                }
            });
        }

        [HttpGet]
        public async Task<IActionResult> ResolveSupplierFromProgramSettings()
        {
            var user = await GetAuthenticatedMobileUserAsync();
            if (user == null)
                return MobileUnauthorized();

            if (IsCollectorUser(user))
                return Unauthorized(new { ok = false, message = "هذا المسار مخصص للصيدلي وليس للموزع." });

            var settings = await _mobileProgramCodeService.GetSettingsAsync();
            var programCode = (settings.ProgramCode ?? string.Empty).Trim();
            var companyName = (settings.CompanyName ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(programCode) && string.IsNullOrWhiteSpace(companyName))
                return NotFound(new { ok = false, message = "إعدادات كود/اسم الشركة غير مكتملة." });

            var baseQuery = _db.Customers
                .AsNoTracking()
                .Where(c => c.IsActive)
                .Select(c => new
                {
                    c.CustomerId,
                    c.CustomerName,
                    c.ExternalCode
                });

            var supplier = !string.IsNullOrWhiteSpace(programCode)
                ? await baseQuery.FirstOrDefaultAsync(c =>
                    c.ExternalCode != null && c.ExternalCode.Trim() == programCode)
                : null;

            if (supplier == null && !string.IsNullOrWhiteSpace(companyName))
                supplier = await baseQuery.FirstOrDefaultAsync(c =>
                    c.CustomerName != null &&
                    EF.Functions.Like(c.CustomerName, $"%{companyName}%"));

            if (supplier == null)
                return NotFound(new { ok = false, message = "لم يتم العثور على شركة توزيع مطابقة لإعدادات التطبيق." });

            return Json(new
            {
                ok = true,
                supplier = new
                {
                    SupplierId = supplier.CustomerId,
                    SupplierName = supplier.CustomerName,
                    SupplierCode = supplier.ExternalCode
                }
            });
        }

        [HttpGet]
        public async Task<IActionResult> SupplierProducts(
            int supplierId,
            int customerId,
            int? warehouseId = null,
            string? search = null,
            int page = 1,
            int pageSize = 20)
        {
            var user = await GetAuthenticatedMobileUserAsync();
            if (user == null)
                return MobileUnauthorized();

            if (IsCollectorUser(user))
                return Unauthorized(new { ok = false, message = "هذا المسار مخصص للصيدلي وليس للموزع." });

            if (supplierId <= 0)
                return BadRequest(new { ok = false, message = "بيانات الشركة غير صحيحة." });
            if (customerId <= 0)
                return BadRequest(new { ok = false, message = "بيانات العميل غير صحيحة." });

            page = page <= 0 ? 1 : page;
            pageSize = pageSize < 1 ? 20 : (pageSize > 50 ? 50 : pageSize);

            var supplierExists = await _db.Customers
                .AsNoTracking()
                .AnyAsync(c => c.CustomerId == supplierId && c.IsActive);
            if (!supplierExists)
                return NotFound(new { ok = false, message = "الشركة غير موجودة." });

            var stockBase = _db.StockBatches
                .AsNoTracking()
                .Where(sb => sb.QtyOnHand > 0);

            if (warehouseId.HasValue && warehouseId.Value > 0)
                stockBase = stockBase.Where(sb => sb.WarehouseId == warehouseId.Value);

            var stockRows = await stockBase
                .Where(sb => sb.Expiry.HasValue)
                .Select(sb => new
                {
                    sb.ProdId,
                    BatchNo = (sb.BatchNo ?? string.Empty).Trim(),
                    Expiry = sb.Expiry!.Value.Date,
                    sb.QtyOnHand
                })
                .ToListAsync();

            if (stockRows.Count == 0)
                return Json(new { ok = true, page, pageSize, total = 0, items = new List<object>() });

            var stockProdIds = stockRows.Select(x => x.ProdId).Distinct().ToList();

            IQueryable<VendorProductMapping> query = _db.VendorProductMappings
                .AsNoTracking()
                .Where(x => x.CustomerId == supplierId && stockProdIds.Contains(x.ProductId))
                .Include(x => x.Product);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                query = query.Where(x =>
                    (x.VendorProductName != null && EF.Functions.Like(x.VendorProductName, $"%{s}%")) ||
                    (x.VendorProductCode != null && EF.Functions.Like(x.VendorProductCode, $"%{s}%")) ||
                    (x.Product != null && x.Product.ProdName != null && EF.Functions.Like(x.Product.ProdName, $"%{s}%")) ||
                    x.ProductId.ToString().Contains(s));
            }

            var mappedRows = await query
                .OrderBy(x => x.VendorProductName)
                .Select(x => new
                {
                    x.ProductId,
                    VendorName = x.VendorProductName,
                    x.VendorProductCode,
                    SupplierPrice = x.PriceRetail,
                    ProductName = x.Product != null ? x.Product.ProdName : null,
                    ProductRetail = x.Product != null ? (decimal?)x.Product.PriceRetail : null
                })
                .ToListAsync();

            var items = new List<object>();
            var mappedProductIds = new HashSet<int>(mappedRows.Select(r => r.ProductId));

            async Task<(decimal weightedDiscount, decimal displaySaleDiscount)> ResolveDisplayedDiscountsAsync(int prodId)
            {
                int? targetWarehouseId = warehouseId.HasValue && warehouseId.Value > 0
                    ? warehouseId.Value
                    : null;

                var weightedDiscount = await _stockAnalysisService.GetEffectivePurchaseDiscountAsync(prodId, targetWarehouseId, null);
                weightedDiscount = Math.Clamp(weightedDiscount, 0m, 100m);

                if (!targetWarehouseId.HasValue)
                {
                    int policyId = 1;
                    if (customerId > 0)
                    {
                        var customerPolicyId = await _db.Customers
                            .AsNoTracking()
                            .Where(c => c.CustomerId == customerId)
                            .Select(c => c.PolicyId)
                            .FirstOrDefaultAsync();

                        if (customerPolicyId.HasValue && customerPolicyId.Value > 0)
                            policyId = customerPolicyId.Value;
                    }

                    var productGroupId = await _db.Products
                        .AsNoTracking()
                        .Where(p => p.ProdId == prodId)
                        .Select(p => (int?)p.ProductGroupId)
                        .FirstOrDefaultAsync();

                    decimal policyDisplayDiscount = 0m;

                    if (productGroupId.HasValue && productGroupId.Value > 0)
                    {
                        policyDisplayDiscount = await _db.ProductGroupPolicies
                            .AsNoTracking()
                            .Where(gp =>
                                gp.ProductGroupId == productGroupId.Value &&
                                gp.PolicyId == policyId &&
                                gp.IsActive &&
                                gp.MaxDiscountToCustomer > 0m)
                            .Select(gp => gp.MaxDiscountToCustomer ?? 0m)
                            .OrderByDescending(x => x)
                            .FirstOrDefaultAsync();
                    }

                    if (policyDisplayDiscount <= 0m)
                    {
                        policyDisplayDiscount = await _db.WarehousePolicyRules
                            .AsNoTracking()
                            .Where(r =>
                                r.PolicyId == policyId &&
                                r.IsActive &&
                                r.MaxDiscountToCustomer > 0m)
                            .Select(r => r.MaxDiscountToCustomer ?? 0m)
                            .OrderByDescending(x => x)
                            .FirstOrDefaultAsync();
                    }

                    if (policyDisplayDiscount <= 0m)
                        policyDisplayDiscount = weightedDiscount;

                    return (weightedDiscount, Math.Clamp(policyDisplayDiscount, 0m, 100m));
                }

                var saleDetails = await _stockAnalysisService.GetSaleDiscountDetailsAsync(
                    prodId,
                    targetWarehouseId.Value,
                    customerId,
                    weightedDiscount);

                var displaySaleDiscount = saleDetails.SaleDiscount;
                if (displaySaleDiscount <= 0m)
                {
                    var configuredPolicyDiscount = await _stockAnalysisService.GetConfiguredPolicyDiscountAsync(
                        prodId,
                        targetWarehouseId.Value,
                        customerId);

                    displaySaleDiscount = configuredPolicyDiscount > 0m
                        ? configuredPolicyDiscount
                        : weightedDiscount;
                }

                return (weightedDiscount, Math.Clamp(displaySaleDiscount, 0m, 100m));
            }

            foreach (var row in mappedRows)
            {
                var basePrice = row.ProductRetail ?? 0m;
                var companyPrice = row.SupplierPrice ?? basePrice;
                decimal companySaleDiscount = 0m;
                if (basePrice > 0m && companyPrice >= 0m && companyPrice <= basePrice)
                    companySaleDiscount = Math.Round((1m - (companyPrice / basePrice)) * 100m, 2, MidpointRounding.AwayFromZero);

                var (weightedDiscount, policyDiscount) = await ResolveDisplayedDiscountsAsync(row.ProductId);
                var productStocks = stockRows.Where(x => x.ProdId == row.ProductId).ToList();
                foreach (var stockRow in productStocks)
                {
                    items.Add(new
                    {
                        ItemKey = $"{row.ProductId}|{stockRow.BatchNo}|{stockRow.Expiry:yyyy-MM-dd}",
                        ProdId = row.ProductId,
                        Name = row.ProductName ?? row.VendorName ?? string.Empty,
                        VendorProductName = row.VendorName ?? string.Empty,
                        VendorProductCode = row.VendorProductCode ?? string.Empty,
                        SupplierPrice = companyPrice,
                        BasePrice = basePrice,
                        CompanySaleDiscountPercent = companySaleDiscount,
                        CustomerPolicyDiscountPercent = policyDiscount,
                        WeightedDiscountPercent = weightedDiscount,
                        AvailableQty = stockRow.QtyOnHand,
                        BatchNo = stockRow.BatchNo,
                        Expiry = stockRow.Expiry
                    });
                }
            }

            // Fallback: أي أصناف موجودة بالمخزن وليس لها Mapping عند الشركة الحالية.
            var missingProdIds = stockProdIds.Where(id => !mappedProductIds.Contains(id)).ToList();
            if (missingProdIds.Count > 0)
            {
                var fallbackProductsQuery = _db.Products
                    .AsNoTracking()
                    .Where(p => p.IsActive && missingProdIds.Contains(p.ProdId));

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var s = search.Trim();
                    fallbackProductsQuery = fallbackProductsQuery.Where(p =>
                        (p.ProdName != null && EF.Functions.Like(p.ProdName, $"%{s}%")) ||
                        p.ProdId.ToString().Contains(s));
                }

                var fallbackProducts = await fallbackProductsQuery
                    .Select(p => new { p.ProdId, p.ProdName, p.PriceRetail })
                    .ToListAsync();

                foreach (var p in fallbackProducts)
                {
                    var (weightedDiscount, policyDiscount) = await ResolveDisplayedDiscountsAsync(p.ProdId);
                    var productStocks = stockRows.Where(x => x.ProdId == p.ProdId).ToList();
                    foreach (var stockRow in productStocks)
                    {
                        items.Add(new
                        {
                            ItemKey = $"{p.ProdId}|{stockRow.BatchNo}|{stockRow.Expiry:yyyy-MM-dd}",
                            ProdId = p.ProdId,
                            Name = p.ProdName ?? string.Empty,
                            VendorProductName = p.ProdName ?? string.Empty,
                            VendorProductCode = string.Empty,
                            SupplierPrice = p.PriceRetail,
                            BasePrice = p.PriceRetail,
                            CompanySaleDiscountPercent = 0m,
                            CustomerPolicyDiscountPercent = policyDiscount,
                            WeightedDiscountPercent = weightedDiscount,
                            AvailableQty = stockRow.QtyOnHand,
                            BatchNo = stockRow.BatchNo,
                            Expiry = stockRow.Expiry
                        });
                    }
                }
            }

            // Paging بعد دمج source mapping + fallback
            var total = items.Count;
            var pageItems = items
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return Json(new { ok = true, page, pageSize, total, items = pageItems });
        }

        [HttpGet]
        public async Task<IActionResult> CompareAcrossSuppliers(string? search = null, int page = 1, int pageSize = 20)
        {
            var user = await GetAuthenticatedMobileUserAsync();
            if (user == null)
                return MobileUnauthorized();

            if (IsCollectorUser(user))
                return Unauthorized(new { ok = false, message = "هذا المسار مخصص للصيدلي وليس للموزع." });

            page = page <= 0 ? 1 : page;
            pageSize = pageSize < 1 ? 20 : (pageSize > 50 ? 50 : pageSize);

            var q = _db.VendorProductMappings
                .AsNoTracking()
                .Include(x => x.Product)
                .Include(x => x.Customer)
                .Where(x => x.Product != null && x.Customer != null);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                q = q.Where(x =>
                    (x.Product != null && x.Product.ProdName != null && EF.Functions.Like(x.Product.ProdName, $"%{s}%")) ||
                    (x.VendorProductName != null && EF.Functions.Like(x.VendorProductName, $"%{s}%")) ||
                    (x.Customer != null && x.Customer.CustomerName != null && EF.Functions.Like(x.Customer.CustomerName, $"%{s}%")) ||
                    x.ProductId.ToString().Contains(s));
            }

            var materialized = await q
                .Select(x => new
                {
                    x.ProductId,
                    ProductName = x.Product != null ? x.Product.ProdName : "",
                    BasePrice = x.Product != null ? x.Product.PriceRetail : 0m,
                    SupplierId = x.CustomerId,
                    SupplierName = x.Customer != null ? x.Customer.CustomerName : "",
                    SupplierCode = x.Customer != null ? x.Customer.ExternalCode : "",
                    SupplierPrice = x.PriceRetail
                })
                .ToListAsync();

            var groups = materialized
                .GroupBy(x => new { x.ProductId, x.ProductName, x.BasePrice })
                .Select(g => new
                {
                    g.Key.ProductId,
                    Name = g.Key.ProductName ?? string.Empty,
                    BasePrice = g.Key.BasePrice,
                    Offers = g.Select(o =>
                    {
                        var offerPrice = o.SupplierPrice ?? g.Key.BasePrice;
                        decimal companySaleDiscount = 0m;
                        if (g.Key.BasePrice > 0m && offerPrice >= 0m && offerPrice <= g.Key.BasePrice)
                            companySaleDiscount = Math.Round((1m - (offerPrice / g.Key.BasePrice)) * 100m, 2, MidpointRounding.AwayFromZero);

                        return new
                        {
                            o.SupplierId,
                            o.SupplierName,
                            o.SupplierCode,
                            SupplierPrice = offerPrice,
                            CompanySaleDiscountPercent = companySaleDiscount
                        };
                    }).OrderBy(x => x.SupplierPrice).ToList()
                })
                .Where(x => x.Offers.Count > 0)
                .OrderBy(x => x.Name)
                .ToList();

            var total = groups.Count;
            var items = groups.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            return Json(new { ok = true, page, pageSize, total, items });
        }

        [HttpGet]
        public async Task<IActionResult> CustomerStatement(int customerId, int take = 60)
        {
            var user = await GetAuthenticatedMobileUserAsync();
            if (user == null)
                return MobileUnauthorized();

            Response.Headers["Cache-Control"] = "no-store, no-cache, max-age=0, must-revalidate";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Expires"] = "0";

            if (customerId <= 0)
                return BadRequest(new { ok = false, message = "بيانات العميل غير صحيحة." });

            if (IsCollectorUser(user) && !await CanCollectorAccessCustomerAsync(user.UserId, customerId))
                return Unauthorized(new { ok = false, message = "هذا العميل غير مربوط بهذا الموزع." });

            var customer = await _db.Customers
                .AsNoTracking()
                .Where(c => c.CustomerId == customerId && c.IsActive)
                .Select(c => new
                {
                    c.CustomerId,
                    c.CustomerName,
                    c.ExternalCode,
                    c.CreditLimit,
                    c.CreditLimitTemporaryIncrease,
                    c.CreditLimitTemporaryUntil
                })
                .FirstOrDefaultAsync();

            if (customer == null)
                return NotFound(new { ok = false, message = "العميل غير موجود." });

            var safeTake = Math.Clamp(take, 20, 200);

            var allEntries = await _db.LedgerEntries
                .AsNoTracking()
                .Include(x => x.Account)
                .Where(x => x.CustomerId == customerId)
                .OrderBy(x => x.EntryDate)
                .ThenBy(x => x.Id)
                .ToListAsync();

            static bool IsReverse(LedgerEntry entry) =>
                (entry.Description ?? string.Empty).Contains("عكس ترحيل", StringComparison.Ordinal);

            static bool IsAlwaysVisibleType(LedgerSourceType sourceType) =>
                sourceType == LedgerSourceType.Opening
                || sourceType == LedgerSourceType.Journal
                || sourceType == LedgerSourceType.Adjustment
                || sourceType == LedgerSourceType.StockTransfer
                || sourceType == LedgerSourceType.StockAdjustment;

            var salesInvoiceIds = allEntries
                .Where(x => x.SourceType == LedgerSourceType.SalesInvoice && x.SourceId.HasValue)
                .Select(x => x.SourceId!.Value)
                .Distinct()
                .ToList();
            var salesReturnIds = allEntries
                .Where(x => x.SourceType == LedgerSourceType.SalesReturn && x.SourceId.HasValue)
                .Select(x => x.SourceId!.Value)
                .Distinct()
                .ToList();
            var purchaseInvoiceIds = allEntries
                .Where(x => x.SourceType == LedgerSourceType.PurchaseInvoice && x.SourceId.HasValue)
                .Select(x => x.SourceId!.Value)
                .Distinct()
                .ToList();
            var purchaseReturnIds = allEntries
                .Where(x => x.SourceType == LedgerSourceType.PurchaseReturn && x.SourceId.HasValue)
                .Select(x => x.SourceId!.Value)
                .Distinct()
                .ToList();
            var receiptIds = allEntries
                .Where(x => x.SourceType == LedgerSourceType.Receipt && x.SourceId.HasValue)
                .Select(x => x.SourceId!.Value)
                .Distinct()
                .ToList();
            var paymentIds = allEntries
                .Where(x => x.SourceType == LedgerSourceType.Payment && x.SourceId.HasValue)
                .Select(x => x.SourceId!.Value)
                .Distinct()
                .ToList();
            var debitNoteIds = allEntries
                .Where(x => x.SourceType == LedgerSourceType.DebitNote && x.SourceId.HasValue)
                .Select(x => x.SourceId!.Value)
                .Distinct()
                .ToList();
            var creditNoteIds = allEntries
                .Where(x => x.SourceType == LedgerSourceType.CreditNote && x.SourceId.HasValue)
                .Select(x => x.SourceId!.Value)
                .Distinct()
                .ToList();

            var existingSalesInvoices = new HashSet<int>(await _db.SalesInvoices
                .AsNoTracking()
                .Where(x => salesInvoiceIds.Contains(x.SIId))
                .Select(x => x.SIId)
                .ToListAsync());
            var existingSalesReturns = new HashSet<int>(await _db.SalesReturns
                .AsNoTracking()
                .Where(x => salesReturnIds.Contains(x.SRId))
                .Select(x => x.SRId)
                .ToListAsync());
            var existingPurchaseInvoices = new HashSet<int>(await _db.PurchaseInvoices
                .AsNoTracking()
                .Where(x => purchaseInvoiceIds.Contains(x.PIId))
                .Select(x => x.PIId)
                .ToListAsync());
            var existingPurchaseReturns = new HashSet<int>(await _db.PurchaseReturns
                .AsNoTracking()
                .Where(x => purchaseReturnIds.Contains(x.PRetId))
                .Select(x => x.PRetId)
                .ToListAsync());
            var existingReceipts = new HashSet<int>(await _db.CashReceipts
                .AsNoTracking()
                .Where(x => receiptIds.Contains(x.CashReceiptId))
                .Select(x => x.CashReceiptId)
                .ToListAsync());
            var existingPayments = new HashSet<int>(await _db.CashPayments
                .AsNoTracking()
                .Where(x => paymentIds.Contains(x.CashPaymentId))
                .Select(x => x.CashPaymentId)
                .ToListAsync());
            var existingDebitNotes = new HashSet<int>(await _db.DebitNotes
                .AsNoTracking()
                .Where(x => debitNoteIds.Contains(x.DebitNoteId))
                .Select(x => x.DebitNoteId)
                .ToListAsync());
            var existingCreditNotes = new HashSet<int>(await _db.CreditNotes
                .AsNoTracking()
                .Where(x => creditNoteIds.Contains(x.CreditNoteId))
                .Select(x => x.CreditNoteId)
                .ToListAsync());

            bool SourceExists(LedgerEntry entry)
            {
                if (!entry.SourceId.HasValue)
                    return true;
                if (IsAlwaysVisibleType(entry.SourceType))
                    return true;

                return entry.SourceType switch
                {
                    LedgerSourceType.SalesInvoice => existingSalesInvoices.Contains(entry.SourceId.Value),
                    LedgerSourceType.SalesReturn => existingSalesReturns.Contains(entry.SourceId.Value),
                    LedgerSourceType.PurchaseInvoice => existingPurchaseInvoices.Contains(entry.SourceId.Value),
                    LedgerSourceType.PurchaseReturn => existingPurchaseReturns.Contains(entry.SourceId.Value),
                    LedgerSourceType.Receipt => existingReceipts.Contains(entry.SourceId.Value),
                    LedgerSourceType.Payment => existingPayments.Contains(entry.SourceId.Value),
                    LedgerSourceType.DebitNote => existingDebitNotes.Contains(entry.SourceId.Value),
                    LedgerSourceType.CreditNote => existingCreditNotes.Contains(entry.SourceId.Value),
                    _ => false
                };
            }

            var entriesBySource = allEntries
                .Where(x => x.SourceId.HasValue && !IsAlwaysVisibleType(x.SourceType))
                .GroupBy(x => (x.SourceType, SourceId: x.SourceId!.Value))
                .ToDictionary(g => g.Key, g => g.ToList());

            var filteredEntries = new List<LedgerEntry>();
            foreach (var entry in allEntries)
            {
                if (!entry.SourceId.HasValue || IsAlwaysVisibleType(entry.SourceType))
                {
                    filteredEntries.Add(entry);
                    continue;
                }

                var key = (entry.SourceType, SourceId: entry.SourceId.Value);
                if (!entriesBySource.TryGetValue(key, out var sourceEntries))
                    continue;

                var sourceExists = SourceExists(entry);
                if (!sourceExists)
                {
                    if (IsReverse(entry))
                        filteredEntries.Add(entry);

                    continue;
                }

                if (IsReverse(entry))
                    continue;

                var lastStage = sourceEntries
                    .Where(x => !IsReverse(x))
                    .Select(x => x.PostVersion)
                    .DefaultIfEmpty(0)
                    .Max();

                if (entry.PostVersion == lastStage)
                    filteredEntries.Add(entry);
            }

            var visibleSalesInvoiceIds = filteredEntries
                .Where(x => x.SourceType == LedgerSourceType.SalesInvoice && x.SourceId.HasValue)
                .Select(x => x.SourceId!.Value)
                .Distinct()
                .ToList();

            var invoiceLinesLookup = visibleSalesInvoiceIds.Count == 0
                ? new Dictionary<int, List<object>>()
                : await _db.SalesInvoiceLines
                    .AsNoTracking()
                    .Include(x => x.Product)
                    .Where(x => visibleSalesInvoiceIds.Contains(x.SIId))
                    .OrderBy(x => x.SIId)
                    .ThenBy(x => x.LineNo)
                    .GroupBy(x => x.SIId)
                    .ToDictionaryAsync(
                        g => g.Key,
                        g => g.Select(line => (object)new
                        {
                            line.LineNo,
                            line.ProdId,
                            ProductName = line.Product != null ? line.Product.ProdName : string.Empty,
                            line.Qty,
                            line.PriceRetail,
                            line.Disc1Percent,
                            line.UnitSalePrice,
                            line.LineNetTotal
                        }).ToList());

            decimal running = 0m;
            var mapped = new List<object>(filteredEntries.Count);
            foreach (var entry in filteredEntries.OrderBy(x => x.EntryDate).ThenBy(x => x.Id))
            {
                running += (entry.Debit - entry.Credit);
                var hasDetails = entry.SourceType == LedgerSourceType.SalesInvoice
                    && entry.SourceId.HasValue
                    && invoiceLinesLookup.ContainsKey(entry.SourceId.Value);

                mapped.Add(new
                {
                    entry.Id,
                    EntryDate = entry.EntryDate,
                    SourceType = SourceTypeDisplayAr(entry.SourceType),
                    SourceTypeCode = entry.SourceType.ToString(),
                    entry.SourceId,
                    entry.VoucherNo,
                    Account = entry.Account != null
                        ? $"{entry.Account.AccountCode} - {entry.Account.AccountName}"
                        : string.Empty,
                    entry.Description,
                    entry.Debit,
                    entry.Credit,
                    RunningBalance = Math.Round(running, 2, MidpointRounding.AwayFromZero)
                    ,
                    HasDetails = hasDetails,
                    Details = (object)(hasDetails && entry.SourceId.HasValue
                        ? invoiceLinesLookup[entry.SourceId.Value]
                        : Array.Empty<object>())
                });
            }

            var currentBalance = Math.Round(running, 2, MidpointRounding.AwayFromZero);

            var recent = mapped
                .TakeLast(safeTake)
                .Reverse()
                .ToList();

            var effectiveLimit = CustomerCreditLimitCalculator.GetEffectiveCreditLimit(
                customer.CreditLimit,
                customer.CreditLimitTemporaryIncrease,
                customer.CreditLimitTemporaryUntil,
                DateTime.Now);

            var remainingCredit = effectiveLimit > 0m
                ? Math.Max(0m, effectiveLimit - currentBalance)
                : 0m;

            return Json(new
            {
                ok = true,
                summary = new
                {
                    customer.CustomerId,
                    customer.CustomerName,
                    CustomerCode = customer.ExternalCode,
                    CurrentBalance = currentBalance,
                    customer.CreditLimit,
                    EffectiveCreditLimit = effectiveLimit,
                    RemainingCredit = remainingCredit,
                    TotalDebit = filteredEntries.Sum(x => x.Debit),
                    TotalCredit = filteredEntries.Sum(x => x.Credit),
                    Net = filteredEntries.Sum(x => x.Debit) - filteredEntries.Sum(x => x.Credit)
                },
                entries = recent
            });
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> CreateInvoice([FromBody] CreateInvoiceRequest request)
        {
            var user = await GetAuthenticatedMobileUserAsync();
            if (user == null)
                return MobileUnauthorized();

            if (IsCollectorUser(user))
                return Unauthorized(new { ok = false, message = "إنشاء الفواتير غير متاح للموزع." });

            if (request == null || request.WarehouseId <= 0 || request.CustomerId <= 0 || request.Items == null || request.Items.Count == 0)
                return BadRequest(new { ok = false, message = "بيانات الفاتورة غير مكتملة." });

            var customerId = request.CustomerId;
            var customer = await _db.Customers
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.CustomerId == customerId);

            if (customer == null || !customer.IsActive)
                return BadRequest(new { ok = false, message = "العميل غير موجود أو غير نشط." });

            var now = DateTime.Now;
            var displayName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName : user.DisplayName;
            var mobileCreatedBy = $"MOBILE:{user.UserName}";
            var normalizedItems = request.Items
                .GroupBy(x => new
                {
                    x.ProdId,
                    BatchNo = (x.BatchNo ?? string.Empty).Trim(),
                    Expiry = x.Expiry?.Date
                })
                .Select(g => new CreateInvoiceItem
                {
                    ProdId = g.Key.ProdId,
                    BatchNo = g.Key.BatchNo,
                    Expiry = g.Key.Expiry,
                    Qty = g.Sum(x => x.Qty)
                })
                .ToList();

            var preparedLines = new List<(int ProdId, string ProdName, int Qty, decimal PriceRetail, decimal SaleDiscount, decimal WeightedDiscount, decimal UnitSalePrice, decimal LineTotal, string BatchNo, DateTime Expiry)>();

            foreach (var item in normalizedItems)
            {
                if (item.ProdId <= 0 || item.Qty <= 0)
                    return BadRequest(new { ok = false, message = "يوجد صنف أو كمية غير صحيحة داخل السلة." });

                var product = await _db.Products
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.ProdId == item.ProdId && p.IsActive);
                if (product == null)
                    return BadRequest(new { ok = false, message = $"الصنف رقم {item.ProdId} غير موجود أو غير نشط." });

                var availableQty = await _db.StockBatches
                    .AsNoTracking()
                    .Where(sb => sb.WarehouseId == request.WarehouseId && sb.ProdId == item.ProdId)
                    .SumAsync(sb => (int?)sb.QtyOnHand) ?? 0;
                if (availableQty < item.Qty)
                    return BadRequest(new { ok = false, message = $"الكمية غير متاحة للصنف {product.ProdName}. المتاح {availableQty} فقط." });

                var availableFromLedger = await _db.StockLedger
                    .AsNoTracking()
                    .Where(x => x.WarehouseId == request.WarehouseId && x.ProdId == item.ProdId && x.QtyIn > 0)
                    .SumAsync(x => x.RemainingQty ?? 0);
                if (item.Qty > availableFromLedger)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        message = availableFromLedger == 0
                            ? $"لا يمكن بيع الصنف {product.ProdName}: لا يوجد رصيد مسجل في دفتر الحركة."
                            : $"الكمية المطلوبة للصنف {product.ProdName} ({item.Qty}) تتجاوز الرصيد الفعلي في دفتر الحركة ({availableFromLedger})."
                    });
                }

                if (string.IsNullOrWhiteSpace(item.BatchNo) || !item.Expiry.HasValue)
                    return BadRequest(new { ok = false, message = $"بيانات التشغيلة أو الصلاحية غير مكتملة للصنف {product.ProdName}." });

                var candidates = await _db.StockBatches
                    .AsNoTracking()
                    .Where(sb =>
                        sb.WarehouseId == request.WarehouseId &&
                        sb.ProdId == item.ProdId &&
                        (sb.BatchNo ?? string.Empty).Trim() == item.BatchNo &&
                        sb.Expiry.HasValue &&
                        sb.Expiry.Value.Date == item.Expiry.Value.Date &&
                        sb.QtyOnHand > 0 &&
                        sb.Expiry.HasValue)
                    .OrderBy(sb => sb.Expiry)
                    .ThenBy(sb => sb.BatchNo)
                    .Select(sb => new { sb.BatchNo, Expiry = sb.Expiry!.Value, sb.QtyOnHand })
                    .ToListAsync();

                var remainingToSell = item.Qty;
                var segments = new List<(string BatchNo, DateTime Expiry, int Qty)>();
                foreach (var candidate in candidates)
                {
                    if (remainingToSell <= 0)
                        break;

                    var take = Math.Min(remainingToSell, candidate.QtyOnHand);
                    if (take <= 0)
                        continue;

                    segments.Add(((candidate.BatchNo ?? string.Empty).Trim(), candidate.Expiry.Date, take));
                    remainingToSell -= take;
                }

                if (remainingToSell > 0)
                    return BadRequest(new { ok = false, message = $"المخزون غير كاف للصنف {product.ProdName}. المتبقي غير متاح: {remainingToSell}" });

                foreach (var segment in segments)
                {
                    var batchRow = await _db.Batches
                        .AsNoTracking()
                        .Where(b =>
                            b.ProdId == item.ProdId &&
                            b.BatchNo == segment.BatchNo &&
                            b.Expiry.Date == segment.Expiry.Date)
                        .OrderByDescending(b => b.EntryDate)
                        .FirstOrDefaultAsync();

                    var weightedDiscount = await _stockAnalysisService.GetEffectivePurchaseDiscountAsync(
                        item.ProdId,
                        request.WarehouseId,
                        batchRow?.BatchId);

                    var saleDiscount = await _stockAnalysisService.GetSaleDiscountAsync(
                        item.ProdId,
                        request.WarehouseId,
                        customerId,
                        weightedDiscount);

                    if (saleDiscount <= 0m)
                    {
                        var configuredPolicyDiscount = await _stockAnalysisService.GetConfiguredPolicyDiscountAsync(
                            item.ProdId,
                            request.WarehouseId,
                            customerId);
                        if (configuredPolicyDiscount > 0m)
                            saleDiscount = configuredPolicyDiscount;
                    }

                    var priceRetail = batchRow?.PriceRetailBatch ?? product.PriceRetail;
                    var unitSalePrice = Math.Round(priceRetail * (1m - (saleDiscount / 100m)), 2, MidpointRounding.AwayFromZero);
                    var lineTotal = Math.Round(unitSalePrice * segment.Qty, 2, MidpointRounding.AwayFromZero);

                    preparedLines.Add((
                        item.ProdId,
                        product.ProdName,
                        segment.Qty,
                        priceRetail,
                        saleDiscount,
                        weightedDiscount,
                        unitSalePrice,
                        lineTotal,
                        segment.BatchNo,
                        segment.Expiry
                    ));
                }
            }

            var estimatedNet = preparedLines.Sum(x => x.LineTotal);
            var currentBalance = await GetCustomerCurrentBalanceAsync(customerId);
            var effectiveLimit = CustomerCreditLimitCalculator.GetEffectiveCreditLimit(
                customer.CreditLimit,
                customer.CreditLimitTemporaryIncrease,
                customer.CreditLimitTemporaryUntil,
                now);
            if (effectiveLimit > 0m)
            {
                var projectedBalance = currentBalance + estimatedNet;
                if (projectedBalance > effectiveLimit)
                {
                    var available = Math.Max(0m, effectiveLimit - currentBalance);
                    return BadRequest(new
                    {
                        ok = false,
                        message = $"لا يمكن إنشاء الفاتورة: ستتجاوز الحد الائتماني. المتاح الآن {available:F2} ج.م"
                    });
                }
            }

            await using var tx = await _db.Database.BeginTransactionAsync();

            var invoice = new SalesInvoice
            {
                SIDate = now.Date,
                SITime = now.TimeOfDay,
                CustomerId = customerId,
                WarehouseId = request.WarehouseId,
                PaymentMethod = "آجل",
                Status = "غير مرحلة",
                IsPosted = false,
                CreatedBy = mobileCreatedBy,
                CreatedAt = now,
                CustomerBalanceAtSave = currentBalance
            };

            _db.SalesInvoices.Add(invoice);
            await _db.SaveChangesAsync();

            var lineNo = 1;
            foreach (var line in preparedLines)
            {
                var affectedLine = new SalesInvoiceLine
                {
                    SIId = invoice.SIId,
                    LineNo = lineNo++,
                    ProdId = line.ProdId,
                    Qty = line.Qty,
                    PriceRetail = line.PriceRetail,
                    Disc1Percent = line.SaleDiscount,
                    Disc2Percent = 0m,
                    Disc3Percent = 0m,
                    DiscountValue = 0m,
                    UnitSalePrice = line.UnitSalePrice,
                    LineTotalAfterDiscount = line.LineTotal,
                    TaxPercent = 0m,
                    TaxValue = 0m,
                    LineNetTotal = line.LineTotal,
                    PurchaseDiscountEffective = line.WeightedDiscount,
                    CostPerUnit = 0m,
                    CostTotal = 0m,
                    ProfitValue = 0m,
                    ProfitPercent = 0m,
                    BatchNo = line.BatchNo,
                    Expiry = line.Expiry
                };
                _db.SalesInvoiceLines.Add(affectedLine);
                await _db.SaveChangesAsync();

                var outLedger = new StockLedger
                {
                    TranDate = DateTime.UtcNow,
                    WarehouseId = invoice.WarehouseId,
                    ProdId = line.ProdId,
                    BatchNo = line.BatchNo,
                    Expiry = line.Expiry,
                    BatchId = null,
                    QtyIn = 0,
                    QtyOut = line.Qty,
                    UnitCost = 0m,
                    RemainingQty = null,
                    SourceType = "Sales",
                    SourceId = invoice.SIId,
                    SourceLine = affectedLine.LineNo,
                    Note = $"Sales Line: {line.ProdName}"
                };

                _db.StockLedger.Add(outLedger);
                await _db.SaveChangesAsync();

                static decimal InflowUnitCostForFifo(StockLedger inLedger)
                {
                    if (inLedger.QtyIn <= 0) return inLedger.UnitCost;
                    if (inLedger.UnitCost != 0m) return inLedger.UnitCost;
                    if (inLedger.TotalCost.HasValue && inLedger.TotalCost.Value != 0m)
                        return Math.Round(inLedger.TotalCost.Value / inLedger.QtyIn, 4, MidpointRounding.AwayFromZero);
                    return 0m;
                }

                var need = line.Qty;
                decimal costTotal = 0m;

                var inLedgers = await _db.StockLedger
                    .Where(x =>
                        x.WarehouseId == invoice.WarehouseId &&
                        x.ProdId == line.ProdId &&
                        x.QtyIn > 0 &&
                        (x.RemainingQty ?? 0) > 0 &&
                        (x.BatchNo ?? "").Trim() == (line.BatchNo ?? "").Trim() &&
                        ((x.Expiry.HasValue ? x.Expiry.Value.Date : (DateTime?)null) == line.Expiry.Date))
                    .OrderBy(x => x.Expiry)
                    .ThenBy(x => x.EntryId)
                    .ToListAsync();

                foreach (var inLedger in inLedgers)
                {
                    if (need <= 0) break;

                    var available = inLedger.RemainingQty ?? 0;
                    if (available <= 0) continue;

                    var take = Math.Min(need, available);
                    inLedger.RemainingQty = available - take;

                    var inflowUnitCost = InflowUnitCostForFifo(inLedger);
                    _db.Set<StockFifoMap>().Add(new StockFifoMap
                    {
                        OutEntryId = outLedger.EntryId,
                        InEntryId = inLedger.EntryId,
                        Qty = take,
                        UnitCost = inflowUnitCost
                    });

                    costTotal += take * inflowUnitCost;
                    need -= take;
                }

                if (need > 0)
                {
                    var fallbackInLedgers = await _db.StockLedger
                        .Where(x =>
                            x.WarehouseId == invoice.WarehouseId &&
                            x.ProdId == line.ProdId &&
                            x.QtyIn > 0 &&
                            (x.RemainingQty ?? 0) > 0)
                        .OrderBy(x => x.Expiry)
                        .ThenBy(x => x.EntryId)
                        .ToListAsync();

                    foreach (var inLedger in fallbackInLedgers)
                    {
                        if (need <= 0) break;

                        var available = inLedger.RemainingQty ?? 0;
                        if (available <= 0) continue;

                        var take = Math.Min(need, available);
                        inLedger.RemainingQty = available - take;

                        var inflowUnitCost = InflowUnitCostForFifo(inLedger);
                        _db.Set<StockFifoMap>().Add(new StockFifoMap
                        {
                            OutEntryId = outLedger.EntryId,
                            InEntryId = inLedger.EntryId,
                            Qty = take,
                            UnitCost = inflowUnitCost
                        });

                        costTotal += take * inflowUnitCost;
                        need -= take;
                    }
                }

                var costPerUnit = line.Qty > 0
                    ? Math.Round(costTotal / line.Qty, 2, MidpointRounding.AwayFromZero)
                    : 0m;

                outLedger.UnitCost = costPerUnit;
                outLedger.TotalCost = Math.Round(costTotal, 2, MidpointRounding.AwayFromZero);

                var profitValue = line.LineTotal - costTotal;
                var profitPercent = line.LineTotal > 0m
                    ? (profitValue / line.LineTotal) * 100m
                    : 0m;

                affectedLine.CostPerUnit = costPerUnit;
                affectedLine.CostTotal = Math.Round(costTotal, 2, MidpointRounding.AwayFromZero);
                affectedLine.ProfitValue = Math.Round(profitValue, 2, MidpointRounding.AwayFromZero);
                affectedLine.ProfitPercent = Math.Round(profitPercent, 2, MidpointRounding.AwayFromZero);

                var sbRow = await _db.StockBatches.FirstOrDefaultAsync(x =>
                    x.WarehouseId == invoice.WarehouseId &&
                    x.ProdId == line.ProdId &&
                    x.BatchNo == line.BatchNo &&
                    x.Expiry.HasValue &&
                    x.Expiry.Value.Date == line.Expiry.Date);

                if (sbRow == null)
                    return BadRequest(new { ok = false, message = $"تعذر تحديث رصيد التشغيلة للصنف {line.ProdName}." });

                if (sbRow.QtyOnHand < line.Qty)
                    return BadRequest(new { ok = false, message = $"المخزون غير كاف داخل التشغيلة للصنف {line.ProdName}." });

                sbRow.QtyOnHand -= line.Qty;
                sbRow.UpdatedAt = DateTime.UtcNow;
                sbRow.Note = $"SI:{invoice.SIId} Line:{affectedLine.LineNo} (-{line.Qty})";

                await _db.SaveChangesAsync();
            }

            await _docTotals.RecalcSalesInvoiceTotalsAsync(invoice.SIId);
            await tx.CommitAsync();

            var freshHeader = await _db.SalesInvoices
                .AsNoTracking()
                .Where(x => x.SIId == invoice.SIId)
                .Select(x => new { x.SIId, x.NetTotal, x.TotalBeforeDiscount, x.TotalAfterDiscountBeforeTax })
                .FirstAsync();

            return Json(new { ok = true, message = "تم إنشاء الفاتورة بنجاح.", invoice = freshHeader });
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> PostInvoice([FromBody] PostInvoiceRequest request)
        {
            var user = await GetAuthenticatedMobileUserAsync();
            if (user == null)
                return MobileUnauthorized();

            if (IsCollectorUser(user))
                return Unauthorized(new { ok = false, message = "ترحيل الفواتير غير متاح للموزع." });

            if (request == null || request.InvoiceId <= 0 || request.CustomerId <= 0)
                return BadRequest(new { ok = false, message = "بيانات الترحيل غير مكتملة." });

            var mobileCreatedBy = $"MOBILE:{user.UserName}";
            var invoice = await _db.SalesInvoices
                .FirstOrDefaultAsync(x => x.SIId == request.InvoiceId);

            if (invoice == null)
                return NotFound(new { ok = false, message = "الفاتورة غير موجودة." });

            if (invoice.CustomerId != request.CustomerId)
                return BadRequest(new { ok = false, message = "هذه الفاتورة لا تخص هذه الصيدلية." });

            if (!string.Equals(invoice.CreatedBy, mobileCreatedBy, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { ok = false, message = "غير مسموح بترحيل هذه الفاتورة من تطبيق الصيدلي." });

            if (invoice.IsPosted)
                return Json(new { ok = true, message = "الفاتورة مُرحلة بالفعل.", alreadyPosted = true });

            try
            {
                var postedBy = string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName : user.DisplayName;
                await _ledgerPostingService.PostSalesInvoiceAsync(invoice.SIId, $"MOBILE:{postedBy}");
            }
            catch (Exception ex)
            {
                return BadRequest(new { ok = false, message = $"فشل الترحيل: {ex.Message}" });
            }

            var fresh = await _db.SalesInvoices
                .AsNoTracking()
                .Where(x => x.SIId == request.InvoiceId)
                .Select(x => new { x.SIId, x.IsPosted, x.Status, x.PostedAt, x.PostedBy })
                .FirstOrDefaultAsync();

            return Json(new { ok = true, message = "تم ترحيل الفاتورة بنجاح.", invoice = fresh });
        }

        private static string CreateSessionToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
        }

        private static string NormalizePortalRole(string? portalRole)
        {
            if (string.Equals(portalRole, "Collector", StringComparison.OrdinalIgnoreCase) || string.Equals(portalRole, "موزع", StringComparison.OrdinalIgnoreCase))
                return "Collector";

            if (string.Equals(portalRole, "Pharmacist", StringComparison.OrdinalIgnoreCase) || string.Equals(portalRole, "صيدلي", StringComparison.OrdinalIgnoreCase))
                return "Pharmacist";

            return "Employee";
        }

        private static bool IsCollectorUser(User user)
            => NormalizePortalRole(user.PortalRole) == "Collector";

        private async Task<bool> CanCollectorAccessCustomerAsync(int userId, int customerId)
        {
            if (userId <= 0 || customerId <= 0)
                return false;

            return await _db.CustomerCollectors
                .AsNoTracking()
                .AnyAsync(x => x.UserId == userId && x.CustomerId == customerId);
        }

        private async Task<decimal> GetCustomerCurrentBalanceAsync(int customerId)
        {
            return await _db.LedgerEntries
                .AsNoTracking()
                .Where(e => e.CustomerId == customerId)
                .SumAsync(e => (decimal?)(e.Debit - e.Credit)) ?? 0m;
        }

        private static string SourceTypeDisplayAr(LedgerSourceType t)
        {
            return t switch
            {
                LedgerSourceType.Opening => "رصيد افتتاحي",
                LedgerSourceType.SalesInvoice => "فاتورة مبيعات",
                LedgerSourceType.SalesReturn => "مرتجع بيع",
                LedgerSourceType.PurchaseInvoice => "فاتورة مشتريات",
                LedgerSourceType.PurchaseReturn => "مرتجع شراء",
                LedgerSourceType.Receipt => "إذن استلام",
                LedgerSourceType.Payment => "إذن دفع",
                LedgerSourceType.Journal => "قيد يومية",
                LedgerSourceType.Adjustment => "تسوية",
                LedgerSourceType.StockTransfer => "تحويل مخزني",
                LedgerSourceType.StockAdjustment => "تسوية جرد",
                LedgerSourceType.DebitNote => "إشعار خصم",
                LedgerSourceType.CreditNote => "إشعار إضافة",
                _ => t.ToString()
            };
        }

        private async Task<User?> GetAuthenticatedMobileUserAsync()
        {
            if (!Request.Cookies.TryGetValue(MobileCookieName, out var token) || string.IsNullOrWhiteSpace(token))
                return null;

            if (!_mobileSessions.TryGetValue(token, out var ticket))
                return null;

            if (ticket.ExpiresAtUtc <= DateTime.UtcNow)
            {
                _mobileSessions.TryRemove(token, out _);
                Response.Cookies.Delete(MobileCookieName);
                return null;
            }

            ticket.ExpiresAtUtc = DateTime.UtcNow.Add(MobileSessionTtl);
            _mobileSessions[token] = ticket;

            return await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserId == ticket.UserId && u.IsActive);
        }

        private IActionResult MobileUnauthorized()
            => Unauthorized(new { ok = false, authRequired = true, message = "سجّل الدخول لتطبيق الصيدلي أولاً." });

        private sealed class MobileSessionTicket
        {
            public int UserId { get; set; }
            public DateTime ExpiresAtUtc { get; set; }
        }

        public sealed class AppLoginRequest
        {
            public string UserName { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }

        public sealed class CreateInvoiceRequest
        {
            public int WarehouseId { get; set; }
            public int CustomerId { get; set; }
            public List<CreateInvoiceItem> Items { get; set; } = new();
        }

        public sealed class PostInvoiceRequest
        {
            public int InvoiceId { get; set; }
            public int CustomerId { get; set; }
        }

        public sealed class CreateInvoiceItem
        {
            public int ProdId { get; set; }
            public int Qty { get; set; }
            public string? BatchNo { get; set; }
            public DateTime? Expiry { get; set; }
        }

        public sealed class CollectorCreateReceiptRequest
        {
            public int CustomerId { get; set; }
            public decimal Amount { get; set; }
            public string? Notes { get; set; }
        }
    }
}
