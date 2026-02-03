image.png# دليل المطور - نظام ERP

## 🚀 البدء السريع

### إعداد البيئة

1. **تثبيت .NET 8.0 SDK**
   ```bash
   dotnet --version  # يجب أن يكون 8.0 أو أحدث
   ```

2. **استنساخ المشروع**
   ```bash
   git clone <repository-url>
   cd ERP
   ```

3. **استعادة الحزم**
   ```bash
   dotnet restore
   ```

4. **تحديث Connection String**
   - افتح `appsettings.json`
   - حدّث `ConnectionStrings:conString`

5. **تشغيل المشروع**
   ```bash
   dotnet run
   ```

---

## 📝 إضافة موديول جديد

### الخطوة 1: إنشاء Model

```csharp
// Models/MyModel.cs
using System.ComponentModel.DataAnnotations;

namespace ERP.Models
{
    public class MyModel
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        [StringLength(200)]
        public string Name { get; set; } = string.Empty;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
```

### الخطوة 2: إضافة DbSet في AppDbContext

```csharp
// Data/AppDbContext.cs
public DbSet<MyModel> MyModels { get; set; } = null!;
```

### الخطوة 3: تكوين Fluent API (اختياري)

```csharp
// في OnModelCreating
mb.Entity<MyModel>(entity =>
{
    entity.ToTable("MyModels");
    entity.HasKey(x => x.Id);
    entity.Property(x => x.Name)
          .IsRequired()
          .HasMaxLength(200);
});
```

### الخطوة 4: إنشاء Migration

```bash
dotnet ef migrations add AddMyModel
dotnet ef database update
```

### الخطوة 5: إنشاء Controller

```csharp
// Controllers/MyModelsController.cs
[Authorize]
public class MyModelsController : Controller
{
    private readonly AppDbContext _context;
    
    public MyModelsController(AppDbContext context)
    {
        _context = context;
    }
    
    public async Task<IActionResult> Index()
    {
        var models = await _context.MyModels.ToListAsync();
        return View(models);
    }
    
    [HttpGet]
    public IActionResult Create()
    {
        return View();
    }
    
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(MyModel model)
    {
        if (ModelState.IsValid)
        {
            _context.MyModels.Add(model);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        return View(model);
    }
}
```

### الخطوة 6: إنشاء Views

```
Views/MyModels/
├── Index.cshtml
├── Create.cshtml
├── Edit.cshtml
└── Details.cshtml
```

---

## 🔐 إضافة صلاحيات جديدة

### الخطوة 1: إضافة في PermissionCodes.cs

```csharp
// Security/PermissionCodes.cs
public static class MyModule
{
    public const string View = "MyModule.View";
    public const string Create = "MyModule.Create";
    public const string Edit = "MyModule.Edit";
    public const string Delete = "MyModule.Delete";
}
```

### الخطوة 2: إضافة في GetAll()

```csharp
// في PermissionCodes.GetAll()
yield return (MyModule.View, "عرض الموديول", "الموديول", "وصف الصلاحية");
yield return (MyModule.Create, "إنشاء جديد", "الموديول", "وصف");
yield return (MyModule.Edit, "تعديل", "الموديول", "وصف");
yield return (MyModule.Delete, "حذف", "الموديول", "وصف");
```

### الخطوة 3: تطبيق في Controller

```csharp
[Authorize(Policy = PermissionCodes.MyModule.View)]
public async Task<IActionResult> Index()
{
    // ...
}

[Authorize(Policy = PermissionCodes.MyModule.Create)]
public IActionResult Create()
{
    // ...
}
```

### الخطوة 4: التحقق في View

```csharp
@if (User.HasPermission(PermissionCodes.MyModule.Create))
{
    <a href="@Url.Action("Create")" class="btn btn-primary">إضافة جديد</a>
}
```

---

## 🔧 إضافة خدمة جديدة

### الخطوة 1: إنشاء Service Class

```csharp
// Services/MyService.cs
public interface IMyService
{
    Task<decimal> CalculateTotalAsync(int id);
}

public class MyService : IMyService
{
    private readonly AppDbContext _context;
    
    public MyService(AppDbContext context)
    {
        _context = context;
    }
    
    public async Task<decimal> CalculateTotalAsync(int id)
    {
        // Logic
        return 0m;
    }
}
```

### الخطوة 2: تسجيل في Program.cs

```csharp
// Program.cs
builder.Services.AddScoped<IMyService, MyService>();
```

### الخطوة 3: استخدام في Controller

