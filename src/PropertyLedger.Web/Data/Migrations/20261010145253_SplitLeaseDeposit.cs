using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropertyLedger.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class SplitLeaseDeposit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Deposit",
                table: "Leases",
                newName: "HouseDeposit");

            migrationBuilder.AddColumn<decimal>(
                name: "UtilityDeposit",
                table: "Leases",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UtilityDeposit",
                table: "Leases");

            migrationBuilder.RenameColumn(
                name: "HouseDeposit",
                table: "Leases",
                newName: "Deposit");
        }
    }
}
