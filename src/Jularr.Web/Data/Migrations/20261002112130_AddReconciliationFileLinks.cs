using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReconciliationFileLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LibraryReconciliationFileLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    LibraryRootId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkEpisodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkVolumeId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkChapterId = table.Column<Guid>(type: "uuid", nullable: true),
                    OriginalRelativePath = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    RelativePath = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    CommittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryReconciliationFileLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LibraryReconciliationFileLinks_LibraryReconciliationPlanIte~",
                        column: x => x.PlanItemId,
                        principalTable: "LibraryReconciliationPlanItems",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LibraryReconciliationFileLinks_LibraryReconciliationPlans_P~",
                        column: x => x.PlanId,
                        principalTable: "LibraryReconciliationPlans",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LibraryReconciliationFileLinks_LibraryRoots_LibraryRootId",
                        column: x => x.LibraryRootId,
                        principalTable: "LibraryRoots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LibraryReconciliationFileLinks_WorkChapters_WorkChapterId",
                        column: x => x.WorkChapterId,
                        principalTable: "WorkChapters",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LibraryReconciliationFileLinks_WorkEpisodes_WorkEpisodeId",
                        column: x => x.WorkEpisodeId,
                        principalTable: "WorkEpisodes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LibraryReconciliationFileLinks_WorkVolumes_WorkVolumeId",
                        column: x => x.WorkVolumeId,
                        principalTable: "WorkVolumes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LibraryReconciliationFileLinks_Works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "Works",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryReconciliationFileLinks_LibraryRootId_RelativePath",
                table: "LibraryReconciliationFileLinks",
                columns: new[] { "LibraryRootId", "RelativePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryReconciliationFileLinks_PlanId",
                table: "LibraryReconciliationFileLinks",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryReconciliationFileLinks_PlanItemId",
                table: "LibraryReconciliationFileLinks",
                column: "PlanItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryReconciliationFileLinks_WorkChapterId",
                table: "LibraryReconciliationFileLinks",
                column: "WorkChapterId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryReconciliationFileLinks_WorkEpisodeId",
                table: "LibraryReconciliationFileLinks",
                column: "WorkEpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryReconciliationFileLinks_WorkId",
                table: "LibraryReconciliationFileLinks",
                column: "WorkId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryReconciliationFileLinks_WorkVolumeId",
                table: "LibraryReconciliationFileLinks",
                column: "WorkVolumeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LibraryReconciliationFileLinks");
        }
    }
}
