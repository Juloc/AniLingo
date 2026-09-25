using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260925130000_AddUniversalLearningCourses")]
public sealed class AddUniversalLearningCourses : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "LearningUnits" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_LearningUnits" PRIMARY KEY,
                "Kind" TEXT NOT NULL,
                "LegacyTermId" TEXT NULL,
                "CreatedAt" TEXT NOT NULL
            );
            CREATE UNIQUE INDEX "IX_LearningUnits_LegacyTermId"
                ON "LearningUnits" ("LegacyTermId")
                WHERE "LegacyTermId" IS NOT NULL;

            CREATE TABLE "LearningVariants" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_LearningVariants" PRIMARY KEY,
                "UnitId" TEXT NOT NULL,
                "LanguageTag" TEXT NOT NULL,
                "Text" TEXT NOT NULL,
                "Reading" TEXT NULL,
                "Role" TEXT NOT NULL,
                "SourceKind" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_LearningVariants_Unit"
                    FOREIGN KEY ("UnitId") REFERENCES "LearningUnits" ("Id")
                    ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX "IX_LearningVariants_Unit_Language_Text"
                ON "LearningVariants" ("UnitId", "LanguageTag", "Text");
            CREATE INDEX "IX_LearningVariants_Language_Text"
                ON "LearningVariants" ("LanguageTag", "Text");

            CREATE TABLE "LearningCourses" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_LearningCourses" PRIMARY KEY,
                "ProfileId" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "SourceLanguage" TEXT NOT NULL,
                "TargetLanguage" TEXT NOT NULL,
                "IsEnabled" INTEGER NOT NULL DEFAULT 1,
                "RecognitionEnabled" INTEGER NOT NULL DEFAULT 1,
                "ProductionEnabled" INTEGER NOT NULL DEFAULT 0,
                "ListeningEnabled" INTEGER NOT NULL DEFAULT 0,
                "WritingEnabled" INTEGER NOT NULL DEFAULT 0,
                "SentencePracticeEnabled" INTEGER NOT NULL DEFAULT 1,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL
            );
            CREATE UNIQUE INDEX "IX_LearningCourses_Profile_Pair"
                ON "LearningCourses" (
                    "ProfileId", "SourceLanguage", "TargetLanguage");
            CREATE INDEX "IX_LearningCourses_Profile_Enabled"
                ON "LearningCourses" ("ProfileId", "IsEnabled");

            CREATE TABLE "LearningCards" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_LearningCards" PRIMARY KEY,
                "ProfileId" TEXT NOT NULL,
                "CourseId" TEXT NOT NULL,
                "UnitId" TEXT NOT NULL,
                "PromptLanguage" TEXT NOT NULL,
                "AnswerLanguage" TEXT NOT NULL,
                "Mode" TEXT NOT NULL,
                "State" TEXT NOT NULL,
                "IntervalDays" INTEGER NOT NULL DEFAULT 0,
                "NextReviewAt" TEXT NULL,
                "LearningStartedAt" TEXT NULL,
                "QueuePosition" INTEGER NULL,
                "LegacyUserTermId" TEXT NULL,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_LearningCards_Course"
                    FOREIGN KEY ("CourseId") REFERENCES "LearningCourses" ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_LearningCards_Unit"
                    FOREIGN KEY ("UnitId") REFERENCES "LearningUnits" ("Id")
                    ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX "IX_LearningCards_Course_Unit_Direction_Mode"
                ON "LearningCards" (
                    "CourseId", "UnitId",
                    "PromptLanguage", "AnswerLanguage", "Mode");
            CREATE UNIQUE INDEX "IX_LearningCards_LegacyUserTermId"
                ON "LearningCards" ("LegacyUserTermId")
                WHERE "LegacyUserTermId" IS NOT NULL;
            CREATE INDEX "IX_LearningCards_Profile_State_Due"
                ON "LearningCards" ("ProfileId", "State", "NextReviewAt");

            CREATE TABLE "LearningCardReviews" (
                "Id" INTEGER NOT NULL
                    CONSTRAINT "PK_LearningCardReviews" PRIMARY KEY AUTOINCREMENT,
                "CardId" TEXT NOT NULL,
                "Rating" INTEGER NOT NULL,
                "ClientEventId" TEXT NULL,
                "ReviewedAt" TEXT NOT NULL,
                "NextReviewAt" TEXT NOT NULL,
                "LegacyReviewId" INTEGER NULL,
                CONSTRAINT "FK_LearningCardReviews_Card"
                    FOREIGN KEY ("CardId") REFERENCES "LearningCards" ("Id")
                    ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX "IX_LearningCardReviews_ClientEventId"
                ON "LearningCardReviews" ("ClientEventId")
                WHERE "ClientEventId" IS NOT NULL;
            CREATE UNIQUE INDEX "IX_LearningCardReviews_LegacyReviewId"
                ON "LearningCardReviews" ("LegacyReviewId")
                WHERE "LegacyReviewId" IS NOT NULL;
            CREATE INDEX "IX_LearningCardReviews_Card_Reviewed"
                ON "LearningCardReviews" ("CardId", "ReviewedAt");

            CREATE TABLE "LearningContexts" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_LearningContexts" PRIMARY KEY,
                "UnitId" TEXT NOT NULL,
                "SourceType" TEXT NOT NULL,
                "SourceKey" TEXT NOT NULL,
                "PositionKey" TEXT NULL,
                "LanguageTag" TEXT NOT NULL,
                "Text" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_LearningContexts_Unit"
                    FOREIGN KEY ("UnitId") REFERENCES "LearningUnits" ("Id")
                    ON DELETE CASCADE
            );
            CREATE INDEX "IX_LearningContexts_Unit"
                ON "LearningContexts" ("UnitId");
            CREATE INDEX "IX_LearningContexts_Source"
                ON "LearningContexts" ("SourceType", "SourceKey");

            -- Legacy Term/UserTerm/Review data is copied by DatabaseMigrationBridge
            -- after EF has created these tables. The bridge inspects the actual
            -- pre-migration schema first because supported Epoch2 databases may
            -- contain only a subset of the later learning columns.
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
