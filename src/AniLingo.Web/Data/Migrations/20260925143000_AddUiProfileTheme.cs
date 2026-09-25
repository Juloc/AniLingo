using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260925143000_AddUiProfileTheme")]
public sealed class AddUiProfileTheme : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "UiProfileLocales"
            ADD COLUMN "ThemeMode" TEXT NOT NULL DEFAULT 'system'
            CHECK ("ThemeMode" IN ('system', 'light', 'dark'));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "UiProfileLocales"
            DROP COLUMN "ThemeMode";
            """);
    }
}
