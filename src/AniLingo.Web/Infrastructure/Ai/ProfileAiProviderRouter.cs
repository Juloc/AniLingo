using AniLingo.Web.Features.Ai;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Books;
using AniLingo.Web.Features.Novels;

namespace AniLingo.Web.Infrastructure.Ai;

public sealed class ProfileAiProviderRouter(
    CurrentAccountContext currentAccount,
    AiProfileSettingsStore settingsStore,
    CodexCliProvider codex,
    IHttpClientFactory httpClientFactory)
    : IAiProvider, IAiSentenceExplainer, INovelTranslator, IBookTranslator, INovelMappingSuggester
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
        return settings.ProviderId == AiProviderIds.OpenAiCompatible
            ? await CreatePersonal(settings).ExplainSentenceAsync(request, cancellationToken)
            : await codex.ExplainSentenceAsync(request, cancellationToken);
    }

    public async Task<string> TranslateAsync(
        string japaneseText,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var settings = await LoadSettingsAsync(cancellationToken);
        return settings.ProviderId == AiProviderIds.OpenAiCompatible
            ? await CreatePersonal(settings).TranslateAsync(japaneseText, targetLanguage, cancellationToken)
            : await codex.TranslateAsync(japaneseText, targetLanguage, cancellationToken);
    }

    public async Task<string> TranslateLiteraryAsync(
        string sourceText,
        string sourceLanguage,
        string targetLanguage,
        string context,
        CancellationToken cancellationToken)
    {
        var settings = await LoadSettingsAsync(cancellationToken);
        return settings.ProviderId == AiProviderIds.OpenAiCompatible
            ? await CreatePersonal(settings).TranslateLiteraryAsync(
                sourceText,
                sourceLanguage,
                targetLanguage,
                context,
                cancellationToken)
            : await codex.TranslateLiteraryAsync(
                sourceText,
                sourceLanguage,
                targetLanguage,
                context,
                cancellationToken);
    }

    public async Task<BookTranslationBibleSeed> AnalyzeBookAsync(
        BookTranslationAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await LoadSettingsAsync(cancellationToken);
        return settings.ProviderId == AiProviderIds.OpenAiCompatible
            ? await CreatePersonal(settings).AnalyzeBookAsync(request, cancellationToken)
            : await codex.AnalyzeBookAsync(request, cancellationToken);
    }

    public async Task<string> EditLiteraryAsync(
        BookLiteraryEditRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await LoadSettingsAsync(cancellationToken);
        return settings.ProviderId == AiProviderIds.OpenAiCompatible
            ? await CreatePersonal(settings).EditLiteraryAsync(request, cancellationToken)
            : await codex.EditLiteraryAsync(request, cancellationToken);
    }

    public async Task<BookTranslationQualityReview> ReviewLiteraryAsync(
        BookTranslationQaRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await LoadSettingsAsync(cancellationToken);
        return settings.ProviderId == AiProviderIds.OpenAiCompatible
            ? await CreatePersonal(settings).ReviewLiteraryAsync(request, cancellationToken)
            : await codex.ReviewLiteraryAsync(request, cancellationToken);
    }

    public async Task<BookTranslationMemoryDelta> ExtractTranslationMemoryAsync(
        BookTranslationMemoryRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await LoadSettingsAsync(cancellationToken);
        return settings.ProviderId == AiProviderIds.OpenAiCompatible
            ? await CreatePersonal(settings).ExtractTranslationMemoryAsync(request, cancellationToken)
            : await codex.ExtractTranslationMemoryAsync(request, cancellationToken);
    }

    public async Task<IReadOnlyList<NovelMappingSuggestion>> SuggestMappingsAsync(
        NovelMappingSuggestionRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await LoadSettingsAsync(cancellationToken);
        return settings.ProviderId == AiProviderIds.OpenAiCompatible
            ? await CreatePersonal(settings).SuggestMappingsAsync(request, cancellationToken)
            : await codex.SuggestMappingsAsync(request, cancellationToken);
    }

    private Task<AiProfileSettings> LoadSettingsAsync(
        CancellationToken cancellationToken) =>
        settingsStore.LoadAsync(
            currentAccount.ProfileId,
            cancellationToken);

    private OpenAiCompatibleProvider CreatePersonal(
        AiProfileSettings settings) =>
        new(
            httpClientFactory.CreateClient("ai-openai-compatible"),
            settings);
}
