using AniLingo.Web.Data;
using AniLingo.Web.Features.Vocabulary;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Learning.Courses;

/// <summary>
/// Owns Learning courses, language-neutral units/variants, context anchors and
/// the existence of directional cards. Card state changes and FSRS scheduling
/// go through <see cref="LearningService"/>.
/// </summary>
public sealed class LearningCourseStore(AppDbContext db)
{
    public const int NameMaxLength = 120;

    public Task<LearningCourseSnapshot> CreateAsync(
        string profileId,
        string sourceLanguage,
        string targetLanguage,
        string? name,
        LearningCourseOptions? options,
        CancellationToken cancellationToken) =>
        CreateAsync(
            profileId,
            sourceLanguage,
            targetLanguage,
            name,
            options,
            primaryWhenFirstForSource: true,
            cancellationToken);

    /// <param name="primaryWhenFirstForSource">
    /// False for auxiliary courses such as a script trainer that must never
    /// receive catalog words implicitly.
    /// </param>
    public async Task<LearningCourseSnapshot> CreateAsync(
        string profileId,
        string sourceLanguage,
        string targetLanguage,
        string? name,
        LearningCourseOptions? options,
        bool primaryWhenFirstForSource,
        CancellationToken cancellationToken)
    {
        var course = await AddCourseAsync(
            profileId,
            sourceLanguage,
            targetLanguage,
            name,
            options ?? new LearningCourseOptions(),
            primaryWhenFirstForSource,
            cancellationToken);

        return LearningCourseSnapshot.From(course);
    }

    public async Task<IReadOnlyList<LearningCourseSnapshot>> ListAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        ValidateProfile(profileId);
        var id = profileId.Trim();

