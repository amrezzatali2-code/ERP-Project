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


#### 2.1.1 🔒 ثوابت أسماء DbSet المعتمدة في AppDbContext (ثابت مشروع)

> هذا القسم مرجعي وأساسي.  
> ❌ يُمنع استخدام أي اسم DbSet غير مذكور هنا داخل أي Controller أو Service.  
> ✅ الهدف: منع أخطاء الأسماء (مثل StockLedgers/StockLedger) وتثبيت مرجع واحد للمشروع.

**المشتريات (Purchasing)**
- `PurchaseInvoices`  → `DbSet<PurchaseInvoice>`
- `PILines`           → `DbSet<PILine>`
- `PurchaseRequests`  → `DbSet<PurchaseRequest>`
- `PRLines`           → `DbSet<PRLine>`
- `PurchaseReturns`   → `DbSet<PurchaseReturn>`
- `PurchaseReturnLines` → `DbSet<PurchaseReturnLine>`

**المبيعات (Sales)**
- `SalesInvoices`       → `DbSet<SalesInvoice>`
- `SalesInvoiceLines`   → `DbSet<SalesInvoiceLine>`
- `SalesOrders`         → `DbSet<SalesOrder>`
- `SOLines`             → `DbSet<SOLine>`
- `SalesReturns`        → `DbSet<SalesReturn>`
- `SalesReturnLines`    → `DbSet<SalesReturnLine>`

**المخزون (Stock)**
- `StockLedger`   → `DbSet<StockLedger>`  (**الاسم المعتمد مفرد بدون s**)
- `StockFifoMap`  → `DbSet<StockFifoMap>` (الجدول موجود، وتفعيله المنطقي الأساسي يبدأ مع المبيعات)
- `Batches`       → `DbSet<Batch>`

**الماستر داتا (Master Data)**
- `Products`     → `DbSet<Product>`
- `Customers`    → `DbSet<Customer>`
- `Warehouses`   → `DbSet<Warehouse>`
- `Branches`     → `DbSet<Branch>`
- `Categories`   → `DbSet<Category>`
- `Accounts`     → `DbSet<Account>`
- `DocumentSeries` → `DbSet<DocumentSeries>`

**التسويات والتحويلات**
- `StockAdjustments`      → `DbSet<StockAdjustment>`
- `StockAdjustmentLines`  → `DbSet<StockAdjustmentLine>`
- `StockTransfers`        → `DbSet<StockTransfer>`
- `StockTransferLines`    → `DbSet<StockTransferLine>`

**الخزنة / القيود**
- `LedgerEntries` → `DbSet<LedgerEntry>`
- `CashReceipts`  → `DbSet<CashReceipt>`
- `CashPayments`  → `DbSet<CashPayment>`
- `DebitNotes`    → `DbSet<DebitNote>`
- `CreditNotes`   → `DbSet<CreditNote>`

**المستخدمين والصلاحيات**
- `Users`                 → `DbSet<User>`
- `Roles`                 → `DbSet<Role>`
- `Permissions`           → `DbSet<Permission>`
- `UserRoles`             → `DbSet<UserRole>`
- `RolePermissions`       → `DbSet<RolePermission>`
- `UserDeniedPermissions` → `DbSet<UserDeniedPermission>`
- `UserExtraPermissions`  → `DbSet<UserExtraPermissions>`
- `UserActivityLogs`      → `DbSet<UserActivityLog>`


#### 2.1.2 🔒 ثوابت عامة للتطوير (Global Coding Standards) — ثابت مشروع

> هذه قواعد تشغيل ثابتة نلتزم بها في كل أجزاء الـ ERP.  
> الهدف: سرعة + دقة + منع كسر الميزات الحالية عند إضافة أي تعديل جديد.

**أولاً: قواعد الأمان (Transactions)**
- أي عملية تؤثر على **سطور مستند** (إضافة/تعديل/حذف) يجب أن تكون داخل **Transaction واحدة** تشمل:
  - جدول السطور + تحديث إجماليات الهيدر + تحديث المخزون + أي جداول مساعدة.
- أي فشل في خطوة ⇒ `Rollback` + رسالة عربية واضحة للمستخدم.

**ثانيًا: قواعد تسجيل النشاط (UserActivityLog)**
- أي عملية مهمة تُسجَّل دائمًا:
  - Create / Edit / Delete / Post
- السجل يحتوي (على الأقل): المستخدم + الوقت + نوع العملية + رقم المستند/السجل.
- ❌ ممنوع ترك عمليات كبيرة بدون Log.

**ثالثًا: قاعدة “لا تغيّر الأسماء / لا نخمن الأسماء”**
- ❌ ممنوع تخمين أسماء:
  - DbSet / Properties / Routes / Actions
- أي تغيير اسم (Model/Property/DbSet/Route) ⇒ يجب توثيقه فورًا في هذا الملف.

**رابعًا: ثوابت نظام التابات (Tabs) + frame**
- أي شاشة تفتح داخل Tab يجب أن تعتمد:
  - `data-tab-id` ثابت (بدون أرقام/بدون IDs متغيرة)
  - الرابط يحتوي `frame=1`
- أي تعديل في منطق التابات يكون فقط داخل:
  - `Views/Shared/_Layout.cshtml`
  - `wwwroot/js/site.js`

**خامسًا: ثوابت السرعة داخل الفواتير (Shell ثابت + Body متغير)**
- الفاتورة = Shell ثابت داخل التاب + Body متغير يتم تحميله جزئيًا.
- التنقل (أول/سابق/التالي/آخر/بحث) يتم بـ `fetch()` لتبديل الـ Body فقط.
- الأزرار لا تختفي: ✅ فقط `Disabled/Enabled` حسب الحالة.

**سادسًا: ثوابت AJAX**
- أي Endpoint يُستدعى بـ AJAX:
  - إما نستخدم `[IgnoreAntiforgeryToken]` (لو هذا هو النمط الحالي)
  - أو نعتمد نمط موحد لإرسال AntiForgery Token (إذا قررنا تفعيله لاحقًا)
