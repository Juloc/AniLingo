using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReconciliationVolumeAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssignedWorkVolumeId",
                table: "LibraryReconciliationPlanItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryReconciliationPlanItems_AssignedWorkVolumeId",
                table: "LibraryReconciliationPlanItems",
                column: "AssignedWorkVolumeId");

            migrationBuilder.AddForeignKey(
                name: "FK_LibraryReconciliationPlanItems_WorkVolumes_AssignedWorkVolu~",
                table: "LibraryReconciliationPlanItems",
                column: "AssignedWorkVolumeId",
                principalTable: "WorkVolumes",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LibraryReconciliationPlanItems_WorkVolumes_AssignedWorkVolu~",
                table: "LibraryReconciliationPlanItems");

            migrationBuilder.DropIndex(
                name: "IX_LibraryReconciliationPlanItems_AssignedWorkVolumeId",
                table: "LibraryReconciliationPlanItems");

            migrationBuilder.DropColumn(
                name: "AssignedWorkVolumeId",
                table: "LibraryReconciliationPlanItems");
        }
    }
}
