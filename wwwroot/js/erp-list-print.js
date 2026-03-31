/**
 * ERP — طباعة القوائم (صفحة Print منفصلة + printCols)
 *
 * الاستخدام من القائمة:
 *   1) في الـ View: مسار الطباعة من Url.Action("Print", "Controller", routeValues)
 *   2) جدول القائمة: thead th[data-col] بدون data-col-fixed للأعمدة القابلة للإخفاء
 *   3) localStorage: نفس المفتاح المستخدم في «اختيار الأعمدة» (مصفوفة المفاتيح المخفية JSON)
 *   4) عند النقر: ERP.openListPrint(printUrl, { tableId: "...", storageKey: "..." })
 *
 * على الخادم: IActionResult Print(..., string printCols = null)
 *   + ListPrintColumnParser.ParsePrintColumns(printCols, allowedOrder, aliases)
 *
 * قوائم تطبع عبر window.print() على نفس الصفحة (.erp-print-area) لا تستخدم هذا الملف
 * إلا إذا أضفت لاحقاً Print منفصل بنفس النمط.
 */
(function (w) {
    w.ERP = w.ERP || {};

    /**
     * @param {string} baseUrl
     * @param {{ tableId: string, storageKey: string }} opts
     * @returns {string}
     */
    w.ERP.buildListPrintUrl = function (baseUrl, opts) {
        if (!baseUrl || baseUrl === "#") return baseUrl;
        var u;
        try {
            u = new URL(baseUrl, w.location.origin);
        } catch (e) {
            return baseUrl;
        }
        opts = opts || {};
        var table = opts.tableId ? document.getElementById(opts.tableId) : null;
        var storageKey = opts.storageKey;
        if (!table || !storageKey) return u.toString();

        var keys = [];
        table.querySelectorAll("thead th[data-col]:not([data-col-fixed])").forEach(function (th) {
            var k = th.getAttribute("data-col");
            if (k) keys.push(k);
        });

        var hidden = [];
        try {
            var s = w.localStorage.getItem(storageKey);
            if (s) hidden = JSON.parse(s);
        } catch (e) {
            hidden = [];
        }
        if (!Array.isArray(hidden)) hidden = [];

        var visible = keys.filter(function (k) {
            return hidden.indexOf(k) < 0;
        });

        if (visible.length > 0 && visible.length < keys.length)
            u.searchParams.set("printCols", visible.join(","));
        else u.searchParams.delete("printCols");

        return u.toString();
    };

    /**
     * @param {string} baseUrl
     * @param {{ tableId: string, storageKey: string }} opts
     * @returns {Window|null}
     */
    w.ERP.openListPrint = function (baseUrl, opts) {
        var url = w.ERP.buildListPrintUrl(baseUrl, opts);
        if (!url || url === "#") return null;
        var win = w.open(url, "_blank", "noopener,noreferrer");
        if (win) win.focus();
        return win;
    };
})(window);
