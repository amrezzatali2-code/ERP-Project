// erp-list-column-filter.js
// سكربت مشترك: لوحة فلتر الأعمدة (بحث شبيه بإكسل) + مقابض توسعة الأعمدة
// الفلاتر تراكمية: عند التطبيق لا يُمسح فلتر بقية الأعمدة، يحدّث فقط filterCol_العمودالحالي
// الاستخدام: ERP.initListColumnFilter({ panelId, tableId, wrapperId, getColumnValuesUrl, defaultSort?, defaultDir? })

(function () {
    'use strict';

    function init(options) {
        if (!options || !options.panelId || !options.tableId || !options.getColumnValuesUrl) return;

        var panel = document.getElementById(options.panelId);
        var table = document.getElementById(options.tableId);
        if (!panel || !table) return;

        var titleEl = panel.querySelector('.erp-column-filter-header span');
        var btnClose = panel.querySelector('.erp-column-filter-header .btn-close');
        var searchInp = panel.querySelector('.erp-column-filter-body input[type="text"]');
        var listEl = panel.querySelector('.erp-column-filter-list');
        var searchRow = searchInp ? (searchInp.closest('.erp-filter-section') || searchInp.parentElement) : null;
        var sortSection = panel.querySelector('.erp-filter-sort-section');
        var btnOk = panel.querySelector('.erp-column-filter-actions button[data-action="apply"]') || panel.querySelector('.erp-column-filter-actions .btn-primary') || panel.querySelector('.erp-column-filter-actions .btn-erp-primary');
        var btnClear = panel.querySelector('.erp-column-filter-actions button[data-action="clear"]') || panel.querySelector('.erp-column-filter-actions .btn-secondary') || panel.querySelector('.erp-column-filter-actions .btn-erp-secondary');

        var defaultSort = (options.defaultSort || '').trim() || 'Id';
        var defaultDir = ((options.defaultDir || '').toLowerCase() === 'desc') ? 'desc' : 'asc';

        var currentCol = null, currentSortKey = null, allItems = [], currentSelected = new Set(), anchorRect = null;

        function getCurrentFilterValue(col) {
            return new URL(window.location.href).searchParams.get('filterCol_' + col) || '';
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
            // لا نعبّئ بوكس البحث في الكارت من الرابط أبداً — حتى لا يظهر نص (مثل كليكسان) لم يكتبه المستخدم.
            // البوكس للبحث داخل اللوحة فقط؛ الفلتر المطبّق يُقرأ من التشيك بوكسات (filterCol_X).
            if (searchInp) searchInp.value = '';
            currentSelected = new Set((getCurrentFilterValue(currentCol) || '').split(/[|,;]/).filter(Boolean));
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

            adjustPanelPosition();
            if (searchRow) searchRow.classList.remove('d-none');
            if (listEl) listEl.innerHTML = '<div class="text-muted text-center py-3">جاري التحميل...</div>';

            var url = options.getColumnValuesUrl + (options.getColumnValuesUrl.indexOf('?') >= 0 ? '&' : '?') + 'column=' + encodeURIComponent(currentCol);
            var searchVal = (searchInp && searchInp.value) ? searchInp.value.trim() : '';
            if (searchVal) url += '&search=' + encodeURIComponent(searchVal);

            fetch(url)
                .then(function (r) { return r.json(); })
                .then(function (items) {
                    allItems = items || [];
                    renderList(searchInp ? searchInp.value : '');
                })
                .catch(function () {
                    if (listEl) listEl.innerHTML = '<div class="text-danger text-center py-2">فشل التحميل</div>';
                });
        }

        function renderList(search) {
            if (!listEl) return;
            var term = (search || '').toLowerCase();
            var filtered = term ? allItems.filter(function (x) {
                return (x.display || x.value || '').toLowerCase().indexOf(term) >= 0;
            }) : allItems;
            var allChecked = filtered.length > 0 && filtered.every(function (x) { return currentSelected.has(String(x.value)); });
            var someChecked = filtered.some(function (x) { return currentSelected.has(String(x.value)); });

            var selectAllId = (panel.id || 'erpFilterPanel') + '_selectAll';
            listEl.innerHTML =
                '<div class="form-check mb-1">' +
                '<input type="checkbox" class="form-check-input" id="' + selectAllId + '">' +
                '<label class="form-check-label small" for="' + selectAllId + '">(تحديد الكل)</label>' +
                '</div>' +
                '<div class="erp-filter-items" style="max-height:200px;min-height:180px;overflow-y:auto;">' +
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
                    renderList(searchInp ? searchInp.value : '');
                };
            }
            listEl.querySelectorAll('.erp-filter-item').forEach(function (cb) {
                cb.onchange = function () {
                    if (this.checked) currentSelected.add(this.value);
                    else currentSelected.delete(this.value);
                    renderList(searchInp ? searchInp.value : '');
                };
            });
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
            var val = Array.from(currentSelected).filter(Boolean).join('|');
            var containsParam = 'filterCol_' + currentCol + '_contains';
            if (val) {
                url.searchParams.set('filterCol_' + currentCol, val);
                url.searchParams.delete(containsParam);
                // عند تطبيق فلتر من التشيك بوكسات نزيل البحث العام حتى لا يُطبَّق بحث "يحتوي" ولا يظهر نص في بوكس البحث — ينطبق على كل الأعمدة
                url.searchParams.delete('search');
                url.searchParams.delete('searchBy');
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
                renderList(this.value);
                if (fetchDebounce) clearTimeout(fetchDebounce);
                fetchDebounce = setTimeout(function () {
                    fetchDebounce = null;
                    if (!currentCol) return;
                    var url = options.getColumnValuesUrl + (options.getColumnValuesUrl.indexOf('?') >= 0 ? '&' : '?') + 'column=' + encodeURIComponent(currentCol);
                    var searchVal = (searchInp && searchInp.value) ? searchInp.value.trim() : '';
                    if (searchVal) url += '&search=' + encodeURIComponent(searchVal);
                    fetch(url).then(function (r) { return r.json(); }).then(function (items) {
                        allItems = items || [];
                        renderList(searchInp ? searchInp.value : '');
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
