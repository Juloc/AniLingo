using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260922184600_AddDailyNewWordQueue")]
public sealed class AddDailyNewWordQueue : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "LearningStartedAt",
            table: "UserTerms",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "QueuePosition",
            table: "UserTerms",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "NewWordsPerDay",
            table: "LearningPreferences",
            type: "INTEGER",
            nullable: false,
            defaultValue: 10);

        migrationBuilder.Sql("""
            UPDATE UserTerms
            SET LearningStartedAt = UpdatedAt
            WHERE State = 2
              AND NextReviewAt IS NOT NULL
              AND LearningStartedAt IS NULL;
            """);

        migrationBuilder.Sql("""
            UPDATE UserTerms
            SET QueuePosition = rowid
            WHERE State = 2
              AND NextReviewAt IS NULL
              AND QueuePosition IS NULL;
            """);

        migrationBuilder.CreateIndex(
            name: "IX_UserTerms_ProfileId_State_LearningStartedAt_QueuePosition",
            table: "UserTerms",
            columns: new[] { "ProfileId", "State", "LearningStartedAt", "QueuePosition" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_UserTerms_ProfileId_State_LearningStartedAt_QueuePosition",
            table: "UserTerms");

        migrationBuilder.DropColumn(
            name: "LearningStartedAt",
            table: "UserTerms");

        migrationBuilder.DropColumn(
            name: "QueuePosition",
            table: "UserTerms");

        migrationBuilder.DropColumn(
            name: "NewWordsPerDay",
            table: "LearningPreferences");
    }
}
