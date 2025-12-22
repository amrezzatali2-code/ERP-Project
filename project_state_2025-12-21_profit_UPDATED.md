# ERP — PROJECT STATE (حالة مشروع ERP)

> 🔒 **أجزاء ثابتة لا تتغيّر إلا عند تعديل تصميم البرنامج نفسه**  
> 🔄 **الأجزاء الديناميكية (القسم 5) نحدّثها في نهاية كل جلسة عمل.**

---

## 1) تعريف عام بالمشروع (ثابت)

- **اسم المشروع:** ERP  
- **التقنية:** ASP.NET Core MVC + Entity Framework Core + SQL Server (LocalDB)  
- **لغة الواجهة:** عربية (RTL) مع دعم كامل للـ Bootstrap و jQuery و CSS مخصص (`wwwroot/css/site.css`).  
- **هدف البرنامج العام:**
  - برنامج ERP متكامل لشركة توزيع أدوية يمكن تركيبه وبيعه لشركات مشابهة.
  - يغطي:  
    - الماستر داتا (الأصناف، العملاء، المخازن، …)  
    - المشتريات  
    - المبيعات  
    - المخزون (دفتر الحركة + FIFO + التحويلات + التسويات)  
    - الحسابات (الحسابات العامة / القيود / الخزنة)  
    - نظام صلاحيات كامل (Users / Roles / Permissions / Logs).

---

## 2) البنية التقنية والملفات الأساسية (ثابت)

### 2.1 البنية العامة للمشروع

- **Program.cs**
  - تكوين الـ services (DbContext, Identity, Authentication, Authorization).
  - تسجيل الـ Services الداخلية (مثل `DocumentTotalsService`, `StockAnalysisService`, `UserActivityLogger`).
  - إعداد الـ Middleware (Authentication/Authorization, StaticFiles, Routing, …).

- **AppDbContext.cs** (داخل مجلد `Data`)
  - تعريف جميع الجداول/الـ DbSet:
    - Master Data (Accounts, Products, Customers, Warehouses, …)
    - Purchase / Sales / Stock / Security / Logs …
  - تكوين العلاقات، الـ OnModelCreating، وأي Fluent API إضافي.

- **الهجرة (Migrations)**
  - مجلد `Migrations` يحتوي كل الـ migrations بترتيب زمني.
  - آخر Migration هي الحالة الفعلية لهيكل قاعدة البيانات.

- **الحزم (NuGet Packages)**
  - `Microsoft.EntityFrameworkCore` + `Microsoft.EntityFrameworkCore.SqlServer` + `Microsoft.EntityFrameworkCore.Tools`
  - `Microsoft.AspNetCore.Identity.EntityFrameworkCore`
  - `Microsoft.AspNetCore.Authentication.JwtBearer`
  - `ClosedXML`  (للتقارير / التصدير إلى Excel)
  - `EPPlus`     (للتعامل مع ملفات Excel)

---

### 2.2 طبقة الـ Infrastructure (ثابت)

- **PagedResult.cs**
  - كلاس عام لإدارة الـ Paging + البحث + الترتيب.
  - يُستخدم في معظم شاشات القوائم (نظام القوائم الموحد).

- **QueryableExtensions.SearchSort.cs**
  - Extensions لـ IQueryable:
    - تعميم البحث (Search)
    - الترتيب (Sort)
    - دعم الفلاتر (FromCode/ToCode, FromDate/ToDate, …).

- **UserActivityLogger.cs**
  - تسجيل عمليات المستخدمين (Create / Edit / Delete / Login / …).
  - يُستدعى من الكنترولرات المهمة (فواتير، حركات مخزون، صلاحيات، …).

---

### 2.3 طبقة الـ Security (ثابت)

- **PermissionCodes.cs**
  - ثوابت لأكواد الصلاحيات (View / Create / Edit / Delete لكل جدول/شاشة).
  - تُستخدم مع جداول:
    - `Permission`
    - `RolePermission`
    - `UserExtraPermission`
    - `UserDeniedPermission`
    - `UserRole`
    - `UserActivityLog`
    - `User`

---

