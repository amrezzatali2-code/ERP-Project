# ERP — PROJECT STATE (حالة المشروع)

> **طريقة الاستخدام (ثابتة):**
> - في **بداية الشغل**: افتح هذا الملف واقرأ قسم **(A) ثوابت المشروع** ثم **(C) آخر نقطة توقف**.
> - في **نهاية الشغل**: حدّث فقط قسم **(C/D/E/F/I/J)** (الحالة + تفاصيل الجزء الجاري + سجل التعديلات + القادم).
> - **ممنوع** تغيير قسم **(A) ثوابت المشروع** إلا بقرار صريح لأنّه مرجع أساسي لكل الشغل.

---

## A) ثوابت المشروع (لا تتغير إلا بقرار)

### A.1 الهدف العام
- بناء نظام **ERP عربي RTL** قابل للبيع لشركات توزيع الأدوية.
- التركيز الأساسي: **المبيعات + المشتريات + المخزون + الخزينة/الحسابات** مع **نظام صلاحيات** و**سجل نشاط المستخدم**.
- الأولوية الحالية في التطوير: **السرعة في التشغيل** + **بحث سريع بالأوتوكمبليت** + **نظام القوائم الموحد**.

### A.2 التقنيات
- ASP.NET Core MVC (C#)
- EF Core + Migrations
- SQL Server (محلي/سيرفر حسب إعدادات الاتصال)
- واجهات Bootstrap (مع CSS مخصص للـ ERP) + JavaScript (AJAX عند اللزوم)

### A.3 قواعد قاعدة البيانات (DB Rules)
- **كل مفاتيح Id = int** (PK/FK) + Identity حيث يلزم.
- حقول التتبع القياسية في أغلب الجداول:
  - `CreatedAt`, `UpdatedAt` (UTC)
  - (عند اللزوم) `CreatedBy`, `UpdatedBy`, `IsActive`
- **الأعمدة في قاعدة البيانات باللغة الإنجليزية** (قاعدة ثابتة)، والعربي يكون في UI فقط.
- القيم المالية `decimal` مع Precision ثابت (حسب ما تم اعتماده في المشروع).

### A.4 قواعد المستندات (Documents Rules)
- عند الضغط على **حفظ مستند جديد** (خصوصًا فواتير البيع/الشراء):
  يتم اعتماد رقم المستند وتحديث تأثيره على المخزون/الحسابات فورًا، مع السماح بالتعديل بعد ذلك حسب تصميم الشاشة.
- زر **الترحيل/الغلق** (Posting/Close): يقفل المستند نهائيًا، ولا يُفتح إلا بصلاحية خاصة.

### A.5 قواعد المخزون (Stock Rules)
- طريقة السحب الأساسية: **FIFO حسب Batch + Expiry**.
- السماح باستثناءات للأدوية (قد يكون نفس الصنف بسعر بيع مختلف للصيدلي):
  يتم البيع على **سطرين** أو أكثر بسعرين مختلفين حسب الحاجة، مع الحفاظ على منطق السحب من المخزون.
- دفتر الحركة المخزنية:
  - الجدول المرجعي للحركات: `StockLedger`
  - الربط (اختياري/مساعد): `StockFifoMap` (out ↔ in)
  - جدول سريع للتجميع (مخطط/قيد التنفيذ حسب المرحلة): `Stock_Batches`

### A.6 قواعد الواجهة (UI Rules)
- عربي RTL + خطوط وتنسيقات ERP.
- **نظام القوائم الموحد** هو النمط القياسي لأي شاشة قائمة (Index):
  - بحث + ترتيب + Paging عبر `PagedResult`
  - فلترة تاريخ/وقت + من كود/إلى كود
  - اختيار أعمدة + طباعة + تصدير
  - BulkDelete + DeleteAll
  - Sticky header
  - إجمالي السجلات على مستوى البيانات المفلترة (مش الصفحة فقط)
- نظام التابات في الـ Layout:
  - فتح الصفحات داخل Tabs باستخدام `site.js`.

### A.7 التتبع وسجل النشاط (Logging Rules)
- أي عملية مهمة (Create/Edit/Delete/Post/Import/...) يجب أن تسجل في `UserActivityLog`.
- تذكير ثابت: **لا ننسى LogActivity** عند بناء أي جزء مناسب.

---

## B) خريطة المكونات الرئيسية (Architecture Map)

> الهدف من هذا القسم: لما أقرأه أتذكر بسرعة “إيه موجود في المشروع” و“إيه دوره”.

### B.1 طبقة البيانات
- `AppDbContext`:
  يحتوي DbSets لكل الجداول + إعدادات الـ Precision والعلاقات.

### B.2 أهم الدومينات (Domains)

#### (1) Master Data
- Products / Categories
- Customers / Accounts (مع PartyCategory)
- Warehouses / Batches
- Governorates / Districts / Areas / Branches
- DocumentSeries

#### (2) Documents (Headers + Lines)
- PurchaseInvoices + PILines
- PurchaseRequests + PRLines
- PurchaseReturns + PurchaseReturnLines
- SalesInvoices + SalesInvoiceLines
- SalesOrders + SOLines
- SalesReturns + SalesReturnLines

#### (3) Stock
- StockLedger (الحركات)
- StockFifoMap (ربط FIFO)
- Batches (+ PriceRetailBatch / UnitCostDefault)
- Stock_Batches (مخطط/قيد التطوير حسب المرحلة)

#### (4) Security
- Users / Roles / Permissions
- UserRoles / RolePermissions
- UserDeniedPermissions
- UserActivityLogs

### B.3 الكنترولرات (Controllers) — أمثلة مهمة
> (ليست قائمة حصرية، لكنها أهم ما نرجع له غالبًا)
- ProductsController
- CustomersController
- PurchaseInvoicesController
- PILinesController
- StockLedgerController
- StockFifoMapController
- AccountsController
- GovernoratesController / DistrictsController / AreasController / BranchesController
- RolesController / PermissionsController / UserActivityLogsController
- (عند اللزوم) CashReceiptsController / CashPaymentsController

### B.4 الخدمات (Services) — الثابت + المخطط
- **DocumentTotalsService**:
  مسؤول عن إعادة حساب إجماليات الهيدر من السطور (Purchase/Sales/Returns/Orders/Requests).
- **(مخطط/قيد التنفيذ)** `StockAnalysisService`:
  هدفه: أي حسابات تحليلية/تقريرية مرتبطة بالمخزون من StockLedger (مثل الخصم المرجح/تحليل فترة/...).
  **مهم:** لا نخلط منطق “إجماليات المستندات” داخل DocumentTotalsService مع منطق “تحليل المخزون”.

### B.5 الفيوهات (Views) والبارشال (Partials)
- `_Layout.cshtml` بنظام Tabs.
- البارشال القياسي للفلاتر: `_IndexFilters` (ضمن نظام القوائم الموحد).
- PurchaseInvoices/Show.cshtml: شاشة فاتورة المشتريات (هيدر + كارت إضافة صنف + جدول السطور + إجماليات).

### B.6 ملفات الـ JS/CSS المهمة
- `wwwroot/js/site.js` (Tabs + سلوك عام)
- `wwwroot/js/tables-init.js` (Helpers فقط، بدون auto-init)
- (قد توجد ملفات شاشة معينة مثل import.js حسب الجزء)

---

## C) آخر نسخة محفوظة (Git) + آخر تحديث

- Branch: `master`
- آخر Commit (حسب الملف كمثال سابق): `e7f868c`
- Message: `STILL WORK ON STOCK LEDGER`
- تاريخ آخر تحديث ملف الحالة: **2025-12-21 (أفريقيا/القاهرة)**

---

## D) الجزء الحالي الذي نعمل عليه

### D.1 دفتر الحركة المخزنية (StockLedger) — صفحة Index
**الهدف الحالي:**
إظهار أعمدة مالية واضحة ومفيدة (قادمة من مستندات الشراء/البيع) + إضافة أزرار إجراءات لكل سطر **بدون كسر حفظ الفواتير** أو أي شاشة.

### D.2 ملاحظة مهمة من آخر جلسة
- حصل خطأ بعد تعديل سابق في جزء StockLedger (أثّر على حفظ الفواتير)، وتم التراجع عنه فعاد الحفظ للعمل.
**القاعدة:** أي تعديل في StockLedger لازم يكون معزول (Query/Display فقط) ولا يلمس منطق حفظ المستندات.

### D.3 مشكلة واجهة فتح فاتورة الشراء من StockLedger
- عند فتح فاتورة الشراء من StockLedger، شكل جدول السطور يظهر مختلف عن “فتح فاتورة جديدة”.
- نخشى أن يكون السبب UI/JS (render) وليس مصدر البيانات.
- المطلوب: التأكد أن فتح الفاتورة يعتمد على **PIId** فقط وأن تحميل السطور يتم بنفس المسار (نفس Action/نفس JS).

---

## E) أعمدة StockLedger المطلوبة (الجدول الحالي)

### E.1 أعمدة بيانات الصنف
1) **اسم الصنف** (ProdName)
2) **سعر الجمهور** (PriceRetail)
3) **سعر الجمهور للتشغيلة** (BatchPriceRetail = `PriceRetailBatch`) — يأتي من **فاتورة البيع** عند تفعيل جزء البيع.

