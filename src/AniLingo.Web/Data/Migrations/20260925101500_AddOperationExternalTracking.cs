using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260925101500_AddOperationExternalTracking")]
public sealed class AddOperationExternalTracking : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "Operations"
                ADD COLUMN "ExternalProvider" TEXT NULL;

            ALTER TABLE "Operations"
                ADD COLUMN "ExternalId" TEXT NULL;

            CREATE INDEX "IX_Operations_ExternalProvider_ExternalId"
                ON "Operations" ("ExternalProvider", "ExternalId");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // SQLite cannot drop these columns safely without rebuilding the
        // Operations table. AniLingo's bounded upgrade policy treats this
        // migration as forward-only.
    }
}
