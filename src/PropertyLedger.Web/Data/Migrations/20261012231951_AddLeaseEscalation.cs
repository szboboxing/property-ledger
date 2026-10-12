using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropertyLedger.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaseEscalation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EscMode",
                table: "Leases",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "EscValue",
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
                name: "EscMode",
                table: "Leases");

            migrationBuilder.DropColumn(
                name: "EscValue",
                table: "Leases");
        }
    }
}
