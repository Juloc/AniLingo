using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations;

/// <summary>
/// Manga durable series/chapter identity (#563). Chapters and series already lived in
/// <c>MangaChapters</c>/<c>MangaSeries</c> (added by AddMangaReading), but their row ids were
/// deterministic hashes of the source file path, so a rename/reorganize produced a brand-new id
/// instead of updating the existing row - orphaning <c>MangaProgress</c>/<c>MangaBookmarks</c> and
/// duplicating the chapter on the next scan. <c>Features/Manga/MangaImportService</c> and
/// <c>MangaRepository</c> now assign a stable id once and match an existing chapter on rescan by
/// (series, chapter/volume number) as a fallback when the source path changed, updating the stored
/// path in place; the id (and everything keyed on it) survives.
///
/// This migration only adds <c>MangaVolumes</c> - its own durable id/title per (series, volume
/// number), independent of any one chapter file - and the nullable <c>MangaChapters.VolumeId</c>
/// link. Both are accessed only through <c>MangaRepository</c>'s raw ADO.NET, never mapped as EF
/// entities, so the EF model snapshot is unchanged and
/// <c>dotnet ef migrations has-pending-model-changes</c> stays clean.
///
/// Already-imported Manga is unaffected until its next scan (Series page "Refresh" or a new
/// completed download for that series), which populates <c>MangaVolumes</c> and backfills
/// <c>VolumeId</c> for its chapters; no migration-time backfill runs against existing rows.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260929260000_AddMangaVolumes")]
public sealed class AddMangaVolumes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "MangaVolumes" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_MangaVolumes" PRIMARY KEY,
                "SeriesId" TEXT NOT NULL,
                "Number" INTEGER NOT NULL,
                "Title" TEXT NULL,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_MangaVolumes_MangaSeries_SeriesId"
                    FOREIGN KEY ("SeriesId") REFERENCES "MangaSeries" ("Id") ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX "IX_MangaVolumes_Series_Number"
                ON "MangaVolumes" ("SeriesId", "Number");

            ALTER TABLE "MangaChapters" ADD COLUMN "VolumeId" TEXT NULL
                CONSTRAINT "FK_MangaChapters_MangaVolumes_VolumeId"
                    REFERENCES "MangaVolumes" ("Id") ON DELETE SET NULL;
            CREATE INDEX "IX_MangaChapters_VolumeId"
                ON "MangaChapters" ("VolumeId");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX "IX_MangaChapters_VolumeId";
            ALTER TABLE "MangaChapters" DROP COLUMN "VolumeId";
            DROP TABLE "MangaVolumes";
            """);
    }
}
