using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReconciliationReleaseMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssignedWorkEditionId",
                table: "LibraryReconciliationPlanItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedWorkVersionId",
                table: "LibraryReconciliationPlanItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AudioLanguage",
                table: "LibraryReconciliationPlanItems",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "LibraryReconciliationPlanItems",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QualitySource",
                table: "LibraryReconciliationPlanItems",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubtitleLanguage",
                table: "LibraryReconciliationPlanItems",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AudioLanguage",
                table: "LibraryReconciliationFileLinks",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "LibraryReconciliationFileLinks",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QualitySource",
                table: "LibraryReconciliationFileLinks",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubtitleLanguage",
                table: "LibraryReconciliationFileLinks",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkEditionId",
                table: "LibraryReconciliationFileLinks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkVersionId",
                table: "LibraryReconciliationFileLinks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryReconciliationPlanItems_AssignedWorkEditionId",
                table: "LibraryReconciliationPlanItems",
                column: "AssignedWorkEditionId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryReconciliationPlanItems_AssignedWorkVersionId",
                table: "LibraryReconciliationPlanItems",
                column: "AssignedWorkVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryReconciliationFileLinks_WorkEditionId",
                table: "LibraryReconciliationFileLinks",
                column: "WorkEditionId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryReconciliationFileLinks_WorkVersionId",
                table: "LibraryReconciliationFileLinks",
                column: "WorkVersionId");

            migrationBuilder.AddForeignKey(
                name: "FK_LibraryReconciliationFileLinks_WorkEditions_WorkEditionId",
                table: "LibraryReconciliationFileLinks",
                column: "WorkEditionId",
                principalTable: "WorkEditions",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_LibraryReconciliationFileLinks_WorkVersions_WorkVersionId",
                table: "LibraryReconciliationFileLinks",
                column: "WorkVersionId",
                principalTable: "WorkVersions",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_LibraryReconciliationPlanItems_WorkEditions_AssignedWorkEdi~",
                table: "LibraryReconciliationPlanItems",
                column: "AssignedWorkEditionId",
                principalTable: "WorkEditions",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_LibraryReconciliationPlanItems_WorkVersions_AssignedWorkVer~",
                table: "LibraryReconciliationPlanItems",
                column: "AssignedWorkVersionId",
                principalTable: "WorkVersions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LibraryReconciliationFileLinks_WorkEditions_WorkEditionId",
                table: "LibraryReconciliationFileLinks");

            migrationBuilder.DropForeignKey(
                name: "FK_LibraryReconciliationFileLinks_WorkVersions_WorkVersionId",
                table: "LibraryReconciliationFileLinks");

            migrationBuilder.DropForeignKey(
                name: "FK_LibraryReconciliationPlanItems_WorkEditions_AssignedWorkEdi~",
                table: "LibraryReconciliationPlanItems");

            migrationBuilder.DropForeignKey(
                name: "FK_LibraryReconciliationPlanItems_WorkVersions_AssignedWorkVer~",
                table: "LibraryReconciliationPlanItems");

            migrationBuilder.DropIndex(
                name: "IX_LibraryReconciliationPlanItems_AssignedWorkEditionId",
                table: "LibraryReconciliationPlanItems");

            migrationBuilder.DropIndex(
                name: "IX_LibraryReconciliationPlanItems_AssignedWorkVersionId",
                table: "LibraryReconciliationPlanItems");

            migrationBuilder.DropIndex(
                name: "IX_LibraryReconciliationFileLinks_WorkEditionId",
                table: "LibraryReconciliationFileLinks");

            migrationBuilder.DropIndex(
                name: "IX_LibraryReconciliationFileLinks_WorkVersionId",
                table: "LibraryReconciliationFileLinks");

            migrationBuilder.DropColumn(
                name: "AssignedWorkEditionId",
                table: "LibraryReconciliationPlanItems");

            migrationBuilder.DropColumn(
                name: "AssignedWorkVersionId",
                table: "LibraryReconciliationPlanItems");

            migrationBuilder.DropColumn(
                name: "AudioLanguage",
                table: "LibraryReconciliationPlanItems");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "LibraryReconciliationPlanItems");

            migrationBuilder.DropColumn(
                name: "QualitySource",
                table: "LibraryReconciliationPlanItems");

            migrationBuilder.DropColumn(
                name: "SubtitleLanguage",
                table: "LibraryReconciliationPlanItems");

            migrationBuilder.DropColumn(
                name: "AudioLanguage",
                table: "LibraryReconciliationFileLinks");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "LibraryReconciliationFileLinks");

            migrationBuilder.DropColumn(
                name: "QualitySource",
                table: "LibraryReconciliationFileLinks");

            migrationBuilder.DropColumn(
                name: "SubtitleLanguage",
                table: "LibraryReconciliationFileLinks");

            migrationBuilder.DropColumn(
                name: "WorkEditionId",
                table: "LibraryReconciliationFileLinks");

            migrationBuilder.DropColumn(
                name: "WorkVersionId",
                table: "LibraryReconciliationFileLinks");
        }
    }
}
