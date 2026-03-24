-- =============================================================================
-- تصفير جداول: باتش (Batches) ، ستوك ليدجر (StockLedger) ، ستوك باتش (Stock_Batches) ، دفتر الاستاذ (LedgerEntries)
-- يُنفَّذ مرة واحدة على قاعدة ERP دون تغيير أي كود.
-- الترتيب إلزامي بسبب قيود المفتاح الأجنبي (FK).
-- =============================================================================

USE [ERP];
GO

BEGIN TRANSACTION;

BEGIN TRY
    -- (1) StockFifoMap يعتمد على StockLedger (OutEntryId, InEntryId)
    DELETE FROM [dbo].[StockFifoMap];

    -- (2) ProductDiscountOverrides يعتمد على Batches (BatchId) — سبب خطأ FK_ProductDiscountOverrides_Batches_BatchId
    -- حذف كل السطور حتى يُسمح بحذف Batches (لو أردت الإبقاء على سطور لا تشير لباتش: استبدل بـ DELETE ... WHERE BatchId IS NOT NULL)
    DELETE FROM [dbo].[ProductDiscountOverrides];

    -- (3) إلغاء ربط Batches من الجداول التي تشير إليها (حتى نستطيع حذف Batches لاحقاً)
    UPDATE [dbo].[StockTransferLines] SET [BatchId] = NULL WHERE [BatchId] IS NOT NULL;
    UPDATE [dbo].[StockAdjustmentLines] SET [BatchId] = NULL WHERE [BatchId] IS NOT NULL;

    -- (4) دفتر الحركة المخزنية (يجب حذفه قبل Batches لأنه يشير إلى BatchId)
    DELETE FROM [dbo].[StockLedger];

    -- (5) جدول التشغيلات (Batches)
    DELETE FROM [dbo].[Batches];

    -- (6) رصيد التشغيلات (Stock_Batches)
    DELETE FROM [dbo].[Stock_Batches];

    -- (7) دفتر الأستاذ (LedgerEntries)
    DELETE FROM [dbo].[LedgerEntries];

    COMMIT TRANSACTION;
    PRINT N'تم تصفير الجداول بنجاح: StockFifoMap, ProductDiscountOverrides (جزئي), StockLedger, Batches, Stock_Batches, LedgerEntries.';
END TRY
BEGIN CATCH
    ROLLBACK TRANSACTION;
    DECLARE @msg NVARCHAR(4000) = ERROR_MESSAGE();
    RAISERROR(N'خطأ: %s', 16, 1, @msg);
END CATCH;
GO
