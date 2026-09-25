using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260925173500_AddBookEditionsAndFiles")]
public sealed class AddBookEditionsAndFiles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "BookEditions" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_BookEditions" PRIMARY KEY,
                "WorkId" TEXT NOT NULL,
                "EditionKey" TEXT NOT NULL,
                "Language" TEXT NOT NULL,
                "Isbn10" TEXT NULL,
                "Isbn13" TEXT NULL,
                "Publisher" TEXT NULL,
                "PublishedDate" TEXT NULL,
                "Title" TEXT NULL,
                "Author" TEXT NULL,
                "SourceProvider" TEXT NULL,
                "SourceExternalId" TEXT NULL,
                "IsPrimary" INTEGER NOT NULL DEFAULT 1,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_BookEditions_NovelWorks_WorkId"
                    FOREIGN KEY ("WorkId")
                    REFERENCES "NovelWorks" ("Id")
                    ON DELETE CASCADE
            );

            CREATE UNIQUE INDEX "IX_BookEditions_WorkId_EditionKey"
                ON "BookEditions" ("WorkId", "EditionKey");
            CREATE INDEX "IX_BookEditions_WorkId_IsPrimary"
                ON "BookEditions" ("WorkId", "IsPrimary");
            CREATE INDEX "IX_BookEditions_Isbn13"
                ON "BookEditions" ("Isbn13");
            CREATE INDEX "IX_BookEditions_Isbn10"
                ON "BookEditions" ("Isbn10");

            CREATE TABLE "BookFiles" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_BookFiles" PRIMARY KEY,
                "EditionId" TEXT NOT NULL,
                "FileKey" TEXT NOT NULL,
                "FileName" TEXT NOT NULL,
                "Format" TEXT NOT NULL,
                "MediaType" TEXT NOT NULL,
                "SourceKind" TEXT NOT NULL,
                "SourceUrl" TEXT NULL,
                "ContentHash" TEXT NOT NULL,
                "SizeBytes" INTEGER NOT NULL,
                "StoragePath" TEXT NULL,
                "IsPrimary" INTEGER NOT NULL DEFAULT 1,
                "ImportedAt" TEXT NOT NULL,
                CONSTRAINT "FK_BookFiles_BookEditions_EditionId"
                    FOREIGN KEY ("EditionId")
                    REFERENCES "BookEditions" ("Id")
                    ON DELETE CASCADE
            );

            CREATE UNIQUE INDEX "IX_BookFiles_EditionId_FileKey"
                ON "BookFiles" ("EditionId", "FileKey");
            CREATE INDEX "IX_BookFiles_EditionId_IsPrimary"
                ON "BookFiles" ("EditionId", "IsPrimary");
            CREATE INDEX "IX_BookFiles_ContentHash"
                ON "BookFiles" ("ContentHash");

            INSERT INTO "BookEditions" (
                "Id",
                "WorkId",
                "EditionKey",
                "Language",
                "Title",
                "Author",
                "SourceProvider",
                "SourceExternalId",
                "IsPrimary",
                "CreatedAt",
                "UpdatedAt")
            SELECT
                "Id",
                "Id",
                'legacy-' || substr("SourceKey", 1, 100),
                CASE
                    WHEN "Format" LIKE 'EPUB:%' THEN substr("Format", 6)
                    ELSE 'und'
                END,
                COALESCE("MetadataTitle", "Title"),
                "Author",
                "MetadataProvider",
                "MetadataExternalId",
                1,
                "ImportedAt",
                "UpdatedAt"
            FROM "NovelWorks"
            WHERE "SourceProvider" = 'book-epub';

            INSERT INTO "BookFiles" (
                "Id",
                "EditionId",
                "FileKey",
                "FileName",
                "Format",
                "MediaType",
                "SourceKind",
                "SourceUrl",
                "ContentHash",
                "SizeBytes",
                "IsPrimary",
                "ImportedAt")
            SELECT
                "Id",
                "Id",
                'legacy-' || substr("SourceKey", 1, 100),
                'legacy.epub',
                'EPUB',
                'application/epub+zip',
                CASE
                    WHEN "SourceUrl" LIKE 'upload://%' THEN 'upload'
                    WHEN "MetadataProvider" = 'direct-epub' THEN 'remote'
                    WHEN "MetadataProvider" LIKE '%gutenberg%' THEN 'gutenberg'
                    ELSE 'legacy'
                END,
                "SourceUrl",
                '',
                0,
                1,
                "ImportedAt"
            FROM "NovelWorks"
            WHERE "SourceProvider" = 'book-epub';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE "BookFiles";
            DROP TABLE "BookEditions";
            """);
    }
}
