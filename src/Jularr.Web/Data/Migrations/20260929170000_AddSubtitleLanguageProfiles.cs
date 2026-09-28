using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubtitleLanguageProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Forced",
                table: "SubtitleTracks",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Sdh",
                table: "SubtitleTracks",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "SubtitleLanguageProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    CutoffPosition = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubtitleLanguageProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubtitleLanguageProfileItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LanguageTag = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Forced = table.Column<bool>(type: "INTEGER", nullable: false),
                    Sdh = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubtitleLanguageProfileItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubtitleLanguageProfileItems_SubtitleLanguageProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "SubtitleLanguageProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubtitleProfileAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MediaType = table.Column<int>(type: "INTEGER", nullable: true),
                    LibraryRootId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProfileId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubtitleProfileAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubtitleProfileAssignments_LibraryRoots_LibraryRootId",
                        column: x => x.LibraryRootId,
                        principalTable: "LibraryRoots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SubtitleProfileAssignments_SubtitleLanguageProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "SubtitleLanguageProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubtitleLanguageProfileItems_ProfileId_SortOrder",
                table: "SubtitleLanguageProfileItems",
                columns: new[] { "ProfileId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_SubtitleLanguageProfiles_Name",
                table: "SubtitleLanguageProfiles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubtitleProfileAssignments_LibraryRootId",
                table: "SubtitleProfileAssignments",
                column: "LibraryRootId");

            migrationBuilder.CreateIndex(
                name: "IX_SubtitleProfileAssignments_MediaType_LibraryRootId",
                table: "SubtitleProfileAssignments",
                columns: new[] { "MediaType", "LibraryRootId" });

            migrationBuilder.CreateIndex(
                name: "IX_SubtitleProfileAssignments_ProfileId",
                table: "SubtitleProfileAssignments",
                column: "ProfileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubtitleLanguageProfileItems");

            migrationBuilder.DropTable(
                name: "SubtitleProfileAssignments");

            migrationBuilder.DropTable(
                name: "SubtitleLanguageProfiles");

            migrationBuilder.DropColumn(
                name: "Forced",
                table: "SubtitleTracks");

            migrationBuilder.DropColumn(
                name: "Sdh",
                table: "SubtitleTracks");
        }
    }
}
