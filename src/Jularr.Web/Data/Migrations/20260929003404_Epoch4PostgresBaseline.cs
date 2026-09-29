using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Jularr.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class Epoch4PostgresBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AcquisitionApiKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    KeyPrefix = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    KeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcquisitionApiKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiSentenceExplanationCache",
                columns: table => new
                {
                    CacheKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PromptVersion = table.Column<int>(type: "integer", nullable: false),
                    Translation = table.Column<string>(type: "text", nullable: false),
                    GrammarJson = table.Column<string>(type: "text", nullable: false),
                    ColloquialJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiSentenceExplanationCache", x => x.CacheKey);
                });

            migrationBuilder.CreateTable(
                name: "Anime",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Anime", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearningCourses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SourceLanguage = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    TargetLanguage = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    RecognitionEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ProductionEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ListeningEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    WritingEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    SentencePracticeEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningCourses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearningPreferences",
                columns: table => new
                {
                    ProfileId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DesiredRetention = table.Column<double>(type: "double precision", nullable: false),
                    ReviewBatchSize = table.Column<int>(type: "integer", nullable: false),
                    NewWordsPerDay = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningPreferences", x => x.ProfileId);
                });

            migrationBuilder.CreateTable(
                name: "LibraryRoots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastScannedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WakeOnLanEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    WakeMacAddress = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    WakeBroadcastAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReconciliationIntervalMinutes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryRoots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NovelWorks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceProvider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Author = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    MetadataProvider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    MetadataExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    MetadataTitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MetadataNativeTitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MetadataDescription = table.Column<string>(type: "text", nullable: true),
                    CoverImageUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    BannerImageUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Format = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    MetadataStatus = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    MetadataChapterCount = table.Column<int>(type: "integer", nullable: true),
                    MetadataVolumeCount = table.Column<int>(type: "integer", nullable: true),
                    MetadataGenresJson = table.Column<string>(type: "TEXT", nullable: true),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NovelWorks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OwnerAccounts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UserName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    NormalizedUserName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnerAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProfilePlaybackPreferences",
                columns: table => new
                {
                    ProfileId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    AutoplayNext = table.Column<bool>(type: "boolean", nullable: false),
                    PreferredAudioLanguage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    PreferredSubtitleLanguage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    DefaultPlaybackSpeed = table.Column<double>(type: "double precision", nullable: false, defaultValue: 1.0),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfilePlaybackPreferences", x => x.ProfileId);
                });

            migrationBuilder.CreateTable(
                name: "SubtitleLanguageProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CutoffPosition = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubtitleLanguageProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Terms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Canonical = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Reading = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Meaning = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Terms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AcquisitionHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AnimeId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonNumber = table.Column<int>(type: "integer", nullable: false),
                    EpisodeNumber = table.Column<int>(type: "integer", nullable: false),
                    AbsoluteEpisodeNumber = table.Column<int>(type: "integer", nullable: true),
                    EventKind = table.Column<int>(type: "integer", nullable: false),
                    ReleaseTitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReleaseKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Score = table.Column<int>(type: "integer", nullable: true),
                    QualityKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Indexer = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcquisitionHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcquisitionHistory_Anime_AnimeId",
                        column: x => x.AnimeId,
                        principalTable: "Anime",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AnimeLocalMetadata",
                columns: table => new
                {
                    AnimeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OriginalTitle = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Plot = table.Column<string>(type: "text", nullable: true),
                    Year = table.Column<int>(type: "integer", nullable: true),
                    Premiered = table.Column<DateOnly>(type: "date", nullable: true),
                    MyAnimeListId = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    TvdbId = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    TmdbId = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    ImdbId = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    SourceFileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    SourceFileLastWriteTimeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnimeLocalMetadata", x => x.AnimeId);
                    table.ForeignKey(
                        name: "FK_AnimeLocalMetadata_Anime_AnimeId",
                        column: x => x.AnimeId,
                        principalTable: "Anime",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AnimeMetadata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AnimeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PreferredTitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    RomajiTitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    EnglishTitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    NativeTitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CoverImageUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    BannerImageUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Format = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Season = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    SeasonYear = table.Column<int>(type: "integer", nullable: true),
                    EpisodeCount = table.Column<int>(type: "integer", nullable: true),
                    EpisodeDurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    AverageScore = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnimeMetadata", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnimeMetadata_Anime_AnimeId",
                        column: x => x.AnimeId,
                        principalTable: "Anime",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Episodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AnimeId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonNumber = table.Column<int>(type: "integer", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DiscoveredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Episodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Episodes_Anime_AnimeId",
                        column: x => x.AnimeId,
                        principalTable: "Anime",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BookEditions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    EditionKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Isbn10 = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Isbn13 = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    Publisher = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    PublishedDate = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Author = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    SourceProvider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    SourceExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookEditions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookEditions_NovelWorks_WorkId",
                        column: x => x.WorkId,
                        principalTable: "NovelWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NovelAnimeMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChapterStart = table.Column<int>(type: "integer", nullable: false),
                    ChapterEnd = table.Column<int>(type: "integer", nullable: false),
                    AnimeProvider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    AnimeExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SeasonNumber = table.Column<int>(type: "integer", nullable: false),
                    EpisodeStart = table.Column<int>(type: "integer", nullable: false),
                    EpisodeEnd = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NovelAnimeMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NovelAnimeMappings_NovelWorks_WorkId",
                        column: x => x.WorkId,
                        principalTable: "NovelWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NovelBookmarkTombstones",
                columns: table => new
                {
                    BookmarkId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NovelBookmarkTombstones", x => x.BookmarkId);
                    table.ForeignKey(
                        name: "FK_NovelBookmarkTombstones_NovelWorks_WorkId",
                        column: x => x.WorkId,
                        principalTable: "NovelWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NovelVolumes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceFileName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SourceStoragePath = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    SourceContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CoverAsset = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NovelVolumes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NovelVolumes_NovelWorks_WorkId",
                        column: x => x.WorkId,
                        principalTable: "NovelWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReaderPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ScopeKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReadingMode = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    PageTransition = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    TwoPageSpread = table.Column<bool>(type: "boolean", nullable: true),
                    AutoScrollSpeed = table.Column<double>(type: "double precision", nullable: true),
                    FontFamily = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FontSizeRem = table.Column<double>(type: "double precision", nullable: true),
                    LineHeight = table.Column<double>(type: "double precision", nullable: true),
                    ParagraphSpacingEm = table.Column<double>(type: "double precision", nullable: true),
                    TextWidthPx = table.Column<int>(type: "integer", nullable: true),
                    TextAlignment = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    ChapterStyle = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    PaperStyle = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    GenreArtworkEnabled = table.Column<bool>(type: "boolean", nullable: true),
                    GenreTheme = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true),
                    BackgroundAssetId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    BackgroundIntensity = table.Column<double>(type: "double precision", nullable: true),
                    BackgroundMotionMode = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    ThemeEffectStrength = table.Column<double>(type: "double precision", nullable: true),
                    ThemeBrightness = table.Column<double>(type: "double precision", nullable: true),
                    ThemeContrast = table.Column<double>(type: "double precision", nullable: true),
                    ThemeSaturation = table.Column<double>(type: "double precision", nullable: true),
                    ThemeBlurPx = table.Column<double>(type: "double precision", nullable: true),
                    ThemeVignetteStrength = table.Column<double>(type: "double precision", nullable: true),
                    ThemeGrainStrength = table.Column<double>(type: "double precision", nullable: true),
                    ThemeTextBackdropStrength = table.Column<double>(type: "double precision", nullable: true),
                    ThemeParallaxStrength = table.Column<double>(type: "double precision", nullable: true),
                    ThemeTintStrength = table.Column<double>(type: "double precision", nullable: true),
                    BookmarkStyle = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    BookmarkColor = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    FuriganaEnabled = table.Column<bool>(type: "boolean", nullable: true),
                    Hyphenation = table.Column<bool>(type: "boolean", nullable: true),
                    ShowPageNumbers = table.Column<bool>(type: "boolean", nullable: true),
                    ShowIllustrations = table.Column<bool>(type: "boolean", nullable: true),
                    ParagraphIndent = table.Column<bool>(type: "boolean", nullable: true),
                    AutoContinueChapters = table.Column<bool>(type: "boolean", nullable: true),
                    TtsProviderId = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    TtsVoiceIds = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    TtsRate = table.Column<double>(type: "double precision", nullable: true),
                    TtsPitch = table.Column<double>(type: "double precision", nullable: true),
                    TtsVolume = table.Column<double>(type: "double precision", nullable: true),
                    TtsAutoContinueChapters = table.Column<bool>(type: "boolean", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReaderPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReaderPreferences_NovelWorks_WorkId",
                        column: x => x.WorkId,
                        principalTable: "NovelWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubtitleLanguageProfileItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    LanguageTag = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Forced = table.Column<bool>(type: "boolean", nullable: false),
                    Sdh = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubtitleLanguageProfileItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubtitleLanguageProfileItems_SubtitleLanguageProfiles_Profi~",
                        column: x => x.ProfileId,
                        principalTable: "SubtitleLanguageProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubtitleProfileAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaType = table.Column<int>(type: "integer", nullable: true),
                    LibraryRootId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubtitleProfileAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubtitleProfileAssignments_LibraryRoots_LibraryRootId",
                        column: x => x.LibraryRootId,
                        principalTable: "LibraryRoots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SubtitleProfileAssignments_SubtitleLanguageProfiles_Profile~",
                        column: x => x.ProfileId,
                        principalTable: "SubtitleLanguageProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LearningUnits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TermId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearningUnits_Terms_TermId",
                        column: x => x.TermId,
                        principalTable: "Terms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "EpisodeMediaSegments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    StartMs = table.Column<long>(type: "bigint", nullable: false),
                    EndMs = table.Column<long>(type: "bigint", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    Method = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    MediaIdentity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "EpisodePlaybackHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastPlayedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PositionMs = table.Column<long>(type: "bigint", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    ReachedEnd = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EpisodePlaybackHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EpisodePlaybackHistory_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EpisodeProgress",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    PositionMs = table.Column<long>(type: "bigint", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    IsCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EpisodeProgress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EpisodeProgress_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EpisodeSegmentDetectionStates",
                columns: table => new
                {
                    EpisodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Method = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    MediaIdentity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SegmentsFound = table.Column<int>(type: "integer", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "EpisodeTerms",
                columns: table => new
                {
                    EpisodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    TermId = table.Column<Guid>(type: "uuid", nullable: false),
                    Occurrences = table.Column<int>(type: "integer", nullable: false),
                    FirstCueStartMs = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EpisodeTerms", x => new { x.EpisodeId, x.TermId });
                    table.ForeignKey(
                        name: "FK_EpisodeTerms_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EpisodeTerms_Terms_TermId",
                        column: x => x.TermId,
                        principalTable: "Terms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LibraryRootId = table.Column<Guid>(type: "uuid", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    LastWriteTimeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DiscoveredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaFiles_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MediaFiles_LibraryRoots_LibraryRootId",
                        column: x => x.LibraryRootId,
                        principalTable: "LibraryRoots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubtitleTracks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Format = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Forced = table.Column<bool>(type: "boolean", nullable: false),
                    Sdh = table.Column<bool>(type: "boolean", nullable: false),
                    SourceUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubtitleTracks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubtitleTracks_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BookFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EditionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    FileName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Format = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MediaType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SourceKind = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StoragePath = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookFiles_BookEditions_EditionId",
                        column: x => x.EditionId,
                        principalTable: "BookEditions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NovelChapters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    VolumeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OriginalText = table.Column<string>(type: "text", nullable: false),
                    ContentJson = table.Column<string>(type: "TEXT", nullable: true),
                    GroupTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SourceHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NovelChapters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NovelChapters_NovelVolumes_VolumeId",
                        column: x => x.VolumeId,
                        principalTable: "NovelVolumes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NovelChapters_NovelWorks_WorkId",
                        column: x => x.WorkId,
                        principalTable: "NovelWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LearningCards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CourseId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    PromptLanguage = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    AnswerLanguage = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    Mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IntervalDays = table.Column<int>(type: "integer", nullable: false),
                    NextReviewAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LearningStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    QueuePosition = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningCards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearningCards_LearningCourses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "LearningCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LearningCards_LearningUnits_UnitId",
                        column: x => x.UnitId,
                        principalTable: "LearningUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LearningContexts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PositionKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LanguageTag = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    Text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningContexts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearningContexts_LearningUnits_UnitId",
                        column: x => x.UnitId,
                        principalTable: "LearningUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LearningVariants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    LanguageTag = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    Text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Reading = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SourceKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningVariants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearningVariants_LearningUnits_UnitId",
                        column: x => x.UnitId,
                        principalTable: "LearningUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaAnalyses",
                columns: table => new
                {
                    MediaFileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProbeVersion = table.Column<int>(type: "integer", nullable: false),
                    SourceSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    SourceLastWriteTimeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourceFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Diagnostic = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AnalyzedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Container = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    DurationSeconds = table.Column<double>(type: "double precision", nullable: true),
                    VideoCodec = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    VideoProfile = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Width = table.Column<int>(type: "integer", nullable: true),
                    Height = table.Column<int>(type: "integer", nullable: true),
                    PixelFormat = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    BitDepth = table.Column<int>(type: "integer", nullable: true),
                    DynamicRange = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true)
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
                name: "SubtitleCues",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SubtitleTrackId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartMs = table.Column<int>(type: "integer", nullable: false),
                    EndMs = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubtitleCues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubtitleCues_SubtitleTracks_SubtitleTrackId",
                        column: x => x.SubtitleTrackId,
                        principalTable: "SubtitleTracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NovelBookmarks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChapterId = table.Column<Guid>(type: "uuid", nullable: false),
                    PositionPermille = table.Column<int>(type: "integer", nullable: false),
                    Language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ParagraphIndex = table.Column<int>(type: "integer", nullable: true),
                    CharacterOffset = table.Column<int>(type: "integer", nullable: false),
                    AnchorText = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    Label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Style = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    Color = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SyncUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClientEventId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NovelBookmarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NovelBookmarks_NovelChapters_ChapterId",
                        column: x => x.ChapterId,
                        principalTable: "NovelChapters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NovelBookmarks_NovelWorks_WorkId",
                        column: x => x.WorkId,
                        principalTable: "NovelWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NovelHighlights",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChapterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ParagraphIndex = table.Column<int>(type: "integer", nullable: false),
                    StartOffset = table.Column<int>(type: "integer", nullable: false),
                    EndOffset = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NovelHighlights", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NovelHighlights_NovelChapters_ChapterId",
                        column: x => x.ChapterId,
                        principalTable: "NovelChapters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NovelHighlights_NovelWorks_WorkId",
                        column: x => x.WorkId,
                        principalTable: "NovelWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NovelProgress",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChapterId = table.Column<Guid>(type: "uuid", nullable: false),
                    PositionPermille = table.Column<int>(type: "integer", nullable: false),
                    AnchorLanguage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AnchorParagraphIndex = table.Column<int>(type: "integer", nullable: true),
                    AnchorOffset = table.Column<int>(type: "integer", nullable: false),
                    AnchorText = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NovelProgress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NovelProgress_NovelChapters_ChapterId",
                        column: x => x.ChapterId,
                        principalTable: "NovelChapters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NovelProgress_NovelWorks_WorkId",
                        column: x => x.WorkId,
                        principalTable: "NovelWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NovelTranslations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChapterId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetLanguage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ProviderId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PromptVersion = table.Column<int>(type: "integer", nullable: false),
                    SourceHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NovelTranslations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NovelTranslations_NovelChapters_ChapterId",
                        column: x => x.ChapterId,
                        principalTable: "NovelChapters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LearningCardReviews",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProfileId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CardId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    ClientEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NextReviewAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningCardReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearningCardReviews_LearningCards_CardId",
                        column: x => x.CardId,
                        principalTable: "LearningCards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaAnalysisStreams",
                columns: table => new
                {
                    MediaFileId = table.Column<Guid>(type: "uuid", nullable: false),
                    StreamIndex = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Codec = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Language = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Channels = table.Column<int>(type: "integer", nullable: true),
                    ChannelLayout = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsForced = table.Column<bool>(type: "boolean", nullable: false)
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
                name: "IX_AcquisitionApiKeys_KeyHash",
                table: "AcquisitionApiKeys",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionHistory_AnimeId_SeasonNumber_EpisodeNumber_Occur~",
                table: "AcquisitionHistory",
                columns: new[] { "AnimeId", "SeasonNumber", "EpisodeNumber", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Anime_Key",
                table: "Anime",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnimeMetadata_AnimeId",
                table: "AnimeMetadata",
                column: "AnimeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnimeMetadata_Provider_ExternalId",
                table: "AnimeMetadata",
                columns: new[] { "Provider", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookEditions_Isbn10",
                table: "BookEditions",
                column: "Isbn10");

            migrationBuilder.CreateIndex(
                name: "IX_BookEditions_Isbn13",
                table: "BookEditions",
                column: "Isbn13");

            migrationBuilder.CreateIndex(
                name: "IX_BookEditions_WorkId_EditionKey",
                table: "BookEditions",
                columns: new[] { "WorkId", "EditionKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookEditions_WorkId_IsPrimary",
                table: "BookEditions",
                columns: new[] { "WorkId", "IsPrimary" });

            migrationBuilder.CreateIndex(
                name: "IX_BookFiles_ContentHash",
                table: "BookFiles",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_BookFiles_EditionId_FileKey",
                table: "BookFiles",
                columns: new[] { "EditionId", "FileKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookFiles_EditionId_IsPrimary",
                table: "BookFiles",
                columns: new[] { "EditionId", "IsPrimary" });

            migrationBuilder.CreateIndex(
                name: "IX_EpisodeMediaSegments_EpisodeId_Kind_Source",
                table: "EpisodeMediaSegments",
                columns: new[] { "EpisodeId", "Kind", "Source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EpisodePlaybackHistory_EpisodeId",
                table: "EpisodePlaybackHistory",
                column: "EpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_EpisodePlaybackHistory_ProfileId_LastPlayedAt",
                table: "EpisodePlaybackHistory",
                columns: new[] { "ProfileId", "LastPlayedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EpisodeProgress_EpisodeId",
                table: "EpisodeProgress",
                column: "EpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_EpisodeProgress_ProfileId_EpisodeId",
                table: "EpisodeProgress",
                columns: new[] { "ProfileId", "EpisodeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EpisodeProgress_ProfileId_UpdatedAt",
                table: "EpisodeProgress",
                columns: new[] { "ProfileId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_AnimeId_SeasonNumber_Number",
                table: "Episodes",
                columns: new[] { "AnimeId", "SeasonNumber", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EpisodeTerms_TermId",
                table: "EpisodeTerms",
                column: "TermId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningCardReviews_CardId_ReviewedAt",
                table: "LearningCardReviews",
                columns: new[] { "CardId", "ReviewedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LearningCardReviews_ProfileId_ClientEventId",
                table: "LearningCardReviews",
                columns: new[] { "ProfileId", "ClientEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearningCardReviews_ProfileId_ReviewedAt",
                table: "LearningCardReviews",
                columns: new[] { "ProfileId", "ReviewedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LearningCards_CourseId_UnitId_Mode",
                table: "LearningCards",
                columns: new[] { "CourseId", "UnitId", "Mode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearningCards_ProfileId_State_NextReviewAt",
                table: "LearningCards",
                columns: new[] { "ProfileId", "State", "NextReviewAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LearningCards_UnitId",
                table: "LearningCards",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningContexts_ProfileId_SourceType_SourceKey",
                table: "LearningContexts",
                columns: new[] { "ProfileId", "SourceType", "SourceKey" });

            migrationBuilder.CreateIndex(
                name: "IX_LearningContexts_ProfileId_UnitId_SourceType_SourceKey_Posi~",
                table: "LearningContexts",
                columns: new[] { "ProfileId", "UnitId", "SourceType", "SourceKey", "PositionKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearningContexts_UnitId",
                table: "LearningContexts",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningCourses_ProfileId_SourceLanguage",
                table: "LearningCourses",
                columns: new[] { "ProfileId", "SourceLanguage" },
                unique: true,
                filter: "\"IsPrimary\"");

            migrationBuilder.CreateIndex(
                name: "IX_LearningCourses_ProfileId_SourceLanguage_TargetLanguage",
                table: "LearningCourses",
                columns: new[] { "ProfileId", "SourceLanguage", "TargetLanguage" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearningUnits_TermId",
                table: "LearningUnits",
                column: "TermId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearningVariants_LanguageTag_Text",
                table: "LearningVariants",
                columns: new[] { "LanguageTag", "Text" });

            migrationBuilder.CreateIndex(
                name: "IX_LearningVariants_UnitId_LanguageTag_Text",
                table: "LearningVariants",
                columns: new[] { "UnitId", "LanguageTag", "Text" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryRoots_Path",
                table: "LibraryRoots",
                column: "Path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaAnalyses_Status_ProbeVersion",
                table: "MediaAnalyses",
                columns: new[] { "Status", "ProbeVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_EpisodeId",
                table: "MediaFiles",
                column: "EpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_LibraryRootId_EpisodeId",
                table: "MediaFiles",
                columns: new[] { "LibraryRootId", "EpisodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_Path",
                table: "MediaFiles",
                column: "Path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NovelAnimeMappings_AnimeProvider_AnimeExternalId",
                table: "NovelAnimeMappings",
                columns: new[] { "AnimeProvider", "AnimeExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_NovelAnimeMappings_WorkId_ChapterStart_ChapterEnd",
                table: "NovelAnimeMappings",
                columns: new[] { "WorkId", "ChapterStart", "ChapterEnd" });

            migrationBuilder.CreateIndex(
                name: "IX_NovelBookmarks_ChapterId",
                table: "NovelBookmarks",
                column: "ChapterId");

            migrationBuilder.CreateIndex(
                name: "IX_NovelBookmarks_ProfileId_ChapterId",
                table: "NovelBookmarks",
                columns: new[] { "ProfileId", "ChapterId" });

            migrationBuilder.CreateIndex(
                name: "IX_NovelBookmarks_ProfileId_WorkId_CreatedAt",
                table: "NovelBookmarks",
                columns: new[] { "ProfileId", "WorkId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NovelBookmarks_WorkId",
                table: "NovelBookmarks",
                column: "WorkId");

            migrationBuilder.CreateIndex(
                name: "IX_NovelBookmarkTombstones_ProfileId_WorkId",
                table: "NovelBookmarkTombstones",
                columns: new[] { "ProfileId", "WorkId" });

            migrationBuilder.CreateIndex(
                name: "IX_NovelBookmarkTombstones_WorkId",
                table: "NovelBookmarkTombstones",
                column: "WorkId");

            migrationBuilder.CreateIndex(
                name: "IX_NovelChapters_VolumeId",
                table: "NovelChapters",
                column: "VolumeId");

            migrationBuilder.CreateIndex(
                name: "IX_NovelChapters_WorkId_Number",
                table: "NovelChapters",
                columns: new[] { "WorkId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NovelHighlights_ChapterId",
                table: "NovelHighlights",
                column: "ChapterId");

            migrationBuilder.CreateIndex(
                name: "IX_NovelHighlights_ProfileId_ChapterId_Language_ParagraphIndex",
                table: "NovelHighlights",
                columns: new[] { "ProfileId", "ChapterId", "Language", "ParagraphIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_NovelHighlights_ProfileId_WorkId_CreatedAt",
                table: "NovelHighlights",
                columns: new[] { "ProfileId", "WorkId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NovelHighlights_WorkId",
                table: "NovelHighlights",
                column: "WorkId");

            migrationBuilder.CreateIndex(
                name: "IX_NovelProgress_ChapterId",
                table: "NovelProgress",
                column: "ChapterId");

            migrationBuilder.CreateIndex(
                name: "IX_NovelProgress_ProfileId_WorkId",
                table: "NovelProgress",
                columns: new[] { "ProfileId", "WorkId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NovelProgress_WorkId",
                table: "NovelProgress",
                column: "WorkId");

            migrationBuilder.CreateIndex(
                name: "IX_NovelTranslations_ChapterId_TargetLanguage_ProviderId_Promp~",
                table: "NovelTranslations",
                columns: new[] { "ChapterId", "TargetLanguage", "ProviderId", "PromptVersion", "SourceHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NovelVolumes_WorkId_Number",
                table: "NovelVolumes",
                columns: new[] { "WorkId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NovelVolumes_WorkId_SourceKey",
                table: "NovelVolumes",
                columns: new[] { "WorkId", "SourceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NovelWorks_MetadataProvider_MetadataExternalId",
                table: "NovelWorks",
                columns: new[] { "MetadataProvider", "MetadataExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NovelWorks_SourceProvider_SourceKey",
                table: "NovelWorks",
                columns: new[] { "SourceProvider", "SourceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OwnerAccounts_NormalizedUserName",
                table: "OwnerAccounts",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReaderPreferences_ProfileId_ScopeKey",
                table: "ReaderPreferences",
                columns: new[] { "ProfileId", "ScopeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReaderPreferences_WorkId",
                table: "ReaderPreferences",
                column: "WorkId");

            migrationBuilder.CreateIndex(
                name: "IX_SubtitleCues_SubtitleTrackId_StartMs",
                table: "SubtitleCues",
                columns: new[] { "SubtitleTrackId", "StartMs" });

            migrationBuilder.CreateIndex(
                name: "IX_SubtitleLanguageProfileItems_ProfileId_SortOrder",
                table: "SubtitleLanguageProfileItems",
                columns: new[] { "ProfileId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_SubtitleLanguageProfiles_Name",
                table: "SubtitleLanguageProfiles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubtitleProfileAssignments_LibraryRootId",
                table: "SubtitleProfileAssignments",
                column: "LibraryRootId");

            migrationBuilder.CreateIndex(
                name: "IX_SubtitleProfileAssignments_MediaType_LibraryRootId",
                table: "SubtitleProfileAssignments",
                columns: new[] { "MediaType", "LibraryRootId" });

            migrationBuilder.CreateIndex(
                name: "IX_SubtitleProfileAssignments_ProfileId",
                table: "SubtitleProfileAssignments",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_SubtitleTracks_EpisodeId_Language",
                table: "SubtitleTracks",
                columns: new[] { "EpisodeId", "Language" });

            migrationBuilder.CreateIndex(
                name: "IX_SubtitleTracks_Path",
                table: "SubtitleTracks",
                column: "Path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Terms_Language_Canonical",
                table: "Terms",
                columns: new[] { "Language", "Canonical" },
                unique: true);

            // Raw-SQL tables: created/read by ADO.NET stores, not part of the EF model
            // (kept out of the model snapshot). Created here so the squashed Postgres baseline
            // reproduces the complete schema. See docs/PERSISTENCE.md.
            migrationBuilder.Sql(@"
CREATE TABLE ""AccountSessionStates"" (
    ""AccountId"" TEXT NOT NULL CONSTRAINT ""PK_AccountSessionStates"" PRIMARY KEY,
    ""Version"" bigint NOT NULL DEFAULT 1,
    CONSTRAINT ""FK_AccountSessionStates_OwnerAccounts_AccountId""
        FOREIGN KEY (""AccountId"") REFERENCES ""OwnerAccounts"" (""Id"") ON DELETE CASCADE
);


CREATE TABLE ""AcquisitionAccessPolicies"" (
    ""Kind"" TEXT NOT NULL CONSTRAINT ""PK_AcquisitionAccessPolicies"" PRIMARY KEY
        CHECK (""Kind"" IN ('anime', 'manga', 'lightNovel', 'book')),
    ""UserAddMode"" TEXT NOT NULL CHECK (""UserAddMode"" IN ('disabled', 'request', 'automatic')),
    ""ManualAddMode"" TEXT NOT NULL CHECK (""ManualAddMode"" IN ('ownerOnly', 'users')),
    ""UpdatedAt"" TEXT NOT NULL
);


CREATE TABLE ""AcquisitionRequests"" (
    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_AcquisitionRequests"" PRIMARY KEY,
    ""Kind"" TEXT NOT NULL CHECK (""Kind"" IN ('anime', 'manga', 'lightNovel', 'book')),
    ""Provider"" TEXT NOT NULL,
    ""ExternalId"" TEXT NOT NULL,
    ""Title"" TEXT NOT NULL,
    ""Subtitle"" TEXT NULL,
    ""CoverImageUrl"" TEXT NULL,
    ""PayloadJson"" TEXT NULL,
    ""RequestedByProfileId"" TEXT NOT NULL,
    ""Status"" TEXT NOT NULL CHECK (""Status"" IN ('pending', 'approved', 'searching', 'downloading', 'importing', 'completed', 'rejected', 'failed')),
    ""StatusMessage"" TEXT NULL,
    ""OperationId"" TEXT NULL,
    ""ResultUrl"" TEXT NULL,
    ""CreatedAt"" TEXT NOT NULL,
    ""UpdatedAt"" TEXT NOT NULL,
    ""DecidedByProfileId"" TEXT NULL,
    ""DecidedAt"" TEXT NULL
);

CREATE INDEX ""IX_AcquisitionRequests_Status_UpdatedAt"" ON ""AcquisitionRequests"" (""Status"", ""UpdatedAt"");
CREATE INDEX ""IX_AcquisitionRequests_RequestedByProfileId"" ON ""AcquisitionRequests"" (""RequestedByProfileId"");
CREATE UNIQUE INDEX ""IX_AcquisitionRequests_OpenTitle"" ON ""AcquisitionRequests"" (""Kind"", ""Provider"", ""ExternalId"")
    WHERE ""Status"" IN ('pending', 'approved', 'searching', 'downloading', 'importing');

CREATE TABLE ""AiModelCatalogs"" (
    ""ProviderKey"" TEXT NOT NULL CONSTRAINT ""PK_AiModelCatalogs"" PRIMARY KEY,
    ""ModelsJson"" TEXT NOT NULL,
    ""Discovery"" TEXT NOT NULL CHECK (""Discovery"" IN ('unknown', 'supported', 'unsupported')),
    ""FetchedAt"" TEXT NULL,
    ""LastAttemptAt"" TEXT NULL,
    ""LastError"" TEXT NULL
);


CREATE TABLE ""AiUsageDaily"" (
    ""ProfileId"" TEXT NOT NULL,
    ""Day"" TEXT NOT NULL,
    ""ProviderId"" TEXT NOT NULL,
    ""Model"" TEXT NOT NULL,
    ""Operation"" TEXT NOT NULL,
    ""Requests"" bigint NOT NULL DEFAULT 0,
    ""EstimatedRequests"" bigint NOT NULL DEFAULT 0,
    ""InputTokens"" bigint NOT NULL DEFAULT 0,
    ""CachedInputTokens"" bigint NOT NULL DEFAULT 0,
    ""OutputTokens"" bigint NOT NULL DEFAULT 0,
    ""ReasoningOutputTokens"" bigint NOT NULL DEFAULT 0,
    ""EstimatedInputTokens"" bigint NOT NULL DEFAULT 0,
    ""EstimatedOutputTokens"" bigint NOT NULL DEFAULT 0,
    ""ContextTokens"" bigint NOT NULL DEFAULT 0,
    ""CacheHits"" bigint NOT NULL DEFAULT 0,
    ""ResumedChunks"" bigint NOT NULL DEFAULT 0,
    ""Retries"" bigint NOT NULL DEFAULT 0,
    ""Failures"" bigint NOT NULL DEFAULT 0,
    ""Cancellations"" bigint NOT NULL DEFAULT 0,
    ""DurationMs"" bigint NOT NULL DEFAULT 0, ""FullContextTokens"" bigint NOT NULL DEFAULT 0,
    CONSTRAINT ""PK_AiUsageDaily"" PRIMARY KEY (""ProfileId"", ""Day"", ""ProviderId"", ""Model"", ""Operation"")
);

CREATE INDEX ""IX_AiUsageDaily_Day"" ON ""AiUsageDaily"" (""Day"");

CREATE TABLE ""AnimeMappingAuditEntries"" (
    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_AnimeMappingAuditEntries"" PRIMARY KEY,
    ""AnimeId"" TEXT NOT NULL,
    ""Action"" TEXT NOT NULL,
    ""Summary"" TEXT NOT NULL,
    ""Details"" TEXT NOT NULL,
    ""Actor"" TEXT NOT NULL,
    ""CreatedAt"" TEXT NOT NULL
);

CREATE INDEX ""IX_AnimeMappingAuditEntries_Anime""
    ON ""AnimeMappingAuditEntries"" (""AnimeId"", ""CreatedAt"");

CREATE TABLE ""ChapterArtworkPreferences"" (
    ""ProfileId"" TEXT NOT NULL CONSTRAINT ""PK_ChapterArtworkPreferences"" PRIMARY KEY,
    ""Enabled"" bigint NOT NULL,
    ""AutoGenerate"" bigint NOT NULL,
    ""Style"" TEXT NOT NULL,
    ""Quality"" TEXT NOT NULL CHECK (""Quality"" IN ('standard', 'high')),
    ""Variations"" bigint NOT NULL CHECK (""Variations"" BETWEEN 1 AND 3),
    ""UpdatedAt"" TEXT NOT NULL
);


CREATE TABLE ""ChapterArtworkWorkSettings"" (
    ""WorkId"" TEXT NOT NULL CONSTRAINT ""PK_ChapterArtworkWorkSettings"" PRIMARY KEY,
    ""Enabled"" bigint NULL,
    ""Style"" TEXT NULL,
    ""SeriesStyle"" TEXT NULL,
    ""UseChapterTitles"" bigint NOT NULL DEFAULT 0,
    ""UpdatedAt"" TEXT NOT NULL
);


CREATE TABLE ""ChapterArtworks"" (
    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_ChapterArtworks"" PRIMARY KEY,
    ""WorkId"" TEXT NOT NULL,
    ""ChapterId"" TEXT NOT NULL,
    ""ChapterNumber"" bigint NOT NULL,
    ""Status"" TEXT NOT NULL CHECK (""Status"" IN ('queued', 'generating', 'preview', 'accepted', 'failed', 'cancelled')),
    ""AssetPath"" TEXT NULL,
    ""MediaType"" TEXT NULL,
    ""ContentHash"" TEXT NULL,
    ""ByteSize"" bigint NULL,
    ""ProviderId"" TEXT NULL,
    ""Model"" TEXT NULL,
    ""PromptVersion"" bigint NOT NULL,
    ""ContextHash"" TEXT NULL,
    ""ContextScope"" TEXT NOT NULL,
    ""Prompt"" TEXT NULL,
    ""NeutralPrompt"" bigint NOT NULL DEFAULT 0,
    ""Style"" TEXT NOT NULL,
    ""Quality"" TEXT NOT NULL CHECK (""Quality"" IN ('standard', 'high')),
    ""Error"" TEXT NULL,
    ""OperationId"" TEXT NULL,
    ""RequestedByProfileId"" TEXT NOT NULL,
    ""CreatedAt"" TEXT NOT NULL,
    ""UpdatedAt"" TEXT NOT NULL,
    ""AcceptedAt"" TEXT NULL
);

CREATE INDEX ""IX_ChapterArtworks_Work_Chapter"" ON ""ChapterArtworks"" (""WorkId"", ""ChapterNumber"");
CREATE UNIQUE INDEX ""IX_ChapterArtworks_Accepted"" ON ""ChapterArtworks"" (""WorkId"", ""ChapterNumber"")
    WHERE ""Status"" = 'accepted';

CREATE TABLE ""Events"" (
    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_Events"" PRIMARY KEY,
    ""Category"" integer NOT NULL,
    ""Audience"" integer NOT NULL,
    ""ProfileId"" TEXT NULL,
    ""MediaType"" TEXT NULL,
    ""SubjectId"" TEXT NULL,
    ""MessageParamsJson"" TEXT NULL,
    ""Severity"" integer NOT NULL,
    ""DeepLink"" TEXT NULL,
    ""DedupKey"" TEXT NULL,
    ""RelatedOperationId"" TEXT NULL,
    ""CreatedAtUtc"" TEXT NOT NULL
);

CREATE INDEX ""IX_Events_CreatedAtUtc"" ON ""Events"" (""CreatedAtUtc"");

CREATE TABLE ""Franchises"" (
    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_Franchises"" PRIMARY KEY,
    ""Title"" TEXT NOT NULL,
    ""SeedMediaType"" TEXT NOT NULL,
    ""SeedProvider"" TEXT NOT NULL,
    ""SeedExternalId"" TEXT NOT NULL,
    ""CreatedAtUtc"" TEXT NOT NULL,
    ""UpdatedAtUtc"" TEXT NOT NULL,
    ""LastRefreshedAtUtc"" TEXT NULL, ""RefreshRequestedAtUtc"" TEXT NULL,
    CONSTRAINT ""AK_Franchises_Seed"" UNIQUE (""SeedMediaType"", ""SeedProvider"", ""SeedExternalId"")
);


CREATE TABLE ""FranchiseMembers"" (
    ""FranchiseId"" TEXT NOT NULL,
    ""MediaType"" TEXT NOT NULL,
    ""Provider"" TEXT NOT NULL,
    ""ExternalId"" TEXT NOT NULL,
    ""Title"" TEXT NOT NULL,
    ""NativeTitle"" TEXT NULL,
    ""CoverImageUrl"" TEXT NULL,
    ""Format"" TEXT NULL,
    ""Status"" TEXT NULL,
    ""Year"" integer NULL,
    ""LocalMediaId"" TEXT NULL,
    ""DetailsUrl"" TEXT NULL,
    ""RelationType"" TEXT NULL,
    ""IsSeed"" integer NOT NULL,
    ""UpdatedAtUtc"" TEXT NOT NULL, ""RelationsCheckedAtUtc"" TEXT NULL,
    CONSTRAINT ""PK_FranchiseMembers"" PRIMARY KEY (""FranchiseId"", ""MediaType"", ""Provider"", ""ExternalId""),
    CONSTRAINT ""FK_FranchiseMembers_Franchises"" FOREIGN KEY (""FranchiseId"") REFERENCES ""Franchises"" (""Id"") ON DELETE CASCADE
);

CREATE INDEX ""IX_FranchiseMembers_ProviderIdentity"" ON ""FranchiseMembers"" (""MediaType"", ""Provider"", ""ExternalId"");

CREATE TABLE ""ProfileFranchiseFollows"" (
    ""ProfileId"" TEXT NOT NULL,
    ""FranchiseId"" TEXT NOT NULL,
    ""FollowedAtUtc"" TEXT NOT NULL,
    CONSTRAINT ""PK_ProfileFranchiseFollows"" PRIMARY KEY (""ProfileId"", ""FranchiseId""),
    CONSTRAINT ""FK_ProfileFranchiseFollows_Franchises"" FOREIGN KEY (""FranchiseId"") REFERENCES ""Franchises"" (""Id"") ON DELETE CASCADE
);

CREATE INDEX ""IX_ProfileFranchiseFollows_FranchiseId"" ON ""ProfileFranchiseFollows"" (""FranchiseId"");

CREATE TABLE ""InstanceAppearanceSettings"" (
    ""Id"" integer NOT NULL CONSTRAINT ""PK_InstanceAppearanceSettings"" PRIMARY KEY CHECK (""Id"" = 1),
    ""DefaultThemeId"" TEXT NOT NULL DEFAULT 'original',
    ""AllowProfileThemeOverride"" integer NOT NULL DEFAULT 1,
    ""AllowProfileAccentOverride"" integer NOT NULL DEFAULT 1,
    ""UpdatedAt"" TEXT NOT NULL
);


CREATE TABLE ""KnownDevices"" (
    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_KnownDevices"" PRIMARY KEY,
    ""ProfileId"" TEXT NOT NULL,
    ""ClientKind"" TEXT NOT NULL,
    ""Label"" TEXT NULL,
    ""AppVersion"" TEXT NULL,
    ""UserAgent"" TEXT NULL,
    ""FirstSeenUtc"" timestamp with time zone NOT NULL,
    ""LastSeenUtc"" timestamp with time zone NOT NULL,
    CONSTRAINT ""FK_KnownDevices_OwnerAccounts_ProfileId""
        FOREIGN KEY (""ProfileId"") REFERENCES ""OwnerAccounts"" (""Id"") ON DELETE CASCADE
);

CREATE INDEX ""IX_KnownDevices_ProfileId"" ON ""KnownDevices"" (""ProfileId"");

CREATE TABLE ""LearningCapabilityOverrides"" (
    ""ProfileId"" TEXT NOT NULL,
    ""ScopeType"" TEXT NOT NULL,
    ""ScopeKey"" TEXT NOT NULL,
    ""Capability"" TEXT NOT NULL,
    ""IsEnabled"" integer NOT NULL,
    ""UpdatedAt"" TEXT NOT NULL,
    CONSTRAINT ""PK_LearningCapabilityOverrides""
        PRIMARY KEY (
            ""ProfileId"", ""ScopeType"", ""ScopeKey"", ""Capability"")
);

CREATE INDEX ""IX_LearningCapabilityOverrides_Profile""
    ON ""LearningCapabilityOverrides"" (
        ""ProfileId"", ""ScopeType"", ""ScopeKey"");

CREATE TABLE ""LearningScopeModes"" (
    ""ProfileId"" TEXT NOT NULL,
    ""ScopeType"" TEXT NOT NULL,
    ""ScopeKey"" TEXT NOT NULL,
    ""ModeOverride"" TEXT NOT NULL,
    ""UpdatedAt"" TEXT NOT NULL,
    CONSTRAINT ""PK_LearningScopeModes""
        PRIMARY KEY (""ProfileId"", ""ScopeType"", ""ScopeKey"")
);

CREATE INDEX ""IX_LearningScopeModes_Profile""
    ON ""LearningScopeModes"" (""ProfileId"", ""ScopeType"");

CREATE TABLE ""MangaSeries"" (
    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_MangaSeries"" PRIMARY KEY,
    ""Title"" TEXT NOT NULL,
    ""SourcePath"" TEXT NOT NULL,
    ""MetadataProvider"" TEXT NULL,
    ""MetadataExternalId"" TEXT NULL,
    ""MetadataTitle"" TEXT NULL,
    ""MetadataNativeTitle"" TEXT NULL,
    ""MetadataDescription"" TEXT NULL,
    ""CoverImageUrl"" TEXT NULL,
    ""BannerImageUrl"" TEXT NULL,
    ""MetadataStatus"" TEXT NULL,
    ""Direction"" TEXT NOT NULL DEFAULT 'rtl',
    ""CreatedAt"" TEXT NOT NULL,
    ""UpdatedAt"" TEXT NOT NULL
);

CREATE UNIQUE INDEX ""IX_MangaSeries_SourcePath""
    ON ""MangaSeries"" (""SourcePath"");
CREATE UNIQUE INDEX ""IX_MangaSeries_Metadata""
    ON ""MangaSeries"" (""MetadataProvider"", ""MetadataExternalId"")
    WHERE ""MetadataProvider"" IS NOT NULL AND ""MetadataExternalId"" IS NOT NULL;

CREATE TABLE ""MangaVolumes"" (
    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_MangaVolumes"" PRIMARY KEY,
    ""SeriesId"" TEXT NOT NULL,
    ""Number"" integer NOT NULL,
    ""Title"" TEXT NULL,
    ""CreatedAt"" TEXT NOT NULL,
    ""UpdatedAt"" TEXT NOT NULL,
    CONSTRAINT ""FK_MangaVolumes_MangaSeries_SeriesId""
        FOREIGN KEY (""SeriesId"") REFERENCES ""MangaSeries"" (""Id"") ON DELETE CASCADE
);

CREATE UNIQUE INDEX ""IX_MangaVolumes_Series_Number""
    ON ""MangaVolumes"" (""SeriesId"", ""Number"");

CREATE TABLE ""MangaChapters"" (
    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_MangaChapters"" PRIMARY KEY,
    ""SeriesId"" TEXT NOT NULL,
    ""Number"" double precision NOT NULL,
    ""VolumeNumber"" integer NULL,
    ""Title"" TEXT NOT NULL,
    ""SourcePath"" TEXT NOT NULL,
    ""SourceKind"" TEXT NOT NULL,
    ""PageCount"" integer NOT NULL,
    ""SourceUpdatedAt"" TEXT NOT NULL,
    ""CreatedAt"" TEXT NOT NULL,
    ""UpdatedAt"" TEXT NOT NULL, ""VolumeId"" TEXT NULL
    CONSTRAINT ""FK_MangaChapters_MangaVolumes_VolumeId""
        REFERENCES ""MangaVolumes"" (""Id"") ON DELETE SET NULL,
    CONSTRAINT ""FK_MangaChapters_MangaSeries_SeriesId""
        FOREIGN KEY (""SeriesId"") REFERENCES ""MangaSeries"" (""Id"") ON DELETE CASCADE
);

CREATE UNIQUE INDEX ""IX_MangaChapters_Series_Source""
    ON ""MangaChapters"" (""SeriesId"", ""SourcePath"");
CREATE INDEX ""IX_MangaChapters_Series_Number""
    ON ""MangaChapters"" (""SeriesId"", ""Number"");
CREATE INDEX ""IX_MangaChapters_VolumeId""
    ON ""MangaChapters"" (""VolumeId"");

CREATE TABLE ""MangaPages"" (
    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_MangaPages"" PRIMARY KEY,
    ""ChapterId"" TEXT NOT NULL,
    ""PageIndex"" integer NOT NULL,
    ""CachedPath"" TEXT NOT NULL,
    ""MimeType"" TEXT NOT NULL,
    ""SourceEntry"" TEXT NOT NULL,
    CONSTRAINT ""FK_MangaPages_MangaChapters_ChapterId""
        FOREIGN KEY (""ChapterId"") REFERENCES ""MangaChapters"" (""Id"") ON DELETE CASCADE
);

CREATE UNIQUE INDEX ""IX_MangaPages_Chapter_Page""
    ON ""MangaPages"" (""ChapterId"", ""PageIndex"");

CREATE TABLE ""MangaBookmarks"" (
    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_MangaBookmarks"" PRIMARY KEY,
    ""ProfileId"" TEXT NOT NULL,
    ""SeriesId"" TEXT NOT NULL,
    ""ChapterId"" TEXT NOT NULL,
    ""PageIndex"" integer NOT NULL,
    ""Label"" TEXT NULL,
    ""CreatedAt"" TEXT NOT NULL,
    CONSTRAINT ""FK_MangaBookmarks_MangaSeries_SeriesId""
        FOREIGN KEY (""SeriesId"") REFERENCES ""MangaSeries"" (""Id"") ON DELETE CASCADE,
    CONSTRAINT ""FK_MangaBookmarks_MangaChapters_ChapterId""
        FOREIGN KEY (""ChapterId"") REFERENCES ""MangaChapters"" (""Id"") ON DELETE CASCADE
);

CREATE INDEX ""IX_MangaBookmarks_Profile_Series""
    ON ""MangaBookmarks"" (""ProfileId"", ""SeriesId"", ""CreatedAt"");

CREATE TABLE ""MangaProgress"" (
    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_MangaProgress"" PRIMARY KEY,
    ""ProfileId"" TEXT NOT NULL,
    ""SeriesId"" TEXT NOT NULL,
    ""ChapterId"" TEXT NOT NULL,
    ""PageIndex"" integer NOT NULL,
    ""UpdatedAt"" TEXT NOT NULL,
    CONSTRAINT ""FK_MangaProgress_MangaSeries_SeriesId""
        FOREIGN KEY (""SeriesId"") REFERENCES ""MangaSeries"" (""Id"") ON DELETE CASCADE,
    CONSTRAINT ""FK_MangaProgress_MangaChapters_ChapterId""
        FOREIGN KEY (""ChapterId"") REFERENCES ""MangaChapters"" (""Id"") ON DELETE CASCADE
);

CREATE UNIQUE INDEX ""IX_MangaProgress_Profile_Series""
    ON ""MangaProgress"" (""ProfileId"", ""SeriesId"");
CREATE INDEX ""IX_MangaProgress_Profile_Updated""
    ON ""MangaProgress"" (""ProfileId"", ""UpdatedAt"");

CREATE TABLE ""MediaArtworkAssets"" (
    ""Scope"" TEXT NOT NULL,
    ""OwnerId"" TEXT NOT NULL,
    ""SeasonNumber"" integer NOT NULL,
    ""Kind"" TEXT NOT NULL,
    ""FileName"" TEXT NOT NULL,
    ""Source"" TEXT NOT NULL,
    ""SourceIdentity"" TEXT NULL,
    ""FileLength"" bigint NOT NULL,
    ""FileLastWriteTimeUtc"" TEXT NOT NULL,
    ""UpdatedAt"" TEXT NOT NULL,
    CONSTRAINT ""PK_MediaArtworkAssets"" PRIMARY KEY (""Scope"", ""OwnerId"", ""SeasonNumber"", ""Kind"")
);


CREATE TABLE ""MediaRelations"" (
    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_MediaRelations"" PRIMARY KEY,
    ""FromMediaType"" TEXT NOT NULL,
    ""FromProvider"" TEXT NOT NULL,
    ""FromExternalId"" TEXT NOT NULL,
    ""ToMediaType"" TEXT NOT NULL,
    ""ToProvider"" TEXT NOT NULL,
    ""ToExternalId"" TEXT NOT NULL,
    ""RelationType"" TEXT NOT NULL,
    ""Source"" TEXT NOT NULL,
    ""Confidence"" double precision NOT NULL,
    ""ReviewState"" TEXT NOT NULL,
    ""IsManual"" integer NOT NULL,
    ""UpdatedAtUtc"" TEXT NOT NULL,
    CONSTRAINT ""AK_MediaRelations_Direction"" UNIQUE (""FromMediaType"", ""FromProvider"", ""FromExternalId"", ""ToMediaType"", ""ToProvider"", ""ToExternalId"", ""RelationType"")
);

CREATE INDEX ""IX_MediaRelations_From"" ON ""MediaRelations"" (""FromMediaType"", ""FromProvider"", ""FromExternalId"");
CREATE INDEX ""IX_MediaRelations_To"" ON ""MediaRelations"" (""ToMediaType"", ""ToProvider"", ""ToExternalId"");
CREATE INDEX ""IX_MediaRelations_ReviewState"" ON ""MediaRelations"" (""ReviewState"");

CREATE TABLE ""Notifications"" (
    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_Notifications"" PRIMARY KEY,
    ""ProfileId"" TEXT NOT NULL,
    ""EventId"" TEXT NOT NULL,
    ""Category"" integer NOT NULL,
    ""Severity"" integer NOT NULL,
    ""MediaType"" TEXT NULL,
    ""SubjectId"" TEXT NULL,
    ""MessageParamsJson"" TEXT NULL,
    ""DeepLink"" TEXT NULL,
    ""DedupKey"" TEXT NULL,
    ""OccurrenceCount"" integer NOT NULL DEFAULT 1,
    ""CreatedAtUtc"" TEXT NOT NULL,
    ""UpdatedAtUtc"" TEXT NOT NULL,
    ""ReadAtUtc"" TEXT NULL
);

CREATE INDEX ""IX_Notifications_ProfileId_UpdatedAtUtc"" ON ""Notifications"" (""ProfileId"", ""UpdatedAtUtc"");
CREATE UNIQUE INDEX ""IX_Notifications_ProfileId_DedupKey"" ON ""Notifications"" (""ProfileId"", ""DedupKey"") WHERE ""DedupKey"" IS NOT NULL;

CREATE TABLE ""NotificationSubscriptions"" (
    ""ProfileId"" TEXT NOT NULL,
    ""Category"" integer NOT NULL,
    ""Mode"" integer NOT NULL,
    ""UpdatedAtUtc"" TEXT NOT NULL,
    CONSTRAINT ""PK_NotificationSubscriptions"" PRIMARY KEY (""ProfileId"", ""Category"")
);


CREATE TABLE ""Operations"" (
    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_Operations"" PRIMARY KEY,
    ""Kind"" TEXT NOT NULL,
    ""Category"" TEXT NOT NULL,
    ""Lane"" bigint NOT NULL,
    ""Status"" bigint NOT NULL,
    ""ProfileId"" TEXT NULL,
    ""Title"" TEXT NOT NULL,
    ""Subject"" TEXT NULL,
    ""ProgressPercent"" bigint NULL,
    ""Message"" TEXT NULL,
    ""Error"" TEXT NULL,
    ""IsDownload"" bigint NOT NULL DEFAULT 0,
    ""BytesTotal"" bigint NULL,
    ""BytesCompleted"" bigint NULL,
    ""BytesPerSecond"" double precision NULL,
    ""EtaUtc"" TEXT NULL,
    ""Attempt"" bigint NOT NULL DEFAULT 1,
    ""Retryable"" bigint NOT NULL DEFAULT 1,
    ""CreatedAtUtc"" TEXT NOT NULL,
    ""StartedAtUtc"" TEXT NULL,
    ""FinishedAtUtc"" TEXT NULL,
    ""UpdatedAtUtc"" TEXT NOT NULL
, ""ExternalProvider"" TEXT NULL, ""ExternalId"" TEXT NULL, ""Details"" TEXT NULL);

CREATE INDEX ""IX_Operations_Status_UpdatedAtUtc""
    ON ""Operations"" (""Status"", ""UpdatedAtUtc"");
CREATE INDEX ""IX_Operations_Lane_Status""
    ON ""Operations"" (""Lane"", ""Status"");
CREATE INDEX ""IX_Operations_ProfileId_UpdatedAtUtc""
    ON ""Operations"" (""ProfileId"", ""UpdatedAtUtc"");
CREATE INDEX ""IX_Operations_IsDownload_Status""
    ON ""Operations"" (""IsDownload"", ""Status"");
CREATE INDEX ""IX_Operations_ExternalProvider_ExternalId""
    ON ""Operations"" (""ExternalProvider"", ""ExternalId"");

CREATE TABLE ""OperationLogs"" (
    ""Id"" bigint GENERATED BY DEFAULT AS IDENTITY NOT NULL CONSTRAINT ""PK_OperationLogs"" PRIMARY KEY,
    ""OperationId"" TEXT NOT NULL,
    ""CreatedAtUtc"" TEXT NOT NULL,
    ""Level"" bigint NOT NULL,
    ""Module"" TEXT NOT NULL,
    ""Message"" TEXT NOT NULL,
    CONSTRAINT ""FK_OperationLogs_Operations_OperationId""
        FOREIGN KEY (""OperationId"") REFERENCES ""Operations"" (""Id"") ON DELETE CASCADE
);

CREATE INDEX ""IX_OperationLogs_OperationId_Id""
    ON ""OperationLogs"" (""OperationId"", ""Id"");
CREATE INDEX ""IX_OperationLogs_Level_Id""
    ON ""OperationLogs"" (""Level"", ""Id"");

CREATE TABLE ""PresentationGroups"" (
    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_PresentationGroups"" PRIMARY KEY,
    ""MediaType"" TEXT NOT NULL CHECK (""MediaType"" IN ('anime', 'novel', 'manga', 'book')),
    ""WorkId"" TEXT NOT NULL,
    ""Name"" TEXT NOT NULL,
    ""SortOrder"" bigint NOT NULL,
    ""CreatedAt"" TEXT NOT NULL,
    ""UpdatedAt"" TEXT NOT NULL
);

CREATE INDEX ""IX_PresentationGroups_Work""
    ON ""PresentationGroups"" (""MediaType"", ""WorkId"", ""SortOrder"");

CREATE TABLE ""PresentationGroupRanges"" (
    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_PresentationGroupRanges"" PRIMARY KEY,
    ""GroupId"" TEXT NOT NULL,
    ""StartUnit"" bigint NOT NULL,
    ""EndUnit"" bigint NOT NULL,
    ""SortOrder"" bigint NOT NULL,
    CONSTRAINT ""FK_PresentationGroupRanges_Group"" FOREIGN KEY (""GroupId"")
        REFERENCES ""PresentationGroups"" (""Id"") ON DELETE CASCADE
);

CREATE INDEX ""IX_PresentationGroupRanges_Group""
    ON ""PresentationGroupRanges"" (""GroupId"", ""SortOrder"");

CREATE TABLE ""ProfileWatchlistPreferences"" (
    ""ProfileId"" TEXT NOT NULL,
    ""MediaType"" TEXT NOT NULL,
    ""Provider"" TEXT NOT NULL,
    ""ExternalId"" TEXT NOT NULL,
    ""FollowState"" TEXT NOT NULL,
    ""Title"" TEXT NOT NULL,
    ""NativeTitle"" TEXT NULL,
    ""CoverImageUrl"" TEXT NULL,
    ""Format"" TEXT NULL,
    ""Status"" TEXT NULL,
    ""Year"" integer NULL,
    ""LocalMediaId"" TEXT NULL,
    ""DetailsUrl"" TEXT NULL,
    ""UpdatedAtUtc"" TEXT NOT NULL,
    CONSTRAINT ""PK_ProfileWatchlistPreferences"" PRIMARY KEY (""ProfileId"", ""MediaType"", ""Provider"", ""ExternalId"")
);

CREATE INDEX ""IX_ProfileWatchlistPreferences_ProfileState"" ON ""ProfileWatchlistPreferences"" (""ProfileId"", ""FollowState"");

CREATE TABLE ""ProviderRoleAssignments"" (
    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_ProviderRoleAssignments"" PRIMARY KEY,
    ""MediaType"" TEXT NOT NULL,
    ""WorkId"" TEXT NOT NULL,
    ""Role"" TEXT NOT NULL,
    ""Provider"" TEXT NOT NULL,
    ""UpdatedAt"" TEXT NOT NULL
);

CREATE UNIQUE INDEX ""IX_ProviderRoleAssignments_Scope""
    ON ""ProviderRoleAssignments"" (""MediaType"", ""WorkId"", ""Role"");

CREATE TABLE ""ReleaseCalendarSources"" (
    ""Provider"" TEXT NOT NULL,
    ""ExternalId"" TEXT NOT NULL,
    ""ProviderStatus"" TEXT NULL,
    ""RefreshedAt"" TEXT NULL,
    ""LastAttemptAt"" TEXT NULL,
    ""LastError"" TEXT NULL,
    CONSTRAINT ""PK_ReleaseCalendarSources"" PRIMARY KEY (""Provider"", ""ExternalId"")
);


CREATE TABLE ""ReleaseCalendarEntries"" (
    ""Provider"" TEXT NOT NULL,
    ""ExternalId"" TEXT NOT NULL,
    ""Kind"" TEXT NOT NULL CHECK (""Kind"" IN (
        'episode', 'seasonPremiere', 'seriesStart', 'chapter', 'volume', 'publication',
        'cinema', 'digital', 'streaming', 'physical')),
    ""UnitNumber"" integer NOT NULL DEFAULT 0,
    ""DateValue"" TEXT NOT NULL,
    ""Precision"" TEXT NOT NULL CHECK (""Precision"" IN ('unknown', 'year', 'quarter', 'month', 'day', 'dateTime')),
    ""RangeStart"" TEXT NULL,
    ""RangeEnd"" TEXT NULL,
    ""FetchedAt"" TEXT NOT NULL,
    CONSTRAINT ""PK_ReleaseCalendarEntries"" PRIMARY KEY (""Provider"", ""ExternalId"", ""Kind"", ""UnitNumber""),
    CONSTRAINT ""FK_ReleaseCalendarEntries_Sources"" FOREIGN KEY (""Provider"", ""ExternalId"")
        REFERENCES ""ReleaseCalendarSources"" (""Provider"", ""ExternalId"") ON DELETE CASCADE
);

CREATE INDEX ""IX_ReleaseCalendarEntries_Range"" ON ""ReleaseCalendarEntries"" (""RangeStart"", ""RangeEnd"");

CREATE TABLE ""UiLocales"" (
    ""Locale"" TEXT NOT NULL CONSTRAINT ""PK_UiLocales"" PRIMARY KEY,
    ""EnglishName"" TEXT NOT NULL,
    ""NativeName"" TEXT NOT NULL,
    ""Direction"" TEXT NOT NULL,
    ""IsEnabled"" integer NOT NULL DEFAULT 1,
    ""IsSource"" integer NOT NULL DEFAULT 0,
    ""CreatedAt"" TEXT NOT NULL,
    ""UpdatedAt"" TEXT NOT NULL
);


CREATE TABLE ""UiTranslationMessages"" (
    ""Key"" TEXT NOT NULL CONSTRAINT ""PK_UiTranslationMessages"" PRIMARY KEY,
    ""DefaultText"" TEXT NOT NULL,
    ""Feature"" TEXT NOT NULL,
    ""Surface"" TEXT NOT NULL,
    ""Description"" TEXT NOT NULL,
    ""Tone"" TEXT NOT NULL,
    ""MaxLength"" integer NULL,
    ""PlaceholdersJson"" TEXT NOT NULL,
    ""DoNotTranslateJson"" TEXT NOT NULL,
    ""SourceHash"" TEXT NOT NULL,
    ""SourceVersion"" integer NOT NULL DEFAULT 1,
    ""UpdatedAt"" TEXT NOT NULL
);


CREATE TABLE ""UiTranslations"" (
    ""Locale"" TEXT NOT NULL,
    ""MessageKey"" TEXT NOT NULL,
    ""Text"" TEXT NOT NULL,
    ""Status"" TEXT NOT NULL,
    ""SourceHash"" TEXT NOT NULL,
    ""Provider"" TEXT NULL,
    ""Model"" TEXT NULL,
    ""PromptVersion"" TEXT NULL,
    ""GeneratedAt"" TEXT NULL,
    ""ReviewedAt"" TEXT NULL,
    ""UpdatedAt"" TEXT NOT NULL,
    CONSTRAINT ""PK_UiTranslations"" PRIMARY KEY (""Locale"", ""MessageKey""),
    CONSTRAINT ""FK_UiTranslations_UiLocales_Locale""
        FOREIGN KEY (""Locale"") REFERENCES ""UiLocales"" (""Locale"") ON DELETE CASCADE,
    CONSTRAINT ""FK_UiTranslations_UiTranslationMessages_MessageKey""
        FOREIGN KEY (""MessageKey"") REFERENCES ""UiTranslationMessages"" (""Key"") ON DELETE CASCADE
);

CREATE INDEX ""IX_UiTranslations_MessageKey""
    ON ""UiTranslations"" (""MessageKey"");
CREATE INDEX ""IX_UiTranslations_Locale_Status""
    ON ""UiTranslations"" (""Locale"", ""Status"");

CREATE TABLE ""UiProfileLocales"" (
    ""ProfileId"" TEXT NOT NULL CONSTRAINT ""PK_UiProfileLocales"" PRIMARY KEY,
    ""Locale"" TEXT NOT NULL,
    ""UpdatedAt"" TEXT NOT NULL,
    CONSTRAINT ""FK_UiProfileLocales_UiLocales_Locale""
        FOREIGN KEY (""Locale"") REFERENCES ""UiLocales"" (""Locale"") ON DELETE RESTRICT
);

CREATE INDEX ""IX_UiProfileLocales_Locale""
    ON ""UiProfileLocales"" (""Locale"");

CREATE TABLE ""UiProfileThemes"" (
    ""ProfileId"" TEXT NOT NULL CONSTRAINT ""PK_UiProfileThemes"" PRIMARY KEY,
    ""ThemeMode"" TEXT NOT NULL DEFAULT 'system'
        CHECK (""ThemeMode"" IN ('system', 'light', 'dark')),
    ""UpdatedAt"" TEXT NOT NULL
, ""AccentColor"" TEXT NULL
    CHECK (""AccentColor"" IS NULL OR ""AccentColor"" ~ '^#[0-9a-f]{6}$'), ""SakuraMode"" TEXT NOT NULL DEFAULT 'subtle'
    CHECK (""SakuraMode"" IN ('off', 'subtle', 'full')), ""ThemeId"" TEXT NULL);



-- #570 full-text + fuzzy search: pg_trgm plus generated tsvector/normalized-text columns and GIN
-- indexes over the title metadata of every searchable media type. The columns are outside the EF
-- model (EF ignores them); they stay in sync automatically as STORED generated columns.
CREATE EXTENSION IF NOT EXISTS pg_trgm;

ALTER TABLE ""AnimeMetadata""
    ADD COLUMN IF NOT EXISTS ""SearchText"" text GENERATED ALWAYS AS (
        lower(
            coalesce(""PreferredTitle"", '') || ' ' || coalesce(""RomajiTitle"", '') || ' ' ||
            coalesce(""EnglishTitle"", '') || ' ' || coalesce(""NativeTitle"", ''))) STORED,
    ADD COLUMN IF NOT EXISTS ""SearchVector"" tsvector GENERATED ALWAYS AS (
        to_tsvector('simple',
            coalesce(""PreferredTitle"", '') || ' ' || coalesce(""RomajiTitle"", '') || ' ' ||
            coalesce(""EnglishTitle"", '') || ' ' || coalesce(""NativeTitle"", ''))) STORED;
CREATE INDEX IF NOT EXISTS ""IX_AnimeMetadata_SearchVector"" ON ""AnimeMetadata"" USING gin (""SearchVector"");
CREATE INDEX IF NOT EXISTS ""IX_AnimeMetadata_SearchText_Trgm"" ON ""AnimeMetadata"" USING gin (""SearchText"" gin_trgm_ops);

ALTER TABLE ""NovelWorks""
    ADD COLUMN IF NOT EXISTS ""SearchText"" text GENERATED ALWAYS AS (
        lower(
            coalesce(""Title"", '') || ' ' || coalesce(""Author"", '') || ' ' ||
            coalesce(""MetadataTitle"", '') || ' ' || coalesce(""MetadataNativeTitle"", ''))) STORED,
    ADD COLUMN IF NOT EXISTS ""SearchVector"" tsvector GENERATED ALWAYS AS (
        to_tsvector('simple',
            coalesce(""Title"", '') || ' ' || coalesce(""Author"", '') || ' ' ||
            coalesce(""MetadataTitle"", '') || ' ' || coalesce(""MetadataNativeTitle"", ''))) STORED;
CREATE INDEX IF NOT EXISTS ""IX_NovelWorks_SearchVector"" ON ""NovelWorks"" USING gin (""SearchVector"");
CREATE INDEX IF NOT EXISTS ""IX_NovelWorks_SearchText_Trgm"" ON ""NovelWorks"" USING gin (""SearchText"" gin_trgm_ops);

ALTER TABLE ""MangaSeries""
    ADD COLUMN IF NOT EXISTS ""SearchText"" text GENERATED ALWAYS AS (
        lower(
            coalesce(""Title"", '') || ' ' || coalesce(""MetadataTitle"", '') || ' ' ||
            coalesce(""MetadataNativeTitle"", ''))) STORED,
    ADD COLUMN IF NOT EXISTS ""SearchVector"" tsvector GENERATED ALWAYS AS (
        to_tsvector('simple',
            coalesce(""Title"", '') || ' ' || coalesce(""MetadataTitle"", '') || ' ' ||
            coalesce(""MetadataNativeTitle"", ''))) STORED;
CREATE INDEX IF NOT EXISTS ""IX_MangaSeries_SearchVector"" ON ""MangaSeries"" USING gin (""SearchVector"");
CREATE INDEX IF NOT EXISTS ""IX_MangaSeries_SearchText_Trgm"" ON ""MangaSeries"" USING gin (""SearchText"" gin_trgm_ops);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

            // Drop the raw-SQL tables created by the raw Sql block in Up().
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""UiProfileThemes"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""UiProfileLocales"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""UiTranslations"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""UiTranslationMessages"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""UiLocales"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""ReleaseCalendarEntries"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""ReleaseCalendarSources"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""ProviderRoleAssignments"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""ProfileWatchlistPreferences"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""PresentationGroupRanges"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""PresentationGroups"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""OperationLogs"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""Operations"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""NotificationSubscriptions"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""Notifications"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""MediaRelations"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""MediaArtworkAssets"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""MangaProgress"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""MangaBookmarks"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""MangaPages"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""MangaChapters"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""MangaVolumes"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""MangaSeries"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""LearningScopeModes"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""LearningCapabilityOverrides"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""KnownDevices"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""InstanceAppearanceSettings"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""ProfileFranchiseFollows"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""FranchiseMembers"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""Franchises"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""Events"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""ChapterArtworks"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""ChapterArtworkWorkSettings"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""ChapterArtworkPreferences"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""AnimeMappingAuditEntries"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""AiUsageDaily"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""AiModelCatalogs"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""AcquisitionRequests"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""AcquisitionAccessPolicies"" CASCADE;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""AccountSessionStates"" CASCADE;");
            migrationBuilder.DropTable(
                name: "AcquisitionApiKeys");

            migrationBuilder.DropTable(
                name: "AcquisitionHistory");

            migrationBuilder.DropTable(
                name: "AiSentenceExplanationCache");

            migrationBuilder.DropTable(
                name: "AnimeLocalMetadata");

            migrationBuilder.DropTable(
                name: "AnimeMetadata");

            migrationBuilder.DropTable(
                name: "BookFiles");

            migrationBuilder.DropTable(
                name: "EpisodeMediaSegments");

            migrationBuilder.DropTable(
                name: "EpisodePlaybackHistory");

            migrationBuilder.DropTable(
                name: "EpisodeProgress");

            migrationBuilder.DropTable(
                name: "EpisodeSegmentDetectionStates");

            migrationBuilder.DropTable(
                name: "EpisodeTerms");

            migrationBuilder.DropTable(
                name: "LearningCardReviews");

            migrationBuilder.DropTable(
                name: "LearningContexts");

            migrationBuilder.DropTable(
                name: "LearningPreferences");

            migrationBuilder.DropTable(
                name: "LearningVariants");

            migrationBuilder.DropTable(
                name: "MediaAnalysisStreams");

            migrationBuilder.DropTable(
                name: "NovelAnimeMappings");

            migrationBuilder.DropTable(
                name: "NovelBookmarks");

            migrationBuilder.DropTable(
                name: "NovelBookmarkTombstones");

            migrationBuilder.DropTable(
                name: "NovelHighlights");

            migrationBuilder.DropTable(
                name: "NovelProgress");

            migrationBuilder.DropTable(
                name: "NovelTranslations");

            migrationBuilder.DropTable(
                name: "OwnerAccounts");

            migrationBuilder.DropTable(
                name: "ProfilePlaybackPreferences");

            migrationBuilder.DropTable(
                name: "ReaderPreferences");

            migrationBuilder.DropTable(
                name: "SubtitleCues");

            migrationBuilder.DropTable(
                name: "SubtitleLanguageProfileItems");

            migrationBuilder.DropTable(
                name: "SubtitleProfileAssignments");

            migrationBuilder.DropTable(
                name: "BookEditions");

            migrationBuilder.DropTable(
                name: "LearningCards");

            migrationBuilder.DropTable(
                name: "MediaAnalyses");

            migrationBuilder.DropTable(
                name: "NovelChapters");

            migrationBuilder.DropTable(
                name: "SubtitleTracks");

            migrationBuilder.DropTable(
                name: "SubtitleLanguageProfiles");

            migrationBuilder.DropTable(
                name: "LearningCourses");

            migrationBuilder.DropTable(
                name: "LearningUnits");

            migrationBuilder.DropTable(
                name: "MediaFiles");

            migrationBuilder.DropTable(
                name: "NovelVolumes");

            migrationBuilder.DropTable(
                name: "Terms");

            migrationBuilder.DropTable(
                name: "Episodes");

            migrationBuilder.DropTable(
                name: "LibraryRoots");

            migrationBuilder.DropTable(
                name: "NovelWorks");

            migrationBuilder.DropTable(
                name: "Anime");
        }
    }
}
