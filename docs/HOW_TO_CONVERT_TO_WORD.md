# كيفية تحويل التوثيق إلى Word

تم إنشاء ملف توثيق شامل في: `docs/ERP_Documentation_Full.md`

يمكنك تحويله إلى Word بعدة طرق:

---

## الطريقة 1: استخدام Pandoc (الأفضل)

### تثبيت Pandoc

**Windows:**
1. حمّل من: https://pandoc.org/installing.html
2. أو استخدم Chocolatey:
   ```bash
   choco install pandoc
   ```

**Mac:**
```bash
brew install pandoc
```

**Linux:**
```bash
sudo apt-get install pandoc
```

### التحويل

```bash
cd docs
pandoc ERP_Documentation_Full.md -o ERP_Documentation.docx
```

### مع تنسيق أفضل

```bash
pandoc ERP_Documentation_Full.md -o ERP_Documentation.docx \
  --reference-doc=reference.docx \
  --toc \
  --toc-depth=3
```

---

## الطريقة 2: استخدام Visual Studio Code

1. افتح الملف `ERP_Documentation_Full.md` في VS Code
2. اضغط `Ctrl+Shift+P` (أو `Cmd+Shift+P` على Mac)
3. ابحث عن "Markdown: Open Preview"
4. اضغط `Ctrl+K V` لفتح Preview بجانب الملف
5. انسخ المحتوى من Preview
6. الصقه في Word

---

## الطريقة 3: استخدام أدوات Online

### Dillinger.io
1. افتح: https://dillinger.io/
2. انسخ محتوى `ERP_Documentation_Full.md`
3. الصقه في Dillinger
4. اضغط "Export as" → "Styled HTML"
5. افتح HTML في Word
6. احفظه كـ .docx

### Markdown to Word
1. افتح: https://www.markdowntoword.com/
2. ارفع ملف `ERP_Documentation_Full.md`
3. حمّل ملف Word الناتج

---

## الطريقة 4: استخدام Word مباشرة

1. افتح Microsoft Word
2. اذهب إلى File → Open
3. اختر ملف `ERP_Documentation_Full.md`
4. Word سيقوم بتحويله تلقائيًا
5. احفظه كـ .docx

> **ملاحظة**: قد تحتاج لتنسيق بعض العناصر يدويًا

---

## الطريقة 5: استخدام Python Script

### تثبيت المكتبات المطلوبة

```bash
pip install pypandoc python-docx
```

### Script التحويل

```python
import pypandoc

output = pypandoc.convert_file(
    'docs/ERP_Documentation_Full.md',
    'docx',
    format='md',
    outputfile='docs/ERP_Documentation.docx',
    extra_args=['--toc', '--toc-depth=3']
)
```

---

## نصائح للتحسين

### بعد التحويل إلى Word:

1. **تحقق من التنسيق**
   - العناوين الرئيسية
   - الجداول
   - أكواد البرمجة

2. **أضف صفحة عنوان**
   - اسم المشروع
   - التاريخ
   - الإصدار

3. **تحقق من جدول المحتويات**
   - تأكد من وجوده
   - حدّثه إذا لزم الأمر

4. **تحقق من الصفحات**
   - أرقام الصفحات
   - Headers/Footers

5. **تحقق من الخطوط العربية**
   - تأكد من دعم العربية
   - استخدم خطوط واضحة مثل: Arial, Tahoma, أو Segoe UI

---

## إذا واجهت مشاكل

### المشكلة: الجداول لا تظهر بشكل صحيح

**الحل**: استخدم Pandoc مع خيارات إضافية:
```bash
pandoc ERP_Documentation_Full.md -o ERP_Documentation.docx \
  --wrap=none \
  --columns=1000
```

### المشكلة: أكواد البرمجة لا تظهر بشكل صحيح

**الحل**: استخدم Pandoc مع highlight:
```bash
pandoc ERP_Documentation_Full.md -o ERP_Documentation.docx \
  --highlight-style=tango
```

### المشكلة: الخطوط العربية لا تظهر

**الحل**: 
1. افتح Word
2. حدد كل النص
3. غيّر الخط إلى Arial أو Tahoma
4. تأكد من أن Encoding هو UTF-8

---

## ملف مرجعي للـ Pandoc (اختياري)

يمكنك إنشاء ملف `reference.docx` كقالب:

```bash
pandoc --print-default-data-file reference.docx > reference.docx
```

ثم عدّل التنسيقات فيه (خطوط، هوامش، ألوان...) واستخدمه:

```bash
pandoc ERP_Documentation_Full.md -o ERP_Documentation.docx \
  --reference-doc=reference.docx
```

---

## النتيجة النهائية

بعد التحويل، ستحصل على ملف Word يحتوي على:
- ✅ جدول محتويات تلقائي
- ✅ تنسيقات منظمة
- ✅ جداول منسقة
- ✅ أكواد برمجة منسقة
- ✅ دعم كامل للعربية

---

**ملاحظة**: إذا كنت تفضل، يمكنني إنشاء ملف HTML منسق يمكن فتحه مباشرة في Word.
