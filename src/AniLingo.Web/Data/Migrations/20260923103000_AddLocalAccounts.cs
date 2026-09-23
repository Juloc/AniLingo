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