### E.2 أعمدة الحسابات/الإجماليات
4) **تكلفة/وحدة** (UnitCost)
5) **إجمالي السطر** (TotalCost / LineTotal)
6) **خصم الشراء** (PurchaseDiscount)
7) **الخصم المرجح** (WeightedDiscount)

> **مصادر البيانات المتفق عليها:**
> - (مشتريات) أغلب الأعمدة المالية تأتي من **PILines / PurchaseInvoices**.
> - (مبيعات) سعر الجمهور للتشغيلة يأتي من **SalesInvoiceLines** عند إضافة جزء البيع لاحقًا.
> - الخصم المرجح: سيتم حسابه في `StockAnalysisService` اعتمادًا على StockLedger (وليس من Batch quantities).

---

## F) أزرار الإجراءات في StockLedger (لكل سطر)
1) **زر: حركة الصنف**
   - يفتح تقرير/شاشة حركة الصنف لنفس `ProdId` (مع فلترة تلقائية).
2) **زر: فتح المستند**
   - يعتمد على `SourceType` + `SourceId`:
     - Purchase → PurchaseInvoices/Show (PIId)
     - Sales → SalesInvoices/Show (SIId)
     - (تحويل/تسوية/افتتاحي لاحقًا)

---

## G) قاعدة حساب “الخصم المرجح” (تصميم مبدئي)
> الهدف: يكون تقرير الخصم المرجح سريع حتى لو عندنا 1000 عملية شراء للصنف.

