window.ERP = window.ERP || {};

window.ERP.initProductBalancesDelegateTools = function initProductBalancesDelegateTools(cfg) {
    var exportUrl = (cfg && cfg.exportUrl) ? String(cfg.exportUrl) : "";
    if (!exportUrl) return;

    function buildExportUrl(format, extra) {
        var form = document.getElementById("filtersForm");
        if (!form) return "";
        var u = new URL(exportUrl, window.location.origin);
        var fd = new FormData(form);
        fd.forEach(function (v, k) {
            if (typeof v === "string" && v !== "") u.searchParams.set(k, v);
        });
        if (format) u.searchParams.set("format", format);
        if (extra) {
            Object.keys(extra).forEach(function (k) {
                var val = extra[k];
                if (val !== null && val !== undefined && val !== "") u.searchParams.set(k, val);
            });
        }
        return u.toString();
    }

    (function bindSinglePrintClick() {
        var printBtn = document.getElementById("erpPbPrintPage");
        if (!printBtn || printBtn.dataset.pbPrintBound === "1") return;
        printBtn.dataset.pbPrintBound = "1";
        var printInProgress = false;
        printBtn.onclick = function (e) {
            e.preventDefault();
            e.stopPropagation();
            if (printInProgress) return false;
            printInProgress = true;
            printBtn.disabled = true;

            var form = document.getElementById("filtersForm");
            if (!form) {
                window.print();
                return false;
            }
            var u = new URL(window.location.href);
            var fd = new FormData(form);
            u.search = "";
            fd.forEach(function (v, k) {
                if (typeof v === "string" && v !== "") u.searchParams.set(k, v);
            });
            u.searchParams.set("loadReport", "true");
            u.searchParams.set("page", "1");
            u.searchParams.set("pageSize", "0");
            u.searchParams.set("autoPrint", "1");
            window.location.href = u.toString();
            return false;
        };
    })();

    function escapeHtml(s) {
        if (!s) return "";
        return String(s)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/"/g, "&quot;");
    }

    async function runDelegatePrint(cols, printWindow) {
        if (cols !== 1 && cols !== 2 && cols !== 3) cols = 2;
        var form = document.getElementById("filtersForm");
        if (!form) {
            window.print();
            return;
        }
        var polSel = document.getElementById("erpPbDelegatePolicy");
        var pol = polSel ? parseInt(polSel.value, 10) : 1;
        if (isNaN(pol) || pol < 1 || pol > 10) pol = 1;

        var url = buildExportUrl("json", { exportKind: "delegate", delegatePolicyId: String(pol) });
        if (!url) return;

        var w = printWindow || window.open("", "_blank");
        if (!w) {
            window.alert("تعذر فتح نافذة الطباعة (تحقق من السماح بالنوافذ المنبثقة).");
            return;
        }
        try {
            w.document.open();
            w.document.write("<!doctype html><html><head><meta charset=\"utf-8\"></head><body dir=\"rtl\" style=\"font-family:Segoe UI,Tahoma,sans-serif;padding:16px;\">جاري تجهيز قائمة الطباعة...</body></html>");
            w.document.close();
        } catch (e) {}

        var resp = await fetch(url, { credentials: "same-origin" });
        if (!resp.ok) {
            try { w.close(); } catch (e) {}
            window.alert("تعذر تحميل بيانات قائمة الصيدلي للطباعة.");
            return;
        }
        var data = await resp.json();
        var policyName = (data && data.policyName) ? String(data.policyName) : ("سياسة " + pol);
        var rows = (data && Array.isArray(data.rows)) ? data.rows : [];

        var columnsData = Array.from({ length: cols }, function () { return []; });
        rows.forEach(function (r, idx) {
            columnsData[idx % cols].push(r);
        });

        var html = "<!DOCTYPE html><html dir=\"rtl\"><head><meta charset=\"utf-8\"/><title>قائمة الصيدلي</title>" +
            "<style>" +
            "body{font-family:Segoe UI,Tahoma,sans-serif;padding:12px;direction:rtl;color:#0f172a;}" +
            ".head{margin-bottom:10px;font-weight:700;font-size:18px;}" +
            ".grid{display:grid;grid-template-columns:repeat(" + cols + ",minmax(0,1fr));gap:12px;align-items:start;}" +
            ".col-table{width:100%;border-collapse:collapse;table-layout:fixed;}" +
            ".col-table th,.col-table td{border:1px solid #6366f1;padding:6px 8px;font-size:12px;vertical-align:middle;}" +
            ".col-table thead th{background:#eef2ff;font-weight:700;text-align:center;}" +
            ".col-table td.name{text-align:right;font-weight:600;word-break:break-word;}" +
            ".col-table td.num{text-align:center;font-weight:700;white-space:nowrap;}" +
            "</style>" +
            "<script>window.onafterprint=function(){window.close();};window.addEventListener('load',function(){setTimeout(function(){window.print();},80);});<\/script>" +
            "</head><body>" +
            "<div class=\"head\">قائمة صيدلي — " + escapeHtml(policyName) + "</div>" +
            "<div class=\"grid\">";

        columnsData.forEach(function (bucket) {
            html += "<table class=\"col-table\"><thead><tr>" +
                "<th style=\"width:58%;\">اسم الصنف</th>" +
                "<th style=\"width:21%;\">سعر الجمهور</th>" +
                "<th style=\"width:21%;\">الخصم</th>" +
                "</tr></thead><tbody>";
            bucket.forEach(function (r) {
                var nm = escapeHtml(r.name || "");
                var pr = Number(r.price || 0).toFixed(2);
                var dc = Number(r.discount || 0).toFixed(2) + "%";
                html += "<tr><td class=\"name\">" + nm + "</td><td class=\"num\">" + pr + "</td><td class=\"num\">" + dc + "</td></tr>";
            });
            if (bucket.length === 0) {
                html += "<tr><td colspan=\"3\" class=\"num\">—</td></tr>";
            }
            html += "</tbody></table>";
        });

        html += "</div></body></html>";

        w.document.open();
        w.document.write(html);
        w.document.close();
    }

    document.getElementById("erpPbPrintListOpen")?.addEventListener("click", function () {
        var colsEl = document.getElementById("erpPbDelegatePrintColsInline");
        var cols = colsEl ? parseInt(colsEl.value, 10) : 2;
        var w = window.open("", "_blank");
        runDelegatePrint(cols, w);
    });

    document.getElementById("pbDelegatePrintRun")?.addEventListener("click", function () {
        var colSel = document.getElementById("pbDelegatePrintCols");
        var cols = colSel ? parseInt(colSel.value, 10) : 2;
        var w = window.open("", "_blank");
        runDelegatePrint(cols, w);
        var modalEl = document.getElementById("pbDelegatePrintModal");
        if (modalEl && window.bootstrap && typeof bootstrap.Modal === "function")
            bootstrap.Modal.getInstance(modalEl)?.hide();
    });

    function runDelegateExport(format) {
        var polEl = document.getElementById("erpPbDelegatePolicy");
        var pol = polEl ? parseInt(polEl.value, 10) : 1;
        if (isNaN(pol) || pol < 1 || pol > 10) pol = 1;
        var colsEl = document.getElementById("erpPbDelegatePrintColsInline");
        var cols = colsEl ? parseInt(colsEl.value, 10) : 2;
        if (isNaN(cols) || cols < 1 || cols > 3) cols = 2;
        var url = buildExportUrl(format, {
            exportKind: "delegate",
            delegatePolicyId: String(pol),
            delegateCols: String(cols)
        });
        if (!url) return;

        // PDF: نجبر التنزيل كملف لتسهيل فتحه ببرنامج النظام الافتراضي بدل العرض داخل التبويب.
        if (format === "pdf") {
            fetch(url, { credentials: "same-origin" })
                .then(function (resp) {
                    if (!resp.ok) throw new Error("download-failed");
                    return Promise.all([resp.blob(), Promise.resolve(resp.headers.get("content-disposition") || "")]);
                })
                .then(function (arr) {
                    var blob = arr[0];
                    var cd = arr[1];
                    var fileName = "قائمة_اصناف_صيدلية.pdf";
                    var m = /filename\*=UTF-8''([^;]+)|filename="?([^\";]+)"?/i.exec(cd || "");
                    if (m) fileName = decodeURIComponent((m[1] || m[2] || fileName).trim());
                    var a = document.createElement("a");
                    var objUrl = URL.createObjectURL(blob);
                    a.href = objUrl;
                    a.download = fileName;
                    document.body.appendChild(a);
                    a.click();
                    a.remove();
                    setTimeout(function () { URL.revokeObjectURL(objUrl); }, 1500);
                })
                .catch(function () {
                    window.location.href = url;
                });
            return;
        }

        window.location.href = url;
    }

    document.getElementById("erpPbExportListRun")?.addEventListener("click", function () {
        var fmtEl = document.getElementById("erpPbDelegateListFormat");
        var fmt = fmtEl ? (fmtEl.value || "excel") : "excel";
        if (fmt !== "pdf") fmt = "excel";
        runDelegateExport(fmt);
    });

    (function autoPrintFullPage() {
        var qs = new URLSearchParams(window.location.search);
        if (qs.get("autoPrint") !== "1") return;
        setTimeout(function () { window.print(); }, 150);
    })();
};
