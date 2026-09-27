using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations;

/// <summary>
/// Books, Manga and Light Novel requests now share one release-request state
/// (<c>ReleaseRequestPayload</c>: triedReleases, searches, nextSearchUtc, lastProblem). Books
/// already stored the tried releases as <c>triedReleases</c>; Manga and Light Novel requests
/// stored them as <c>triedReleaseIds</c>. This renames that key once so every payload has the
/// same shape and nothing reads the old key again. Data-only: the model is unchanged.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260929100000_UnifyReleaseRequestPayload")]
public sealed class UnifyReleaseRequestPayload : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE "AcquisitionRequests"
            SET "PayloadJson" = json_remove(
                    json_set("PayloadJson", '$.triedReleases', json(json_extract("PayloadJson", '$.triedReleaseIds'))),
                    '$.triedReleaseIds')
            WHERE "Kind" IN ('manga', 'lightNovel')
              AND "PayloadJson" IS NOT NULL
              AND json_valid("PayloadJson")
              AND json_type("PayloadJson", '$.triedReleaseIds') IS NOT NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE "AcquisitionRequests"
            SET "PayloadJson" = json_remove(
                    json_set("PayloadJson", '$.triedReleaseIds', json(json_extract("PayloadJson", '$.triedReleases'))),
                    '$.triedReleases')
            WHERE "Kind" IN ('manga', 'lightNovel')
              AND "PayloadJson" IS NOT NULL
              AND json_valid("PayloadJson")
              AND json_type("PayloadJson", '$.triedReleases') IS NOT NULL;
            """);
    }
}
