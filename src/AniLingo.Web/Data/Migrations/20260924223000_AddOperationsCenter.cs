using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260924223000_AddOperationsCenter")]
public sealed class AddOperationsCenter : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "Operations" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Operations" PRIMARY KEY,
                "Kind" TEXT NOT NULL,
                "Category" TEXT NOT NULL,
                "Lane" INTEGER NOT NULL,
                "Status" INTEGER NOT NULL,
                "ProfileId" TEXT NULL,
                "Title" TEXT NOT NULL,
                "Subject" TEXT NULL,
                "ProgressPercent" INTEGER NULL,
                "Message" TEXT NULL,
                "Error" TEXT NULL,
                "IsDownload" INTEGER NOT NULL DEFAULT 0,
                "BytesTotal" INTEGER NULL,
                "BytesCompleted" INTEGER NULL,
                "BytesPerSecond" REAL NULL,
                "EtaUtc" TEXT NULL,
                "Attempt" INTEGER NOT NULL DEFAULT 1,
                "Retryable" INTEGER NOT NULL DEFAULT 1,
                "CreatedAtUtc" TEXT NOT NULL,
                "StartedAtUtc" TEXT NULL,
                "FinishedAtUtc" TEXT NULL,
                "UpdatedAtUtc" TEXT NOT NULL
            );

            CREATE INDEX "IX_Operations_Status_UpdatedAtUtc"
                ON "Operations" ("Status", "UpdatedAtUtc");

            CREATE INDEX "IX_Operations_Lane_Status"
                ON "Operations" ("Lane", "Status");

            CREATE INDEX "IX_Operations_ProfileId_UpdatedAtUtc"
                ON "Operations" ("ProfileId", "UpdatedAtUtc");

            CREATE INDEX "IX_Operations_IsDownload_Status"
                ON "Operations" ("IsDownload", "Status");

            CREATE TABLE "OperationLogs" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_OperationLogs" PRIMARY KEY AUTOINCREMENT,
                "OperationId" TEXT NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "Level" INTEGER NOT NULL,
                "Module" TEXT NOT NULL,
                "Message" TEXT NOT NULL,
                CONSTRAINT "FK_OperationLogs_Operations_OperationId"
                    FOREIGN KEY ("OperationId") REFERENCES "Operations" ("Id") ON DELETE CASCADE
            );

            CREATE INDEX "IX_OperationLogs_OperationId_Id"
                ON "OperationLogs" ("OperationId", "Id");

            CREATE INDEX "IX_OperationLogs_Level_Id"
                ON "OperationLogs" ("Level", "Id");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE "OperationLogs";
            DROP TABLE "Operations";
            """);
    }
}
