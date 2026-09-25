using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260925010000_AddReaderThemeAdjustments")]
public sealed class AddReaderThemeAdjustments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "ReaderPreferences" ADD COLUMN "BackgroundMotionMode" TEXT NULL;
            ALTER TABLE "ReaderPreferences" ADD COLUMN "ThemeEffectStrength" REAL NULL;
            ALTER TABLE "ReaderPreferences" ADD COLUMN "ThemeBrightness" REAL NULL;
            ALTER TABLE "ReaderPreferences" ADD COLUMN "ThemeContrast" REAL NULL;
            ALTER TABLE "ReaderPreferences" ADD COLUMN "ThemeSaturation" REAL NULL;
            ALTER TABLE "ReaderPreferences" ADD COLUMN "ThemeVignetteStrength" REAL NULL;
            ALTER TABLE "ReaderPreferences" ADD COLUMN "ThemeGrainStrength" REAL NULL;
            ALTER TABLE "ReaderPreferences" ADD COLUMN "ThemeTextBackdropStrength" REAL NULL;
            ALTER TABLE "ReaderPreferences" ADD COLUMN "ThemeParallaxStrength" REAL NULL;
            ALTER TABLE "ReaderPreferences" ADD COLUMN "ThemeTintStrength" REAL NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "BackgroundMotionMode", table: "ReaderPreferences");
        migrationBuilder.DropColumn(name: "ThemeEffectStrength", table: "ReaderPreferences");
        migrationBuilder.DropColumn(name: "ThemeBrightness", table: "ReaderPreferences");
        migrationBuilder.DropColumn(name: "ThemeContrast", table: "ReaderPreferences");
        migrationBuilder.DropColumn(name: "ThemeSaturation", table: "ReaderPreferences");
        migrationBuilder.DropColumn(name: "ThemeVignetteStrength", table: "ReaderPreferences");
        migrationBuilder.DropColumn(name: "ThemeGrainStrength", table: "ReaderPreferences");
        migrationBuilder.DropColumn(name: "ThemeTextBackdropStrength", table: "ReaderPreferences");
        migrationBuilder.DropColumn(name: "ThemeParallaxStrength", table: "ReaderPreferences");
        migrationBuilder.DropColumn(name: "ThemeTintStrength", table: "ReaderPreferences");
    }
}
