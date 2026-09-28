using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations;

/// <summary>
/// Known clients/devices registry (#527, part of epic #510): one row per (account, client kind,
/// client label) touched from <see cref="Features.Playback.Decision.PlaybackPlanService"/> every
/// time a client opens a playback session. Raw SQL only, like "AccountSessionStates" before it
/// (20260924211600_AddAccountSessionState): this table is intentionally outside the EF model, so
/// it never appears in a model snapshot diff. Timestamped 140000 (not 130000) because #524's
/// "AddPresentationGroups" already claimed 20260929130000 by the time this merged.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260929140000_AddKnownDevices")]
public sealed class AddKnownDevices : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "KnownDevices" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_KnownDevices" PRIMARY KEY,
                "ProfileId" TEXT NOT NULL,
                "ClientKind" TEXT NOT NULL,
                "Label" TEXT NULL,
                "AppVersion" TEXT NULL,
                "UserAgent" TEXT NULL,
                "FirstSeenUtc" TEXT NOT NULL,
                "LastSeenUtc" TEXT NOT NULL,
                CONSTRAINT "FK_KnownDevices_OwnerAccounts_ProfileId"
                    FOREIGN KEY ("ProfileId") REFERENCES "OwnerAccounts" ("Id") ON DELETE CASCADE
            );

            CREATE INDEX "IX_KnownDevices_ProfileId" ON "KnownDevices" ("ProfileId");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE "KnownDevices";
            """);
    }
}
