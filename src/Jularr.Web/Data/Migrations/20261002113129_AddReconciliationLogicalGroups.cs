using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReconciliationLogicalGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LogicalGroupId",
                table: "LibraryReconciliationPlanItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LibraryReconciliationLogicalGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryReconciliationLogicalGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LibraryReconciliationLogicalGroups_LibraryReconciliationPla~",
                        column: x => x.PlanId,
                        principalTable: "LibraryReconciliationPlans",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryReconciliationPlanItems_LogicalGroupId",
                table: "LibraryReconciliationPlanItems",
                column: "LogicalGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryReconciliationLogicalGroups_PlanId_Name",
                table: "LibraryReconciliationLogicalGroups",
                columns: new[] { "PlanId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_LibraryReconciliationPlanItems_LibraryReconciliationLogical~",
                table: "LibraryReconciliationPlanItems",
                column: "LogicalGroupId",
                principalTable: "LibraryReconciliationLogicalGroups",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LibraryReconciliationPlanItems_LibraryReconciliationLogical~",
                table: "LibraryReconciliationPlanItems");

            migrationBuilder.DropTable(
                name: "LibraryReconciliationLogicalGroups");

            migrationBuilder.DropIndex(
                name: "IX_LibraryReconciliationPlanItems_LogicalGroupId",
                table: "LibraryReconciliationPlanItems");

            migrationBuilder.DropColumn(
                name: "LogicalGroupId",
                table: "LibraryReconciliationPlanItems");
        }
    }
}
