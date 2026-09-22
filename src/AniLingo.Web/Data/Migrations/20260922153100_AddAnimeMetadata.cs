using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

[Migration("20260922153100_AddAnimeMetadata")]
public sealed class AddAnimeMetadata : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AnimeMetadata",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                AnimeId = table.Column<Guid>(type: "TEXT", nullable: false),
                Provider = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                ExternalId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                PreferredTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                RomajiTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                EnglishTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                NativeTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                Description = table.Column<string>(type: "TEXT", nullable: true),
                CoverImageUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                BannerImageUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                Format = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                Status = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                Season = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                SeasonYear = table.Column<int>(type: "INTEGER", nullable: true),
                EpisodeCount = table.Column<int>(type: "INTEGER", nullable: true),
                EpisodeDurationMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AnimeMetadata", x => x.Id);
                table.ForeignKey(
                    name: "FK_AnimeMetadata_Anime_AnimeId",
                    column: x => x.AnimeId,
                    principalTable: "Anime",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AnimeMetadata_AnimeId",
            table: "AnimeMetadata",
            column: "AnimeId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AnimeMetadata_Provider_ExternalId",
            table: "AnimeMetadata",
            columns: new[] { "Provider", "ExternalId" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "AnimeMetadata");
}
