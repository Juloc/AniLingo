using System.Text.Json;
using AniLingo.Web.Features.Ai;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Books;
using AniLingo.Web.Features.Novels;

namespace AniLingo.Web.Infrastructure.Ai;

public sealed class ProfileAiProviderRouter(
    CurrentAccountContext currentAccount,
    AiProfileSettingsStore settingsStore,
    AiUsageTracker usageTracker,
    CodexCliProvider codex,
    IHttpClientFactory httpClientFactory)
    : IAiProvider,
      IAiSentenceExplainer,
      INovelTranslator,
      IBookTranslator,
      INovelMappingSuggester,
      IAiUsageReporter
{
    public string Id => "profile-ai-v1";
    public string DisplayName => "Profile AI";

    public async Task<AiTranslationMode> GetTranslationModeAsync(
        CancellationToken cancellationToken)
    {
        var settings = await LoadSettingsAsync(cancellationToken);
        return settings.TranslationMode;
    }

    public async Task<AiProviderStatus> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        var settings = await LoadSettingsAsync(cancellationToken);
        if (settings.ProviderId == AiProviderIds.OpenAiCompatible)
        {
            return await CreatePersonal(settings)
                .GetStatusAsync(cancellationToken);
        }

        return await codex.GetStatusAsync(cancellationToken);
    }

    public async Task<AiProviderStatus> TestCurrentAsync(
        CancellationToken cancellationToken)
    {
        var settings = await LoadSettingsAsync(cancellationToken);
        if (settings.ProviderId == AiProviderIds.OpenAiCompatible)
        {
            return await CreatePersonal(settings)
                .TestAsync(cancellationToken);
        }

        return await codex.GetStatusAsync(cancellationToken);
    }

    public async Task<AiSentenceExplanation> ExplainSentenceAsync(
        AiSentenceExplainRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await LoadSettingsAsync(cancellationToken);
        if (settings.ProviderId == AiProviderIds.OpenAiCompatible)
        {
            return await CreatePersonal(settings)
                .ExplainSentenceAsync(request, cancellationToken);
        }

        var result = await codex.ExplainSentenceAsync(
            request,
            cancellationToken);
        RecordEstimated(
            "sentence-explanation",
            request.Sentence.Length,
            JsonSerializer.Serialize(result).Length);
        return result;
    }

    public async Task<string> TranslateAsync(
        string japaneseText,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var settings = await LoadSettingsAsync(cancellationToken);
        if (settings.ProviderId == AiProviderIds.OpenAiCompatible)
        {
            return await CreatePersonal(settings)
                .TranslateAsync(
                    japaneseText,
                    targetLanguage,
                    cancellationToken);
        }

        var result = await codex.TranslateAsync(
            japaneseText,
            targetLanguage,
            cancellationToken);
        RecordEstimated(
            "novel-translation",
            japaneseText.Length + targetLanguage.Length,
            result.Length);
        return result;
    }

    public async Task<string> TranslateLiteraryAsync(
        string sourceText,
        string sourceLanguage,
        string targetLanguage,
        string context,
        CancellationToken cancellationToken)
    {
        var settings = await LoadSettingsAsync(cancellationToken);
        if (settings.ProviderId == AiProviderIds.OpenAiCompatible)
        {
            return await CreatePersonal(settings)
                .TranslateLiteraryAsync(
                    sourceText,
                    sourceLanguage,
                    targetLanguage,
                    context,
                    cancellationToken);
        }

        var result = await codex.TranslateLiteraryAsync(
            sourceText,
            sourceLanguage,
            targetLanguage,
            context,
            cancellationToken);
        RecordEstimated(
            "book-translation",
            sourceText.Length
                + sourceLanguage.Length
                + targetLanguage.Length
                + context.Length,
            result.Length);
        return result;
    }

    public async Task<BookTranslationBibleSeed> AnalyzeBookAsync(
        BookTranslationAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await LoadSettingsAsync(cancellationToken);
        if (settings.ProviderId == AiProviderIds.OpenAiCompatible)
        {
            return await CreatePersonal(settings)
                .AnalyzeBookAsync(request, cancellationToken);
        }

        var result = await codex.AnalyzeBookAsync(
            request,
            cancellationToken);
        RecordEstimated(
            "book-analysis",
            request.Title.Length
                + (request.Author?.Length ?? 0)
                + (request.Description?.Length ?? 0)
                + request.SourceSample.Length,
            JsonSerializer.Serialize(result).Length);
        return result;
    }

    public async Task<string> EditLiteraryAsync(
        BookLiteraryEditRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await LoadSettingsAsync(cancellationToken);
        if (settings.ProviderId == AiProviderIds.OpenAiCompatible)
        {
            return await CreatePersonal(settings)
                .EditLiteraryAsync(request, cancellationToken);
        }

        var result = await codex.EditLiteraryAsync(
            request,
            cancellationToken);
        RecordEstimated(
            "book-edit",
            request.SourceText.Length
                + request.DraftTranslation.Length
                + request.Context.Length,
            result.Length);
        return result;
    }

    public async Task<BookTranslationQualityReview> ReviewLiteraryAsync(
        BookTranslationQaRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await LoadSettingsAsync(cancellationToken);
        if (settings.ProviderId == AiProviderIds.OpenAiCompatible)
        {
            return await CreatePersonal(settings)
                .ReviewLiteraryAsync(request, cancellationToken);
        }

        var result = await codex.ReviewLiteraryAsync(
            request,
            cancellationToken);
        RecordEstimated(
            "book-qa",
            request.SourceText.Length
                + request.EditedTranslation.Length
                + request.Context.Length,
            JsonSerializer.Serialize(result).Length);
        return result;
    }

    public async Task<BookTranslationMemoryDelta> ExtractTranslationMemoryAsync(
        BookTranslationMemoryRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await LoadSettingsAsync(cancellationToken);
        if (settings.ProviderId == AiProviderIds.OpenAiCompatible)
        {
            return await CreatePersonal(settings)
                .ExtractTranslationMemoryAsync(request, cancellationToken);
        }

        var result = await codex.ExtractTranslationMemoryAsync(
            request,
            cancellationToken);
        RecordEstimated(
            "book-memory",
            request.SourceText.Length
                + request.FinalTranslation.Length
                + request.ExistingContext.Length,
            JsonSerializer.Serialize(result).Length);
        return result;
    }

    public async Task<IReadOnlyList<NovelMappingSuggestion>> SuggestMappingsAsync(
        NovelMappingSuggestionRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await LoadSettingsAsync(cancellationToken);
        if (settings.ProviderId == AiProviderIds.OpenAiCompatible)
        {
            return await CreatePersonal(settings)
                .SuggestMappingsAsync(request, cancellationToken);
        }

        var result = await codex.SuggestMappingsAsync(
            request,
            cancellationToken);
        var inputCharacters =
            request.NovelTitle.Length
            + request.AnimeTitle.Length
            + request.Chapters.Sum(x => x.Title.Length + 12)
            + request.Episodes.Sum(x => x.Title.Length + 16);
        RecordEstimated(
            "novel-mapping",
            inputCharacters,
            JsonSerializer.Serialize(result).Length);
        return result;
    }

    public void RecordCacheHit(string operation) =>
        usageTracker.Record(
            currentAccount.ProfileId,
            new AiUsageMeasurement(
                DateTimeOffset.UtcNow,
                operation,
                "cache",
                null,
                0,
                0,
                0,
                0,
                Estimated: false,
                CacheHit: true,
                ResumedChunk: false));

    public void RecordResumedChunk(string operation) =>
        usageTracker.Record(
            currentAccount.ProfileId,
            new AiUsageMeasurement(
                DateTimeOffset.UtcNow,
                operation,
                "cache",
                null,
                0,
                0,
                0,
                0,
                Estimated: false,
                CacheHit: false,
                ResumedChunk: true));

    private Task<AiProfileSettings> LoadSettingsAsync(
        CancellationToken cancellationToken) =>
        settingsStore.LoadAsync(
            currentAccount.ProfileId,
            cancellationToken);

    private OpenAiCompatibleProvider CreatePersonal(
        AiProfileSettings settings) =>
        new(
            httpClientFactory.CreateClient("ai-openai-compatible"),
            settings,
            measurement =>
                usageTracker.Record(
                    currentAccount.ProfileId,
                    measurement));

    private void RecordEstimated(
        string operation,
        int inputCharacters,
        int outputCharacters)
    {
        usageTracker.Record(
            currentAccount.ProfileId,
            new AiUsageMeasurement(
                DateTimeOffset.UtcNow,
                operation,
                codex.Id,
                null,
                inputCharacters,
                outputCharacters,
                AiUsageTracker.EstimateTokens(inputCharacters),
                AiUsageTracker.EstimateTokens(outputCharacters),
                Estimated: true,
                CacheHit: false,
                ResumedChunk: false));
    }
}
