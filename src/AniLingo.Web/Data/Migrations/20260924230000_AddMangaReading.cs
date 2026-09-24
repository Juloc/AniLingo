using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260924230000_AddMangaReading")]
public sealed class AddMangaReading : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "MangaSeries" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_MangaSeries" PRIMARY KEY,
                "Title" TEXT NOT NULL,
                "SourcePath" TEXT NOT NULL,
                "MetadataProvider" TEXT NULL,
                "MetadataExternalId" TEXT NULL,
                "MetadataTitle" TEXT NULL,
                "MetadataNativeTitle" TEXT NULL,
                "MetadataDescription" TEXT NULL,
                "CoverImageUrl" TEXT NULL,
                "BannerImageUrl" TEXT NULL,
                "MetadataStatus" TEXT NULL,
                "Direction" TEXT NOT NULL DEFAULT 'rtl',
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL
            );
            CREATE UNIQUE INDEX "IX_MangaSeries_SourcePath"
                ON "MangaSeries" ("SourcePath");
            CREATE UNIQUE INDEX "IX_MangaSeries_Metadata"
                ON "MangaSeries" ("MetadataProvider", "MetadataExternalId")
                WHERE "MetadataProvider" IS NOT NULL AND "MetadataExternalId" IS NOT NULL;

            CREATE TABLE "MangaChapters" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_MangaChapters" PRIMARY KEY,
                "SeriesId" TEXT NOT NULL,
                "Number" REAL NOT NULL,
                "VolumeNumber" INTEGER NULL,
                "Title" TEXT NOT NULL,
                "SourcePath" TEXT NOT NULL,
                "SourceKind" TEXT NOT NULL,
                "PageCount" INTEGER NOT NULL,
                "SourceUpdatedAt" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_MangaChapters_MangaSeries_SeriesId"
                    FOREIGN KEY ("SeriesId") REFERENCES "MangaSeries" ("Id") ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX "IX_MangaChapters_Series_Source"
                ON "MangaChapters" ("SeriesId", "SourcePath");
            CREATE INDEX "IX_MangaChapters_Series_Number"
                ON "MangaChapters" ("SeriesId", "Number");

            CREATE TABLE "MangaPages" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_MangaPages" PRIMARY KEY,
                "ChapterId" TEXT NOT NULL,
                "PageIndex" INTEGER NOT NULL,
                "CachedPath" TEXT NOT NULL,
                "MimeType" TEXT NOT NULL,
                "SourceEntry" TEXT NOT NULL,
                CONSTRAINT "FK_MangaPages_MangaChapters_ChapterId"
                    FOREIGN KEY ("ChapterId") REFERENCES "MangaChapters" ("Id") ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX "IX_MangaPages_Chapter_Page"
                ON "MangaPages" ("ChapterId", "PageIndex");

            CREATE TABLE "MangaProgress" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_MangaProgress" PRIMARY KEY,
                "ProfileId" TEXT NOT NULL,
                "SeriesId" TEXT NOT NULL,
                "ChapterId" TEXT NOT NULL,
                "PageIndex" INTEGER NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_MangaProgress_MangaSeries_SeriesId"
                    FOREIGN KEY ("SeriesId") REFERENCES "MangaSeries" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_MangaProgress_MangaChapters_ChapterId"
                    FOREIGN KEY ("ChapterId") REFERENCES "MangaChapters" ("Id") ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX "IX_MangaProgress_Profile_Series"
                ON "MangaProgress" ("ProfileId", "SeriesId");
            CREATE INDEX "IX_MangaProgress_Profile_Updated"
                ON "MangaProgress" ("ProfileId", "UpdatedAt");

            CREATE TABLE "MangaBookmarks" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_MangaBookmarks" PRIMARY KEY,
                "ProfileId" TEXT NOT NULL,
                "SeriesId" TEXT NOT NULL,
                "ChapterId" TEXT NOT NULL,
                "PageIndex" INTEGER NOT NULL,
                "Label" TEXT NULL,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_MangaBookmarks_MangaSeries_SeriesId"
                    FOREIGN KEY ("SeriesId") REFERENCES "MangaSeries" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_MangaBookmarks_MangaChapters_ChapterId"
                    FOREIGN KEY ("ChapterId") REFERENCES "MangaChapters" ("Id") ON DELETE CASCADE
            );
            CREATE INDEX "IX_MangaBookmarks_Profile_Series"
                ON "MangaBookmarks" ("ProfileId", "SeriesId", "CreatedAt");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE "MangaBookmarks";
            DROP TABLE "MangaProgress";
            DROP TABLE "MangaPages";
            DROP TABLE "MangaChapters";
            DROP TABLE "MangaSeries";
            """);
    }
}
