using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

/// <summary>
/// Canonical per-episode media segment markers (intro, recap, outro, preview,
/// credits) with source, method, version and confidence. Generated trickplay
/// assets live in the disposable /data cache and are not stored here.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260926120000_AddEpisodeMediaSegments")]
public sealed class AddEpisodeMediaSegments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "EpisodeMediaSegments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                EpisodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                Kind = table.Column<int>(type: "INTEGER", nullable: false),
                StartMs = table.Column<long>(type: "INTEGER", nullable: false),
                EndMs = table.Column<long>(type: "INTEGER", nullable: false),
                Source = table.Column<int>(type: "INTEGER", nullable: false),
                Method = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                Version = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Confidence = table.Column<double>(type: "REAL", nullable: false),
                MediaIdentity = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EpisodeMediaSegments", x => x.Id);
                table.ForeignKey(
                    name: "FK_EpisodeMediaSegments_Episodes_EpisodeId",
                    column: x => x.EpisodeId,
                    principalTable: "Episodes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_EpisodeMediaSegments_EpisodeId_Kind_Source",
            table: "EpisodeMediaSegments",
            columns: new[] { "EpisodeId", "Kind", "Source" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "EpisodeMediaSegments");
    }
}