```csharp
public class MyController : Controller
{
    private readonly IMyService _myService;
    
    public MyController(IMyService myService)
    {
        _myService = myService;
    }
    
    public async Task<IActionResult> Index()
    {
        var total = await _myService.CalculateTotalAsync(1);
        return View();
    }
}
```

---

## 📊 استخدام PagedResult

```csharp
public async Task<IActionResult> Index(int page = 1, string search = "")
{
    var query = _context.Products.AsQueryable();
    
    // البحث
    if (!string.IsNullOrEmpty(search))
    {
        query = query.Where(p => p.ProdName.Contains(search));
    }
    
    // الترتيب
    query = query.OrderBy(p => p.ProdName);
    
    // Pagination
    var result = await PagedResult<Product>.CreateAsync(
        query, 
        page, 
        pageSize: 20
    );
    
    return View(result);
}
```

**في View**:
```csharp
@model PagedResult<Product>

@foreach (var item in Model.Items)
{
    <div>@item.ProdName</div>
}

<!-- Pagination -->
<nav>
    @if (Model.HasPreviousPage)
    {
        <a href="?page=@(Model.PageNumber - 1)">السابق</a>
    }
    
    <span>صفحة @Model.PageNumber من @Model.TotalPages</span>
    
    @if (Model.HasNextPage)
    {
        <a href="?page=@(Model.PageNumber + 1)">التالي</a>
    }
</nav>
```

---

## 🔍 استخدام QueryableExtensions

```csharp
var query = _context.SalesInvoices
    .Search(search)                    // بحث تلقائي
    .Sort(sortBy, sortDir)            // ترتيب
    .FromDate(fromDate)               // من تاريخ
    .ToDate(toDate)                   // إلى تاريخ
    .Where(s => s.Status == "مرحل");  // فلاتر إضافية
```

---

## 📝 إضافة AJAX Endpoint

### في Controller

```csharp
[HttpPost]
[IgnoreAntiforgeryToken]  // أو إرسال Token في Header
public async Task<IActionResult> GetDataJson(int id)
{
    try
    {
        var data = await _context.MyModels
            .Where(m => m.Id == id)
            .FirstOrDefaultAsync();
            
        if (data == null)
            return NotFound(new { ok = false, message = "غير موجود" });
            
        return Json(new { ok = true, data = data });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error getting data");
        return BadRequest(new { ok = false, message = "حدث خطأ" });
    }
}
```

### في JavaScript

```javascript
async function loadData(id) {
    try {
        const response = await fetch(`/MyModels/GetDataJson?id=${id}`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            }
        });
        
        const result = await response.json();
        
        if (result.ok) {
            // استخدام result.data
            console.log(result.data);
        } else {
            alert(result.message);
        }
    } catch (error) {
        console.error('Error:', error);
        alert('حدث خطأ في الاتصال');
    }
}
```

---

## 🗄️ إضافة جدول جديد

### 1. إنشاء Model

```csharp
public class MyTable
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;
    
    public int? ForeignKeyId { get; set; }
    
    [ForeignKey(nameof(ForeignKeyId))]
    public virtual RelatedTable? RelatedTable { get; set; }
}
```

### 2. إضافة DbSet

```csharp
public DbSet<MyTable> MyTables { get; set; } = null!;
```

### 3. تكوين العلاقات

```csharp
mb.Entity<MyTable>(entity =>
{
    entity.HasKey(x => x.Id);
    
    // One-to-Many
    entity.HasOne(x => x.RelatedTable)
          .WithMany()
          .HasForeignKey(x => x.ForeignKeyId)
          .OnDelete(DeleteBehavior.Restrict);
    
    // Index
    entity.HasIndex(x => x.Name);
    
    // Unique Constraint
    entity.HasIndex(x => x.Name)
          .IsUnique();
});
```

### 4. Migration

```bash
dotnet ef migrations add AddMyTable
dotnet ef database update
```

---

## 🔄 استخدام Transactions

```csharp
using var transaction = await _context.Database.BeginTransactionAsync();
try
{
    // عملية 1
    var invoice = new SalesInvoice { /* ... */ };
    _context.SalesInvoices.Add(invoice);
    await _context.SaveChangesAsync();
    
    // عملية 2
    var line = new SalesInvoiceLine { /* ... */ };
    _context.SalesInvoiceLines.Add(line);
    await _context.SaveChangesAsync();
    
    // عملية 3
    await _totalsService.RecalculateTotalsAsync(invoice.SIId);
    
    await transaction.CommitAsync();
}
catch (Exception ex)
{
    await transaction.RollbackAsync();
    _logger.LogError(ex, "Transaction failed");
    throw;
}
```

