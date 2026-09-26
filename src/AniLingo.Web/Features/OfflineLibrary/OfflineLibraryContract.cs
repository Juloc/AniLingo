using System.Security.Cryptography;
using System.Text;

namespace AniLingo.Web.Features.OfflineLibrary;

/// <summary>
/// Canonical, versioned offline package contract for Books/Novels (#221 part 1).
/// One server model backs both the PWA (IndexedDB/OPFS) and, later, the
/// Android client: a manifest per work, a payload per chapter and an asset
/// endpoint, all differential by per-chapter hash. Reuses the shared
/// <c>NovelWork</c>/<c>NovelVolume</c>/<c>NovelChapter</c> tables (Features/Novels)
/// that Books and Novels already share; nothing here duplicates that state.
/// </summary>
public static class OfflineLibraryContract
{
    /// <summary>Schema version of the manifest/chapter payload wire shape.</summary>
    public const int SchemaVersion = 1;

    /// <summary>Upper bound of events accepted by one sync request (progress and bookmarks each).</summary>
    public const int MaxSyncBatchItems = 200;

    /// <summary>
    /// Deterministic per-chapter content version: a hash of the chapter's own
    /// source identity plus every cached translation's identity, so a change to
    /// either the original text or a translation changes the chapter's offline
    /// version. Order-independent in the translations (they are sorted first).
    /// </summary>
    public static string ComputeChapterHash(
        string chapterSourceHash,
        IEnumerable<(string TargetLanguage, string ProviderId, int PromptVersion, string SourceHash)> translations)
    {
        var builder = new StringBuilder();
        builder.Append("chapter:v1|").Append(chapterSourceHash).Append('|');

        foreach (var translation in translations
            .OrderBy(x => x.TargetLanguage, StringComparer.Ordinal)
            .ThenBy(x => x.ProviderId, StringComparer.Ordinal))
        {
            builder
                .Append(translation.TargetLanguage).Append(':')
                .Append(translation.ProviderId).Append(':')
                .Append(translation.PromptVersion).Append(':')
                .Append(translation.SourceHash).Append(';');
        }

        return HashHex(builder.ToString());
    }

    /// <summary>
    /// Deterministic whole-work content version: a hash over every chapter's
    /// version in series reading order, plus the metadata that affects offline
    /// presentation (title/author/cover). Changes when a chapter is added,
    /// removed, reordered or edited, or when the metadata clients cache changes.
    /// </summary>
    public static string ComputeWorkContentVersion(
        string title,
        string? author,
        string? coverImageUrl,
        IEnumerable<string> orderedChapterHashes)
    {
        var builder = new StringBuilder();
        builder.Append("work:v1|").Append(title).Append('|')
            .Append(author ?? "").Append('|')
            .Append(coverImageUrl ?? "").Append('|');

        foreach (var hash in orderedChapterHashes)
        {
            builder.Append(hash).Append(';');
        }

        return HashHex(builder.ToString());
    }

    private static string HashHex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
