# 📘 مرجع فاتورة المشتريات (Purchase Invoice) — توثيق عملي كامل (ERP)

**هذا الملف هو “المرجع الرسمي” لفاتورة المشتريات في مشروع ERP**:  
يوثق *كل زر + كل بوكس + كل أكشن + كل جزء في الـ JavaScript*، مع شرح المنطق “ليه عملنا كده”، علشان نرجع له لاحقًا عند بناء **فاتورة المبيعات** وباقي الشاشات.

---

## بيانات الإصدار

- **تاريخ التحديث:** 2026-01-08  

**ملاحظة مهمة (نقطة ثابتة):** تم الرجوع إلى كود الحذف القديم الذي كان يعمل بدون مشاكل (بدون الحذف العميق).
- حذف الفاتورة من القائمة يعمل ويُعيد التوجيه للـ Index بشكل طبيعي.
- حذف السطر من داخل الفاتورة يعمل كما كان (قبل إضافات الحذف العميق).
- سنضيف (الحذف العميق) للمخزون + الحسابات لاحقًا في محادثة جديدة خطوة بخطوة.
- **اسم المرجع:** `PurchaseInvoice_Full_Reference.md`  
- **المصدر الأساسي للتوثيق:** ملفات المشروع الحالية (كما وصلتني):
  - `PurchaseInvoicesController.cs`
  - `Views/PurchaseInvoices/Show.cshtml`
  - `DocumentTotalsService.cs`
  - `LedgerPostingService.cs`
  - `PurchaseInvoiceHeaderDto.cs`
  - `UserActivityLogger.cs`
  - (للتابات) `wwwroot/js/site.js`
  - (تصميم الصفحة) CSS داخل `Show.cshtml` + أي قواعد عامة في `site.css`

> ملاحظة مهمة: في `Show.cshtml` يوجد زر “مسح الصنف بالكامل” يستدعي أكشن باسم `RemoveProductLinesJson`.  
> **لم أجده داخل `PurchaseInvoicesController.cs` المرفوع هنا**.  
> إذا كان الزر يعمل عندك فالأكشن موجود في مشروعك (قد يكون ملف الكنترولر عندك أحدث من النسخة المرفوعة).  
> إذا لم يعمل: فهذه نقطة “تحقق/استكمال” لاحقًا.

---

## فهرس سريع

1) فكرة الشاشة ومبدأ السرعة  
2) الجداول التي تتأثر بالمشتريات ولماذا  
3) حالات الفاتورة (مفتوحة/مرحلة) ونظام القفل  
4) خريطة الواجهة (كل كارت وكل عنصر)  
5) جدول الـ IDs المهمة (زر/حقل/جدول)  
6) JavaScript داخل Show.cshtml — شرح الدوال سطر بسطر  
7) PurchaseInvoicesController — شرح الأكشنات  
8) DocumentTotalsService — إعادة الحساب  
9) LedgerPostingService — الترحيل (قيود)  
10) UserActivityLogger — سجل النشاط  
11) سيناريوهات اختبار سريعة  
12) مشاكل شائعة وحلولها  
13) Checklist: ما الذي سننسخه في فاتورة المبيعات بالضبط

---

# 1) فكرة الشاشة ومبدأ السرعة

فاتورة المشتريات في ERP معمولة بمبدأين ثابتين:

## (أ) السرعة قبل أي شيء (مع الدقة)
- المستخدم يضيف أصناف كثير → لازم “إضافة السطر” تكون AJAX بدون إعادة تحميل الصفحة.
- التنقل بين الفواتير بالأسهم لازم يبقى سريع → لذلك بنستخدم **تحميل Body فقط** داخل نفس الصفحة بدل فتح صفحة جديدة بالكامل.
- منع مشاكل “تكرار ربط الأحداث” (Double Binding) → نستخدم `dataset.bound...` علشان كل زر يتربط مرة واحدة فقط.

## (ب) المصدر الحقيقي للمخزون هو StockLedger
- المشتريات تسجل دخول المخزون في **StockLedger**.
- `StockBatch` (أو Stock_Batches) موجود للتسريع وعرض “كمية حالية” بسرعة.
- FIFO يعتمد على:
  - وجود Batch + Expiry
  - ووجود `RemainingQty` في سطور الدخول داخل StockLedger.

---

# 2) الجداول التي تتأثر عند الشراء ولماذا