### 2.4 الـ Services (ثابت)

- **DocumentTotalsService.cs**
  - مسئول عن إعادة حساب إجماليات المستندات:
    - `PurchaseInvoice` + `PurchaseReturn`
    - `SalesInvoice` + `SalesReturn`
    - وربطها بجداول السطور (PILines, PRLines, SalesInvoiceLines, SalesReturnLines).

- **StockAnalysisService.cs**
  - تحليل المخزون ودفتر الحركة:
    - قراءة `StockLedger` + `StockFifoMap` + جداول الحركات.
    - حساب الكميات المتاحة/المتبقية.
    - تجهيز بيانات تحليلات المخزون (متوسطات، خصم مرجّح …) لاستخدامها لاحقًا.

---

### 2.5 طبقة الـ Data / Seed (ثابت)

- **Seeders (داخل Data/Seed):**
  - `AccountsSeeder`          — إنشاء شجرة الحسابات الأساسية.
  - `PermissionSeeder`        — ملء جدول `Permissions` بكل أكواد الصلاحيات.
  - `PolicySeeder`            — سياسات الأصناف/العملاء/المخازن.
  - `ProductGroupSeeder`      — مجموعات الأصناف.
  - `ProductBonusGroupSeeder` — مجموعات العروض/البونص.
  - `RoleSeeder`              — الأدوار الأساسية (Admin, User, …).
  - `RolePermissionSeeder`    — ربط الأدوار بالصلاحيات.
  - (جميع هذه الـ seeders تُستدعى من نقطة واحدة عند تشغيل النظام أول مرة).

---

## 3) الموديولات الرئيسية (Models + Controllers + Views) — ثابت

> الهدف هنا أن أتذكر خريطة المشروع، وليس كل ملف cshtml بالتفصيل.  
> القاعدة العامة: **كل Model رئيسي له Controller بنفس الاسم تقريبًا + Folder في Views بنفس الاسم.**

### 3.1 Master Data / التعريفات الأساسية

- **الحسابات وقطع الحساب**
  - Models: `Account`
  - Controller: `AccountsController`
  - Views Folder: `Views/Accounts`

- **المناطق الجغرافية**
  - `Governorate`, `District`, `Area`, `City`
  - Controllers: `GovernoratesController`, `DistrictsController`, `AreasController`, `CitiesController`
  - Views: `Views/Governorates`, `Views/Districts`, `Views/Areas`, `Views/Cities`

- **الفروع والمخازن**
  - Models: `Branch`, `Warehouse`, `WarehousePolicyRule`
  - Controllers: `BranchesController`, `WarehousesController`, `WarehousePolicyRulesController`
  - Views: `Views/Branches`, `Views/Warehouses`, `Views/WarehousePolicyRules`

- **العملاء**
  - Model: `Customer`
  - Controller: `CustomersController`
  - Views: `Views/Customers`

- **الأصناف ومجموعاتها**
  - Models:
    - `Product`, `ProductGroup`, `ProductGroupPolicy`,
    - `ProductBonusGroup`, `ProductPriceHistory`
  - Controllers:
    - `ProductsController`, `ProductGroupsController`,
    - `ProductGroupPoliciesController`, `ProductBonusGroupsController`,
    - `ProductPriceHistoryController`
  - Views:
    - `Views/Products`, `Views/ProductGroups`,
    - `Views/ProductGroupPolicies`, `Views/ProductBonusGroups`,
    - `Views/ProductPriceHistory`

---

### 3.2 المشتريات (Purchasing)

- Models:
  - `PurchaseInvoice`, `PILine`
  - `PurchaseRequest`, `PurchaseReturn`, `PurchaseReturnLine`
- Controllers:
  - `PurchaseInvoicesController`
  - `PILinesController`
  - `PurchaseRequestsController`
  - `PurchaseReturnsController`
  - `PurchaseReturnLinesController`
- Views:
  - `Views/PurchaseInvoices`
  - `Views/PILines`
  - `Views/PurchaseRequests`
  - `Views/PurchaseReturns`
  - `Views/PurchaseReturnLines`

