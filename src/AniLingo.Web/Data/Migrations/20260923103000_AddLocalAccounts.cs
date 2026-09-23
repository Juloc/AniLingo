using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260923103000_AddLocalAccounts")]
public sealed class AddLocalAccounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsEnabled",
            table: "OwnerAccounts",
            type: "INTEGER",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<int>(
            name: "Role",
            table: "OwnerAccounts",
            type: "INTEGER",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.Sql("""
            UPDATE OwnerAccounts
            SET Role = 1,
                IsEnabled = 1
            WHERE Id = 'owner';
            """);

        migrationBuilder.Sql("""
            DELETE FROM UserTerms
            WHERE ProfileId = 'default'
              AND EXISTS (
                SELECT 1
                FROM UserTerms AS existing
                WHERE existing.ProfileId = 'owner'
                  AND existing.TermId = UserTerms.TermId
              );

            UPDATE UserTerms
            SET ProfileId = 'owner'
            WHERE ProfileId = 'default';

            UPDATE Reviews
            SET ProfileId = 'owner'
            WHERE ProfileId = 'default';

            DELETE FROM LearningPreferences
            WHERE ProfileId = 'default'
              AND EXISTS (
                SELECT 1
                FROM LearningPreferences AS existing
                WHERE existing.ProfileId = 'owner'
              );

            UPDATE LearningPreferences
            SET ProfileId = 'owner'
            WHERE ProfileId = 'default';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "IsEnabled",
            table: "OwnerAccounts");

        migrationBuilder.DropColumn(
            name: "Role",
            table: "OwnerAccounts");
    }
}