عند إضافة/مسح سطر في فاتورة مشتريات، الجداول المتأثرة (حسب الكود الحالي):

## (1) PurchaseInvoices (الهيدر)
- يتخزن فيه بيانات الفاتورة الأساسية: المورد، المخزن، التاريخ، الحالة، الضريبة، الإجماليات… إلخ.
- يتحدث بعد أي تعديل على السطور لأن الإجماليات لازم تتغير.

## (2) PILines (سطور الفاتورة)
- كل سطر يمثل صنف/تشغيلة/صلاحية/خصم/سعر/كمية.
- يوجد منطق يمنع تكرار نفس السطر إذا كان نفس:
  - `ProdId + BatchNo + Expiry + PriceRetail + PurchaseDiscountPct`  
  في هذه الحالة يتم دمج الكميات بدل إنشاء سطر جديد.

## (3) StockLedger (دفتر الحركة المخزنية)
- عند إضافة سطر شراء: يتم عمل **حركة دخول QtyIn** وتعبئة `RemainingQty = QtyIn`.
- عند مسح سطر شراء: يتم حذف حركة الدخول بشرط أمان مهم:
  - **لا يسمح بالحذف إذا تم استهلاك جزء من الكمية** (يعني RemainingQty أصبحت أقل من QtyIn).

## (4) StockBatch (رصيد سريع للتشغيلة داخل المخزن)
- يتم زيادة `QtyOnHand` عند الشراء.
- يتم إنقاص `QtyOnHand` عند المسح.

## (5) Batches (الماستر)
- ضمان وجود سجل Master للتشغيلة + الصلاحية.
- تحديث سعر الجمهور وتكلفة الوحدة الافتراضية + تسجيل تاريخ السعر (ProductPriceHistory) عند تغيّر السعر.

## (6) ProductPriceHistory (تاريخ سعر الصنف)
- يتسجل عند اختلاف السعر الجديد عن السعر السابق:
  - `OldPrice` / `NewPrice` / `BatchNo` / `Expiry` / `CustomerId` … إلخ.

## (7) LedgerEntries (قيود محاسبية) — عند الترحيل
- الترحيل يفترض أنه يعمل قيود: المخزون/المورد… إلخ (حسب LedgerPostingService).

---

# 3) حالات الفاتورة ونظام القفل (Posted / Open)

## الحالات المنطقية في الكود
- **فاتورة جديدة:** `PIId = 0` (لسه ما اتعملش لها حفظ Header)
- **فاتورة محفوظة ومفتوحة:** `PIId > 0` و `IsPosted = false`
- **فاتورة مرحلة/مقفولة:** `IsPosted = true`

## ما معنى “القفل”؟
لما الفاتورة تكون مرحلة:
- ممنوع تعديل الهيدر (SaveHeader)
- ممنوع إضافة سطر
- ممنوع مسح سطر
- ممنوع تعديل الضريبة
- الواجهة نفسها بتتقفل (Disable/Readonly) تلقائيًا

## كيف يشتغل القفل في الواجهة؟
داخل `Show.cshtml` أي عنصر لازم يتقفل عند الترحيل نضيف له:
- `data-lock-posted="1"`

ثم JavaScript ينفذ:
- `applyPostedUiLock(isPosted)`  
  - لو `true` → يقفل كل العناصر
  - لو `false` → يرجعها لحالتها الأصلية (Enable/Readonly كما كانت)

> **ثابت مهم:** القفل لا يعتمد على “إخفاء أزرار” لأننا اتفقنا أن الأزرار لا تختفي وتظهر.  
> الأزرار تكون موجودة دائمًا، لكن Disabled عند الترحيل.

---

# 4) خريطة الواجهة داخل Show.cshtml

الصفحة متقسمة منطقيًا إلى:

1) **شريط الأزرار العلوي (Navigation + عمليات)**
2) **كارت الهيدر (بيانات الفاتورة)**
3) **كارت بيانات المورد (تفاصيل تلقائية)**
4) **كارت إضافة الصنف (إدخال السطر قبل الإضافة)**
5) **جدول السطور (مع الجروبنج)**
6) **كارت الإجماليات والضريبة**
7) **منطقة الطباعة المخفية (piPrintArea)**
8) **Body Host للتنقل السريع داخل نفس الصفحة (PI_BodyHost)**

---

# 5) جدول الـ IDs المهمة (زر/حقل/جدول)

