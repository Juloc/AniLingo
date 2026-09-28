using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations;

/// <summary>
/// The unified event and notification boundary (#429).
///
/// <c>Events</c> is the durable, append-only audit log of every <c>JularrEvent</c> published
/// through <c>IJularrEventPublisher</c> (download grabbed/failed, import completed/failed, release
/// available, request approved/denied, storage problem).
///
/// <c>NotificationSubscriptions</c> is each profile's chosen <c>NotificationMode</c> per event
/// category; a missing row defaults to in-app (opt-out, not opt-in).
///
/// <c>Notifications</c> is the per-profile in-app inbox: one durable row per delivered event.
/// <c>IX_Notifications_ProfileId_DedupKey</c> lets the store find an existing row for the same
/// (profile, dedup key) so a replayed/duplicate event bumps <c>OccurrenceCount</c> instead of
/// spamming a second row.
///
/// All three tables are accessed only through raw-ADO.NET stores (<c>EventLogStore</c>,
/// <c>NotificationSubscriptionStore</c>, <c>NotificationStore</c>), never mapped as EF entities, so
/// the EF model snapshot is unchanged and <c>dotnet ef migrations has-pending-model-changes</c>
/// stays clean.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260929240000_AddEventsAndNotifications")]
public sealed class AddEventsAndNotifications : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "Events" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Events" PRIMARY KEY,
                "Category" INTEGER NOT NULL,
                "Audience" INTEGER NOT NULL,
                "ProfileId" TEXT NULL,
                "MediaType" TEXT NULL,
                "SubjectId" TEXT NULL,
                "MessageParamsJson" TEXT NULL,
                "Severity" INTEGER NOT NULL,
                "DeepLink" TEXT NULL,
                "DedupKey" TEXT NULL,
                "RelatedOperationId" TEXT NULL,
                "CreatedAtUtc" TEXT NOT NULL
            );

            CREATE INDEX "IX_Events_CreatedAtUtc" ON "Events" ("CreatedAtUtc");

            CREATE TABLE "NotificationSubscriptions" (
                "ProfileId" TEXT NOT NULL,
                "Category" INTEGER NOT NULL,
                "Mode" INTEGER NOT NULL,
                "UpdatedAtUtc" TEXT NOT NULL,
                CONSTRAINT "PK_NotificationSubscriptions" PRIMARY KEY ("ProfileId", "Category")
            );

            CREATE TABLE "Notifications" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Notifications" PRIMARY KEY,
                "ProfileId" TEXT NOT NULL,
                "EventId" TEXT NOT NULL,
                "Category" INTEGER NOT NULL,
                "Severity" INTEGER NOT NULL,
                "MediaType" TEXT NULL,
                "SubjectId" TEXT NULL,
                "MessageParamsJson" TEXT NULL,
                "DeepLink" TEXT NULL,
                "DedupKey" TEXT NULL,
                "OccurrenceCount" INTEGER NOT NULL DEFAULT 1,
                "CreatedAtUtc" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NOT NULL,
                "ReadAtUtc" TEXT NULL
            );

            CREATE INDEX "IX_Notifications_ProfileId_UpdatedAtUtc" ON "Notifications" ("ProfileId", "UpdatedAtUtc");
            CREATE UNIQUE INDEX "IX_Notifications_ProfileId_DedupKey" ON "Notifications" ("ProfileId", "DedupKey") WHERE "DedupKey" IS NOT NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE "Notifications";
            DROP TABLE "NotificationSubscriptions";
            DROP TABLE "Events";
            """);
    }
}
