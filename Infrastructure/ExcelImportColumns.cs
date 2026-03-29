using System.Collections.Generic;

namespace ERP.Infrastructure
{
    /// <summary>
    /// أعمدة الإكسل الثابتة لكل نوع استيراد.
    /// البرنامج يقرأ هذه الأعمدة فقط؛ أي عمود إضافي في الملف يُتجاهل ما لم يُحدَّث هذا الملف.
    /// </summary>
    public static class ExcelImportColumns
    {
        /// <summary>
        /// استيراد الأصناف (دواء / إكسسوار).
        /// مطلوب: عمود اسم الصنف فقط. الباقي اختياري.
        /// </summary>
        public static readonly IReadOnlyList<string> Products = new[]
        {
            "اسم الكود", "كود الصنف", "ProdId", "الصنف", "ProdName", "اسم الصنف",
            "الشركة", "الشركه", "Company",
            "التصنيف", "التصنيه", "Classification",
            "الموقع", "Location",
            "المخزن", "WarehouseId", "اسم المخزن", "WarehouseName",
            "سعرج", "سعر ج", "PriceRetail",
            "الكود", "Code",
            "المرجح", "الاسم العلمي", "GenericName",
            "الوصف", "Description",
            "الشكل الدوائي", "DosageForm",
            "Barcode", "الباركود",
            "المنشأ", "Imported",
            "كمية الكرتونة", "CartonQuantity", "الكرتونة",
            "كمية الباكو", "PackQuantity", "الباكو", "باكو"
        };

        /// <summary>
        /// استيراد أرصدة الأصناف (رصيد أول المدة) — دفتر الحركة المخزنية SourceType = Opening.
        /// مطلوب: الصنف (كود أو اسم)، الكمية، الخصم المرجح، إجمالي التكلفة. اختياري: كود المخزن.
        /// التشغيلة + الصلاحية معاً: تُنشئ/تربط جدول Batch وتُسجّل في StockLedger ثم تُزامن Stock_Batches مع الدفتر.
        /// </summary>
        public static readonly IReadOnlyList<string> ProductOpeningBalance = new[]
        {
            "اسم الكود", "كود الصنف", "ProdId", "ProdName", "الصنف", "اسم الصنف",
            "الكمية", "QtyIn", "Quantity",
            "الخصم المرجح", "PurchaseDiscount", "المرجح",
            "إجمالي التكلفة", "اجمالي التكلفة", "إجمالي تكلفة", "TotalCost",
            "تكلفة العلبة", "تكلفة الوحدة", "UnitCost",
            "سعر الجمهور", "PriceRetail", "PriceRetailBatch",
            "كود المخزن", "WarehouseId", "المخزن",
            "التشغيلة", "التشغيله", "رقم التشغيلة", "رقم التشغيله", "BatchNo", "Batch",
            "الصلاحية", "الصلاحيه", "Expiry", "تاريخ الصلاحية", "تاريخ الصلاحيه", "Expire"
        };

        /// <summary>
        /// استيراد قائمة العملاء.
        /// مطلوب: اسم العميل أو كود العميل (الاسم). الباقي اختياري.
        /// مسلسل → كود الإكسل (ExternalCode).
        /// عمود المنطقة: يطابق مناطق موجودة أو يُنشئ مناطق جديدة (مع المحافظة/الحي في واجهة الاستيراد).
        /// </summary>
        public static readonly IReadOnlyList<string> Customers = new[]
        {
            "اسم الكود", "مسلسل", "كود العميل", "CustomerId", "الرقم", "كود", "Code", "الكود",
            "التليفون", "الهاتف", "Phone", "Phone1",
            "الاسم", "اسم العميل", "CustomerName",
            "التاريخ", "Date", "EntryDate",
            "اسم المسؤول", "اسم المسئول", "OrderContactName",
            "ملاحظات", "Notes",
            "المنطقة", "Region", "RegionName", "Area", "AreaName",
            "العنوان", "Address",
            "الشريحه", "الشريحة", "Segment",
            "الأرصدة", "Balance", "CurrentBalance",
            "المندوب", "UserId", "SalesRep",
            "رقم البطاقة الضريبية", "الرقم القومى", "رقم البطاقة الضريبية / الرقم القومى", "TaxId", "NationalId",
            "رقم السجل", "RecordNumber",
            "رقم الرخصة", "LicenseNumber",
            "النوع", "PartyCategory"
        };

        /// <summary>
        /// استيراد أرصدة العملاء (رصيد أول المدة) — دفتر الأستاذ LedgerEntry مع SourceType = Opening.
        /// مطلوب: العميل (كود أو اسم)، مدين أو دائن. اختياري: التاريخ، رقم الحساب.
        /// </summary>
        public static readonly IReadOnlyList<string> CustomerOpeningBalance = new[]
        {
            "اسم الكود", "كود العميل", "CustomerId", "رقم الحساب", "اسم العميل", "العميل", "الاسم",
            "مدين", "Debit", "مدین",
            "دائن", "Credit",
            "تاريخ", "EntryDate", "AccountId"
        };

        /// <summary>
        /// استيراد أسماء المستخدمين.
        /// مطلوب: اسم الدخول أو الاسم. الباقي اختياري.
        /// </summary>
        public static readonly IReadOnlyList<string> Users = new[]
        {
            "اسم الكود", "كود المستخدم", "UserId",
            "اسم المستخدم", "UserName", "اسم الدخول",
            "الاسم", "Name", "DisplayName",
            "البريد", "Email"
        };

        /// <summary>
        /// استيراد رصيد الخزينة (رصيد أول المدة).
        /// من النموذج: رقم حساب الخزينة. من الملف (اختياري): مبلغ، مدين/دائن.
        /// </summary>
        public static readonly IReadOnlyList<string> TreasuryOpeningBalance = new[]
        {
            "رصيد الخزينة", "رقم الحساب", "AccountId",
            "مبلغ", "Amount", "مدين", "Debit", "دائن", "Credit"
        };
    }
}