- ❌ ممنوع خلط طريقتين داخل نفس الموديول.

**سابعًا: رسائل الأخطاء للمستخدم**
- ❌ لا نعرض تفاصيل `Exception` للمستخدم النهائي.
- ✅ نرجع شكل ثابت:
  - `{ ok: false, message: "رسالة عربية واضحة" }`
- تفاصيل الخطأ تذهب للـ Logs فقط.

**ثامنًا: ثوابت الأداء**
- Endpoints الخاصة بـ `frag=body` (أو Body فقط) يجب أن ترجع HTML “خفيف” قدر الإمكان.
- نستخدم `_ts=Date.now()` في طلبات التنقل السريعة لكسر الكاش عند الحاجة.
- ❌ ممنوع تحميل بيانات ضخمة داخل الـ Body إذا لم تكن مطلوبة فورًا (Lazy Load عند الضرورة).
- **ثابت شاشات الفواتير السريعة:** بعد أي تبديل للـ Body (`innerHTML`)، لازم نستدعي `window.__ERP_INIT__()` لإعادة تهيئة الأحداث (الأوتوكومبليت/Enter/الأسهم/الفوكس) حتى لا تختفي قيم مثل السعر.
- **ثابت بناء روابط التنقل:** الاعتماد يكون على `data-base-url` + `data-id` (ولا نعتمد على `data-url` لأنه قد يصبح قديم بعد تبديل الـ Body).

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

- **LedgerPostingService.cs**
  - خدمة الترحيل/القيد المحاسبي (Posting):
    - تُستخدم لتسجيل القيود الناتجة عن المستندات (مشتريات/مبيعات/مرتجعات/خزنة).
    - أي تعديل في منطق الترحيل يُعتبر “ثابت حساس” لأنه يؤثر على الحسابات.

---

### 2.4.1 ViewModels المهمة (ثابت)

- **LoginViewModel.cs**
  - ViewModel لعملية تسجيل الدخول.
  - يحتوي على حقول الإدخال المطلوبة + التحقق من البيانات.

- **PurchaseInvoiceHeaderDto.cs**
  - DTO/VM خاص بحفظ هيدر فاتورة المشتريات عبر AJAX.
  - يُستخدم في Endpoints مثل SaveHeader/UpdateHeader لتقليل البيانات المرسلة.

---

### 2.5 طبقة الـ Data / Seed (ثابت)

---

### 2.4.1 ViewModels (ثابت)

- **LoginViewModel.cs**
  - موديل تسجيل الدخول (اسم المستخدم/كلمة المرور/تذكرني... حسب التصميم الحالي).

- **PurchaseInvoiceHeaderDto.cs**
  - DTO خاص بهيدر فاتورة المشتريات يُستخدم مع AJAX (حفظ الهيدر بدون إعادة تحميل الصفحة).


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



- **مبدأ السرعة داخل التابات (ثابت جديد) — Shell ثابت + تحديث جزئي بدون Reload**

---

## ✅ ثوابت جلسة 2025-12-24 — فاتورة المشتريات (Shell ثابت + تنقل سريع + تحديث التاب)

> هذه ثوابت سنستخدمها لاحقًا في باقي الفواتير (المبيعات/المرتجعات/التحويلات…)، ولا تتغير إلا بتعديل تصميم البرنامج نفسه.

### A) قاعدة “Shell ثابت + Body متغير” داخل التاب (IFRAME)

- **الهدف:** سرعة + عدم وميض/تأخر ظهور الأزرار + عدم إعادة تحميل iframe بالكامل.
- **التطبيق القياسي داخل أي شاشة فاتورة:**
  1) **شريط الأزرار الجانبي** (Actions Side Card) يكون **خارج** الجزء المتغير.
  2) الجزء المتغير فقط يوضع داخل عنصر ثابت مثل: `#PI_BodyHost`.
  3) عند التنقل (أول/سابق/التالي/آخر/بحث): نحمّل **Body فقط** بالـ `fetch()` ثم نستبدل `innerHTML` داخل `#PI_BodyHost`.
  4) بعد الاستبدال: نعيد ضبط روابط/حالة أزرار التنقل من الـ hidden inputs داخل الـ body (لأنها تتغير حسب الفاتورة).
  5) نضيف `frag=body` (أو Endpoint مخصص) ليعيد HTML المناسب للـ body فقط.
  6) لا نستخدم show/hide للأزرار (ثابت: الأزرار لا تختفي)، فقط **Disabled/Enabled** حسب الحالة.

> ✅ سبب حل “تأخر ظهور الأزرار”:  
> كان شريط الأزرار داخل الجزء الذي يتم تبديله، فكان يختفي ويظهر بعد التحميل.  
> الحل الثابت: الأزرار خارج `#PI_BodyHost`، والـ body وحده هو الذي يتبدل.

---

### B) نظام الأسهم + البحث داخل فاتورة المشتريات (بدون فتح تابات جديدة)

- **القاعدة:** اعتراض الضغط على **الأسهم** و **زر البحث** بـ  
  `document.addEventListener('click', handler, true)` *(Capture)*  
  لمنع أي handler آخر (مثل `open-same-tab`) من فتح تاب جديد.

- **IDs ثابتة في أي فاتورة تستخدم التنقل السريع:**
  - أزرار الأسهم: `btnFirstInvoice`, `btnPrevInvoice`, `btnNextInvoice`, `btnLastInvoice`
  - البحث: `NavInvoiceSearchInput`, `btnNavInvoiceSearch`

- **مصدر أرقام التنقل (IDs):** hidden inputs داخل `#PI_BodyHost` (تتحدث مع كل فاتورة):
  - `NavFirstId`, `NavLastId`, `NavPrevId`, `NavNextId`, `NavCurrentId`

