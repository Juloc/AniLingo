using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations;

/// <summary>
/// Owner-configured add/request rules per media type and the one request table shared by
/// anime, manga, light novels and books. Missing policy rows mean the default
/// (users request, manual add is owner-only).
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260927120000_AddAcquisitionAccessAndRequests")]
public sealed class AddAcquisitionAccessAndRequests : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "AcquisitionAccessPolicies" (
                "Kind" TEXT NOT NULL CONSTRAINT "PK_AcquisitionAccessPolicies" PRIMARY KEY
                    CHECK ("Kind" IN ('anime', 'manga', 'lightNovel', 'book')),
                "UserAddMode" TEXT NOT NULL CHECK ("UserAddMode" IN ('disabled', 'request', 'automatic')),
                "ManualAddMode" TEXT NOT NULL CHECK ("ManualAddMode" IN ('ownerOnly', 'users')),
                "UpdatedAt" TEXT NOT NULL
            );

            CREATE TABLE "AcquisitionRequests" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_AcquisitionRequests" PRIMARY KEY,
                "Kind" TEXT NOT NULL CHECK ("Kind" IN ('anime', 'manga', 'lightNovel', 'book')),
                "Provider" TEXT NOT NULL,
                "ExternalId" TEXT NOT NULL,
                "Title" TEXT NOT NULL,
                "Subtitle" TEXT NULL,
                "CoverImageUrl" TEXT NULL,
                "PayloadJson" TEXT NULL,
                "RequestedByProfileId" TEXT NOT NULL,
                "Status" TEXT NOT NULL CHECK ("Status" IN ('pending', 'approved', 'searching', 'downloading', 'completed', 'rejected', 'failed')),
                "StatusMessage" TEXT NULL,
                "OperationId" TEXT NULL,
                "ResultUrl" TEXT NULL,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                "DecidedByProfileId" TEXT NULL,
                "DecidedAt" TEXT NULL
            );

            CREATE INDEX "IX_AcquisitionRequests_Status_UpdatedAt" ON "AcquisitionRequests" ("Status", "UpdatedAt");
            CREATE INDEX "IX_AcquisitionRequests_RequestedByProfileId" ON "AcquisitionRequests" ("RequestedByProfileId");
            CREATE UNIQUE INDEX "IX_AcquisitionRequests_OpenTitle" ON "AcquisitionRequests" ("Kind", "Provider", "ExternalId")
                WHERE "Status" IN ('pending', 'approved', 'searching', 'downloading');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE "AcquisitionRequests";
            DROP TABLE "AcquisitionAccessPolicies";
            """);
    }
}
