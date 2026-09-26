using AniLingo.Web.Features.Vocabulary;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Learning.Courses;

public static class LearningCourseModelConfiguration
{
    public const int LanguageTagMaxLength = 35;

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LearningUnit>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
            entity.HasOne<Term>().WithMany().HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(x => x.TermId).IsUnique();
        });

        modelBuilder.Entity<LearningVariant>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.LanguageTag).HasMaxLength(LanguageTagMaxLength);
            entity.Property(x => x.Text).HasMaxLength(1000);
            entity.Property(x => x.Reading).HasMaxLength(300);
            entity.Property(x => x.Role).HasMaxLength(16);
            entity.Property(x => x.SourceKind).HasMaxLength(32);
            entity.HasOne<LearningUnit>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.UnitId, x.LanguageTag, x.Text }).IsUnique();
            entity.HasIndex(x => new { x.LanguageTag, x.Text });
        });

        modelBuilder.Entity<LearningCourse>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProfileId).HasMaxLength(80);
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.Property(x => x.SourceLanguage).HasMaxLength(LanguageTagMaxLength);
            entity.Property(x => x.TargetLanguage).HasMaxLength(LanguageTagMaxLength);
            entity.HasIndex(x => new { x.ProfileId, x.SourceLanguage, x.TargetLanguage }).IsUnique();
            entity.HasIndex(x => new { x.ProfileId, x.SourceLanguage })
                .IsUnique()
                .HasFilter("\"IsPrimary\" = 1");
        });

        modelBuilder.Entity<LearningCard>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProfileId).HasMaxLength(80);
            entity.Property(x => x.PromptLanguage).HasMaxLength(LanguageTagMaxLength);
            entity.Property(x => x.AnswerLanguage).HasMaxLength(LanguageTagMaxLength);
            entity.Property(x => x.Mode).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.State).HasConversion<string>().HasMaxLength(16);
            entity.HasOne<LearningCourse>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<LearningUnit>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.CourseId, x.UnitId, x.Mode }).IsUnique();
            entity.HasIndex(x => new { x.ProfileId, x.State, x.NextReviewAt });
            entity.HasIndex(x => x.UnitId);
        });

        modelBuilder.Entity<LearningCardReview>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProfileId).HasMaxLength(80);
            entity.HasOne<LearningCard>().WithMany().HasForeignKey(x => x.CardId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.CardId, x.ReviewedAt });
            entity.HasIndex(x => new { x.ProfileId, x.ReviewedAt });
            entity.HasIndex(x => new { x.ProfileId, x.ClientEventId }).IsUnique();
        });

        modelBuilder.Entity<LearningContext>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProfileId).HasMaxLength(80);
            entity.Property(x => x.SourceType).HasMaxLength(32);
            entity.Property(x => x.SourceKey).HasMaxLength(200);
            entity.Property(x => x.PositionKey).HasMaxLength(200);
            entity.Property(x => x.LanguageTag).HasMaxLength(LanguageTagMaxLength);
            entity.Property(x => x.Text).HasMaxLength(2000);
            entity.HasOne<LearningUnit>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.UnitId);
            entity.HasIndex(x => new { x.ProfileId, x.SourceType, x.SourceKey });
            entity.HasIndex(x => new { x.ProfileId, x.UnitId, x.SourceType, x.SourceKey, x.PositionKey })
                .IsUnique();
        });
    }
}
