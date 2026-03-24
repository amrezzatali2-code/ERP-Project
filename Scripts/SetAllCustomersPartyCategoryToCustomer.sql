-- ============================================================
-- جعل نوع الطرف لجميع العملاء = Customer (حساب العملاء)
-- شغّل هذا السكربت مباشرة على قاعدة البيانات (SQL Server).
-- في قائمة العملاء سيظهر "عميل" (ترجمة) وليس الكلمة الإنجليزية.
-- ============================================================

UPDATE Customers
SET PartyCategory = N'Customer'
WHERE PartyCategory IS NULL
   OR PartyCategory <> N'Customer';

-- لو حابب تتأكد: عدد العملاء الذين تم تحديثهم (أو الذين كانوا فعلاً Customer)
-- SELECT PartyCategory, COUNT(*) AS Cnt FROM Customers GROUP BY PartyCategory;
