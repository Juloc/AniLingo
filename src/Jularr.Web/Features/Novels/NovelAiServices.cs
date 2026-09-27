using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jularr.Web.Data;
using Jularr.Web.Features.Ai;
using Jularr.Web.Features.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Jularr.Web.Features.Novels;

public interface INovelTranslator
{
    string Id { get; }

    Task<string> TranslateAsync(
        string japaneseText,
        string targetLanguage,
        CancellationToken cancellationToken);
}

public enum NovelTranslationEngine
{
    Ai,
    TranslateGemma
}

public static class NovelTranslationProviders
{
    public const string TranslateGemmaPrefix = "translategemma:";

    /// <summary>
    /// TranslateGemma text is stored under its own track language ("de-gemma"), so every
    /// query for the AI "de" track (offline export, search, counters) excludes it by construction.
    /// </summary>
    public const string TranslateGemmaTrackSuffix = "-gemma";

    public static bool IsTranslateGemma(string providerId) =>
        providerId.StartsWith(TranslateGemmaPrefix, StringComparison.Ordinal);

    public static string TrackLanguage(NovelTranslationEngine engine, string targetLanguage) =>
        engine == NovelTranslationEngine.TranslateGemma
            ? targetLanguage + TranslateGemmaTrackSuffix
            : targetLanguage;
}

public sealed record NovelMappingChapter(int Number, string Title);
public sealed record NovelMappingEpisode(int SeasonNumber, int Number, string Title);

public sealed record NovelMappingSuggestion(
    int ChapterStart,
    int ChapterEnd,
    int SeasonNumber,
    int EpisodeStart,
    int EpisodeEnd,
    string? Label);

public sealed record NovelMappingSuggestionRequest(
    string NovelTitle,
    IReadOnlyList<NovelMappingChapter> Chapters,
    string AnimeTitle,
    IReadOnlyList<NovelMappingEpisode> Episodes);

public interface INovelMappingSuggester
{
    string Id { get; }

    Task<IReadOnlyList<NovelMappingSuggestion>> SuggestMappingsAsync(
        NovelMappingSuggestionRequest request,
        CancellationToken cancellationToken);
}

