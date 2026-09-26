using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260926090000_AddReaderTtsPreferences")]
public sealed class AddReaderTtsPreferences : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "ReaderPreferences" ADD COLUMN "TtsProviderId" TEXT NULL;
            ALTER TABLE "ReaderPreferences" ADD COLUMN "TtsVoiceIds" TEXT NULL;
            ALTER TABLE "ReaderPreferences" ADD COLUMN "TtsRate" REAL NULL;
            ALTER TABLE "ReaderPreferences" ADD COLUMN "TtsPitch" REAL NULL;
            ALTER TABLE "ReaderPreferences" ADD COLUMN "TtsVolume" REAL NULL;
            ALTER TABLE "ReaderPreferences" ADD COLUMN "TtsAutoContinueChapters" INTEGER NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "TtsAutoContinueChapters", table: "ReaderPreferences");
        migrationBuilder.DropColumn(name: "TtsVolume", table: "ReaderPreferences");
        migrationBuilder.DropColumn(name: "TtsPitch", table: "ReaderPreferences");
        migrationBuilder.DropColumn(name: "TtsRate", table: "ReaderPreferences");
        migrationBuilder.DropColumn(name: "TtsVoiceIds", table: "ReaderPreferences");
        migrationBuilder.DropColumn(name: "TtsProviderId", table: "ReaderPreferences");
    }
}
