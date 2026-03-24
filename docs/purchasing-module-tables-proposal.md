# مقترح جداول موديول برنامج المشتريات

هذا المقترح يعتمد على منطق الموديول في `.cursor/rules/purchasing-program-logic.mdc` والربط مع جداول الـ ERP الحالية (Customer, Product, Warehouse).

---

## إجابات سريعة

- **قاعدة البيانات / المطابقة، أين تُحفظ؟**  
  في جدول واحد **مطابقة_أصناف_العميل** (VendorProductMapping): فيه اسم العميل (ربط بـ Customer)، اسم الصنف، الكود (كود الصنف في شركتنا)، سعر الجمهور، و**كود العميل**. القاعدة الحالية وأي مطابقة لاحقة تُحفظ في هذا الجدول بنفس الأعمدة.

- **المطابقة عند استلام الفاكس:** أولاً بالاسم (اسم الصنف في الفاكس مع اسم الصنف في الجدول لنفس العميل). لاحقاً عند وجود كود العميل يمكن أن تتم المطابقة بالكود أولاً إن وُجد، ثم بالاسم.

---

## أسماء الجداول: إنجليزي ↔ عربي

| الاسم بالإنجليزي (الكود) | الاسم بالعربي |
|--------------------------|----------------|
| PurchasePolicyRule | **سياسات_الشراء** |
| PurchasingDataSourceConfig | **إعداد_مصدر_البيانات** |
| VendorFaxUpload | **رأس_استيراد_الفاكس** |
| VendorFaxLine | **سطور_الفاكس** |
| VendorProductMapping | **مطابقة_أصناف_العميل** (قاعدة البيانات) |
| PurchasingOrder | **طلب_شراء_الموديول** |
| PurchasingOrderLine | **سطور_طلب_الشراء** |
| PurchasingOrderAmendment | **سجل_تأكيد_التعديل** |

