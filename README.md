# نظام ERP - توثيق شامل

## 📋 نظرة عامة

نظام **ERP** هو نظام تخطيط موارد المؤسسة متكامل مبني خصيصًا لشركات توزيع الأدوية. يوفر النظام إدارة كاملة لعمليات الشركة من المبيعات والمشتريات إلى إدارة المخزون والحسابات.

### المميزات الرئيسية

- ✅ **إدارة المبيعات والمشتريات**: فواتير البيع والشراء مع دعم كامل للخصومات والضرائب
- ✅ **إدارة المخزون**: دفتر حركة مخزني متقدم مع نظام FIFO/FEFO
- ✅ **إدارة الحسابات**: دفتر الأستاذ العام، الخزنة، القيود المحاسبية
- ✅ **نظام الصلاحيات**: إدارة مستخدمين وأدوار وصلاحيات متقدمة
- ✅ **التقارير**: تقارير شاملة للمبيعات والمشتريات والمخزون
- ✅ **واجهة عربية كاملة**: تصميم RTL مع دعم كامل للغة العربية

---

## 🛠️ التقنيات المستخدمة

### Backend
- **ASP.NET Core 8.0** - إطار العمل الأساسي
- **Entity Framework Core 9.0** - ORM لإدارة قاعدة البيانات
- **SQL Server** - قاعدة البيانات الرئيسية
- **C#** - لغة البرمجة

### Frontend
- **Bootstrap 5** - إطار عمل CSS
- **jQuery** - مكتبة JavaScript
- **Razor Views** - محرك القوالب

### المكتبات الإضافية
- **ClosedXML** - للتعامل مع ملفات Excel
- **EPPlus** - معالجة ملفات Excel
- **Microsoft.AspNetCore.Authentication** - نظام التوثيق

---

## 📁 بنية المشروع

```
ERP/
├── Controllers/          # وحدات التحكم (Controllers)
│   ├── SalesInvoicesController.cs
│   ├── PurchaseInvoicesController.cs
│   ├── ProductsController.cs
│   ├── CustomersController.cs
│   └── ...
├── Models/              # نماذج البيانات (Data Models)
│   ├── SalesInvoice.cs
│   ├── PurchaseInvoice.cs
│   ├── Product.cs
│   ├── Customer.cs
│   └── ...
├── Views/               # واجهات المستخدم (Razor Views)
│   ├── SalesInvoices/
│   ├── PurchaseInvoices/
│   ├── Products/
│   └── ...
├── Data/                # طبقة البيانات
│   ├── AppDbContext.cs
│   └── Seed/           # بيانات أولية
├── Services/           # الخدمات (Business Logic)
│   ├── DocumentTotalsService.cs
│   ├── LedgerPostingService.cs
│   └── StockAnalysisService.cs
├── Infrastructure/     # البنية التحتية
│   ├── PagedResult.cs
│   ├── QueryableExtensions.SearchSort.cs
│   └── UserActivityLogger.cs
├── Security/           # نظام الأمان
│   └── PermissionCodes.cs
├── Migrations/         # هجرات قاعدة البيانات
├── wwwroot/           # الملفات الثابتة
│   ├── css/
│   ├── js/
│   └── lib/
└── Program.cs         # نقطة البداية
```

---

## 🗄️ قاعدة البيانات

### الجداول الرئيسية

#### المبيعات (Sales)
- `SalesInvoices` - فواتير البيع
- `SalesInvoiceLines` - سطور فواتير البيع
- `SalesOrders` - أوامر البيع
- `SOLines` - سطور أوامر البيع
- `SalesReturns` - مرتجعات البيع
- `SalesReturnLines` - سطور مرتجعات البيع

#### المشتريات (Purchasing)
- `PurchaseInvoices` - فواتير الشراء
- `PILines` - سطور فواتير الشراء
- `PurchaseRequests` - طلبات الشراء
- `PRLines` - سطور طلبات الشراء
- `PurchaseReturns` - مرتجعات الشراء
- `PurchaseReturnLines` - سطور مرتجعات الشراء