> الهدف: لما تحتاج تعدل سلوك زر/بوكس… تعرف فورًا اسمه وإيه اللي بيقرأه.

## 5.1 عناصر حالة الفاتورة (Hidden/Display)
- `InvoiceId` : رقم الفاتورة الحالي (0 لو جديدة)
- `InvoiceStatusHidden` : نص حالة الفاتورة (مثال: "مفتوحة" / "مرحلة 1")
- `InvoiceIsPostedFlag` : 0/1 لتحديد إن كانت مرحلة
- `InvoiceNumberDisplay` : عرض رقم الفاتورة (Readonly)
- `InvoiceDateDisplay` / `InvoiceTimeDisplay` : عرض التاريخ/الوقت
- `InvoiceIsPostedDisplay` : عرض الحالة النصية (مرحلة… أو لا)
- `InvoiceCreatedByDisplay` : من أنشأ الفاتورة

## 5.2 أزرار التنقل والبحث
- `btnFirstInvoice` : أول فاتورة
- `btnPrevInvoice` : السابقة
- `btnNextInvoice` : التالية
- `btnLastInvoice` : الأخيرة
- `btnNavInvoiceSearch` : زر “بحث/اذهب لرقم فاتورة”
- `NavInvoiceNumberInput` : صندوق رقم الفاتورة للبحث

## 5.3 حفظ الهيدر والترحيل/الفتح والطباعة
- `btnSaveInvoiceHeader` : حفظ بيانات الهيدر
- `btnPostInvoice` : ترحيل الفاتورة (قفل)
- `btnOpenInvoice` : فتح الفاتورة (إلغاء القفل)
- `btnPrintInvoice` : طباعة الفاتورة

## 5.4 بيانات المورد
- `CustomerId` : hidden رقم المورد (يتحدد من الداتا ليست)
- `CustomerNameInput` : إدخال اسم المورد (datalist)
- `customersList` : datalist للموردين
- `CustomerCodeDisplay` : عرض كود المورد
- `CustomerPhone`, `CustomerAddress`
- `CustomerGovernorate`, `CustomerDistrict`, `CustomerArea`
- `CustomerCredit`

## 5.5 إدخال الصنف (قبل الإضافة)
- `ProdNameInput` : اسم الصنف (datalist)
- `productsList` : datalist للأصناف
- `ProdHiddenId` : Hidden ProdId (يتملأ من الاختيار)
- `ProdIdInput` : إدخال/بحث بالكود
- `ProdRetailInput` : سعر الجمهور
- `ProdDisc1Input` : خصم الشراء %
- `ProdQtyInput` : الكمية
- `ProdBatchInput` : رقم التشغيلة
- `ProdExpiryInput` : الصلاحية (MM/YYYY)
- `btnAddInvoiceLine` : زر إضافة سطر

## 5.6 جدول السطور
- `PILinesTbody` : tbody الذي يُعاد رسمه
- `btnRemoveLineFromInvoice` : زر مسح سطر (class + data-line-no)
- `btnRemoveProductFromInvoice` : زر مسح الصنف بالكامل (class + data-prodid)

## 5.7 الإجماليات/الضريبة
- `TaxAmountInput` : الضريبة
- `btnSaveTax` : حفظ الضريبة
- `TotalLinesValue`, `TotalItemsValue`, `TotalQtyValue`
- `TotalRetailValue`, `TotalAfterDiscountValue`
- `TaxAmountValue`, `TotalAfterDiscountAndTaxValue`, `NetTotalValue`

## 5.8 الطباعة
- `piPrintArea` : المنطقة المخفية
- `piPrintNow` : زر الطباعة داخل المنطقة
- `piPrintTitle`, `piPrintInvoiceNo`, `piPrintInvoiceDate`
- `piPrintSupplier`, `piPrintWarehouse`
- `piPrintLinesWrap`, `piPrintTotalsWrap`

---

# 6) JavaScript داخل Show.cshtml — شرح الدوال والمنطق

> هذا الجزء هو الأهم لأنه هو اللي يضمن “سرعة + ثبات” في الفاتورة.

## 6.1 ثوابت عامة داخل السكربت
- عدم استخدام jQuery: كل شيء Vanilla JS + fetch
- كل زر حساس يتم ربطه مرة واحدة:
  - `if (btn.dataset.boundX) return;`
