// ===============================
// نظام التابات للـ ERP (نسخة IFRAME + زر تحديث)
// ===============================

// تعليق عربي: حماية لمنع تشغيل نفس الملف مرتين (لو اتعمل include مرتين بالخطأ)
if (window.__ERP_TABS_INITED__) {
    console.warn("ERP Tabs: already initialized (site.js loaded twice?)");
} else {
    window.__ERP_TABS_INITED__ = true;

    (function () {

        // ===============================
        // 0) دوال مساعدة (تنظيف قيم التاب/الروابط)
        // ===============================

        // تعليق عربي: تنظيف tabId من مسافات/علامات اتجاه خفية
        function normalizeTabId(tabId) {
            if (!tabId) return '';
            return String(tabId)
                .replace(/[\u200E\u200F\u202A-\u202E]/g, '')  // علامات اتجاه خفية
                .trim();
        }

        // تعليق عربي: تحويل الرابط إلى Absolute ثابت للمقارنة
        function normalizeUrl(url) {
            if (!url) return '';
            try {
                return new URL(url, window.location.href).href;
            } catch (e) {
                return String(url).trim();
            }
        }

        // ===============================
        // 1) كشف هل الصفحة داخل IFRAME أم لا
        // ===============================
        var inFrame = false;
        try {
            inFrame = (window.self !== window.top);
        } catch (e) {
            inFrame = true;
        }

        // ===============================
        // 2) وضع "داخل iframe"
        // ===============================
        // مهم: لا نمنع أي لينك إلا لو فعلاً من نوع:
        // app-menu-link أو open-tab أو open-same-tab
        // ===============================
        if (inFrame) {

            document.addEventListener('click', function (event) {

                // تعليق عربي: نلتقط فقط الروابط المخصصة لنظام التابات
                var link = event.target.closest('a.app-menu-link, a.open-tab, a.open-same-tab');
                if (!link) return;

                // ------------------------------
                // (أ) open-same-tab
                // ------------------------------
                if (link.classList.contains('open-same-tab')) {

                    var tabId = normalizeTabId(link.getAttribute('data-tab-id') || '');
                    var tabTitle = (link.getAttribute('data-tab-title') || link.textContent || '').trim();
                    var url = normalizeUrl(link.getAttribute('data-url'));

                    // تعليق عربي: لو tabId فاضي ⇒ لا نفتح تاب جديد عشوائي (هذا سبب التكرار)
                    if (!tabId) {
                        console.warn("⚠ open-same-tab: data-tab-id فارغ → تم إلغاء العملية لمنع تابات مكررة");
                        return;
                    }

                    if (!url) {
                        console.warn("⚠ open-same-tab: data-url غير موجود/فارغ");
                        return;
                    }

                    // تعليق عربي: الآن فقط نمنع السلوك الافتراضي
                    event.preventDefault();

                    window.top.postMessage({
                        type: 'erp-open-tab',
                        tabId: tabId,
                        title: tabTitle,
                        url: url
                    }, '*');

                    return;
                }

                // ------------------------------
                // (ب) open-tab / app-menu-link
                // ------------------------------
                var tabId2 = normalizeTabId(link.getAttribute('data-tab-id') || '');
                var tabTitle2 = (link.getAttribute('data-tab-title') || link.textContent || '').trim();
                var url2 = normalizeUrl(link.href);

                // تعليق عربي: لو tabId فاضي هنا ⇒ نخلي الرابط يفتح عادي داخل نفس iframe
                // لأن فتح تاب بـ id عشوائي = تابات مكررة
                if (!tabId2) {
                    // لا نعمل preventDefault
                    console.warn("⚠ open-tab/app-menu-link: data-tab-id فارغ → سيتم ترك الرابط يفتح بشكل طبيعي داخل نفس التاب");
                    return;
                }

                if (!url2) return;

                event.preventDefault();

                window.top.postMessage({
                    type: 'erp-open-tab',
                    tabId: tabId2,
                    title: tabTitle2,
                    url: url2
                }, '*');
            });

            return;
        }

        // ===============================
        // 3) من هنا يبدأ منطق التابات في Layout الرئيسي
        // ===============================
        var tabsBar = document.getElementById('appTabsBar');
        var tabsContainer = document.getElementById('appTabsContainer');
        var homeContent = document.getElementById('homeContent');

        if (!tabsBar || !tabsContainer) return;

        // إظهار منطقة التابات
        function enableTabsMode() {
            document.body.classList.add('has-tabs');
            if (homeContent) homeContent.style.display = 'none';
        }

        // إخفاء منطقة التابات لو مفيش ولا تاب
        function disableTabsModeIfNoTabs() {
            var count = tabsBar.querySelectorAll('.app-tab').length;
            if (count === 0) {
                document.body.classList.remove('has-tabs');
                if (homeContent) homeContent.style.display = '';
            }
        }

        // ===============================
        // فتح / تحديث تاب
        // ===============================
        function openTab(tabId, url, title) {

            tabId = normalizeTabId(tabId);
            url = normalizeUrl(url);

            // تعليق عربي: ممنوع إنشاء تاب عشوائي لو tabId فاضي (ده كان سبب التكرار)
            if (!tabId) {
                console.warn("⚠ openTab تم استدعاؤه بـ tabId فارغ → تم إلغاء فتح التاب لمنع التكرار");
                return;
            }

            var existingTab = tabsBar.querySelector('[data-tab-id="' + tabId + '"]');

            // لو التاب موجود → فعّله وحدث الـ URL لو اتغير
            if (existingTab) {
                var existingFrame = tabsContainer.querySelector('.app-tab-frame[data-tab-id="' + tabId + '"]');

                if (existingFrame) {
                    var currentSrc = normalizeUrl(existingFrame.getAttribute('src') || existingFrame.src);
                    if (currentSrc !== url) {
                        existingFrame.src = url;
                    }
                }

                enableTabsMode();
                activateTab(tabId);
                return;
            }

            // إنشاء تاب جديد
            var tabButton = document.createElement('button');
            tabButton.type = 'button';
            tabButton.className = 'app-tab btn btn-sm btn-light';
            tabButton.setAttribute('data-tab-id', tabId);
            tabButton.innerHTML =
                '<span class="app-tab-title">' + (title || 'تبويب جديد') + '</span>' +
                '<span class="app-tab-close" title="إغلاق">&times;</span>';

            tabsBar.appendChild(tabButton);

            var frame = document.createElement('iframe');
            frame.className = 'app-tab-frame';
            frame.setAttribute('data-tab-id', tabId);
            frame.src = url;
            frame.loading = 'lazy';

            tabsContainer.appendChild(frame);

            enableTabsMode();
            activateTab(tabId);
        }

        // تنشيط تاب
        function activateTab(tabId) {
            tabId = normalizeTabId(tabId);

            var allTabs = tabsBar.querySelectorAll('.app-tab');
            allTabs.forEach(function (tab) {
                tab.classList.toggle('active', normalizeTabId(tab.getAttribute('data-tab-id')) === tabId);
            });

            var allFrames = tabsContainer.querySelectorAll('.app-tab-frame');
            allFrames.forEach(function (frame) {
                frame.classList.toggle('d-none', normalizeTabId(frame.getAttribute('data-tab-id')) !== tabId);
            });
        }

        // إغلاق تاب
        function closeTab(tabId) {
            tabId = normalizeTabId(tabId);

            var tab = tabsBar.querySelector('[data-tab-id="' + tabId + '"]');
            if (!tab) return;

            var wasActive = tab.classList.contains('active');
            tab.remove();

            var frame = tabsContainer.querySelector('.app-tab-frame[data-tab-id="' + tabId + '"]');
            if (frame) frame.remove();

            if (wasActive) {
                var lastTab = tabsBar.querySelector('.app-tab:last-child');
                if (lastTab) {
                    activateTab(lastTab.getAttribute('data-tab-id'));
                }
            }

            disableTabsModeIfNoTabs();
        }

        // تحديث التاب الحالي
        function refreshCurrentTab() {
            var activeFrame = tabsContainer.querySelector('.app-tab-frame:not(.d-none)');
            if (!activeFrame) return;
            activeFrame.src = activeFrame.src;
        }

        // التعامل مع كليك شريط التابات
        tabsBar.addEventListener('click', function (event) {
            var target = event.target;

            if (target.classList.contains('app-tab-close')) {
                var tab = target.closest('.app-tab');
                closeTab(tab.getAttribute('data-tab-id'));
                return;
            }

            var tabButton = target.closest('.app-tab');
            if (tabButton) {
                activateTab(tabButton.getAttribute('data-tab-id'));
            }
        });

        // ربط روابط القائمة الرئيسية بنظام التابات
        document.querySelectorAll('.app-menu-link').forEach(function (link) {
            link.addEventListener('click', function (event) {
                event.preventDefault();

                var tabId = normalizeTabId(link.getAttribute('data-tab-id') || '');
                var title = (link.getAttribute('data-tab-title') || link.textContent || '').trim();
                var url = normalizeUrl(link.href);

                // تعليق عربي: لو tabId فاضي لا نفتح تاب
                if (!tabId) {
                    console.warn("⚠ app-menu-link: data-tab-id فارغ → لن يتم فتح تبويب");
                    return;
                }

                openTab(tabId, url, title);
            });
        });

        // استقبال رسائل من صفحات IFRAME
        window.addEventListener('message', function (event) {
            var data = event.data;
            if (!data || data.type !== 'erp-open-tab') return;

            var tabId = normalizeTabId(data.tabId || '');
            var url = normalizeUrl(data.url);
            var title = (data.title || '').trim();

            if (!tabId) {
                console.warn("⚠ message: tabId فارغ → لن يتم فتح تبويب");
                return;
            }

            if (!url) return;

            openTab(tabId, url, title);
        });

        // زر التحديث في الـ Layout الرئيسي
        document.addEventListener('DOMContentLoaded', function () {
            var btnRefresh = document.getElementById('btnRefreshTab');
            if (btnRefresh) {
                btnRefresh.addEventListener('click', function (event) {
                    event.preventDefault();
                    refreshCurrentTab();
                });
            }
        });

        // إتاحة الدوال للاستخدام خارجيًا
        window.erpTabs = {
            openTab: openTab,
            activateTab: activateTab,
            closeTab: closeTab,
            refreshCurrentTab: refreshCurrentTab
        };

    })();
}
