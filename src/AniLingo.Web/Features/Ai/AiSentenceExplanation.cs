using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AniLingo.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Ai;

public sealed record AiSentenceExplanationRequest(
    string Sentence,
    string Target);

public sealed record AiSentenceExplanationResult(
    string Translation,
    IReadOnlyList<string> GrammarNotes,
    IReadOnlyList<string> SpeechNotes);

public interface IAiSentenceExplainer
{
    string ProviderId { get; }

    Task<AiSentenceExplanationResult> ExplainSentenceAsync(
        AiSentenceExplanationRequest request,
        CancellationToken cancellationToken);
}

public sealed class AiSentenceExplanationCache
{
    public string CacheKey { get; set; } = "";
    public string ProviderId { get; set; } = "";
    public string PromptVersion { get; set; } = "";
    public string Translation { get; set; } = "";
    public string GrammarNotesJson { get; set; } = "[]";
    public string SpeechNotesJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AiSentenceExplanationService(
    AppDbContext db,
    IAiSentenceExplainer explainer)
{
    public const string PromptVersion = "sentence-v1";

    public async Task<AiSentenceExplanationResult?> GetCachedAsync(
        string sentence,
        string target,
        CancellationToken cancellationToken)
    {
        var cacheKey = BuildCacheKey(explainer.ProviderId, sentence, target);

        var row = await db.AiSentenceExplanations
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CacheKey == cacheKey, cancellationToken);

        return row is null ? null : FromCache(row);
    }

    public async Task<AiSentenceExplanationResult> GetOrCreateAsync(
        string sentence,
        string target,
        CancellationToken cancellationToken)
    {
        ValidateInput(sentence, target);

        var cached = await GetCachedAsync(sentence, target, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var result = NormalizeResult(await explainer.ExplainSentenceAsync(
            new AiSentenceExplanationRequest(sentence, target),
            cancellationToken));

        var row = new AiSentenceExplanationCache
        {
            CacheKey = BuildCacheKey(explainer.ProviderId, sentence, target),
            ProviderId = explainer.ProviderId,
            PromptVersion = PromptVersion,
            Translation = result.Translation,
            GrammarNotesJson = JsonSerializer.Serialize(result.GrammarNotes),
            SpeechNotesJson = JsonSerializer.Serialize(result.SpeechNotes),
            CreatedAt = DateTime.UtcNow
        };

        db.AiSentenceExplanations.Add(row);
        await db.SaveChangesAsync(cancellationToken);

        return result;
    }

    public static AiSentenceExplanationResult NormalizeResult(
        AiSentenceExplanationResult result)
    {
        var translation = NormalizeText(result.Translation, 1200);
        if (translation.Length == 0)
        {
            throw new InvalidOperationException("AI explanation did not contain a translation.");
        }

        return new AiSentenceExplanationResult(
            translation,
            NormalizeNotes(result.GrammarNotes, 6),
            NormalizeNotes(result.SpeechNotes, 6));
    }

    private static IReadOnlyList<string> NormalizeNotes(
        IReadOnlyList<string>? notes,
        int maxCount) =>
        (notes ?? [])
            .Select(note => NormalizeText(note, 800))
            .Where(note => note.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(maxCount)
            .ToArray();

    private static string NormalizeText(string? value, int maxLength)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength].TrimEnd();
    }

    private static AiSentenceExplanationResult FromCache(
        AiSentenceExplanationCache row) =>
        NormalizeResult(new AiSentenceExplanationResult(
            row.Translation,
            DeserializeNotes(row.GrammarNotesJson),
            DeserializeNotes(row.SpeechNotesJson)));

    private static IReadOnlyList<string> DeserializeNotes(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Cached AI explanation is invalid.", exception);
        }
    }

    private static string BuildCacheKey(
        string providerId,
        string sentence,
        string target)
    {
        var canonical = string.Join(
            '\n',
            providerId.Trim(),
            PromptVersion,
            sentence.Normalize(NormalizationForm.FormKC).Trim(),
            target.Normalize(NormalizationForm.FormKC).Trim());

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static void ValidateInput(string sentence, string target)
    {
        if (string.IsNullOrWhiteSpace(sentence))
        {
            throw new ArgumentException("Sentence is required.", nameof(sentence));
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            throw new ArgumentException("Target term is required.", nameof(target));
        }

        if (sentence.Length > 4000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sentence),
                "Sentence is too long for a review explanation.");
        }

        if (target.Length > 300)
        {
            throw new ArgumentOutOfRangeException(
                nameof(target),
                "Target term is too long.");
        }
    }
}
