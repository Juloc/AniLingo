using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

/// <summary>
/// Introduces the series → volume → chapter reading model. Every existing
/// novel work (Narou/Ncode web novels and Books imports) receives exactly one
/// implicit volume and all of its chapters are moved into it, so the reader,
/// catalog, progress and annotation services have a single canonical shape.
/// NovelChapters is rebuilt with foreign keys disabled so that dropping the
/// old table cannot cascade into translations, progress, bookmarks or
/// highlights.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260926120000_AddNovelVolumes")]
public sealed class AddNovelVolumes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("PRAGMA foreign_keys = 0;", suppressTransaction: true);

        migrationBuilder.Sql("""
            CREATE TABLE "NovelVolumes" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_NovelVolumes" PRIMARY KEY,
                "WorkId" TEXT NOT NULL,
                "Number" INTEGER NOT NULL,
                "Title" TEXT NULL,
                "Kind" TEXT NOT NULL,
                "SourceKey" TEXT NOT NULL,
                "SourceFileName" TEXT NULL,
                "SourceContentHash" TEXT NULL,
                "CoverAsset" TEXT NULL,
                "ImportedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_NovelVolumes_NovelWorks_WorkId"
                    FOREIGN KEY ("WorkId") REFERENCES "NovelWorks" ("Id") ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX "IX_NovelVolumes_WorkId_Number"
                ON "NovelVolumes" ("WorkId", "Number");
            CREATE UNIQUE INDEX "IX_NovelVolumes_WorkId_SourceKey"
                ON "NovelVolumes" ("WorkId", "SourceKey");

            CREATE TEMP TABLE "NovelVolumeMigrationIds" AS
                SELECT "Id" AS "WorkId", hex(randomblob(16)) AS "Hex"
                FROM "NovelWorks";

            INSERT INTO "NovelVolumes" (
                "Id", "WorkId", "Number", "Title", "Kind", "SourceKey", "ImportedAt", "UpdatedAt")
            SELECT
                substr(ids."Hex", 1, 8) || '-' || substr(ids."Hex", 9, 4) || '-' ||
                    substr(ids."Hex", 13, 4) || '-' || substr(ids."Hex", 17, 4) || '-' ||
                    substr(ids."Hex", 21, 12),
                work."Id",
                1,
                NULL,
                CASE WHEN work."SourceProvider" = 'book-epub' THEN 'book' ELSE 'web' END,
                CASE WHEN work."SourceProvider" = 'book-epub' THEN 'book' ELSE 'web' END,
                work."ImportedAt",
                work."UpdatedAt"
            FROM "NovelWorks" AS work
            INNER JOIN "NovelVolumeMigrationIds" AS ids ON ids."WorkId" = work."Id";

            DROP TABLE "NovelVolumeMigrationIds";

            CREATE TABLE "ef_temp_NovelChapters" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_NovelChapters" PRIMARY KEY,
                "WorkId" TEXT NOT NULL,
                "VolumeId" TEXT NOT NULL,
                "Number" INTEGER NOT NULL,
                "SourceUrl" TEXT NOT NULL,
                "Title" TEXT NOT NULL,
                "OriginalText" TEXT NOT NULL,
                "ContentJson" TEXT NULL,
                "SourceHash" TEXT NOT NULL,
                "PublishedAt" TEXT NULL,
                "ImportedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_NovelChapters_NovelVolumes_VolumeId"
                    FOREIGN KEY ("VolumeId") REFERENCES "NovelVolumes" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_NovelChapters_NovelWorks_WorkId"
                    FOREIGN KEY ("WorkId") REFERENCES "NovelWorks" ("Id") ON DELETE CASCADE
            );

            INSERT INTO "ef_temp_NovelChapters" (
                "Id", "WorkId", "VolumeId", "Number", "SourceUrl", "Title", "OriginalText",
                "ContentJson", "SourceHash", "PublishedAt", "ImportedAt", "UpdatedAt")
            SELECT
                chapter."Id", chapter."WorkId", volume."Id", chapter."Number", chapter."SourceUrl",
                chapter."Title", chapter."OriginalText", NULL, chapter."SourceHash",
                chapter."PublishedAt", chapter."ImportedAt", chapter."UpdatedAt"
            FROM "NovelChapters" AS chapter
            INNER JOIN "NovelVolumes" AS volume ON volume."WorkId" = chapter."WorkId";

            DROP TABLE "NovelChapters";
            ALTER TABLE "ef_temp_NovelChapters" RENAME TO "NovelChapters";

            CREATE UNIQUE INDEX "IX_NovelChapters_WorkId_Number"
                ON "NovelChapters" ("WorkId", "Number");
            CREATE INDEX "IX_NovelChapters_VolumeId"
                ON "NovelChapters" ("VolumeId");
            """);

        migrationBuilder.Sql("PRAGMA foreign_keys = 1;", suppressTransaction: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException(
            "Novel chapters were moved into the series/volume model; this one-way migration cannot be reverted.");
    }
}
