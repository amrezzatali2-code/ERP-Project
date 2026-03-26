# البنية التقنية - نظام ERP

## 📐 نظرة عامة على البنية

نظام ERP مبني على **معمارية MVC (Model-View-Controller)** باستخدام ASP.NET Core، مع فصل واضح بين طبقات العرض والمنطق والبيانات.

---

## 🏗️ الطبقات المعمارية

### 1. طبقة العرض (Presentation Layer)

#### Controllers
- **الموقع**: `Controllers/`
- **الوظيفة**: معالجة طلبات HTTP وإرجاع الردود المناسبة
- **المسؤوليات**:
  - التحقق من الصلاحيات
  - استدعاء الخدمات (Services)
  - إرجاع Views أو JSON
  - معالجة الأخطاء

**مثال**:
```csharp
public class SalesInvoicesController : Controller
{
    private readonly AppDbContext _context;
    private readonly DocumentTotalsService _totalsService;
    
    [Authorize(Policy = PermissionCodes.SalesInvoices.View)]
    public async Task<IActionResult> Index(int page = 1, string search = "")
    {
        // Logic
    }
}
```

#### Views
- **الموقع**: `Views/`
- **التقنية**: Razor Views (.cshtml)
- **المسؤوليات**:
  - عرض البيانات للمستخدم
  - جمع المدخلات من المستخدم
  - التفاعل مع JavaScript

**الهيكل**:
```
Views/
├── Shared/          # Views مشتركة
│   ├── _Layout.cshtml
│   └── _PartialView.cshtml
├── SalesInvoices/   # Views خاصة بالمبيعات
│   ├── Index.cshtml
│   ├── Show.cshtml
│   └── Create.cshtml
└── ...
```

---

### 2. طبقة المنطق (Business Logic Layer)

#### Services
- **الموقع**: `Services/`
- **الوظيفة**: تنفيذ منطق العمل المعقد
- **المسؤوليات**:
  - حسابات معقدة
  - معالجة البيانات
  - التكامل بين الموديولات

**الخدمات الرئيسية**:

##### DocumentTotalsService
```csharp
public class DocumentTotalsService
{
    // إعادة حساب إجماليات الفاتورة
    public async Task RecalculateTotalsAsync(int invoiceId)
    {
        // حساب الإجماليات من السطور
        // تحديث الهيدر
    }
}
```

##### LedgerPostingService
```csharp
public class LedgerPostingService
{
    // ترحيل فاتورة للمحاسبة
    public async Task PostPurchaseInvoiceAsync(int invoiceId, string userName)
    {
        // إنشاء قيود محاسبية
        // تحديث أرصدة الحسابات
    }
}
```

##### StockAnalysisService
```csharp
public class StockAnalysisService
{
    // حساب الخصم المرجّح
    public async Task<decimal> GetWeightedDiscountAsync(
        string prodId, 
        int warehouseId)
    {
        // حساب من StockLedger
    }
}
```

---

### 3. طبقة البيانات (Data Access Layer)

#### AppDbContext
- **الموقع**: `Data/AppDbContext.cs`
- **الوظيفة**: نقطة الوصول الرئيسية لقاعدة البيانات
- **المسؤوليات**:
  - تعريف DbSets
  - تكوين العلاقات (Relationships)
  - Fluent API Configuration
  - تتبع تغييرات الأسعار

**مثال**:
```csharp
public class AppDbContext : DbContext
{
    public DbSet<Product> Products { get; set; }
    public DbSet<SalesInvoice> SalesInvoices { get; set; }
    
    protected override void OnModelCreating(ModelBuilder mb)
    {
        // Fluent API Configuration
    }
}
```

#### Models
- **الموقع**: `Models/`
- **الوظيفة**: تمثيل جداول قاعدة البيانات
- **المسؤوليات**:
  - تعريف الكيانات (Entities)
  - العلاقات بين الجداول
  - Validation Attributes

