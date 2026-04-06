-- توزيع أنواع الحركة في دفتر المخزن — شغّله لترى لماذا بقي آلاف السجلات بعد حذف Opening فقط
SELECT SourceType, COUNT(*) AS Cnt
FROM StockLedger
GROUP BY SourceType
ORDER BY Cnt DESC;
