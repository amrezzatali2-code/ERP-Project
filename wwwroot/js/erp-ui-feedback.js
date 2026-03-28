/**
 * ERP — مودال تأكيد/تنبيه موحّد الشكل + توست (يفضّل استخدامه بدل alert/confirm)
 */
(function (w) {
    'use strict';

    w.ERP = w.ERP || {};

    function ensureToastHost() {
        var id = 'erpToastHost';
        var el = document.getElementById(id);
        if (!el) {
            el = document.createElement('div');
            el.id = id;
            el.className = 'erp-toast-host';
            el.setAttribute('aria-live', 'polite');
            document.body.appendChild(el);
        }
        return el;
    }

    /**
     * @param {string} text
     * @param {string} [type] success | danger | warning | muted
     * @param {number} [durationMs] الافتراضي 4000؛ نجاح الترحيل يُنصح بـ 1000
     */
    w.ERP.showToast = function (text, type, durationMs) {
        if (!text) return;
        type = type || 'muted';
        var ms = typeof durationMs === 'number' && durationMs >= 0 ? durationMs : 4000;
        var container = ensureToastHost();
        var el = document.createElement('div');
        el.className = 'erp-toast text-' + type;
        el.textContent = text;
        container.appendChild(el);
        setTimeout(function () {
            el.style.opacity = '0';
            el.style.transition = 'opacity 0.25s ease';
            setTimeout(function () {
                if (el.parentNode) el.parentNode.removeChild(el);
            }, 250);
        }, ms);
    };

    /**
     * @param {{ title?: string, message?: string, confirmText?: string, cancelText?: string }} opts
     * @returns {Promise<boolean>}
     */
    /**
     * رسالة بزر واحد (بديل alert) — نفس شكل مودال التأكيد
     * @param {{ title?: string, message?: string, okText?: string }} opts
     * @returns {Promise<void>}
     */
    w.ERP.showAlertModal = function (opts) {
        opts = opts || {};
        var title = opts.title || 'تنبيه';
        var message = opts.message || '';
        var okText = opts.okText || 'حسناً';
        var titleId = 'erpAlertTitle_' + Date.now();

        return new Promise(function (resolve) {
            var backdrop = document.createElement('div');
            backdrop.className = 'erp-confirm-backdrop';
            backdrop.setAttribute('role', 'dialog');
            backdrop.setAttribute('aria-modal', 'true');
            backdrop.setAttribute('aria-labelledby', titleId);

            var dialog = document.createElement('div');
            dialog.className = 'erp-confirm-dialog';

            var header = document.createElement('div');
            header.className = 'erp-confirm-header';
            var h = document.createElement('h2');
            h.id = titleId;
            h.className = 'erp-confirm-title';
            h.textContent = title;
            header.appendChild(h);

            var body = document.createElement('div');
            body.className = 'erp-confirm-body';
            body.textContent = message;

            var footer = document.createElement('div');
            footer.className = 'erp-confirm-footer erp-confirm-footer-single';

            var btnOk = document.createElement('button');
            btnOk.type = 'button';
            btnOk.className = 'btn btn-erp btn-erp-primary btn-erp-sm erp-confirm-ok';
            btnOk.textContent = okText;

            footer.appendChild(btnOk);
            dialog.appendChild(header);
            dialog.appendChild(body);
            dialog.appendChild(footer);
            backdrop.appendChild(dialog);
            document.body.appendChild(backdrop);

            function cleanup() {
                document.removeEventListener('keydown', onKey);
                if (backdrop.parentNode) backdrop.parentNode.removeChild(backdrop);
                resolve();
            }

            function onKey(e) {
                if (e.key === 'Escape') {
                    e.preventDefault();
                    cleanup();
                }
            }

            document.addEventListener('keydown', onKey);
            btnOk.addEventListener('click', cleanup);
            backdrop.addEventListener('click', function (e) {
                if (e.target === backdrop) cleanup();
            });

            setTimeout(function () {
                try { btnOk.focus(); } catch (x) { }
            }, 50);
        });
    };

    w.ERP.showConfirmModal = function (opts) {
        opts = opts || {};
        var title = opts.title || 'تأكيد';
        var message = opts.message || '';
        var confirmText = opts.confirmText || 'تأكيد';
        var cancelText = opts.cancelText || 'إلغاء';
        var titleId = 'erpConfirmTitle_' + Date.now();

        return new Promise(function (resolve) {
            var backdrop = document.createElement('div');
            backdrop.className = 'erp-confirm-backdrop';
            backdrop.setAttribute('role', 'dialog');
            backdrop.setAttribute('aria-modal', 'true');
            backdrop.setAttribute('aria-labelledby', titleId);

            var dialog = document.createElement('div');
            dialog.className = 'erp-confirm-dialog';

            var header = document.createElement('div');
            header.className = 'erp-confirm-header';
            var h = document.createElement('h2');
            h.id = titleId;
            h.className = 'erp-confirm-title';
            h.textContent = title;
            header.appendChild(h);

            var body = document.createElement('div');
            body.className = 'erp-confirm-body';
            body.textContent = message;

            var footer = document.createElement('div');
            footer.className = 'erp-confirm-footer';

            var btnCancel = document.createElement('button');
            btnCancel.type = 'button';
            btnCancel.className = 'btn btn-erp btn-erp-secondary btn-erp-sm erp-confirm-cancel';
            btnCancel.textContent = cancelText;

            var btnOk = document.createElement('button');
            btnOk.type = 'button';
            btnOk.className = 'btn btn-erp btn-erp-primary btn-erp-sm erp-confirm-ok';
            btnOk.textContent = confirmText;

            /* RTL: العنصر الأول يظهر يميناً — نضع «ترحيل» يمين «إلغاء» */
            footer.appendChild(btnOk);
            footer.appendChild(btnCancel);

            dialog.appendChild(header);
            dialog.appendChild(body);
            dialog.appendChild(footer);
            backdrop.appendChild(dialog);
            document.body.appendChild(backdrop);

            function cleanup(result) {
                document.removeEventListener('keydown', onKey);
                if (backdrop.parentNode) backdrop.parentNode.removeChild(backdrop);
                resolve(result);
            }

            function onKey(e) {
                if (e.key === 'Escape') {
                    e.preventDefault();
                    cleanup(false);
                }
            }

            document.addEventListener('keydown', onKey);
            btnCancel.addEventListener('click', function () { cleanup(false); });
            btnOk.addEventListener('click', function () { cleanup(true); });
            backdrop.addEventListener('click', function (e) {
                if (e.target === backdrop) cleanup(false);
            });

            setTimeout(function () {
                try { btnOk.focus(); } catch (x) { }
            }, 50);
        });
    };
})(typeof window !== 'undefined' ? window : this);