**مثال**:
```csharp
public class SalesInvoice
{
    public int SIId { get; set; }
    public int CustomerId { get; set; }
    public DateTime SIDate { get; set; }
    
    // Navigation Properties
    public virtual Customer Customer { get; set; }
    public virtual ICollection<SalesInvoiceLine> Lines { get; set; }
}
```

---

### 4. طبقة البنية التحتية (Infrastructure Layer)

#### PagedResult
- **الموقع**: `Infrastructure/PagedResult.cs`
- **الوظيفة**: إدارة Pagination للقوائم

**الاستخدام**:
```csharp
var result = await PagedResult<Product>.CreateAsync(
    query, 
    pageNumber: 1, 
    pageSize: 20
);
```

#### QueryableExtensions
- **الموقع**: `Infrastructure/QueryableExtensions.SearchSort.cs`
- **الوظيفة**: Extension Methods للبحث والترتيب

**الاستخدام**:
```csharp
var query = _context.Products
    .Search(searchTerm)
    .Sort("ProdName", "asc")
    .FromDate(fromDate)
    .ToDate(toDate);
```

#### UserActivityLogger
- **الموقع**: `Infrastructure/UserActivityLogger.cs`
- **الوظيفة**: تسجيل نشاط المستخدمين

**الاستخدام**:
```csharp
await _activityLogger.LogAsync(
    User.Identity.Name,
    UserActionType.Create,
    "SalesInvoice",
    invoiceId.ToString()
);
```

---

## 🔄 تدفق البيانات (Data Flow)

### سيناريو: إنشاء فاتورة بيع

```
1. المستخدم يضغط "فاتورة جديدة"
   ↓
2. Controller: SalesInvoicesController.Create()
   ↓
3. View: Views/SalesInvoices/Show.cshtml
   ↓
4. المستخدم يضيف سطور
   ↓
5. AJAX → Controller.AddLineJson()
   ↓
6. Service: DocumentTotalsService.RecalculateTotalsAsync()
   ↓
7. AppDbContext.SaveChanges()
   ↓
8. StockLedger: تسجيل حركة مخزنية
   ↓
9. Response JSON → Update UI
```

---

## 🔐 نظام الأمان

### Authentication (التوثيق)
- **النوع**: Cookie Authentication
- **الإعداد**: `Program.cs`
```csharp
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.LogoutPath = "/Login/Logout";
    });
```

### Authorization (الصلاحيات)
- **النوع**: Policy-based Authorization
- **الآلية**:
  1. المستخدم له أدوار (Roles)
  2. كل دور له صلاحيات (Permissions)
  3. يمكن إضافة/نقص صلاحيات للمستخدم مباشرة

**التحقق في Controller**:
```csharp
[Authorize(Policy = PermissionCodes.SalesInvoices.Create)]
public IActionResult Create()
{
    // Action
}
```

**التحقق في View**:
```csharp
@if (User.HasPermission(PermissionCodes.SalesInvoices.Edit))
{
    <button>تعديل</button>
}
```

---

## 📊 إدارة المخزون

### StockLedger (مصدر الحقيقة)
- **الوظيفة**: تسجيل كل حركة مخزنية
- **البنية**:
  - `QtyIn` / `QtyOut`: الكميات
  - `UnitCost`: تكلفة الوحدة
  - `RemainingQty`: الكمية المتبقية (لـ FIFO)
  - `SourceType` / `SourceId`: ربط بالمستند المصدر

### StockFifoMap (ربط FIFO)
- **الوظيفة**: ربط المخارج بالمداخل
- **الاستخدام**: حساب تكلفة الخروج بدقة

### StockBatches (رصيد سريع)
- **الوظيفة**: Materialized View للرصيد
- **الميزة**: استعلام سريع بدون تجميع

---

## 💾 قاعدة البيانات

### العلاقات (Relationships)

