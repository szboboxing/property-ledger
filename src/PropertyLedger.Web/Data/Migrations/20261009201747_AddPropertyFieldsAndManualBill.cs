using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropertyLedger.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPropertyFieldsAndManualBill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ManagementContact",
                table: "Properties",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Properties",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "House"); // 既有数据默认「房屋」，避免空串无法转换为枚举

            migrationBuilder.AddColumn<string>(
                name: "UtilityContact",
                table: "Properties",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsManual",
                table: "Bills",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ManagementContact",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "UtilityContact",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "IsManual",
                table: "Bills");
        }
    }
}