> **ملاحظة مهمة:**  
> شاشة فاتورة المشتريات الأساسية هي `PurchaseInvoices/Show.cshtml`  
> — فيها:
>   - كارت الهيدر (المورد + المخزن + التاريخ + …)  
>   - كارت إضافة صنف للفاتورة  
>   - كارت سطور الفاتورة (Grid)  
>   - كارت الإجماليات والتخزين.

---

### 3.3 المبيعات (Sales)

- Models:
  - `SalesInvoice`, `SalesInvoiceLine`
  - `SalesOrder`, `SOLine`
  - `SalesReturn`, `SalesReturnLine`
- Controllers:
  - `SalesInvoicesController`, `SalesInvoiceLinesController`
  - `SalesOrdersController`, `SOLinesController`
  - `SalesReturnsController`, `SalesReturnLinesController`
- Views:
  - `Views/SalesInvoices`, `Views/SalesInvoiceLines`
  - `Views/SalesOrders`, `Views/SOLines`
  - `Views/SalesReturns`, `Views/SalesReturnLines`

---

### 3.4 المخزون (Stock)

- Models:
  - `Batch`                — تشغيلات الأصناف + Expiry + أسعار افتراضية
  - `StockLedger`          — دفتر الحركة المخزنية (كل حركة في المخزن)
  - `StockFifoMap`         — ربط المخارج بالمداخل (FIFO)
  - `StockAdjustment`, `StockAdjustmentLine`
  - `StockTransfer`, `StockTransferLine`
- Controllers:
  - `StockLedgerController`
  - `StockFifoMapController`
  - `StockAdjustmentsController`, `StockAdjustmentLinesController`
  - `StockTransfersController`, `StockTransferLinesController`
- Views:
  - `Views/StockLedger`
  - `Views/StockFifoMap`
  - `Views/StockAdjustments`, `Views/StockAdjustmentLines`
  - `Views/StockTransfers`, `Views/StockTransferLines`

---

### 3.5 المستندات المالية الأخرى

- **الخزنة / الكاش**
  - Models: `CashReceipt`, `CashPayment`, `LedgerEntry`
  - Controllers: `CashReceiptsController`, `CashPaymentsController`, `LedgerEntriesController`
  - Views: `Views/CashReceipts`, `Views/CashPayments`, `Views/LedgerEntries`

- **مستندات التسوية**
  - Models: `DebitNote`, `CreditNote`
  - Controllers: `DebitNotesController`, `CreditNotesController`
  - Views: `Views/DebitNotes`, `Views/CreditNotes`

- **ترقيم المستندات**
  - Model: `DocumentSeries`
  - Controller: `DocumentSeriesController`
  - Views: `Views/DocumentSeries`

---

### 3.6 المستخدمين والصلاحيات (Security Module)

- Models:
  - `User`, `UserRole`
  - `Role`, `RolePermission`
  - `Permission`
  - `UserExtraPermission`, `UserDeniedPermission`
  - `UserActivityLog`
- Controllers:
  - `UsersController`, `UserRolesController`
  - `RolesController`, `RolePermissionsController`
  - `PermissionsController`
  - `UserExtraPermissionsController`, `UserDeniedPermissionsController`
  - `UserActivityLogsController`
  - `LoginController`
- Views:
  - `Views/Users`, `Views/UserRoles`
  - `Views/Roles`, `Views/RolePermissions`
  - `Views/Permissions`
  - `Views/UserExtraPermissions`, `Views/UserDeniedPermissions`
  - `Views/UserActivityLogs`
  - `Views/Login`

---

### 3.7 ViewModels (ثابت)

- `LoginViewModel` — نموذج تسجيل الدخول.
- `PurchaseInvoiceHeaderDto` — نموذج هيدر فاتورة المشتريات.

---

### 3.8 Shared / Partials (ثابت على مستوى الفكرة)

- مجلد `Views/Shared` يحتوي على:
  - الـ Layout الرئيسي.
  - Partial Views عامة مثل:
    - شريط الفلاتر (Index Filters / Toolbar).
    - شريط الترقيم (Pager).
    - أجزاء الـ Navbar / Sidebar.
  - هذه الأجزاء تُستخدم في نظام القوائم الموحد لكل الجداول.


