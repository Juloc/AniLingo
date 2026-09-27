using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260928110000_AddWatchlistFranchise")]
public sealed class AddWatchlistFranchise : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Franchises",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                Title = table.Column<string>(type: "TEXT", nullable: false),
                SeedMediaType = table.Column<string>(type: "TEXT", nullable: false),
                SeedProvider = table.Column<string>(type: "TEXT", nullable: false),
                SeedExternalId = table.Column<string>(type: "TEXT", nullable: false),
                CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                LastRefreshedAtUtc = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Franchises", x => x.Id);
                table.UniqueConstraint(
                    "AK_Franchises_Seed",
                    x => new { x.SeedMediaType, x.SeedProvider, x.SeedExternalId });
            });

        migrationBuilder.CreateTable(
            name: "ProfileWatchlistPreferences",
            columns: table => new
            {
                ProfileId = table.Column<string>(type: "TEXT", nullable: false),
                MediaType = table.Column<string>(type: "TEXT", nullable: false),
                Provider = table.Column<string>(type: "TEXT", nullable: false),
                ExternalId = table.Column<string>(type: "TEXT", nullable: false),
                FollowState = table.Column<string>(type: "TEXT", nullable: false),
                Title = table.Column<string>(type: "TEXT", nullable: false),
                NativeTitle = table.Column<string>(type: "TEXT", nullable: true),
                CoverImageUrl = table.Column<string>(type: "TEXT", nullable: true),
                Format = table.Column<string>(type: "TEXT", nullable: true),
                Status = table.Column<string>(type: "TEXT", nullable: true),
                Year = table.Column<int>(type: "INTEGER", nullable: true),
                LocalMediaId = table.Column<string>(type: "TEXT", nullable: true),
                DetailsUrl = table.Column<string>(type: "TEXT", nullable: true),
                UpdatedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_ProfileWatchlistPreferences",
                    x => new { x.ProfileId, x.MediaType, x.Provider, x.ExternalId });
            });

        migrationBuilder.CreateTable(
            name: "FranchiseMembers",
            columns: table => new
            {
                FranchiseId = table.Column<string>(type: "TEXT", nullable: false),
                MediaType = table.Column<string>(type: "TEXT", nullable: false),
                Provider = table.Column<string>(type: "TEXT", nullable: false),
                ExternalId = table.Column<string>(type: "TEXT", nullable: false),
                Title = table.Column<string>(type: "TEXT", nullable: false),
                NativeTitle = table.Column<string>(type: "TEXT", nullable: true),
                CoverImageUrl = table.Column<string>(type: "TEXT", nullable: true),
                Format = table.Column<string>(type: "TEXT", nullable: true),
                Status = table.Column<string>(type: "TEXT", nullable: true),
                Year = table.Column<int>(type: "INTEGER", nullable: true),
                LocalMediaId = table.Column<string>(type: "TEXT", nullable: true),
                DetailsUrl = table.Column<string>(type: "TEXT", nullable: true),
                RelationType = table.Column<string>(type: "TEXT", nullable: true),
                IsSeed = table.Column<int>(type: "INTEGER", nullable: false),
                UpdatedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_FranchiseMembers",
                    x => new { x.FranchiseId, x.MediaType, x.Provider, x.ExternalId });
                table.ForeignKey(
                    name: "FK_FranchiseMembers_Franchises",
                    column: x => x.FranchiseId,
                    principalTable: "Franchises",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ProfileFranchiseFollows",
            columns: table => new
            {
                ProfileId = table.Column<string>(type: "TEXT", nullable: false),
                FranchiseId = table.Column<string>(type: "TEXT", nullable: false),
                FollowedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_ProfileFranchiseFollows",
                    x => new { x.ProfileId, x.FranchiseId });
                table.ForeignKey(
                    name: "FK_ProfileFranchiseFollows_Franchises",
                    column: x => x.FranchiseId,
                    principalTable: "Franchises",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_FranchiseMembers_ProviderIdentity",
            table: "FranchiseMembers",
            columns: new[] { "MediaType", "Provider", "ExternalId" });

        migrationBuilder.CreateIndex(
            name: "IX_ProfileFranchiseFollows_FranchiseId",
            table: "ProfileFranchiseFollows",
            column: "FranchiseId");

        migrationBuilder.CreateIndex(
            name: "IX_ProfileWatchlistPreferences_ProfileState",
            table: "ProfileWatchlistPreferences",
            columns: new[] { "ProfileId", "FollowState" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ProfileWatchlistPreferences");
        migrationBuilder.DropTable(name: "ProfileFranchiseFollows");
        migrationBuilder.DropTable(name: "FranchiseMembers");
        migrationBuilder.DropTable(name: "Franchises");
    }
}