- **قاعدة بناء الرابط (ثابتة لتفادي أخطاء الأسهم/السعر/الفوكس):**
  - كل الأزرار تعتمد على:
    - `data-base-url` = رابط ثابت لـ `Show` (مثلاً `id=0` + `frame=1`)
    - `data-id` = رقم الفاتورة الهدف (يتحدّث من `refreshSideNavButtons()` بعد كل تحميل body)
  - **ممنوع** الاعتماد على `data-url` القديم لأنه قد يصبح “Stale” بعد تبديل الـ body.

- **منطق البحث (زر البحث):**
  1) اقرأ الرقم من `NavInvoiceSearchInput`.
  2) خذ `data-base-url` من زر البحث.
  3) استبدل باراميتر `id` بالقيمة المكتوبة ثم استدعِ `loadInvoiceBody(url)`.

- **منطق الأسهم (أول/سابق/التالي/آخر):**
  1) خذ `data-base-url` من الزر.
  2) خذ `data-id` من نفس الزر.
  3) استبدل باراميتر `id` بالقيمة الموجودة في `data-id` ثم استدعِ `loadInvoiceBody(url)`.

- **حالة “فاتورة جديدة” (NavCurrentId = 0):**
  - السابق = آخر فاتورة
  - التالي = أول فاتورة  
  *(يتم حسابها داخل الكنترولر وتُمرَّر عبر الـ hidden inputs)*

- **بعد كل تحميل Body (الجزء المتغير فقط):**
  1) `refreshSideNavButtons()` لتحديث `data-id` + `disabled` للأزرار
  2) `setupTooltips()` مرة واحدة وبشكل آمن
  3) **الأهم:** استدعاء `window.__ERP_INIT__()` لإعادة تهيئة:
     - أوتوكومبليت المورد/الصنف
     - ربط أحداث Enter/الأسهم
     - إظهار السعر وتهيئة الحقول
     - قواعد الـ Focus (فتح فاتورة جديدة ⇒ فوكس المورد / فاتورة محفوظة ⇒ فوكس الصنف)

> ✅ ملاحظة مهمة (سبب “السعر لا يظهر مع الأسهم”):  
> عند تبديل `#PI_BodyHost` بـ `innerHTML` بتضيع كل الـ event listeners والتهيئة القديمة.  
> الحل الثابت: **لازم** `__ERP_INIT__()` يتنفّذ بعد أي تحميل Body (سواء سهم أو بحث).


### C) التولتيب ToolTip (مرة واحدة فقط + بدون خطف الفوكس)

- **مطلوب ثابت:** التولتيب يظهر فقط على:
  - أسهم التنقل (⏮ ◀ ▶ ⏭)
  - (اختياري) زر البحث فقط
- **ممنوع ثابت:** أي تولتيب على input البحث نفسه (يكفي placeholder).
- **حل تكرار/انتقال التولتيب:** 
  - نمنع “تثبيت” التولتيب أو بقاؤه بعد الضغط.  
  - الأفضل تهيئة Bootstrap Tooltip بخيار `trigger: 'hover'` فقط (بدون focus) أو التخلص من `title` في العناصر التي لا نريدها.

---

### D) زر “تحديث التاب النشط” (Refresh Active Tab)

- **الهدف:** بعد عمليات مثل ترحيل فاتورة، نحتاج تحديث التاب الحالي (أو التاب النشط) بدون إعادة تحميل الصفحة الرئيسية.
- **الثابت:** زر في الـ Navbar أو داخل شريط التابات (مثال: `#btnRefreshActiveTab`) يقوم بتحديث iframe الخاص بالتاب النشط.
- **ملاحظة مهمة:** لا نلمس منطق التابات الأساسي في `site.js`؛ نضيف فقط جزء التحديث بشكل لا يكسّر النظام.

  - **الهدف:** منع Reload كامل للـ iframe عند التنقل/البحث، والحفاظ على شريط الأزرار ثابت دائمًا.
  - **المشكلة التي يمنعها هذا المبدأ:** اختفاء/ظهور الأزرار، وظهور شريط الأزرار متأخرًا بعد فتح الصفحة.
  - **القاعدة القياسية التي سنطبقها على كل الشاشات:**
    1. صفحة الشاشة الأساسية (Shell) مثل `Show.cshtml` تظل ثابتة داخل التاب.
       - شريط الأزرار (Actions Bar) يكون داخل الـ Shell وليس داخل الجزء المتغير.
    2. وضع كل المحتوى الذي يتغير داخل Container ثابت مثل: `#PageBody`.
    3. إنشاء Partial View للمحتوى المتغير فقط (مثال: `_PurchaseInvoiceBody.cshtml`).
    4. إنشاء Endpoint يعيد HTML للـ Partial فقط (مثال: `GET /PurchaseInvoices/Body?id=...`).
    5. أزرار (أول/سابق/التالي/آخر/بحث) تستدعي Endpoint بـ `fetch()` وتستبدل `innerHTML` للـ Container.
    6. تحديث بيانات التنقل (data-id) بعد كل تحميل جزئي حتى تظل الأسهم دقيقة.
    7. إضافة `_ts=Date.now()` لمنع الكاش في الطلبات السريعة.
  - **النتيجة المطلوبة (ثابت):** لا فلاش، لا اختفاء للأزرار، والتنقل يحدث فورًا داخل نفس التاب.
- **Lib**
  - Bootstrap (css/js) — الإطار الأساسي للـ UI.
  - jQuery + jQuery Validation.

---

## 5) حالة العمل الحالية (Dynamic — يتم تحديثها في نهاية كل جلسة)

### 5.1 آخر نسخة محفوظة (Git)

- **Project State نسخة:** v6 (تحديث 2025-12-24)

- Branch: `master`  
- آخر Commit: `e7f868c`  
- Message: `STILL WORK ON STOCK LEDGER`  
- ملاحظة: حذف ملفات `docs/xlsx/zip` كان مقصود لتنظيف الريبو.

---

### 5.2 الجزء الحالي الذي نعمل عليه

**أولاً: توحيد شاشة فاتورة المشتريات (PurchaseInvoices/Show.cshtml)**

