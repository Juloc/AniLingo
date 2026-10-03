using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReconciliationChapterAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssignedWorkChapterId",
                table: "LibraryReconciliationPlanItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryReconciliationPlanItems_AssignedWorkChapterId",
                table: "LibraryReconciliationPlanItems",
                column: "AssignedWorkChapterId");

            migrationBuilder.AddForeignKey(
                name: "FK_LibraryReconciliationPlanItems_WorkChapters_AssignedWorkCha~",
                table: "LibraryReconciliationPlanItems",
                column: "AssignedWorkChapterId",
                principalTable: "WorkChapters",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LibraryReconciliationPlanItems_WorkChapters_AssignedWorkCha~",
                table: "LibraryReconciliationPlanItems");

            migrationBuilder.DropIndex(
                name: "IX_LibraryReconciliationPlanItems_AssignedWorkChapterId",
                table: "LibraryReconciliationPlanItems");

            migrationBuilder.DropColumn(
                name: "AssignedWorkChapterId",
                table: "LibraryReconciliationPlanItems");
        }
    }
}
