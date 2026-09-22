using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Metadata;
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LibraryRoot>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.Property(x => x.Path).HasMaxLength(2048);
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

        modelBuilder.Entity<UserTerm>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProfileId).HasMaxLength(80);
            entity.HasOne<Term>().WithMany().HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.ProfileId, x.TermId }).IsUnique();
            entity.HasIndex(x => new { x.ProfileId, x.State, x.NextReviewAt });
        });

        modelBuilder.Entity<Review>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProfileId).HasMaxLength(80);
            entity.HasOne<Term>().WithMany().HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.ProfileId, x.ReviewedAt });
        });
    }
}
