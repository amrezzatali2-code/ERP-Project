using System.Collections.Generic;

namespace ERP.Security
{
    /// <summary>
    /// كودات الصلاحيات الأساسية فى النظام.
    /// الهدف: عدم تكرار النصوص "Strings" فى الكود
    /// واستخدام نفس الكود فى كل مكان (Controller / View / Seeder).
    /// </summary>
    public static class PermissionCodes
    {
        // ===== المبيعات – فواتير البيع =====
        public static class SalesInvoices
        {
            public const string View = "Sales.Invoices.View";    // عرض قائمة الفواتير
            public const string Show = "Sales.Invoices.Show";    // فتح فاتورة واحدة
            public const string Create = "Sales.Invoices.Create";  // إنشاء فاتورة
            public const string Edit = "Sales.Invoices.Edit";    // تعديل فاتورة
            public const string Delete = "Sales.Invoices.Delete";  // حذف فاتورة
            public const string Export = "Sales.Invoices.Export";  // تصدير
            public const string Post = "Sales.Invoices.Post";    // ترحيل
            public const string UnPost = "Sales.Invoices.UnPost";  // إلغاء الترحيل
            public const string Print = "Sales.Invoices.Print";   // طباعة الفاتورة
        }

        // ===== العملاء =====
        public static class Customers
        {
            public const string View = "Customers.View";
            public const string Show = "Customers.Show";
            public const string Create = "Customers.Create";
            public const string Edit = "Customers.Edit";
            public const string Delete = "Customers.Delete";
            public const string Export = "Customers.Export";
        }

        // ===== إدارة المخازن – الأصناف =====
        public static class Products
        {
            public const string View = "Products.View";
            public const string Show = "Products.Show";
            public const string Create = "Products.Create";
            public const string Edit = "Products.Edit";
            public const string Delete = "Products.Delete";
            public const string Export = "Products.Export";
        }

        // ===== إدارة المخازن – حركة المخزون =====
        public static class Stock
        {
            public const string StockAdjustments_View = "Stock.Adjustments.View";
            public const string StockAdjustments_Edit = "Stock.Adjustments.Edit";
            public const string StockTransfers_View = "Stock.Transfers.View";
            public const string StockTransfers_Edit = "Stock.Transfers.Edit";
        }

        // ===== الحسابات =====
        public static class Accounts
        {
            public const string Chart_View = "Accounts.Chart.View";     // شجرة الحسابات
            public const string Chart_Edit = "Accounts.Chart.Edit";
            public const string Ledger_View = "Accounts.Ledger.View";    // دفتر الأستاذ
            public const string Ledger_Export = "Accounts.Ledger.Export";
            public const string CashReceipt = "Accounts.CashReceipt";    // إذن قبض
            public const string CashPayment = "Accounts.CashPayment";    // إذن صرف
            public const string DebitNote = "Accounts.DebitNote";      // إشعار خصم
            public const string CreditNote = "Accounts.CreditNote";     // إشعار إضافة
        }

        // ===== المستخدمون والصلاحيات =====
        public static class Security
        {
            public const string Users_View = "Security.Users.View";
            public const string Users_Edit = "Security.Users.Edit";
            public const string Roles_View = "Security.Roles.View";
            public const string Roles_Edit = "Security.Roles.Edit";
            public const string Permissions_View = "Security.Permissions.View";
            public const string Permissions_Edit = "Security.Permissions.Edit";
        }

