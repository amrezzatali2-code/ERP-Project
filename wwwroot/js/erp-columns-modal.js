/**
 * مودال اختيار الأعمدة الظاهرة — إضافة "اختيار الكل" وتحديث حالته
 * يعمل مع أي مودال يحتوي على .erp-columns-modal-list ويحتوي تشيك بوكسات لها data-col-key
 */
(function () {
    "use strict";

    var SELECT_ALL_ROW_CLASS = "erp-columns-select-all-row";
    var SELECT_ALL_ID_PREFIX = "erpColumnsSelectAll_";

    function ensureSelectAll(listEl) {
        if (!listEl || !listEl.classList.contains("erp-columns-modal-list")) return;
        if (listEl.querySelector("." + SELECT_ALL_ROW_CLASS)) return;

        var id = SELECT_ALL_ID_PREFIX + (listEl.id || "list") + "_" + Math.random().toString(36).slice(2, 8);
        var row = document.createElement("div");
        row.className = "form-check " + SELECT_ALL_ROW_CLASS;
        row.innerHTML =
            '<input type="checkbox" class="form-check-input" id="' + id + '" aria-label="اختيار الكل">' +
            '<label class="form-check-label" for="' + id + '">اختيار الكل</label>';
        listEl.insertBefore(row, listEl.firstChild);

        var selectAllCb = row.querySelector('input[type="checkbox"]');
        var colCheckboxes = function () { return listEl.querySelectorAll('input[type="checkbox"][data-col-key]'); };

        function updateSelectAllState() {
            var list = colCheckboxes();
            selectAllCb.checked = list.length > 0 && Array.prototype.every.call(list, function (c) { return c.checked; });
        }
        updateSelectAllState();

        selectAllCb.addEventListener("change", function () {
            var checked = selectAllCb.checked;
            colCheckboxes().forEach(function (cb) { cb.checked = checked; });
        });

        listEl.addEventListener("change", function (e) {
            var t = e.target;
            if (t && t.getAttribute("data-col-key") !== null) updateSelectAllState();
        });
    }

    document.addEventListener("show.bs.modal", function (e) {
        var modal = e.target;
        if (!modal || !modal.querySelector) return;
        var list = modal.querySelector(".erp-columns-modal-list");
        if (list) ensureSelectAll(list);
    });
})();