public sealed class NovelTranslationService(
    AppDbContext db,
    NovelImportService imports,
    INovelTranslator translator,
    IHttpClientFactory? httpClientFactory = null,
    IConfiguration? configuration = null,
    AiActivityRunner? activityRunner = null,
    CurrentAccountContext? account = null,
    ILogger<NovelTranslationService>? logger = null)
{
    public const int PromptVersion = 1;
    public const int TranslateGemmaPromptVersion = 1;
    private const int MaxChunkCharacters = 6500;
    private static readonly SemaphoreSlim AiGenerateGate = new(1, 1);
    private static readonly SemaphoreSlim TranslateGemmaGenerateGate = new(1, 1);

    public bool TranslateGemmaConfigured =>
        TranslateGemmaOptions.FromConfiguration(configuration, logger) is not null;

    public Task<NovelTranslation?> GetCachedAsync(
        Guid chapterId,
        string targetLanguage,
        CancellationToken cancellationToken) =>
        GetCachedAsync(
            chapterId,
            targetLanguage,
            NovelTranslationEngine.Ai,
            cancellationToken);

    public Task<NovelTranslation?> GetCachedAsync(
        Guid chapterId,
        string targetLanguage,
        NovelTranslationEngine engine,
        CancellationToken cancellationToken)
    {
        var promptVersion = engine == NovelTranslationEngine.TranslateGemma
            ? TranslateGemmaPromptVersion
            : PromptVersion;

        var trackLanguage = NovelTranslationProviders.TrackLanguage(engine, targetLanguage);
        var query =
            from translation in db.NovelTranslations.AsNoTracking()
            join chapter in db.NovelChapters.AsNoTracking()
                on translation.ChapterId equals chapter.Id
            where translation.ChapterId == chapterId &&
                translation.TargetLanguage == trackLanguage &&
                translation.PromptVersion == promptVersion &&
                translation.SourceHash == chapter.SourceHash
            select translation;

        query = engine == NovelTranslationEngine.TranslateGemma
            ? query.Where(x => x.ProviderId.StartsWith(NovelTranslationProviders.TranslateGemmaPrefix))
            : query.Where(x => !x.ProviderId.StartsWith(NovelTranslationProviders.TranslateGemmaPrefix));

        return query
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<NovelTranslation> TranslateChapterAsync(
        Guid chapterId,
        string targetLanguage,
        CancellationToken cancellationToken) =>
        TranslateChapterAsync(
            chapterId,
            targetLanguage,
            NovelTranslationEngine.Ai,
            cancellationToken);

    public async Task<NovelTranslation> TranslateChapterAsync(
        Guid chapterId,
        string targetLanguage,
        NovelTranslationEngine engine,
        CancellationToken cancellationToken)
    {
        var chapter = await imports.DownloadChapterContentAsync(
            chapterId,
            forceRefresh: false,
            cancellationToken);

        return engine switch
        {
            NovelTranslationEngine.TranslateGemma =>
                await TranslateWithTranslateGemmaAsync(chapter, targetLanguage, cancellationToken),
            _ => await TranslateWithAiAsync(chapter, targetLanguage, cancellationToken)
        };
    }

    public static IReadOnlyList<string> ChunkText(string text, int maxCharacters)
    {
        if (maxCharacters < 200)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCharacters));
        }

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

        if (normalized.Length == 0)
        {
            return [];
        }

        var chunks = new List<string>();
        var builder = new StringBuilder();

        foreach (var paragraph in normalized.Split("\n\n"))
        {
            var remaining = paragraph.Trim();
            if (remaining.Length == 0)
            {
                continue;
            }

            while (remaining.Length > maxCharacters)
            {
                if (builder.Length > 0)
                {
                    chunks.Add(builder.ToString().Trim());
                    builder.Clear();
                }

                var split = FindSafeSplit(remaining, maxCharacters);
                chunks.Add(remaining[..split].Trim());
                remaining = remaining[split..].TrimStart();
            }

            if (builder.Length > 0 &&
                builder.Length + 2 + remaining.Length > maxCharacters)
            {
                chunks.Add(builder.ToString().Trim());
                builder.Clear();
            }

            if (builder.Length > 0)
            {
                builder.Append("\n\n");
            }

            builder.Append(remaining);
        }

        if (builder.Length > 0)
        {
            chunks.Add(builder.ToString().Trim());
        }

        return chunks;
    }

    private async Task<NovelTranslation> TranslateWithAiAsync(
        NovelChapter chapter,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var cached = await GetExactAiAsync(
            chapter.Id,
            chapter.SourceHash,
            targetLanguage,
            cancellationToken);

        if (cached is not null)
        {
            if (translator is IAiUsageReporter usageReporter)
            {
                usageReporter.RecordCacheHit(AiOperations.NovelTranslation);
            }

            return cached;
        }

        await AiGenerateGate.WaitAsync(cancellationToken);
        try
        {
            cached = await GetExactAiAsync(
                chapter.Id,
                chapter.SourceHash,
                targetLanguage,
                cancellationToken);

            if (cached is not null)
            {
                if (translator is IAiUsageReporter usageReporter)
                {
                    usageReporter.RecordCacheHit(AiOperations.NovelTranslation);
                }

                return cached;
            }

            var translatedChunks = new List<string>();
            foreach (var chunk in ChunkText(chapter.OriginalText, MaxChunkCharacters))
            {
                var translated = (await translator.TranslateAsync(
                    chunk,
                    targetLanguage,
                    cancellationToken)).Trim();

                if (translated.Length == 0)
                {
                    throw new InvalidOperationException(
                        "AI translation returned an empty chapter segment.");
                }

                translatedChunks.Add(translated);
            }

            var completed = new NovelTranslation
            {
                ChapterId = chapter.Id,
                TargetLanguage = targetLanguage,
                ProviderId = translator.Id,
                PromptVersion = PromptVersion,
                SourceHash = chapter.SourceHash,
                Text = string.Join("\n\n", translatedChunks),
                CreatedAt = DateTime.UtcNow
            };

            db.NovelTranslations.Add(completed);
            await db.SaveChangesAsync(cancellationToken);
            return completed;
        }
        finally
        {
            AiGenerateGate.Release();
        }
    }

    private async Task<NovelTranslation> TranslateWithTranslateGemmaAsync(
        NovelChapter chapter,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var options = TranslateGemmaOptions.FromConfiguration(configuration, logger)
            ?? throw new InvalidOperationException(
                "TranslateGemma is not configured. Set TranslateGemma:Endpoint to an OpenAI-compatible local chat-completions endpoint.");

        if (httpClientFactory is null)
        {
            throw new InvalidOperationException(
                "TranslateGemma requires the configured HTTP client factory.");
        }

        var localTranslator = new TranslateGemmaNovelTranslator(httpClientFactory, options);
        var trackLanguage = NovelTranslationProviders.TrackLanguage(
            NovelTranslationEngine.TranslateGemma,
            targetLanguage);
        var cached = await GetExactTranslateGemmaAsync(
            chapter.Id,
            chapter.SourceHash,
            trackLanguage,
            localTranslator.Id,
            cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        await TranslateGemmaGenerateGate.WaitAsync(cancellationToken);
        try
        {
            cached = await GetExactTranslateGemmaAsync(
                chapter.Id,
                chapter.SourceHash,
                trackLanguage,
                localTranslator.Id,
                cancellationToken);
            if (cached is not null)
            {
                return cached;
            }

            var paragraphs = NovelTextLayout.SplitParagraphs(chapter.OriginalText);
            if (paragraphs.Count == 0)
            {
                throw new InvalidOperationException(
                    "The chapter contains no text to translate.");
            }

            // Paragraphs finished by an earlier failed run are kept, so a retried job
            // only requests the ones that are still missing.
            var resumeKey = TranslateGemmaResumeCache.Key(
                chapter.Id,
                chapter.SourceHash,
                trackLanguage,
                localTranslator.Id);
            var translatedParagraphs = TranslateGemmaResumeCache.Get(resumeKey, paragraphs.Count);

            try
            {
                await RunTranslateGemmaActivityAsync(
                    options,
                    paragraphs,
                    token => localTranslator.TranslateParagraphsAsync(
                        paragraphs,
                        targetLanguage,
                        translatedParagraphs,
                        token),
                    cancellationToken);
            }
            catch
            {
                TranslateGemmaResumeCache.Save(resumeKey, translatedParagraphs);
                throw;
            }

            if (translatedParagraphs.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidOperationException(
                    "TranslateGemma did not return a complete paragraph-aligned translation.");
            }

            var completed = new NovelTranslation
            {
                ChapterId = chapter.Id,
                TargetLanguage = trackLanguage,
                ProviderId = localTranslator.Id,
                PromptVersion = TranslateGemmaPromptVersion,
                SourceHash = chapter.SourceHash,
                Text = string.Join("\n\n", translatedParagraphs),
                CreatedAt = DateTime.UtcNow
            };

            db.NovelTranslations.Add(completed);
            await db.SaveChangesAsync(cancellationToken);
            TranslateGemmaResumeCache.Remove(resumeKey);
            return completed;
        }
        finally
        {
            TranslateGemmaGenerateGate.Release();
        }
    }

    /// <summary>
    /// Runs the chapter as one tracked AI activity (live state, progress, retries and one usage
    /// measurement) when the activity infrastructure is registered.
    /// </summary>
    private async Task RunTranslateGemmaActivityAsync(
        TranslateGemmaOptions options,
        IReadOnlyList<string> paragraphs,
        Func<CancellationToken, Task<int>> translate,
        CancellationToken cancellationToken)
    {
        if (activityRunner is null || account is null)
        {
            await translate(cancellationToken);
            return;
        }

        var inputCharacters = paragraphs.Sum(x => x.Length);
        await activityRunner.RunAsync(
            new AiActivityStart(
                account.ProfileId,
                AiOperations.NovelTranslation,
                TranslateGemmaNovelTranslator.ActivityProviderId,
                new AiInvocationOptions(options.Model, null, null, null),
                AiUsageTracker.EstimateTokens(inputCharacters)),
            inputCharacters,
            translate,
            outputCharacters => outputCharacters,
            cancellationToken);
    }

    private Task<NovelTranslation?> GetExactAiAsync(
        Guid chapterId,
        string sourceHash,
        string targetLanguage,
        CancellationToken cancellationToken) =>
        db.NovelTranslations
            .AsNoTracking()
            .Where(x => x.ChapterId == chapterId &&
                x.TargetLanguage == targetLanguage &&
                x.PromptVersion == PromptVersion &&
                x.SourceHash == sourceHash &&
                !x.ProviderId.StartsWith(NovelTranslationProviders.TranslateGemmaPrefix))
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    private Task<NovelTranslation?> GetExactTranslateGemmaAsync(
        Guid chapterId,
        string sourceHash,
        string trackLanguage,
        string providerId,
        CancellationToken cancellationToken) =>
        db.NovelTranslations
            .AsNoTracking()
            .Where(x => x.ChapterId == chapterId &&
                x.TargetLanguage == trackLanguage &&
                x.ProviderId == providerId &&
                x.PromptVersion == TranslateGemmaPromptVersion &&
                x.SourceHash == sourceHash)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    private static int FindSafeSplit(string value, int maxCharacters)
    {
        var start = Math.Max(1, maxCharacters - 500);
        for (var index = maxCharacters; index >= start; index--)
        {
            if (value[index - 1] is '\n' or '。' or '！' or '？' or '!' or '?')
            {
                return index;
            }
        }

        return maxCharacters;
    }
}

