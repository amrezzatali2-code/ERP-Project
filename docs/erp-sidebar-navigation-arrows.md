# تنقّل الأسهم وبحث الرقم في الشريط الجانبي (نفس التاب / نفس الـ iframe)

## المشكلة

- أزرار تحمل الصنف **`open-same-tab`** تمر عبر `wwwroot/js/site.js`: تُرسل `postMessage` من الـ iframe للأب (`erp-open-tab`) ليفتح أو يحدّث تاباً. حسب إعدادات شريط التابات في التطبيق، قد يُفتح **تاب جديد** بدل استبدال المحتوى الحالي.
- استدعاء **`window.erpTabs.openTab(...)`** من سكربت الشاشة لبحث الرقم قد يسبب نفس السلوك.

## السلوك المطلوب

- التنقّل بين السجلات (أول / سابق / تالي / آخر) و**بحث برقم المستند** يجب أن يحدّث **نفس الصفحة** داخل **نفس الـ iframe** (أو نفس النافذة إن لم يكن iframe)، **دون** تاب إضافي.

## المرجعان المعتمدان

| الشاشة | الملف | آلية التنقّل |
|--------|--------|----------------|
| **فاتورة مبيعات** | `Views/SalesInvoices/Show.cshtml` | `document.addEventListener('click', docCaptureHandler, **true**)` في مرحلة **capture**؛ يستدعي **`loadInvoiceBody(url)`** لتحميل الجسم فقط داخل `#PI_BodyHost` مع تحديث السايد بار. |
| **أمر بيع** | `Views/SalesOrders/Show.cshtml` | اعتراض capture على `#btnSoFirst` … `#btnSoLast` و `#btnNavOrderSearch`؛ **`window.location.assign(ensureSoNavUrl(url))`** لتحميل الصفحة كاملة في نفس الإطار (لا يوجد تحميل جزئي للجسم). |

## قواعد التطبيق على شاشات جديدة

1. **لا** تضع `class="open-same-tab"` على أزرار الأسهم أو زر البحث برقم المستند إذا كان المطلوب التنقّل داخل نفس الإطار.
2. استخدم **معرفات فريدة** للشاشة (مثل `btnSoFirst` لأمر البيع) لتفادي التداخل مع شاشات أخرى.
3. سجّل معالجاً في مرحلة **الالتقاط** (`addEventListener(..., true)`):
   - `ev.preventDefault(); ev.stopPropagation(); ev.stopImmediatePropagation();` حتى لا يصل الحدث إلى `site.js` (أو إلى معالجات أخرى تفتح تاباً).
4. ابنِ الرابط من `data-url` أو من `data-base-url` + `id`، مع **`frame=1`** ومعامل **`_ts`** لتفادي كاش قديم (انظر `ensureSoNavUrl` في أمر البيع، و`addNoCache` في `site.js`).
5. إن وُجد تحميل جزئي للمحتوى (مثل فاتورة المبيعات): استدعِ دالة التحميل الجزئي بدل `location.assign`.
6. لحقل البحث: Enter يستدعي نفس منطق زر البحث (انظر `NavOrderSearchInput` في أمر البيع).

## ملفات ذات صلة

- `wwwroot/js/site.js` — معالج `open-same-tab` و`erp-open-tab`.
- `Views/SalesInvoices/Show.cshtml` — `docCaptureHandler` + `refreshSideNavButtons`.
- `Views/SalesOrders/Show.cshtml` — `soOrderNavCapture` + أزرار `btnSo*`.
