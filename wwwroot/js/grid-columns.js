// grid-columns.js
// مسئول عن إظهار/إخفاء أعمدة الجدول + حفظ الاختيار في localStorage
// يعمل مع أي جدول له data-grid-id و checkboxes من كلاس erp-grid-col-toggle

(function () {

    function applyColumnVisibility(gridId, columnKey, visible) {
        var table = document.getElementById(gridId);
        if (!table) return;

        var cells = table.querySelectorAll('[data-column-key="' + columnKey + '"]');
        cells.forEach(function (cell) {
            if (visible) {
                cell.classList.remove('d-none');
            } else {
                cell.classList.add('d-none');
            }
        });
    }

    function loadState(gridId) {
        try {
            var raw = window.localStorage.getItem('grid-cols-' + gridId);
            if (!raw) return null;
            return JSON.parse(raw);
        } catch (e) {
            return null;
        }
    }

    function saveState(gridId, keysVisible) {
        try {
            window.localStorage.setItem('grid-cols-' + gridId, JSON.stringify(keysVisible));
        } catch (e) {
            // تجاهل أي خطأ في التخزين
        }
    }

    document.addEventListener('DOMContentLoaded', function () {

        var toggles = document.querySelectorAll('.erp-grid-col-toggle');
        if (!toggles.length) return;

        // نجمع التوجلات حسب الجدول
        var grids = {};

        toggles.forEach(function (chk) {
            var gridId = chk.getAttribute('data-grid-id');
            var colKey = chk.getAttribute('data-column-key');
            if (!gridId || !colKey) return;

            if (!grids[gridId]) {
                grids[gridId] = [];
            }
            grids[gridId].push(chk);
        });

        Object.keys(grids).forEach(function (gridId) {

            var group = grids[gridId];
            var saved = loadState(gridId);

            group.forEach(function (chk) {
                var colKey = chk.getAttribute('data-column-key');

                // لو فيه حالة محفوظة نستخدمها
                if (saved && Array.isArray(saved)) {
                    chk.checked = saved.indexOf(colKey) !== -1;
                }

                // نطبق الرؤية لأول مرة
                applyColumnVisibility(gridId, colKey, chk.checked);

                // عند التغيير نطبّق ونحفظ
                chk.addEventListener('change', function () {
                    applyColumnVisibility(gridId, colKey, chk.checked);

                    // نحسب الأعمدة الظاهرة ونحفظها
                    var visibleKeys = group
                        .filter(function (c) { return c.checked; })
                        .map(function (c) { return c.getAttribute('data-column-key'); });

                    saveState(gridId, visibleKeys);
                });
            });
        });
    });

})();
