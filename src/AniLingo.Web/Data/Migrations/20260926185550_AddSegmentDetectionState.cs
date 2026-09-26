using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSegmentDetectionState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EpisodeSegmentDetectionStates",
                columns: table => new
                {
                    EpisodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Method = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    MediaIdentity = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RunAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SegmentsFound = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EpisodeSegmentDetectionStates", x => x.EpisodeId);
                    table.ForeignKey(
                        name: "FK_EpisodeSegmentDetectionStates_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EpisodeSegmentDetectionStates");
        }
    }
}
