// ===============================
// نظام التابات للـ ERP (نسخة IFRAME + زر تحديث)
// ===============================
(function () {

    // ===============================
    // 0) كشف هل الصفحة داخل IFRAME أم لا
    // ===============================
    var inFrame = false;
    try {
        inFrame = (window.self !== window.top);   // لو داخل iframe → true
    } catch (e) {
        inFrame = true;
    }

    // ===============================
    // 1) وضع "داخل iframe"
    //    لو أنا جوّه التاب، أى لينك من نوع app-menu-link
    //    هيبعت رسالة للنافذة الأم تفتح/تفعّل التاب المطلوب
    // ===============================
    if (inFrame) {

        // تعليق عربي: حدث كليك عام على الصفحة داخل الـ iframe
        document.addEventListener('click', function (event) {
            // نلتقط روابط القائمة + روابط فتح التابات داخل الصفحات (مثل: عرض الفاتورة)
            var link = event.target.closest('a.app-menu-link, a.open-tab');

            if (!link) return; // لو مش رابط من نوع app-menu-link نخرج

            event.preventDefault(); // منع التنقل العادي داخل نفس التاب

            var tabId = link.getAttribute('data-tab-id') || '';
            var tabTitle = link.getAttribute('data-tab-title') || link.textContent.trim();
            var url = link.href; // عنوان Show أو أي صفحة أخرى

            // تعليق: إرسال رسالة للنافذة الأساسية (layout الرئيسي)
            window.top.postMessage({
                type: 'erp-open-tab',
                tabId: tabId,
                title: tabTitle,
                url: url
            }, '*');
        });

        // مهم: لا نكمل منطق التابات داخل الـ iframe
        return;
    }

    // ===============================
    // 2) من هنا الكود يعمل في الصفحة الرئيسية فقط (Layout الرئيسي)
    // ===============================

    var tabsBar = document.getElementById('appTabsBar');            // شريط التابات (الأزرار)
    var tabsContainer = document.getElementById('appTabsContainer'); // حاوية الإطارات (iframes)
    var homeContent = document.getElementById('homeContent');       // محتوى الصفحة الرئيسية

    if (!tabsBar || !tabsContainer) {
        return; // لا يوجد تابات فى هذا الـLayout
    }

    // ===== مساعدات لتشغيل/إيقاف "وضع التابات" =====

    // تفعيل وضع التابات: إخفاء الصفحة الرئيسية وإظهار منطقة التابات
    function enableTabsMode() {
        if (!document.body.classList.contains('has-tabs')) {
            document.body.classList.add('has-tabs');
        }
        if (homeContent) {
            homeContent.style.display = 'none';
        }
    }

    // لو مفيش ولا تاب مفتوح نرجع كل شيء كما كان
    function disableTabsModeIfNoTabs() {
        var count = tabsBar.querySelectorAll('.app-tab').length;
        if (count === 0) {
            document.body.classList.remove('has-tabs');
            if (homeContent) {
                homeContent.style.display = '';
            }
        }
    }

    // ===============================
    // فتح تبويب جديد أو تنشيط الموجود
    // ===============================
    function openTab(tabId, url, title) {

        // حماية بسيطة لو مفيش tabId
        if (!tabId || tabId.trim() === '') {
            tabId = 'tab-' + Math.random().toString(36).substring(2);
        }

        // لو التاب موجود بالفعل → فعّله (مع تحديث الـ URL لو مختلف)
        var existingTab = tabsBar.querySelector('[data-tab-id="' + tabId + '"]');
        if (existingTab) {
            var existingFrame = tabsContainer.querySelector('.app-tab-frame[data-tab-id="' + tabId + '"]');

            // لو نفس التاب لكن URL جديد (مثلاً Show لسجل آخر) نحدث الـ src
            if (existingFrame && existingFrame.src !== url) {
                existingFrame.src = url;
            }

            enableTabsMode();
            activateTab(tabId);
            return;
        }

        // === إنشاء تاب جديد ===

        // إنشاء زر التاب
        var tabButton = document.createElement('button');
        tabButton.type = 'button';
        tabButton.className = 'app-tab btn btn-sm btn-light';
        tabButton.setAttribute('data-tab-id', tabId);
        tabButton.innerHTML =
            '<span class="app-tab-title">' + (title || 'تبويب جديد') + '</span>' +
            '<span class="app-tab-close" title="إغلاق">&times;</span>';

        tabsBar.appendChild(tabButton);

        // إنشاء IFRAME للشاشة
        var frame = document.createElement('iframe');
        frame.className = 'app-tab-frame';
        frame.setAttribute('data-tab-id', tabId);
        frame.src = url;
        frame.loading = 'lazy';

        tabsContainer.appendChild(frame);

        // تفعيل وضع التابات وإظهار التاب الجديد
        enableTabsMode();
        activateTab(tabId);
    }

    // ===============================
    // تنشيط تبويب معيّن
    // ===============================
    function activateTab(tabId) {

        // 1) الأزرار (التابات في الشريط العلوي)
        var allTabs = tabsBar.querySelectorAll('.app-tab');
        allTabs.forEach(function (tab) {
            var isActive = tab.getAttribute('data-tab-id') === tabId;
            if (isActive) {
                tab.classList.add('active');
            } else {
                tab.classList.remove('active');
            }
        });

        // 2) الإطارات (iframes)
        var allFrames = tabsContainer.querySelectorAll('.app-tab-frame');
        allFrames.forEach(function (frame) {
            var isActive = frame.getAttribute('data-tab-id') === tabId;
            if (isActive) {
                frame.classList.remove('d-none');
            } else {
                frame.classList.add('d-none');
            }
        });
    }

    // ===============================
    // إغلاق تبويب
    // ===============================
    function closeTab(tabId) {

        var tab = tabsBar.querySelector('[data-tab-id="' + tabId + '"]');
        if (!tab) return;

        var wasActive = tab.classList.contains('active');
        tab.remove();

        var frame = tabsContainer.querySelector('.app-tab-frame[data-tab-id="' + tabId + '"]');
        if (frame) {
            frame.remove();
        }

        if (wasActive) {
            // فعّل آخر تاب إن وجد
            var lastTab = tabsBar.querySelector('.app-tab:last-child');
            if (lastTab) {
                var lastId = lastTab.getAttribute('data-tab-id');
                activateTab(lastId);
            }
        }

        // لو مفيش تابات → أرجع الصفحة الرئيسية
        disableTabsModeIfNoTabs();
    }

    // ===============================
    // تحديث (ريفريش) التاب النشط حاليًا (IFRAME)
    // ===============================
    function refreshCurrentTab() {
        // نجيب الـ iframe الظاهر حاليًا (اللى مش عليه d-none)
        var activeFrame = tabsContainer.querySelector('.app-tab-frame:not(.d-none)');
        if (!activeFrame) return;

        // طريقة بسيطة: إعادة تعيين الـ src لنفسه → Reload
        var currentSrc = activeFrame.src;
        activeFrame.src = currentSrc;
    }

    // ===============================
    // التعامل مع كليك شريط التابات
    // ===============================
    tabsBar.addEventListener('click', function (event) {
        var target = event.target;

        // ضغط على زر الإغلاق ×
        if (target.classList.contains('app-tab-close')) {
            var tab = target.closest('.app-tab');
            if (tab) {
                var tabId = tab.getAttribute('data-tab-id');
                closeTab(tabId);
            }
            return;
        }

        // ضغط على جسم التاب نفسه
        var tabButton = target.closest('.app-tab');
        if (tabButton) {
            var id = tabButton.getAttribute('data-tab-id');
            activateTab(id);
        }
    });

    // ===============================
    // ربط روابط القائمة العلوية بنظام التابات
    // ===============================
    var menuLinks = document.querySelectorAll('.app-menu-link');

    menuLinks.forEach(function (link) {
        link.addEventListener('click', function (event) {
            event.preventDefault(); // منع فتح الرابط مباشرة

            var tabId = link.getAttribute('data-tab-id');
            var title = link.getAttribute('data-tab-title') || link.textContent.trim();
            var url = link.href;

            openTab(tabId, url, title);
        });
    });

    // ===============================
    // استقبال رسائل من الصفحات داخل الـ IFRAME
    // (زر تفاصيل داخل قائمة العملاء مثلاً)
    // ===============================
    window.addEventListener('message', function (event) {
        var data = event.data;
        if (!data || data.type !== 'erp-open-tab') return;

        var tabId = data.tabId || '';
        var title = data.title || '';
        var url = data.url;

        if (!url) return;

        openTab(tabId, url, title);
    });

    // ===============================
    // زر "تحديث التاب" في الـ Layout الرئيسي (لو موجود)
    // ===============================
    document.addEventListener('DOMContentLoaded', function () {
        var btnRefresh = document.getElementById('btnRefreshTab');
        if (btnRefresh) {
            btnRefresh.addEventListener('click', function (e) {
                e.preventDefault();
                refreshCurrentTab(); // تحديث التاب النشط فقط
            });
        }
    });

    // ===============================
    // إتاحة الدوال عالميًا لو احتجناها لاحقاً
    // ===============================
    window.erpTabs = {
        openTab: openTab,               // فتح تاب جديد
        activateTab: activateTab,       // تفعيل تاب موجود
        closeTab: closeTab,             // إغلاق تاب
        refreshCurrentTab: refreshCurrentTab // تحديث التاب الحالي (IFRAME)
    };

})();
