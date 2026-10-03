using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryReconciliationPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LibraryReconciliationPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LibraryRootId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartFolder = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    IncludeSubfolders = table.Column<bool>(type: "boolean", nullable: false),
                    SkipConfidentAssignments = table.Column<bool>(type: "boolean", nullable: false),
                    OnlyUnclearItems = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ScannedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Failure = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryReconciliationPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LibraryReconciliationPlans_LibraryRoots_LibraryRootId",
                        column: x => x.LibraryRootId,
                        principalTable: "LibraryRoots",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LibraryReconciliationPlanItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelativePath = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    IsDirectory = table.Column<bool>(type: "boolean", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Confidence = table.Column<int>(type: "integer", nullable: false),
                    FileCount = table.Column<int>(type: "integer", nullable: false),
                    UnresolvedCount = table.Column<int>(type: "integer", nullable: false),
                    AssignedWorkId = table.Column<Guid>(type: "uuid", nullable: true),
                    DetectionSummary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryReconciliationPlanItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LibraryReconciliationPlanItems_LibraryReconciliationPlans_P~",
                        column: x => x.PlanId,
                        principalTable: "LibraryReconciliationPlans",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LibraryReconciliationPlanItems_Works_AssignedWorkId",
                        column: x => x.AssignedWorkId,
                        principalTable: "Works",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryReconciliationPlanItems_AssignedWorkId",
                table: "LibraryReconciliationPlanItems",
                column: "AssignedWorkId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryReconciliationPlanItems_PlanId_RelativePath",
                table: "LibraryReconciliationPlanItems",
                columns: new[] { "PlanId", "RelativePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryReconciliationPlans_LibraryRootId",
                table: "LibraryReconciliationPlans",
                column: "LibraryRootId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LibraryReconciliationPlanItems");

            migrationBuilder.DropTable(
                name: "LibraryReconciliationPlans");
        }
    }
}