- ✅ **تطبيق مبدأ السرعة داخل التابات (Shell + Partial بدون Reload):**
  - تحويل `Show.cshtml` إلى Shell ثابت (شريط الأزرار ثابت لا يختفي).
  - إنشاء Partial للمحتوى المتغير `_PurchaseInvoiceBody.cshtml`.
  - إضافة Endpoint `PurchaseInvoices/Body` لإرجاع الـ Partial.
  - تحويل أزرار (أول/سابق/التالي/آخر/بحث) إلى `fetch()` لتحديث `#PurchaseInvoiceBody` بدون تغيير iframe.
  - الهدف: حل مشكلة اختفاء الأزرار وتأخر ظهورها نهائيًا.


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

#### ✅ تحديث 2025-12-24 — اعتماد “نظام الأسهم + البحث + ثبات الأزرار” كمرجع

- ✅ تم اعتماد **#PI_BodyHost** كحاوية للـ Body المتغير فقط داخل `PurchaseInvoices/Show.cshtml`.
- ✅ شريط الأزرار الجانبي أصبح خارج الـ Body المتغير ⇒ لا تأخير/لا وميض أثناء التنقل.
- ✅ الأسهم + البحث أصبحوا يعملون عبر `fetch()` لتبديل الـ Body فقط داخل نفس التاب (بدون فتح تاب جديد).
- ✅ تم حل مشكلة: (السابق/التالي يرجع فاضي أو يذهب لآخر فاتورة فقط) عن طريق تحديث روابط الأزرار بعد كل تحميل Body (`refreshSideNavButtons`).
- ✅ تم اعتماد قاعدة Tooltips: على الأسهم فقط (والبحث اختياري)، وإزالة Tooltip من input البحث والاكتفاء بـ placeholder.
- ✅ تم إضافة زر “تحديث التاب النشط” لتحديث iframe الحالي بعد عمليات مثل الترحيل، بدون كسر نظام التابات.


#### تحديث 2025-12-22 — شريط أزرار فاتورة المشتريات (PurchaseInvoices/Show.cshtml)

- ✅ تم نقل الأزرار لتكون **جانبية يمين** وتكون **Sticky** أثناء السكرول.
- ✅ تم إزالة الـ Tooltips من الأزرار الجانبية، مع الإبقاء عليها **فقط** على:
  - أسهم التنقل (أول/سابق/التالي/آخر)
  - مربع البحث وزر البحث
- ✅ تم حل أولويات الجلسة السابقة بدون كسر الوظائف الحالية.

#### تحديث 2025-12-23 — مشكلة تحديث التابات بعد عمليات (مثل الترحيل)

- عند وجود Tab مفتوح (مثل: **دفتر الأستاذ**) ثم تنفيذ عملية تؤثر على بياناته (مثل ترحيل فاتورة)،  
  التاب لا يقوم بعمل Refresh تلقائيًا.
- المطلوب: إضافة **زر تحديث داخل كل Tab** (بدون إغلاق/فتح) أو إضافة آلية Refresh للتاب من `site.js`.
- هذا سيتم تنفيذه كمرحلة مستقلة حتى نحافظ على ثبات النظام الحالي.

1. **تقرير الخصم المرجّح والمخزون الحالي:**
   - لم يتم بعد إنشاء شاشة/تقرير مخصص لحساب وعرض الخصم المرجّح على الكمية المتبقية لكل صنف/تشغيلة/مخزن.
   - هذا التقرير سيعتمد بالكامل على بيانات `StockLedger` + الـ FIFO + أعمدة السعر والتكلفة المذكورة أعلاه.

---

#### تحديث 2025-12-24 — (استكمال) بيانات المورد/العميل + ملاحظة بطء التنقل

- ✅ تم إصلاح عرض بيانات المورد/العميل الإضافية داخل `Show.cshtml` (الهاتف/العنوان/الحد الائتماني…).
- ✅ تم توحيد أسماء الحقول مع موديل `Customer` الصحيح:
  - `Phone1` بدلًا من `Phone`
  - `CreditLimit` بدلًا من `Credit`
  - الربط الجغرافي عبر:
    - `Customer.Governorate` / `Customer.District` / `Customer.Area`
- ⚠️ ملاحظة أداء: يوجد **بطء ملحوظ** عند التنقل بين الفواتير لأن تحميل `frag=body` ما زال يعيد HTML كبير (قد يصل لعدة MB).
  - **قاعدة ثابتة للأداء:** عند استخدام `frag=body` نرجّع Body “خفيف” قدر الإمكان (بدون تجهيزات ثقيلة/تحميل بيانات غير لازمة).
  - **الهدف القادم:** جعل Endpoint الـ Body يحمّل فقط ما نحتاجه فعليًا (وتحميل الأجزاء الثقيلة Lazy عند الطلب).

#### تحديث 2025-12-24 — زر مسح السطر (Delete Line) — المطلوب تثبيته كنمط قياسي

- ✅ تم إضافة زر مسح في جدول السطور:
  - زر يحمل `data-line-no` ويستدعي AJAX على أكشن `RemoveLine`.
- ❗ المشكلة الحالية: المسح يتم على جدول السطور فقط، **بدون**:
  1) تحديث إجماليات الفاتورة (Header Totals) في جدول `PurchaseInvoices`.
  2) مسح/عكس قيد المخزن في `StockLedger`.
  3) تحديث `RemainingQty` وخرائط FIFO عند وجود ربط.
  4) تحديث بيانات التشغيلات/الرصيد السريع إن كان موجود (مثل `Stock_Batches` أو تحديث Batch balances).
- ✅ **القاعدة القياسية التي سنطبقها على أي مستند (شراء/بيع/مرتجع/تحويل):**
  - حذف/تعديل سطر = معاملة واحدة Transaction تشمل:
    - حذف السطر
    - إعادة حساب الإجماليات (DocumentTotalsService)
    - تحديث المخزن (StockLedger/StockFifoMap/Stock_Batches) حسب نوع السطر
    - تسجيل نشاط المستخدم (UserActivityLog)
  - أي فشل في خطوة → Rollback وعودة رسالة خطأ واضحة.

