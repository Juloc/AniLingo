using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations;

/// <summary>
/// Incremental franchise refresh (#486): when each member's relations were last read and when a
/// full refresh was last asked for. Library ids and links that browsers once stored with follows
/// and franchise members are cleared; they are resolved from the library when shown. Every member
/// is read again once so provider data replaces what browsers stored.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260928150000_AddFranchiseRefreshState")]
public sealed class AddFranchiseRefreshState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "FranchiseMembers" ADD COLUMN "RelationsCheckedAtUtc" TEXT NULL;
            ALTER TABLE "Franchises" ADD COLUMN "RefreshRequestedAtUtc" TEXT NULL;
            UPDATE "FranchiseMembers" SET "LocalMediaId" = NULL, "DetailsUrl" = NULL;
            UPDATE "ProfileWatchlistPreferences" SET "LocalMediaId" = NULL, "DetailsUrl" = NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "Franchises" DROP COLUMN "RefreshRequestedAtUtc";
            ALTER TABLE "FranchiseMembers" DROP COLUMN "RelationsCheckedAtUtc";
            """);
    }
}
