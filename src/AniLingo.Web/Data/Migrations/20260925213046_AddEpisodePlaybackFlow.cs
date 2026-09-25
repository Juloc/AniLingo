using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEpisodePlaybackFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EpisodePlaybackHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProfileId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    EpisodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastPlayedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PositionMs = table.Column<long>(type: "INTEGER", nullable: false),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: true),
                    ReachedEnd = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EpisodePlaybackHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EpisodePlaybackHistory_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProfilePlaybackPreferences",
                columns: table => new
                {
                    ProfileId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    AutoplayNext = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfilePlaybackPreferences", x => x.ProfileId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EpisodePlaybackHistory_EpisodeId",
                table: "EpisodePlaybackHistory",
                column: "EpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_EpisodePlaybackHistory_ProfileId_LastPlayedAt",
                table: "EpisodePlaybackHistory",
                columns: new[] { "ProfileId", "LastPlayedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EpisodePlaybackHistory");

            migrationBuilder.DropTable(
                name: "ProfilePlaybackPreferences");
        }
    }
}