#### المخزون (Stock)
- `StockLedger` - دفتر الحركة المخزني (مصدر الحقيقة)
- `StockFifoMap` - ربط المخارج بالمداخل (FIFO)
- `StockBatches` - رصيد سريع للتشغيلات
- `Batches` - ماستر التشغيلات
- `StockAdjustments` - تسويات المخزون
- `StockAdjustmentLines` - سطور التسويات
- `StockTransfers` - تحويلات المخزون
- `StockTransferLines` - سطور التحويلات

#### الماستر داتا (Master Data)
- `Products` - الأصناف
- `Customers` - العملاء/الموردين
- `Warehouses` - المخازن
- `Branches` - الفروع
- `Categories` - الفئات
- `ProductGroups` - مجموعات الأصناف
- `ProductBonusGroups` - مجموعات البونص
- `Accounts` - شجرة الحسابات

#### الحسابات (Accounting)
- `LedgerEntries` - دفتر الأستاذ العام
- `CashReceipts` - إيصالات القبض
- `CashPayments` - إيصالات الصرف
- `DebitNotes` - إشعارات الخصم
- `CreditNotes` - إشعارات الإضافة

#### الأمان (Security)
- `Users` - المستخدمين
- `Roles` - الأدوار
- `Permissions` - الصلاحيات
- `UserRoles` - ربط المستخدمين بالأدوار
- `RolePermissions` - ربط الأدوار بالصلاحيات
- `UserDeniedPermissions` - استثناءات الصلاحيات
- `UserExtraPermissions` - صلاحيات إضافية
- `UserActivityLogs` - سجل نشاط المستخدمين

#### الإعدادات والسياسات
- `Policies` - السياسات العامة
- `WarehousePolicyRules` - قواعد السياسة للمخازن
- `ProductGroupPolicies` - سياسات مجموعات الأصناف
- `DocumentSeries` - ترقيم المستندات

#### المناطق الجغرافية
- `Governorates` - المحافظات
- `Districts` - الأحياء/المراكز
- `Cities` - المدن
- `Areas` - المناطق

---

## 🔐 نظام الصلاحيات

النظام يستخدم نظام صلاحيات متقدم يعتمد على:

### المكونات الأساسية

1. **المستخدمين (Users)**: المستخدمون الذين يدخلون النظام
2. **الأدوار (Roles)**: مجموعات من الصلاحيات (مثل: مدير، محاسب، بائع)
3. **الصلاحيات (Permissions)**: الصلاحيات الفردية (مثل: `Sales.Invoices.View`, `Products.Edit`)

### آلية العمل

- المستخدم يمكن أن يكون له **أدوار متعددة**
- كل دور له **صلاحيات محددة**
- يمكن **إضافة صلاحيات إضافية** لمستخدم معين
- يمكن **حرمان مستخدم** من صلاحية معينة حتى لو كانت في أدواره

### أمثلة على الصلاحيات

```csharp
// المبيعات
Sales.Invoices.View      // عرض فواتير المبيعات
Sales.Invoices.Create    // إنشاء فاتورة جديدة
Sales.Invoices.Edit      // تعديل فاتورة
Sales.Invoices.Post      // ترحيل فاتورة
Sales.Invoices.Print     // طباعة فاتورة

// الأصناف
Products.View            // عرض الأصناف
Products.Create          // إضافة صنف جديد
Products.Edit            // تعديل صنف
Products.Delete          // حذف صنف
```

---

## 🔧 الخدمات (Services)

### DocumentTotalsService
**الوظيفة**: إعادة حساب إجماليات المستندات (الفواتير)

**الاستخدام**:
- تحديث إجماليات فواتير الشراء والبيع
- حساب الخصومات والضرائب
- تحديث إجماليات الهيدر بعد تعديل السطور

### LedgerPostingService
**الوظيفة**: الترحيل المحاسبي للمستندات

**الاستخدام**:
- ترحيل فواتير الشراء والبيع
- إنشاء قيود في دفتر الأستاذ
- تحديث أرصدة الحسابات
- إدارة مراحل الترحيل (مرحلة 1، مرحلة 2، ...)

### StockAnalysisService
**الوظيفة**: تحليل المخزون وحساب المؤشرات

