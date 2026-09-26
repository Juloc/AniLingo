using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

/// <summary>
/// Creates the canonical multilingual Learning model. Existing Term/UserTerm/
/// Review learning state is copied into it exactly once by
/// <see cref="DatabaseMigrationBridge"/> while this is the latest applied
/// migration; <see cref="RetireLegacyLearningState"/> then drops the legacy tables.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260926080000_AddUniversalLearningCourses")]
public sealed class AddUniversalLearningCourses : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "LearningUnits" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_LearningUnits" PRIMARY KEY,
                "Kind" TEXT NOT NULL,
                "TermId" TEXT NULL,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_LearningUnits_Terms_TermId"
                    FOREIGN KEY ("TermId") REFERENCES "Terms" ("Id")
                    ON DELETE SET NULL
            );
            CREATE UNIQUE INDEX "IX_LearningUnits_TermId"
                ON "LearningUnits" ("TermId");

            CREATE TABLE "LearningVariants" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_LearningVariants" PRIMARY KEY,
                "UnitId" TEXT NOT NULL,
                "LanguageTag" TEXT NOT NULL,
                "Text" TEXT NOT NULL,
                "Reading" TEXT NULL,
                "Role" TEXT NOT NULL,
                "SourceKind" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_LearningVariants_LearningUnits_UnitId"
                    FOREIGN KEY ("UnitId") REFERENCES "LearningUnits" ("Id")
                    ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX "IX_LearningVariants_UnitId_LanguageTag_Text"
                ON "LearningVariants" ("UnitId", "LanguageTag", "Text");
            CREATE INDEX "IX_LearningVariants_LanguageTag_Text"
                ON "LearningVariants" ("LanguageTag", "Text");

            CREATE TABLE "LearningCourses" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_LearningCourses" PRIMARY KEY,
                "ProfileId" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "SourceLanguage" TEXT NOT NULL,
                "TargetLanguage" TEXT NOT NULL,
                "IsEnabled" INTEGER NOT NULL,
                "IsPrimary" INTEGER NOT NULL,
                "RecognitionEnabled" INTEGER NOT NULL,
                "ProductionEnabled" INTEGER NOT NULL,
                "ListeningEnabled" INTEGER NOT NULL,
                "WritingEnabled" INTEGER NOT NULL,
                "SentencePracticeEnabled" INTEGER NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL
            );
            CREATE UNIQUE INDEX "IX_LearningCourses_ProfileId_SourceLanguage_TargetLanguage"
                ON "LearningCourses" ("ProfileId", "SourceLanguage", "TargetLanguage");
            CREATE UNIQUE INDEX "IX_LearningCourses_ProfileId_SourceLanguage"
                ON "LearningCourses" ("ProfileId", "SourceLanguage")
                WHERE "IsPrimary" = 1;

            CREATE TABLE "LearningCards" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_LearningCards" PRIMARY KEY,
                "ProfileId" TEXT NOT NULL,
                "CourseId" TEXT NOT NULL,
                "UnitId" TEXT NOT NULL,
                "PromptLanguage" TEXT NOT NULL,
                "AnswerLanguage" TEXT NOT NULL,
                "Mode" TEXT NOT NULL,
                "State" TEXT NOT NULL,
                "IntervalDays" INTEGER NOT NULL,
                "NextReviewAt" TEXT NULL,
                "LearningStartedAt" TEXT NULL,
                "QueuePosition" INTEGER NULL,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_LearningCards_LearningCourses_CourseId"
                    FOREIGN KEY ("CourseId") REFERENCES "LearningCourses" ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_LearningCards_LearningUnits_UnitId"
                    FOREIGN KEY ("UnitId") REFERENCES "LearningUnits" ("Id")
                    ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX "IX_LearningCards_CourseId_UnitId_Mode"
                ON "LearningCards" ("CourseId", "UnitId", "Mode");
            CREATE INDEX "IX_LearningCards_ProfileId_State_NextReviewAt"
                ON "LearningCards" ("ProfileId", "State", "NextReviewAt");
            CREATE INDEX "IX_LearningCards_UnitId"
                ON "LearningCards" ("UnitId");

            CREATE TABLE "LearningCardReviews" (
                "Id" INTEGER NOT NULL
                    CONSTRAINT "PK_LearningCardReviews" PRIMARY KEY AUTOINCREMENT,
                "ProfileId" TEXT NOT NULL,
                "CardId" TEXT NOT NULL,
                "Rating" INTEGER NOT NULL,
                "ClientEventId" TEXT NULL,
                "ReviewedAt" TEXT NOT NULL,
                "NextReviewAt" TEXT NOT NULL,
                CONSTRAINT "FK_LearningCardReviews_LearningCards_CardId"
                    FOREIGN KEY ("CardId") REFERENCES "LearningCards" ("Id")
                    ON DELETE CASCADE
            );
            CREATE INDEX "IX_LearningCardReviews_CardId_ReviewedAt"
                ON "LearningCardReviews" ("CardId", "ReviewedAt");
            CREATE INDEX "IX_LearningCardReviews_ProfileId_ReviewedAt"
                ON "LearningCardReviews" ("ProfileId", "ReviewedAt");
            CREATE UNIQUE INDEX "IX_LearningCardReviews_ProfileId_ClientEventId"
                ON "LearningCardReviews" ("ProfileId", "ClientEventId");

            CREATE TABLE "LearningContexts" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_LearningContexts" PRIMARY KEY,
                "UnitId" TEXT NOT NULL,
                "SourceType" TEXT NOT NULL,
                "SourceKey" TEXT NOT NULL,
                "PositionKey" TEXT NULL,
                "LanguageTag" TEXT NOT NULL,
                "Text" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_LearningContexts_LearningUnits_UnitId"
                    FOREIGN KEY ("UnitId") REFERENCES "LearningUnits" ("Id")
                    ON DELETE CASCADE
            );
            CREATE INDEX "IX_LearningContexts_UnitId"
                ON "LearningContexts" ("UnitId");
            CREATE INDEX "IX_LearningContexts_SourceType_SourceKey"
                ON "LearningContexts" ("SourceType", "SourceKey");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE "LearningContexts";
            DROP TABLE "LearningCardReviews";
            DROP TABLE "LearningCards";
            DROP TABLE "LearningCourses";
            DROP TABLE "LearningVariants";
            DROP TABLE "LearningUnits";
            """);
    }
}
