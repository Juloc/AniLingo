using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations;

/// <summary>
/// Spoiler-safe generated chapter artwork (#407): metadata of generated
/// images (the files live on media storage), per-profile preferences and
/// per-book overrides. Rows are keyed by work and chapter number so a
/// re-import that recreates chapters keeps accepted artwork.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260927200000_AddChapterArtwork")]
public sealed class AddChapterArtwork : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "ChapterArtworks" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_ChapterArtworks" PRIMARY KEY,
                "WorkId" TEXT NOT NULL,
                "ChapterId" TEXT NOT NULL,
                "ChapterNumber" INTEGER NOT NULL,
                "Status" TEXT NOT NULL CHECK ("Status" IN ('queued', 'generating', 'preview', 'accepted', 'failed', 'cancelled')),
                "AssetPath" TEXT NULL,
                "MediaType" TEXT NULL,
                "ContentHash" TEXT NULL,
                "ByteSize" INTEGER NULL,
                "ProviderId" TEXT NULL,
                "Model" TEXT NULL,
                "PromptVersion" INTEGER NOT NULL,
                "ContextHash" TEXT NULL,
                "ContextScope" TEXT NOT NULL,
                "Prompt" TEXT NULL,
                "NeutralPrompt" INTEGER NOT NULL DEFAULT 0,
                "Style" TEXT NOT NULL,
                "Quality" TEXT NOT NULL CHECK ("Quality" IN ('standard', 'high')),
                "Error" TEXT NULL,
                "OperationId" TEXT NULL,
                "RequestedByProfileId" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                "AcceptedAt" TEXT NULL
            );

            CREATE INDEX "IX_ChapterArtworks_Work_Chapter" ON "ChapterArtworks" ("WorkId", "ChapterNumber");
            CREATE UNIQUE INDEX "IX_ChapterArtworks_Accepted" ON "ChapterArtworks" ("WorkId", "ChapterNumber")
                WHERE "Status" = 'accepted';

            CREATE TABLE "ChapterArtworkPreferences" (
                "ProfileId" TEXT NOT NULL CONSTRAINT "PK_ChapterArtworkPreferences" PRIMARY KEY,
                "Enabled" INTEGER NOT NULL,
                "AutoGenerate" INTEGER NOT NULL,
                "Style" TEXT NOT NULL,
                "Quality" TEXT NOT NULL CHECK ("Quality" IN ('standard', 'high')),
                "Variations" INTEGER NOT NULL CHECK ("Variations" BETWEEN 1 AND 3),
                "UpdatedAt" TEXT NOT NULL
            );

            CREATE TABLE "ChapterArtworkWorkSettings" (
                "WorkId" TEXT NOT NULL CONSTRAINT "PK_ChapterArtworkWorkSettings" PRIMARY KEY,
                "Enabled" INTEGER NULL,
                "Style" TEXT NULL,
                "SeriesStyle" TEXT NULL,
                "UseChapterTitles" INTEGER NOT NULL DEFAULT 0,
                "UpdatedAt" TEXT NOT NULL
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE "ChapterArtworkWorkSettings";
            DROP TABLE "ChapterArtworkPreferences";
            DROP TABLE "ChapterArtworks";
            """);
    }
}