- معظم العمليات ترجع JSON وتقوم:
  1) `renderGroupedLines(lines)`
  2) `updateSummaryBadges(totals)`
  3) `updateInvoiceStatusUi(...)` عند اللزوم

---

## 6.2 القفل (applyPostedUiLock)

### الهدف
- عندما تكون الفاتورة مرحلة:  
  **اقفل عناصر محددة فقط** (المعلم عليها بـ `data-lock-posted="1"`)

### الفكرة
- بنمشي على العناصر ذات `data-lock-posted="1"`
- قبل ما نقفلها… بنحفظ حالتها الأصلية في dataset:
  - `data-orig-disabled`
  - `data-orig-readonly`
  - `data-orig-pointer-events`
  - `data-orig-opacity`

ثم:
- لو Posted:
  - `disabled = true` للأزرار و الـ select
  - `readOnly = true` للـ inputs
  - ونقلل opacity بسيط + منع التفاعل
- لو Open:
  - نرجّعها للحالة الأصلية (عشان ما نكسرش أي عنصر كان أصلاً readonly)

> **سبب مهم:** بعض الحقول تكون readonly حتى في الفاتورة المفتوحة  
> (مثل حقول عرض رقم الفاتورة/التاريخ)، فلا ينفع “نفكها” بالغلط.

---

## 6.3 حفظ الهيدر (btnSaveInvoiceHeader)

### الهدف
- حفظ بيانات الفاتورة الأساسية (المورد + المخزن + طلب شراء مرجعي اختياري)
- لو الفاتورة جديدة (PIId=0) يتم إنشاء رقم فاتورة فعلي فورًا.

### خطوات السكربت
1) منع الضغط المتكرر بـ `busy`
2) قراءة:
   - `InvoiceId`
   - `CustomerId`
   - `WarehouseId`
   - `RefPRId` (إن وجد)
3) Validation سريع:
   - لازم مورد + مخزن
4) إرسال DTO إلى:
   - `POST PurchaseInvoices/SaveHeader`
5) عند النجاح:
   - تحديث `InvoiceId`
   - تحديث `InvoiceNumberDisplay`, `InvoiceDateDisplay`, `InvoiceTimeDisplay`
   - تحديث `InvoiceStatusHidden`
   - تحديث `InvoiceIsPostedDisplay` (يعرض status بدل نعم/لا)
   - فوكس مباشرة على `ProdNameInput` (ثابت سرعة)

---

## 6.4 إضافة سطر (btnAddInvoiceLine)

### الهدف
- إضافة سطر بسرعة بدون Reload

### خطوات منطقية
1) **لازم الفاتورة تكون محفوظة**
   - لو `InvoiceId = 0` → رسالة “احفظ الهيدر أولًا”
2) **لازم ProdId يكون موجود**
   - من `ProdHiddenId` أو من إدخال الكود
3) تجهيز DTO:
   - `PIId`
   - `prodId`
   - `qty`
   - `priceRetail`
   - `purchaseDiscountPct`
   - `BatchNo`
   - `expiryText`
4) إرسال:
   - `POST PurchaseInvoices/AddLineJson` (مع AntiForgeryToken)
5) عند النجاح:
   - تحديث جدول السطور `renderGroupedLines`
   - تحديث الإجماليات `updateSummaryBadges`
   - تنظيف المدخلات + فوكس على الصنف للسرعة

### لماذا يوجد “دمج سطور”؟
لو المستخدم ضغط إضافة مرتين بنفس البيانات:
- بدل تكرار السطر، نزيد الكمية لنفس السطر (أسرع + أقل فوضى)

---

## 6.5 رسم جدول السطور (renderGroupedLines)

### لماذا نعيد الرسم بالكامل؟
- لأن عدد السطور في الفاتورة غالبًا صغير نسبيًا.
- إعادة الرسم الكامل تمنع أخطاء تحديث جزئي (أسهل وأضمن).

### فكرة الجروبنج
- السطور تأتي من السيرفر فيها:
  - `prodId`, `prodName`, `qty`, `batchNo`, `expiry`, `disc`, `lineValue`, …
- نجمع حسب `prodId`
- لو للصنف تشغيلات متعددة → نعرض:
  - صف عنوان للصنف + زر “مسح الصنف بالكامل”
  - تحته سطور التشغيلات فقط

---

## 6.6 مسح سطر (RemoveLineJson)

