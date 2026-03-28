// ===============================
// نظام التابات للـ ERP (نسخة IFRAME + زر تحديث)
// + نظام ثابت لإعادة تهيئة الصفحات بعد التحميل (ERP_INIT Hook)
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

        // تعليق عربي: ضمان وجود frame=1 في الرابط (عشان يفتح داخل التاب/iframe)
        function ensureFrameParam(url) {
            if (!url) return url;
            if (url.includes("frame=1")) return url;
            const joiner = url.includes("?") ? "&" : "?";
            return url + joiner + "frame=1";
        }

        // تعليق عربي: منع الكاش (مهم لأن بعض المتصفحات قد تعيد صفحة قديمة فتظهر فاضية)
        function addNoCache(url) {
            if (!url) return url;
            const joiner = url.includes("?") ? "&" : "?";
            return url + joiner + "_ts=" + Date.now();
        }

        // تعليق عربي: قراءة URL من data-url (لو موجود) أو href (لو لينك)
        function getTargetUrl(el) {
            if (!el) return '';
            const dataUrl = el.getAttribute("data-url");
            if (dataUrl && dataUrl.trim()) return dataUrl.trim();

            // لو عنصر <a>
            const href = el.getAttribute("href");
            if (href && href.trim()) return href.trim();

            return '';
        }

        // =========================================================
        // ✅ نظام ثابت: محاولة استدعاء __ERP_INIT__ داخل صفحة الـ iframe
        // الهدف: أي View فيها بحث/أسهم/Fetch لازم تعرّف window.__ERP_INIT__
        // =========================================================
        function tryCallErpInitFromFrame(frameEl) {
            if (!frameEl) return;

            // تعليق عربي: onload قد يتكرر — لا مشكلة، المهم يكون init Idempotent
            try {
                const w = frameEl.contentWindow;
                if (!w) return;

                // 1) الشكل القياسي
                if (typeof w.__ERP_INIT__ === 'function') {
                    w.__ERP_INIT__();
                    return;
                }

                // 2) بدائل احتياطية (لو في شاشات قديمة)
                if (typeof w.erpInitPage === 'function') {
                    w.erpInitPage();
                    return;
                }

            } catch (e) {
                // تعليق عربي: قد يحدث Cross-origin في حالات نادرة، نتجاهل
                console.warn("ERP Init: cannot call init inside frame", e);
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
        if (inFrame) {

            document.addEventListener('click', function (event) {

                // =========================================================
                // (0) زر البحث داخل فاتورة المشتريات / طلب الشراء (يبني URL حسب رقم المستخدم)
                // =========================================================
                var navSearchBtn = event.target.closest('#btnNavInvoiceSearch');
                if (navSearchBtn) {
                    // طلب الشراء (PurchaseRequests): المعالج في الصفحة نفسها (docCaptureHandler) — لا نتدخل
                    var baseUrl = (navSearchBtn.getAttribute('data-base-url') || '').toLowerCase();
                    if (baseUrl.indexOf('purchaserequests') >= 0) return;

                    event.preventDefault();

                    var input = document.getElementById('NavInvoiceSearchInput');
                    var id = input ? parseInt(input.value || '0', 10) : 0;

                    if (!id || id <= 0) return;

                    var tabIdS = normalizeTabId(navSearchBtn.getAttribute('data-tab-id') || 'pi-show-tab');
                    var titleS = (navSearchBtn.getAttribute('data-tab-title') || 'فاتورة المشتريات').trim();

                    baseUrl = normalizeUrl(navSearchBtn.getAttribute('data-base-url') || '');
                    baseUrl = ensureFrameParam(baseUrl);

                    var joiner = baseUrl.includes("?") ? "&" : "?";
                    var urlS = baseUrl + joiner + "id=" + encodeURIComponent(id);
                    urlS = addNoCache(urlS);

                    window.top.postMessage({
                        type: 'erp-open-tab',
                        tabId: tabIdS,
                        title: titleS,
                        url: urlS
                    }, '*');

                    return;
                }

                // =========================================================
                // (1) التقاط الروابط/الأزرار الخاصة بنظام التابات (Delegation)
                // =========================================================
                var link = event.target.closest('a.app-menu-link, a.open-tab, a.open-same-tab, button.open-same-tab');
                if (!link) return;

                // ------------------------------
                // (أ) open-same-tab
                // ------------------------------
                if (link.classList.contains('open-same-tab')) {

                    var tabId = normalizeTabId(link.getAttribute('data-tab-id') || '');
                    var tabTitle = (link.getAttribute('data-tab-title') || link.textContent || '').trim();

                    var url = '';
                    // ✅ حجم تعامل عميل: فتح Customers/Show في تاب جديد بمعرّف فريد (لا نستبدل تاب طلب الشراء/الفاتورة)
                    if (tabId === 'customer-engagement') {
                        var volBase = (link.getAttribute('data-base-url') || '').trim();
                        if (volBase) {
                            var doc = link.ownerDocument || document;
                            var cidInput = doc.getElementById('CustomerId');
                            var cid = cidInput ? parseInt((cidInput.value || '0'), 10) : 0;
                            if (cid > 0) {
                                var joiner = volBase.indexOf('?') >= 0 ? '&' : '?';
                                url = normalizeUrl(volBase + joiner + 'id=' + cid);
                                tabId = 'customer-volume-' + cid;
                                tabTitle = (tabTitle || 'حجم تعامل').trim();
                            }
                        }
                    }
                    if (!url) url = normalizeUrl(getTargetUrl(link));

                    if (!url) {
                        console.warn("⚠ open-same-tab: url غير موجود/فارغ (data-url/href)");
                        return;
                    }

                    // ✅ ضمان frame=1 + منع الكاش
                    url = ensureFrameParam(url);
                    url = addNoCache(url);

                    event.preventDefault();

                    // ✅ لو في iframe: نرسل للوالد لفتح/تحديث التاب المناسب
                    try {
                        if (window.top && window.top !== window) {
                            // لو tabId محدد (بما فيه حجم التعامل بعد تحويله لـ customer-volume-xxx): فتح/تحديث تاب. وإلا: تحديث التاب الحالي
                            if (tabId) {
                                window.top.postMessage({
                                    type: 'erp-open-tab',
                                    tabId: tabId,
                                    url: url,
                                    title: tabTitle
                                }, '*');
                            } else {
                                window.top.postMessage({
                                    type: 'erp-update-current-tab',
                                    url: url,
                                    title: tabTitle
                                }, '*');
                            }
                            return;
                        }
                    } catch (e) {
                        console.error('خطأ في postMessage:', e);
                    }

                    // ✅ لو مش في iframe: نستخدم openTab مع tabId (أو بدون tabId لتحديث التاب الحالي)
                    if (tabId) {
                        // محاولة فتح/تحديث تاب بنفس tabId
                        if (window.erpTabs && typeof window.erpTabs.openTab === 'function') {
                            window.erpTabs.openTab(tabId, url, tabTitle);
                        } else {
                            window.location.href = url;
                        }
                    } else {
                        // لو مفيش tabId: نحدث التاب الحالي
                        if (window.erpTabs && typeof window.erpTabs.updateCurrentTabUrl === 'function') {
                            window.erpTabs.updateCurrentTabUrl(url, tabTitle);
                        } else {
                            window.location.href = url;
                        }
                    }

                    return;
                }

                // ------------------------------
                // (ب) open-tab / app-menu-link
                // ------------------------------
                var tabId2 = normalizeTabId(link.getAttribute('data-tab-id') || '');
                var tabTitle2 = (link.getAttribute('data-tab-title') || link.textContent || '').trim();
                var url2 = normalizeUrl(link.href);

                if (!tabId2) {
                    console.warn("⚠ open-tab/app-menu-link: data-tab-id فارغ → سيتم ترك الرابط يفتح بشكل طبيعي داخل نفس التاب");
                    return;
                }

                if (!url2) return;

                url2 = ensureFrameParam(url2);
                url2 = addNoCache(url2);

                event.preventDefault();

                window.top.postMessage({
                    type: 'erp-open-tab',
                    tabId: tabId2,
                    title: tabTitle2,
                    url: url2
                }, '*');
            });

            // =========================================================
            // (2) دعم Enter داخل حقل البحث في فاتورة المشتريات
            // =========================================================
            document.addEventListener('keydown', function (ev) {
                var input = ev.target;
                if (!input || input.id !== 'NavInvoiceSearchInput') return;

                if (ev.key === 'Enter') {
                    ev.preventDefault();
                    var btn = document.getElementById('btnNavInvoiceSearch');
                    if (btn) btn.click();
                }
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

        function enableTabsMode() {
            document.body.classList.add('has-tabs');
            if (homeContent) homeContent.style.display = 'none';
        }

        function disableTabsModeIfNoTabs() {
            var count = tabsBar.querySelectorAll('.app-tab').length;
            if (count === 0) {
                document.body.classList.remove('has-tabs');
                if (homeContent) homeContent.style.display = '';
            }
        }

        /** حفظ موضع التمرير داخل iframe التاب قبل إخفائه (نفس الأصل) */
        function saveFrameScroll(frame) {
            try {
                var w = frame.contentWindow;
                if (!w || !w.document) return;
                var docEl = w.document.documentElement;
                var body = w.document.body;
                var y = Math.max(docEl ? docEl.scrollTop : 0, body ? body.scrollTop : 0);
                var x = Math.max(docEl ? docEl.scrollLeft : 0, body ? body.scrollLeft : 0);
                frame.setAttribute('data-erp-scroll-y', String(y));
                frame.setAttribute('data-erp-scroll-x', String(x));
            } catch (e) { /* cross-origin */ }
        }

        /** استعادة التمرير بعد إظهار التاب (بعد إزالة d-none) */
        function restoreFrameScroll(frame) {
            try {
                var y = parseInt(frame.getAttribute('data-erp-scroll-y') || '0', 10) || 0;
                var x = parseInt(frame.getAttribute('data-erp-scroll-x') || '0', 10) || 0;
                var w = frame.contentWindow;
                if (!w) return;
                var apply = function () {
                    w.scrollTo(x, y);
                };
                apply();
                requestAnimationFrame(apply);
                setTimeout(apply, 50);
            } catch (e) { /* cross-origin */ }
        }

        // ===============================
        // فتح / تحديث تاب
        // ===============================
        // تابات فتحتها قائمة المبيعات القديمة (list-show-*) تُدمَج في si-show-tab وتُغلَق المكررات
        function tryAdoptListShowSalesInvoiceTabs() {
            var candidates = [];
            var allTabBtns = tabsBar.querySelectorAll('.app-tab');
            for (var i = 0; i < allTabBtns.length; i++) {
                var t = allTabBtns[i];
                var oid = normalizeTabId(t.getAttribute('data-tab-id') || '');
                if (!oid || oid.indexOf('list-show-') !== 0) continue;
                var fr = tabsContainer.querySelector('.app-tab-frame[data-tab-id="' + oid + '"]');
                if (!fr) continue;
                var src = (fr.getAttribute('src') || fr.src || '').toLowerCase();
                if (src.indexOf('/salesinvoices/show') < 0) continue;
                candidates.push({ tab: t, frame: fr });
            }
            if (candidates.length === 0) return null;
            for (var j = candidates.length - 1; j >= 1; j--) {
                closeTab(normalizeTabId(candidates[j].tab.getAttribute('data-tab-id')));
            }
            candidates[0].tab.setAttribute('data-tab-id', 'si-show-tab');
            candidates[0].frame.setAttribute('data-tab-id', 'si-show-tab');
            return candidates[0].tab;
        }

        /** توحيد التاب القديم sales-orders-create مع so-show-tab (نفس نمط si-show-tab) */
        function tryAdoptSalesOrderShowTabs() {
            var candidates = [];
            var allTabBtns = tabsBar.querySelectorAll('.app-tab');
            for (var i = 0; i < allTabBtns.length; i++) {
                var t = allTabBtns[i];
                var oid = normalizeTabId(t.getAttribute('data-tab-id') || '');
                if (oid !== 'sales-orders-create') continue;
                var fr = tabsContainer.querySelector('.app-tab-frame[data-tab-id="sales-orders-create"]');
                if (!fr) continue;
                var src = (fr.getAttribute('src') || fr.src || '').toLowerCase();
                if (src.indexOf('/salesorders/') < 0) continue;
                candidates.push({ tab: t, frame: fr });
            }
            if (candidates.length === 0) return null;
            for (var j = candidates.length - 1; j >= 1; j--) {
                closeTab(normalizeTabId(candidates[j].tab.getAttribute('data-tab-id')));
            }
            candidates[0].tab.setAttribute('data-tab-id', 'so-show-tab');
            candidates[0].frame.setAttribute('data-tab-id', 'so-show-tab');
            return candidates[0].tab;
        }

        function openTab(tabId, url, title) {

            tabId = normalizeTabId(tabId);
            url = normalizeUrl(url);
            // تاب حركة الصنف: عنوان ثابت "حركة الصنف" فقط
            if (tabId === 'product-movement-tab') title = 'حركة الصنف';
            // تاب تعديل الصنف: عنوان ثابت "تعديل الصنف" (يفتح مرة واحدة ويُحدَّث حسب الصنف)
            if (tabId === 'product-edit-tab') title = 'تعديل الصنف';

            if (!tabId) {
                console.warn("⚠ openTab تم استدعاؤه بـ tabId فارغ → تم إلغاء فتح التاب لمنع التكرار");
                return;
            }

            var existingTab = tabsBar.querySelector('[data-tab-id="' + tabId + '"]');
            if (!existingTab && tabId === 'si-show-tab') {
                existingTab = tryAdoptListShowSalesInvoiceTabs();
            }
            if (!existingTab && tabId === 'so-show-tab') {
                existingTab = tryAdoptSalesOrderShowTabs();
            }

            // لو التاب موجود → فعّله وحدث الـ URL والعنوان لو اتغير
            if (existingTab) {
                var existingFrame = tabsContainer.querySelector('.app-tab-frame[data-tab-id="' + tabId + '"]');

                if (existingFrame) {

                    var currentSrc = normalizeUrl(existingFrame.getAttribute('src') || existingFrame.src);
                    if (currentSrc !== url) {
                        // ✅ عند تغيير الرابط فقط: تحميل + __ERP_INIT__
                        existingFrame.onload = function () {
                            tryCallErpInitFromFrame(existingFrame);
                        };
                        existingFrame.src = url;
                    }
                    // نفس الرابط: لا نعيد تعيين src (كان addNoCache يعيد تحميل الصفحة ويعيد التمرير لأعلى)
                }

                // ✅ تحديث عنوان التاب (مثلاً فاتورة مشتريات vs طلب شراء) لئلا يبقى العنوان القديم
                var titleEl = existingTab.querySelector('.app-tab-title');
                if (titleEl && (title || '').trim()) {
                    titleEl.textContent = (title || '').trim();
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

            // ✅ Hook ثابت بعد التحميل
            frame.onload = function () {
                tryCallErpInitFromFrame(frame);
            };

            frame.src = url;
            frame.loading = 'lazy';

            tabsContainer.appendChild(frame);

            enableTabsMode();
            activateTab(tabId);
        }

        function activateTab(tabId) {
            tabId = normalizeTabId(tabId);

            var prevActive = tabsContainer.querySelector('.app-tab-frame:not(.d-none)');
            if (prevActive && normalizeTabId(prevActive.getAttribute('data-tab-id')) !== tabId) {
                saveFrameScroll(prevActive);
            }

            var allTabs = tabsBar.querySelectorAll('.app-tab');
            allTabs.forEach(function (tab) {
                tab.classList.toggle('active', normalizeTabId(tab.getAttribute('data-tab-id')) === tabId);
            });

            var allFrames = tabsContainer.querySelectorAll('.app-tab-frame');
            allFrames.forEach(function (frame) {
                frame.classList.toggle('d-none', normalizeTabId(frame.getAttribute('data-tab-id')) !== tabId);
            });

            var targetFrame = tabsContainer.querySelector('.app-tab-frame[data-tab-id="' + tabId + '"]');
            if (targetFrame) {
                setTimeout(function () {
                    restoreFrameScroll(targetFrame);
                }, 0);
            }
        }

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

        // ✅ تحديث التاب الحالي (مع استدعاء __ERP_INIT__ بعد التحميل)
        function refreshCurrentTab() {
            var activeFrame = tabsContainer.querySelector('.app-tab-frame:not(.d-none)');
            if (!activeFrame) return;

            activeFrame.onload = function () {
                tryCallErpInitFromFrame(activeFrame);
            };

            activeFrame.src = addNoCache(activeFrame.src);
        }

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

        document.querySelectorAll('.app-menu-link').forEach(function (link) {
            link.addEventListener('click', function (event) {
                event.preventDefault();

                var tabId = normalizeTabId(link.getAttribute('data-tab-id') || '');
                var title = (link.getAttribute('data-tab-title') || link.textContent || '').trim();
                var url = normalizeUrl(link.href);

                if (!tabId) {
                    console.warn("⚠ app-menu-link: data-tab-id فارغ → لن يتم فتح تبويب");
                    return;
                }

                openTab(tabId, url, title);
            });
        });

        window.addEventListener('message', function (event) {
            var data = event.data;
            if (!data) return;

            // ✅ تحديث عنوان التاب الحالي فقط (بدون تغيير الـ URL)
            if (data.type === 'erp-set-tab-title') {
                var activeTab = tabsBar.querySelector('.app-tab.active');
                if (activeTab && normalizeTabId(activeTab.getAttribute('data-tab-id')) === 'product-movement-tab') return; // لا نغيّر عنوان تاب حركة الصنف
                var title = (data.title || '').trim();
                if (title) {
                    if (activeTab) {
                        var titleEl = activeTab.querySelector('.app-tab-title');
                        if (titleEl) titleEl.textContent = title;
                    }
                }
                return;
            }

            // ✅ تحديث URL التاب الحالي (بدون فتح تاب جديد)
            if (data.type === 'erp-update-current-tab') {
                var url = normalizeUrl(data.url);
                var title = (data.title || '').trim();
                if (url) {
                    updateCurrentTabUrl(url, title);
                }
                return;
            }

            // ✅ فتح تاب (بنفس المنطق القديم)
            if (data.type !== 'erp-open-tab') return;

            var tabId = normalizeTabId(data.tabId || '');
            var url = normalizeUrl(data.url);
            var title = (data.title || '').trim();
            // تاب حركة الصنف: عنوان ثابت دائماً "حركة الصنف" فقط
            if (tabId === 'product-movement-tab') title = 'حركة الصنف';

            if (!tabId) {
                console.warn("⚠ message: tabId فارغ → لن يتم فتح تبويب");
                return;
            }

            if (!url) return;

            openTab(tabId, url, title);
        });

        document.addEventListener('DOMContentLoaded', function () {
            var btnRefresh = document.getElementById('btnRefreshTab');
            if (btnRefresh) {
                btnRefresh.addEventListener('click', function (event) {
                    event.preventDefault();
                    refreshCurrentTab();
                });
            }
        });

        // ✅ تحديث URL للتاب الحالي (بدون فتح تاب جديد)
        function updateCurrentTabUrl(url, title) {
            var activeFrame = tabsContainer.querySelector('.app-tab-frame:not(.d-none)');
            if (!activeFrame) return false;

            var activeTabId = activeFrame.getAttribute('data-tab-id');
            if (!activeTabId) return false;

            url = normalizeUrl(url);
            url = ensureFrameParam(url);
            url = addNoCache(url);

            activeFrame.onload = function () {
                tryCallErpInitFromFrame(activeFrame);
            };

            activeFrame.src = url;

            if (title) {
                var activeTab = tabsBar.querySelector('.app-tab.active');
                if (activeTab) {
                    var titleEl = activeTab.querySelector('.app-tab-title');
                    if (titleEl) {
                        titleEl.textContent = title.trim();
                    }
                }
            }

            return true;
        }

        window.erpTabs = {
            openTab: openTab,
            activateTab: activateTab,
            closeTab: closeTab,
            refreshCurrentTab: refreshCurrentTab,
            updateCurrentTabUrl: updateCurrentTabUrl
        };

    })();
}
