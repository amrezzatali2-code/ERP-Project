// grid-bulk.js
// مسئول عن اختيار متعدد في الجداول (Bulk Actions)

(function () {

    document.addEventListener('DOMContentLoaded', function () {

        // كل جدول عليه data-grid-bulk="true"
        var grids = document.querySelectorAll('table[data-grid-bulk="true"]');
        if (!grids.length) return;

        grids.forEach(function (table) {

            var selectAll = table.querySelector('.erp-select-all-rows');
            var rowCheckboxes = table.querySelectorAll('.erp-select-row');

            // زر تحديد الكل
            if (selectAll) {
                selectAll.addEventListener('change', function () {
                    var checked = selectAll.checked;
                    rowCheckboxes.forEach(function (cb) {
                        cb.checked = checked;
                    });
                });
            }

            // لو المستخدم غيّر واحدة من الصفوف، نحدث حالة زر تحديد الكل
            rowCheckboxes.forEach(function (cb) {
                cb.addEventListener('change', function () {
                    if (!selectAll) return;

                    var allChecked = Array.from(rowCheckboxes).every(function (x) { return x.checked; });
                    var noneChecked = Array.from(rowCheckboxes).every(function (x) { return !x.checked; });

                    if (allChecked) {
                        selectAll.checked = true;
                        selectAll.indeterminate = false;
                    } else if (noneChecked) {
                        selectAll.checked = false;
                        selectAll.indeterminate = false;
                    } else {
                        selectAll.indeterminate = true;
                    }
                });
            });

            // الفورم الأب المسؤول عن BulkDelete
            var bulkForm = table.closest('form.erp-bulk-form');
            if (bulkForm) {
                bulkForm.addEventListener('submit', function (e) {
                    var anyChecked = Array.from(rowCheckboxes).some(function (x) { return x.checked; });
                    if (!anyChecked) {
                        e.preventDefault();
                        alert('من فضلك اختر على الأقل عميل واحد قبل تنفيذ الحذف.');
                    } else {
                        if (!confirm('هل أنت متأكد من حذف العملاء المحددين؟')) {
                            e.preventDefault();
                        }
                    }
                });
            }

        });
    });

})();
