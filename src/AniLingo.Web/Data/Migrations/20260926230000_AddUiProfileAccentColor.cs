using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

/// <summary>
/// Per-profile accent colour for the Jularr theme engine. NULL means the brand default; only the
/// seed is stored, every derived colour is computed at render time.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260926230000_AddUiProfileAccentColor")]
public sealed class AddUiProfileAccentColor : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "UiProfileThemes" ADD COLUMN "AccentColor" TEXT NULL
                CHECK ("AccentColor" IS NULL OR "AccentColor" GLOB '#[0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f]');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "UiProfileThemes" DROP COLUMN "AccentColor";
            """);
    }
}
