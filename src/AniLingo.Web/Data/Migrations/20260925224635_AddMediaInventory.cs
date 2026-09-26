using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MediaAnalyses",
                columns: table => new
                {
                    MediaFileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ProbeVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    SourceLastWriteTimeUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SourceFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Diagnostic = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    AnalyzedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Container = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    DurationSeconds = table.Column<double>(type: "REAL", nullable: true),
                    VideoCodec = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    VideoProfile = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    Width = table.Column<int>(type: "INTEGER", nullable: true),
                    Height = table.Column<int>(type: "INTEGER", nullable: true),
                    PixelFormat = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    BitDepth = table.Column<int>(type: "INTEGER", nullable: true),
                    DynamicRange = table.Column<string>(type: "TEXT", maxLength: 24, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaAnalyses", x => x.MediaFileId);
                    table.ForeignKey(
                        name: "FK_MediaAnalyses_MediaFiles_MediaFileId",
                        column: x => x.MediaFileId,
                        principalTable: "MediaFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaAnalysisStreams",
                columns: table => new
                {
                    MediaFileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StreamIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Codec = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Language = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Channels = table.Column<int>(type: "INTEGER", nullable: true),
                    ChannelLayout = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsForced = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaAnalysisStreams", x => new { x.MediaFileId, x.StreamIndex });
                    table.ForeignKey(
                        name: "FK_MediaAnalysisStreams_MediaAnalyses_MediaFileId",
                        column: x => x.MediaFileId,
                        principalTable: "MediaAnalyses",
                        principalColumn: "MediaFileId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaAnalyses_Status_ProbeVersion",
                table: "MediaAnalyses",
                columns: new[] { "Status", "ProbeVersion" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaAnalysisStreams");

            migrationBuilder.DropTable(
                name: "MediaAnalyses");
        }
    }
}
