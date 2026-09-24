using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260924065000_ImproveNovelReaderUx")]
public sealed class ImproveNovelReaderUx : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "NovelWorks" ADD COLUMN "MetadataDescription" TEXT NULL;
            ALTER TABLE "NovelWorks" ADD COLUMN "BannerImageUrl" TEXT NULL;
            ALTER TABLE "NovelWorks" ADD COLUMN "MetadataStatus" TEXT NULL;
            ALTER TABLE "NovelWorks" ADD COLUMN "MetadataChapterCount" INTEGER NULL;
            ALTER TABLE "NovelWorks" ADD COLUMN "MetadataVolumeCount" INTEGER NULL;

            ALTER TABLE "NovelProgress" ADD COLUMN "AnchorLanguage" TEXT NOT NULL DEFAULT 'ja';
            ALTER TABLE "NovelProgress" ADD COLUMN "AnchorParagraphIndex" INTEGER NULL;
            ALTER TABLE "NovelProgress" ADD COLUMN "AnchorOffset" INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE "NovelProgress" ADD COLUMN "AnchorText" TEXT NULL;

            CREATE TABLE "NovelBookmarks" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_NovelBookmarks" PRIMARY KEY,
                "ProfileId" TEXT NOT NULL,
                "WorkId" TEXT NOT NULL,
                "ChapterId" TEXT NOT NULL,
                "PositionPermille" INTEGER NOT NULL,
                "Language" TEXT NOT NULL,
                "ParagraphIndex" INTEGER NULL,
                "CharacterOffset" INTEGER NOT NULL,
                "AnchorText" TEXT NULL,
                "Label" TEXT NULL,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_NovelBookmarks_NovelWorks_WorkId"
                    FOREIGN KEY ("WorkId") REFERENCES "NovelWorks" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_NovelBookmarks_NovelChapters_ChapterId"
                    FOREIGN KEY ("ChapterId") REFERENCES "NovelChapters" ("Id") ON DELETE CASCADE
            );
            CREATE INDEX "IX_NovelBookmarks_ProfileId_WorkId_CreatedAt"
                ON "NovelBookmarks" ("ProfileId", "WorkId", "CreatedAt");
            CREATE INDEX "IX_NovelBookmarks_ProfileId_ChapterId"
                ON "NovelBookmarks" ("ProfileId", "ChapterId");
            CREATE INDEX "IX_NovelBookmarks_WorkId" ON "NovelBookmarks" ("WorkId");
            CREATE INDEX "IX_NovelBookmarks_ChapterId" ON "NovelBookmarks" ("ChapterId");

            CREATE TABLE "NovelHighlights" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_NovelHighlights" PRIMARY KEY,
                "ProfileId" TEXT NOT NULL,
                "WorkId" TEXT NOT NULL,
                "ChapterId" TEXT NOT NULL,
                "Language" TEXT NOT NULL,
                "ParagraphIndex" INTEGER NOT NULL,
                "StartOffset" INTEGER NOT NULL,
                "EndOffset" INTEGER NOT NULL,
                "Text" TEXT NOT NULL,
                "Note" TEXT NULL,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_NovelHighlights_NovelWorks_WorkId"
                    FOREIGN KEY ("WorkId") REFERENCES "NovelWorks" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_NovelHighlights_NovelChapters_ChapterId"
                    FOREIGN KEY ("ChapterId") REFERENCES "NovelChapters" ("Id") ON DELETE CASCADE
            );
            CREATE INDEX "IX_NovelHighlights_ProfileId_WorkId_CreatedAt"
                ON "NovelHighlights" ("ProfileId", "WorkId", "CreatedAt");
            CREATE INDEX "IX_NovelHighlights_ProfileId_ChapterId_Language_ParagraphIndex"
                ON "NovelHighlights" ("ProfileId", "ChapterId", "Language", "ParagraphIndex");
            CREATE INDEX "IX_NovelHighlights_WorkId" ON "NovelHighlights" ("WorkId");
            CREATE INDEX "IX_NovelHighlights_ChapterId" ON "NovelHighlights" ("ChapterId");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE "NovelHighlights";
            DROP TABLE "NovelBookmarks";

            ALTER TABLE "NovelProgress" DROP COLUMN "AnchorText";
            ALTER TABLE "NovelProgress" DROP COLUMN "AnchorOffset";
            ALTER TABLE "NovelProgress" DROP COLUMN "AnchorParagraphIndex";
            ALTER TABLE "NovelProgress" DROP COLUMN "AnchorLanguage";

            ALTER TABLE "NovelWorks" DROP COLUMN "MetadataVolumeCount";
            ALTER TABLE "NovelWorks" DROP COLUMN "MetadataChapterCount";
            ALTER TABLE "NovelWorks" DROP COLUMN "MetadataStatus";
            ALTER TABLE "NovelWorks" DROP COLUMN "BannerImageUrl";
            ALTER TABLE "NovelWorks" DROP COLUMN "MetadataDescription";
            """);
    }
}
