using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReconciliationObservedFileState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ObservedLastWriteTimeUtc",
                table: "LibraryReconciliationPlanItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ObservedSizeBytes",
                table: "LibraryReconciliationPlanItems",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ObservedLastWriteTimeUtc",
                table: "LibraryReconciliationPlanItems");

            migrationBuilder.DropColumn(
                name: "ObservedSizeBytes",
                table: "LibraryReconciliationPlanItems");
        }
    }
}
