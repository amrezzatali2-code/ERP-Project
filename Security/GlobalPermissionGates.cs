using System;

namespace ERP.Security
{
    /// <summary>
    /// صلاحيات عامة (Global.*) تُطبَّق قبل الصلاحية التفصيلية؛ إزالة Global.Edit من
    /// المستخدم/الدور تمنع التعديل في كل البرنامج دون تعديل كل شاشة.
    /// </summary>
    public static class GlobalPermissionGates
    {
        public const string Open = "Global.Open";
        public const string Edit = "Global.Edit";
        public const string Delete = "Global.Delete";
        public const string Export = "Global.Export";
        public const string Print = "Global.Print";
        public const string GeneralList = "Global.GeneralList";
        /// <summary>إظهار أرقام الإجماليات في كروت القوائم وفي سطر tfoot — لا تُربط بأكشن محدد؛ تُفحص في الواجهة.</summary>
        public const string ShowSummaries = "Global.ShowSummaries";

        /// <summary>
        /// إن وُجدت قيمة: يجب أن يكون للمستخدم هذا الكود العام قبل التحقق من الصلاحية التفصيلية.
        /// للأكواد Global.* نُرجع null حتى لا يحدث حلقات.
        /// </summary>
        public static string? TryGetRequiredGlobalCode(string permissionCode)
        {
            if (string.IsNullOrWhiteSpace(permissionCode)) return null;
            var c = permissionCode.Trim();
            if (c.StartsWith("Global.", StringComparison.OrdinalIgnoreCase)) return null;

            var dot = c.LastIndexOf('.');
            if (dot < 0) return null;
            var a = c.Substring(dot + 1);

            // 1) طباعة
            if (a.IndexOf("Print", StringComparison.OrdinalIgnoreCase) >= 0)
                return Print;

            // 2) تصدير (يشمل ExportCustomerBalances وغيرها)
            if (string.Equals(a, "Export", StringComparison.OrdinalIgnoreCase) || a.StartsWith("Export", StringComparison.OrdinalIgnoreCase))
                return Export;

            // 3) حذف سجلات
            if (IsDeleteAction(a))
                return Delete;

            // 4) فتح مستند/شاشة مغلقة فقط
            if (IsOpenAction(a))
                return Open;

            // 5) تعديل وإنشاء وحفظ وواجهات API للمستندات
            if (IsEditLikeAction(a))
                return Edit;

            return null;
        }

        private static bool IsDeleteAction(string a) =>
            a.Equals("Delete", StringComparison.OrdinalIgnoreCase)
            || a.Equals("DeleteConfirmed", StringComparison.OrdinalIgnoreCase)
            || a.Equals("BulkDelete", StringComparison.OrdinalIgnoreCase)
            || a.Equals("DeleteAll", StringComparison.OrdinalIgnoreCase)
            || a.Equals("DeleteOneFromList", StringComparison.OrdinalIgnoreCase)
            || a.Equals("DeleteOneFromShow", StringComparison.OrdinalIgnoreCase);

        private static bool IsOpenAction(string a)
        {
            if (a.Equals("Open", StringComparison.OrdinalIgnoreCase)) return true;
            if (a.StartsWith("Open", StringComparison.OrdinalIgnoreCase)) return true;
            if (a.Equals("Unlock", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool IsEditLikeAction(string a)
        {
            if (a.Equals("Create", StringComparison.OrdinalIgnoreCase)) return true;
            if (a.Equals("Edit", StringComparison.OrdinalIgnoreCase)) return true;
            if (a.Equals("Update", StringComparison.OrdinalIgnoreCase)) return true;
            if (a.Equals("Import", StringComparison.OrdinalIgnoreCase)) return true;
            if (a.Equals("ProductsExcelImport", StringComparison.OrdinalIgnoreCase)) return true;
            if (a.Equals("ResetToRoleDefaults", StringComparison.OrdinalIgnoreCase)) return true;
            if (a.Equals("SeeAll", StringComparison.OrdinalIgnoreCase)) return true;
            if (a.Equals("SaveManualDiscount", StringComparison.OrdinalIgnoreCase)) return true;
            if (a.StartsWith("Save", StringComparison.OrdinalIgnoreCase)) return true;
            if (a.StartsWith("Add", StringComparison.OrdinalIgnoreCase)) return true;
            if (a.StartsWith("Remove", StringComparison.OrdinalIgnoreCase)) return true;
            if (a.StartsWith("Clear", StringComparison.OrdinalIgnoreCase)) return true;
            if (a.StartsWith("Post", StringComparison.OrdinalIgnoreCase)) return true;
            if (a.StartsWith("Recalc", StringComparison.OrdinalIgnoreCase)) return true;
            if (a.StartsWith("Convert", StringComparison.OrdinalIgnoreCase)) return true;
            if (a.StartsWith("Create", StringComparison.OrdinalIgnoreCase)) return true;
            if (a.StartsWith("DeleteLine", StringComparison.OrdinalIgnoreCase)) return true;
            if (a.StartsWith("CreateHeader", StringComparison.OrdinalIgnoreCase)) return true;
            if (a.StartsWith("UpdateHeader", StringComparison.OrdinalIgnoreCase)) return true;
            if (a.StartsWith("Zero", StringComparison.OrdinalIgnoreCase)) return true;
            if (a.StartsWith("Fix", StringComparison.OrdinalIgnoreCase)) return true;
            if (a.StartsWith("Sync", StringComparison.OrdinalIgnoreCase)) return true;
            if (a.StartsWith("Clean", StringComparison.OrdinalIgnoreCase)) return true;
            if (a.IndexOf("Diagnose", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (a.Equals("SearchProductsForReturn", StringComparison.OrdinalIgnoreCase)) return true;
            if (a.StartsWith("Get", StringComparison.OrdinalIgnoreCase) && !a.Equals("GetColumnValues", StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }
    }
}
