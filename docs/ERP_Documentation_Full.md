# نظام ERP - التوثيق الشامل

**الإصدار**: 1.0.0  
**تاريخ آخر تحديث**: يناير 2025  
**اللغة**: العربية

---

# جدول المحتويات

1. [نظرة عامة](#نظرة-عامة)
2. [التقنيات المستخدمة](#التقنيات-المستخدمة)
3. [بنية المشروع](#بنية-المشروع)
4. [قاعدة البيانات](#قاعدة-البيانات)
5. [نظام الصلاحيات](#نظام-الصلاحيات)
6. [الخدمات](#الخدمات)
7. [الموديولات الرئيسية](#الموديولات-الرئيسية)
8. [الواجهات والتصميم](#الواجهات-والتصميم)
9. [الأداء والتحسينات](#الأداء-والتحسينات)
10. [التثبيت والتشغيل](#التثبيت-والتشغيل)
11. [دليل المطور](#دليل-المطور)
12. [البنية التقنية](#البنية-التقنية)
13. [مخطط قاعدة البيانات](#مخطط-قاعدة-البيانات)

---

# نظرة عامة

نظام **ERP** هو نظام تخطيط موارد المؤسسة متكامل مبني خصيصًا لشركات توزيع الأدوية. يوفر النظام إدارة كاملة لعمليات الشركة من المبيعات والمشتريات إلى إدارة المخزون والحسابات.

## المميزات الرئيسية

- ✅ **إدارة المبيعات والمشتريات**: فواتير البيع والشراء مع دعم كامل للخصومات والضرائب
- ✅ **إدارة المخزون**: دفتر حركة مخزني متقدم مع نظام FIFO/FEFO
- ✅ **إدارة الحسابات**: دفتر الأستاذ العام، الخزنة، القيود المحاسبية
- ✅ **نظام الصلاحيات**: إدارة مستخدمين وأدوار وصلاحيات متقدمة
- ✅ **التقارير**: تقارير شاملة للمبيعات والمشتريات والمخزون
- ✅ **واجهة عربية كاملة**: تصميم RTL مع دعم كامل للغة العربية

---

# التقنيات المستخدمة

## Backend

- **ASP.NET Core 8.0** - إطار العمل الأساسي
- **Entity Framework Core 9.0** - ORM لإدارة قاعدة البيانات
- **SQL Server** - قاعدة البيانات الرئيسية
- **C#** - لغة البرمجة

## Frontend

- **Bootstrap 5** - إطار عمل CSS
- **jQuery** - مكتبة JavaScript
- **Razor Views** - محرك القوالب

## المكتبات الإضافية

- **ClosedXML** - للتعامل مع ملفات Excel
- **EPPlus** - معالجة ملفات Excel
- **Microsoft.AspNetCore.Authentication** - نظام التوثيق

---

# بنية المشروع

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

# قاعدة البيانات

## الجداول الرئيسية

### المبيعات (Sales)

- `SalesInvoices` - فواتير البيع
- `SalesInvoiceLines` - سطور فواتير البيع
- `SalesOrders` - أوامر البيع
- `SOLines` - سطور أوامر البيع
- `SalesReturns` - مرتجعات البيع
- `SalesReturnLines` - سطور مرتجعات البيع

### المشتريات (Purchasing)

- `PurchaseInvoices` - فواتير الشراء
- `PILines` - سطور فواتير الشراء
- `PurchaseRequests` - طلبات الشراء
- `PRLines` - سطور طلبات الشراء
- `PurchaseReturns` - مرتجعات الشراء
- `PurchaseReturnLines` - سطور مرتجعات الشراء

### المخزون (Stock)

- `StockLedger` - دفتر الحركة المخزني (مصدر الحقيقة)
- `StockFifoMap` - ربط المخارج بالمداخل (FIFO)
- `StockBatches` - رصيد سريع للتشغيلات
- `Batches` - ماستر التشغيلات
- `StockAdjustments` - تسويات المخزون
- `StockAdjustmentLines` - سطور التسويات
- `StockTransfers` - تحويلات المخزون
- `StockTransferLines` - سطور التحويلات

### الماستر داتا (Master Data)

- `Products` - الأصناف
- `Customers` - العملاء/الموردين
- `Warehouses` - المخازن
- `Branches` - الفروع
- `Categories` - الفئات
- `ProductGroups` - مجموعات الأصناف
- `ProductBonusGroups` - مجموعات البونص
- `Accounts` - شجرة الحسابات

### الحسابات (Accounting)

- `LedgerEntries` - دفتر الأستاذ العام
- `CashReceipts` - إيصالات القبض
- `CashPayments` - إيصالات الصرف
- `DebitNotes` - إشعارات الخصم
- `CreditNotes` - إشعارات الإضافة

### الأمان (Security)

- `Users` - المستخدمين
- `Roles` - الأدوار
- `Permissions` - الصلاحيات
- `UserRoles` - ربط المستخدمين بالأدوار
- `RolePermissions` - ربط الأدوار بالصلاحيات
- `UserDeniedPermissions` - استثناءات الصلاحيات
- `UserExtraPermissions` - صلاحيات إضافية
- `UserActivityLogs` - سجل نشاط المستخدمين

---

# نظام الصلاحيات

النظام يستخدم نظام صلاحيات متقدم يعتمد على:

## المكونات الأساسية

1. **المستخدمين (Users)**: المستخدمون الذين يدخلون النظام
2. **الأدوار (Roles)**: مجموعات من الصلاحيات (مثل: مدير، محاسب، بائع)
3. **الصلاحيات (Permissions)**: الصلاحيات الفردية (مثل: `Sales.Invoices.View`, `Products.Edit`)

## آلية العمل

- المستخدم يمكن أن يكون له **أدوار متعددة**
- كل دور له **صلاحيات محددة**
- يمكن **إضافة صلاحيات إضافية** لمستخدم معين
- يمكن **حرمان مستخدم** من صلاحية معينة حتى لو كانت في أدواره

## أمثلة على الصلاحيات

```
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

# الخدمات

## DocumentTotalsService

**الوظيفة**: إعادة حساب إجماليات المستندات (الفواتير)

**الاستخدام**:
- تحديث إجماليات فواتير الشراء والبيع
- حساب الخصومات والضرائب
- تحديث إجماليات الهيدر بعد تعديل السطور

## LedgerPostingService

**الوظيفة**: الترحيل المحاسبي للمستندات

**الاستخدام**:
- ترحيل فواتير الشراء والبيع
- إنشاء قيود في دفتر الأستاذ
- تحديث أرصدة الحسابات
- إدارة مراحل الترحيل (مرحلة 1، مرحلة 2، ...)

## StockAnalysisService

**الوظيفة**: تحليل المخزون وحساب المؤشرات

**الاستخدام**:
- حساب الخصم المرجّح (Weighted Discount)
- حساب المخزون الحالي
- تحليل حركة الأصناف
- حساب تكلفة المخزون

---

# الموديولات الرئيسية

## 1. المبيعات (Sales)

### فواتير البيع (Sales Invoices)

- إنشاء فواتير بيع جديدة
- إضافة سطور للفاتورة
- حساب الخصومات والضرائب تلقائيًا
- ترحيل الفواتير للمخزون والحسابات
- طباعة الفواتير

### أوامر البيع (Sales Orders)

- إنشاء أوامر بيع
- تحويل الأوامر إلى فواتير
- متابعة حالة الأوامر

### مرتجعات البيع (Sales Returns)

- تسجيل مرتجعات البيع
- ربط المرتجعات بالفواتير الأصلية
- تحديث المخزون تلقائيًا

## 2. المشتريات (Purchasing)

### فواتير الشراء (Purchase Invoices)

- إنشاء فواتير شراء جديدة
- إدارة التشغيلات (Batches) مع تواريخ الصلاحية
- حساب تكلفة الوحدة والخصومات
- ترحيل الفواتير للمخزون والحسابات
- نظام التنقل السريع بين الفواتير (أول/سابق/التالي/آخر)

### طلبات الشراء (Purchase Requests)

- إنشاء طلبات شراء
- تحويل الطلبات إلى فواتير
- متابعة حالة الطلبات

### مرتجعات الشراء (Purchase Returns)

- تسجيل مرتجعات الشراء
- تحديث المخزون والحسابات

## 3. المخزون (Stock Management)

### دفتر الحركة (Stock Ledger)

- **مصدر الحقيقة** لجميع حركات المخزون
- تسجيل كل حركة دخول/خروج
- ربط الحركات بالمستندات المصدرية
- دعم نظام FIFO/FEFO

### التشغيلات (Batches)

- إدارة التشغيلات مع تواريخ الصلاحية
- تتبع سعر الجمهور لكل تشغيلة
- حساب تكلفة الوحدة الافتراضية

### رصيد سريع (StockBatches)

- جدول Materialized View للرصيد السريع
- صف واحد لكل (مخزن + صنف + تشغيلة + صلاحية)
- تحديث تلقائي مع كل حركة

### تسويات المخزون (Stock Adjustments)

- تسجيل جرد المخزون
- تسوية الفروقات بين الرصيد الفعلي والنظري
- تحديث دفتر الحركة تلقائيًا

### تحويلات المخزون (Stock Transfers)

- تحويل كميات بين المخازن
- تتبع تكلفة التحويل
- تحديث الرصيد في كلا المخزنين

## 4. الحسابات (Accounting)

### دفتر الأستاذ العام (General Ledger)

- تسجيل جميع القيود المحاسبية
- ربط القيود بالمستندات المصدرية
- كشف حساب لكل حساب
- ميزان مراجعة

### الخزنة (Cash Management)

- إيصالات القبض النقدي
- إيصالات الصرف النقدي
- ربط بالحسابات المالية

### إشعارات الدائن والمدين

- إشعارات الخصم (Debit Notes)
- إشعارات الإضافة (Credit Notes)
- ربط بالعملاء والموردين

## 5. الماستر داتا (Master Data)

### الأصناف (Products)

- إدارة كاملة للأصناف
- ربط بالفئات والمجموعات
- تتبع سعر الجمهور
- سجل تغييرات الأسعار

### العملاء (Customers)

- إدارة بيانات العملاء/الموردين
- ربط بالمناطق الجغرافية
- تتبع الحد الائتماني والرصيد
- سياسات التسعير

### المخازن (Warehouses)

- إدارة المخازن والفروع
- ربط بالسياسات
- قواعد السياسة لكل مخزن

---

# الواجهات والتصميم

## نظام التابات (Tabs System)

- جميع الشاشات الرئيسية تفتح داخل تابات
- إعادة استخدام التابات المفتوحة
- تحديث محتوى التاب بدون إعادة تحميل

## مبدأ Shell + Body

- **Shell ثابت**: شريط الأزرار والتنقل
- **Body متغير**: المحتوى الذي يتغير
- تحديث Body فقط بدون إعادة تحميل كامل

## التنقل السريع

- أزرار التنقل (أول/سابق/التالي/آخر)
- البحث السريع داخل المستندات
- تحديث فوري بدون فتح تابات جديدة

## الطباعة

- تصميم طباعة احترافي للفواتير
- إخفاء عناصر التعديل في الطباعة
- دعم طباعة A4

---

# الأداء والتحسينات

## التحسينات المطبقة

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

## توصيات مستقبلية

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

# التثبيت والتشغيل

## المتطلبات

- .NET 8.0 SDK
- SQL Server 2019 أو أحدث
- Visual Studio 2022 أو Visual Studio Code

## خطوات التثبيت

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

## البيانات الأولية (Seed Data)

عند أول تشغيل، يتم إنشاء:
- ✅ الصلاحيات الأساسية
- ✅ الأدوار الأساسية (Admin, User)
- ✅ ربط الأدوار بالصلاحيات
- ✅ شجرة الحسابات الأساسية
- ✅ مجموعات الأصناف (A-Z)
- ✅ مجموعات البونص
- ✅ السياسات الأساسية

---

# دليل المطور

## إضافة موديول جديد

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

## إضافة صلاحية جديدة

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

## إضافة خدمة جديدة

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

# البنية التقنية

## الطبقات المعمارية

### 1. طبقة العرض (Presentation Layer)

#### Controllers
- **الموقع**: `Controllers/`
- **الوظيفة**: معالجة طلبات HTTP وإرجاع الردود المناسبة
- **المسؤوليات**:
  - التحقق من الصلاحيات
  - استدعاء الخدمات (Services)
  - إرجاع Views أو JSON
  - معالجة الأخطاء

#### Views
- **الموقع**: `Views/`
- **التقنية**: Razor Views (.cshtml)
- **المسؤوليات**:
  - عرض البيانات للمستخدم
  - جمع المدخلات من المستخدم
  - التفاعل مع JavaScript

### 2. طبقة المنطق (Business Logic Layer)

#### Services
- **الموقع**: `Services/`
- **الوظيفة**: تنفيذ منطق العمل المعقد
- **المسؤوليات**:
  - حسابات معقدة
  - معالجة البيانات
  - التكامل بين الموديولات

### 3. طبقة البيانات (Data Access Layer)

#### AppDbContext
- **الموقع**: `Data/AppDbContext.cs`
- **الوظيفة**: نقطة الوصول الرئيسية لقاعدة البيانات
- **المسؤوليات**:
  - تعريف DbSets
  - تكوين العلاقات (Relationships)
  - Fluent API Configuration
  - تتبع تغييرات الأسعار

#### Models
- **الموقع**: `Models/`
- **الوظيفة**: تمثيل جداول قاعدة البيانات
- **المسؤوليات**:
  - تعريف الكيانات (Entities)
  - العلاقات بين الجداول
  - Validation Attributes

### 4. طبقة البنية التحتية (Infrastructure Layer)

#### PagedResult
- **الوظيفة**: إدارة Pagination للقوائم

#### QueryableExtensions
- **الوظيفة**: Extension Methods للبحث والترتيب

#### UserActivityLogger
- **الوظيفة**: تسجيل نشاط المستخدمين

## تدفق البيانات

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

## نظام الأمان

### Authentication (التوثيق)
- **النوع**: Cookie Authentication
- **الإعداد**: `Program.cs`

### Authorization (الصلاحيات)
- **النوع**: Policy-based Authorization
- **الآلية**:
  1. المستخدم له أدوار (Roles)
  2. كل دور له صلاحيات (Permissions)
  3. يمكن إضافة/نقص صلاحيات للمستخدم مباشرة

---

# مخطط قاعدة البيانات

## الجداول الرئيسية

### المبيعات (Sales)

#### SalesInvoices
فاتورة البيع الرئيسية

| العمود | النوع | الوصف |
|--------|------|-------|
| SIId | int (PK) | رقم الفاتورة |
| CustomerId | int (FK) | العميل |
| WarehouseId | int (FK) | المخزن |
| SIDate | date | تاريخ الفاتورة |
| Status | nvarchar(20) | الحالة |
| IsPosted | bit | هل مُرحّلة؟ |
| NetTotal | decimal(18,2) | الإجمالي الصافي |

#### SalesInvoiceLines
سطور فاتورة البيع

| العمود | النوع | الوصف |
|--------|------|-------|
| SIId | int (PK, FK) | رقم الفاتورة |
| LineNo | int (PK) | رقم السطر |
| ProdId | nvarchar(50) (FK) | الصنف |
| Qty | decimal(18,4) | الكمية |
| PriceRetail | decimal(18,2) | سعر الجمهور |
| UnitSalePrice | decimal(18,2) | سعر البيع للوحدة |
| LineNetTotal | decimal(18,2) | صافي السطر |

### المشتريات (Purchasing)

#### PurchaseInvoices
فاتورة الشراء الرئيسية

| العمود | النوع | الوصف |
|--------|------|-------|
| PIId | int (PK) | رقم الفاتورة |
| CustomerId | int (FK) | المورد |
| WarehouseId | int (FK) | المخزن |
| Status | nvarchar(20) | الحالة |
| IsPosted | bit | هل مُرحّلة؟ |

#### PILines
سطور فاتورة الشراء

| العمود | النوع | الوصف |
|--------|------|-------|
| PIId | int (PK, FK) | رقم الفاتورة |
| LineNo | int (PK) | رقم السطر |
| ProdId | nvarchar(50) (FK) | الصنف |
| BatchNo | nvarchar(50) | رقم التشغيلة |
| Expiry | date | تاريخ الصلاحية |
| Qty | decimal(18,4) | الكمية |
| UnitCost | decimal(18,4) | تكلفة الوحدة |
| PriceRetail | decimal(18,2) | سعر الجمهور |
| PurchaseDiscountPct | decimal(5,2) | خصم الشراء % |

### المخزون (Stock)

#### StockLedger
دفتر الحركة المخزني (مصدر الحقيقة)

| العمود | النوع | الوصف |
|--------|------|-------|
| EntryId | int (PK) | رقم القيد |
| WarehouseId | int (FK) | المخزن |
| ProdId | nvarchar(50) (FK) | الصنف |
| BatchId | int (FK, nullable) | التشغيلة |
| TranDate | datetime2 | تاريخ الحركة |
| QtyIn | decimal(18,4) | كمية الدخول |
| QtyOut | decimal(18,4) | كمية الخروج |
| RemainingQty | decimal(18,4) | الكمية المتبقية (FIFO) |
| UnitCost | decimal(18,4) | تكلفة الوحدة |
| SourceType | nvarchar(30) | نوع المصدر |
| SourceId | int | رقم المستند |

#### StockBatches
رصيد سريع للتشغيلات

| العمود | النوع | الوصف |
|--------|------|-------|
| Id | int (PK) | المعرف |
| WarehouseId | int (FK) | المخزن |
| ProdId | nvarchar(50) (FK) | الصنف |
| BatchNo | nvarchar(50) | رقم التشغيلة |
| Expiry | date | تاريخ الصلاحية |
| QtyOnHand | decimal(18,4) | الرصيد الحالي |
| QtyReserved | decimal(18,4) | الكمية المحجوزة |

### الماستر داتا (Master Data)

#### Products
الأصناف

| العمود | النوع | الوصف |
|--------|------|-------|
| ProdId | nvarchar(50) (PK) | كود الصنف |
| ProdName | nvarchar(200) | اسم الصنف |
| Barcode | nvarchar(100) | الباركود |
| CategoryId | nvarchar(50) (FK) | الفئة |
| PriceRetail | decimal(18,2) | سعر الجمهور |
| IsActive | bit | نشط؟ |

#### Customers
العملاء/الموردين

| العمود | النوع | الوصف |
|--------|------|-------|
| CustomerId | int (PK) | كود العميل |
| CustomerName | nvarchar(200) | اسم العميل |
| Phone1 | nvarchar(20) | الهاتف 1 |
| Address | nvarchar(250) | العنوان |
| CreditLimit | decimal(18,2) | الحد الائتماني |
| CurrentBalance | decimal(18,2) | الرصيد الحالي |

### الحسابات (Accounting)

#### LedgerEntries
دفتر الأستاذ العام

| العمود | النوع | الوصف |
|--------|------|-------|
| Id | int (PK) | رقم القيد |
| EntryDate | date | تاريخ القيد |
| AccountId | int (FK) | الحساب |
| CustomerId | int (FK, nullable) | العميل/المورد |
| SourceType | nvarchar(50) | نوع المصدر |
| SourceId | int | رقم المستند |
| PostVersion | int | رقم المرحلة |
| Debit | decimal(18,2) | مدين |
| Credit | decimal(18,2) | دائن |
| Description | nvarchar(250) | البيان |

### الأمان (Security)

#### Users
المستخدمين

| العمود | النوع | الوصف |
|--------|------|-------|
| UserId | int (PK) | رقم المستخدم |
| UserName | nvarchar(50) | اسم الدخول (فريد) |
| DisplayName | nvarchar(150) | الاسم المعروض |
| PasswordHash | nvarchar(256) | كلمة المرور المشفرة |
| IsAdmin | bit | مدير؟ |
| IsActive | bit | نشط؟ |

#### Roles
الأدوار

| العمود | النوع | الوصف |
|--------|------|-------|
| RoleId | int (PK) | رقم الدور |
| Name | nvarchar(50) | اسم الدور (فريد) |

#### Permissions
الصلاحيات

| العمود | النوع | الوصف |
|--------|------|-------|
| PermissionId | int (PK) | رقم الصلاحية |
| Code | nvarchar(100) | كود الصلاحية (فريد) |
| NameAr | nvarchar(200) | الاسم بالعربي |
| Module | nvarchar(100) | الموديول |

## العلاقات الرئيسية

### One-to-Many

```
SalesInvoice (1) ──→ (N) SalesInvoiceLine
PurchaseInvoice (1) ──→ (N) PILine
Customer (1) ──→ (N) SalesInvoice
Warehouse (1) ──→ (N) StockLedger
Product (1) ──→ (N) StockLedger
Account (1) ──→ (N) LedgerEntry
```

### Many-to-Many

```
User (N) ←──→ (N) Role (عبر UserRoles)
Role (N) ←──→ (N) Permission (عبر RolePermissions)
```

---

# استكشاف الأخطاء

## مشاكل شائعة

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

# المراجع والموارد

## الوثائق الرسمية

- [ASP.NET Core Documentation](https://docs.microsoft.com/aspnet/core)
- [Entity Framework Core](https://docs.microsoft.com/ef/core)
- [Bootstrap Documentation](https://getbootstrap.com/docs)

## ملفات التوثيق الداخلية

- `project_state_2025-12-22_tabs_UPDATED_v15_SALES_DONE (1).md` - حالة المشروع التفصيلية
- `PERFORMANCE_ANALYSIS.md` - تحليل الأداء
- `PurchaseInvoice_Full_Reference_UPDATED_2026-01-08.md` - مرجع فاتورة المشتريات

---

# خارطة الطريق المستقبلية

## المرحلة القادمة

- [ ] تحسينات الأداء (Caching, Query Optimization)
- [ ] تقارير متقدمة (Excel Export)
- [ ] واجهة API للتكامل مع أنظمة أخرى
- [ ] تطبيق موبايل (اختياري)

## التحسينات المقترحة

- [ ] نظام الإشعارات
- [ ] لوحة تحكم (Dashboard)
- [ ] تقارير تحليلية متقدمة
- [ ] دعم متعدد اللغات

---

**آخر تحديث**: يناير 2025

**الإصدار**: 1.0.0

---

*هذا التوثيق تم إنشاؤه تلقائيًا من ملفات Markdown في المشروع*