public sealed record TranslateGemmaOptions(
    Uri Endpoint,
    string Model,
    int MaxChunkCharacters,
    TimeSpan RequestTimeout)
{
    public const string SectionName = "TranslateGemma";
    private const string DefaultModel = "translategemma-12b-it";
    private static string? lastInvalidWarning;

    /// <summary>Pause before retrying a paragraph after a transient failure; multiplied per attempt.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Reads the TranslateGemma settings. Missing or invalid settings mean "not configured": an
    /// invalid value is logged once as a warning instead of failing every novel page that asks.
    /// </summary>
    public static TranslateGemmaOptions? FromConfiguration(
        IConfiguration? configuration,
        ILogger? logger = null)
    {
        var endpointValue = configuration?[$"{SectionName}:Endpoint"]?.Trim();
        if (string.IsNullOrWhiteSpace(endpointValue))
        {
            return null;
        }

        if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            return Invalid(
                logger,
                "TranslateGemma:Endpoint must be an absolute HTTP or HTTPS URL; local translation stays disabled.");
        }

        var model = configuration?[$"{SectionName}:Model"]?.Trim();
        if (string.IsNullOrWhiteSpace(model))
        {
            model = DefaultModel;
        }

        if (model.Length > 200)
        {
            return Invalid(
                logger,
                "TranslateGemma:Model must not exceed 200 characters; local translation stays disabled.");
        }

        // TranslateGemma is tuned for roughly 2K input tokens. Keep the
        // default conservative for Japanese text; operators can override it.
        var maxChunkCharacters = 1200;
        if (int.TryParse(
                configuration?[$"{SectionName}:MaxChunkCharacters"],
                out var configuredChunkCharacters))
        {
            maxChunkCharacters = Math.Clamp(configuredChunkCharacters, 400, 3000);
        }

        var timeoutMinutes = 30;
        if (int.TryParse(
                configuration?[$"{SectionName}:TimeoutMinutes"],
                out var configuredTimeoutMinutes))
        {
            timeoutMinutes = Math.Clamp(configuredTimeoutMinutes, 1, 120);
        }

        return new TranslateGemmaOptions(
            endpoint,
            model,
            maxChunkCharacters,
            TimeSpan.FromMinutes(timeoutMinutes));
    }

    private static TranslateGemmaOptions? Invalid(ILogger? logger, string message)
    {
        // The reader asks on every chapter; warn once per distinct problem.
        if (logger is not null &&
            !string.Equals(Interlocked.Exchange(ref lastInvalidWarning, message), message, StringComparison.Ordinal))
        {
            logger.LogWarning("{Message}", message);
        }

        return null;
    }
}