> في قاعدة البيانات يمكن استخدام الاسم الإنجليزي أو العربي عبر `[ToTable("...")]` في EF. الكود (C#) يبقى بالإنجليزي للكلاسات والأعمدة.

---

## الرسم العام للعلاقات

```
Customer (ERP) ◄──── VendorFaxUpload ◄──── VendorFaxLine
     │                       │
     │                       └── MatchedProductId ──► Product (ERP)
     │
     ├──────────────────────► VendorProductMapping (مطابقة_أصناف_العميل / قاعدة البيانات) ◄──── Product (ERP)
     │
     └──────────────────────► PurchasingOrder ◄──── PurchasingOrderLine ──► Product (ERP)
                                        │
                                        └── ErpPurchaseRequestId ──► PurchaseRequest (ERP)

PurchasePolicyRule ( standalone )
PurchasingDataSourceConfig ( إعداد مصدر البيانات )
PurchasingOrderAmendment ( سجل التأكيد/التعديل )
```

---

## 1. سياسات الشراء — PurchasePolicyRule (جدول: سياسات_الشراء)

**الغرض:** تخزين قواعد الشراء (مقارنة الخصم، نسبة الهدف، حد المخزون، تقريب الكمية، إلخ).

| العمود | النوع | وصف |
|--------|--------|------|
| Id | int, PK, Identity | المعرّف |
| Enabled | bool | مفعّل؟ |
| RuleType | tinyint | 0=مقارنة خصم (Compare), 1=تقريب كمية بحد سعر (RoundQtyWithPriceCap) |
| CompareOp | tinyint | عند Compare: 0=مساوي, 1=أعلى, 2=أدنى |
| DiffExact | decimal(9,2) NULL | فرق الخصم المطلوب (نقاط) أو خطوة التقريب |
| TargetPercent | decimal(9,2) NULL | نسبة الهدف من المتوسط (مثلاً 100) |
| StockBelowPercent | decimal(9,2) NULL | شرط: المخزون أقل من نسبة % من الهدف |
| Tolerance | decimal(9,2) | سماح المقارنة (افتراضي 0.10) |
| Action | tinyint | 0=شراء (Buy), 1=مراجعة (Review) |
| SortOrder | int | ترتيب تطبيق القواعد (الأولوية) |
| CreatedAt | datetime2 | تاريخ الإنشاء |
| UpdatedAt | datetime2 | آخر تحديث |

**ملاحظة:** يمكن لاحقاً إضافة حقول مثل RoundPriceCap (حد السعر عند تقريب الكمية) إن لزم.

---

## 2. إعداد مصدر البيانات — PurchasingDataSourceConfig (جدول: إعداد_مصدر_البيانات)

**الغرض:** تسجيل مصدر البيانات وإعدادات الاستيراد (حسب الحاجة لاحقاً).

| العمود | النوع | وصف |
|--------|--------|------|
| Id | int, PK, Identity | المعرّف |
| SourceType | nvarchar(20) | "ORGA" أو "ERP" |
| DisplayName | nvarchar(100) NULL | اسم معروض |
| CreatedAt | datetime2 | |
| UpdatedAt | datetime2 | |

---

## 3. رأس استيراد الفاكس — VendorFaxUpload (جدول: رأس_استيراد_الفاكس)

**الغرض:** كل استيراد Excel من عميل = رأس واحد (نحفظ منه التاريخ والوقت بدقة).

| العمود | النوع | وصف |
|--------|--------|------|
| Id | int, PK, Identity | المعرّف |
| CustomerId | int, FK → Customer | العميل/المورد الذي أرسل الفاكس |
| ReceivedAt | datetime2 | **تاريخ ووقت** استلام الفاكس (مهم: قد يرسل مرتين في اليوم) |
| FileName | nvarchar(255) NULL | اسم الملف المرفوع |
| ImportedBy | nvarchar(100) NULL | المستخدم الذي قام بالاستيراد |
| CreatedAt | datetime2 | |

**العلاقة:** Customer من جدول الـ ERP (نفس العملاء/الموردين).

---

## 4. سطور الفاكس — VendorFaxLine (جدول: سطور_الفاكس)

**الغرض:** كل سطر في الفاكس: صنف كما أرسله العميل، سعر، خصم، وربطه بالصنف عندنا بعد المطابقة.

| العمود | النوع | وصف |
|--------|--------|------|
| Id | int, PK, Identity | المعرّف |
| VendorFaxUploadId | int, FK → VendorFaxUpload | رأس الاستيراد |
| LineNo | int | رقم السطر داخل نفس الاستيراد |
| ProductNameFromVendor | nvarchar(255) | اسم الصنف كما أرسله العميل |
| VendorProductCode | nvarchar(100) NULL | كود الصنف عند العميل (إن وُجد في الإكسل) |
| Price | decimal(18,4) | السعر |
| DiscountPct | decimal(9,2) | الخصم % |
| MatchedProductId | int NULL, FK → Product | يُملأ بعد المطابقة: الصنف عندنا المقابل |
| CreatedAt | datetime2 | |

**ملاحظة:** عند استيراد فاكس جديد نطابق أولاً مع جدول المطابقة (VendorProductMapping) ونحدّث MatchedProductId للمتطابق؛ الباقي يبقى NULL حتى تتم المطابقة اليدوية.

---

## 5. قاعدة البيانات / مطابقة أصناف العميل — VendorProductMapping (جدول: مطابقة_أصناف_العميل)

**الغرض:** جدول واحد لقاعدة بيانات العملاء وأي مطابقة لاحقة. الأعمدة مطابقة لورقة الإكسل الحالية مع إضافة **كود العميل**. المطابقة عند الفاكس: أولاً بالكود إن وُجد، ثم بالاسم.

| العمود | النوع | وصف (العرض في الواجهة) |
|--------|--------|------------------------|
| Id | int, PK, Identity | المعرّف |
| CustomerId | int, FK → Customer | **اسم العميل** (الربط بجدول العملاء) |
| VendorProductName | nvarchar(255) NULL | **اسم الصنف** |
| ProductId | int, FK → Product | **الكود** (كود الصنف في شركتنا في الـ ERP) |
| PriceRetail | decimal(18,4) NULL | **سعر الجمهور** |
| VendorProductCode | nvarchar(100) NULL | **كود العميل** (كود الصنف عند العميل — للمطابقة لاحقاً بالكود) |
| Tag | nvarchar(50) NULL | وسم اختياري: "استبعاد", "سعر مختلف", إلخ. |
| CreatedAt | datetime2 | |
| UpdatedAt | datetime2 | |

**ملاحظة:** المطابقة عند استلام الفاكس تتم بين أصناف الفاكس وهذا الجدول لنفس العميل: أولاً **بالكود** (كود العميل) إن وُجد في الفاكس وفي الجدول، ثم **بالاسم** (اسم الصنف).

---

## 6. طلب الشراء (هيدر) — PurchasingOrder (جدول: طلب_شراء_الموديول)

**الغرض:** طلب شراء في الموديول (قبل التحويل إلى ERP). يُرسل واتساب ثم تأكيد/تعديل من العميل.

| العمود | النوع | وصف |
|--------|--------|------|
| Id | int, PK, Identity | المعرّف |
| CustomerId | int, FK → Customer | العميل/المورد |
| OrderNumber | nvarchar(50) NULL | رقم عرضي للطلب (للعرض والمرجع) |
| OrderDate | date | تاريخ الطلب |
| Status | nvarchar(30) | Draft, SentToWhatsApp, Confirmed, Modified, ConvertedToErp |
| SentAt | datetime2 NULL | وقت الإرسال على الواتساب |
| ConfirmedAt | datetime2 NULL | وقت التأكيد من العميل |
| AmendmentNotes | nvarchar(max) NULL | ملاحظات التعديل من العميل |
| ErpPurchaseRequestId | int NULL, FK → PurchaseRequest | عند التحويل إلى ERP: رقم طلب الشراء في النظام |
| CreatedBy | nvarchar(100) NULL | مستخدم النظام الذي أنشأ الطلب |
| CreatedAt | datetime2 | |
| UpdatedAt | datetime2 | |

---

## 7. سطور طلب الشراء — PurchasingOrderLine (جدول: سطور_طلب_الشراء)

**الغرض:** تفاصيل كل صنف في طلب الشراء (هيدر + سطور).

| العمود | النوع | وصف |
|--------|--------|------|
| Id | int, PK, Identity | المعرّف |
| PurchasingOrderId | int, FK → PurchasingOrder | رأس الطلب |
| LineNo | int | رقم السطر |
| ProductId | int, FK → Product | الصنف عندنا |
| VendorProductCode | nvarchar(100) NULL | كود الصنف عند العميل (للمرجع) |
| ProductName | nvarchar(255) NULL | اسم الصنف (نسخة للعرض) |
| Qty | decimal(18,4) | الكمية المطلوبة |
| UnitPrice | decimal(18,4) NULL | سعر الوحدة |
| DiscountPct | decimal(9,2) NULL | خصم % |
| Notes | nvarchar(500) NULL | ملاحظات السطر |
| CreatedAt | datetime2 | |

---

## 8. سجل التأكيد/التعديل — PurchasingOrderAmendment (جدول: سجل_تأكيد_التعديل — اختياري)

**الغرض:** تتبع تأكيد العميل أو طلبات التعديل (للمراجعة والتاريخ).

| العمود | النوع | وصف |
|--------|--------|------|
| Id | int, PK, Identity | المعرّف |
| PurchasingOrderId | int, FK → PurchasingOrder | الطلب |
| AmendmentType | nvarchar(20) | Confirmed, Modified |
| AmendmentDate | datetime2 | وقت التأكيد/التعديل |
| Notes | nvarchar(max) NULL | نص التعديل أو التأكيد |
| CreatedAt | datetime2 | |

---

## ملخص الجداول والاعتماد على الـ ERP

| الجدول (إنجليزي) | الجدول (عربي) | الاعتماد على ERP |
|-------------------|----------------|-------------------|
| PurchasePolicyRule | سياسات_الشراء | مستقل (موديول فقط) |
| PurchasingDataSourceConfig | إعداد_مصدر_البيانات | مستقل (إعداد) |
| VendorFaxUpload | رأس_استيراد_الفاكس | Customer |
| VendorFaxLine | سطور_الفاكس | VendorFaxUpload, Product (بعد المطابقة) |
| VendorProductMapping | مطابقة_أصناف_العميل (**قاعدة البيانات**) | اسم العميل، اسم الصنف، الكود، سعر الجمهور، **كود العميل**؛ Customer, Product |
| PurchasingOrder | طلب_شراء_الموديول | Customer, PurchaseRequest (عند التحويل) |
| PurchasingOrderLine | سطور_طلب_الشراء | PurchasingOrder, Product |
| PurchasingOrderAmendment | سجل_تأكيد_التعديل | PurchasingOrder |

---

## الخطوة التالية

- إنشاء الـ **Entity classes** في مجلد Models (أو Areas/PurchasingProgram/Models إن وُجد).
- إضافة الـ **DbSet** في AppDbContext.
- إنشاء **Migration** لجدولة الجداول في قاعدة الـ ERP.

إذا وافقت على هذا المقترح ننتقل لكتابة الـ Entities وربطها في الـ DbContext.