**الاستخدام**:
- حساب الخصم المرجّح (Weighted Discount)
- حساب المخزون الحالي
- تحليل حركة الأصناف
- حساب تكلفة المخزون

---

## 📊 الموديولات الرئيسية

### 1. المبيعات (Sales)

#### فواتير البيع (Sales Invoices)
- إنشاء فواتير بيع جديدة
- إضافة سطور للفاتورة
- حساب الخصومات والضرائب تلقائيًا
- ترحيل الفواتير للمخزون والحسابات
- طباعة الفواتير

#### أوامر البيع (Sales Orders)
- إنشاء أوامر بيع
- تحويل الأوامر إلى فواتير
- متابعة حالة الأوامر

#### مرتجعات البيع (Sales Returns)
- تسجيل مرتجعات البيع
- ربط المرتجعات بالفواتير الأصلية
- تحديث المخزون تلقائيًا

### 2. المشتريات (Purchasing)

#### فواتير الشراء (Purchase Invoices)
- إنشاء فواتير شراء جديدة
- إدارة التشغيلات (Batches) مع تواريخ الصلاحية
- حساب تكلفة الوحدة والخصومات
- ترحيل الفواتير للمخزون والحسابات
- نظام التنقل السريع بين الفواتير (أول/سابق/التالي/آخر)

#### طلبات الشراء (Purchase Requests)
- إنشاء طلبات شراء
- تحويل الطلبات إلى فواتير
- متابعة حالة الطلبات

#### مرتجعات الشراء (Purchase Returns)
- تسجيل مرتجعات الشراء
- تحديث المخزون والحسابات

### 3. المخزون (Stock Management)

#### دفتر الحركة (Stock Ledger)
- **مصدر الحقيقة** لجميع حركات المخزون
- تسجيل كل حركة دخول/خروج
- ربط الحركات بالمستندات المصدرية
- دعم نظام FIFO/FEFO

#### التشغيلات (Batches)
- إدارة التشغيلات مع تواريخ الصلاحية
- تتبع سعر الجمهور لكل تشغيلة
- حساب تكلفة الوحدة الافتراضية

#### رصيد سريع (StockBatches)
- جدول Materialized View للرصيد السريع
- صف واحد لكل (مخزن + صنف + تشغيلة + صلاحية)
- تحديث تلقائي مع كل حركة

#### تسويات المخزون (Stock Adjustments)
- تسجيل جرد المخزون
- تسوية الفروقات بين الرصيد الفعلي والنظري
- تحديث دفتر الحركة تلقائيًا

#### تحويلات المخزون (Stock Transfers)
- تحويل كميات بين المخازن
- تتبع تكلفة التحويل
- تحديث الرصيد في كلا المخزنين

### 4. الحسابات (Accounting)

#### دفتر الأستاذ العام (General Ledger)
- تسجيل جميع القيود المحاسبية
- ربط القيود بالمستندات المصدرية
- كشف حساب لكل حساب
- ميزان مراجعة

#### الخزنة (Cash Management)
- إيصالات القبض النقدي
- إيصالات الصرف النقدي
- ربط بالحسابات المالية

#### إشعارات الدائن والمدين
- إشعارات الخصم (Debit Notes)
- إشعارات الإضافة (Credit Notes)
- ربط بالعملاء والموردين

### 5. الماستر داتا (Master Data)

#### الأصناف (Products)
- إدارة كاملة للأصناف
- ربط بالفئات والمجموعات
- تتبع سعر الجمهور
- سجل تغييرات الأسعار

#### العملاء (Customers)
- إدارة بيانات العملاء/الموردين
- ربط بالمناطق الجغرافية
- تتبع الحد الائتماني والرصيد
- سياسات التسعير

#### المخازن (Warehouses)
- إدارة المخازن والفروع
- ربط بالسياسات
- قواعد السياسة لكل مخزن

---

## 🎨 الواجهات والتصميم

### نظام التابات (Tabs System)
- جميع الشاشات الرئيسية تفتح داخل تابات
- إعادة استخدام التابات المفتوحة
- تحديث محتوى التاب بدون إعادة تحميل

