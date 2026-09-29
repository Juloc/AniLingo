using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class MediaCoreFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Works",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaType = table.Column<int>(type: "integer", nullable: false),
                    CanonicalTitle = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Works", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkEditions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    EditionKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Language = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Format = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Publisher = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Isbn13 = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkEditions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkEditions_Works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "Works",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkExternalIdentities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaType = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    Evidence = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IsManualOverride = table.Column<bool>(type: "boolean", nullable: false),
                    ReviewState = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkExternalIdentities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkExternalIdentities_Works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "Works",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkFieldProvenance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Source = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ProviderExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Confidence = table.Column<double>(type: "double precision", nullable: true),
                    IsManualOverride = table.Column<bool>(type: "boolean", nullable: false),
                    FallbackPriority = table.Column<int>(type: "integer", nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkFieldProvenance", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkFieldProvenance_Works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "Works",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkRelations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FromWorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToWorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelationType = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    IsManualOverride = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkRelations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkRelations_Works_FromWorkId",
                        column: x => x.FromWorkId,
                        principalTable: "Works",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkRelations_Works_ToWorkId",
                        column: x => x.ToWorkId,
                        principalTable: "Works",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkSeasons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonNumber = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsSpecial = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkSeasons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkSeasons_Works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "Works",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkSourceLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKind = table.Column<int>(type: "integer", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkSourceLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkSourceLinks_Works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "Works",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkTitles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    TitleType = table.Column<int>(type: "integer", nullable: false),
                    Language = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Value = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    NormalizedValue = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    Source = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkTitles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkTitles_Works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "Works",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkVolumes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkVolumes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkVolumes_Works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "Works",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    EditionId = table.Column<Guid>(type: "uuid", nullable: true),
                    VersionKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UnitKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Quality = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ReleaseGroup = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Source = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkVersions_WorkEditions_EditionId",
                        column: x => x.EditionId,
                        principalTable: "WorkEditions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WorkVersions_Works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "Works",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkEpisodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonId = table.Column<Guid>(type: "uuid", nullable: true),
                    SeasonNumber = table.Column<int>(type: "integer", nullable: false),
                    EpisodeNumber = table.Column<int>(type: "integer", nullable: false),
                    AbsoluteNumber = table.Column<int>(type: "integer", nullable: true),
                    IsSpecial = table.Column<bool>(type: "boolean", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AiredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkEpisodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkEpisodes_WorkSeasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "WorkSeasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WorkEpisodes_Works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "Works",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkChapters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    VolumeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Number = table.Column<double>(type: "double precision", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsSpecial = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkChapters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkChapters_WorkVolumes_VolumeId",
                        column: x => x.VolumeId,
                        principalTable: "WorkVolumes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WorkChapters_Works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "Works",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkChapters_VolumeId",
                table: "WorkChapters",
                column: "VolumeId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkChapters_WorkId_Number",
                table: "WorkChapters",
                columns: new[] { "WorkId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkEditions_Isbn13",
                table: "WorkEditions",
                column: "Isbn13");

            migrationBuilder.CreateIndex(
                name: "IX_WorkEditions_WorkId_EditionKey",
                table: "WorkEditions",
                columns: new[] { "WorkId", "EditionKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkEditions_WorkId_IsPrimary",
                table: "WorkEditions",
                columns: new[] { "WorkId", "IsPrimary" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkEpisodes_SeasonId",
                table: "WorkEpisodes",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkEpisodes_WorkId_AbsoluteNumber",
                table: "WorkEpisodes",
                columns: new[] { "WorkId", "AbsoluteNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkEpisodes_WorkId_SeasonNumber_EpisodeNumber",
                table: "WorkEpisodes",
                columns: new[] { "WorkId", "SeasonNumber", "EpisodeNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkExternalIdentities_Provider_MediaType_ExternalId",
                table: "WorkExternalIdentities",
                columns: new[] { "Provider", "MediaType", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkExternalIdentities_WorkId",
                table: "WorkExternalIdentities",
                column: "WorkId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkFieldProvenance_WorkId_FieldKey",
                table: "WorkFieldProvenance",
                columns: new[] { "WorkId", "FieldKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkRelations_FromWorkId_ToWorkId_RelationType",
                table: "WorkRelations",
                columns: new[] { "FromWorkId", "ToWorkId", "RelationType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkRelations_ToWorkId",
                table: "WorkRelations",
                column: "ToWorkId");

            migrationBuilder.CreateIndex(
                name: "IX_Works_MediaType",
                table: "Works",
                column: "MediaType");

            migrationBuilder.CreateIndex(
                name: "IX_WorkSeasons_WorkId_SeasonNumber",
                table: "WorkSeasons",
                columns: new[] { "WorkId", "SeasonNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkSourceLinks_SourceKind_SourceId",
                table: "WorkSourceLinks",
                columns: new[] { "SourceKind", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkSourceLinks_WorkId",
                table: "WorkSourceLinks",
                column: "WorkId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTitles_NormalizedValue",
                table: "WorkTitles",
                column: "NormalizedValue");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTitles_WorkId_TitleType_Language_NormalizedValue",
                table: "WorkTitles",
                columns: new[] { "WorkId", "TitleType", "Language", "NormalizedValue" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkVersions_EditionId",
                table: "WorkVersions",
                column: "EditionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkVersions_WorkId_UnitKey",
                table: "WorkVersions",
                columns: new[] { "WorkId", "UnitKey" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkVersions_WorkId_VersionKey",
                table: "WorkVersions",
                columns: new[] { "WorkId", "VersionKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkVolumes_WorkId_Number",
                table: "WorkVolumes",
                columns: new[] { "WorkId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkChapters");

            migrationBuilder.DropTable(
                name: "WorkEpisodes");

            migrationBuilder.DropTable(
                name: "WorkExternalIdentities");

            migrationBuilder.DropTable(
                name: "WorkFieldProvenance");

            migrationBuilder.DropTable(
                name: "WorkRelations");

            migrationBuilder.DropTable(
                name: "WorkSourceLinks");

            migrationBuilder.DropTable(
                name: "WorkTitles");

            migrationBuilder.DropTable(
                name: "WorkVersions");

            migrationBuilder.DropTable(
                name: "WorkVolumes");

            migrationBuilder.DropTable(
                name: "WorkSeasons");

            migrationBuilder.DropTable(
                name: "WorkEditions");

            migrationBuilder.DropTable(
                name: "Works");
        }
    }
}
