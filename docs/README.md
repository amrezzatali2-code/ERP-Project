# توثيق نظام ERP

مرحبًا بك في توثيق نظام ERP. هذا المجلد يحتوي على ملفات التوثيق التقنية والمراجع التفصيلية للمشروع.

## 📖 ابدأ من هنا

إذا كنت جديدًا على المشروع، ابدأ بقراءة:

1. **[INDEX.md](INDEX.md)** — فهرس بكل ملفات التوثيق (هذا المجلد + الجذر)
2. **[README.md](../README.md)** — التوثيق الرئيسي للمشروع (نقطة البداية الموصى بها)

## 📄 التوثيق المجمّع والتصدير

- **[ERP_Documentation_Full.md](ERP_Documentation_Full.md)** — نسخة واحدة طويلة تجمع نفس المحتوى التعريفي تقريباً الموجود في `README.md` الجذر؛ **المصدر اليومي للقراءة هو README الجذر**، ويُحدَّث هذا الملف يدوياً عند الحاجة للطباعة أو التسليم.
- **[HOW_TO_CONVERT_TO_WORD.md](HOW_TO_CONVERT_TO_WORD.md)** — تحويل `ERP_Documentation_Full.md` إلى Word (Pandoc وغيره).

## 📚 الملفات الأساسية في `docs/`

| الملف | الوصف |
|--------|--------|
| [ARCHITECTURE.md](ARCHITECTURE.md) | البنية التقنية والطبقات |
| [DEVELOPER_GUIDE.md](DEVELOPER_GUIDE.md) | دليل المطور |
| [DATABASE_SCHEMA.md](DATABASE_SCHEMA.md) | مخطط قاعدة البيانات |

## 📚 مواضيع تفصيلية في `docs/`

| الملف | الوصف |
|--------|--------|
| [opening-balance-design.md](opening-balance-design.md) | تصميم الرصيد الافتتاحي |
| [purchasing-module-tables-proposal.md](purchasing-module-tables-proposal.md) | مقترح جداول موديول المشتريات |
| [purchasing-module-implementation-plan.md](purchasing-module-implementation-plan.md) | خطة تنفيذ المشتريات |
| [search-and-performance-review.md](search-and-performance-review.md) | مراجعة البحث والأداء |
| [orga-import-and-product-matching.md](orga-import-and-product-matching.md) | استيراد أورجا ومطابقة الأصناف |
| [تفعيل_الصلاحيات.md](تفعيل_الصلاحيات.md) | تفعيل الصلاحيات |
| [كيف_تعمل_الصلاحيات.md](كيف_تعمل_الصلاحيات.md) | آلية الصلاحيات |
| [مراجعة-الصلاحيات-والأدوار.md](مراجعة-الصلاحيات-والأدوار.md) | مراجعة الصلاحيات والأدوار |
| [فتح الفواتير ككتلة واحدة.md](فتح%20الفواتير%20ككتلة%20واحدة.md) | فتح الفواتير ككتلة واحدة |

## 📁 ملفات في جذر المستودع (مرتبطة بالتوثيق)

- **[../README.md](../README.md)** — التوثيق الرئيسي
- **[../PERFORMANCE_ANALYSIS.md](../PERFORMANCE_ANALYSIS.md)** — تحليل الأداء
- **[../project_state_2025-12-22_tabs_UPDATED_v15_SALES_DONE (1).md](../project_state_2025-12-22_tabs_UPDATED_v15_SALES_DONE%20(1).md)** — حالة المشروع التفصيلية
- **[../PurchaseInvoice_Full_Reference_UPDATED_2026-01-08.md](../PurchaseInvoice_Full_Reference_UPDATED_2026-01-08.md)** — مرجع فاتورة المشتريات

## 🎯 حسب احتياجك

### أريد فهم المشروع
→ اقرأ [README.md](../README.md) ثم [ARCHITECTURE.md](ARCHITECTURE.md)

### أريد تطوير ميزة جديدة
→ اقرأ [DEVELOPER_GUIDE.md](DEVELOPER_GUIDE.md)

### أريد فهم قاعدة البيانات
→ اقرأ [DATABASE_SCHEMA.md](DATABASE_SCHEMA.md)

### أريد تحسين الأداء
→ اقرأ [PERFORMANCE_ANALYSIS.md](../PERFORMANCE_ANALYSIS.md) و [search-and-performance-review.md](search-and-performance-review.md)

### أريد معرفة الحالة الحالية
→ اقرأ [project_state_2025-12-22_tabs_UPDATED_v15_SALES_DONE (1).md](../project_state_2025-12-22_tabs_UPDATED_v15_SALES_DONE%20(1).md)

## 📝 ملاحظات

- **اللغة:** أغلب المحتوى بالعربية؛ بعض أسماء الملفات أو العناوين الفرعية بالإنجليزية لسهولة الأدوات وروابط Git.
- التوثيق يُحدَّث مع تطور المشروع؛ عند تغيير كبير في `README.md` الجذر، راجع ما إذا كان يجب مواءمة [ERP_Documentation_Full.md](ERP_Documentation_Full.md).
- قواعد سلوك الواجهة والقوائم للمطورين موجودة أيضاً تحت `.cursor/rules/` (ليست بديلاً عن هذا المجلد بل مكمّلة للتطوير).

---

**آخر تحديث**: مارس 2026
