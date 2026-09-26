using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAcquisitionHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AcquisitionHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AnimeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SeasonNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    EpisodeNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    AbsoluteEpisodeNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    EventKind = table.Column<int>(type: "INTEGER", nullable: false),
                    ReleaseTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ReleaseKey = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Score = table.Column<int>(type: "INTEGER", nullable: true),
                    QualityKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    Indexer = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcquisitionHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcquisitionHistory_Anime_AnimeId",
                        column: x => x.AnimeId,
                        principalTable: "Anime",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionHistory_AnimeId_SeasonNumber_EpisodeNumber_OccurredAtUtc",
                table: "AcquisitionHistory",
                columns: new[] { "AnimeId", "SeasonNumber", "EpisodeNumber", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AcquisitionHistory");
        }
    }
}
