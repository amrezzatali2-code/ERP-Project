/**
 * تقليل/منع نافذة المتصفح «Saved info» و Autofill (Chrome يتجاهل autocomplete="off" لحقول تشبه الأسماء).
 * - قيمة autocomplete عشوائية لكل تحميل (لا تطابق ملفّات العنوان).
 * - سمات شائعة لمديري كلمات المرور.
 * - readonly حتى أول mousedown أو focus ثم يُزال (Chrome لا يعرض الحفظ على حقول readonly غالباً).
 * - استثناء: data-erp-allow-autocomplete — أو data-erp-no-readonly-shield (إكمال فقط بدون readonly).
 */
(function () {
    'use strict';

    if (!window.__ERP_AC_TOKEN__) {
        window.__ERP_AC_TOKEN_ = 'erp-' + Date.now().toString(36) + '-' + Math.random().toString(36).slice(2, 10);
    }
    var acToken = window.__ERP_AC_TOKEN_;

    var shielded = typeof WeakSet !== 'undefined' ? new WeakSet() : null;
    var shieldedFallback = typeof WeakSet === 'undefined' ? [] : null;

    function isShielded(el) {
        if (shielded) return shielded.has(el);
        return shieldedFallback.indexOf(el) >= 0;
    }

    function markShielded(el) {
        if (shielded) shielded.add(el);
        else if (shieldedFallback.indexOf(el) < 0) shieldedFallback.push(el);
    }

    function shouldSkip(el) {
        if (!el || el.nodeType !== 1) return true;
        if (el.hasAttribute('data-erp-allow-autocomplete')) return true;
        if (typeof el.closest === 'function' && el.closest('[data-erp-allow-autocomplete]')) return true;
        return false;
    }

    function unlockField(el) {
        try {
            el.removeAttribute('readonly');
            el.readOnly = false;
        } catch (e) { /* */ }
    }

    /** يمنع Chrome من ربط الحقل بملف «Saved info» حتى أول تفاعل */
    function attachReadonlyShield(el) {
        if (shouldSkip(el) || el.hasAttribute('data-erp-no-readonly-shield')) return;
        if (isShielded(el)) return;
        var t = (el.getAttribute('type') || '').toLowerCase();
        if (t === 'hidden' || t === 'submit' || t === 'button' || t === 'reset' || t === 'image' ||
            t === 'checkbox' || t === 'radio' || t === 'file' || t === 'range' || t === 'color') return;
        if (t === 'number' || t === 'date' || t === 'time' || t === 'datetime-local' || t === 'month' || t === 'week') return;
        if (el.readOnly || el.disabled) return;

        markShielded(el);
        try {
            el.readOnly = true;
        } catch (e) { return; }

        var done = false;
        function onceUnlock() {
            if (done) return;
            done = true;
            unlockField(el);
        }
        el.addEventListener('mousedown', onceUnlock, { capture: true, once: true });
        el.addEventListener('focus', onceUnlock, { capture: true, once: true });
        el.addEventListener('touchstart', onceUnlock, { capture: true, once: true });
    }

    function applyToRoot(root) {
        if (!root || !root.querySelectorAll) return;

        root.querySelectorAll('form').forEach(function (f) {
            if (shouldSkip(f)) return;
            f.setAttribute('autocomplete', 'off');
        });

        root.querySelectorAll('input, textarea, select').forEach(function (el) {
            if (shouldSkip(el)) return;
            var t = (el.getAttribute('type') || '').toLowerCase();
            if (t === 'hidden' || t === 'submit' || t === 'button' || t === 'reset' || t === 'image') return;

            el.setAttribute('autocomplete', acToken);
            try {
                el.setAttribute('data-lpignore', 'true');
                el.setAttribute('data-1p-ignore', 'true');
                el.setAttribute('data-bwignore', 'true');
            } catch (e) { /* */ }

            if (el.tagName === 'TEXTAREA') {
                attachReadonlyShield(el);
                return;
            }

            if (t === '' || t === 'text' || t === 'search' || t === 'tel' || t === 'email' || t === 'url') {
                attachReadonlyShield(el);
            }
        });
    }

    var rafScheduled = null;
    function scheduleApply() {
        if (rafScheduled != null) return;
        rafScheduled = requestAnimationFrame(function () {
            rafScheduled = null;
            applyToRoot(document);
        });
    }

    function init() {
        applyToRoot(document);
        try {
            var obs = new MutationObserver(scheduleApply);
            obs.observe(document.documentElement, { childList: true, subtree: true });
        } catch (e) { /* */ }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