### 3.9 قاعدة ثابتة للتسعير والتشغيلات (Batches & Pricing)

- **سعر الجمهور الأساسي للصنف** موجود فى حقل `Product.PriceRetail`.
- عند شراء صنف بتشغيلات متعددة:
  - يتم تسجيل **سعر الجمهور** و **خصم الشراء %** لكل تشغيله على مستوى سطر فاتورة المشتريات  
    (`PILine.PriceRetail` و `PILine.PurchaseDiscountPct`).
  - يتم تحديث جدول `Batch` بالقيم:
    - `Batch.PriceRetailBatch`  = سعر الجمهور لهذه التشغيله فى آخر شراء (أو آخر تعديل يدوى).
    - `Batch.UnitCostDefault`   = تكلفة الوحدة الافتراضية لهذه التشغيله.
- **فواتير المبيعات** تعتمد على:
  - القراءة من جدول `Batch` (حسب `ProdId + BatchNo + Expiry`) إن وُجد سعر تشغيله.
  - لو لم توجد تشغيله مسجَّلة ⇒ نرجع إلى `Product.PriceRetail` كـ fallback.
- **لا يتم تعديل `Product.PriceRetail` تلقائيًا مع كل تشغيله**؛ يتم تحديثه يدويًا أو عند توحيد الأسعار لكل التشغيلات.
- **الخصم المرجّح (Weighted Discount) لن يُخزَّن كحقل ثابت فى الجداول**:
  - سيتم احتسابه لاحقًا فى التقارير بالاعتماد على بيانات `StockLedger` وحساب الكمية المتبقية (FIFO) لكل صنف/تشغيله/مخزن.

---

---

## 4) ملفات الواجهة (wwwroot) — ثابت

- **CSS**
  - `wwwroot/css/site.css` — تنسيقات مخصصة (RTL, ألوان، كروت الفواتير، الجداول، …).

- **JavaScript**
  - `wwwroot/js/site.js`         — سكربتات عامة + منطق التابات (فتح الشاشات فى تابات وتحديث التاب لو كان مفتوح).
  - `wwwroot/js/grid-bulk.js`    — عمليات Bulk على الجداول.
  - `wwwroot/js/grid-columns.js` — إظهار/إخفاء أعمدة الجداول (اختيار الأعمدة).

- **نظام التابات (Tabs) — ثابت فى كل الشاشات**
  - كل شاشة رئيسية (مثلاً: فواتير المشتريات، دفتر الحركة المخزنية، التقارير) تفتح داخل تاب أعلى الصفحة.
  - عند الضغط على زر يفتح شاشة تم فتحها من قبل:
    - لو التاب موجود بالفعل ⇒ يتم **إعادة استخدامه وتحديث محتواه** بدل فتح تاب جديد.
    - لو التاب غير موجود ⇒ يتم إنشاء تاب جديد.
  - سيتم تثبيت منطق التابات فى سكربت واحد (حاليًا داخل `site.js`، ويمكن لاحقًا فصله لملف مستقل عند الحاجة).

- **Lib**
  - Bootstrap (css/js) — الإطار الأساسي للـ UI.
  - jQuery + jQuery Validation.

---

## 5) حالة العمل الحالية (Dynamic — يتم تحديثها في نهاية كل جلسة)

### 5.1 آخر نسخة محفوظة (Git)

- Branch: `master`  
- آخر Commit: `e7f868c`  
- Message: `STILL WORK ON STOCK LEDGER`  
- ملاحظة: حذف ملفات `docs/xlsx/zip` كان مقصود لتنظيف الريبو.

---

### 5.2 الجزء الحالي الذي نعمل عليه

**أولاً: توحيد شاشة فاتورة المشتريات (PurchaseInvoices/Show.cshtml)**

