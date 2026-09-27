using System.Text.Json;

namespace Jularr.Web.Features.Media.Optimization;

public enum MediaOptimizationStage
{
    // ffmpeg is writing the partial output; the source is the only library file.
    Remuxing,
    // The verified output is being adopted: renamed into place, then recorded, then the source removed.
    Committing
}

public sealed record MediaOptimizationJournalEntry(
    Guid MediaFileId,
    string SourcePath,
    string TargetPath,
    string PartialPath,
    long SourceSizeBytes,
    MediaOptimizationStage Stage,
    long? OutputSizeBytes,
    DateTime CreatedAtUtc);

// Durable record of an optimization in flight, one small file per media file. After a crash or
// restart it tells recovery which partial output to discard and whether an interrupted commit
// rolls back (the source stays canonical) or forward (the database already names the output).
public sealed class MediaOptimizationJournal
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string directory;

    public MediaOptimizationJournal(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        this.directory = directory;
    }

    public void Write(MediaOptimizationJournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Directory.CreateDirectory(directory);
        var path = PathFor(entry.MediaFileId);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(entry, JsonOptions));
        File.Move(temporary, path, overwrite: true);
    }

    public MediaOptimizationJournalEntry? Read(Guid mediaFileId)
    {
        var path = PathFor(mediaFileId);
        return File.Exists(path) ? Deserialize(path) : null;
    }

    public IReadOnlyList<MediaOptimizationJournalEntry> ReadAll()
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return
        [
            .. Directory.EnumerateFiles(directory, "*.json")
                .Select(Deserialize)
                .OfType<MediaOptimizationJournalEntry>()
        ];
    }

    public bool HasEntries() =>
        Directory.Exists(directory) && Directory.EnumerateFiles(directory, "*.json").Any();

    public void Delete(Guid mediaFileId)
    {
        var path = PathFor(mediaFileId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string PathFor(Guid mediaFileId) =>
        Path.Combine(directory, $"{mediaFileId:N}.json");

    private static MediaOptimizationJournalEntry? Deserialize(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<MediaOptimizationJournalEntry>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException)
        {
            // Only a torn write produces this; it carries nothing recovery could act on.
            File.Delete(path);
            return null;
        }
    }
}