        /// <summary>
        /// ترجع كل الصلاحيات فى شكل List علشان نستخدمها فى Seeder.
        /// Code = الكود الثابت
        /// NameAr = اسم الصلاحية بالعربي
        /// Module = اسم الموديول (اللى ظاهر فى الـ Navbar بالعربي)
        /// Description = وصف مختصر
        /// </summary>
        public static IEnumerable<(string Code, string NameAr, string Module, string Description)> GetAll()
        {
            // ===================== المبيعات =====================

            // فواتير المبيعات
            yield return (SalesInvoices.View, "عرض فواتير المبيعات", "المبيعات", "فتح شاشة قائمة فواتير المبيعات");
            yield return (SalesInvoices.Show, "عرض تفاصيل فاتورة مبيعات", "المبيعات", "فتح شاشة فاتورة واحدة للعرض");
            yield return (SalesInvoices.Create, "إنشاء فاتورة مبيعات جديدة", "المبيعات", "إضافة فاتورة جديدة");
            yield return (SalesInvoices.Edit, "تعديل فاتورة مبيعات", "المبيعات", "تعديل بيانات فاتورة موجودة");
            yield return (SalesInvoices.Delete, "حذف فاتورة مبيعات", "المبيعات", "حذف فاتورة مع السطور التابعة لها");
            yield return (SalesInvoices.Export, "تصدير فواتير المبيعات", "المبيعات", "تصدير قائمة الفواتير إلى Excel/CSV");
            yield return (SalesInvoices.Post, "ترحيل فاتورة مبيعات", "المبيعات", "ترحيل الفاتورة للمخزون والحسابات");
            yield return (SalesInvoices.UnPost, "إلغاء ترحيل فاتورة مبيعات", "المبيعات", "إلغاء ترحيل فاتورة سبق ترحيلها");
            yield return (SalesInvoices.Print, "طباعة فاتورة المبيعات", "المبيعات", "طباعة الفاتورة بتصميم الطباعة");

            // العملاء (منيو العملاء)
            yield return (Customers.View, "عرض العملاء", "العملاء", "قائمة العملاء");
            yield return (Customers.Create, "إضافة عميل جديد", "العملاء", "تسجيل عميل جديد");
            yield return (Customers.Edit, "تعديل بيانات عميل", "العملاء", "تعديل بيانات عميل موجود");
            yield return (Customers.Delete, "حذف عميل", "العملاء", "حذف عميل (مع مراعاة القيود)");
            yield return (Customers.Export, "تصدير العملاء", "العملاء", "تصدير قائمة العملاء إلى ملف");

            // ===================== إدارة المخازن =====================

            // الأصناف
            yield return (Products.View, "عرض الأصناف", "إدارة المخازن", "قائمة الأصناف");
            yield return (Products.Create, "إضافة صنف جديد", "إدارة المخازن", "تسجيل صنف جديد");
            yield return (Products.Edit, "تعديل صنف", "إدارة المخازن", "تعديل بيانات صنف");
            yield return (Products.Delete, "حذف صنف", "إدارة المخازن", "حذف صنف (مع مراعاة القيود)");
            yield return (Products.Export, "تصدير الأصناف", "إدارة المخازن", "تصدير قائمة الأصناف");

            // حركة المخزون
            yield return (Stock.StockAdjustments_View, "عرض تسويات الجرد", "إدارة المخازن", "قائمة تسويات المخزون");
            yield return (Stock.StockAdjustments_Edit, "إنشاء/تعديل تسوية جرد", "إدارة المخازن", "إدخال تسوية جرد جديدة أو تعديلها");
            yield return (Stock.StockTransfers_View, "عرض تحويلات المخزون", "إدارة المخازن", "قائمة تحويلات بين المخازن");
            yield return (Stock.StockTransfers_Edit, "إنشاء/تعديل تحويل مخزون", "إدارة المخازن", "تحويل كمية من مخزن لمخزن آخر");

            // ===================== الحسابات =====================

            yield return (Accounts.Chart_View, "عرض شجرة الحسابات", "الحسابات", "قائمة شجرة الحسابات");
            yield return (Accounts.Chart_Edit, "تعديل شجرة الحسابات", "الحسابات", "إضافة/تعديل/حذف حساب فى الشجرة");
            yield return (Accounts.Ledger_View, "عرض دفتر الأستاذ", "الحسابات", "استعلام حركات حساب (دفتر الأستاذ)");
            yield return (Accounts.Ledger_Export, "تصدير دفتر الأستاذ", "الحسابات", "تصدير حركات حساب إلى Excel");
            yield return (Accounts.CashReceipt, "إذن قبض نقدي", "الحسابات", "إنشاء إيصال قبض نقدي");
            yield return (Accounts.CashPayment, "إذن صرف نقدي", "الحسابات", "إنشاء إيصال صرف نقدي");
            yield return (Accounts.DebitNote, "إشعار خصم", "الحسابات", "تسجيل إشعار خصم لعميل أو مورد");
            yield return (Accounts.CreditNote, "إشعار إضافة", "الحسابات", "تسجيل إشعار إضافة لعميل أو مورد");

            // ===================== المستخدمون والصلاحيات =====================

            yield return (Security.Users_View, "عرض المستخدمين", "المستخدمون والصلاحيات", "قائمة المستخدمين");
            yield return (Security.Users_Edit, "إدارة المستخدمين", "المستخدمون والصلاحيات", "إضافة وتعديل وحذف المستخدمين");
            yield return (Security.Roles_View, "عرض الأدوار", "المستخدمون والصلاحيات", "قائمة الأدوار");
            yield return (Security.Roles_Edit, "إدارة الأدوار", "المستخدمون والصلاحيات", "إضافة/تعديل/حذف دور");
            yield return (Security.Permissions_View, "عرض الصلاحيات", "المستخدمون والصلاحيات", "قائمة الصلاحيات الأساسية");
            yield return (Security.Permissions_Edit, "إدارة الصلاحيات والأدوار", "المستخدمون والصلاحيات", "تعديل الصلاحيات وربطها بالأدوار والمستخدمين");
        }
    }
}