- نفس شكل الفاتورة (الهيدر + كروت العميل والمخزن + كارت السطور + كارت الأزرار الجانبية) يجب أن يعمل بنفس الطريقة فى الحالات الآتية:
  - عند فتح **فاتورة جديدة** من زر "فاتورة مشتريات جديدة".
  - عند فتح **فاتورة قديمة** من:
    - قائمة فواتير المشتريات (SalesInvoices/Index أو PurchaseInvoices/Index).
    - دفتر الحركة المخزنية (StockLedger → زر "فتح الفاتورة").
- الهدف: كل الطرق تستخدم نفس الـ View ونفس سكربت الجافاسكربت (نفس دالة `renderGroupedLines` ونفس كروت السطور) بدون تكرار أو اختلاف فى الرسم.

**ثانيًا: التحضير لتقارير الخصم المرجّح والمخزون الحالي**

- الاعتماد على الأعمدة الجديدة فى `StockLedger`:
  - `PriceRetail`, `UnitCost`, `TotalCost`
  - `PurchaseDiscountPct`
  - `QtyIn`, `QtyOut`, `RemainingQty`
  - `BatchId`, `BatchNo`, `Expiry`
- **الخصم المرجّح (Weighted Discount) لن يكون عمودًا ثابتًا فى جدول `StockLedger`**:
  - سيتم حسابه لاحقًا فى تقارير التحليل من حركة المخزون، مع الأخذ فى الاعتبار الكمية المتبقية الفعلية لكل صنف/تشغيلة/مخزن (FIFO).

---

### 5.3 الأعمدة فى جدول / شاشة StockLedger (تم تنفيذها — بدون عمود الخصم المرجّح)

تم بالفعل إضافة الأعمدة المطلوبة فى الجدول / الـ View المستخدم فى صفحة `StockLedger/Index`، وكذلك فى الـ Grid على الشاشة:

- `PriceRetail`          — سعر الجمهور وقت الحركة (يأتى من سطر فاتورة الشراء أو من جدول الـ Batch).
- `QtyIn` / `QtyOut`     — كميات الدخول والخروج من المخزن.
- `UnitCost`             — تكلفة الوحدة الفعلية للحركة.
- `TotalCost`            — إجمالى تكلفة الحركة (عادة = الكمية * تكلفة الوحدة).
- `PurchaseDiscountPct`  — نسبة خصم الشراء المسجَّلة على سطر فاتورة المشتريات.
- `RemainingQty`         — الكمية المتبقية بعد الحركة (تُستخدم لاحقًا مع FIFO).
- `BatchId` / `BatchNo` / `Expiry` — ربط الحركة بالتشغيلة وتاريخ الصلاحية.
- `SourceType` / `SourceId` / `SourceLine` — نوع المستند ورقم المستند ورقم السطر.

> **تنبيه ثابت:**  
> لا يوجد عمود فعلى باسم `WeightedDiscount` داخل `StockLedger`.  
> هذا الحقل سيكون **مشتقًّا فى التقارير فقط** من هذه البيانات + الكمية المتبقية.

---

### 5.4 منطق البيانات فى الأعمدة الجديدة (مصدر القيمة + علاقتها بالتسعير)

**المبدأ العام:**  
`StockLedger` هو دفتر الحركة الموحد الذى نعتمد عليه فى:

- حساب المخزون الفعلى لكل صنف/تشغيلة/مخزن.
- حساب تكلفة المخزون.
- تجهيز تقارير الخصم المرجّح على المخزون الحالى.

**مصادر القيم الأساسية:**

- `PriceRetail`:
  - فى حركات الشراء (Purchase): يأتى من `PILine.PriceRetail` (سعر الجمهور الذى تم إدخاله لهذه التشغيلة فى فاتورة الشراء).
  - يتم فى نفس الوقت تحديث جدول `Batch` بالحقل `PriceRetailBatch`.
  - فى الحركات اللاحقة (البيع / المرتجع / التحويل) يمكن القراءة من `Batch.PriceRetailBatch`؛ ولو لم توجد تشغيلة معروفة يتم الرجوع إلى `Product.PriceRetail` كـ fallback.
- `UnitCost`:
  - فى الشراء: يأتى من تكلفة سطر فاتورة المشتريات بعد احتساب الخصومات الفعلية.
  - فى الحركات الأخرى (بيع / مرتجع / تحويل / تسوية): نستخدم نفس تكلفة الدخول (حسب FIFO) أو تكلفة افتراضية من `Batch.UnitCostDefault` عند الحاجة.
