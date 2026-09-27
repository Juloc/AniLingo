using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations;

/// <summary>
/// Per-profile Sakura particle effect density (#387): off, subtle or full. Defaults to subtle so
/// existing profiles pick up the recommended default without an extra migration step.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260927120000_AddUiProfileSakuraMode")]
public sealed class AddUiProfileSakuraMode : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "UiProfileThemes" ADD COLUMN "SakuraMode" TEXT NOT NULL DEFAULT 'subtle'
                CHECK ("SakuraMode" IN ('off', 'subtle', 'full'));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "UiProfileThemes" DROP COLUMN "SakuraMode";
            """);
    }
}
