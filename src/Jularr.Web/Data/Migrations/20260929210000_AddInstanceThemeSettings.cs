using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260929210000_AddInstanceThemeSettings")]
public sealed class AddInstanceThemeSettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "UiProfileThemes" ADD COLUMN "ThemeId" TEXT NULL;

            CREATE TABLE "InstanceAppearanceSettings" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_InstanceAppearanceSettings" PRIMARY KEY CHECK ("Id" = 1),
                "DefaultThemeId" TEXT NOT NULL DEFAULT 'original',
                "AllowProfileThemeOverride" INTEGER NOT NULL DEFAULT 1,
                "AllowProfileAccentOverride" INTEGER NOT NULL DEFAULT 1,
                "UpdatedAt" TEXT NOT NULL
            );

            INSERT INTO "InstanceAppearanceSettings" (
                "Id", "DefaultThemeId", "AllowProfileThemeOverride", "AllowProfileAccentOverride", "UpdatedAt")
            VALUES (1, 'original', 1, 1, CURRENT_TIMESTAMP);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE "InstanceAppearanceSettings";
            ALTER TABLE "UiProfileThemes" DROP COLUMN "ThemeId";
            """);
    }
}