/// <summary>
/// Client for a local TranslateGemma model behind an OpenAI-compatible chat-completions endpoint.
/// It cannot go through <c>OpenAiCompatibleProvider</c>: TranslateGemma's chat template needs one
/// structured user message and no system prompt, and the endpoint is instance configuration
/// without an API key rather than a per-profile provider. Activity, usage and error sanitising
/// use the same AI infrastructure.
/// </summary>
public sealed class TranslateGemmaNovelTranslator
{
    public const string HttpClientName = "translategemma";
    public const string ActivityProviderId = "translategemma";
    private const int MaxAttempts = 3;

    private readonly IHttpClientFactory httpClientFactory;
    private readonly TranslateGemmaOptions options;

    public TranslateGemmaNovelTranslator(
        IHttpClientFactory httpClientFactory,
        TranslateGemmaOptions options)
    {
        this.httpClientFactory = httpClientFactory;
        this.options = options;
        Id = BuildProviderId(options);
    }

    public string Id { get; }

    /// <summary>
    /// Fills the empty slots of <paramref name="translated"/> paragraph by paragraph. Slots already
    /// filled (from an earlier failed run) are skipped, and each finished paragraph stays in the
    /// array even when a later one fails. Returns the number of translated characters.
    /// </summary>
    public async Task<int> TranslateParagraphsAsync(
        IReadOnlyList<string> paragraphs,
        string targetLanguage,
        string?[] translated,
        CancellationToken cancellationToken)
    {
        if (translated.Length != paragraphs.Count)
        {
            throw new ArgumentException(
                "The result array must have one slot per paragraph.",
                nameof(translated));
        }

        var activity = AiActivityScope.Current;
        activity?.SetTransport(AiTransports.OpenAiChatCompletions);
        activity?.SetModel(options.Model, null);
        activity?.SetState(AiActivityState.Running);

        var targetLanguageCode = NormalizeTargetLanguage(targetLanguage);
        var usage = new UsageTotals();

        for (var index = 0; index < paragraphs.Count; index++)
        {
            activity?.ReportProgress(index, paragraphs.Count);
            if (!string.IsNullOrWhiteSpace(translated[index]))
            {
                continue;
            }

            var paragraph = paragraphs[index];
            var parts = paragraph.Length <= options.MaxChunkCharacters
                ? [paragraph]
                : NovelTranslationService.ChunkText(
                    paragraph,
                    options.MaxChunkCharacters);

            var translatedParts = new List<string>(parts.Count);
            foreach (var part in parts)
            {
                translatedParts.Add(await TranslateWithRetryAsync(
                    part,
                    DetectSourceLanguage(part),
                    targetLanguageCode,
                    usage,
                    cancellationToken));
            }

            translated[index] = string.Join(" ", translatedParts);
            activity?.ReportUsage(usage.ToTokenUsage());
        }

        activity?.ReportProgress(paragraphs.Count, paragraphs.Count);
        return translated.Sum(x => x?.Length ?? 0);
    }

