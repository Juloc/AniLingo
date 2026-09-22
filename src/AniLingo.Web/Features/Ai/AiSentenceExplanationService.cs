using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AniLingo.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Ai;

public sealed class AiSentenceExplanationService(
    AppDbContext db,
    IAiSentenceExplainer explainer)
{
    public const int PromptVersion = 1;
    private static readonly SemaphoreSlim GenerateGate = new(1, 1);

    public PreparedJapaneseSentence PrepareLocal(string sentence) =>
        JapaneseSentencePreprocessor.Prepare(sentence);

    public async Task<AiSentenceExplanation?> GetCachedAsync(
        string sentence,
        CancellationToken cancellationToken)
    {
        var input = BuildInput(sentence);
        var cached = await db.AiSentenceExplanationCache
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CacheKey == input.CacheKey, cancellationToken);

        return cached is null ? null : FromCache(cached);
    }

    public async Task<AiSentenceExplanation> ExplainAsync(
        string sentence,
        CancellationToken cancellationToken)
    {
        var input = BuildInput(sentence);
        var cached = await db.AiSentenceExplanationCache
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CacheKey == input.CacheKey, cancellationToken);

        if (cached is not null)
        {
            return FromCache(cached);
        }

        await GenerateGate.WaitAsync(cancellationToken);
        try
        {
            cached = await db.AiSentenceExplanationCache
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.CacheKey == input.CacheKey, cancellationToken);

            if (cached is not null)
            {
                return FromCache(cached);
            }

            var generated = await explainer.ExplainSentenceAsync(
                new AiSentenceExplainRequest(
                    input.Prepared.Sentence,
                    input.Prepared.LocalHints),
                cancellationToken);

            var validated = Validate(generated);

            db.AiSentenceExplanationCache.Add(new AiSentenceExplanationCache
            {
                CacheKey = input.CacheKey,
                ProviderId = explainer.Id,
                PromptVersion = PromptVersion,
                Translation = validated.Translation,
                GrammarJson = JsonSerializer.Serialize(validated.Grammar),
                ColloquialJson = JsonSerializer.Serialize(validated.Colloquial),
                CreatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync(cancellationToken);
            return validated with { FromCache = false };
        }
        finally
        {
            GenerateGate.Release();
        }
    }

    private ExplanationInput BuildInput(string sentence)
    {
        var prepared = JapaneseSentencePreprocessor.Prepare(sentence);
        if (prepared.Sentence.Length == 0)
        {
            throw new InvalidOperationException("Sentence is empty.");
        }

        var material = string.Join(
            '\n',
            PromptVersion,
            explainer.Id,
            prepared.Sentence);

        var cacheKey = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(material)));

        return new ExplanationInput(cacheKey, prepared);
    }

    private static AiSentenceExplanation Validate(AiSentenceExplanation explanation)
    {
        var translation = Clean(explanation.Translation, 500);
        if (translation.Length == 0)
        {
            throw new InvalidOperationException("AI explanation returned no translation.");
        }

        var grammar = explanation.Grammar
            .Select(note => Clean(note, 260))
            .Where(note => note.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(3)
            .ToArray();

        var colloquial = explanation.Colloquial
            .Select(note => Clean(note, 260))
            .Where(note => note.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToArray();

        return new AiSentenceExplanation(
            translation,
            grammar,
            colloquial,
            explanation.FromCache);
    }

    private static string Clean(string value, int maxLength)
    {
        var clean = value.Trim();
        return clean.Length <= maxLength ? clean : clean[..maxLength].TrimEnd();
    }

    private static AiSentenceExplanation FromCache(AiSentenceExplanationCache cached) =>
        new(
            cached.Translation,
            DeserializeList(cached.GrammarJson),
            DeserializeList(cached.ColloquialJson),
            FromCache: true);

    private static IReadOnlyList<string> DeserializeList(string json)
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

    private sealed record ExplanationInput(
        string CacheKey,
        PreparedJapaneseSentence Prepared);
}
