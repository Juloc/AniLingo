using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations;

/// <summary>
/// Reader layout preferences for the Light Novel reader frame: hyphenation,
/// page numbers, illustrations, paragraph indent and automatic continue to the
/// next chapter. All nullable so every scope keeps inheriting field by field.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260928173000_AddReaderLayoutPreferences")]
public sealed class AddReaderLayoutPreferences : Migration
{
    private static readonly string[] Columns =
    [
        "Hyphenation",
        "ShowPageNumbers",
        "ShowIllustrations",
        "ParagraphIndent",
        "AutoContinueChapters"
    ];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var column in Columns)
        {
            migrationBuilder.AddColumn<bool>(
                name: column,
                table: "ReaderPreferences",
                type: "INTEGER",
                nullable: true);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var column in Columns)
        {
            migrationBuilder.DropColumn(
                name: column,
                table: "ReaderPreferences");
        }
    }
}
