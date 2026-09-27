using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations;

/// <summary>
/// Cache of provider release data for the release calendar (Features/Calendar). Sources are the
/// provider entries that were fetched (with their provider status and last refresh), entries the
/// normalized dated releases of one source. Canonical media stays in the library tables: entries
/// only carry the provider id, so the calendar links them to anime, manga and novels at read time.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260927180000_AddReleaseCalendarCache")]
public sealed class AddReleaseCalendarCache : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "ReleaseCalendarSources" (
                "Provider" TEXT NOT NULL,
                "ExternalId" TEXT NOT NULL,
                "ProviderStatus" TEXT NULL,
                "RefreshedAt" TEXT NULL,
                "LastAttemptAt" TEXT NULL,
                "LastError" TEXT NULL,
                CONSTRAINT "PK_ReleaseCalendarSources" PRIMARY KEY ("Provider", "ExternalId")
            );

            CREATE TABLE "ReleaseCalendarEntries" (
                "Provider" TEXT NOT NULL,
                "ExternalId" TEXT NOT NULL,
                "Kind" TEXT NOT NULL CHECK ("Kind" IN (
                    'episode', 'seasonPremiere', 'seriesStart', 'chapter', 'volume', 'publication',
                    'cinema', 'digital', 'streaming', 'physical')),
                "UnitNumber" INTEGER NOT NULL DEFAULT 0,
                "DateValue" TEXT NOT NULL,
                "Precision" TEXT NOT NULL CHECK ("Precision" IN ('unknown', 'year', 'quarter', 'month', 'day', 'dateTime')),
                "RangeStart" TEXT NULL,
                "RangeEnd" TEXT NULL,
                "FetchedAt" TEXT NOT NULL,
                CONSTRAINT "PK_ReleaseCalendarEntries" PRIMARY KEY ("Provider", "ExternalId", "Kind", "UnitNumber"),
                CONSTRAINT "FK_ReleaseCalendarEntries_Sources" FOREIGN KEY ("Provider", "ExternalId")
                    REFERENCES "ReleaseCalendarSources" ("Provider", "ExternalId") ON DELETE CASCADE
            );

            CREATE INDEX "IX_ReleaseCalendarEntries_Range" ON "ReleaseCalendarEntries" ("RangeStart", "RangeEnd");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE "ReleaseCalendarEntries";
            DROP TABLE "ReleaseCalendarSources";
            """);
    }
}
