using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260924202500_AddReaderPersonalization")]
public sealed class AddReaderPersonalization : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "NovelWorks"
                ADD COLUMN "MetadataGenresJson" TEXT NULL;

            ALTER TABLE "NovelBookmarks"
                ADD COLUMN "Style" TEXT NULL;

            ALTER TABLE "NovelBookmarks"
                ADD COLUMN "Color" TEXT NULL;

            CREATE TABLE "ReaderPreferences" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_ReaderPreferences" PRIMARY KEY,
                "ProfileId" TEXT NOT NULL,
                "ScopeKey" TEXT NOT NULL,
                "WorkId" TEXT NULL,
                "ReadingMode" TEXT NULL,
                "PageTransition" TEXT NULL,
                "TwoPageSpread" INTEGER NULL,
                "AutoScrollSpeed" REAL NULL,
                "FontFamily" TEXT NULL,
                "FontSizeRem" REAL NULL,
                "LineHeight" REAL NULL,
                "ParagraphSpacingEm" REAL NULL,
                "TextWidthPx" INTEGER NULL,
                "TextAlignment" TEXT NULL,
                "ChapterStyle" TEXT NULL,
                "PaperStyle" TEXT NULL,
                "GenreArtworkEnabled" INTEGER NULL,
                "GenreTheme" TEXT NULL,
                "BackgroundAssetId" TEXT NULL,
                "BackgroundIntensity" REAL NULL,
                "BookmarkStyle" TEXT NULL,
                "BookmarkColor" TEXT NULL,
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_ReaderPreferences_NovelWorks_WorkId"
                    FOREIGN KEY ("WorkId") REFERENCES "NovelWorks" ("Id") ON DELETE CASCADE
            );

            CREATE UNIQUE INDEX "IX_ReaderPreferences_ProfileId_ScopeKey"
                ON "ReaderPreferences" ("ProfileId", "ScopeKey");

            CREATE INDEX "IX_ReaderPreferences_WorkId"
                ON "ReaderPreferences" ("WorkId");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ReaderPreferences");
        migrationBuilder.DropColumn(name: "MetadataGenresJson", table: "NovelWorks");
        migrationBuilder.DropColumn(name: "Style", table: "NovelBookmarks");
        migrationBuilder.DropColumn(name: "Color", table: "NovelBookmarks");
    }
}
