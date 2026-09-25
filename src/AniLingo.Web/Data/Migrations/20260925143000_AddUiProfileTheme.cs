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
            CREATE TABLE "UiProfileThemes" (
                "ProfileId" TEXT NOT NULL CONSTRAINT "PK_UiProfileThemes" PRIMARY KEY,
                "ThemeMode" TEXT NOT NULL DEFAULT 'system'
                    CHECK ("ThemeMode" IN ('system', 'light', 'dark')),
                "UpdatedAt" TEXT NOT NULL
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE "UiProfileThemes";
            """);
    }
}
