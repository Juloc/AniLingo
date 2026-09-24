using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260924141000_AddLibraryRootWakeConfiguration")]
public sealed class AddLibraryRootWakeConfiguration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "LibraryRoots" ADD COLUMN "WakeOnLanEnabled" INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE "LibraryRoots" ADD COLUMN "WakeMacAddress" TEXT NULL;
            ALTER TABLE "LibraryRoots" ADD COLUMN "WakeBroadcastAddress" TEXT NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "LibraryRoots" DROP COLUMN "WakeBroadcastAddress";
            ALTER TABLE "LibraryRoots" DROP COLUMN "WakeMacAddress";
            ALTER TABLE "LibraryRoots" DROP COLUMN "WakeOnLanEnabled";
            """);
    }
}
