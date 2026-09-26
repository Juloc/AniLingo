using AniLingo.Web.Features.Ai;
using AniLingo.Web.Features.ReaderPreferences;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Books;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Learning.Courses;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.MediaSegments;
using AniLingo.Web.Features.Metadata;
using AniLingo.Web.Features.Novels;
using AniLingo.Web.Features.OfflineLibrary;
using AniLingo.Web.Features.Progress;
using AniLingo.Web.Features.Subtitles;
using AniLingo.Web.Features.Vocabulary;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<LibraryRoot> LibraryRoots => Set<LibraryRoot>();
    public DbSet<Anime> Anime => Set<Anime>();
    public DbSet<Episode> Episodes => Set<Episode>();
    public DbSet<MediaFile> MediaFiles => Set<MediaFile>();
    public DbSet<MediaAnalysis> MediaAnalyses => Set<MediaAnalysis>();
    public DbSet<MediaAnalysisStream> MediaAnalysisStreams => Set<MediaAnalysisStream>();
    public DbSet<AnimeMetadata> AnimeMetadata => Set<AnimeMetadata>();
    public DbSet<AnimeLocalMetadata> AnimeLocalMetadata => Set<AnimeLocalMetadata>();
    public DbSet<SubtitleTrack> SubtitleTracks => Set<SubtitleTrack>();
    public DbSet<SubtitleCue> SubtitleCues => Set<SubtitleCue>();
    public DbSet<Term> Terms => Set<Term>();
    public DbSet<EpisodeTerm> EpisodeTerms => Set<EpisodeTerm>();
    public DbSet<LearningUnit> LearningUnits => Set<LearningUnit>();
    public DbSet<LearningVariant> LearningVariants => Set<LearningVariant>();
    public DbSet<LearningCourse> LearningCourses => Set<LearningCourse>();
    public DbSet<LearningCard> LearningCards => Set<LearningCard>();
    public DbSet<LearningCardReview> LearningCardReviews => Set<LearningCardReview>();
    public DbSet<LearningContext> LearningContexts => Set<LearningContext>();
    public DbSet<LearningPreferences> LearningPreferences => Set<LearningPreferences>();
    public DbSet<AiSentenceExplanationCache> AiSentenceExplanationCache => Set<AiSentenceExplanationCache>();
    public DbSet<OwnerAccount> OwnerAccounts => Set<OwnerAccount>();
    public DbSet<EpisodeProgress> EpisodeProgress => Set<EpisodeProgress>();
    public DbSet<EpisodePlaybackHistoryEntry> EpisodePlaybackHistory => Set<EpisodePlaybackHistoryEntry>();
    public DbSet<ProfilePlaybackPreferences> ProfilePlaybackPreferences => Set<ProfilePlaybackPreferences>();
    public DbSet<NovelWork> NovelWorks => Set<NovelWork>();
    public DbSet<BookEdition> BookEditions => Set<BookEdition>();
    public DbSet<BookFile> BookFiles => Set<BookFile>();
    public DbSet<NovelVolume> NovelVolumes => Set<NovelVolume>();
    public DbSet<NovelChapter> NovelChapters => Set<NovelChapter>();
    public DbSet<NovelTranslation> NovelTranslations => Set<NovelTranslation>();
    public DbSet<NovelProgress> NovelProgress => Set<NovelProgress>();
    public DbSet<NovelBookmark> NovelBookmarks => Set<NovelBookmark>();
    public DbSet<NovelHighlight> NovelHighlights => Set<NovelHighlight>();
    public DbSet<NovelBookmarkTombstone> NovelBookmarkTombstones => Set<NovelBookmarkTombstone>();
    public DbSet<NovelAnimeMapping> NovelAnimeMappings => Set<NovelAnimeMapping>();
    public DbSet<ReaderPreference> ReaderPreferences => Set<ReaderPreference>();
    public DbSet<EpisodeMediaSegment> EpisodeMediaSegments => Set<EpisodeMediaSegment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OwnerAccount>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(32);
            entity.Property(x => x.UserName).HasMaxLength(80);
            entity.Property(x => x.NormalizedUserName).HasMaxLength(80);
            entity.Property(x => x.PasswordHash).HasMaxLength(1024);
            entity.Property(x => x.Role).HasConversion<int>();
            entity.HasIndex(x => x.NormalizedUserName).IsUnique();
        });

        modelBuilder.Entity<LibraryRoot>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.Property(x => x.Path).HasMaxLength(2048);
            entity.Property(x => x.WakeMacAddress).HasMaxLength(32);
            entity.Property(x => x.WakeBroadcastAddress).HasMaxLength(64);
            entity.HasIndex(x => x.Path).IsUnique();
        });

        modelBuilder.Entity<Anime>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Key).HasMaxLength(300);
            entity.Property(x => x.Title).HasMaxLength(300);
            entity.HasIndex(x => x.Key).IsUnique();
        });

        modelBuilder.Entity<AnimeMetadata>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Provider).HasMaxLength(80);
            entity.Property(x => x.ExternalId).HasMaxLength(200);
            entity.Property(x => x.PreferredTitle).HasMaxLength(500);
            entity.Property(x => x.RomajiTitle).HasMaxLength(500);
            entity.Property(x => x.EnglishTitle).HasMaxLength(500);
            entity.Property(x => x.NativeTitle).HasMaxLength(500);
            entity.Property(x => x.CoverImageUrl).HasMaxLength(2048);
            entity.Property(x => x.BannerImageUrl).HasMaxLength(2048);
            entity.Property(x => x.Format).HasMaxLength(80);
            entity.Property(x => x.Status).HasMaxLength(80);
            entity.Property(x => x.Season).HasMaxLength(80);
            entity.HasOne<Anime>().WithOne().HasForeignKey<AnimeMetadata>(x => x.AnimeId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.AnimeId).IsUnique();
            entity.HasIndex(x => new { x.Provider, x.ExternalId }).IsUnique();
        });

        modelBuilder.Entity<AnimeLocalMetadata>(entity =>
        {
            entity.HasKey(x => x.AnimeId);
            entity.Property(x => x.Source).HasMaxLength(20);
            entity.Property(x => x.OriginalTitle).HasMaxLength(NfoReader.MaxTitleLength);
            entity.Property(x => x.MyAnimeListId).HasMaxLength(10);
            entity.Property(x => x.TvdbId).HasMaxLength(10);
            entity.Property(x => x.TmdbId).HasMaxLength(10);
            entity.Property(x => x.ImdbId).HasMaxLength(12);
            entity.HasOne<Anime>().WithOne().HasForeignKey<AnimeLocalMetadata>(x => x.AnimeId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Episode>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(500);
            entity.HasOne<Anime>().WithMany().HasForeignKey(x => x.AnimeId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.AnimeId, x.SeasonNumber, x.Number }).IsUnique();
        });

        modelBuilder.Entity<MediaFile>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Path).HasMaxLength(2048);
            entity.HasOne<LibraryRoot>().WithMany().HasForeignKey(x => x.LibraryRootId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Episode>().WithMany().HasForeignKey(x => x.EpisodeId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.Path).IsUnique();
            entity.HasIndex(x => new { x.LibraryRootId, x.EpisodeId });
        });

        modelBuilder.Entity<MediaAnalysis>(entity =>
        {
            entity.HasKey(x => x.MediaFileId);
            entity.Property(x => x.Status).HasConversion<int>();
            entity.Property(x => x.SourceFingerprint).HasMaxLength(64);
            entity.Property(x => x.Diagnostic).HasMaxLength(MediaInventoryService.DiagnosticMaxLength);
            entity.Property(x => x.Container).HasMaxLength(120);
            entity.Property(x => x.VideoCodec).HasMaxLength(64);
            entity.Property(x => x.VideoProfile).HasMaxLength(80);
            entity.Property(x => x.PixelFormat).HasMaxLength(40);
            entity.Property(x => x.DynamicRange).HasMaxLength(24);
            entity.HasOne<MediaFile>().WithOne().HasForeignKey<MediaAnalysis>(x => x.MediaFileId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.Status, x.ProbeVersion });
        });

        modelBuilder.Entity<MediaAnalysisStream>(entity =>
        {
            entity.HasKey(x => new { x.MediaFileId, x.StreamIndex });
            entity.Property(x => x.Kind).HasConversion<int>();
            entity.Property(x => x.Codec).HasMaxLength(64);
            entity.Property(x => x.Language).HasMaxLength(32);
            entity.Property(x => x.Title).HasMaxLength(300);
            entity.Property(x => x.ChannelLayout).HasMaxLength(64);
            entity.HasOne<MediaAnalysis>().WithMany().HasForeignKey(x => x.MediaFileId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SubtitleTrack>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Path).HasMaxLength(2048);
            entity.Property(x => x.Language).HasMaxLength(16);
            entity.Property(x => x.Format).HasMaxLength(16);
            entity.HasOne<Episode>().WithMany().HasForeignKey(x => x.EpisodeId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.Path).IsUnique();
            entity.HasIndex(x => new { x.EpisodeId, x.Language });
        });

        modelBuilder.Entity<SubtitleCue>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne<SubtitleTrack>().WithMany().HasForeignKey(x => x.SubtitleTrackId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.SubtitleTrackId, x.StartMs });
        });

        modelBuilder.Entity<Term>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Language).HasMaxLength(16);
            entity.Property(x => x.Canonical).HasMaxLength(300);
            entity.Property(x => x.Reading).HasMaxLength(300);
            entity.Property(x => x.Meaning).HasMaxLength(1000);
            entity.HasIndex(x => new { x.Language, x.Canonical }).IsUnique();
        });

        modelBuilder.Entity<EpisodeTerm>(entity =>
        {
            entity.HasKey(x => new { x.EpisodeId, x.TermId });
            entity.HasOne<Episode>().WithMany().HasForeignKey(x => x.EpisodeId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Term>().WithMany().HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.TermId);
        });

        modelBuilder.Entity<EpisodeProgress>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProfileId).HasMaxLength(80);
            entity.HasOne<Episode>().WithMany().HasForeignKey(x => x.EpisodeId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.ProfileId, x.EpisodeId }).IsUnique();
            entity.HasIndex(x => new { x.ProfileId, x.UpdatedAt });
        });

        modelBuilder.Entity<EpisodePlaybackHistoryEntry>(entity =>
        {
            entity.ToTable("EpisodePlaybackHistory");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProfileId).HasMaxLength(80);
            entity.HasOne<Episode>().WithMany().HasForeignKey(x => x.EpisodeId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.ProfileId, x.LastPlayedAt });
        });

        modelBuilder.Entity<ProfilePlaybackPreferences>(entity =>
        {
            entity.HasKey(x => x.ProfileId);
            entity.Property(x => x.ProfileId).HasMaxLength(80);
            entity.Property(x => x.PreferredAudioLanguage).HasMaxLength(16);
            entity.Property(x => x.PreferredSubtitleLanguage).HasMaxLength(16);
            entity.Property(x => x.DefaultPlaybackSpeed).HasDefaultValue(PlaybackPreferenceRules.DefaultSpeed);
        });

        LearningCourseModelConfiguration.Configure(modelBuilder);

        modelBuilder.Entity<LearningPreferences>(entity =>
        {
            entity.HasKey(x => x.ProfileId);
            entity.Property(x => x.ProfileId).HasMaxLength(80);
        });

        modelBuilder.Entity<AiSentenceExplanationCache>(entity =>
        {
            entity.HasKey(x => x.CacheKey);
            entity.Property(x => x.CacheKey).HasMaxLength(64);
            entity.Property(x => x.ProviderId).HasMaxLength(80);
        });

        modelBuilder.Entity<NovelWork>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SourceProvider).HasMaxLength(80);
            entity.Property(x => x.SourceKey).HasMaxLength(80);
            entity.Property(x => x.SourceUrl).HasMaxLength(2048);
            entity.Property(x => x.Title).HasMaxLength(500);
            entity.Property(x => x.Author).HasMaxLength(300);
            entity.Property(x => x.MetadataProvider).HasMaxLength(80);
            entity.Property(x => x.MetadataExternalId).HasMaxLength(200);
            entity.Property(x => x.MetadataTitle).HasMaxLength(500);
            entity.Property(x => x.MetadataNativeTitle).HasMaxLength(500);
            entity.Property(x => x.CoverImageUrl).HasMaxLength(2048);
            entity.Property(x => x.BannerImageUrl).HasMaxLength(2048);
            entity.Property(x => x.Format).HasMaxLength(80);
            entity.Property(x => x.MetadataStatus).HasMaxLength(80);
            entity.Property(x => x.MetadataGenresJson).HasColumnType("TEXT");
            entity.HasIndex(x => new { x.SourceProvider, x.SourceKey }).IsUnique();
            entity.HasIndex(x => new { x.MetadataProvider, x.MetadataExternalId }).IsUnique();
        });

        modelBuilder.Entity<BookEdition>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EditionKey).HasMaxLength(120);
            entity.Property(x => x.Language).HasMaxLength(16);
            entity.Property(x => x.Isbn10).HasMaxLength(10);
            entity.Property(x => x.Isbn13).HasMaxLength(13);
            entity.Property(x => x.Publisher).HasMaxLength(300);
            entity.Property(x => x.PublishedDate).HasMaxLength(80);
            entity.Property(x => x.Title).HasMaxLength(500);
            entity.Property(x => x.Author).HasMaxLength(300);
            entity.Property(x => x.SourceProvider).HasMaxLength(80);
            entity.Property(x => x.SourceExternalId).HasMaxLength(200);
            entity.HasOne<NovelWork>()
                .WithMany()
                .HasForeignKey(x => x.WorkId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.WorkId, x.EditionKey }).IsUnique();
            entity.HasIndex(x => new { x.WorkId, x.IsPrimary });
            entity.HasIndex(x => x.Isbn13);
            entity.HasIndex(x => x.Isbn10);
        });

        modelBuilder.Entity<BookFile>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FileKey).HasMaxLength(120);
            entity.Property(x => x.FileName).HasMaxLength(500);
            entity.Property(x => x.Format).HasMaxLength(32);
            entity.Property(x => x.MediaType).HasMaxLength(120);
            entity.Property(x => x.SourceKind).HasMaxLength(80);
            entity.Property(x => x.SourceUrl).HasMaxLength(2048);
            entity.Property(x => x.ContentHash).HasMaxLength(64);
            entity.Property(x => x.StoragePath).HasMaxLength(2048);
            entity.HasOne<BookEdition>()
                .WithMany()
                .HasForeignKey(x => x.EditionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.EditionId, x.FileKey }).IsUnique();
            entity.HasIndex(x => new { x.EditionId, x.IsPrimary });
            entity.HasIndex(x => x.ContentHash);
        });

        modelBuilder.Entity<NovelVolume>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(500);
            entity.Property(x => x.Kind).HasMaxLength(16);
            entity.Property(x => x.SourceKey).HasMaxLength(200);
            entity.Property(x => x.SourceFileName).HasMaxLength(500);
            entity.Property(x => x.SourceContentHash).HasMaxLength(64);
            entity.Property(x => x.CoverAsset).HasMaxLength(120);
            entity.HasOne<NovelWork>().WithMany().HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.WorkId, x.Number }).IsUnique();
            entity.HasIndex(x => new { x.WorkId, x.SourceKey }).IsUnique();
        });

        modelBuilder.Entity<NovelChapter>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SourceUrl).HasMaxLength(2048);
            entity.Property(x => x.Title).HasMaxLength(500);
            entity.Property(x => x.SourceHash).HasMaxLength(64);
            entity.Property(x => x.ContentJson).HasColumnType("TEXT");
            entity.HasOne<NovelWork>().WithMany().HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<NovelVolume>().WithMany().HasForeignKey(x => x.VolumeId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.WorkId, x.Number }).IsUnique();
        });

        modelBuilder.Entity<NovelTranslation>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TargetLanguage).HasMaxLength(16);
            entity.Property(x => x.ProviderId).HasMaxLength(80);
            entity.Property(x => x.SourceHash).HasMaxLength(64);
            entity.HasOne<NovelChapter>().WithMany().HasForeignKey(x => x.ChapterId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new
            {
                x.ChapterId,
                x.TargetLanguage,
                x.ProviderId,
                x.PromptVersion,
                x.SourceHash
            }).IsUnique();
        });

        modelBuilder.Entity<NovelProgress>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProfileId).HasMaxLength(80);
            entity.Property(x => x.AnchorLanguage).HasMaxLength(16);
            entity.Property(x => x.AnchorText).HasMaxLength(NovelTextLayout.AnchorTextLimit);
            entity.HasOne<NovelWork>().WithMany().HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<NovelChapter>().WithMany().HasForeignKey(x => x.ChapterId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.ProfileId, x.WorkId }).IsUnique();
        });

        modelBuilder.Entity<NovelBookmark>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProfileId).HasMaxLength(80);
            entity.Property(x => x.Language).HasMaxLength(16);
            entity.Property(x => x.AnchorText).HasMaxLength(NovelTextLayout.AnchorTextLimit);
            entity.Property(x => x.Label).HasMaxLength(120);
            entity.Property(x => x.Style).HasMaxLength(24);
            entity.Property(x => x.Color).HasMaxLength(16);
            entity.HasOne<NovelWork>().WithMany().HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<NovelChapter>().WithMany().HasForeignKey(x => x.ChapterId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.ProfileId, x.WorkId, x.CreatedAt });
            entity.HasIndex(x => new { x.ProfileId, x.ChapterId });
        });

        modelBuilder.Entity<NovelBookmarkTombstone>(entity =>
        {
            entity.HasKey(x => x.BookmarkId);
            entity.Property(x => x.ProfileId).HasMaxLength(80);
            entity.HasOne<NovelWork>().WithMany().HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.ProfileId, x.WorkId });
        });

        modelBuilder.Entity<NovelHighlight>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProfileId).HasMaxLength(80);
            entity.Property(x => x.Language).HasMaxLength(16);
            entity.Property(x => x.Text).HasMaxLength(2000);
            entity.Property(x => x.Note).HasMaxLength(2000);
            entity.HasOne<NovelWork>().WithMany().HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<NovelChapter>().WithMany().HasForeignKey(x => x.ChapterId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.ProfileId, x.WorkId, x.CreatedAt });
            entity.HasIndex(x => new { x.ProfileId, x.ChapterId, x.Language, x.ParagraphIndex });
        });

        modelBuilder.Entity<ReaderPreference>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProfileId).HasMaxLength(80);
            entity.Property(x => x.ScopeKey).HasMaxLength(80);
            entity.Property(x => x.ReadingMode).HasMaxLength(24);
            entity.Property(x => x.PageTransition).HasMaxLength(24);
            entity.Property(x => x.FontFamily).HasMaxLength(100);
            entity.Property(x => x.TextAlignment).HasMaxLength(24);
            entity.Property(x => x.ChapterStyle).HasMaxLength(32);
            entity.Property(x => x.PaperStyle).HasMaxLength(32);
            entity.Property(x => x.GenreTheme).HasMaxLength(48);
            entity.Property(x => x.BackgroundAssetId).HasMaxLength(120);
            entity.Property(x => x.BackgroundMotionMode).HasMaxLength(24);
            entity.Property(x => x.BookmarkStyle).HasMaxLength(24);
            entity.Property(x => x.BookmarkColor).HasMaxLength(16);
            entity.Property(x => x.TtsProviderId).HasMaxLength(24);
            entity.Property(x => x.TtsVoiceIds).HasMaxLength(8000);
            entity.HasOne<NovelWork>().WithMany().HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.ProfileId, x.ScopeKey }).IsUnique();
            entity.HasIndex(x => x.WorkId);
        });

        modelBuilder.Entity<NovelAnimeMapping>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AnimeProvider).HasMaxLength(80);
            entity.Property(x => x.AnimeExternalId).HasMaxLength(200);
            entity.Property(x => x.Label).HasMaxLength(200);
            entity.Property(x => x.Source).HasMaxLength(20);
            entity.HasOne<NovelWork>().WithMany().HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.WorkId, x.ChapterStart, x.ChapterEnd });
            entity.HasIndex(x => new { x.AnimeProvider, x.AnimeExternalId });
        });

        modelBuilder.Entity<EpisodeMediaSegment>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Kind).HasConversion<int>();
            entity.Property(x => x.Source).HasConversion<int>();
            entity.Property(x => x.Method).HasMaxLength(80);
            entity.Property(x => x.Version).HasMaxLength(40);
            entity.Property(x => x.MediaIdentity).HasMaxLength(64);
            entity.HasOne<Episode>().WithMany().HasForeignKey(x => x.EpisodeId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.EpisodeId, x.Kind, x.Source }).IsUnique();
        });
    }
}
