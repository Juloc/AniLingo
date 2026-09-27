using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations;

/// <summary>
/// Stores the provider average score (AniList <c>averageScore</c>, 0-100) on the matched
/// AnimeMetadata row so the Library media card can show a rating without a provider call (#395).
/// Existing rows stay null until their metadata is matched or refreshed again.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260927210000_AddAnimeMetadataAverageScore")]
public sealed class AddAnimeMetadataAverageScore : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "AnimeMetadata" ADD COLUMN "AverageScore" INTEGER NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "AnimeMetadata" DROP COLUMN "AverageScore";
            """);
    }
}
