using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260924211600_AddAccountSessionState")]
public sealed class AddAccountSessionState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "AccountSessionStates" (
                "AccountId" TEXT NOT NULL CONSTRAINT "PK_AccountSessionStates" PRIMARY KEY,
                "Version" INTEGER NOT NULL DEFAULT 1,
                CONSTRAINT "FK_AccountSessionStates_OwnerAccounts_AccountId"
                    FOREIGN KEY ("AccountId") REFERENCES "OwnerAccounts" ("Id") ON DELETE CASCADE
            );

            INSERT INTO "AccountSessionStates" ("AccountId", "Version")
            SELECT "Id", 1
            FROM "OwnerAccounts";
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE "AccountSessionStates";
            """);
    }
}