### مبدأ Shell + Body
- **Shell ثابت**: شريط الأزرار والتنقل
- **Body متغير**: المحتوى الذي يتغير
- تحديث Body فقط بدون إعادة تحميل كامل

### التنقل السريع
- أزرار التنقل (أول/سابق/التالي/آخر)
- البحث السريع داخل المستندات
- تحديث فوري بدون فتح تابات جديدة

### الطباعة
- تصميم طباعة احترافي للفواتير
- إخفاء عناصر التعديل في الطباعة
- دعم طباعة A4

---

## ⚡ الأداء والتحسينات

### التحسينات المطبقة

1. **Connection Pooling**
   - Max Pool Size: 200
   - Min Pool Size: 10
   - Connection Timeout: 30 ثانية

2. **فهارس قاعدة البيانات**
   - فهارس على الجداول المهمة
   - فهارس مركبة للاستعلامات الشائعة

3. **StockBatch كـ Materialized View**
   - رصيد سريع بدون تجميع ملايين السطور
   - تحديث تلقائي مع كل حركة

4. **Pagination**
   - استخدام `PagedResult` للقوائم
   - منع تحميل كل البيانات في الذاكرة

### توصيات مستقبلية

1. **Caching**
   - إضافة Memory Cache للبيانات الثابتة
   - Cache للأصناف والعملاء والمخازن

2. **Query Optimization**
   - استخدام Compiled Queries للاستعلامات المتكررة
   - تحسين استعلامات JOIN

3. **Read Replicas**
   - قاعدة بيانات رئيسية للكتابة
   - Replicas للقراءة (التقارير)

---

## 🚀 التثبيت والتشغيل

### المتطلبات

- .NET 8.0 SDK
- SQL Server 2019 أو أحدث
- Visual Studio 2022 أو Visual Studio Code

### خطوات التثبيت

1. **استنساخ المشروع**
   ```bash
   git clone <repository-url>
   cd ERP
   ```

2. **تحديث Connection String**
   - افتح `appsettings.json`
   - حدّث `ConnectionStrings:conString` بقاعدة البيانات الخاصة بك

3. **تطبيق Migrations**
   ```bash
   dotnet ef database update
   ```
   أو عند تشغيل التطبيق لأول مرة، سيتم تطبيق Migrations تلقائيًا

4. **تشغيل التطبيق**
   ```bash
   dotnet run
   ```

5. **الوصول للتطبيق**
   - افتح المتصفح على: `https://localhost:5001` أو `http://localhost:5000`

### البيانات الأولية (Seed Data)

عند أول تشغيل، يتم إنشاء:
- ✅ الصلاحيات الأساسية
- ✅ الأدوار الأساسية (Admin, User)
- ✅ ربط الأدوار بالصلاحيات
- ✅ شجرة الحسابات الأساسية
- ✅ مجموعات الأصناف (A-Z)
- ✅ مجموعات البونص
- ✅ السياسات الأساسية

---

## 👥 المستخدمون الافتراضيون

بعد التثبيت، يمكنك إنشاء مستخدمين من خلال:
- شاشة **المستخدمين** (`/Users`)
- أو من خلال قاعدة البيانات مباشرة

> **ملاحظة**: تأكد من تشفير كلمة المرور قبل إضافتها في قاعدة البيانات

---

## 📝 دليل المطور

### إضافة موديول جديد

1. **إنشاء Model**
   ```csharp
   public class MyModel
   {
       public int Id { get; set; }
       public string Name { get; set; }
   }
   ```

2. **إضافة DbSet في AppDbContext**
   ```csharp
   public DbSet<MyModel> MyModels { get; set; }
   ```

3. **إنشاء Migration**
   ```bash
   dotnet ef migrations add AddMyModel
   dotnet ef database update
   ```

4. **إنشاء Controller**
   ```csharp
   public class MyModelsController : Controller
   {
       // Actions
   }
   ```

5. **إنشاء Views**
   - `Views/MyModels/Index.cshtml`
   - `Views/MyModels/Create.cshtml`
   - `Views/MyModels/Edit.cshtml`
   - إلخ...

### إضافة صلاحية جديدة

