using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jularr.Web.Features.Ai;

namespace Jularr.Web.Features.Novels;

public sealed record TranslateGemmaOptions(
    Uri Endpoint,
    string Model,
    int MaxChunkCharacters,
    TimeSpan RequestTimeout)
{
    public const string SectionName = "TranslateGemma";
    public const string DefaultModel = "translategemma-12b-it";
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

        // TranslateGemma is trained for a 2K-token input context. 1200 characters of Japanese stay
        // well inside it together with the instruction; operators can override it.
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
/// How the request carries the translation instruction. Servers differ: Ollama and llama.cpp
/// (generic Gemma template) need the rendered instruction as plain text, the official Hugging Face
/// chat template (vLLM, SGLang, transformers) needs one structured content item, and the
/// vLLM-packaged TranslateGemma models expect a delimiter string.
/// </summary>
public enum TranslateGemmaRequestFormat
{
    Prompt,
    Structured,
    Delimited
}

/// <summary>
/// Client for a local TranslateGemma model behind an OpenAI-compatible chat-completions endpoint.
/// It cannot go through <c>OpenAiCompatibleProvider</c>: TranslateGemma takes one user message
/// with its own instruction and no system prompt, and the endpoint is instance configuration
/// without an API key rather than a per-profile provider. Activity, usage and error sanitising
/// use the same AI infrastructure.
/// </summary>
public sealed partial class TranslateGemmaNovelTranslator
{
    public const string HttpClientName = "translategemma";
    public const string ActivityProviderId = "translategemma";

    /// <summary>Changes whenever the request or paragraph algorithm changes, so old caches are regenerated.</summary>
    public const string AlgorithmVersion = "prompt-batch-v2";

    /// <summary>Consecutive short paragraphs sent together so dialogue keeps its context.</summary>
    public const int MaxBatchParagraphs = 8;

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
    /// Provider id stored with a translation made by this configuration: model name plus a
    /// fingerprint of endpoint, model and algorithm version.
    /// </summary>
    public static string BuildProviderId(TranslateGemmaOptions options)
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
            $"{options.Endpoint.AbsoluteUri}|{options.Model}|{AlgorithmVersion}";
        var fingerprint = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintSource)))
            .ToLowerInvariant()[..12];

        return $"{NovelTranslationProviders.TranslateGemmaPrefix}{safeModel}:{fingerprint}";
    }

    /// <summary>
    /// Fills the empty slots of <paramref name="translated"/>, one slot per source paragraph.
    /// Consecutive short paragraphs of the same language go out as one request and are only
    /// accepted when the answer splits back into the same number of paragraphs; otherwise the
    /// group is halved until it does. Paragraphs without letters (scene breaks, "……") are copied.
    /// Slots already filled (from an earlier failed run) are skipped, and each finished paragraph
    /// stays in the array even when a later one fails. Returns the number of translated characters.
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
        var index = 0;

        while (index < paragraphs.Count)
        {
            activity?.ReportProgress(index, paragraphs.Count);
            if (!string.IsNullOrWhiteSpace(translated[index]))
            {
                index++;
                continue;
            }

            var sourceLanguage = DetectSourceLanguage(paragraphs[index]);
            if (sourceLanguage is null)
            {
                translated[index] = paragraphs[index].Trim();
                index++;
                continue;
            }

            var batch = CollectBatch(paragraphs, translated, index, sourceLanguage);
            await TranslateBatchAsync(
                paragraphs,
                batch,
                sourceLanguage,
                targetLanguageCode,
                translated,
                usage,
                cancellationToken);
            activity?.ReportUsage(usage.ToTokenUsage());
            index = batch[^1] + 1;
        }

        activity?.ReportProgress(paragraphs.Count, paragraphs.Count);
        return translated.Sum(x => x?.Length ?? 0);
    }

    /// <summary>The source language code for a paragraph, or null when it has nothing to translate.</summary>
    public static string? DetectSourceLanguage(string text)
    {
        var hasLetter = false;
        foreach (var character in text)
        {
            if (character is >= '぀' and <= 'ヿ' ||
                character is >= '㐀' and <= '䶿' ||
                character is >= '一' and <= '鿿' ||
                character is >= 'ｦ' and <= 'ﾟ')
            {
                return "ja";
            }

            if (character is >= '가' and <= '힯')
            {
                return "ko";
            }

            hasLetter |= char.IsLetter(character);
        }

        return hasLetter ? "en" : null;
    }

    /// <summary>The instruction of TranslateGemma's official chat template, rendered as plain text.</summary>
    public static string BuildPrompt(string source, string sourceLanguageCode, string targetLanguageCode)
    {
        var sourceName = LanguageName(sourceLanguageCode);
        var targetName = LanguageName(targetLanguageCode);
        return
            $"You are a professional {sourceName} ({sourceLanguageCode}) to {targetName} ({targetLanguageCode}) translator. " +
            $"Your goal is to accurately convey the meaning and nuances of the original {sourceName} text while adhering to " +
            $"{targetName} grammar, vocabulary, and cultural sensitivities.\n" +
            $"Produce only the {targetName} translation, without any additional explanations or commentary. " +
            $"Please translate the following {sourceName} text into {targetName}:\n\n\n{source}";
    }

    private List<int> CollectBatch(
        IReadOnlyList<string> paragraphs,
        string?[] translated,
        int start,
        string sourceLanguage)
    {
        var batch = new List<int> { start };
        var characters = paragraphs[start].Length;

        for (var next = start + 1;
             next < paragraphs.Count && batch.Count < MaxBatchParagraphs;
             next++)
        {
            var paragraph = paragraphs[next];
            if (!string.IsNullOrWhiteSpace(translated[next]) ||
                characters + 2 + paragraph.Length > options.MaxChunkCharacters ||
                DetectSourceLanguage(paragraph) != sourceLanguage)
            {
                break;
            }

            batch.Add(next);
            characters += 2 + paragraph.Length;
        }

        return batch;
    }

    private async Task TranslateBatchAsync(
        IReadOnlyList<string> paragraphs,
        IReadOnlyList<int> batch,
        string sourceLanguageCode,
        string targetLanguageCode,
        string?[] translated,
        UsageTotals usage,
        CancellationToken cancellationToken)
    {
        if (batch.Count == 1)
        {
            translated[batch[0]] = await TranslateParagraphAsync(
                paragraphs[batch[0]],
                sourceLanguageCode,
                targetLanguageCode,
                usage,
                cancellationToken);
            return;
        }

        string? answer;
        try
        {
            answer = await TranslateWithRetryAsync(
                string.Join("\n\n", batch.Select(index => paragraphs[index].Trim())),
                sourceLanguageCode,
                targetLanguageCode,
                usage,
                cancellationToken);
        }
        catch (InvalidOperationException exception) when (exception is not TranslateGemmaTransientException)
        {
            // A rejected group is retried in halves, so one bad paragraph does not cost the rest.
            answer = null;
        }

        if (answer is not null && TrySplitAnswer(answer, batch.Count, out var parts))
        {
            for (var index = 0; index < batch.Count; index++)
            {
                translated[batch[index]] = parts[index];
            }

            return;
        }

        var half = batch.Count / 2;
        await TranslateBatchAsync(
            paragraphs,
            batch.Take(half).ToArray(),
            sourceLanguageCode,
            targetLanguageCode,
            translated,
            usage,
            cancellationToken);
        await TranslateBatchAsync(
            paragraphs,
            batch.Skip(half).ToArray(),
            sourceLanguageCode,
            targetLanguageCode,
            translated,
            usage,
            cancellationToken);
    }

    private async Task<string> TranslateParagraphAsync(
        string paragraph,
        string sourceLanguageCode,
        string targetLanguageCode,
        UsageTotals usage,
        CancellationToken cancellationToken)
    {
        var parts = paragraph.Length <= options.MaxChunkCharacters
            ? [paragraph]
            : NovelTranslationService.ChunkText(paragraph, options.MaxChunkCharacters);

        var translatedParts = new List<string>(parts.Count);
        foreach (var part in parts)
        {
            translatedParts.Add(await TranslateWithRetryAsync(
                part,
                sourceLanguageCode,
                targetLanguageCode,
                usage,
                cancellationToken));
        }

        // One source paragraph stays one stored paragraph: blank lines in the answer would
        // otherwise shift every following paragraph out of alignment.
        return BlankLines().Replace(string.Join(" ", translatedParts), "\n").Trim();
    }

    private static bool TrySplitAnswer(string answer, int expected, out string[] parts)
    {
        parts = BlankLines().Split(answer.Trim())
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToArray();
        if (parts.Length == expected)
        {
            return true;
        }

        parts = answer.Split('\n')
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToArray();
        return parts.Length == expected;
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
                return await SendAsync(
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

    private async Task<string> SendAsync(
        string source,
        string sourceLanguageCode,
        string targetLanguageCode,
        UsageTotals usage,
        CancellationToken cancellationToken)
    {
        try
        {
            // Plain instruction text first (Ollama, llama.cpp, LM Studio). A server whose chat
            // template cannot render it answers 400/422 at once and gets the next format. The
            // choice is not remembered: a 400 can also mean "input too long", and a server that
            // then accepts a format it cannot render would silently translate without instruction.
            string? rejection = null;
            for (var candidate = TranslateGemmaRequestFormat.Prompt;
                 candidate <= TranslateGemmaRequestFormat.Delimited;
                 candidate++)
            {
                var result = await SendOnceAsync(
                    BuildContent(candidate, source, sourceLanguageCode, targetLanguageCode),
                    source,
                    usage,
                    cancellationToken);
                if (result.Translation is not null)
                {
                    return result.Translation;
                }

                rejection = result.Rejection;
            }

            throw new InvalidOperationException(rejection ?? "TranslateGemma rejected the request.");
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

    private static object BuildContent(
        TranslateGemmaRequestFormat requestFormat,
        string source,
        string sourceLanguageCode,
        string targetLanguageCode) =>
        requestFormat switch
        {
            TranslateGemmaRequestFormat.Structured => new object[]
            {
                new
                {
                    type = "text",
                    source_lang_code = sourceLanguageCode,
                    target_lang_code = targetLanguageCode,
                    text = source
                }
            },
            TranslateGemmaRequestFormat.Delimited =>
                $"<<<source>>>{sourceLanguageCode}<<<target>>>{targetLanguageCode}<<<text>>>{source}",
            _ => BuildPrompt(source, sourceLanguageCode, targetLanguageCode)
        };

    private async Task<(string? Translation, string? Rejection)> SendOnceAsync(
        object userContent,
        string source,
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
            var error = await ReadErrorAsync(response, timeout.Token);
            var failure = AiErrorSanitizer.Sanitize(
                    $"TranslateGemma returned HTTP {(int)response.StatusCode}: {error}")
                ?? $"TranslateGemma returned HTTP {(int)response.StatusCode}.";

            if ((int)response.StatusCode is 400 or 422)
            {
                return (null, failure);
            }

            // Overload and server errors are worth another attempt; other rejections are not.
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
            !message.TryGetProperty("content", out var contentElement) ||
            contentElement.ValueKind != JsonValueKind.String)
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
        return (content, null);
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

    private static string NormalizeTargetLanguage(string language) =>
        language.Trim().ToLowerInvariant() switch
        {
            "de" or "de-de" => "de-DE",
            "en" or "en-us" => "en-US",
            "en-gb" => "en-GB",
            var value => value
        };

    private static string LanguageName(string code) =>
        code.Split('-', '_')[0].ToLowerInvariant() switch
        {
            "ja" => "Japanese",
            "de" => "German",
            "en" => "English",
            "ko" => "Korean",
            "zh" => "Chinese",
            "fr" => "French",
            "es" => "Spanish",
            _ => code
        };

    [GeneratedRegex(@"\n\s*\n+", RegexOptions.CultureInvariant)]
    private static partial Regex BlankLines();

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
