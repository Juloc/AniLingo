using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations;

/// <summary>
/// PDF books were named after their PDF Info title, which is often a library label such as
/// "The Project Gutenberg eBook #33283: Calculus Made Easy" (#469). Imports now prefer the
/// requested catalog title and strip such labels; this applies the same rule once to PDF books
/// already in the library: a PDF linked to a Books catalog request takes that request's title,
/// an unlinked one loses the Project Gutenberg label. Data-only: the model is unchanged.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260928160000_CleanPdfBookTitles")]
public sealed class CleanPdfBookTitles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE "NovelWorks"
            SET "Title" = (
                    SELECT substr(trim(r."Title"), 1, 500) FROM "AcquisitionRequests" r
                    WHERE r."Kind" = 'book' AND r."Provider" = 'books-catalog'
                      AND r."ExternalId" = "NovelWorks"."MetadataExternalId"
                      AND trim(r."Title") <> ''
                    ORDER BY r."UpdatedAt" DESC LIMIT 1)
            WHERE "SourceProvider" = 'book-epub'
              AND "Format" LIKE 'PDF:%'
              AND "MetadataProvider" = 'books-catalog'
              AND EXISTS (
                    SELECT 1 FROM "AcquisitionRequests" r
                    WHERE r."Kind" = 'book' AND r."Provider" = 'books-catalog'
                      AND r."ExternalId" = "NovelWorks"."MetadataExternalId"
                      AND trim(r."Title") <> '');

            UPDATE "NovelWorks"
            SET "Title" = trim(substr("Title", instr("Title", ':') + 1))
            WHERE "SourceProvider" = 'book-epub'
              AND "Format" LIKE 'PDF:%'
              AND "Title" LIKE 'The Project Gutenberg eBook #%:_%';

            UPDATE "NovelWorks"
            SET "Title" = trim(substr("Title", 1, instr("Title", ', by ') - 1))
            WHERE "SourceProvider" = 'book-epub'
              AND "Format" LIKE 'PDF:%'
              AND "MetadataProvider" IS NOT 'books-catalog'
              AND instr("Title", ', by ') > 1
              AND "MetadataTitle" LIKE 'The Project Gutenberg eBook #%';

            UPDATE "NovelWorks"
            SET "MetadataTitle" = "Title"
            WHERE "SourceProvider" = 'book-epub'
              AND "Format" LIKE 'PDF:%'
              AND ("MetadataTitle" IS NULL OR "MetadataTitle" <> "Title");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The former titles were PDF file metadata, not user data; they are not restored.
    }
}
