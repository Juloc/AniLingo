using System.Collections.Concurrent;
using System.Text.Json;

namespace Jularr.Web.Features.StoryContext;

/// <summary>
/// Persists one <see cref="StoryContextDocument"/> per work as a small JSON
/// document under Jularr application data. Writes are atomic
/// (temp file + move) and serialized per work.
/// </summary>
public sealed class StoryContextStore
{
    public const string DefaultRoot = "/data/story-context";
    private const int MaxBytes = 4 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates =
        new(StringComparer.Ordinal);

    public StoryContextStore(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException(
                "Story context root path is required.",
                nameof(rootPath));
        }

        RootPath = Path.GetFullPath(rootPath);
    }

    public string RootPath { get; }

    /// <summary>
    /// <c>StoryContext:Path</c> when configured; otherwise beside a custom
    /// translation-memory root, otherwise <see cref="DefaultRoot"/>.
    /// </summary>
    public static string ResolveRoot(IConfiguration configuration)
    {
        var configured = configuration["StoryContext:Path"]?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var memoryPath = configuration["Books:Translation:MemoryPath"]?.Trim();
        return string.IsNullOrWhiteSpace(memoryPath)
            ? DefaultRoot
            : Path.Combine(memoryPath, "story-context");
    }

    public static StoryContextStore FromConfiguration(IConfiguration configuration) =>
        new(ResolveRoot(configuration));

    public async Task<StoryContextDocument?> LoadAsync(
        Guid workId,
        CancellationToken cancellationToken)
    {
        var path = GetPath(workId);
        var gate = GateFor(path);

        await gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadCoreAsync(path, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Loads (or creates) the work's document, applies <paramref name="mutation"/>
    /// and persists it when the mutation reports a change.
    /// </summary>
    public Task<StoryContextDocument> UpdateAsync(
        Guid workId,
        string? sourceLanguage,
        Func<StoryContextDocument, bool> mutation,
        CancellationToken cancellationToken) =>
        UpdateCoreAsync(
            workId,
            sourceLanguage,
            createWhenMissing: true,
            mutation,
            cancellationToken)!;

    /// <summary>Like <see cref="UpdateAsync"/> but returns null instead of creating a document.</summary>
    public Task<StoryContextDocument?> UpdateExistingAsync(
        Guid workId,
        Func<StoryContextDocument, bool> mutation,
        CancellationToken cancellationToken) =>
        UpdateCoreAsync(
            workId,
            sourceLanguage: null,
            createWhenMissing: false,
            mutation,
            cancellationToken);

    public async Task DeleteAsync(
        Guid workId,
        CancellationToken cancellationToken)
    {
        var path = GetPath(workId);
        var gate = GateFor(path);

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<StoryContextDocument?> UpdateCoreAsync(
        Guid workId,
        string? sourceLanguage,
        bool createWhenMissing,
        Func<StoryContextDocument, bool> mutation,
        CancellationToken cancellationToken)
    {
        var path = GetPath(workId);
        var gate = GateFor(path);

        await gate.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadCoreAsync(path, cancellationToken);
            var created = false;

            if (document is null)
            {
                if (!createWhenMissing)
                {
                    return null;
                }

                document = new StoryContextDocument
                {
                    WorkId = workId,
                    SourceLanguage = StoryText.NormalizeLanguage(sourceLanguage)
                };
                created = true;
            }
            else if (document.SourceLanguage == "und"
                && !string.IsNullOrWhiteSpace(sourceLanguage))
            {
                document.SourceLanguage = StoryText.NormalizeLanguage(sourceLanguage);
            }

            var changed = mutation(document);
            if (changed || created)
            {
                StoryContextMerge.Normalize(document);
                document.Revision++;
                document.UpdatedAt = DateTime.UtcNow;
                await SaveCoreAsync(document, path, cancellationToken);
            }

            return document;
        }
        finally
        {
            gate.Release();
        }
    }

    private string GetPath(Guid workId) =>
        Path.Combine(RootPath, workId.ToString("N") + ".json");

    private static SemaphoreSlim GateFor(string path) =>
        Gates.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));

    private static async Task<StoryContextDocument?> LoadCoreAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > MaxBytes)
            {
                return null;
            }

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                useAsync: true);

            var document = await JsonSerializer.DeserializeAsync<StoryContextDocument>(
                stream,
                JsonOptions,
                cancellationToken);

            return document is null || document.Version > StoryContextDocument.CurrentVersion
                ? null
                : document;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            return null;
        }
    }

    private static async Task SaveCoreAsync(
        StoryContextDocument document,
        string path,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                "Story context directory is unavailable.");
        Directory.CreateDirectory(directory);

        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        await using (var stream = new FileStream(
            temporary,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                document,
                JsonOptions,
                cancellationToken);
        }

        if (new FileInfo(temporary).Length > MaxBytes)
        {
            File.Delete(temporary);
            throw new InvalidOperationException(
                "Story context exceeded the 4 MB safety limit.");
        }

        File.Move(temporary, path, overwrite: true);
    }
}

internal static class StoryText
{
    public static string? Clean(string? value, int maxLength)
    {
        var clean = value?.Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            return null;
        }

        return clean.Length <= maxLength
            ? clean
            : clean[..maxLength];
    }

    public static string? Prefer(string? primary, string? fallback) =>
        string.IsNullOrWhiteSpace(primary)
            ? fallback
            : primary;

    public static string NormalizeLanguage(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized)
            ? "und"
            : normalized;
    }
}