### الزر
- زر مسح السطر داخل الجدول يحمل:
  - `class="btnRemoveLineFromInvoice"`
  - `data-line-no="..."`

### خطوات السكربت
1) تأكيد من المستخدم
2) إرسال:
   - `POST PurchaseInvoices/RemoveLineJson` (مع AntiForgeryToken)
3) عند النجاح:
   - إعادة رسم السطور + تحديث الإجماليات

### أهم شيء: حماية FIFO
المسح لا يتم إذا:
- تم استهلاك جزء من الكمية من StockLedger
- لأن ذلك سيكسر FIFO ومصداقية المخزون

---

## 6.7 مسح الصنف بالكامل (RemoveProductLinesJson) — **تحقق**
- يوجد زر في صف عنوان الصنف (group header)
- السكربت ينادي:
  - `POST PurchaseInvoices/RemoveProductLinesJson`

> لم أجد الأكشن في الكنترولر المرفوع.  
> لذلك هذا الجزء “موجود في الواجهة” ويحتاج التأكد من السيرفر.

---

## 6.8 مسح جميع الأصناف (ClearAllLinesJson)
- زر يمسح كل السطور
- نفس حراسة FIFO: لا يسمح لو أي سطر تم استهلاكه

---

## 6.9 حفظ الضريبة (SaveTaxJson)
- `TaxAmountInput` + `btnSaveTax`
- يرسل:
  - `POST PurchaseInvoices/SaveTaxJson`
- ثم:
  - تحديث الإجماليات في الواجهة

---

## 6.10 التنقل بين الفواتير (الأسهم + البحث)

### لماذا لا نعمل Redirect عادي؟
لأننا داخل نظام التابات (iframe) وعايزين السرعة:
- ننقل داخل نفس التاب
- نحافظ على تحميل الصفحة الرئيسي
- نقلل الـ flicker

### كيف يشتغل؟
- الدالة `loadInvoiceBody(id)`:
  1) تعمل fetch لـ URL:
     - `PurchaseInvoices/Show?id=...&frag=body`
  2) تعمل `DOMParser` للـ HTML
  3) تستخرج `#PI_BodyHost` من الصفحة الجديدة
  4) تستبدل محتوى `PI_BodyHost` الحالي بالمحتوى الجديد
  5) تعيد تهيئة السكربت `initPurchaseInvoicePage()` (لو موجودة)

> يوجد استخدام لـ `AbortController` لمنع تداخل طلبين Fetch عند الضغط السريع على الأسهم.

### البحث
- إدخال رقم الفاتورة في `NavInvoiceNumberInput` ثم ضغط `btnNavInvoiceSearch`
- نفس فكرة `loadInvoiceBody`

---

## 6.11 الطباعة (piPrintNow)

### الفكرة العامة
- نجهز Template للطباعة داخل `piPrintArea` (مخفي)
- عند الضغط على `btnPrintInvoice`:
  - نملأ بيانات الطباعة
  - نعرض زر `piPrintNow`
  - عند الضغط: نعمل `window.print()` لكن بعد تجهيز نسخة نظيفة

### أهم ثابت في الطباعة
قبل الطباعة:
- نحذف أزرار المسح من نسخة الطباعة
- نخفي أي عمود “إجراءات”
- نضبط عرض الأعمدة

> ده تم علشان شرطك الأساسي:  
> **"الفاتورة تحتوي جميع الأعمدة ولا أريد ظهور سطر زر المسح"**

---

# 7) PurchaseInvoicesController — شرح الأكشنات الأساسية

> هنا بنوثّق السلوك الحقيقي الموجود في `PurchaseInvoicesController.cs`.

## 7.1 Show(id, frame, frag)
**الهدف:** عرض صفحة الفاتورة + تجهيز بيانات الداتا ليست + التنقل.

**ملاحظات:**
- يوجد Frame Guard:
  - لو `frame != 1` يعمل Redirect لنفسه بـ `frame=1`  
  (ثابت التابات)
- لو الفاتورة غير موجودة:
  - يرجع لأقرب فاتورة موجودة (Prev أو Next) بدل NotFound.

**يجهّز:**
- `FillPurchaseInvoiceNavAsync(...)`  
  (first/prev/next/last)
- `PopulateDropDownsAsync(...)`  
  (قائمة الموردين والمخازن…)
