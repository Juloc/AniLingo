using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260926090000_AddLibraryScanLifecycle")]
public sealed class AddLibraryScanLifecycle : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "ReconciliationIntervalMinutes",
            table: "LibraryRoots",
            type: "INTEGER",
            nullable: false,
            defaultValue: 30);

        migrationBuilder.Sql("""
            ALTER TABLE "Operations"
                ADD COLUMN "Details" TEXT NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ReconciliationIntervalMinutes",
            table: "LibraryRoots");

        // SQLite cannot drop the Operations column safely without rebuilding
        // the table. AniLingo's bounded upgrade policy treats this migration
        // as forward-only.
    }
}
