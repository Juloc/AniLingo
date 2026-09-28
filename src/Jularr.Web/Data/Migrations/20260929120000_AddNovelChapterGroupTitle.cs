using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations;

/// <summary>
/// Adds the optional chapter group/section label (#512): Light Novels group chapters under
/// headings such as "Extra", "Character Stories" or "Short Stories", read from an EPUB's
/// <c>nav</c>/<c>toc.ncx</c> nesting or a Narou chapter-index section heading. Existing chapters
/// stay ungrouped (rendered as today's flat list) until their volume is re-imported or refreshed.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260929120000_AddNovelChapterGroupTitle")]
public sealed class AddNovelChapterGroupTitle : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "NovelChapters" ADD COLUMN "GroupTitle" TEXT NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "NovelChapters" DROP COLUMN "GroupTitle";
            """);
    }
}