### 5.7 ترحيل فاتورة المشتريات (Posting) + قفل التعديل بعد الترحيل (ثابت)

> الهدف: عند الضغط على **ترحيل الفاتورة** يتم:
> 1) إنشاء قيود في **LedgerEntries** مرتبطة بالفاتورة  
> 2) تحديث **رصيد الحساب (CurrentBalance)** للطرف (Account)  
> 3) تغيير حالة الفاتورة إلى **مُرحَّلة IsPosted = true** (وتصبح “مغلقة للتعديل”)  
> 4) **لا نخفي أي زر** — نُعطّل فقط الأزرار/الخانات المعلَّمة (Disabled/ReadOnly)

---

### (أ) Backend — أكشن الترحيل في PurchaseInvoicesController

**اسم الأكشن:** `PostInvoice(int id)`  
**سلوكه:**
- يتحقق من وجود الفاتورة.
- يمنع الترحيل لو الفاتورة مُرحّلة بالفعل.
- يستدعي خدمة الترحيل:  
  `await _ledgerPostingService.PostPurchaseInvoiceAsync(invoice.PIId, User.Identity?.Name);`
- يسجل ActivityLog لنوع العملية: `UserActionType.Post`
- يرجّع JSON لو الطلب AJAX (الواجهة بتستخدم fetch).

> ملاحظة: أكشن الترحيل لازم يحدّث حقول الحالة في الفاتورة مثل:  
> `IsPosted`, `Status`, `PostedAt`, `PostedBy` (حسب الموجود عندك في المودل).

---

### (ب) Backend — فكرة LedgerPostingService (المعتمد حاليًا)

**الفكرة الأساسية:**
- قيود LedgerEntries تُنشأ “مرة واحدة” لكل عملية ترحيل.
- تُربط القيود بالفاتورة عن طريق: `SourceType = "PurchaseInvoice"` و `SourceId = PIId` (أو أي تسمية ثابتة اعتمدناها في جدول LedgerEntries).
- يتم **تحديث CurrentBalance** في حساب الطرف (Account) بمبلغ الفاتورة.

#### لماذا CurrentBalance؟
- لأن الرصيد “مخزّن” في الحساب لتسريع عرض “حجم تعامل عميل / Supplier Balance” بدون إعادة تجميع كل القيود في كل مرة.
- لذلك عند “فتح الفاتورة لاحقًا” (Unpost/Reopen) لازم نعمل العكس:
  - حذف قيود Ledger الخاصة بالفاتورة
  - عكس تأثير الرصيد: `CurrentBalance -= amount`
  - `IsPosted = false`

> مهم: التحديث ده **ليس محاسبة من القيود** — ده “Cache” سريع لازم يبقى له عكس عند فك الترحيل.

---

### (ج) Frontend — زر الترحيل (JS) بدون Reload

الـ JS الحالي يعمل الآتي:
- يمنع الضغط مرتين
- Confirm قبل الترحيل
- ينادي `/PurchaseInvoices/PostInvoice?id=...` كـ POST
- يحدّث:
  - نص زر الترحيل
  - خانة “مُرحّلة؟” (`#InvoiceIsPostedDisplay`)
  - رسالة نجاح/خطأ

> هذا الجزء **لا يطبّق قفل التعديل على باقي الأزرار/الخانات** — سنطبّقه بالثابت القادم (د).

---

### (د) ثابت “قفل التعديل بعد الترحيل” (بدون تغيير IDs)

#### 1) كيف نعلّم العناصر التي تُقفل بعد الترحيل؟
بدون ما نغيّر أي `id` (لأن الـ JS يعتمد عليه)، نضيف Attribute إضافي فقط:

- **للأزرار** (إضافة/مسح/حفظ سطور… إلخ):
```html
data-lock-posted="1"
```

مثال زر مسح سطر:
```html
<button type="button"
        class="btn btn-danger btn-sm btnRemoveLineFromInvoice"
        data-line-no="@line.LineNo"
        data-lock-posted="1">مسح</button>
```

- **للـ Inputs/Selects** التي تريد منع الكتابة فيها بعد الترحيل:
```html
data-lock-posted="1"
```

مثال:
```html
<input id="ProdNameInput" class="form-control" data-lock-posted="1" />
<select id="WarehouseId" class="form-select" data-lock-posted="1"></select>
```

> ملاحظة: أنت بالفعل بدأت تضيف `data-lock-posted="1"` للأزرار التي تريد قفلها — ده هو المطلوب.

---

#### 2) تطبيق القفل في JS (قاعدة واحدة في Show.cshtml)

**الفكرة:** لو `@Model.IsPosted == true`  
نعطّل كل العناصر التي تحمل `data-lock-posted="1"` فقط، ونترك البحث/التنقل/فاتورة جديدة/فتح الفاتورة تعمل طبيعي.

قالب JS (يوضع داخل Show.cshtml في نفس سكشن السكربتات):

```javascript
// =========================================================
// ثابت: قفل عناصر التعديل بعد الترحيل (بدون إخفاء أزرار)
// =========================================================
function applyPostedLock(isPosted) {

    const items = document.querySelectorAll('[data-lock-posted="1"]');

    items.forEach(el => {

        // أزرار
        if (el.tagName === 'BUTTON') {
            el.disabled = !!isPosted;
            return;
        }

        // Select
        if (el.tagName === 'SELECT') {
            el.disabled = !!isPosted;
            return;
        }

        // Inputs/TextAreas
        if (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA') {

            // readonly أفضل للمظهر (القيمة تظل ظاهرة)
            el.readOnly = !!isPosted;

            // لو نوع لا يحترم readonly (زي checkbox/radio/file) نستخدم disabled
            const t = (el.getAttribute('type') || '').toLowerCase();
            if (t === 'checkbox' || t === 'radio' || t === 'file') {
                el.disabled = !!isPosted;
            }

            // لمسة شكلية بسيطة (اختياري)
            if (isPosted) {
                el.classList.add('bg-light', 'text-muted');
            } else {
                el.classList.remove('bg-light', 'text-muted');
            }
        }
    });
}
```

