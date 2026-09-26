using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

/// <summary>
/// Makes Learning context anchors personal: a context records where one profile
/// met a unit, at most once per unit, source and position. No runtime path
/// wrote LearningContexts before this migration, so there are no rows to assign.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260926133000_ScopeLearningContextsToProfiles")]
public sealed class ScopeLearningContextsToProfiles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_LearningContexts_SourceType_SourceKey",
            table: "LearningContexts");

        migrationBuilder.AddColumn<string>(
            name: "ProfileId",
            table: "LearningContexts",
            type: "TEXT",
            maxLength: 80,
            nullable: false,
            defaultValue: "");

        migrationBuilder.CreateIndex(
            name: "IX_LearningContexts_ProfileId_SourceType_SourceKey",
            table: "LearningContexts",
            columns: ["ProfileId", "SourceType", "SourceKey"]);

        migrationBuilder.CreateIndex(
            name: "IX_LearningContexts_ProfileId_UnitId_SourceType_SourceKey_PositionKey",
            table: "LearningContexts",
            columns: ["ProfileId", "UnitId", "SourceType", "SourceKey", "PositionKey"],
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_LearningContexts_ProfileId_SourceType_SourceKey",
            table: "LearningContexts");

        migrationBuilder.DropIndex(
            name: "IX_LearningContexts_ProfileId_UnitId_SourceType_SourceKey_PositionKey",
            table: "LearningContexts");

        migrationBuilder.DropColumn(
            name: "ProfileId",
            table: "LearningContexts");

        migrationBuilder.CreateIndex(
            name: "IX_LearningContexts_SourceType_SourceKey",
            table: "LearningContexts",
            columns: ["SourceType", "SourceKey"]);
    }
}
