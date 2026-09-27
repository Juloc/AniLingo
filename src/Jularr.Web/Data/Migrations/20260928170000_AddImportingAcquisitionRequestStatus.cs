using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations;

/// <summary>
/// Adds the persisted "importing" request state (#457): a completed download that is being
/// imported, or whose import hit a temporary infrastructure failure, stays open and restart-safe
/// instead of looking like "downloading". SQLite cannot alter a CHECK constraint, so the raw-SQL
/// request table is rebuilt with the extra value and "importing" joins the open-title unique
/// index. The table is not part of the EF model, so the model snapshot is unchanged.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260928170000_AddImportingAcquisitionRequestStatus")]
public sealed class AddImportingAcquisitionRequestStatus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(RebuildSql(
            "'pending', 'approved', 'searching', 'downloading', 'importing', 'completed', 'rejected', 'failed'",
            "'pending', 'approved', 'searching', 'downloading', 'importing'",
            statusSelect: "\"Status\""));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // An importing request becomes downloading again; the lifecycle re-dispatches it.
        migrationBuilder.Sql(RebuildSql(
            "'pending', 'approved', 'searching', 'downloading', 'completed', 'rejected', 'failed'",
            "'pending', 'approved', 'searching', 'downloading'",
            statusSelect: "CASE \"Status\" WHEN 'importing' THEN 'downloading' ELSE \"Status\" END"));
    }

    private static string RebuildSql(string allowedStatuses, string openStatuses, string statusSelect) => $"""
        CREATE TABLE "ef_temp_AcquisitionRequests" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_AcquisitionRequests" PRIMARY KEY,
            "Kind" TEXT NOT NULL CHECK ("Kind" IN ('anime', 'manga', 'lightNovel', 'book')),
            "Provider" TEXT NOT NULL,
            "ExternalId" TEXT NOT NULL,
            "Title" TEXT NOT NULL,
            "Subtitle" TEXT NULL,
            "CoverImageUrl" TEXT NULL,
            "PayloadJson" TEXT NULL,
            "RequestedByProfileId" TEXT NOT NULL,
            "Status" TEXT NOT NULL CHECK ("Status" IN ({allowedStatuses})),
            "StatusMessage" TEXT NULL,
            "OperationId" TEXT NULL,
            "ResultUrl" TEXT NULL,
            "CreatedAt" TEXT NOT NULL,
            "UpdatedAt" TEXT NOT NULL,
            "DecidedByProfileId" TEXT NULL,
            "DecidedAt" TEXT NULL
        );

        INSERT INTO "ef_temp_AcquisitionRequests" (
            "Id", "Kind", "Provider", "ExternalId", "Title", "Subtitle", "CoverImageUrl", "PayloadJson",
            "RequestedByProfileId", "Status", "StatusMessage", "OperationId", "ResultUrl",
            "CreatedAt", "UpdatedAt", "DecidedByProfileId", "DecidedAt")
        SELECT
            "Id", "Kind", "Provider", "ExternalId", "Title", "Subtitle", "CoverImageUrl", "PayloadJson",
            "RequestedByProfileId", {statusSelect}, "StatusMessage", "OperationId", "ResultUrl",
            "CreatedAt", "UpdatedAt", "DecidedByProfileId", "DecidedAt"
        FROM "AcquisitionRequests";

        DROP TABLE "AcquisitionRequests";
        ALTER TABLE "ef_temp_AcquisitionRequests" RENAME TO "AcquisitionRequests";

        CREATE INDEX "IX_AcquisitionRequests_Status_UpdatedAt" ON "AcquisitionRequests" ("Status", "UpdatedAt");
        CREATE INDEX "IX_AcquisitionRequests_RequestedByProfileId" ON "AcquisitionRequests" ("RequestedByProfileId");
        CREATE UNIQUE INDEX "IX_AcquisitionRequests_OpenTitle" ON "AcquisitionRequests" ("Kind", "Provider", "ExternalId")
            WHERE "Status" IN ({openStatuses});
        """;
}