- `LoadProductsForAutoCompleteAsync(...)`  
  (قائمة الأصناف للداتا ليست)

---

## 7.2 SaveHeader(PurchaseInvoiceHeaderDto dto)
**الهدف:** إنشاء فاتورة جديدة أو تعديل فاتورة مفتوحة.

**إنشاء جديد (PIId = 0):**
- يتحقق من وجود CustomerId + WarehouseId
- ينشئ PurchaseInvoice ويحدد:
  - `PIDate`
  - `CreatedAt`
  - `Status`
  - `IsPosted = false`
- يرجع JSON يحتوي:
  - `piId`
  - `invoiceNumber`
  - `invoiceDate`
  - `invoiceTime`
  - `status`
  - `isPosted`
  - `createdBy`

**تعديل فاتورة موجودة:**
- يمنع التعديل لو `IsPosted = true`
- يعدل CustomerId/WarehouseId/RefPRId

---

## 7.3 AddLineJson(AddLineJsonDto dto)
**الهدف:** إضافة سطر شراء + تحديث المخزون + تحديث الإجماليات.

**التحققات الأساسية:**
- dto صحيح + PIId > 0
- الفاتورة موجودة وغير مرحلة
- prodId صحيح والكمية > 0

**منطق الدمج:**
- يبحث عن سطر موجود بنفس (ProdId + BatchNo + Expiry + PriceRetail + PurchaseDiscountPct)
- لو وجد:
  - يزيد Qty
  - يحدث قيم السطر
  - يحدث StockLedger + StockBatch بدل إنشاء جديد

**إنشاء سطر جديد:**
- يحسب LineNo الجديد
- ينشئ PILine
- ينشئ StockLedger دخول (QtyIn + RemainingQty)
- يحدث StockBatch (QtyOnHand)
- يستدعي:
  - `UpdateBatchPriceAndHistoryAsync(...)`
  - `_docTotals.RecalcPurchaseInvoiceTotalsAsync(piId)`
- يرجع JSON:
  - `ok=true`
  - `lines` (للعرض)
  - `totals` (للبادچز)

---

## 7.4 RemoveLineJson(RemoveLineJsonDto dto)
**الهدف:** مسح سطر + عكس أثره على المخزون.

**حماية FIFO (أهم نقطة):**
- يقرأ StockLedger Entries الخاصة بالسطر
- لو `RemainingQty < QtyIn` → ممنوع المسح (تم استهلاك جزء)

**لو مسموح:**
- Transaction:
  - إنقاص StockBatch QtyOnHand
  - حذف StockLedger entries
  - حذف PILine
  - Save + Commit
- إعادة حساب الإجماليات
- يرجع lines + totals

---

## 7.5 SaveTaxJson(SaveTaxJsonDto dto)
- يمنع التعديل لو الفاتورة مرحلة
- يحدث `invoice.TaxTotal`
- يستدعي إعادة حساب الإجماليات
- يرجع totals للواجهة

---

## 7.6 PostInvoice(int id)
**الهدف:** ترحيل الفاتورة وقفلها.

- يمنع لو id = 0
- يقرأ الفاتورة
- يمنع لو مرحلة بالفعل
- يستدعي:
  - `_ledgerPosting.PostPurchaseInvoiceAsync(id, ...)`
- يحدّث:
  - `IsPosted = true`
  - `Status = "مرحلة X"`
  - `PostedAt`, `PostedBy`
- يرجع JSON + يحدّث الواجهة بالقفل

---

## 7.7 OpenInvoice(int id)
**الهدف:** فتح الفاتورة (إلغاء القفل).

**في الكود الحالي المرفوع:**
- يغيّر:
  - `IsPosted = false`
  - `Status = "مفتوحة"`
  - `PostedAt = null`, `PostedBy = null`
- **لا يوجد** في `LedgerPostingService.cs` دالة عكس القيود حاليًا.  
  (يعني فتح الفاتورة هنا “يفتح التعديل” لكن *عكس القيود المحاسبية* إن كانت مطلوبة سيكون خطوة لاحقة).

---

## 7.8 GetAlternativeProducts(int prodId)
- يرجع بدائل بناءً على `GenericName`
- تستخدم في الواجهة لعرض بدائل الصنف بسرعة

---

## 7.9 دوال Private مهمة
### UpdateBatchPriceAndHistoryAsync
- ضمان وجود Batch Master
- تحديث PriceRetailBatch + UnitCostDefault
- كتابة ProductPriceHistory عند تغيّر السعر

