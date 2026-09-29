using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class LearningV3CurriculumFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CurriculumBlueprints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ContentFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurriculumBlueprints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CurriculumLevels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BlueprintId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurriculumLevels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CurriculumLevels_CurriculumBlueprints_BlueprintId",
                        column: x => x.BlueprintId,
                        principalTable: "CurriculumBlueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SharedCourseInstances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BlueprintId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceLanguage = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    TargetLanguage = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    ContentFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharedCourseInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SharedCourseInstances_CurriculumBlueprints_BlueprintId",
                        column: x => x.BlueprintId,
                        principalTable: "CurriculumBlueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CurriculumChapters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LevelId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurriculumChapters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CurriculumChapters_CurriculumLevels_LevelId",
                        column: x => x.LevelId,
                        principalTable: "CurriculumLevels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LearnerCourses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SharedInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    LearningCourseId = table.Column<Guid>(type: "uuid", nullable: true),
                    DisplayNameOverride = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    EnrolledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerCourses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearnerCourses_LearningCourses_LearningCourseId",
                        column: x => x.LearningCourseId,
                        principalTable: "LearningCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LearnerCourses_SharedCourseInstances_SharedInstanceId",
                        column: x => x.SharedInstanceId,
                        principalTable: "SharedCourseInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CurriculumLessons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChapterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurriculumLessons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CurriculumLessons_CurriculumChapters_ChapterId",
                        column: x => x.ChapterId,
                        principalTable: "CurriculumChapters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LearnerCourseItemDeltas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LearnerCourseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsHidden = table.Column<bool>(type: "boolean", nullable: false),
                    CustomOrdinal = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerCourseItemDeltas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearnerCourseItemDeltas_LearnerCourses_LearnerCourseId",
                        column: x => x.LearnerCourseId,
                        principalTable: "LearnerCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LearnerCourseProgress",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LearnerCourseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CompletedCount = table.Column<int>(type: "integer", nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerCourseProgress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearnerCourseProgress_LearnerCourses_LearnerCourseId",
                        column: x => x.LearnerCourseId,
                        principalTable: "LearnerCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CurriculumExercises",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LessonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Prompt = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurriculumExercises", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CurriculumExercises_CurriculumLessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "CurriculumLessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumBlueprints_Key",
                table: "CurriculumBlueprints",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumChapters_LevelId_Key",
                table: "CurriculumChapters",
                columns: new[] { "LevelId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumChapters_LevelId_Ordinal",
                table: "CurriculumChapters",
                columns: new[] { "LevelId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumExercises_LessonId_Key",
                table: "CurriculumExercises",
                columns: new[] { "LessonId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumExercises_LessonId_Ordinal",
                table: "CurriculumExercises",
                columns: new[] { "LessonId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumLessons_ChapterId_Key",
                table: "CurriculumLessons",
                columns: new[] { "ChapterId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumLessons_ChapterId_Ordinal",
                table: "CurriculumLessons",
                columns: new[] { "ChapterId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumLevels_BlueprintId_Key",
                table: "CurriculumLevels",
                columns: new[] { "BlueprintId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumLevels_BlueprintId_Ordinal",
                table: "CurriculumLevels",
                columns: new[] { "BlueprintId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerCourseItemDeltas_LearnerCourseId_ItemType_ItemId",
                table: "LearnerCourseItemDeltas",
                columns: new[] { "LearnerCourseId", "ItemType", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerCourseProgress_LearnerCourseId_ItemType_ItemId",
                table: "LearnerCourseProgress",
                columns: new[] { "LearnerCourseId", "ItemType", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerCourseProgress_LearnerCourseId_Status",
                table: "LearnerCourseProgress",
                columns: new[] { "LearnerCourseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerCourses_LearningCourseId",
                table: "LearnerCourses",
                column: "LearningCourseId");

            migrationBuilder.CreateIndex(
                name: "IX_LearnerCourses_ProfileId",
                table: "LearnerCourses",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_LearnerCourses_ProfileId_SharedInstanceId",
                table: "LearnerCourses",
                columns: new[] { "ProfileId", "SharedInstanceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerCourses_SharedInstanceId",
                table: "LearnerCourses",
                column: "SharedInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_SharedCourseInstances_BlueprintId_SourceLanguage_TargetLang~",
                table: "SharedCourseInstances",
                columns: new[] { "BlueprintId", "SourceLanguage", "TargetLanguage" });

            migrationBuilder.CreateIndex(
                name: "IX_SharedCourseInstances_ContentFingerprint",
                table: "SharedCourseInstances",
                column: "ContentFingerprint",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CurriculumExercises");

            migrationBuilder.DropTable(
                name: "LearnerCourseItemDeltas");

            migrationBuilder.DropTable(
                name: "LearnerCourseProgress");

            migrationBuilder.DropTable(
                name: "CurriculumLessons");

            migrationBuilder.DropTable(
                name: "LearnerCourses");

            migrationBuilder.DropTable(
                name: "CurriculumChapters");

            migrationBuilder.DropTable(
                name: "SharedCourseInstances");

            migrationBuilder.DropTable(
                name: "CurriculumLevels");

            migrationBuilder.DropTable(
                name: "CurriculumBlueprints");
        }
    }
}
