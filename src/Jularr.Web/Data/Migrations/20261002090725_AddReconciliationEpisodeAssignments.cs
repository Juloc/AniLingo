using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReconciliationEpisodeAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssignedWorkEpisodeId",
                table: "LibraryReconciliationPlanItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryReconciliationPlanItems_AssignedWorkEpisodeId",
                table: "LibraryReconciliationPlanItems",
                column: "AssignedWorkEpisodeId");

            migrationBuilder.AddForeignKey(
                name: "FK_LibraryReconciliationPlanItems_WorkEpisodes_AssignedWorkEpi~",
                table: "LibraryReconciliationPlanItems",
                column: "AssignedWorkEpisodeId",
                principalTable: "WorkEpisodes",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LibraryReconciliationPlanItems_WorkEpisodes_AssignedWorkEpi~",
                table: "LibraryReconciliationPlanItems");

            migrationBuilder.DropIndex(
                name: "IX_LibraryReconciliationPlanItems_AssignedWorkEpisodeId",
                table: "LibraryReconciliationPlanItems");

            migrationBuilder.DropColumn(
                name: "AssignedWorkEpisodeId",
                table: "LibraryReconciliationPlanItems");
        }
    }
}