#### One-to-Many
```csharp
// فاتورة واحدة → سطور كثيرة
public class SalesInvoice
{
    public virtual ICollection<SalesInvoiceLine> Lines { get; set; }
}

public class SalesInvoiceLine
{
    public int SIId { get; set; }
    public virtual SalesInvoice SalesInvoice { get; set; }
}
```

#### Many-to-Many
```csharp
// مستخدمون ←→ أدوار
public class User
{
    public virtual ICollection<UserRole> UserRoles { get; set; }
}

public class Role
{
    public virtual ICollection<UserRole> UserRoles { get; set; }
}
```

### Cascade Delete
- **Cascade**: حذف السطور عند حذف الهيدر
- **Restrict**: منع حذف الحساب إذا كان له قيود
- **SetNull**: جعل FK = NULL عند الحذف

---

## 🎨 الواجهة الأمامية

### JavaScript Architecture

#### site.js
- **الوظيفة**: منطق عام للواجهة
- **المسؤوليات**:
  - نظام التابات
  - التنقل السريع
  - Event Handlers العامة

#### AJAX Pattern
```javascript
async function loadInvoiceBody(url) {
    const response = await fetch(url);
    const html = await response.text();
    document.getElementById('BodyHost').innerHTML = html;
    window.__ERP_INIT__(); // إعادة تهيئة
}
```

### CSS Architecture

#### site.css
- **الوظيفة**: تنسيقات مخصصة
- **المسؤوليات**:
  - RTL Support
  - تنسيقات الفواتير
  - الطباعة

---

## 🔄 Transactions

### استخدام Transactions
```csharp
using var transaction = await _context.Database.BeginTransactionAsync();
try
{
    // عمليات متعددة
    await _context.SaveChangesAsync();
    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync();
    throw;
}
```

### متى نستخدم Transactions؟
- ✅ إضافة/حذف سطور مع تحديث الهيدر
- ✅ ترحيل فاتورة (قيود + تحديث رصيد)
- ✅ تحويل مخزون (خروج + دخول)

---

## 📈 الأداء

### التحسينات المطبقة

1. **AsNoTracking()**
   ```csharp
   var products = await _context.Products
       .AsNoTracking()
       .ToListAsync();
   ```

2. **Pagination**
   ```csharp
   var result = await PagedResult<Product>.CreateAsync(
       query, page, pageSize
   );
   ```

3. **Indexes**
   ```csharp
   entity.HasIndex(x => new { x.CustomerId, x.SIDate });
   ```

4. **Connection Pooling**
   ```json
   "Max Pool Size=200;Min Pool Size=10"
   ```

---

## 🧪 Testing Strategy

### Unit Tests (مستقبلي)
- اختبار Services
- اختبار Calculations
- اختبار Validation

### Integration Tests (مستقبلي)
- اختبار Controllers
- اختبار Database Operations
- اختبار Authentication

---

## 📝 Best Practices

### 1. Naming Conventions
- **Controllers**: `[Module]Controller` (مثل: `SalesInvoicesController`)
- **Models**: PascalCase (مثل: `SalesInvoice`)
- **Actions**: PascalCase (مثل: `Create`, `Index`)

### 2. Error Handling
```csharp
try
{
    // Operation
}
catch (Exception ex)
{
    _logger.LogError(ex, "Error message");
    return BadRequest(new { ok = false, message = "رسالة عربية واضحة" });
}
```

### 3. Async/Await
- ✅ استخدم `async Task` في جميع Actions
- ✅ استخدم `await` مع عمليات I/O

### 4. Validation
```csharp
[Required]
[StringLength(200)]
public string ProdName { get; set; }
```

---

## 🔍 Debugging

### Logging
```csharp
_logger.LogInformation("Creating invoice {InvoiceId}", invoiceId);
_logger.LogError(ex, "Error creating invoice");
```

### SQL Profiling
- استخدام SQL Server Profiler
- مراقبة الاستعلامات البطيئة

---

**آخر تحديث**: مارس 2026
