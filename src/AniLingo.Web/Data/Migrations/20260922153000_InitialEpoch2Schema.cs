using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260922153000_InitialEpoch2Schema")]
public sealed class InitialEpoch2Schema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Anime",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Key = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Anime", x => x.Id));

        migrationBuilder.CreateTable(
            name: "LibraryRoots",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                Path = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                LastScannedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_LibraryRoots", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Terms",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Language = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                Canonical = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                Reading = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                Meaning = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_Terms", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Episodes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                AnimeId = table.Column<Guid>(type: "TEXT", nullable: false),
                SeasonNumber = table.Column<int>(type: "INTEGER", nullable: false),
                Number = table.Column<int>(type: "INTEGER", nullable: false),
                Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                DiscoveredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Episodes", x => x.Id);
                table.ForeignKey(
                    name: "FK_Episodes_Anime_AnimeId",
                    column: x => x.AnimeId,
                    principalTable: "Anime",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Reviews",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                ProfileId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                TermId = table.Column<Guid>(type: "TEXT", nullable: false),
                Rating = table.Column<int>(type: "INTEGER", nullable: false),
                ReviewedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                NextReviewAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Reviews", x => x.Id);
                table.ForeignKey(
                    name: "FK_Reviews_Terms_TermId",
                    column: x => x.TermId,
                    principalTable: "Terms",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "UserTerms",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                ProfileId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                TermId = table.Column<Guid>(type: "TEXT", nullable: false),
                State = table.Column<int>(type: "INTEGER", nullable: false),
                IntervalDays = table.Column<int>(type: "INTEGER", nullable: false),
                NextReviewAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserTerms", x => x.Id);
                table.ForeignKey(
                    name: "FK_UserTerms_Terms_TermId",
                    column: x => x.TermId,
                    principalTable: "Terms",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "MediaFiles",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                LibraryRootId = table.Column<Guid>(type: "TEXT", nullable: false),
                EpisodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                Path = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                LastWriteTimeUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                DiscoveredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MediaFiles", x => x.Id);
                table.ForeignKey(
                    name: "FK_MediaFiles_Episodes_EpisodeId",
                    column: x => x.EpisodeId,
                    principalTable: "Episodes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_MediaFiles_LibraryRoots_LibraryRootId",
                    column: x => x.LibraryRootId,
                    principalTable: "LibraryRoots",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "SubtitleTracks",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                EpisodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                Path = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                Language = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                Format = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                SourceUpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                ImportedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SubtitleTracks", x => x.Id);
                table.ForeignKey(
                    name: "FK_SubtitleTracks_Episodes_EpisodeId",
                    column: x => x.EpisodeId,
                    principalTable: "Episodes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "EpisodeTerms",
            columns: table => new
            {
                EpisodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                TermId = table.Column<Guid>(type: "TEXT", nullable: false),
                Occurrences = table.Column<int>(type: "INTEGER", nullable: false),
                FirstCueStartMs = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EpisodeTerms", x => new { x.EpisodeId, x.TermId });
                table.ForeignKey(
                    name: "FK_EpisodeTerms_Episodes_EpisodeId",
                    column: x => x.EpisodeId,
                    principalTable: "Episodes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_EpisodeTerms_Terms_TermId",
                    column: x => x.TermId,
                    principalTable: "Terms",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "SubtitleCues",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                SubtitleTrackId = table.Column<Guid>(type: "TEXT", nullable: false),
                StartMs = table.Column<int>(type: "INTEGER", nullable: false),
                EndMs = table.Column<int>(type: "INTEGER", nullable: false),
                Text = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SubtitleCues", x => x.Id);
                table.ForeignKey(
                    name: "FK_SubtitleCues_SubtitleTracks_SubtitleTrackId",
                    column: x => x.SubtitleTrackId,
                    principalTable: "SubtitleTracks",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(name: "IX_Anime_Key", table: "Anime", column: "Key", unique: true);
        migrationBuilder.CreateIndex(name: "IX_Episodes_AnimeId_SeasonNumber_Number", table: "Episodes", columns: new[] { "AnimeId", "SeasonNumber", "Number" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_EpisodeTerms_TermId", table: "EpisodeTerms", column: "TermId");
        migrationBuilder.CreateIndex(name: "IX_LibraryRoots_Path", table: "LibraryRoots", column: "Path", unique: true);
        migrationBuilder.CreateIndex(name: "IX_MediaFiles_EpisodeId", table: "MediaFiles", column: "EpisodeId");
        migrationBuilder.CreateIndex(name: "IX_MediaFiles_LibraryRootId_EpisodeId", table: "MediaFiles", columns: new[] { "LibraryRootId", "EpisodeId" });
        migrationBuilder.CreateIndex(name: "IX_MediaFiles_Path", table: "MediaFiles", column: "Path", unique: true);
        migrationBuilder.CreateIndex(name: "IX_Reviews_ProfileId_ReviewedAt", table: "Reviews", columns: new[] { "ProfileId", "ReviewedAt" });
        migrationBuilder.CreateIndex(name: "IX_Reviews_TermId", table: "Reviews", column: "TermId");
        migrationBuilder.CreateIndex(name: "IX_SubtitleCues_SubtitleTrackId_StartMs", table: "SubtitleCues", columns: new[] { "SubtitleTrackId", "StartMs" });
        migrationBuilder.CreateIndex(name: "IX_SubtitleTracks_EpisodeId_Language", table: "SubtitleTracks", columns: new[] { "EpisodeId", "Language" });
        migrationBuilder.CreateIndex(name: "IX_SubtitleTracks_Path", table: "SubtitleTracks", column: "Path", unique: true);
        migrationBuilder.CreateIndex(name: "IX_Terms_Language_Canonical", table: "Terms", columns: new[] { "Language", "Canonical" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_UserTerms_ProfileId_State_NextReviewAt", table: "UserTerms", columns: new[] { "ProfileId", "State", "NextReviewAt" });
        migrationBuilder.CreateIndex(name: "IX_UserTerms_ProfileId_TermId", table: "UserTerms", columns: new[] { "ProfileId", "TermId" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_UserTerms_TermId", table: "UserTerms", column: "TermId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "EpisodeTerms");
        migrationBuilder.DropTable(name: "MediaFiles");
        migrationBuilder.DropTable(name: "Reviews");
        migrationBuilder.DropTable(name: "SubtitleCues");
        migrationBuilder.DropTable(name: "UserTerms");
        migrationBuilder.DropTable(name: "LibraryRoots");
        migrationBuilder.DropTable(name: "SubtitleTracks");
        migrationBuilder.DropTable(name: "Terms");
        migrationBuilder.DropTable(name: "Episodes");
        migrationBuilder.DropTable(name: "Anime");
    }
}
