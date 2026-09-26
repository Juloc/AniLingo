using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260926131000_AddPlaybackPreferenceDetails")]
public sealed class AddPlaybackPreferenceDetails : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PreferredAudioLanguage",
            table: "ProfilePlaybackPreferences",
            type: "TEXT",
            maxLength: 16,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PreferredSubtitleLanguage",
            table: "ProfilePlaybackPreferences",
            type: "TEXT",
            maxLength: 16,
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "DefaultPlaybackSpeed",
            table: "ProfilePlaybackPreferences",
            type: "REAL",
            nullable: false,
            defaultValue: 1.0);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "PreferredAudioLanguage",
            table: "ProfilePlaybackPreferences");

        migrationBuilder.DropColumn(
            name: "PreferredSubtitleLanguage",
            table: "ProfilePlaybackPreferences");

        migrationBuilder.DropColumn(
            name: "DefaultPlaybackSpeed",
            table: "ProfilePlaybackPreferences");
    }
}