### PopulateDropDownsAsync
- تجهيز ViewBag للموردين/المخازن

### LoadProductsForAutoCompleteAsync
- تجهيز ViewBag.ProductsAuto للداتا ليست

### LoadInvoiceLinesForUiAsync
- تجهيز شكل بيانات السطور للـ JS (ProdName, LineValue, …)

### FillPurchaseInvoiceNavAsync
- تجهيز Nav IDs: first/prev/next/last

---

# 8) DocumentTotalsService — إعادة حساب الإجماليات

الدالة الأساسية:
- `RecalcPurchaseInvoiceTotalsAsync(int piId)`

**ما الذي تفعله؟**
1) تحميل الفاتورة + سطورها
2) لكل سطر:
   - حساب:
     - retailLineTotal = PriceRetail * Qty
     - discountValue = retailLineTotal * Disc%
     - lineValueAfterDiscount = retailLineTotal - discountValue
   - تعيين `UnitCost` للسطر:
     - `UnitCost = lineValueAfterDiscount / Qty`  
     (ثابت مهم: تكلفة الوحدة بعد الخصم)
3) تجميع الإجماليات:
   - TotalRetail
   - TotalAfterDiscount
   - TaxTotal
   - TotalAfterDiscountAndTax
   - NetTotal
4) حفظ على PurchaseInvoices

> هذا يضمن أن أي تعديل في سطر واحد يعكس نفسه في الإجماليات فورًا.

---

# 9) LedgerPostingService — الترحيل (قيود محاسبية)

الدالة الموجودة في الملف المرفوع:
- `PostPurchaseInvoiceAsync(int piId, int stage, string postedBy, DateTime now)`

**المبدأ:**
- عند الترحيل → نكتب قيود في `LedgerEntries` (حسب تصميم الحسابات عندك)
- يفضّل دائمًا أن تكون القيود:
  - قابلة للتتبع: SourceType + SourceId
  - ولا تتكرر لو ضغط ترحيل مرتين (حراسة)

> فتح/عكس الترحيل غير موجود في الملف المرفوع حاليًا.

---

# 10) UserActivityLogger — سجل النشاط

في الملف الحالي:
- يسجل فقط نوعين:
  - `UserActionType.Edit`
  - `UserActionType.Delete`

**نتيجة جانبية مهمة:**
- لو في الكنترولر بتسجل Create/Post/Open…  
  فهي لن تُكتب في السجل لأن اللوجر يتجاهلها.

> هذا ليس خطأ “تقني”، لكنه قرار تصميم.  
> لو لاحقًا قررت تسجل كل شيء، سنعدّل اللوجر (لكن ده خارج هذا التوثيق).

---

# 11) سيناريوهات اختبار سريعة (قبل فاتورة المبيعات)

## سيناريو 1: إنشاء فاتورة جديدة
1) افتح: PurchaseInvoices/Show?id=0
2) اختر مورد + مخزن
3) اضغط حفظ
4) تأكد أن InvoiceId أصبح رقم > 0

## سيناريو 2: إضافة سطر
1) اختر صنف من الداتا ليست
2) أدخل Qty + Disc% + Batch + Expiry
3) اضغط إضافة
4) تأكد:
   - ظهر السطر في الجدول
   - الإجماليات اتحدثت

## سيناريو 3: دمج سطر
1) أضف نفس الصنف بنفس الباتش والصلاحية والخصم والسعر مرة أخرى
2) تأكد:
   - لم يتم إنشاء سطر جديد
   - زادت الكمية في نفس السطر

## سيناريو 4: مسح سطر
1) اضغط مسح على سطر
2) تأكد:
   - السطر اختفى
   - الإجماليات اتحدثت
   - QtyOnHand في StockBatch نقصت

## سيناريو 5: ترحيل
1) اضغط ترحيل
2) تأكد:
   - الحالة أصبحت “مرحلة …”
   - عناصر الإدخال أصبحت Disabled
3) جرّب إضافة/مسح → لازم ترفض برسالة واضحة

---

# 12) مشاكل شائعة وحلولها

## المشكلة: الأحداث بتتربط مرتين
**الحل الثابت:**  
استخدم `dataset.boundX` لكل زر حساس.

