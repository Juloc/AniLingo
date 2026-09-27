using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations;

/// <summary>
/// TranslateGemma chapter text was stored with TargetLanguage "de", the same value as the AI
/// translation, so offline export, search and the translated counters mixed both tracks (#487).
/// It now has its own track language "de-gemma". Only rows written by the TranslateGemma provider
/// (ProviderId prefix "translategemma:") move; the unique index includes ProviderId, so no row can
/// collide. Data-only: the model is unchanged.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260928140000_SeparateTranslateGemmaTrack")]
public sealed class SeparateTranslateGemmaTrack : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE "NovelTranslations"
            SET "TargetLanguage" = 'de-gemma'
            WHERE "TargetLanguage" = 'de'
              AND substr("ProviderId", 1, 15) = 'translategemma:';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE "NovelTranslations"
            SET "TargetLanguage" = 'de'
            WHERE "TargetLanguage" = 'de-gemma'
              AND substr("ProviderId", 1, 15) = 'translategemma:';
            """);
    }
}