- `TotalCost`:
  - عادة = (كمية الحركة الفعلية * `UnitCost`) مع إشارة موجبة/سالبة حسب نوع الحركة.
- `PurchaseDiscountPct`:
  - يُنسخ من `PILine.PurchaseDiscountPct`، ويُستخدم لاحقًا عند حساب الخصم المرجّح على المخزون.
- `RemainingQty`:
  - تمثل الكمية المتبقية بعد هذه الحركة، وتُستخدم مع باقى الحركات لحساب المخزون الحالى لكل صنف/تشغيلة/مخزن.
- `BatchId` / `BatchNo` / `Expiry`:
  - تربط الحركة بتشغيلة محددة، وهى الأساس لتطبيق FIFO وحساب المخزون والخصم المرجّح على مستوى التشغيلات.

**الخصم المرجّح (Weighted Discount):**

- لن يُخزَّن كحقل فى `StockLedger` أو `Batch`.
- سيُحسب فى تقرير مستقل (تقرير الخصم المرجّح) بالشكل التقريبى التالى:
  - تجميع كل الحركات الداخلة لكل صنف/تشغيلة/مخزن داخل فترة معينة.
  - احتساب تكلفة المخزون مقابل سعر الجمهور (من `PriceRetail`) مع مراعاة الكميات الخارجة (FIFO).
  - من ذلك نستنتج الخصم المرجّح على الكمية المتبقية فعليًا فى المخازن.

---

### 5.5 الملفات المرتبطة بالتعديل الحالى

- `Views/StockLedger/Index.cshtml`              — عرض دفتر الحركة مع الأعمدة الجديدة.
- `Controllers/StockLedgerController.cs`        — الفلترة / الربط مع الـ View / مصادر البيانات.
- (عند الحاجة) `Services/StockAnalysisService.cs` — خدمات تحليل المخزون والخصم المرجّح.
- `Views/PurchaseInvoices/Show.cshtml`          — توحيد شكل فاتورة المشتريات الجديدة والقديمة.
- `Controllers/PurchaseInvoicesController.cs`   — فتح الفواتير من أكثر من مكان (قائمة الفواتير / دفتر الحركة).
- `wwwroot/js/site.js`                          — منطق التابات + فتح الفاتورة فى تاب واحد وتحديثه.
- `Models/Batch.cs`                             — تخزين سعر الجمهور للتشغيلة وتكلفة الوحدة الافتراضية.
- أى `Migrations` تخص إضافة/تغيير أعمدة فى `StockLedger` أو `Batch`.

---

### 5.6 المشاكل الحالية / TODO القريب

1. **اختلاف شكل فاتورة المشتريات:**
   - فتح فاتورة جديدة وفتح فاتورة قديمة (من قائمة الفواتير أو من دفتر الحركة) لا يزال به اختلاف بسيط فى طريقة الرسم / تحميل السطور.
   - الهدف: توحيد المنطق بحيث يستخدم الجميع نفس الـ View ونفس سكربت الجافاسكربت.

2. **تثبيت منطق التابات (Tabs):**
   - التأكد أن كل الأزرار التى تفتح نفس الشاشة (مثلاً: فاتورة المشتريات من أكثر من مكان) تستخدم نفس دالة التابات:
     - لو التاب موجود ⇒ يتم تنشيطه وتحديث محتواه.
     - لو غير موجود ⇒ يتم إنشاؤه.
   - سيتم تثبيت هذا المنطق داخل `site.js` (أو ملف منفصل لوجيك التابات) كجزء من الثوابت.

3. **تقرير الخصم المرجّح والمخزون الحالى:**
   - لم يتم بعد إنشاء شاشة/تقرير مخصص لحساب وعرض الخصم المرجّح على الكمية المتبقية لكل صنف/تشغيلة/مخزن.
   - هذا التقرير سيعتمد بالكامل على بيانات `StockLedger` + الـ FIFO + أعمدة السعر والتكلفة المذكورة أعلاه.

