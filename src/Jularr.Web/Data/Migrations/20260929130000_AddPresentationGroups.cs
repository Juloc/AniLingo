using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations;

/// <summary>
/// User-presentation grouping layer (#524, epic #510): a fourth media-model layer that lets the UI
/// show a work's episodes/chapters/volumes under named groups (Season, Part, Cour, Story Arc,
/// Specials, or arbitrary reality-show person/week/round groups) WITHOUT touching files, the
/// internal identity or provider coordinates. A group is a free-text <c>Name</c> plus an ordered
/// list of inclusive internal-unit ranges (episode <c>Number</c> / chapter / volume number); it is
/// derived state over the stable internal identity, never a rename of it.
///
/// Media-type-agnostic by design: rows are keyed by (<c>MediaType</c>, <c>WorkId</c>) so the same
/// mechanism serves anime episodes today and reading (manga/novel) volumes/chapters later. These
/// tables are accessed only through <c>PresentationGroupStore</c> (raw ADO.NET), never mapped as EF
/// entities, so the EF model snapshot is unchanged and
/// <c>dotnet ef migrations has-pending-model-changes</c> stays clean.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260929130000_AddPresentationGroups")]
public sealed class AddPresentationGroups : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "PresentationGroups" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_PresentationGroups" PRIMARY KEY,
                "MediaType" TEXT NOT NULL CHECK ("MediaType" IN ('anime', 'novel', 'manga', 'book')),
                "WorkId" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "SortOrder" INTEGER NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL
            );

            CREATE INDEX "IX_PresentationGroups_Work"
                ON "PresentationGroups" ("MediaType", "WorkId", "SortOrder");

            CREATE TABLE "PresentationGroupRanges" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_PresentationGroupRanges" PRIMARY KEY,
                "GroupId" TEXT NOT NULL,
                "StartUnit" INTEGER NOT NULL,
                "EndUnit" INTEGER NOT NULL,
                "SortOrder" INTEGER NOT NULL,
                CONSTRAINT "FK_PresentationGroupRanges_Group" FOREIGN KEY ("GroupId")
                    REFERENCES "PresentationGroups" ("Id") ON DELETE CASCADE
            );

            CREATE INDEX "IX_PresentationGroupRanges_Group"
                ON "PresentationGroupRanges" ("GroupId", "SortOrder");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE "PresentationGroupRanges";
            DROP TABLE "PresentationGroups";
            """);
    }
}
