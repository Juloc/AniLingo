using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

/// <summary>
/// Adds AnimeLocalMetadata: the one canonical place for facts read from a local tvshow.nfo
/// (plot/overview, original title, year/premiered, MAL/TVDB/TMDB/IMDb IDs). AnimeMetadata
/// (Features/Metadata) belongs to a matched provider, so this NFO-derived data is never stored
/// there; it stays a distinct, clearly-sourced row per anime, one-to-one with Anime.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260926144500_AddAnimeLocalMetadata")]
public sealed class AddAnimeLocalMetadata : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AnimeLocalMetadata",
            columns: table => new
            {
                AnimeId = table.Column<Guid>(type: "TEXT", nullable: false),
                Source = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                OriginalTitle = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                Plot = table.Column<string>(type: "TEXT", nullable: true),
                Year = table.Column<int>(type: "INTEGER", nullable: true),
                Premiered = table.Column<DateOnly>(type: "TEXT", nullable: true),
                MyAnimeListId = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                TvdbId = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                TmdbId = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                ImdbId = table.Column<string>(type: "TEXT", maxLength: 12, nullable: true),
                SourceFileSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                SourceFileLastWriteTimeUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AnimeLocalMetadata", x => x.AnimeId);
                table.ForeignKey(
                    name: "FK_AnimeLocalMetadata_Anime_AnimeId",
                    column: x => x.AnimeId,
                    principalTable: "Anime",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AnimeLocalMetadata");
    }
}
