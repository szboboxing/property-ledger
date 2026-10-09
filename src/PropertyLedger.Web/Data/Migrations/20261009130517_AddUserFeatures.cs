using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropertyLedger.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Features",
                table: "Users",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Features",
                table: "Users");
        }
    }
}
