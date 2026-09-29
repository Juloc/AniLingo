using Jularr.Web.Features.Learning.Courses;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Learning.Curriculum;

/// <summary>
/// EF Core mapping for the Learning v3 curriculum spine: the media-independent
/// blueprint hierarchy, the shared (deduped) course instances a language pair
/// specializes, and each learner's personal variant with its own delta and course
/// progress. Card review state is untouched here — it stays with the v2
/// <see cref="LearningCard"/> model.
/// </summary>
public static class CurriculumModelConfiguration
{
    public const int KeyMaxLength = 120;
    public const int TitleMaxLength = 300;
    public const int FingerprintMaxLength = 64;
    public const int LanguageTagMaxLength = 35;
    public const int ProfileIdMaxLength = 80;

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CurriculumBlueprint>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Key).HasMaxLength(KeyMaxLength);
            entity.Property(x => x.Title).HasMaxLength(TitleMaxLength);
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.ContentFingerprint).HasMaxLength(FingerprintMaxLength);
            entity.HasIndex(x => x.Key).IsUnique();
        });

        modelBuilder.Entity<CurriculumLevel>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Key).HasMaxLength(KeyMaxLength);
            entity.Property(x => x.Title).HasMaxLength(TitleMaxLength);
            entity.HasOne<CurriculumBlueprint>().WithMany().HasForeignKey(x => x.BlueprintId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.BlueprintId, x.Key }).IsUnique();
            entity.HasIndex(x => new { x.BlueprintId, x.Ordinal }).IsUnique();
        });

        modelBuilder.Entity<CurriculumChapter>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Key).HasMaxLength(KeyMaxLength);
            entity.Property(x => x.Title).HasMaxLength(TitleMaxLength);
            entity.HasOne<CurriculumLevel>().WithMany().HasForeignKey(x => x.LevelId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.LevelId, x.Key }).IsUnique();
            entity.HasIndex(x => new { x.LevelId, x.Ordinal }).IsUnique();
        });

        modelBuilder.Entity<CurriculumLesson>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Key).HasMaxLength(KeyMaxLength);
            entity.Property(x => x.Title).HasMaxLength(TitleMaxLength);
            entity.HasOne<CurriculumChapter>().WithMany().HasForeignKey(x => x.ChapterId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.ChapterId, x.Key }).IsUnique();
            entity.HasIndex(x => new { x.ChapterId, x.Ordinal }).IsUnique();
        });

        modelBuilder.Entity<CurriculumExercise>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Key).HasMaxLength(KeyMaxLength);
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.Prompt).HasMaxLength(1000);
            entity.HasOne<CurriculumLesson>().WithMany().HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.LessonId, x.Key }).IsUnique();
            entity.HasIndex(x => new { x.LessonId, x.Ordinal }).IsUnique();
        });

        modelBuilder.Entity<SharedCourseInstance>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SourceLanguage).HasMaxLength(LanguageTagMaxLength);
            entity.Property(x => x.TargetLanguage).HasMaxLength(LanguageTagMaxLength);
            entity.Property(x => x.ContentFingerprint).HasMaxLength(FingerprintMaxLength);
            entity.Property(x => x.Title).HasMaxLength(TitleMaxLength);
            entity.HasOne<CurriculumBlueprint>().WithMany().HasForeignKey(x => x.BlueprintId).OnDelete(DeleteBehavior.Restrict);
            // The fingerprint is the dedup key: one shared instance per specialized content.
            entity.HasIndex(x => x.ContentFingerprint).IsUnique();
            entity.HasIndex(x => new { x.BlueprintId, x.SourceLanguage, x.TargetLanguage });
        });

        modelBuilder.Entity<LearnerCourse>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProfileId).HasMaxLength(ProfileIdMaxLength);
            entity.Property(x => x.DisplayNameOverride).HasMaxLength(TitleMaxLength);
            entity.HasOne<SharedCourseInstance>().WithMany().HasForeignKey(x => x.SharedInstanceId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<LearningCourse>().WithMany().HasForeignKey(x => x.LearningCourseId).OnDelete(DeleteBehavior.SetNull);
            // A profile enrolls at most once per shared instance.
            entity.HasIndex(x => new { x.ProfileId, x.SharedInstanceId }).IsUnique();
            entity.HasIndex(x => x.ProfileId);
        });

        modelBuilder.Entity<LearnerCourseItemDelta>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ItemType).HasConversion<string>().HasMaxLength(16);
            entity.HasOne<LearnerCourse>().WithMany().HasForeignKey(x => x.LearnerCourseId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.LearnerCourseId, x.ItemType, x.ItemId }).IsUnique();
        });

        modelBuilder.Entity<LearnerCourseProgress>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ItemType).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            entity.HasOne<LearnerCourse>().WithMany().HasForeignKey(x => x.LearnerCourseId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.LearnerCourseId, x.ItemType, x.ItemId }).IsUnique();
            entity.HasIndex(x => new { x.LearnerCourseId, x.Status });
        });
    }
}
