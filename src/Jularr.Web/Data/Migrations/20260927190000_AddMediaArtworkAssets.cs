using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations;

/// <summary>
/// Artwork files Jularr itself wrote beside the media (provider downloads, migrated copies).
/// A file without a row, or whose size/last write no longer match its row, is the user's own
/// artwork and is never replaced. The file name is relative to the owner's media folder so a
/// renamed series folder keeps its rows.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260927190000_AddMediaArtworkAssets")]
public sealed class AddMediaArtworkAssets : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "MediaArtworkAssets" (
                "Scope" TEXT NOT NULL,
                "OwnerId" TEXT NOT NULL,
                "SeasonNumber" INTEGER NOT NULL,
                "Kind" TEXT NOT NULL,
                "FileName" TEXT NOT NULL,
                "Source" TEXT NOT NULL,
                "SourceIdentity" TEXT NULL,
                "FileLength" INTEGER NOT NULL,
                "FileLastWriteTimeUtc" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "PK_MediaArtworkAssets" PRIMARY KEY ("Scope", "OwnerId", "SeasonNumber", "Kind")
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE "MediaArtworkAssets";
            """);
    }
}