- **المبدأ:** التقرير يقرأ من `StockLedger` لأنه سجل الحركات الفعلي.
- **التحسين:** لا نحسب من الصفر لكل تقرير.
  نحتاج واحد من حلين (نحدد وننفذ):
  1) **Pre-aggregation** (تجميع مُسبق)
     - جدول/View لتجميع آخر تكلفة/متوسط/خصم مرجح لكل ProdId (اختياري).
  2) **Query محسّن**
     - Indexes مناسبة + فلترة تاريخ + حسابات على مستوى محدد (شهر/فترة) بدل تاريخ كبير جدًا.

> **ملاحظة:** لأن جدول Batch لا يحتوي كميات، لا نعتمد عليه في حساب الخصم المرجح، بل نعتمد على كميات الحركات نفسها في StockLedger.

---

## H) آخر نقطة توقف (Stop Point)
- تم تعديل هيدر جدول StockLedger لإضافة الأعمدة الجديدة.
- تم إضافة الأعمدة في قاعدة البيانات/أو في العرض (حسب التنفيذ عندك) وتم إخفاء بعض الأعمدة في UI.
- **المطلوب التالي مباشرة:**
  تثبيت مصدر القيم الناقصة (PriceRetail / UnitCost / TotalCost / PurchaseDiscount) بحيث تظهر دائمًا من فاتورة الشراء (PILines).

---

## I) قائمة مهام الجلسة القادمة (Next)
1) مراجعة Query صفحة StockLedger Index:
   - Join صحيح بين StockLedger ↔ Products (ProdName).
   - القيم المالية من مصدرها الصحيح (Purchase Invoice lines عند `SourceType=Purchase`).
2) تثبيت عرض PriceRetail/UnitCost/TotalCost/PurchaseDiscount بدون حالات “يظهر/يختفي”.
3) إضافة أزرار الإجراءات (حركة الصنف + فتح المستند) بشكل نهائي.
4) إنشاء ملف Service جديد ثابت: `Services/StockAnalysisService.cs`
   - نضع فيه لاحقًا كود **WeightedDiscount** + أي تحليل للفترة.
5) تأكيد أن فتح PurchaseInvoice من StockLedger يمر بنفس مسار Show المعتاد (منع اختلاف رسم السطور).

---

## J) سجل التعديلات (Update Log)
> أضف سطر/سطرين في نهاية كل جلسة.

- 2025-12-21: توسيع PROJECT_STATE.md وإضافة ثوابت المشروع + خريطة المكونات + تثبيت هدف جزء StockLedger الحالي.