ثم نستدعيها:
- عند تحميل الصفحة:
```javascript
document.addEventListener("DOMContentLoaded", function () {
    const isPosted = @Model.IsPosted.ToString().ToLower() === "true";
    applyPostedLock(isPosted);
});
```

- وبعد نجاح زر الترحيل (نفس مكان نجاح الترحيل في كود btnPost):
```javascript
applyPostedLock(true);
```

- وبعد نجاح زر فتح الفاتورة (عند تنفيذ Unpost/Reopen لاحقًا):
```javascript
applyPostedLock(false);
```

> مبدأ ثابت: **لا نخفي الأزرار** — نخليها موجودة لكن Disabled / ReadOnly.

---

### (هـ) Backend Guard — رسالة واضحة بدل “حدث خطأ ما”

أي أكشن يغيّر بيانات الفاتورة لازم يبدأ بالتحقق:
- لو الفاتورة مُرحّلة: رجّع رسالة واضحة للـ JS:

**نص الرسالة المعتمد:**
> "هذه الفاتورة مُرحَّلة (مغلقة) ولا يسمح بالتعديل. استخدم زر (فتح الفاتورة) أولاً."

**مثال مكانه (في AddLineJson / RemoveLineJson / SaveTaxJson / SaveHeader...):**
```csharp
if (invoice.IsPosted)
    return BadRequest(new { ok = false, message = "هذه الفاتورة مُرحَّلة (مغلقة) ولا يسمح بالتعديل. استخدم زر (فتح الفاتورة) أولاً." });
```

وبالتالي الـ JS يقرأ `data.message` ويعرضه بدل رسالة عامة.

---

### (و) ملاحظة مهمة — “ترحيل ثاني بعد فتح وتعديل”

المخطط الذي اتفقنا عليه (وسننهيه في مرحلة لاحقة):
- أول ترحيل: قيود جديدة + `IsPosted=true`
- زر فتح الفاتورة: **إلغاء الترحيل** (حذف قيود الفاتورة + عكس `CurrentBalance` + `IsPosted=false`)
- بعد أي تعديل ثم ترحيل مرة أخرى: يتم إنشاء قيود جديدة من جديد  
ويمكن لاحقًا إضافة رقم مرحلة مثل: `مرحلة 1 / مرحلة 2 ...` لتمييز عدد مرات التعديل بعد الترحيل.


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

---

## 🔖 ملخص نظام التابات (Tabs System) — تحديث جلسة 2025-12-21

### الفكرة الأساسية
- جميع الشاشات الرئيسية (مثل: فاتورة المشتريات، حركة الصنف) تُفتح داخل **تاب واحد ثابت**.
- إذا كان التاب مفتوحًا بالفعل:
  - يتم **إعادة استخدام نفس التاب** وتحديث محتواه.
- إذا لم يكن التاب موجودًا:
  - يتم **إنشاء تاب جديد**.

---

### المكان الصحيح للتعديل
- ✅ **جميع تعديلات منطق التابات تكون داخل:**
  - `Views/Shared/_Layout.cshtml`
  - `wwwroot/js/site.js`
- ❌ **لا يتم وضع منطق التابات داخل صفحات Show أو Index الخاصة بالموديولات.**

---

### القاعدة المعتمدة للأزرار
- أي زر يفتح شاشة داخل تاب يجب أن يحتوي على:
  - `data-tab-id`  ➜ معرف ثابت للشاشة (بدون أرقام)
  - `data-tab-title` ➜ عنوان التاب
  - `data-url` أو `href` ➜ رابط الأكشن مع `frame=1`

#### مثال فاتورة المشتريات (المعتمد)
```html
<a class="btn btn-erp btn-erp-outline-success btn-erp-sm open-same-tab"
   data-tab-id="pi-show-tab"
   data-tab-title="فاتورة المشتريات"
   data-url="@Url.Action("Create", "PurchaseInvoices", new { frame = 1 })">
    ➕ فاتورة جديدة
</a>
```

---

### ما الذي تم إصلاحه في هذه الجلسة؟
- ✔️ المشكلة لم تكن في زر داخل صفحة المشتريات.
- ✔️ كان الزر الخاطئ موجودًا في **الـ Navbar داخل `_Layout.cshtml`**.
- ✔️ بعد توحيد `data-tab-id` وإضافة `frame=1`:
  - فتح فاتورة جديدة من الناف بار يعمل في نفس التاب حتى لو كانت فاتورة أخرى مفتوحة.
- ✔️ نفس النظام طُبق بنجاح على:
  - عرض فاتورة مشتريات من القوائم.
  - حركة الصنف (Product Movement).

---

### قاعدة ذهبية (ثابتة للمشروع)
> **أي شاشة لها تاب ثابت → التعديل دائمًا في الـ Layout أو site.js  
> وليس داخل صفحة الشاشة نفسها.**

---

# تحديث جلسة 2025-12-26 — المشتريات + StockBatch (رصيد التشغيلات السريع)

## الهدف
- الحفاظ على **السرعة** بدون التضحية بالدقة.
- جعل التعامل مع التشغيلات (BatchNo + Expiry + سعر الجمهور للتشغيلة) واضح داخل النظام.
- اعتماد جدول **StockBatch** كـ “رصيد سريع” (Snapshot) بجانب **StockLedger** كمصدر الحقيقة.

---

## أولاً: ما اتفقنا عليه في الحذف (ثوابت البرنامج)

### 1) حذف سطر من فاتورة مشتريات (PILine)
عند حذف سطر:
1. يتم حذف السطر من جدول `PILines`.
2. يتم حذف/عكس أثره من `StockLedger` **لنفس السطر**:
   - الربط يكون عبر:  
     `SourceType = "Purchase"`, `SourceId = PIId`, `SourceLine = LineNo`
3. يتم **عكس** الرصيد في `StockBatch` (إن كان السطر له BatchNo + Expiry):
   - `QtyOnHand -= QtyIn الخاصة بسطر الشراء`