1. **إضافة في PermissionCodes.cs**
   ```csharp
   public static class MyModule
   {
       public const string View = "MyModule.View";
       public const string Create = "MyModule.Create";
   }
   ```

2. **إضافة في GetAll()**
   ```csharp
   yield return (MyModule.View, "عرض الموديول", "الموديول", "وصف");
   ```

3. **تطبيق الصلاحية في Controller**
   ```csharp
   [Authorize(Policy = PermissionCodes.MyModule.View)]
   public IActionResult Index() { ... }
   ```

### إضافة خدمة جديدة

1. **إنشاء Service Class**
   ```csharp
   public class MyService
   {
       private readonly AppDbContext _context;
       
       public MyService(AppDbContext context)
       {
           _context = context;
       }
   }
   ```

2. **تسجيل في Program.cs**
   ```csharp
   builder.Services.AddScoped<MyService>();
   ```

3. **استخدام في Controller**
   ```csharp
   private readonly MyService _myService;
   
   public MyController(MyService myService)
   {
       _myService = myService;
   }
   ```

---

## 🔍 البحث والاستعلامات

### استخدام PagedResult

```csharp
var query = _context.Products.AsQueryable();

// تطبيق البحث
if (!string.IsNullOrEmpty(search))
{
    query = query.Where(p => p.ProdName.Contains(search));
}

// تطبيق الترتيب
query = query.OrderBy(p => p.ProdName);

// Pagination
var result = await PagedResult<Product>.CreateAsync(
    query, 
    pageNumber, 
    pageSize
);

return View(result);
```

### استخدام QueryableExtensions

```csharp
var query = _context.SalesInvoices
    .Search(search)  // بحث تلقائي
    .Sort(sortBy, sortDir)  // ترتيب تلقائي
    .FromDate(fromDate)
    .ToDate(toDate);
```

---

## 🐛 استكشاف الأخطاء

### مشاكل شائعة

1. **خطأ في الاتصال بقاعدة البيانات**
   - تحقق من Connection String في `appsettings.json`
   - تأكد من تشغيل SQL Server
   - تحقق من صلاحيات المستخدم

2. **خطأ في Migrations**
   ```bash
   dotnet ef migrations remove
   dotnet ef migrations add InitialCreate
   dotnet ef database update
   ```

3. **مشاكل في الصلاحيات**
   - تحقق من تسجيل الصلاحيات في `PermissionSeeder`
   - تحقق من ربط الأدوار بالصلاحيات في `RolePermissionSeeder`

---

## 📚 المراجع والموارد

### الوثائق الرسمية
- [ASP.NET Core Documentation](https://docs.microsoft.com/aspnet/core)
- [Entity Framework Core](https://docs.microsoft.com/ef/core)
- [Bootstrap Documentation](https://getbootstrap.com/docs)

### ملفات التوثيق الداخلية
- `project_state_2025-12-22_tabs_UPDATED_v15_SALES_DONE (1).md` - حالة المشروع التفصيلية
- `PERFORMANCE_ANALYSIS.md` - تحليل الأداء
- `PurchaseInvoice_Full_Reference_UPDATED_2026-01-08.md` - مرجع فاتورة المشتريات

---

## 📞 الدعم والمساهمة

للمساهمة في المشروع:
1. Fork المشروع
2. إنشاء Branch للميزة الجديدة
3. Commit التغييرات
4. Push إلى Branch
5. فتح Pull Request

---

## 📄 الترخيص

هذا المشروع مملوك للشركة وليس مفتوح المصدر.

---

## 🎯 خارطة الطريق المستقبلية

### المرحلة القادمة
- [ ] تحسينات الأداء (Caching, Query Optimization)
- [ ] تقارير متقدمة (Excel Export)
- [ ] واجهة API للتكامل مع أنظمة أخرى
- [ ] تطبيق موبايل (اختياري)

### التحسينات المقترحة
- [ ] نظام الإشعارات
- [ ] لوحة تحكم (Dashboard)
- [ ] تقارير تحليلية متقدمة
- [ ] دعم متعدد اللغات

---

**آخر تحديث**: يناير 2025

**الإصدار**: 1.0.0
