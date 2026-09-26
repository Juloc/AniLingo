using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

/// <summary>
/// Adds the optional, off-by-default furigana reader preference
/// (issue #147): computed readings over kanji, gated by the toolkit's
/// readings capability and the existing ReaderPreferences cascade.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260926145200_AddNovelReaderFuriganaPreference")]
public sealed class AddNovelReaderFuriganaPreference : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "FuriganaEnabled",
            table: "ReaderPreferences",
            type: "INTEGER",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "FuriganaEnabled",
            table: "ReaderPreferences");
    }
}
