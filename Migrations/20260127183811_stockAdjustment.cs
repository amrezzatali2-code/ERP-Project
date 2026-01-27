using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Migrations
{
    /// <inheritdoc />
    public partial class stockAdjustment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // StockTransfers columns - using SQL with IF NOT EXISTS check
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StockTransfers' AND COLUMN_NAME = 'IsPosted')
                BEGIN
                    ALTER TABLE [StockTransfers] ADD [IsPosted] bit NOT NULL DEFAULT CAST(0 AS bit);
                END
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StockTransfers' AND COLUMN_NAME = 'PostedAt')
                BEGIN
                    ALTER TABLE [StockTransfers] ADD [PostedAt] datetime2 NULL;
                END
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StockTransfers' AND COLUMN_NAME = 'PostedBy')
                BEGIN
                    ALTER TABLE [StockTransfers] ADD [PostedBy] nvarchar(100) NULL;
                END
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StockTransfers' AND COLUMN_NAME = 'Status')
                BEGIN
                    ALTER TABLE [StockTransfers] ADD [Status] nvarchar(50) NULL;
                END
            ");

            // StockAdjustments columns - using SQL with IF NOT EXISTS check
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StockAdjustments' AND COLUMN_NAME = 'IsPosted')
                BEGIN
                    ALTER TABLE [StockAdjustments] ADD [IsPosted] bit NOT NULL DEFAULT CAST(0 AS bit);
                END
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StockAdjustments' AND COLUMN_NAME = 'PostedAt')
                BEGIN
                    ALTER TABLE [StockAdjustments] ADD [PostedAt] datetime2 NULL;
                END
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StockAdjustments' AND COLUMN_NAME = 'PostedBy')
                BEGIN
                    ALTER TABLE [StockAdjustments] ADD [PostedBy] nvarchar(100) NULL;
                END
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StockAdjustments' AND COLUMN_NAME = 'Status')
                BEGIN
                    ALTER TABLE [StockAdjustments] ADD [Status] nvarchar(50) NULL;
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // StockTransfers columns - using SQL with IF EXISTS check
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StockTransfers' AND COLUMN_NAME = 'Status')
                BEGIN
                    ALTER TABLE [StockTransfers] DROP COLUMN [Status];
                END
            ");

            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StockTransfers' AND COLUMN_NAME = 'PostedBy')
                BEGIN
                    ALTER TABLE [StockTransfers] DROP COLUMN [PostedBy];
                END
            ");

            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StockTransfers' AND COLUMN_NAME = 'PostedAt')
                BEGIN
                    ALTER TABLE [StockTransfers] DROP COLUMN [PostedAt];
                END
            ");

            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StockTransfers' AND COLUMN_NAME = 'IsPosted')
                BEGIN
                    ALTER TABLE [StockTransfers] DROP COLUMN [IsPosted];
                END
            ");

            // StockAdjustments columns - using SQL with IF EXISTS check
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StockAdjustments' AND COLUMN_NAME = 'Status')
                BEGIN
                    ALTER TABLE [StockAdjustments] DROP COLUMN [Status];
                END
            ");

            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StockAdjustments' AND COLUMN_NAME = 'PostedBy')
                BEGIN
                    ALTER TABLE [StockAdjustments] DROP COLUMN [PostedBy];
                END
            ");

            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StockAdjustments' AND COLUMN_NAME = 'PostedAt')
                BEGIN
                    ALTER TABLE [StockAdjustments] DROP COLUMN [PostedAt];
                END
            ");

            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StockAdjustments' AND COLUMN_NAME = 'IsPosted')
                BEGIN
                    ALTER TABLE [StockAdjustments] DROP COLUMN [IsPosted];
                END
            ");
        }
    }
}