---

> ✅ **طريقة الاستخدام:**  
> - في بداية أي جلسة جديدة: ترسل لي نسخة من هذا الملف لأقرأه فأعرف بسرعة أين وصلنا.  
> - في نهاية الجلسة: أكتب لك نص التحديث لقسم **5) حالة العمل الحالية** (مع الحفاظ على الأقسام 1–4 ثابتة)، وتلصقه مكان القسم القديم.

---

## 7) طريقة حساب الأرباح (ثابت مشروع)

> هذه الفقرة ثابتة تشرح **منطق حساب التكلفة والربح** في النظام.  
> ملاحظة مهمة حسب آخر اتفاق: **في فاتورة الشراء (PILines) يوجد بالفعل: تكلفة الوحدة + خصم الشراء + قيمة السطر**  
> لذلك **لا نحتاج “نحسب” خصم الشراء داخل الفاتورة**، نحن فقط **نخزن القيم** ونستخدمها لاحقًا في التقارير.

### 7.1 ما الذي نعتبره “تكلفة”؟

- **تكلفة الوحدة عند الشراء** هي القيمة التي يدخلها المستخدم في:
  - `PILine.UnitCost`  (تكلفة الوحدة الفعلية)
- وعند إنشاء قيد الشراء في `StockLedger` نقوم بتخزينها كما هي في:
  - `StockLedger.UnitCost`
- **إجمالي تكلفة القيد**:
  - `StockLedger.TotalCost = QtyIn * UnitCost`
- **خصم الشراء %** (`PILine.PurchaseDiscountPct`) نحتفظ به كـ “معلومة تحليل/تقرير” (وليس شرطًا أنه أساس تكلفة إذا كانت التكلفة تم إدخالها جاهزة).

### 7.2 منطق FIFO وتثبيت تكلفة الخروج

- عند كل حركة خروج (بيع / مرتجع شراء / تحويل خروج / تسوية خصم مخزون):
  1. يتم استهلاك الكميات من **سجلات الدخول** في `StockLedger` (`QtyIn > 0`) بالترتيب (FIFO).
  2. لكل جزء تم سحبه من دخلة معينة، يتم إنشاء صف في:
     - `StockFifoMap`
     ويحتوي على:
     - `OutEntryId` : رقم قيد الخروج (StockLedger)
     - `InEntryId`  : رقم قيد الدخول الذي سحبنا منه (StockLedger)
     - `Qty`        : الكمية المسحوبة من هذه الدخلة
     - `UnitCost`   : تكلفة الوحدة “Snapshot” من الدخلة وقت الشراء
  3. بهذه الطريقة تكون **تكلفة أي خروج** قابلة للحساب بدقة من FIFO.

### 7.3 حساب الربح على مستوى سطر البيع

> **الربح يُحسب ديناميكيًا في التقارير** ولا نحتاج جدول أرباح مستقل الآن.

لكل سطر بيع (SalesInvoiceLine) أو أي حركة خروج مرتبطة به:

1. **قيمة البيع الصافية للسطر**
   - نقرأها من بيانات سطر البيع (بعد الخصومات والضرائب حسب تصميم شاشة البيع).
   - المفهوم:  
     `LineNetTotal` = إجمالي السطر بعد الخصم والضريبة (أو حسب ما ستعتمده في سطور البيع).

2. **تكلفة الكمية المباعة**
   - نجمع من `StockFifoMap` لكل `OutEntryId` الخاص بهذه الحركة:
     - `CostTotal = Σ (Qty × UnitCost)`

3. **الربح**
   - `ProfitValue = LineNetTotal - CostTotal`
   - (اختياري للتقرير) نسبة الربح:
     - `ProfitPercent = (ProfitValue / LineNetTotal) * 100` مع حماية القسمة على صفر.

### 7.4 أين نعرض الأرباح؟

- **لا يوجد جدول أرباح** الآن.
- الأرباح تُحسب في طبقة الخدمات/التقارير (مثال: `StockAnalysisService` أو خدمة تقارير أرباح).
- مصدر البيانات دائمًا:
  - `StockLedger` (الحركات)
  - `StockFifoMap` (ربط FIFO للتكلفة)
  - `SalesInvoiceLines` (سعر البيع والخصومات والضرائب)