---

## 📋 إضافة Seeder

```csharp
// Data/Seed/MyDataSeeder.cs
public static class MyDataSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        if (await db.MyModels.AnyAsync())
            return; // البيانات موجودة بالفعل
            
        var data = new List<MyModel>
        {
            new MyModel { Name = "عنصر 1" },
            new MyModel { Name = "عنصر 2" }
        };
        
        db.MyModels.AddRange(data);
        await db.SaveChangesAsync();
    }
}
```

**في Program.cs**:
```csharp
await MyDataSeeder.SeedAsync(db);
```

---

## 🎨 إضافة View Component

### 1. إنشاء Component

```csharp
// ViewComponents/MyViewComponent.cs
public class MyViewComponent : ViewComponent
{
    private readonly AppDbContext _context;
    
    public MyViewComponent(AppDbContext context)
    {
        _context = context;
    }
    
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var data = await _context.MyModels.ToListAsync();
        return View(data);
    }
}
```

### 2. إنشاء View

```
Views/Shared/Components/My/Default.cshtml
```

### 3. الاستخدام

```csharp
@await Component.InvokeAsync("My")
```

---

## 🔍 Logging

```csharp
private readonly ILogger<MyController> _logger;

public MyController(ILogger<MyController> logger)
{
    _logger = logger;
}

public async Task<IActionResult> Create(MyModel model)
{
    _logger.LogInformation("Creating new model: {Name}", model.Name);
    
    try
    {
        // Operation
        _logger.LogInformation("Model created successfully: {Id}", model.Id);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error creating model: {Name}", model.Name);
        throw;
    }
}
```

---

## ✅ Validation

### في Model

```csharp
[Required(ErrorMessage = "الاسم مطلوب")]
[StringLength(200, ErrorMessage = "الاسم لا يزيد عن 200 حرف")]
public string Name { get; set; } = string.Empty;

[Range(0, 100, ErrorMessage = "القيمة بين 0 و 100")]
public decimal Price { get; set; }

[EmailAddress(ErrorMessage = "البريد الإلكتروني غير صحيح")]
public string Email { get; set; }
```

### في Controller

```csharp
[HttpPost]
public async Task<IActionResult> Create(MyModel model)
{
    if (!ModelState.IsValid)
    {
        return View(model);
    }
    
    // Save
}
```

### في View

```csharp
@if (!ViewData.ModelState.IsValid)
{
    <div class="alert alert-danger">
        @foreach (var error in ViewData.ModelState.Values.SelectMany(v => v.Errors))
        {
            <div>@error.ErrorMessage</div>
        }
    </div>
}

@Html.ValidationMessageFor(m => m.Name, "", new { @class = "text-danger" })
```

---

## 🧪 Testing (مستقبلي)

### Unit Test Example

```csharp
[Fact]
public async Task CalculateTotal_ShouldReturnCorrectValue()
{
    // Arrange
    var service = new MyService(_mockContext);
    
    // Act
    var result = await service.CalculateTotalAsync(1);
    
    // Assert
    Assert.Equal(100m, result);
}
```

---

## 📚 Resources

### مفيد للقراءة
- [ASP.NET Core Documentation](https://docs.microsoft.com/aspnet/core)
- [Entity Framework Core](https://docs.microsoft.com/ef/core)
- [C# Coding Conventions](https://docs.microsoft.com/dotnet/csharp/fundamentals/coding-style/coding-conventions)

### ملفات المشروع المهمة
- `Program.cs` - إعدادات التطبيق
- `AppDbContext.cs` - تكوين قاعدة البيانات
- `PermissionCodes.cs` - أكواد الصلاحيات

---

## ❓ FAQ

### كيف أضيف حقل جديد لجدول موجود؟
1. أضف الخاصية في Model
2. أنشئ Migration: `dotnet ef migrations add AddFieldToTable`
3. طبق Migration: `dotnet ef database update`

### كيف أغير اسم جدول؟
1. في `OnModelCreating`: `entity.ToTable("NewName")`
2. أنشئ Migration

### كيف أضيف علاقة Many-to-Many؟
استخدم جدول وسيط (Junction Table):
```csharp
public class UserRole
{
    public int UserId { get; set; }
    public int RoleId { get; set; }
    public virtual User User { get; set; }
    public virtual Role Role { get; set; }
}
```

---

**آخر تحديث**: يناير 2025
