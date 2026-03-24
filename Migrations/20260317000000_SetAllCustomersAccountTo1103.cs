using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Migrations
{
    /// <inheritdoc />
    /// <remarks>تعيين كل العملاء إلى حساب العملاء (كود 1103). يمكنك تعديل الحساب لكل عميل لاحقاً من شاشة العملاء.</remarks>
    public partial class SetAllCustomersAccountTo1103 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE c
                SET c.AccountId = a.AccountId
                FROM Customers c
                INNER JOIN (SELECT TOP 1 AccountId FROM Accounts WHERE AccountCode = N'1103') a ON 1=1
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // لا نرجع الحسابات إلى قيمها السابقة (كل عميل قد كان له حساب مختلف)
            // يمكنك تعديل الحساب يدوياً من شاشة العملاء إن احتجت
        }
    }
}