4. حماية FIFO (شرط أمان):
   - لا نسمح بالحذف إذا كانت حركة الشراء تم استهلاك جزء منها:  
     `RemainingQty < QtyIn`  
   - لأن هذا يعني أن جزء من الكمية خرج (بيع/تحويل…)، والحذف سيكسر التاريخ.

### 2) حذف فاتورة مشتريات كاملة (PurchaseInvoice)
عند حذف الفاتورة:
- يتم حذف سطورها `PILines` (Cascade من الـ Header للـ Lines فقط).
- يتم التعامل مع `StockLedger` و `StockBatch` **برمجياً** (وليس Cascade في قاعدة البيانات) بنفس قواعد الحذف بالأعلى:
  - لكل سطر: نحذف قيود الليدجر المرتبطة به ونخصم من StockBatch.
  - مع نفس شرط الأمان (عدم السماح إذا تم استهلاك الكمية).

> ملحوظة ثابتة: **لا نعمل Cascade DB من PurchaseInvoice إلى StockLedger**  
> لأن StockLedger مصدر حركة عام (Purchase/Sales/Transfer/Adjustment…) وقد يحدث تعارض مسارات Cascade أو كسر تاريخ المخزون.

---

## ثانياً: دور StockLedger vs StockBatch vs Batch

### 1) StockLedger (مصدر الحقيقة)
- يسجل **كل حركة** دخول/خروج بسجلات متعددة.
- مناسب للحسابات، التاريخ، المراجعة، وتقارير الحركة.
- FIFO يعتمد عليه (QtyIn/RemainingQty + Expiry + TranDate …).

### 2) StockBatch (رصيد سريع “صف واحد”)
- **صف واحد فقط** لكل:
  `(WarehouseId + ProdId + BatchNo + Expiry)`
- الهدف: سرعة عرض “رصيد التشغيلات الحالي” بدون تجميع ملايين سطور الليدجر.
- يتم تحديثه أثناء العمليات:
  - شراء: `QtyOnHand += qtyDelta`
  - بيع/صرف: `QtyOnHand -= qtyOut`
  - حجز (اختياري): `QtyReserved` يزيد/ينقص حسب منطق الحجز.

**مهم:** StockBatch ليس بديل عن StockLedger  
بل “Cache/Materialized Balance” لتسريع الشاشات والاستعلامات.

### 3) Batch (ماستر التشغيلات)
- يحتفظ بخصائص التشغيله (BatchNo/Expiry/سعر الجمهور للتشغيلة/تكلفة افتراضية…).
- وجوده مفيد لأن نفس التشغيله قد تظهر في أكثر من فاتورة شراء (نفس BatchNo/Expiry للصنف).
- **لا نحذف Batch عادةً** حتى لو الرصيد صفر (للتاريخ)، ويمكن استخدام `IsActive` بدل الحذف.
- الرصيد الحقيقي لا يؤخذ من Batch؛ الرصيد يؤخذ من StockBatch/StockLedger.

---

## ثالثاً: قرار “سعر الجمهور للتشغيلة عند البيع”
- لأن السوق فعلاً فيه أكثر من سعر للصنف، ولأنك تريد بيع “كل تشغيلة بثمنها”،
  فإن سعر الجمهور الذي يُستخدم عند البيع يجب أن يكون مرتبطًا بالتشغيلة.
- المصدر المقترح عمليًا:
  - **من StockBatch** (لأنه يمثل الرصيد الحالي للتشغيلة داخل مخزن محدد ويُسهل اختيار التشغيلة).
  - أو من Batch كمرجع “ماستر” (لكن StockBatch هو الأسرع للشاشات).

> القاعدة: **السعر داخل جدول Products لا يتم تغييره مع كل تغيير سوقي**  
> بل البيع يعتمد على “سعر التشغيلة” (Batch/StockBatch).

---

## رابعاً: ثوابت قاعدة البيانات (OnModelCreating) لجدول StockBatch
- اسم DbSet: `StockBatches`
- اسم الجدول: `StockBatches` (كما في الموديل).
- Unique Index:
  - `(WarehouseId, ProdId, BatchNo, Expiry)` Unique
- Defaults:
  - `QtyOnHand` يبدأ 0
  - `QtyReserved` يبدأ 0
- Index إضافي:
  - `(WarehouseId, ProdId)` لتسريع البحث.

---

## خامساً: قاعدة التحديث بدون تكرار صفوف (Upsert)
- في عمليات الشراء/البيع:
  - نبحث عن صف StockBatch بنفس (WarehouseId, ProdId, BatchNo, Expiry)
  - لو موجود: نعدل الكمية فقط.
  - لو غير موجود: ننشئ صف جديد.

---

## سادساً: ملاحظة مهمة قبل التجربة
- لو ظهرت أخطاء “does not exist in the current context” فهذا معناه أنك وضعت جزء كود
  **خارج مكان تعريف المتغيرات** (invoice/batchNo/expDate…).
- الحل: أي إضافة تخص StockBatch يجب أن تأتي **بعد** حساب:
  - `batchNo` و `expDate`
  - وبعد تحميل `invoice`
  - وبعد تحديد `qtyDelta` و `affectedLine`

---

---

## ✅ ثوابت “الترحيل + الفتح” لفاتورة المشتريات (PurchaseInvoice)

> الهدف: نفس سلوك البرنامج الذي اتفقنا عليه:
> - **الترحيل** يقفل الواجهة ويكتب الحالة في الفاتورة (Stage/مرحلة).
> - **زر الفتح** يسمح بالتعديل (Unlock) ويعيد فتح الواجهة مؤقتًا.
> - **الترحيل التالي** (بعد الفتح) يعمل:
>   1) عكس قيود المرحلة السابقة (قيدين عكسيين).
>   2) ترحيل جديد (قيدين طبيعيين) بمرحلة جديدة.
> - **المخزون** يسجل طبيعي عند الإضافة/الحذف أثناء فترة الفتح.
> - **الحسابات** هي التي يتم “عكسها ثم إعادة ترحيلها” عند أي ترحيل جديد بعد فتح.

