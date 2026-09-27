using System.Text.Json;
using Jularr.Web.Features.Ai;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Books;
using Jularr.Web.Features.Novels;
using Jularr.Web.Features.StoryContext;

namespace Jularr.Web.Infrastructure.Ai;

/// <summary>The AI operations both the server Codex provider and personal API providers implement.</summary>
internal interface IProfileAiBackend : IAiSentenceExplainer, INovelTranslator, IBookTranslator, INovelMappingSuggester, IStoryContextExtractor;

/// <summary>
/// Routes every AI operation of the signed-in profile to its selected provider. Each call runs as a
/// tracked activity with capability-checked options (profile defaults plus per-operation overrides)
/// and exactly one usage measurement.
/// </summary>
public sealed class ProfileAiProviderRouter(
    CurrentAccountContext currentAccount,
    AiProfileSettingsStore settingsStore,
    AiUsageTracker usageTracker,
    CodexCliProvider codex,
    IHttpClientFactory httpClientFactory,
    AiActivityRunner activityRunner,
    AiModelCatalogService catalogs)
    : IAiProvider,
      IAiSentenceExplainer,
      INovelTranslator,
      IBookTranslator,
      INovelMappingSuggester,
      IStoryContextExtractor,
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

    /// <summary>Cached model catalog of the profile's provider; never contacts the provider.</summary>
    public async Task<AiModelCatalog> GetCachedCatalogAsync(
        AiProfileSettings settings,
        CancellationToken cancellationToken) =>
        await catalogs.GetCachedAsync(CatalogKey(settings), cancellationToken);

    /// <summary>Upper bound for the automatic first discovery that runs after the settings page loaded.</summary>
    public static readonly TimeSpan AutomaticDiscoveryTimeout = TimeSpan.FromSeconds(25);

    private static readonly TimeSpan ExplicitDiscoveryTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Whether the profile's provider can be asked for models: the shared server connection always
    /// can; a personal provider needs its base URL and API key.
    /// </summary>
    public static bool CanDiscover(AiProfileSettings settings) =>
        settings.ProviderId != AiProviderIds.OpenAiCompatible
        || (!string.IsNullOrWhiteSpace(settings.BaseUrl) && !string.IsNullOrWhiteSpace(settings.ApiKey));

    /// <summary>Explicitly asks the profile's provider for its models (never on a page GET).</summary>
    public async Task<AiModelCatalog> RefreshCatalogAsync(
        AiProfileSettings settings,
        CancellationToken cancellationToken) =>
        CanDiscover(settings)
            ? await catalogs.RefreshAsync(CatalogKey(settings), ModelFetcher(settings), cancellationToken, ExplicitDiscoveryTimeout)
            : await GetCachedCatalogAsync(settings, cancellationToken);

    /// <summary>
    /// The one automatic discovery after the settings page rendered (never during the GET itself):
    /// asks the provider only while <see cref="AiModelCatalogService.NeedsDiscovery"/>.
    /// </summary>
    public async Task<AiModelCatalog> GetOrDiscoverCatalogAsync(
        AiProfileSettings settings,
        CancellationToken cancellationToken) =>
        CanDiscover(settings)
            ? await catalogs.GetOrDiscoverAsync(CatalogKey(settings), ModelFetcher(settings), cancellationToken, AutomaticDiscoveryTimeout)
            : await GetCachedCatalogAsync(settings, cancellationToken);

    public Task<AiSentenceExplanation> ExplainSentenceAsync(
        AiSentenceExplainRequest request,
        CancellationToken cancellationToken) =>
        RunAsync(
            AiOperations.SentenceExplanation,
            request.Sentence.Length,
            0,
            (backend, token) => backend.ExplainSentenceAsync(request, token),
            result => JsonSerializer.Serialize(result).Length,
            cancellationToken);

    public Task<string> TranslateAsync(
        string japaneseText,
        string targetLanguage,
        CancellationToken cancellationToken) =>
        RunAsync(
            AiOperations.NovelTranslation,
            japaneseText.Length + targetLanguage.Length,
            0,
            (backend, token) => backend.TranslateAsync(japaneseText, targetLanguage, token),
            result => result.Length,
            cancellationToken);

    public Task<string> TranslateLiteraryAsync(
        string sourceText,
        string sourceLanguage,
        string targetLanguage,
        string context,
        CancellationToken cancellationToken) =>
        RunAsync(
            AiOperations.BookTranslation,
            sourceText.Length + sourceLanguage.Length + targetLanguage.Length + context.Length,
            context.Length,
            (backend, token) => backend.TranslateLiteraryAsync(sourceText, sourceLanguage, targetLanguage, context, token),
            result => result.Length,
            cancellationToken);

    public Task<BookTranslationBibleSeed> AnalyzeBookAsync(
        BookTranslationAnalysisRequest request,
        CancellationToken cancellationToken) =>
        RunAsync(
            AiOperations.BookAnalysis,
            request.Title.Length
                + (request.Author?.Length ?? 0)
                + (request.Description?.Length ?? 0)
                + request.SourceSample.Length,
            0,
            (backend, token) => backend.AnalyzeBookAsync(request, token),
            result => JsonSerializer.Serialize(result).Length,
            cancellationToken);

    public Task<string> EditLiteraryAsync(
        BookLiteraryEditRequest request,
        CancellationToken cancellationToken) =>
        RunAsync(
            AiOperations.BookEdit,
            request.SourceText.Length + request.DraftTranslation.Length + request.Context.Length,
            request.Context.Length,
            (backend, token) => backend.EditLiteraryAsync(request, token),
            result => result.Length,
            cancellationToken);

    public Task<BookTranslationQualityReview> ReviewLiteraryAsync(
        BookTranslationQaRequest request,
        CancellationToken cancellationToken) =>
        RunAsync(
            AiOperations.BookQa,
            request.SourceText.Length + request.EditedTranslation.Length + request.Context.Length,
            request.Context.Length,
            (backend, token) => backend.ReviewLiteraryAsync(request, token),
            result => JsonSerializer.Serialize(result).Length,
            cancellationToken);

    public Task<BookTranslationMemoryDelta> ExtractTranslationMemoryAsync(
        BookTranslationMemoryRequest request,
        CancellationToken cancellationToken) =>
        RunAsync(
            AiOperations.BookMemory,
            request.SourceText.Length + request.FinalTranslation.Length + request.ExistingContext.Length,
            request.ExistingContext.Length,
            (backend, token) => backend.ExtractTranslationMemoryAsync(request, token),
            result => JsonSerializer.Serialize(result).Length,
            cancellationToken);

    public Task<StoryChapterExtraction> ExtractChapterAsync(
        StoryChapterExtractionRequest request,
        CancellationToken cancellationToken) =>
        RunAsync(
            AiOperations.StoryContext,
            request.SourceText.Length + request.ExistingContext.Length + request.ChapterTitle.Length,
            request.ExistingContext.Length,
            (backend, token) => backend.ExtractChapterAsync(request, token),
            result => JsonSerializer.Serialize(result).Length,
            cancellationToken);

    public Task<IReadOnlyList<NovelMappingSuggestion>> SuggestMappingsAsync(
        NovelMappingSuggestionRequest request,
        CancellationToken cancellationToken) =>
        RunAsync(
            AiOperations.NovelMapping,
            request.NovelTitle.Length
                + request.AnimeTitle.Length
                + request.Chapters.Sum(x => x.Title.Length + 12)
                + request.Episodes.Sum(x => x.Title.Length + 16),
            0,
            (backend, token) => backend.SuggestMappingsAsync(request, token),
            result => JsonSerializer.Serialize(result).Length,
            cancellationToken);

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

    private async Task<T> RunAsync<T>(
        string operation,
        int inputCharacters,
        int contextCharacters,
        Func<IProfileAiBackend, CancellationToken, Task<T>> call,
        Func<T, int> outputCharacters,
        CancellationToken cancellationToken)
    {
        var settings = await LoadSettingsAsync(cancellationToken);
        var requested = settings.Resolve(operation);
        IProfileAiBackend backend;
        string providerId;
        AiInvocationOptions options;

        if (settings.ProviderId == AiProviderIds.OpenAiCompatible)
        {
            backend = CreatePersonal(settings);
            providerId = AiProviderIds.OpenAiCompatible;
            options = AiOptionResolver.ResolvePersonal(requested);
        }
        else
        {
            backend = codex;
            providerId = codex.Id;
            var catalog = await catalogs.GetCachedAsync(AiModelCatalogKeys.CodexServer, cancellationToken);
            options = AiOptionResolver.ResolveServer(requested, catalog, operation);
        }

        var contextTokens = AiUsageTracker.EstimateTokens(contextCharacters);
        return await activityRunner.RunAsync(
            new AiActivityStart(
                currentAccount.ProfileId,
                operation,
                providerId,
                options,
                AiUsageTracker.EstimateTokens(Math.Max(0, inputCharacters - contextCharacters)),
                contextTokens),
            inputCharacters,
            token => call(backend, token),
            outputCharacters,
            cancellationToken);
    }

    private Task<AiProfileSettings> LoadSettingsAsync(
        CancellationToken cancellationToken) =>
        settingsStore.LoadAsync(
            currentAccount.ProfileId,
            cancellationToken);

    private string CatalogKey(AiProfileSettings settings) =>
        settings.ProviderId == AiProviderIds.OpenAiCompatible
            ? AiModelCatalogKeys.OpenAiCompatible(currentAccount.ProfileId, settings.BaseUrl ?? string.Empty)
            : AiModelCatalogKeys.CodexServer;

    private Func<CancellationToken, Task<IReadOnlyList<AiModelDescriptor>>> ModelFetcher(AiProfileSettings settings) =>
        settings.ProviderId == AiProviderIds.OpenAiCompatible
            ? token => CreatePersonal(settings).ListModelsAsync(token)
            : codex.ListModelsAsync;

    // Usage is recorded once per request by the activity runner, so the personal provider gets no sink.
    private OpenAiCompatibleProvider CreatePersonal(
        AiProfileSettings settings) =>
        new(
            httpClientFactory.CreateClient("ai-openai-compatible"),
            settings);
}
