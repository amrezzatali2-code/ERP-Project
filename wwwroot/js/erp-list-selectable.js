// erp-list-selectable.js
// سكربت مشترك: اختيار صف من الجدول + أزرار عرض/حذف في الشريط (نمط الخزينة)
// الاستخدام: ERP.initListSelection({ rowSelector?, showButtonId, deleteButtonId?, deleteFormId?, movementButtonId?, movementUrlAttribute?, movementTabTitle?, idAttribute?, showUrlAttribute?, tabTitleAttribute?, deleteConfirmMessage? })

(function () {
    'use strict';

    function openInTabDefault(tabId, url, title) {
        if (!url) return;
        var inFrame = (window.top !== window);
        try {
            if (inFrame) {
                window.top.postMessage({ type: 'erp-open-tab', tabId: tabId, url: url, title: title || 'تبويب' }, '*');
                return;
            }
            if (window.erpTabs && typeof window.erpTabs.openTab === 'function') {
                window.erpTabs.openTab(tabId, url, title || 'تبويب');
            } else {
                window.location.href = url;
            }
        } catch (e) {
            if (!inFrame) window.location.href = url;
        }
    }

    function init(options) {
        if (!options || !options.showButtonId) return;

        var rowSelector = options.rowSelector || '.erp-list-selectable-row';
        var showButtonId = options.showButtonId;
        var deleteButtonId = options.deleteButtonId || '';
        var deleteFormId = options.deleteFormId || '';
        var movementButtonId = options.movementButtonId || '';
        var movementUrlAttribute = options.movementUrlAttribute || 'data-movement-url';
        var movementTabTitle = options.movementTabTitle || 'حركة الصنف';
        var idAttribute = options.idAttribute || 'data-row-id';
        var showUrlAttribute = options.showUrlAttribute || 'data-show-url';
        var tabTitleAttribute = options.tabTitleAttribute || 'data-tab-title';
        var deleteConfirmMessage = options.deleteConfirmMessage;
        var openInTab = typeof options.openInTab === 'function' ? options.openInTab : openInTabDefault;

        var btnShow = document.getElementById(showButtonId);
        var btnDelete = deleteButtonId ? document.getElementById(deleteButtonId) : null;
        var btnMovement = movementButtonId ? document.getElementById(movementButtonId) : null;
        var deleteForm = deleteFormId ? document.getElementById(deleteFormId) : null;
        var rows = document.querySelectorAll(rowSelector);
        var selectedRow = null;

        function updateButtons() {
            if (!btnShow) return;
            if (!selectedRow) {
                btnShow.disabled = true;
                if (btnDelete) btnDelete.disabled = true;
                if (btnMovement) btnMovement.disabled = true;
                return;
            }
            var showUrl = selectedRow.getAttribute(showUrlAttribute);
            btnShow.disabled = !showUrl;
            if (btnDelete) btnDelete.disabled = false;
            if (btnMovement) {
                var movUrl = selectedRow.getAttribute(movementUrlAttribute);
                btnMovement.disabled = !movUrl;
            }
        }

        rows.forEach(function (row) {
            row.addEventListener('click', function (e) {
                if (e.target.closest('button') || e.target.closest('input[type="checkbox"]')) return;
                document.querySelectorAll(rowSelector).forEach(function (r) { r.classList.remove('selected'); });
                row.classList.add('selected');
                selectedRow = row;
                updateButtons();
            });
        });

        if (btnShow) {
            btnShow.addEventListener('click', function (e) {
                e.preventDefault();
                if (!selectedRow) return;
                var url = selectedRow.getAttribute(showUrlAttribute);
                if (!url) return;
                var id = selectedRow.getAttribute(idAttribute) || '';
                var title = selectedRow.getAttribute(tabTitleAttribute) || '';
                var tabId = 'list-show-' + id;
                openInTab(tabId, url, title);
            });
        }

        if (btnMovement) {
            btnMovement.addEventListener('click', function (e) {
                e.preventDefault();
                if (!selectedRow) return;
                var url = selectedRow.getAttribute(movementUrlAttribute);
                if (!url) return;
                var id = selectedRow.getAttribute(idAttribute) || '';
                var tabId = 'list-movement-' + id;
                openInTab(tabId, url, movementTabTitle);
            });
        }

        if (btnDelete && deleteForm) {
            btnDelete.addEventListener('click', function (e) {
                e.preventDefault();
                if (!selectedRow) return;
                var id = selectedRow.getAttribute(idAttribute);
                if (!id) return;
                var msg = typeof deleteConfirmMessage === 'function'
                    ? deleteConfirmMessage(id)
                    : (deleteConfirmMessage || 'تأكيد حذف هذا السجل؟');
                if (!confirm(msg)) return;
                var base = deleteForm.getAttribute('data-action-base') || '';
                deleteForm.action = base + id;
                deleteForm.submit();
            });
        }
    }

    window.ERP = window.ERP || {};
    window.ERP.initListSelection = init;
})();