## المشكلة: الفورم يفقد الفوكس أو السهم/Enter لا يعمل
**الحل:**  
- بعد أي نجاح (SaveHeader/AddLine) نرجع فوكس على `ProdNameInput`

## المشكلة: الحقول تغير شكلها بسبب أسهم number inputs
**الحل:**  
CSS داخل الصفحة يخفي أسهم الـ number input.

## المشكلة: الطباعة يظهر فيها أزرار المسح أو عمود زائد
**الحل:**  
الطباعة تعتمد على “نسخة” يتم تنظيفها قبل `window.print()`.

---

# 13) Checklist: ما الذي سننسخه في فاتورة المبيعات (بنفس منطق المشتريات)

عند بدء فاتورة المبيعات سننسخ (كثوابت) من فاتورة المشتريات:

## (أ) نفس شكل الصفحة
- نفس تقسيم الكروت
- نفس شريط الأزرار العلوي
- نفس فكرة BodyHost للتنقل السريع

## (ب) نفس نظام الـ AJAX
- SaveHeader
- AddLineJson
- RemoveLineJson
- ClearAllLinesJson
- SaveTaxJson (إن وجدت)
- renderGroupedLines + updateSummaryBadges

## (ج) نفس نظام القفل
- `data-lock-posted="1"`
- `applyPostedUiLock`

## (د) نفس الحراسة المنطقية
- لا مسح لو FIFO اتأثر
- لا تعديل لو مرحلة

## (هـ) نفس مبدأ “المصدر الحقيقي”
- StockLedger هو مصدر الحقيقة
- StockBatch للتسريع
- FIFO يعتمد على RemainingQty

---

## سجل التحديثات داخل هذا المرجع
- 2026-01-03: توسعة شاملة + توثيق السكربت والأكشنات والجداول + Checklist للمبيعات.


---

## 🧩 خطة الحذف العميق (قريبًا)

> الهدف: عند حذف فاتورة مشتريات أو حذف سطر منها، يتم الحفاظ على اتساق **المخزون** و **الحسابات**.

### 1) قواعد الأمان (FIFO Safety)
- لو اشترينا **500** وبعنا **100** من نفس التشغيلة/الصنف:
  - **ممنوع** مسح الفاتورة/السطر الذي أدخل هذه الكمية؛ لأن جزءًا منها تم الصرف منه بالفعل.
- التطبيق الفني: نقرأ قيود StockLedger الخاصة بالشراء (QtyIn > 0) ونفحص:
  - `RemainingQty < QtyIn` ⇒ تم الاستهلاك ⇒ **نرفض الحذف** برسالة واضحة.

### 2) تحديث StockBatches
- عند حذف سطر مشتريات: نقلل `StockBatches.QtyOnHand` بمقدار `line.Qty` لنفس (WarehouseId + ProdId + BatchNo + Expiry).
- **لا نحذف صف StockBatches** حتى لو وصل الرصيد = 0 (حسب اتفاقنا).

### 3) تحديث StockLedger
- نحذف قيود StockLedger المرتبطة بالمصدر:
  - `SourceType="Purchase"` و `SourceId=PIId` (و `SourceLine=LineNo` عند حذف سطر).

### 4) الحسابات (Reverse + Repost)
- لو الفاتورة **مرحلة** (IsPosted=true):
  1) نعمل **قيد عكسي** لآخر ترحيل (قلب Debit/Credit) باستخدام `PostVersion`.
  2) ننفذ الحذف/التعديل.
  3) نعمل **قيد جديد** يعكس الوضع الجديد بعد الحذف (إعادة ترحيل).
- ملحوظة: عندك LedgerEntries **Line-Based** (كل صف = سطر قيد)، لذلك العكس/الإعادة يتمان بإضافة **صفوف جديدة** فقط.

### 5) معاملة واحدة (Transaction)
- كل ما سبق يكون داخل Transaction واحدة لضمان:
  - يا إما كله ينجح… يا إما Rollback بالكامل.

---

## ✅ ملاحظة واجهة مهمة (زر المسح داخل جدول السطور)
لو كان الضغط على زر **"مسح"** بيختار الصف بدل ما ينفذ المسح:
- السبب غالبًا هو كود اختيار الصف على `document.addEventListener('click', ...)`
- الحل: في أول الهاندلر، لو `e.target.closest('button, a, .btnRemoveLineFromInvoice, .btnRemoveProductFromInvoice')` ⇒ نعمل `return;`