    private async Task<string> TranslateWithRetryAsync(
        string source,
        string sourceLanguageCode,
        string targetLanguageCode,
        UsageTotals usage,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await TranslateTextAsync(
                    source,
                    sourceLanguageCode,
                    targetLanguageCode,
                    usage,
                    cancellationToken);
            }
            catch (TranslateGemmaTransientException) when (attempt < MaxAttempts)
            {
                AiActivityScope.Current?.ReportRetry();
                await Task.Delay(options.RetryDelay * attempt, cancellationToken);
                AiActivityScope.Current?.SetState(AiActivityState.Running);
            }
        }
    }

    private async Task<string> TranslateTextAsync(
        string source,
        string sourceLanguageCode,
        string targetLanguageCode,
        UsageTotals usage,
        CancellationToken cancellationToken)
    {
        // TranslateGemma's official chat template requires exactly one user
        // content item with type/source_lang_code/target_lang_code/text and no
        // system message. Some OpenAI-compatible vLLM wrappers cannot pass the
        // custom structured content through; for those we retry once using the
        // delimiter format used by vLLM-compatible TranslateGemma model cards.
        var structuredContent = new object[]
        {
            new
            {
                type = "text",
                source_lang_code = sourceLanguageCode,
                target_lang_code = targetLanguageCode,
                text = source
            }
        };

        return await SendAsync(
            structuredContent,
            allowTemplateFallback: true,
            source,
            sourceLanguageCode,
            targetLanguageCode,
            usage,
            cancellationToken);
    }

    private async Task<string> SendAsync(
        object userContent,
        bool allowTemplateFallback,
        string source,
        string sourceLanguageCode,
        string targetLanguageCode,
        UsageTotals usage,
        CancellationToken cancellationToken)
    {
        try
        {
            return await SendOnceAsync(
                userContent,
                allowTemplateFallback,
                source,
                sourceLanguageCode,
                targetLanguageCode,
                usage,
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TranslateGemmaTransientException(
                $"TranslateGemma did not answer within {FormatTimeout(options.RequestTimeout)}.");
        }
        catch (HttpRequestException exception)
        {
            throw new TranslateGemmaTransientException(
                AiErrorSanitizer.Sanitize($"TranslateGemma is unreachable: {exception.Message}")
                ?? "TranslateGemma is unreachable.");
        }
    }

    private async Task<string> SendOnceAsync(
        object userContent,
        bool allowTemplateFallback,
        string source,
        string sourceLanguageCode,
        string targetLanguageCode,
        UsageTotals usage,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.RequestTimeout);

        // The per-request timeout above is the only limit: HttpClient's own 100 s default would
        // otherwise cut off a slow local model long before the configured TimeoutMinutes.
        var client = httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = Timeout.InfiniteTimeSpan;
        using var request = new HttpRequestMessage(HttpMethod.Post, options.Endpoint)
        {
            Content = JsonContent.Create(new
            {
                model = options.Model,
                messages = new object[]
                {
                    new { role = "user", content = userContent }
                },
                temperature = 0,
                max_tokens = 2048,
                stream = false
            })
        };

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);

        if (!response.IsSuccessStatusCode)
        {
            if (allowTemplateFallback &&
                ((int)response.StatusCode == 400 || (int)response.StatusCode == 422))
            {
                var delimited =
                    $"<<<source>>>{sourceLanguageCode}<<<target>>>{targetLanguageCode}<<<text>>>{source}";
                return await SendOnceAsync(
                    delimited,
                    allowTemplateFallback: false,
                    source,
                    sourceLanguageCode,
                    targetLanguageCode,
                    usage,
                    cancellationToken);
            }

            var error = await ReadErrorAsync(response, timeout.Token);
            var failure = AiErrorSanitizer.Sanitize(
                    $"TranslateGemma returned HTTP {(int)response.StatusCode}: {error}")
                ?? $"TranslateGemma returned HTTP {(int)response.StatusCode}.";

            // Overload and server errors are worth another attempt; rejected requests are not.
            if ((int)response.StatusCode is 408 or 429 or >= 500)
            {
                throw new TranslateGemmaTransientException(failure);
            }

            throw new InvalidOperationException(failure);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: timeout.Token);

        if (!document.RootElement.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0 ||
            !choices[0].TryGetProperty("message", out var message) ||
            !message.TryGetProperty("content", out var contentElement))
        {
            throw new InvalidOperationException(
                "TranslateGemma returned an invalid OpenAI-compatible response.");
        }

        var content = contentElement.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException(
                "TranslateGemma returned an empty translation.");
        }

        usage.Add(document.RootElement, source.Length, content.Length);
        return content;
    }

    private static async Task<string> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return body.Length <= 500 ? body : body[..500];
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            return "";
        }
    }

    private static string FormatTimeout(TimeSpan timeout) =>
        timeout.TotalMinutes >= 1
            ? $"{timeout.TotalMinutes:0} minute(s)"
            : $"{timeout.TotalSeconds:0.#} second(s)";

    /// <summary>Token totals for the chapter; estimated when the endpoint does not report usage.</summary>
    private sealed class UsageTotals
    {
        private long inputTokens;
        private long outputTokens;
        private bool estimated;

        public void Add(JsonElement response, int inputCharacters, int outputCharacters)
        {
            if (response.TryGetProperty("usage", out var usage) &&
                usage.ValueKind == JsonValueKind.Object &&
                usage.TryGetProperty("prompt_tokens", out var prompt) &&
                prompt.TryGetInt64(out var promptTokens) &&
                usage.TryGetProperty("completion_tokens", out var completion) &&
                completion.TryGetInt64(out var completionTokens))
            {
                inputTokens += promptTokens;
                outputTokens += completionTokens;
                return;
            }

            estimated = true;
            inputTokens += AiUsageTracker.EstimateTokens(inputCharacters);
            outputTokens += AiUsageTracker.EstimateTokens(outputCharacters);
        }

        public AiTokenUsage ToTokenUsage() =>
            new(inputTokens, 0, outputTokens, 0, estimated);
    }

    private static string NormalizeTargetLanguage(string language) =>
        language.Trim().ToLowerInvariant() switch
        {
            "de" or "de-de" => "de-DE",
            "en" or "en-us" => "en-US",
            "en-gb" => "en-GB",
            var value when value.Length is 2 => value,
            var value => value
        };

    private static string DetectSourceLanguage(string text)
    {
        foreach (var character in text)
        {
            if (character is >= '\u3040' and <= '\u30ff' ||
                character is >= '\u3400' and <= '\u4dbf' ||
                character is >= '\u4e00' and <= '\u9fff')
            {
                return "ja";
            }
        }

        return "en";
    }

    private static string BuildProviderId(TranslateGemmaOptions options)
    {
        var safeModel = new string(options.Model
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.')
            .Take(36)
            .ToArray());
        if (safeModel.Length == 0)
        {
            safeModel = "model";
        }

        var fingerprintSource =
            $"{options.Endpoint.AbsoluteUri}|{options.Model}|official-structured-v1";
        var fingerprint = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintSource)))
            .ToLowerInvariant()[..12];

        return $"{NovelTranslationProviders.TranslateGemmaPrefix}{safeModel}:{fingerprint}";
    }
}

