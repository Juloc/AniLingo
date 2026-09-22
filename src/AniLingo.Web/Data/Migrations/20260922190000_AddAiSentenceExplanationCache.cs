using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260922190000_AddAiSentenceExplanationCache")]
public sealed class AddAiSentenceExplanationCache : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AiSentenceExplanationCache",
            columns: table => new
            {
                CacheKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                ProviderId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                PromptVersion = table.Column<int>(type: "INTEGER", nullable: false),
                Translation = table.Column<string>(type: "TEXT", nullable: false),
                GrammarJson = table.Column<string>(type: "TEXT", nullable: false),
                ColloquialJson = table.Column<string>(type: "TEXT", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AiSentenceExplanationCache", x => x.CacheKey);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "AiSentenceExplanationCache");
}