### (A) أعمدة/حقول نعتمد عليها

- **PurchaseInvoices**
  - `IsPosted` : فلاج القفل (true/false)
  - `Status` : نص الحالة الظاهر في الواجهة والقوائم  
    **المعيار**:  
    - قبل أي ترحيل: `"Draft"` (أو أي نص عندك)  
    - بعد الترحيل الأول: `"مرحلة 1"`  
    - بعد الترحيل الثاني: `"مرحلة 2"`  
    - … وهكذا
  - `PostedAt`, `PostedBy` : بيانات آخر ترحيل (عرض/تتبع)
  - (اختياري) `OpenedAt`, `OpenedBy` : لو أحببت تسجيل عملية الفتح كتتبع إضافي.

- **LedgerEntries**
  - `SourceType` : نوع المصدر (PurchaseInvoice)
  - `SourceId` : رقم الفاتورة
  - `PostVersion` : رقم المرحلة المرتبطة بالقيد (1/2/3…)
  - `LineNo` : رقم ترتيب/تمييز سطر القيد (ممكن استخدام 1/2 للقيود الطبيعية و9001/9002 للعكس)

> ✅ **ملاحظة مهمة**:  
> لا نعتمد على `VoucherNo` في الترقيم (لأننا ألغينا جدول الترقيم)،
> ونكتفي بـ `SourceType + SourceId + PostVersion` للتمييز.

### (B) قاعدة حساب رقم المرحلة الجديدة (PostVersion)

المرحلة الجديدة تأتي من **حالة الفاتورة نفسها** وليس من LedgerEntries:

- اقرأ `invoice.Status`
- استخرج منها رقم المرحلة إن وجد (`مرحلة X`)
- إن لم توجد مرحلة → اعتبرها 0
- **newStage = oldStage + 1**

بالتالي:
- أول ترحيل: `newStage = 1`
- بعد فتح ثم ترحيل: `newStage = 2`
- بعد فتح ثم ترحيل مرة أخرى: `newStage = 3`
- … إلخ

### (C) الترحيل: قواعد “عكس ثم ترحيل”

**لو الفاتورة لم تكن مترحلة (IsPosted = false):**
- نعمل ترحيل طبيعي فقط (قيدين)
- ثم نحدث:
  - `invoice.IsPosted = true`
  - `invoice.Status = $"مرحلة {newStage}"`
  - `invoice.PostedAt/PostedBy`

**لو الفاتورة كانت مترحلة ثم تم فتحها ثم نرحل مرة أخرى:**
- نحدد المرحلة القديمة `lastStage` من `invoice.Status` (قبل أن نغيرها)
- نجيب **قيدين المرحلة القديمة** من `LedgerEntries` (SourceType+SourceId+PostVersion=lastStage)
- ننشئ **قيدين عكسيين** (Swap Debit/Credit) مع:
  - `PostVersion = lastStage`
  - `LineNo = 9001/9002` (أرقام عالية للتمييز)
  - `Description = "عكس ترحيل..." (عكس مرحلة X)`
- ثم ننشئ **قيدين طبيعيين** للمرحلة الجديدة مع:
  - `PostVersion = newStage`
  - `LineNo = 1/2`
  - `Description = "ترحيل فاتورة..." (مرحلة newStage)`
- ثم نحدث الفاتورة بنفس سطر الحالة أعلاه.

### (D) زر الفتح: قواعد القفل/الفتح في UI

- زر الفتح يظهر/يعمل فقط عندما:
  - `IsPosted = true` (يعني الفاتورة مقفولة)
- عند نجاح فتح الفاتورة:
  - نعمل `invoice.IsPosted = false`
  - ونكتب `invoice.Status = "مفتوحة للتعديل"` (أو أي نص ثابت اتفقنا عليه)
  - **زر الفتح يتقفل** ويظهر رسالة نجاح
  - **زر الترحيل يتفعل**
  - الواجهة تفك القفل (بدون اهتمام بالفوكس حسب طلبك)

> ✅ **مهم**:  
> “الفتح” يغيّر الحالة **لحالة واحدة فقط**: `"مفتوحة للتعديل"`  
> أما “الترحيل” هو الذي يكتب `"مرحلة 1/2/3..."` حسب عدد المرات.

### (E) قاعدة عرض “المرحلة” في UI

- **بوكس العرض داخل Show** (InvoiceIsPostedDisplay) لا نكتب فيه يدويًا “نعم/لا”.
- نخليه يعرض `invoice.Status` أو `postedLabel` القادم من السيرفر.
- القفل الحقيقي يعتمد فقط على `IsPosted`.

### (F) AJAX: قراءة الخطأ مرة واحدة

ثابت مهم لتجنب خطأ:
`execute 'text' on 'Response': body stream already read`

- عند فشل fetch:
  - اقرأ `res.text()` مرة واحدة
  - حاول تفك JSON إن أمكن
  - رجّع message

### (G) ملاحظات ثابتة (UI Lock)

- الاعتماد الأساسي للقفل:
  - `data-is-posted="1/0"` (Flag عام)
  - و `data-lock-posted="1"` على العناصر التي يجب قفلها عند الترحيل
- **زر الفتح** لا يدخل في عناصر القفل (لا نضع عليه data-lock-posted)
- بعد نجاح الترحيل يجب **تفعيل زر الفتح** (لأنه أصبح له معنى)

---

## ✅ مشكلة “مسح صنف” (تم حلها)

- تم إصلاح منطق مسح سطر الصنف بحيث:
  - لا تتكرر السطور في العرض
  - لا تختفي أزرار/أماكنها
  - الحذف يعمل بدون كسر إجماليات أو تعطيل UI

> (لو حبيت لاحقًا نضيف ملخص “أين كان الخطأ بالضبط” نكتبه هنا بس بعد ما ترسل جزء الكود النهائي للحذف.)