/// <summary>A TranslateGemma failure worth retrying: timeout, connection error, overload or 5xx.</summary>
public sealed class TranslateGemmaTransientException(string message)
    : InvalidOperationException(message);

/// <summary>
/// Paragraphs of a TranslateGemma chapter job that failed part-way, keyed by chapter, source hash,
/// track and model. A retried job resumes from here instead of repeating every paragraph. Kept in
/// memory and bounded: after a restart the job simply starts over.
/// </summary>
internal static class TranslateGemmaResumeCache
{
    private const int MaxChapters = 32;
    private static readonly ConcurrentDictionary<string, string?[]> Chapters =
        new(StringComparer.Ordinal);

    public static string Key(Guid chapterId, string sourceHash, string trackLanguage, string providerId) =>
        $"{chapterId:N}|{sourceHash}|{trackLanguage}|{providerId}";

    public static string?[] Get(string key, int paragraphCount) =>
        Chapters.TryGetValue(key, out var saved) && saved.Length == paragraphCount
            ? (string?[])saved.Clone()
            : new string?[paragraphCount];

    public static void Save(string key, string?[] paragraphs)
    {
        if (!paragraphs.Any(x => !string.IsNullOrWhiteSpace(x)))
        {
            return;
        }

        if (Chapters.Count >= MaxChapters && !Chapters.ContainsKey(key))
        {
            Chapters.Clear();
        }

        Chapters[key] = (string?[])paragraphs.Clone();
    }

