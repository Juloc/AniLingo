using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260923104500_AddNovelReader")]
public sealed class AddNovelReader : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "NovelWorks" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_NovelWorks" PRIMARY KEY,
                "SourceProvider" TEXT NOT NULL,
                "SourceKey" TEXT NOT NULL,
                "SourceUrl" TEXT NOT NULL,
                "Title" TEXT NOT NULL,
                "Author" TEXT NULL,
                "Description" TEXT NULL,
                "MetadataProvider" TEXT NULL,
                "MetadataExternalId" TEXT NULL,
                "MetadataTitle" TEXT NULL,
                "MetadataNativeTitle" TEXT NULL,
                "CoverImageUrl" TEXT NULL,
                "Format" TEXT NULL,
                "ImportedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL
            );
            CREATE UNIQUE INDEX "IX_NovelWorks_SourceProvider_SourceKey"
                ON "NovelWorks" ("SourceProvider", "SourceKey");
            CREATE UNIQUE INDEX "IX_NovelWorks_MetadataProvider_MetadataExternalId"
                ON "NovelWorks" ("MetadataProvider", "MetadataExternalId");

            CREATE TABLE "NovelChapters" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_NovelChapters" PRIMARY KEY,
                "WorkId" TEXT NOT NULL,
                "Number" INTEGER NOT NULL,
                "SourceUrl" TEXT NOT NULL,
                "Title" TEXT NOT NULL,
                "OriginalText" TEXT NOT NULL,
                "SourceHash" TEXT NOT NULL,
                "PublishedAt" TEXT NULL,
                "ImportedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_NovelChapters_NovelWorks_WorkId"
                    FOREIGN KEY ("WorkId") REFERENCES "NovelWorks" ("Id") ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX "IX_NovelChapters_WorkId_Number"
                ON "NovelChapters" ("WorkId", "Number");

            CREATE TABLE "NovelTranslations" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_NovelTranslations" PRIMARY KEY,
                "ChapterId" TEXT NOT NULL,
                "TargetLanguage" TEXT NOT NULL,
                "ProviderId" TEXT NOT NULL,
                "PromptVersion" INTEGER NOT NULL,
                "SourceHash" TEXT NOT NULL,
                "Text" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_NovelTranslations_NovelChapters_ChapterId"
                    FOREIGN KEY ("ChapterId") REFERENCES "NovelChapters" ("Id") ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX "IX_NovelTranslations_ChapterId_TargetLanguage_ProviderId_PromptVersion_SourceHash"
                ON "NovelTranslations" ("ChapterId", "TargetLanguage", "ProviderId", "PromptVersion", "SourceHash");

            CREATE TABLE "NovelProgress" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_NovelProgress" PRIMARY KEY,
                "ProfileId" TEXT NOT NULL,
                "WorkId" TEXT NOT NULL,
                "ChapterId" TEXT NOT NULL,
                "PositionPermille" INTEGER NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_NovelProgress_NovelWorks_WorkId"
                    FOREIGN KEY ("WorkId") REFERENCES "NovelWorks" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_NovelProgress_NovelChapters_ChapterId"
                    FOREIGN KEY ("ChapterId") REFERENCES "NovelChapters" ("Id") ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX "IX_NovelProgress_ProfileId_WorkId"
                ON "NovelProgress" ("ProfileId", "WorkId");
            CREATE INDEX "IX_NovelProgress_WorkId" ON "NovelProgress" ("WorkId");
            CREATE INDEX "IX_NovelProgress_ChapterId" ON "NovelProgress" ("ChapterId");

            CREATE TABLE "NovelAnimeMappings" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_NovelAnimeMappings" PRIMARY KEY,
                "WorkId" TEXT NOT NULL,
                "ChapterStart" INTEGER NOT NULL,
                "ChapterEnd" INTEGER NOT NULL,
                "AnimeProvider" TEXT NOT NULL,
                "AnimeExternalId" TEXT NOT NULL,
                "SeasonNumber" INTEGER NOT NULL,
                "EpisodeStart" INTEGER NOT NULL,
                "EpisodeEnd" INTEGER NOT NULL,
                "Label" TEXT NULL,
                "Source" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_NovelAnimeMappings_NovelWorks_WorkId"
                    FOREIGN KEY ("WorkId") REFERENCES "NovelWorks" ("Id") ON DELETE CASCADE
            );
            CREATE INDEX "IX_NovelAnimeMappings_WorkId_ChapterStart_ChapterEnd"
                ON "NovelAnimeMappings" ("WorkId", "ChapterStart", "ChapterEnd");
            CREATE INDEX "IX_NovelAnimeMappings_AnimeProvider_AnimeExternalId"
                ON "NovelAnimeMappings" ("AnimeProvider", "AnimeExternalId");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE "NovelAnimeMappings";
            DROP TABLE "NovelProgress";
            DROP TABLE "NovelTranslations";
            DROP TABLE "NovelChapters";
            DROP TABLE "NovelWorks";
            """);
    }
}