> ✅ هذا المنطق “ثابت” للمشروع: أي تغيير في طريقة إدخال التكلفة أو طريقة البيع يجب تحديثه هنا قبل تعديل الكود.

## 8) طريقة حساب الخصم المرجّح (Weighted Discount) على المخزون الحالي (ثابت مشروع)

> الهدف: نطلع تقرير يجيب **الكمية الحالية** + **الخصم المرجّح** لكل (مخزن + صنف)  
> ويمكن توسعته لاحقًا لكل (مخزن + صنف + تشغيلة + صلاحية).

### 8.1 لماذا لا نحتاج نخزن الخصم المرجّح داخل StockLedger؟

- لأن الخصم المرجّح “يتغير” مع البيع (FIFO) ومع اختلاف التشغيلات والخصومات.
- الأفضل نحسبه في التقرير اعتمادًا على:
  - `StockLedger.QtyIn / QtyOut / RemainingQty`
  - `StockLedger.PurchaseDiscountPct`
  - `StockLedger.PriceRetail`
  - وربط FIFO (إن احتجنا) لضمان أن الـ RemainingQty فعلاً تمثّل المخزون الحالي.

### 8.2 مصدر “الكمية الحالية” (Current Qty)

- نعتمد على سطور الدخول فقط (Purchase / TransferIn / …) التي لها:
  - `QtyIn > 0`
- والكمية الحالية تؤخذ من:
  - `RemainingQty` (المتبقي من الدخلة بعد كل عمليات السحب FIFO)
- إذن:
  - `CurrentQty = Σ RemainingQty`  
  (على مستوى الصنف/المخزن أو الصنف/المخزن/التشغيلة… حسب التقرير)

### 8.3 معادلة الخصم المرجّح (المعتمدة)

لدينا طريقتين، ونثبت “المعتمدة” كالتالي:

✅ **الطريقة المعتمدة: خصم مرجّح بالقيمة (Value-Weighted)**  
لأن سعر الجمهور قد يختلف بين التشغيلات:

- `WeightedDiscountPct = ( Σ (RemainingQty × PriceRetail × PurchaseDiscountPct) ) / ( Σ (RemainingQty × PriceRetail) )`

**ملاحظات مهمة:**
- لو المقام = 0 ⇒ الخصم المرجّح = 0.
- نستخدم `PurchaseDiscountPct` التي تم حفظها عند الشراء (من PILine) داخل `StockLedger`.
- نستخدم `PriceRetail` داخل `StockLedger` (سعر الجمهور وقت الدخلة/التشغيلة).

(اختياري للتقارير البسيطة) **خصم مرجّح بالكمية فقط (Qty-Weighted)**:
- `WeightedDiscountPct_Qty = ( Σ (RemainingQty × PurchaseDiscountPct) ) / ( Σ RemainingQty )`
لكن هذه أقل دقة لو أسعار الجمهور مختلفة.

### 8.4 أين سنستخدم الخصم المرجّح؟

- في **تقرير المخزون الحالي**: يظهر لكل صنف:
  - الكمية الحالية
  - الخصم المرجّح
  - (اختياري) متوسط سعر الجمهور الفعلي على المخزون
- وفي **فاتورة المبيعات**:
  - نستطيع إحضار الخصم المرجّح “لحظيًا” للصنف في المخزن المختار، لاستخدامه كمرجع لاتخاذ قرار الخصم/السعر.

### 8.5 المتطلبات اللازمة في البيانات لكي يكون التقرير صحيح

- لازم أي حركة شراء تنتج:
  - قيد دخول في `StockLedger` بـ:
    - `QtyIn`, `UnitCost`, `TotalCost`, `PriceRetail`, `PurchaseDiscountPct`, `BatchNo`, `Expiry`, `WarehouseId`, `ProdId`
- وFIFO يحدّث `RemainingQty` لسجلات الدخول بدقة بعد كل خروج.
