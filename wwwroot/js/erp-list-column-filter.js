// erp-list-column-filter.js
// سكربت مشترك: لوحة فلتر الأعمدة (بحث شبيه بإكسل) + مقابض توسعة الأعمدة
// الفلاتر تراكمية: عند التطبيق لا يُمسح فلتر بقية الأعمدة، يحدّث فقط filterCol_العمودالحالي
// الأعمدة الرقمية (numericColumns): نفس نمط قائمة الأصناف — بحث رقمي يُخزَّن في filterCol_{col}Expr (< > <= >= : نطاق، أو رقم مطابق)
// الاستخدام: ERP.initListColumnFilter({ panelId, tableId, wrapperId, getColumnValuesUrl, defaultSort?, defaultDir?, numericColumns?: string[] })

(function () {
    'use strict';

    // ——— مطابقة نصية موحّدة مع توثيق الأصناف/العملاء (أرقام عربية + كلمات متعددة AND + يحتوي) ———
    function normalizeDigits(s) {
        if (!s) return '';
        var map = { '\u0660': '0', '\u0661': '1', '\u0662': '2', '\u0663': '3', '\u0664': '4',
            '\u0665': '5', '\u0666': '6', '\u0667': '7', '\u0668': '8', '\u0669': '9' };
        var out = '';
        for (var i = 0; i < s.length; i++) {
            var c = s[i];
            out += map[c] !== undefined ? map[c] : c;
        }
        return out;
    }
    function normalizeForColumnFilterSearch(s) {
        if (!s) return '';
        return normalizeDigits(String(s).toLowerCase());
    }

    /** تطبيع إدخال البحث الرقمي قبل الإرسال (أرقام عربية + توحيد رموز المقارنة/النطاق) — مواءمة مع الخادم */
    function normalizeNumericExprForSubmit(s) {
        var t = normalizeDigits(String(s || '').trim());
        if (!t) return '';
        // توحيد رموز قد يكتبها المستخدم بلوحة عربية/نسخ من خارج النظام
        t = t
            .replace(/[\u061B\u0589\uFE13\uFE55]/g, ':') // ؛ և ﹕ ︓ -> :
            .replace(/[\u2010\u2011\u2012\u2013\u2014\u2015\u2212]/g, '-') // dash variants -> -
            .replace(/[\u2264]/g, '<=') // ≤
            .replace(/[\u2265]/g, '>=') // ≥
            .replace(/\s+/g, '');
        if ((t.split(',').length - 1) === 1 && t.indexOf('.') < 0)
            t = t.replace(',', '.');
        return t;
    }
    /** كل كلمات الاستعلام يجب أن تظهر كنص فرعي في العرض (مثل Like %word% على الخادم) */
    function textMatchesColumnFilterSearch(displayOrValue, rawQuery) {
        var q = (rawQuery || '').trim();
        if (!q) return true;
        var hay = normalizeForColumnFilterSearch(displayOrValue || '');
        var words = q.split(/\s+/).filter(Boolean);
        for (var i = 0; i < words.length; i++) {
            var w = normalizeForColumnFilterSearch(words[i]);
            if (!w) continue;
            if (hay.indexOf(w) === -1) return false;
        }
        return true;
    }

    function init(options) {
        if (!options || !options.panelId || !options.tableId || !options.getColumnValuesUrl) return;

        var panel = document.getElementById(options.panelId);
        var table = document.getElementById(options.tableId);
        if (!panel || !table) return;

        var titleEl = panel.querySelector('.erp-column-filter-header span');
        var btnClose = panel.querySelector('.erp-column-filter-header .btn-close');
        var searchInp = panel.querySelector('.erp-column-filter-body input[type="text"]');
        var listEl = panel.querySelector('.erp-column-filter-list');
        // إخفاء صف «بحث في العناصر» فقط للأعمدة الرقمية — لا نخفي كامل .erp-filter-section حتى تبقى قائمة القيم/البحث الرقمي ظاهرة
        var searchRow = panel.querySelector('#erpColumnFilterSearchRow') || (searchInp ? searchInp.parentElement : null);
        var sortSection = panel.querySelector('.erp-filter-sort-section');
        var btnOk = panel.querySelector('.erp-column-filter-actions button[data-action="apply"]') || panel.querySelector('.erp-column-filter-actions .btn-primary') || panel.querySelector('.erp-column-filter-actions .btn-erp-primary');
        var btnClear = panel.querySelector('.erp-column-filter-actions button[data-action="clear"]') || panel.querySelector('.erp-column-filter-actions .btn-secondary') || panel.querySelector('.erp-column-filter-actions .btn-erp-secondary');

        var defaultSort = (options.defaultSort || '').trim() || 'Id';
        var defaultDir = ((options.defaultDir || '').toLowerCase() === 'desc') ? 'desc' : 'asc';

        var numericCols = (options.numericColumns || []).map(function (c) { return String(c || '').toLowerCase(); });
        function isNumericCol(col) {
            return numericCols.indexOf(String(col || '').toLowerCase()) >= 0;
        }

        var currentCol = null, currentSortKey = null, allItems = [], currentSelected = new Set(), anchorRect = null;

        function getCurrentFilterValue(col) {
            var url = new URL(window.location.href);
            if (isNumericCol(col)) {
                return url.searchParams.get('filterCol_' + col + 'Expr') || url.searchParams.get('filterCol_' + col) || '';
            }
            return url.searchParams.get('filterCol_' + col) || '';
        }
        function getCurrentFilterContains(col) {
            return new URL(window.location.href).searchParams.get('filterCol_' + col + '_contains') || '';
        }

        function getCurrentSort() {
            var url = new URL(window.location.href);
            return {
                sort: url.searchParams.get('sort') || defaultSort,
                dir: url.searchParams.get('dir') || defaultDir
            };
        }

        function adjustPanelPosition() {
            if (!panel || !anchorRect) return;
            setTimeout(function () {
                var r = panel.getBoundingClientRect();
                var top = anchorRect.bottom + 4, left = anchorRect.left;
                if (top + r.height > window.innerHeight - 8) top = Math.max(8, window.innerHeight - 8 - r.height);
                if (left + r.width > window.innerWidth - 8) left = Math.max(8, window.innerWidth - 8 - r.width);
                panel.style.top = top + 'px';
                panel.style.left = left + 'px';
            }, 50);
        }

        function showPanel(btn) {
            currentCol = btn.getAttribute('data-col');
            currentSortKey = btn.getAttribute('data-sort-key') || currentCol;
            if (titleEl) titleEl.textContent = 'ترتيب وفلتر: ' + (btn.getAttribute('data-col-title') || currentCol);
            anchorRect = btn.getBoundingClientRect();
            panel.style.minWidth = Math.max(250, anchorRect.width) + 'px';
            panel.classList.remove('d-none');

            var currentSort = getCurrentSort();
            var isCurrent = currentSort.sort.toLowerCase() === (currentSortKey || '').toLowerCase();
            var currentDir = isCurrent ? currentSort.dir : 'asc';
            if (sortSection) {
                sortSection.querySelectorAll('.erp-sort-btn').forEach(function (b) {
                    if (isCurrent && b.getAttribute('data-sort-dir') === currentDir) {
                        b.classList.add('active', 'btn-primary');
                        b.classList.remove('btn-outline-secondary');
                    } else {
                        b.classList.remove('active', 'btn-primary');
                        b.classList.add('btn-outline-secondary');
                    }
                });
            }

            // ——— عمود رقمي: نفس قائمة الأصناف (بحث رقمي فقط، بدون قائمة تشيك بوكس) ———
            if (isNumericCol(currentCol)) {
                if (searchInp) searchInp.value = '';
                currentSelected = new Set();
                if (searchRow) searchRow.classList.add('d-none');
                var curExpr = getCurrentFilterValue(currentCol) || '';
                var safeVal = String(curExpr).replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
                if (listEl) {
                    listEl.innerHTML =
                        '<div class="mb-2">' +
                        '<label class="form-label small mb-1">بحث رقمي:</label>' +
                        '<input type="text" id="erpColumnFilterNumericExpr" class="form-control form-control-sm" ' +
                        'placeholder="رقم أو &lt;10 أو &gt;100 أو 10:100 أو 10-100" value="' + safeVal + '">' +
                        '<small class="text-muted d-block mt-1">مثال: 53812 أو &lt;10 أو &gt;100 أو 10:100 أو 10-100 (أرقام عربية ٠–٩ مسموحة)</small>' +
                        '</div>';
                }
                adjustPanelPosition();
                setTimeout(function () {
                    var numInp = document.getElementById('erpColumnFilterNumericExpr');
                    if (numInp) {
                        numInp.addEventListener('keydown', function (e) {
                            if (e.key === 'Enter') { e.preventDefault(); applyFilter(); }
                        });
                        numInp.focus();
                    }
                }, 50);
                return;
            }

            // ——— عمود غير رقمي: قائمة القيم ———
            allItems = [];
            if (searchInp) searchInp.value = '';
            currentSelected = new Set((new URL(window.location.href).searchParams.get('filterCol_' + currentCol) || '').split(/[|,;]/).filter(Boolean));
            adjustPanelPosition();
            if (searchRow) searchRow.classList.remove('d-none');
            if (listEl) listEl.innerHTML = '<div class="text-muted text-center py-3">جاري التحميل...</div>';

            var url = options.getColumnValuesUrl + (options.getColumnValuesUrl.indexOf('?') >= 0 ? '&' : '?') + 'column=' + encodeURIComponent(currentCol);
            var searchVal = (searchInp && searchInp.value) ? searchInp.value.trim() : '';
            if (searchVal) url += '&search=' + encodeURIComponent(searchVal);

            fetch(url)
                .then(function (r) { return r.json(); })
                .then(function (items) {
                    var raw = Array.isArray(items) ? items : [];
                    allItems = raw.map(function (v) {
                        if (v != null && typeof v === 'object' && ('value' in v || 'display' in v)) {
                            return {
                                value: v.value != null ? v.value : v.display,
                                display: v.display != null ? v.display : v.value
                            };
                        }
                        return { value: v, display: v };
                    });
                    renderList(searchInp ? searchInp.value : '', { preserveScroll: false });
                })
                .catch(function () {
                    if (listEl) listEl.innerHTML = '<div class="text-danger text-center py-2">فشل التحميل</div>';
                });
        }

        function renderList(search, renderOpts) {
            renderOpts = renderOpts || {};
            var preserveScroll = !!renderOpts.preserveScroll;
            var itemsScrollEl = listEl ? listEl.querySelector('.erp-filter-items') : null;
            var prevScroll = (preserveScroll && itemsScrollEl) ? itemsScrollEl.scrollTop : 0;

            if (!listEl) return;
            var term = (search || '').trim();
            var filtered = term ? allItems.filter(function (x) {
                return textMatchesColumnFilterSearch(x.display != null ? x.display : x.value, term);
            }) : allItems;
            var allChecked = filtered.length > 0 && filtered.every(function (x) { return currentSelected.has(String(x.value)); });
            var someChecked = filtered.some(function (x) { return currentSelected.has(String(x.value)); });

            var selectAllId = (panel.id || 'erpFilterPanel') + '_selectAll';
            listEl.innerHTML =
                '<div class="form-check mb-1">' +
                '<input type="checkbox" class="form-check-input" id="' + selectAllId + '">' +
                '<label class="form-check-label small" for="' + selectAllId + '">(تحديد الكل)</label>' +
                '</div>' +
                '<div class="erp-filter-items">' +
                filtered.map(function (x) {
                    var v = String(x.value);
                    var checked = currentSelected.has(v);
                    return '<div class="form-check">' +
                        '<input type="checkbox" class="form-check-input erp-filter-item" value="' + v.replace(/"/g, '&quot;') + '" ' + (checked ? 'checked' : '') + '>' +
                        '<label class="form-check-label small">' + (x.display || v).replace(/</g, '&lt;') + '</label></div>';
                }).join('') +
                '</div>';

            var selectAllCb = listEl.querySelector('#' + selectAllId);
            if (selectAllCb) {
                selectAllCb.checked = allChecked;
                selectAllCb.indeterminate = someChecked && !allChecked;
                selectAllCb.onchange = function () {
                    filtered.forEach(function (x) {
                        if (this.checked) currentSelected.add(String(x.value));
                        else currentSelected.delete(String(x.value));
                    }.bind(this));
                    renderList(searchInp ? searchInp.value : '', { preserveScroll: true });
                };
            }
            listEl.querySelectorAll('.erp-filter-item').forEach(function (cb) {
                cb.onchange = function () {
                    if (this.checked) currentSelected.add(this.value);
                    else currentSelected.delete(this.value);
                    renderList(searchInp ? searchInp.value : '', { preserveScroll: true });
                };
            });
            if (preserveScroll) {
                var newItems = listEl.querySelector('.erp-filter-items');
                if (newItems) newItems.scrollTop = prevScroll;
            }
            adjustPanelPosition();
        }

        // تراكمية: لا نمسح فلاتر الأعمدة الأخرى، نحدّث فقط العمود الحالي
        // الفصل بين سلوكين: البحث في الجدول (شريط البحث الرئيسي) ≠ بوكس البحث في كارت الفلتر.
        // في الكارت: البوكس فقط لتضييق القائمة لاختيار صنف/قيم؛ التطبيق يعتمد على التشيك بوكسات فقط (فلتر دقيق).
        // "يحتوي" يُستخدم من شريط البحث في الجدول فقط، لا من بوكس الكارت.
        // تحديث فلتر العمود الحالي فقط — إبقاء فلاتر الأعمدة الأخرى (AND)
        function applyFilter(applySort, sortDir) {
            if (!currentCol) return;
            var url = new URL(window.location.href);
            if (applySort !== undefined && sortDir !== undefined) {
                url.searchParams.set('sort', applySort);
                url.searchParams.set('dir', sortDir);
            } else if (currentSortKey) {
                var cur = getCurrentSort();
                url.searchParams.set('sort', cur.sort);
                url.searchParams.set('dir', cur.dir);
            }

            if (isNumericCol(currentCol)) {
                var numInp = document.getElementById('erpColumnFilterNumericExpr');
                var expr = numInp ? normalizeNumericExprForSubmit(numInp.value) : '';
                url.searchParams.delete('filterCol_' + currentCol);
                url.searchParams.delete('filterCol_' + currentCol + '_contains');
                if (expr) {
                    url.searchParams.set('filterCol_' + currentCol + 'Expr', expr);
                } else {
                    url.searchParams.delete('filterCol_' + currentCol + 'Expr');
                }
                url.searchParams.set('page', '1');
                window.location.href = url.toString();
                return;
            }

            var val = Array.from(currentSelected).filter(Boolean).join('|');
            var containsParam = 'filterCol_' + currentCol + '_contains';
            if (val) {
                url.searchParams.set('filterCol_' + currentCol, val);
                url.searchParams.delete(containsParam);
            } else {
                url.searchParams.delete('filterCol_' + currentCol);
                url.searchParams.delete(containsParam);
            }
            url.searchParams.set('page', '1');
            window.location.href = url.toString();
        }

        function clearFilter() {
            if (!currentCol) return;
            var url = new URL(window.location.href);
            url.searchParams.delete('filterCol_' + currentCol);
            url.searchParams.delete('filterCol_' + currentCol + 'Expr');
            url.searchParams.set('page', '1');
            window.location.href = url.toString();
        }

        table.querySelectorAll('.erp-col-filter-btn').forEach(function (btn) {
            btn.addEventListener('click', function (e) {
                e.stopPropagation();
                showPanel(this);
            });
        });

        document.addEventListener('click', function (e) {
            if (e.target.closest('.erp-sort-btn') && panel && !panel.classList.contains('d-none')) {
                var sortBtn = e.target.closest('.erp-sort-btn');
                if (currentSortKey) applyFilter(currentSortKey, sortBtn.getAttribute('data-sort-dir'));
            }
        });

        var fetchDebounce = null;
        if (searchInp) {
            searchInp.addEventListener('input', function () {
                if (currentCol && isNumericCol(currentCol)) return;
                // لا تُصفِّي القائمة قبل اكتمال تحميل قيم العمود الحالي (وإلا يظهر «لا شيء» عند الكتابة السريعة أو بيانات عمود سابق)
                if (allItems.length) {
                    renderList(this.value, { preserveScroll: false });
                }
                if (fetchDebounce) clearTimeout(fetchDebounce);
                fetchDebounce = setTimeout(function () {
                    fetchDebounce = null;
                    if (!currentCol) return;
                    if (isNumericCol(currentCol)) return;
                    var url = options.getColumnValuesUrl + (options.getColumnValuesUrl.indexOf('?') >= 0 ? '&' : '?') + 'column=' + encodeURIComponent(currentCol);
                    var searchVal = (searchInp && searchInp.value) ? searchInp.value.trim() : '';
                    if (searchVal) url += '&search=' + encodeURIComponent(searchVal);
                    fetch(url).then(function (r) { return r.json(); }).then(function (items) {
                        var raw = Array.isArray(items) ? items : [];
                        allItems = raw.map(function (v) {
                            if (v != null && typeof v === 'object' && ('value' in v || 'display' in v)) {
                                return {
                                    value: v.value != null ? v.value : v.display,
                                    display: v.display != null ? v.display : v.value
                                };
                            }
                            return { value: v, display: v };
                        });
                        renderList(searchInp ? searchInp.value : '', { preserveScroll: false });
                    });
                }, 400);
            });
            searchInp.addEventListener('keydown', function (e) {
                if (e.key === 'Enter') { e.preventDefault(); applyFilter(); }
            });
        }

        if (btnOk) btnOk.addEventListener('click', function (e) { e.preventDefault(); e.stopPropagation(); applyFilter(); });
        if (btnClear) btnClear.addEventListener('click', clearFilter);
        if (btnClose) btnClose.addEventListener('click', function () { panel.classList.add('d-none'); });

        document.addEventListener('click', function (e) {
            if (panel && !panel.classList.contains('d-none') && !panel.contains(e.target) && !e.target.closest('.erp-col-filter-btn')) {
                panel.classList.add('d-none');
            }
        });

        // مقابض توسعة الأعمدة
        if (options.wrapperId) {
            var wrapper = document.getElementById(options.wrapperId);
            if (wrapper && table) {
                var ths = table.querySelectorAll('thead th');
                ths.forEach(function (th) {
                    if (th.hasAttribute('data-col-fixed')) return;
                    th.style.position = 'relative';
                    var handle = document.createElement('div');
                    handle.className = 'erp-col-resize-handle';
                    th.appendChild(handle);
                    var startX, startWidth;
                    handle.addEventListener('mousedown', function (e) {
                        e.preventDefault();
                        startX = e.pageX;
                        startWidth = th.offsetWidth;
                        function onMouseMove(eMove) {
                            var diffX = eMove.pageX - startX;
                            var newWidth = startWidth + diffX;
                            if (newWidth < 50) newWidth = 50;
                            th.style.minWidth = newWidth + 'px';
                            th.style.width = newWidth + 'px';
                        }
                        function onMouseUp() {
                            document.removeEventListener('mousemove', onMouseMove);
                            document.removeEventListener('mouseup', onMouseUp);
                        }
                        document.addEventListener('mousemove', onMouseMove);
                        document.addEventListener('mouseup', onMouseUp);
                    });
                });

                if (wrapper.scrollWidth > wrapper.clientWidth) {
                    wrapper.scrollLeft = wrapper.scrollWidth - wrapper.clientWidth;
                }
            }
        }
    }

    window.ERP = window.ERP || {};
    window.ERP.initListColumnFilter = init;
})();