        var courses = await db.LearningCourses
            .AsNoTracking()
            .Where(x => x.ProfileId == id)
            .OrderBy(x => x.SourceLanguage)
            .ThenByDescending(x => x.IsPrimary)
            .ThenBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        return courses.Select(LearningCourseSnapshot.From).ToArray();
    }

    public async Task<LearningCourseSnapshot?> GetAsync(
        string profileId,
        Guid courseId,
        CancellationToken cancellationToken)
    {
        ValidateProfile(profileId);
        var id = profileId.Trim();

        var course = await db.LearningCourses
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.ProfileId == id && x.Id == courseId,
                cancellationToken);

        return course is null ? null : LearningCourseSnapshot.From(course);
    }

    /// <summary>
    /// Updates course settings. Enabling a practice mode creates the missing
    /// directional cards for every unit already in the course; disabling a mode
    /// keeps its cards and their review history but stops scheduling them.
    /// </summary>
    public async Task UpdateAsync(
        string profileId,
        Guid courseId,
        string? name,
        bool isEnabled,
        LearningCourseOptions options,
        CancellationToken cancellationToken)
    {
        var course = await FindOwnedAsync(profileId, courseId, cancellationToken)
            ?? throw new KeyNotFoundException(
                "Learning course was not found for this profile.");

        if (!string.IsNullOrWhiteSpace(name))
        {
            course.Name = CleanName(name);
        }

        course.IsEnabled = isEnabled;
        course.RecognitionEnabled = options.RecognitionEnabled;
        course.ProductionEnabled = options.ProductionEnabled;
        course.ListeningEnabled = options.ListeningEnabled;
        course.WritingEnabled = options.WritingEnabled;
        course.SentencePracticeEnabled = options.SentencePracticeEnabled;
        course.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var unitIds = await db.LearningCards
            .AsNoTracking()
            .Where(x => x.CourseId == course.Id)
            .Select(x => x.UnitId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (unitIds.Count > 0)
        {
            await EnsureCardsAsync(course, unitIds, cancellationToken);
        }
    }

    /// <summary>
    /// Makes the course the one that receives catalog words from content in
    /// its source language.
    /// </summary>
    public async Task SetPrimaryAsync(
        string profileId,
        Guid courseId,
        CancellationToken cancellationToken)
    {
        var course = await FindOwnedAsync(profileId, courseId, cancellationToken)
            ?? throw new KeyNotFoundException(
                "Learning course was not found for this profile.");

        if (course.IsPrimary)
        {
            return;
        }

        await using var transaction =
            await db.Database.BeginTransactionAsync(cancellationToken);

        var previous = await db.LearningCourses
            .Where(x =>
                x.ProfileId == course.ProfileId
                && x.SourceLanguage == course.SourceLanguage
                && x.IsPrimary)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var item in previous)
        {
            item.IsPrimary = false;
            item.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        course.IsPrimary = true;
        course.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public Task<LearningUnitCreateResult> CreateUnitAsync(
        LearningUnitKind kind,
        IReadOnlyList<LearningVariantInput> variants,
        CancellationToken cancellationToken) =>
        CreateUnitAsync(Guid.NewGuid(), kind, variants, cancellationToken);

    /// <param name="unitId">Stable ID for catalog-defined units such as Kana.</param>
    public async Task<LearningUnitCreateResult> CreateUnitAsync(
        Guid unitId,
        LearningUnitKind kind,
        IReadOnlyList<LearningVariantInput> variants,
        CancellationToken cancellationToken)
    {
        if (variants.Count == 0)
        {
            throw new ArgumentException(
                "At least one language variant is required.",
                nameof(variants));
        }

        var normalized = variants.Select(NormalizeVariant).ToArray();
        if (normalized
            .GroupBy(x => (x.LanguageTag, x.Text))
            .Any(x => x.Count() > 1))
        {
            throw new ArgumentException(
                "Duplicate language/text variants are not allowed.",
                nameof(variants));
        }

        var now = DateTime.UtcNow;
        var unit = new LearningUnit
        {
            Id = unitId,
            Kind = kind,
            CreatedAt = now
        };

        var rows = normalized
            .Select(variant => new LearningVariant
            {
                UnitId = unit.Id,
                LanguageTag = variant.LanguageTag,
                Text = variant.Text,
                Reading = variant.Reading,
                Role = variant.Role,
                SourceKind = variant.SourceKind,
                CreatedAt = now
            })
            .ToArray();

        db.LearningUnits.Add(unit);
        db.LearningVariants.AddRange(rows);
        await db.SaveChangesAsync(cancellationToken);

        return new LearningUnitCreateResult(
            new LearningUnitSnapshot(unit.Id, unit.Kind, unit.TermId, unit.CreatedAt),
            rows.Select(LearningVariantSnapshot.From).ToArray());
    }

    /// <summary>
    /// Returns the unit that represents a catalog term, creating it on first
    /// use. The term's text becomes the source-language variant and its
    /// dictionary meaning a <see cref="Term.MeaningLanguage"/> variant.
    /// </summary>
    public async Task<LearningUnit> EnsureTermUnitAsync(
        Term term,
        CancellationToken cancellationToken)
    {
        var language = LearningLanguageTag.Normalize(term.Language);
        var unit = await db.LearningUnits
            .SingleOrDefaultAsync(x => x.TermId == term.Id, cancellationToken);

        var variants = new List<LearningVariant>();
        if (unit is null)
        {
            unit = new LearningUnit
            {
                Kind = LearningUnitKind.Word,
                TermId = term.Id
            };
            db.LearningUnits.Add(unit);
        }
        else
        {
            variants = await db.LearningVariants
                .Where(x => x.UnitId == unit.Id)
                .ToListAsync(cancellationToken);
        }

        var canonical = term.Canonical.Trim();
        var source = variants.FirstOrDefault(x =>
            x.LanguageTag == language && x.Text == canonical);
        var reading = string.IsNullOrWhiteSpace(term.Reading)
            ? null
            : term.Reading.Trim();

        if (source is null)
        {
            db.LearningVariants.Add(new LearningVariant
            {
                UnitId = unit.Id,
                LanguageTag = language,
                Text = canonical,
                Reading = reading,
                Role = LearningVariantRole.Primary,
                SourceKind = LearningVariantSource.Term
            });
        }
        else if (source.Reading is null && reading is not null)
        {
            source.Reading = reading;
        }

        var meaning = term.Meaning?.Trim();
        if (!string.IsNullOrEmpty(meaning)
            && language != Term.MeaningLanguage
            && !variants.Any(x =>
                x.LanguageTag == Term.MeaningLanguage && x.Text == meaning))
        {
            db.LearningVariants.Add(new LearningVariant
            {
                UnitId = unit.Id,
                LanguageTag = Term.MeaningLanguage,
                Text = meaning,
                Role = LearningVariantRole.Meaning,
                SourceKind = LearningVariantSource.Dictionary
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return unit;
    }

    /// <summary>
    /// Resolves the primary course that receives catalog words of a content
    /// language. A profile without one gets a Recognition course towards the
    /// catalog meaning language, matching the bundled dictionary.
    /// </summary>
    public async Task<LearningCourse> ResolvePrimaryCourseAsync(
        string profileId,
        string languageTag,
        CancellationToken cancellationToken)
    {
        ValidateProfile(profileId);
        var id = profileId.Trim();
        var source = LearningLanguageTag.Normalize(languageTag);

        var primary = await db.LearningCourses
            .SingleOrDefaultAsync(
                x => x.ProfileId == id && x.SourceLanguage == source && x.IsPrimary,
                cancellationToken);
        if (primary is not null)
        {
            return primary;
        }

        if (source == Term.MeaningLanguage)
        {
            throw new InvalidOperationException(
                $"Create a Learning course for {source} content in Learning settings first.");
        }

        var existing = await db.LearningCourses
            .SingleOrDefaultAsync(
                x => x.ProfileId == id
                    && x.SourceLanguage == source
                    && x.TargetLanguage == Term.MeaningLanguage,
                cancellationToken);
        if (existing is null)
        {
            return await AddCourseAsync(
                id,
                source,
                Term.MeaningLanguage,
                null,
                new LearningCourseOptions(),
                primaryWhenFirstForSource: true,
                cancellationToken);
        }

        existing.IsPrimary = true;
        existing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<IReadOnlyList<LearningCardSnapshot>> EnsureCourseCardsAsync(
        string profileId,
        Guid courseId,
        Guid unitId,
        CancellationToken cancellationToken)
    {
        var course = await FindOwnedAsync(profileId, courseId, cancellationToken)
            ?? throw new KeyNotFoundException(
                "Learning course was not found for this profile.");

        var cards = await EnsureCardsAsync(course, [unitId], cancellationToken);
        return cards
            .OrderBy(x => x.Mode)
            .Select(LearningCardSnapshot.From)
            .ToArray();
    }

    /// <summary>
    /// Creates the missing directional cards of the given units in a course and
    /// returns all of their cards (tracked). Recognition always exists as the
    /// unit's anchor card; Production, Listening and Writing exist only while the
    /// course enables them, and Production/Writing additionally need a
    /// target-language variant to prompt with. New non-anchor cards inherit the
    /// anchor's state; Known/Learning anchors queue them as new learning.
    /// </summary>
    internal async Task<IReadOnlyList<LearningCard>> EnsureCardsAsync(
        LearningCourse course,
        IReadOnlyCollection<Guid> unitIds,
        CancellationToken cancellationToken)
    {
        var cards = await db.LearningCards
            .Where(x => x.CourseId == course.Id && unitIds.Contains(x.UnitId))
            .ToListAsync(cancellationToken);

        var languages = (await db.LearningVariants
                .AsNoTracking()
                .Where(x =>
                    unitIds.Contains(x.UnitId)
                    && (x.LanguageTag == course.SourceLanguage
                        || x.LanguageTag == course.TargetLanguage))
                .Select(x => new { x.UnitId, x.LanguageTag })
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToLookup(x => x.UnitId, x => x.LanguageTag);

        var now = DateTime.UtcNow;
        long? queuePosition = null;
        var created = new List<LearningCard>();

        foreach (var unitId in unitIds)
        {
            var unitLanguages = languages[unitId].ToHashSet(StringComparer.Ordinal);
            if (!unitLanguages.Contains(course.SourceLanguage))
            {
                throw new InvalidOperationException(
                    $"The learning unit needs a {course.SourceLanguage} variant before it can join the {course.Name} course.");
            }

            var hasTarget = unitLanguages.Contains(course.TargetLanguage);
            var unitCards = cards.Where(x => x.UnitId == unitId).ToList();
            var anchor = unitCards.FirstOrDefault(x => x.Mode == LearningCardMode.Recognition);

            foreach (var mode in Enum.GetValues<LearningCardMode>())
            {
                if (unitCards.Any(x => x.Mode == mode) || !Applies(course, mode, hasTarget))
                {
                    continue;
                }

                var promptIsSource = mode is LearningCardMode.Recognition or LearningCardMode.Listening;
                var card = new LearningCard
                {
                    ProfileId = course.ProfileId,
                    CourseId = course.Id,
                    UnitId = unitId,
                    Mode = mode,
                    PromptLanguage = promptIsSource ? course.SourceLanguage : course.TargetLanguage,
                    AnswerLanguage = promptIsSource ? course.TargetLanguage : course.SourceLanguage,
                    State = UserTermState.Saved,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                if (anchor is not null && anchor.State != UserTermState.Saved)
                {
                    queuePosition ??= await CurrentMaxQueuePositionAsync(
                        course.ProfileId,
                        cancellationToken);
                    var position = queuePosition.Value;

                    if (anchor.State is UserTermState.Known or UserTermState.Learning)
                    {
                        LearningCardTransitions.Queue(card, now, ref position);
                    }
                    else
                    {
                        LearningCardTransitions.Apply(card, anchor.State, now, ref position);
                    }

                    queuePosition = position;
                }

                unitCards.Add(card);
                created.Add(card);
                anchor ??= card;
            }
        }

        if (created.Count > 0)
        {
            db.LearningCards.AddRange(created);
            await db.SaveChangesAsync(cancellationToken);
            cards.AddRange(created);
        }

        return cards;
    }

    public async Task<IReadOnlyList<LearningCardSnapshot>> ListCardsAsync(
        string profileId,
        Guid courseId,
        CancellationToken cancellationToken)
    {
        ValidateProfile(profileId);
        var id = profileId.Trim();

        var cards = await db.LearningCards
            .AsNoTracking()
            .Where(x => x.ProfileId == id && x.CourseId == courseId)
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Mode)
            .ToListAsync(cancellationToken);

        return cards.Select(LearningCardSnapshot.From).ToArray();
    }

    public async Task<IReadOnlyList<LearningVariantSnapshot>> ListVariantsAsync(
        Guid unitId,
        CancellationToken cancellationToken)
    {
        var variants = await db.LearningVariants
            .AsNoTracking()
            .Where(x => x.UnitId == unitId)
            .OrderBy(x => x.LanguageTag)
            .ThenBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        return variants.Select(LearningVariantSnapshot.From).ToArray();
    }

    /// <summary>
    /// Records where a profile met a unit. A profile keeps at most one anchor per
    /// unit, source and position: recording the same place again returns the
    /// existing anchor unchanged.
    /// </summary>
    public async Task<LearningContextAnchor> AddContextAsync(
        string profileId,
        Guid unitId,
        LearningContextInput input,
        CancellationToken cancellationToken)
    {
        ValidateProfile(profileId);
        var profile = profileId.Trim();
        var sourceType = Required(input.SourceType, nameof(input.SourceType), 32)
            .ToLowerInvariant();
        var sourceKey = Required(input.SourceKey, nameof(input.SourceKey), 200);
        var text = Required(input.Text, nameof(input.Text), 2000);
        var positionKey = string.IsNullOrWhiteSpace(input.PositionKey)
            ? null
            : input.PositionKey.Trim();

        if (positionKey?.Length > 200)
        {
            throw new ArgumentException(
                "Learning context position is too long.",
                nameof(input));
        }

        if (!await db.LearningUnits.AnyAsync(x => x.Id == unitId, cancellationToken))
        {
            throw new KeyNotFoundException("Learning unit was not found.");
        }

        var existing = await FindContextAsync(
            profile,
            unitId,
            sourceType,
            sourceKey,
            positionKey,
            cancellationToken);
        if (existing is not null)
        {
            return ToAnchor(existing);
        }

        var context = new LearningContext
        {
            ProfileId = profile,
            UnitId = unitId,
            SourceType = sourceType,
            SourceKey = sourceKey,
            PositionKey = positionKey,
            LanguageTag = LearningLanguageTag.Normalize(input.LanguageTag),
            Text = text
        };

        db.LearningContexts.Add(context);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent request recorded the same anchor first.
            db.Entry(context).State = EntityState.Detached;
            existing = await FindContextAsync(
                profile,
                unitId,
                sourceType,
                sourceKey,
                positionKey,
                cancellationToken);
            if (existing is null)
            {
                throw;
            }

            return ToAnchor(existing);
        }

        return ToAnchor(context);
    }

    public async Task<IReadOnlyList<LearningContextAnchor>> ListContextsAsync(
        string profileId,
        Guid unitId,
        CancellationToken cancellationToken)
    {
        ValidateProfile(profileId);
        var profile = profileId.Trim();

        var contexts = await db.LearningContexts
            .AsNoTracking()
            .Where(x => x.ProfileId == profile && x.UnitId == unitId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        return contexts.Select(ToAnchor).ToArray();
    }

    private Task<LearningContext?> FindContextAsync(
        string profileId,
        Guid unitId,
        string sourceType,
        string sourceKey,
        string? positionKey,
        CancellationToken cancellationToken) =>
        db.LearningContexts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ProfileId == profileId
                    && x.UnitId == unitId
                    && x.SourceType == sourceType
                    && x.SourceKey == sourceKey
                    && x.PositionKey == positionKey,
                cancellationToken);

    internal async Task<long> CurrentMaxQueuePositionAsync(
        string profileId,
        CancellationToken cancellationToken) =>
        await db.LearningCards
            .Where(x => x.ProfileId == profileId)
            .MaxAsync(x => (long?)x.QueuePosition, cancellationToken)
        ?? 0;

    internal Task<LearningCourse?> FindOwnedAsync(
        string profileId,
        Guid courseId,
        CancellationToken cancellationToken)
    {
        ValidateProfile(profileId);
        var id = profileId.Trim();

        return db.LearningCourses.SingleOrDefaultAsync(
            x => x.ProfileId == id && x.Id == courseId,
            cancellationToken);
    }

    private async Task<LearningCourse> AddCourseAsync(
        string profileId,
        string sourceLanguage,
        string targetLanguage,
        string? name,
        LearningCourseOptions options,
        bool primaryWhenFirstForSource,
        CancellationToken cancellationToken)
    {
        ValidateProfile(profileId);
        var id = profileId.Trim();
        var source = LearningLanguageTag.Normalize(sourceLanguage);
        var target = LearningLanguageTag.Normalize(targetLanguage);

        if (source.Equals(target, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Source and target languages must be different.");
        }

        if (await db.LearningCourses.AnyAsync(
                x => x.ProfileId == id
                    && x.SourceLanguage == source
                    && x.TargetLanguage == target,
                cancellationToken))
        {
            throw new InvalidOperationException(
                $"A {source} → {target} learning course already exists for this profile.");
        }

        var isPrimary = primaryWhenFirstForSource
            && !await db.LearningCourses.AnyAsync(
                x => x.ProfileId == id && x.SourceLanguage == source && x.IsPrimary,
                cancellationToken);

        var now = DateTime.UtcNow;
        var course = new LearningCourse
        {
            ProfileId = id,
            Name = string.IsNullOrWhiteSpace(name) ? $"{source} → {target}" : CleanName(name),
            SourceLanguage = source,
            TargetLanguage = target,
            IsEnabled = true,
            IsPrimary = isPrimary,
            RecognitionEnabled = options.RecognitionEnabled,
            ProductionEnabled = options.ProductionEnabled,
            ListeningEnabled = options.ListeningEnabled,
            WritingEnabled = options.WritingEnabled,
            SentencePracticeEnabled = options.SentencePracticeEnabled,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.LearningCourses.Add(course);
        await db.SaveChangesAsync(cancellationToken);
        return course;
    }

    private static bool Applies(
        LearningCourse course,
        LearningCardMode mode,
        bool hasTargetVariant) =>
        mode switch
        {
            LearningCardMode.Recognition => true,
            LearningCardMode.Listening => course.ListeningEnabled,
            LearningCardMode.Production => course.ProductionEnabled && hasTargetVariant,
            LearningCardMode.Writing => course.WritingEnabled && hasTargetVariant,
            _ => false
        };

    private static LearningVariantInput NormalizeVariant(LearningVariantInput variant)
    {
        var text = variant.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Learning variant text is required.");
        }

        return variant with
        {
            LanguageTag = LearningLanguageTag.Normalize(variant.LanguageTag),
            Text = text,
            Reading = string.IsNullOrWhiteSpace(variant.Reading)
                ? null
                : variant.Reading.Trim(),
            Role = string.IsNullOrWhiteSpace(variant.Role)
                ? LearningVariantRole.Primary
                : variant.Role.Trim(),
            SourceKind = string.IsNullOrWhiteSpace(variant.SourceKind)
                ? LearningVariantSource.Manual
                : variant.SourceKind.Trim()
        };
    }

    private static string CleanName(string name)
    {
        var cleaned = name.Trim();
        if (cleaned.Length > NameMaxLength)
        {
            throw new ArgumentException(
                $"Course names can have at most {NameMaxLength} characters.",
                nameof(name));
        }

        return cleaned;
    }

    private static string Required(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentException($"{name} is too long.", name);
        }

        return trimmed;
    }

    private static LearningContextAnchor ToAnchor(LearningContext context) =>
        new(
            context.Id,
            context.UnitId,
            context.SourceType,
            context.SourceKey,
            context.PositionKey,
            context.LanguageTag,
            context.Text,
            context.CreatedAt);

    private static void ValidateProfile(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException(
                "A profile ID is required.",
                nameof(profileId));
        }
    }
}
