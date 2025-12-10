// ===============================
// نظام التابات للـ ERP (نسخة محدثة)
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
    //    هنا كل الفكرة: لما أضغط على app-menu-link جوّه التاب،
    //    ما يتنقلش جوّه نفس التاب، لكن يبعت طلب للـ parent يفتح/يُفعِّل التاب المطلوب
    // ===============================
    if (inFrame) {

        // تعليق عربي: حدث كليك عام على الصفحة داخل الـ iframe
        document.addEventListener('click', function (event) {
            var link = event.target.closest('a.app-menu-link');
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

    var tabsBar = document.getElementById('appTabsBar');          // شريط التابات
    var tabsContainer = document.getElementById('appTabsContainer');    // حاوية الإطارات
    var homeContent = document.getElementById('homeContent');         // محتوى الصفحة الرئيسية

    if (!tabsBar || !tabsContainer) {
        return; // لا يوجد تابات فى هذا الـLayout
    }

    // ===== مساعدات لتشغيل/إيقاف "وضع التابات" =====

    function enableTabsMode() {
        // تعليق: إضافة كلاس لرفع ارتفاع منطقة الإطارات
        if (!document.body.classList.contains('has-tabs')) {
            document.body.classList.add('has-tabs');
        }
        // إخفاء محتوى الصفحة الرئيسية مع وجود تابات
        if (homeContent) {
            homeContent.style.display = 'none';
        }
    }

    function disableTabsModeIfNoTabs() {
        // لو مفيش ولا تاب مفتوح نرجع كل شيء كما كان
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

        // تعليق: حماية بسيطة لو مفيش tabId
        if (!tabId || tabId.trim() === '') {
            tabId = 'tab-' + Math.random().toString(36).substring(2);
        }

        // لو التاب موجود بالفعل → فعّله (مع تحديث الـ URL لو مختلف)
        var existingTab = tabsBar.querySelector('[data-tab-id="' + tabId + '"]');
        if (existingTab) {
            var existingFrame = tabsContainer.querySelector('.app-tab-frame[data-tab-id="' + tabId + '"]');

            // تعليق: لو نفس التاب لكن URL جديد (مثلاً Show لعميل آخر) نحدث الـ src
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



    // دالة: تحديث (ريفريش) التاب النشِط حالياً
    function refreshCurrentTab() {
        // نجيب الـ div الخاص بالتاب الحالي (المحتوى النشِط)
        var activePane = document.querySelector("#erpTabsContent .tab-pane.active");
        if (!activePane) return;   // لو مفيش تاب نشِط نخرج بهدوء

        // نقرأ رابط الصفحة المخزن في data-url
        var url = activePane.getAttribute("data-url");
        if (!url) return;          // لو مفيش URL نخرج

        // نجيب عنوان التاب من اللينك النشط (للاستخدام لاحقاً لو حبيت تظهر لودر)
        var activeLink = document.querySelector("#erpTabsNav .nav-link.active");

        // نقدر نحط لودر بسيط لو حابب
        if (activePane) {
            activePane.innerHTML = "<div class='p-3 text-center text-muted'>جاري تحديث التاب...</div>";
        }

        // طلب الصفحة من السيرفر مرة أخرى
        fetch(url, {
            headers: {
                "X-Requested-With": "XMLHttpRequest" // اختيارى لو حابب تفرق في السيرفر
            }
        })
            .then(function (response) {
                return response.text();
            })
            .then(function (html) {
                // استبدال محتوى التاب بالمحتوى الجديد
                activePane.innerHTML = html;
            })
            .catch(function (error) {
                console.error("خطأ أثناء تحديث التاب:", error);
                activePane.innerHTML = "<div class='p-3 text-danger text-center'>حدث خطأ أثناء التحديث.</div>";
            });
    }

    // في آخر الملف حيث يتم تصدير الدوال عالمياً:
    window.erpTabs = {
        openTab: openTab,         // فتح تاب جديد
        activateTab: activateTab, // تفعيل تاب موجود
        closeTab: closeTab,       // إغلاق تاب
        refreshCurrentTab: refreshCurrentTab // ✅ تحديث التاب الحالي
    };




    document.addEventListener("DOMContentLoaded", function () {
        var btnRefresh = document.getElementById("btnRefreshTab");
        if (btnRefresh) {
            btnRefresh.addEventListener("click", function (e) {
                e.preventDefault();  // منع أي سلوك افتراضي للزر
                if (window.erpTabs && typeof window.erpTabs.refreshCurrentTab === "function") {
                    window.erpTabs.refreshCurrentTab();  // تحديث التاب الحالي فقط
                }
            });
        }
    });



    // ===============================
    // تنشيط تبويب معيّن
    // ===============================
    function activateTab(tabId) {

        // 1) الأزرار
        var allTabs = tabsBar.querySelectorAll('.app-tab');
        allTabs.forEach(function (tab) {
            var isActive = tab.getAttribute('data-tab-id') === tabId;
            if (isActive) {
                tab.classList.add('active');
            } else {
                tab.classList.remove('active');
            }
        });

        // 2) الإطارات
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

        var frame = tabsContainer.querySelector('[data-tab-id="' + tabId + '"]');
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
    // (يشتغل في الـ Layout الرئيسي فقط)
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




    // دالة: تحديث (ريفريش) التاب النشِط حالياً بدون إغلاقه
    function refreshActiveTab() {
        // نجيب اللينك النشِط في شريط التابات
        var activeLink = document.querySelector("#erpTabsNav .nav-link.active");
        // ونجيب الـ div الخاص بمحتوى التاب النشِط
        var activePane = document.querySelector("#erpTabsContent .tab-pane.active");

        if (!activeLink || !activePane) {
            return; // لو مفيش تاب نشِط نخرج بهدوء
        }

        // نحاول نقرأ رابط الصفحة من data-url أو href
        var url =
            activeLink.getAttribute("data-url") ||
            activePane.getAttribute("data-url") ||
            activeLink.getAttribute("href");

        if (!url) {
            return; // لو مش لاقيين URL مش هنعمل حاجة
        }

        // نتأكد إن باراميتر frame=1 موجود (لو انت بتستخدمه لعرض الصفحة داخل التاب)
        if (!url.includes("frame=1")) {
            var sep = url.indexOf("?") >= 0 ? "&" : "?";
            url = url + sep + "frame=1";
        }

        // نعرض رسالة بسيطة أثناء التحميل
        activePane.innerHTML =
            "<div class='p-3 text-center text-muted'>جاري تحديث التاب...</div>";

        // نطلب نفس الصفحة من السيرفر مرة أخرى
        fetch(url, {
            headers: {
                "X-Requested-With": "XMLHttpRequest" // اختيارى
            }
        })
            .then(function (response) {
                return response.text();
            })
            .then(function (html) {
                // نحقن الـ HTML الجديد داخل نفس التاب
                activePane.innerHTML = html;
            })
            .catch(function (error) {
                console.error("خطأ أثناء تحديث التاب:", error);
                activePane.innerHTML =
                    "<div class='p-3 text-danger text-center'>حدث خطأ أثناء التحديث.</div>";
            });
    }








    // إتاحة الدوال عالميًا لو احتجناها لاحقاً
    window.erpTabs = {
        openTab: openTab,             // فتح تاب جديد
        activateTab: activateTab,     // تفعيل تاب موجود
        closeTab: closeTab,           // إغلاق تاب
        refreshActiveTab: refreshActiveTab // ✅ تحديث التاب الحالي بدون إغلاقه
    };



})();
