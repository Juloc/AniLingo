using AniLingo.Web.Features.Ai;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Metadata;
using AniLingo.Web.Features.Novels;
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
    public DbSet<AnimeMetadata> AnimeMetadata => Set<AnimeMetadata>();
    public DbSet<SubtitleTrack> SubtitleTracks => Set<SubtitleTrack>();
    public DbSet<SubtitleCue> SubtitleCues => Set<SubtitleCue>();
    public DbSet<Term> Terms => Set<Term>();
    public DbSet<EpisodeTerm> EpisodeTerms => Set<EpisodeTerm>();
    public DbSet<UserTerm> UserTerms => Set<UserTerm>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<LearningPreferences> LearningPreferences => Set<LearningPreferences>();
    public DbSet<AiSentenceExplanationCache> AiSentenceExplanationCache => Set<AiSentenceExplanationCache>();
    public DbSet<OwnerAccount> OwnerAccounts => Set<OwnerAccount>();
    public DbSet<EpisodeProgress> EpisodeProgress => Set<EpisodeProgress>();
    public DbSet<NovelWork> NovelWorks => Set<NovelWork>();
    public DbSet<NovelChapter> NovelChapters => Set<NovelChapter>();
    public DbSet<NovelTranslation> NovelTranslations => Set<NovelTranslation>();
    public DbSet<NovelProgress> NovelProgress => Set<NovelProgress>();
    public DbSet<NovelBookmark> NovelBookmarks => Set<NovelBookmark>();
    public DbSet<NovelHighlight> NovelHighlights => Set<NovelHighlight>();
    public DbSet<NovelAnimeMapping> NovelAnimeMappings => Set<NovelAnimeMapping>();

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

        modelBuilder.Entity<UserTerm>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProfileId).HasMaxLength(80);
            entity.HasOne<Term>().WithMany().HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.ProfileId, x.TermId }).IsUnique();
            entity.HasIndex(x => new { x.ProfileId, x.State, x.NextReviewAt });
            entity.HasIndex(x => new { x.LearningStartedAt, x.QueuePosition });
        });

        modelBuilder.Entity<Review>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProfileId).HasMaxLength(80);
            entity.HasOne<Term>().WithMany().HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.ProfileId, x.ReviewedAt });
            entity.HasIndex(x => new { x.ProfileId, x.ClientEventId }).IsUnique();
        });

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
            entity.HasIndex(x => new { x.SourceProvider, x.SourceKey }).IsUnique();
            entity.HasIndex(x => new { x.MetadataProvider, x.MetadataExternalId }).IsUnique();
        });

        modelBuilder.Entity<NovelChapter>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SourceUrl).HasMaxLength(2048);
            entity.Property(x => x.Title).HasMaxLength(500);
            entity.Property(x => x.SourceHash).HasMaxLength(64);
            entity.HasOne<NovelWork>().WithMany().HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.Cascade);
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
            entity.HasOne<NovelWork>().WithMany().HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<NovelChapter>().WithMany().HasForeignKey(x => x.ChapterId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.ProfileId, x.WorkId, x.CreatedAt });
            entity.HasIndex(x => new { x.ProfileId, x.ChapterId });
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
    }
}