    public static void Remove(string key) => Chapters.TryRemove(key, out _);
}

public sealed record NovelAnimeChoice(
    Guid AnimeId,
    string Title,
    string Provider,
    string ExternalId);

public sealed class NovelMappingService(
    AppDbContext db,
    INovelMappingSuggester suggester)
{
    public Task<List<NovelAnimeMapping>> GetForChapterAsync(
        Guid workId,
        int chapterNumber,
        CancellationToken cancellationToken) =>
        db.NovelAnimeMappings
            .AsNoTracking()
            .Where(x => x.WorkId == workId &&
                x.ChapterStart <= chapterNumber &&
                x.ChapterEnd >= chapterNumber)
            .OrderBy(x => x.SeasonNumber)
            .ThenBy(x => x.EpisodeStart)
            .ToListAsync(cancellationToken);

    public Task<List<NovelAnimeChoice>> GetAnimeChoicesAsync(
        CancellationToken cancellationToken) =>
        (
            from anime in db.Anime.AsNoTracking()
            join metadata in db.AnimeMetadata.AsNoTracking()
                on anime.Id equals metadata.AnimeId
            orderby metadata.PreferredTitle
            select new NovelAnimeChoice(
                anime.Id,
                metadata.PreferredTitle,
                metadata.Provider,
                metadata.ExternalId)
        ).ToListAsync(cancellationToken);

    public async Task AddManualAsync(
        Guid workId,
        Guid animeId,
        int chapterStart,
        int chapterEnd,
        int seasonNumber,
        int episodeStart,
        int episodeEnd,
        string? label,
        CancellationToken cancellationToken)
    {
        var target = await GetAnimeTargetAsync(animeId, cancellationToken);
        await ValidateRangeAsync(
            workId,
            animeId,
            chapterStart,
            chapterEnd,
            seasonNumber,
            episodeStart,
            episodeEnd,
            cancellationToken);

        db.NovelAnimeMappings.Add(new NovelAnimeMapping
        {
            WorkId = workId,
            ChapterStart = chapterStart,
            ChapterEnd = chapterEnd,
            AnimeProvider = target.Provider,
            AnimeExternalId = target.ExternalId,
            SeasonNumber = seasonNumber,
            EpisodeStart = episodeStart,
            EpisodeEnd = episodeEnd,
            Label = CleanLabel(label),
            Source = "manual",
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> SuggestAsync(
        Guid workId,
        Guid animeId,
        CancellationToken cancellationToken)
    {
        var work = await db.NovelWorks
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == workId, cancellationToken)
            ?? throw new InvalidOperationException("Novel work was not found.");

        var target = await GetAnimeTargetAsync(animeId, cancellationToken);

        var chapters = await db.NovelChapters
            .AsNoTracking()
            .Where(x => x.WorkId == workId)
            .OrderBy(x => x.Number)
            .Select(x => new NovelMappingChapter(x.Number, x.Title))
            .ToListAsync(cancellationToken);

        var episodes = await db.Episodes
            .AsNoTracking()
            .Where(x => x.AnimeId == animeId)
            .OrderBy(x => x.SeasonNumber)
            .ThenBy(x => x.Number)
            .Select(x => new NovelMappingEpisode(
                x.SeasonNumber,
                x.Number,
                x.Title))
            .ToListAsync(cancellationToken);

        if (chapters.Count == 0 || episodes.Count == 0)
        {
            throw new InvalidOperationException(
                "Both novel chapters and local anime episodes are required for AI matching.");
        }

        var suggestions = await suggester.SuggestMappingsAsync(
            new NovelMappingSuggestionRequest(
                work.MetadataTitle ?? work.Title,
                chapters,
                target.Title,
                episodes),
            cancellationToken);

        var valid = new List<NovelMappingSuggestion>();
        foreach (var suggestion in suggestions)
        {
            if (suggestion.ChapterStart > suggestion.ChapterEnd ||
                suggestion.EpisodeStart > suggestion.EpisodeEnd)
            {
                continue;
            }

            var chapterRangeValid =
                chapters.Any(x => x.Number == suggestion.ChapterStart) &&
                chapters.Any(x => x.Number == suggestion.ChapterEnd);

            var episodeRangeValid =
                episodes.Any(x => x.SeasonNumber == suggestion.SeasonNumber &&
                    x.Number == suggestion.EpisodeStart) &&
                episodes.Any(x => x.SeasonNumber == suggestion.SeasonNumber &&
                    x.Number == suggestion.EpisodeEnd);

            if (chapterRangeValid && episodeRangeValid)
            {
                valid.Add(suggestion);
            }
        }

        await db.NovelAnimeMappings
            .Where(x => x.WorkId == workId &&
                x.AnimeProvider == target.Provider &&
                x.AnimeExternalId == target.ExternalId &&
                x.Source == "ai")
            .ExecuteDeleteAsync(cancellationToken);

        foreach (var suggestion in valid)
        {
            db.NovelAnimeMappings.Add(new NovelAnimeMapping
            {
                WorkId = workId,
                ChapterStart = suggestion.ChapterStart,
                ChapterEnd = suggestion.ChapterEnd,
                AnimeProvider = target.Provider,
                AnimeExternalId = target.ExternalId,
                SeasonNumber = suggestion.SeasonNumber,
                EpisodeStart = suggestion.EpisodeStart,
                EpisodeEnd = suggestion.EpisodeEnd,
                Label = CleanLabel(suggestion.Label),
                Source = "ai",
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return valid.Count;
    }

    public async Task RemoveAsync(
        Guid workId,
        Guid mappingId,
        CancellationToken cancellationToken)
    {
        await db.NovelAnimeMappings
            .Where(x => x.Id == mappingId && x.WorkId == workId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task ValidateRangeAsync(
        Guid workId,
        Guid animeId,
        int chapterStart,
        int chapterEnd,
        int seasonNumber,
        int episodeStart,
        int episodeEnd,
        CancellationToken cancellationToken)
    {
        if (chapterStart <= 0 || chapterStart > chapterEnd ||
            seasonNumber < 0 ||
            episodeStart <= 0 || episodeStart > episodeEnd)
        {
            throw new InvalidOperationException("The mapping range is invalid.");
        }

        var chapterBounds = await db.NovelChapters
            .AsNoTracking()
            .Where(x => x.WorkId == workId)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Min = group.Min(x => x.Number),
                Max = group.Max(x => x.Number)
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (chapterBounds is null ||
            chapterStart < chapterBounds.Min ||
            chapterEnd > chapterBounds.Max)
        {
            throw new InvalidOperationException("The chapter range does not exist.");
        }

        var episodeNumbers = await db.Episodes
            .AsNoTracking()
            .Where(x => x.AnimeId == animeId && x.SeasonNumber == seasonNumber)
            .Select(x => x.Number)
            .ToListAsync(cancellationToken);

        if (!episodeNumbers.Contains(episodeStart) ||
            !episodeNumbers.Contains(episodeEnd))
        {
            throw new InvalidOperationException("The episode range does not exist.");
        }
    }

    private async Task<NovelAnimeChoice> GetAnimeTargetAsync(
        Guid animeId,
        CancellationToken cancellationToken)
    {
        var target = await (
            from anime in db.Anime.AsNoTracking()
            join metadata in db.AnimeMetadata.AsNoTracking()
                on anime.Id equals metadata.AnimeId
            where anime.Id == animeId
            select new NovelAnimeChoice(
                anime.Id,
                metadata.PreferredTitle,
                metadata.Provider,
                metadata.ExternalId)
        ).SingleOrDefaultAsync(cancellationToken);

        return target ?? throw new InvalidOperationException(
            "Match the local anime to AniList before creating a novel mapping.");
    }

    private static string? CleanLabel(string? label)
    {
        var clean = label?.Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            return null;
        }

        return clean.Length <= 200 ? clean : clean[..200].TrimEnd();
    }
}
