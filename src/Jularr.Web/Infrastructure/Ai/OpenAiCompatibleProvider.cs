using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jularr.Web.Features.Ai;
using Jularr.Web.Features.Books;
using Jularr.Web.Features.Novels;
using Jularr.Web.Features.StoryContext;

namespace Jularr.Web.Infrastructure.Ai;

public sealed class OpenAiCompatibleProvider(
    HttpClient httpClient,
    AiProfileSettings settings,
    Action<AiUsageMeasurement>? usageSink = null)
    : IAiProvider, IAiSentenceExplainer, INovelTranslator, IBookTranslator, INovelMappingSuggester, IStoryContextExtractor, IProfileAiBackend
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

    private static readonly JsonSerializerOptions RequestJsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

    public string Id => AiProviderIds.OpenAiCompatible;
    public string DisplayName => "OpenAI-compatible API";

    public Task<AiTranslationMode> GetTranslationModeAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(settings.TranslationMode);

    public Task<AiProviderStatus> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        var configured =
            !string.IsNullOrWhiteSpace(settings.BaseUrl)
            && !string.IsNullOrWhiteSpace(settings.Model)
            && !string.IsNullOrWhiteSpace(settings.ApiKey);

        return Task.FromResult(
            new AiProviderStatus(
                Id,
                DisplayName,
                IsAvailable: configured,
                IsAuthenticated: configured,
                Version: settings.Model,
                AuthenticationMethod: configured ? "API key" : null,
                Error: configured
                    ? null
                    : "Base URL, model and API key are required."));
    }

    public async Task<AiProviderStatus> TestAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var reply = await CompleteAsync(
                "provider-test",
                "You are a connectivity check. Follow the user instruction exactly.",
                "Reply with exactly OK.",
                cancellationToken);

            return new AiProviderStatus(
                Id,
                DisplayName,
                IsAvailable: true,
                IsAuthenticated: !string.IsNullOrWhiteSpace(reply),
                Version: settings.Model,
                AuthenticationMethod: "API key",
                Error: string.IsNullOrWhiteSpace(reply)
                    ? "The provider returned an empty response."
                    : null);
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or TaskCanceledException
                or InvalidOperationException
                or JsonException)
        {
            return new AiProviderStatus(
                Id,
                DisplayName,
                IsAvailable: true,
                IsAuthenticated: false,
                Version: settings.Model,
                AuthenticationMethod: "API key",
                Error: exception.Message);
        }
    }

    public async Task<AiSentenceExplanation> ExplainSentenceAsync(
        AiSentenceExplainRequest request,
        CancellationToken cancellationToken)
    {
        var json = await CompleteAsync(
            "sentence-explanation",
            "Explain Japanese sentences for a language learner. Return JSON only with keys translation, grammar, colloquial. grammar and colloquial are arrays of short strings.",
            $"Sentence:\n{request.Sentence}",
            cancellationToken);

        var parsed = DeserializeJson<SentenceExplanationDto>(json);
        if (string.IsNullOrWhiteSpace(parsed.Translation))
        {
            throw new InvalidOperationException(
                "The AI provider returned no sentence translation.");
        }

        return new AiSentenceExplanation(
            parsed.Translation.Trim(),
            parsed.Grammar ?? [],
            parsed.Colloquial ?? [],
            FromCache: false);
    }

    public Task<string> TranslateAsync(
        string japaneseText,
        string targetLanguage,
        CancellationToken cancellationToken) =>
        CompleteAsync(
            "novel-translation",
            "You are a professional literary translator. Translate only the supplied Japanese prose. Preserve paragraph breaks, dialogue, names, tone and meaning. Do not summarize, censor, explain or omit content. Return only the translation.",
            $"Target language: {targetLanguage}\n\nSOURCE TEXT:\n{japaneseText}",
            cancellationToken);

    public Task<string> TranslateLiteraryAsync(
        string sourceText,
        string sourceLanguage,
        string targetLanguage,
        string context,
        CancellationToken cancellationToken) =>
        CompleteAsync(
            "book-translation",
            "You are a professional literary translator and line editor. Produce publication-quality prose in the target language in one pass. Preserve meaning, narrative voice, emotional tone, pacing, dialogue intent, paragraph structure, names and factual details. Context is reference material only. Return only the translated source text.",
            $"Source language: {sourceLanguage}\nTarget language: {targetLanguage}\n\nCONTEXT:\n{BoundedContext(context)}\n\nSOURCE TEXT:\n{sourceText}",
            cancellationToken);

    public async Task<BookTranslationBibleSeed> AnalyzeBookAsync(
        BookTranslationAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        var json = await CompleteAsync(
            "book-analysis",
            "Analyze a book for translation consistency. Return JSON only with keys narrativePerspective, overallStyle, register, audience, themes, entities, terms. entities use sourceName,targetName,type,description,pronouns,relationships,voiceNotes. terms use source,target,category,notes,locked.",
            $"Title: {request.Title}\nAuthor: {request.Author}\nDescription: {request.Description}\nGenres: {string.Join(", ", request.Genres)}\nSource language: {request.SourceLanguage}\nTarget language: {request.TargetLanguage}\n\nSOURCE SAMPLE:\n{request.SourceSample}",
            cancellationToken);

        return DeserializeJson<BookTranslationBibleSeed>(json);
    }

    public Task<string> EditLiteraryAsync(
        BookLiteraryEditRequest request,
        CancellationToken cancellationToken) =>
        CompleteAsync(
            "book-edit",
            "Act as a literary translation editor. Improve the draft for accuracy, natural style, continuity and character voice without adding, removing or summarizing content. Return only the edited translation.",
            $"Source language: {request.SourceLanguage}\nTarget language: {request.TargetLanguage}\n\nCONTEXT:\n{BoundedContext(request.Context)}\n\nSOURCE:\n{request.SourceText}\n\nDRAFT:\n{request.DraftTranslation}",
            cancellationToken);

    public async Task<BookTranslationQualityReview> ReviewLiteraryAsync(
        BookTranslationQaRequest request,
        CancellationToken cancellationToken)
    {
        var json = await CompleteAsync(
            "book-qa",
            "Review a literary translation against its source. Return JSON only with keys accepted (boolean), correctedTranslation (string or null), issues (array). If accepted is false, correctedTranslation must contain the complete corrected translation.",
            $"Source language: {request.SourceLanguage}\nTarget language: {request.TargetLanguage}\n\nCONTEXT:\n{BoundedContext(request.Context)}\n\nSOURCE:\n{request.SourceText}\n\nTRANSLATION:\n{request.EditedTranslation}",
            cancellationToken);

        return DeserializeJson<BookTranslationQualityReview>(json);
    }

    public async Task<BookTranslationMemoryDelta> ExtractTranslationMemoryAsync(
        BookTranslationMemoryRequest request,
        CancellationToken cancellationToken)
    {
        var json = await CompleteAsync(
            "book-memory",
            "Extract only durable translation-memory facts needed for later chapters. Return JSON only with keys chapterSummary, continuityNotes, entities, terms. Keep it compact. entities use sourceName,targetName,type,description,pronouns,relationships,voiceNotes. terms use source,target,category,notes,locked.",
            $"Chapter {request.ChapterNumber}: {request.ChapterTitle}\nSource language: {request.SourceLanguage}\nTarget language: {request.TargetLanguage}\n\nEXISTING CONTEXT:\n{BoundedContext(request.ExistingContext)}\n\nSOURCE SAMPLE:\n{request.SourceText}\n\nTRANSLATION SAMPLE:\n{request.FinalTranslation}",
            cancellationToken);

        return DeserializeJson<BookTranslationMemoryDelta>(json);
    }

    public async Task<StoryChapterExtraction> ExtractChapterAsync(
        StoryChapterExtractionRequest request,
        CancellationToken cancellationToken)
    {
        var boundedRequest = request with { ExistingContext = BoundedContext(request.ExistingContext) };
        var json = await CompleteAsync(
            AiOperations.StoryContext,
            StoryContextExtractionPrompt.Instructions + " " + StoryContextExtractionPrompt.JsonShape,
            StoryContextExtractionPrompt.BuildInput(boundedRequest),
            cancellationToken);

        return StoryContextExtractionPrompt.Map(
            DeserializeJson<StoryContextExtractionPrompt.Result>(json));
    }

    /// <summary>Trims shared context to the ambient per-task context budget; null keeps it unchanged.</summary>
    private static string BoundedContext(string context) =>
        CodexCliProvider.TrimContext(context, AiActivityScope.Current?.Options.ContextBudgetTokens);

    public async Task<IReadOnlyList<NovelMappingSuggestion>> SuggestMappingsAsync(
        NovelMappingSuggestionRequest request,
        CancellationToken cancellationToken)
    {
        var chapters = string.Join(
            "\n",
            request.Chapters.Select(x => $"{x.Number}: {x.Title}"));
        var episodes = string.Join(
            "\n",
            request.Episodes.Select(x => $"S{x.SeasonNumber}E{x.Number}: {x.Title}"));

        var json = await CompleteAsync(
            "novel-mapping",
            "Map novel chapter ranges to anime episode ranges from titles and ordering. Return JSON only as an object with a suggestions array. Each suggestion has chapterStart, chapterEnd, seasonNumber, episodeStart, episodeEnd, label.",
            $"Novel: {request.NovelTitle}\nCHAPTERS:\n{chapters}\n\nAnime: {request.AnimeTitle}\nEPISODES:\n{episodes}",
            cancellationToken);

        return DeserializeJson<MappingEnvelope>(json).Suggestions ?? [];
    }

    private async Task<string> CompleteAsync(
        string operation,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        var activity = AiActivityScope.Current;
        var invocationOptions = activity?.Options ?? AiInvocationOptions.Default;
        var maxAttempts = 1 + Math.Clamp(invocationOptions.MaxRetries ?? 0, 0, AiProfileSettings.MaxRetriesLimit);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await CompleteOnceAsync(operation, systemPrompt, userPrompt, activity, invocationOptions, cancellationToken);
            }
            catch (Exception exception) when (
                attempt < maxAttempts
                && exception is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException
                && !cancellationToken.IsCancellationRequested)
            {
                activity?.ReportRetry();
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
            }
        }
    }

    private async Task<string> CompleteOnceAsync(
        string operation,
        string systemPrompt,
        string userPrompt,
        AiActivityHandle? activity,
        AiInvocationOptions invocationOptions,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.BaseUrl)
            || string.IsNullOrWhiteSpace(settings.Model)
            || string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException(
                "The OpenAI-compatible provider is not fully configured.");
        }

        var model = invocationOptions.Model ?? settings.Model;
        var maxTokens = activity is null ? settings.MaxOutputTokens : invocationOptions.MaxOutputTokens;
        activity?.SetTransport(AiTransports.OpenAiChatCompletions);
        activity?.SetModel(model, null);
        activity?.SetState(AiActivityState.Running);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (invocationOptions.TimeoutSeconds is { } timeoutSeconds)
        {
            timeoutSource.CancelAfter(
                TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, AiProfileSettings.MinTimeoutSeconds, AiProfileSettings.MaxTimeoutSeconds)));
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildUri(settings.BaseUrl, "chat/completions"));
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        request.Content = JsonContent.Create(
            new ChatCompletionRequest(
                model,
                [
                    new ChatRequestMessage("system", systemPrompt),
                    new ChatRequestMessage("user", userPrompt)
                ],
                maxTokens),
            options: RequestJsonOptions);

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeoutSource.Token);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(timeoutSource.Token);
            throw new InvalidOperationException(
                AiErrorSanitizer.Sanitize($"AI provider returned HTTP {(int)response.StatusCode}: {error}"));
        }

        var payload = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(
            JsonOptions,
            timeoutSource.Token)
            ?? throw new InvalidOperationException(
                "The AI provider returned an empty response.");

        var content = payload.Choices?
            .FirstOrDefault()?
            .Message?
            .Content?
            .Trim();

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException(
                "The AI provider returned no message content.");
        }

        var inputCharacters = systemPrompt.Length + userPrompt.Length;
        var exactUsage =
            payload.Usage?.PromptTokens is int
            && payload.Usage?.CompletionTokens is int;
        var inputTokens = payload.Usage?.PromptTokens
            ?? AiUsageTracker.EstimateTokens(inputCharacters);
        var outputTokens = payload.Usage?.CompletionTokens
            ?? AiUsageTracker.EstimateTokens(content.Length);
        var cachedTokens = exactUsage ? payload.Usage?.PromptTokensDetails?.CachedTokens ?? 0 : 0;
        var reasoningTokens = exactUsage ? payload.Usage?.CompletionTokensDetails?.ReasoningTokens ?? 0 : 0;

        activity?.ReportUsage(
            new AiTokenUsage(inputTokens, cachedTokens, outputTokens, reasoningTokens, Estimated: !exactUsage));

        usageSink?.Invoke(
            new AiUsageMeasurement(
                DateTimeOffset.UtcNow,
                operation,
                Id,
                model,
                inputCharacters,
                content.Length,
                inputTokens,
                outputTokens,
                Estimated: !exactUsage,
                CacheHit: false,
                ResumedChunk: false)
            {
                CachedInputTokens = cachedTokens,
                ReasoningOutputTokens = reasoningTokens
            });

        return content;
    }

    /// <summary>
    /// Standards-compatible model discovery via <c>GET {base}/models</c>. Providers without that
    /// endpoint report <see cref="AiModelDiscoveryUnsupportedException"/>; manual entry still works.
    /// </summary>
    public async Task<IReadOnlyList<AiModelDescriptor>> ListModelsAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.BaseUrl) || string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException("Base URL and API key are required to list models.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(settings.BaseUrl, "models"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound
            or HttpStatusCode.MethodNotAllowed
            or HttpStatusCode.NotImplemented)
        {
            throw new AiModelDiscoveryUnsupportedException("This provider does not list its models.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Model discovery returned HTTP {(int)response.StatusCode}.");
        }

        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            throw new AiModelDiscoveryUnsupportedException("This provider does not return a model list.");
        }

        using (document)
        {
            var root = document.RootElement;
            var data = root.ValueKind == JsonValueKind.Array
                ? root
                : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var list) && list.ValueKind == JsonValueKind.Array
                    ? list
                    : default;

            if (data.ValueKind != JsonValueKind.Array)
            {
                throw new AiModelDiscoveryUnsupportedException("This provider does not return a model list.");
            }

            return data.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.Object)
                .Select(x => (
                    Id: x.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null,
                    Context: ReadLong(x, "context_length") ?? ReadLong(x, "context_window")))
                .Where(x => AiProfileSettings.IsValidModelId(x.Id))
                .Select(x => AiModelDescriptor.Basic(x.Id!, x.Context))
                .OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    private static long? ReadLong(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var number)
            ? number
            : null;

    private static Uri BuildUri(string baseUrl, string path)
    {
        var normalized = baseUrl.Trim().TrimEnd('/');
        if (Uri.TryCreate(normalized, UriKind.Absolute, out var parsed)
            && (string.IsNullOrWhiteSpace(parsed.AbsolutePath)
                || parsed.AbsolutePath == "/"))
        {
            normalized += "/v1";
        }

        return new Uri(normalized + "/" + path, UriKind.Absolute);
    }

    private static T DeserializeJson<T>(string value)
    {
        var clean = value.Trim();
        if (clean.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = clean.IndexOf('\n');
            var lastFence = clean.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline)
            {
                clean = clean[(firstNewline + 1)..lastFence].Trim();
            }
        }

        var startObject = clean.IndexOf('{');
        var startArray = clean.IndexOf('[');
        var start = startObject < 0
            ? startArray
            : startArray < 0
                ? startObject
                : Math.Min(startObject, startArray);

        if (start > 0)
        {
            clean = clean[start..];
        }

        var parsed = JsonSerializer.Deserialize<T>(clean, JsonOptions);
        return parsed ?? throw new InvalidOperationException(
            "The AI provider returned invalid structured output.");
    }

    private sealed record SentenceExplanationDto(
        string Translation,
        IReadOnlyList<string>? Grammar,
        IReadOnlyList<string>? Colloquial);

    private sealed record MappingEnvelope(
        IReadOnlyList<NovelMappingSuggestion>? Suggestions);

    private sealed record ChatCompletionResponse(
        [property: JsonPropertyName("choices")]
        IReadOnlyList<ChatChoice>? Choices,
        [property: JsonPropertyName("usage")]
        ChatUsage? Usage);

    private sealed record ChatCompletionRequest(
        [property: JsonPropertyName("model")]
        string Model,
        [property: JsonPropertyName("messages")]
        IReadOnlyList<ChatRequestMessage> Messages,
        [property: JsonPropertyName("max_tokens")]
        int? MaxTokens);

    private sealed record ChatRequestMessage(
        [property: JsonPropertyName("role")]
        string Role,
        [property: JsonPropertyName("content")]
        string Content);

    private sealed record ChatUsage(
        [property: JsonPropertyName("prompt_tokens")]
        int? PromptTokens,
        [property: JsonPropertyName("completion_tokens")]
        int? CompletionTokens,
        [property: JsonPropertyName("prompt_tokens_details")]
        PromptTokenDetails? PromptTokensDetails,
        [property: JsonPropertyName("completion_tokens_details")]
        CompletionTokenDetails? CompletionTokensDetails);

    private sealed record PromptTokenDetails(
        [property: JsonPropertyName("cached_tokens")]
        int? CachedTokens);

    private sealed record CompletionTokenDetails(
        [property: JsonPropertyName("reasoning_tokens")]
        int? ReasoningTokens);

    private sealed record ChatChoice(
        [property: JsonPropertyName("message")]
        ChatMessage? Message);

    private sealed record ChatMessage(
        [property: JsonPropertyName("content")]
        string? Content);
}
