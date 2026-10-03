using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.Import;

namespace Jularr.Web.Features.Games;

public sealed record GameAcquisitionPayload(
    string? PlatformKey = null,
    string? Region = null,
    string? Revision = null)
{
    public static GameAcquisitionPayload FromRequest(AcquisitionRequest? request)
    {
        if (string.IsNullOrWhiteSpace(request?.PayloadJson))
        {
            return new GameAcquisitionPayload();
        }

        try
        {
            return JsonSerializer.Deserialize<GameAcquisitionPayload>(
                       request.PayloadJson,
                       new JsonSerializerOptions(JsonSerializerDefaults.Web))
                   ?? new GameAcquisitionPayload();
        }
        catch (JsonException)
        {
            return new GameAcquisitionPayload();
        }
    }
}

public sealed record GamePlatformDefinition(
    string Key,
    string DisplayName,
    IReadOnlySet<string> Extensions);

public sealed record GameImportEvidenceSummary(
    string? RequestedPlatformKey,
    string? DetectedPlatformKey,
    bool PlatformConflict,
    IReadOnlyList<string> Extensions);

public static class GameImportDetection
{
    private static readonly IReadOnlyDictionary<string, GamePlatformDefinition> Platforms =
        new[]
        {
            new GamePlatformDefinition("gb", "Game Boy", Set(".gb")),
            new GamePlatformDefinition("gbc", "Game Boy Color", Set(".gbc")),
            new GamePlatformDefinition("gba", "Game Boy Advance", Set(".gba")),
            new GamePlatformDefinition("nds", "Nintendo DS", Set(".nds")),
            new GamePlatformDefinition("nes", "Nintendo Entertainment System", Set(".nes")),
            new GamePlatformDefinition("snes", "Super Nintendo", Set(".sfc", ".smc")),
            new GamePlatformDefinition("n64", "Nintendo 64", Set(".n64", ".z64", ".v64")),
            new GamePlatformDefinition("megadrive", "Mega Drive / Genesis", Set(".gen")),
            new GamePlatformDefinition("sms", "Sega Master System", Set(".sms")),
            new GamePlatformDefinition("gamegear", "Sega Game Gear", Set(".gg")),
            new GamePlatformDefinition("psx", "PlayStation 1", Set(".cue"))
        }
        .ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ObviousJunkExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".nfo", ".jpg", ".jpeg", ".png", ".gif",
            ".nzb", ".par2", ".sfv"
        };

    public static GamePlatformDefinition? Platform(string? key) =>
        !string.IsNullOrWhiteSpace(key) && Platforms.TryGetValue(key.Trim(), out var platform)
            ? platform
            : null;

    public static string? DetectPlatformKey(IReadOnlyList<CompletedDownloadFile> files)
    {
        var keys = files
            .Select(file => Path.GetExtension(file.Path))
            .Where(extension => extension.Length > 0)
            .Select(extension => Platforms.Values.FirstOrDefault(platform => platform.Extensions.Contains(extension))?.Key)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return keys.Length == 1 ? keys[0] : null;
    }

    /// <summary>
    /// Files that are useful import evidence. We keep unknown binaries because a known request may
    /// identify an otherwise ambiguous disc/container, but drop obvious download metadata/artwork.
    /// </summary>
    public static IReadOnlyList<CompletedDownloadFile> EvidenceFiles(
        IReadOnlyList<CompletedDownloadFile> files) =>
        files
            .Where(file => !ObviousJunkExtensions.Contains(Path.GetExtension(file.Path)))
            .OrderBy(file => file.Path, StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<CompletedDownloadFile> FilesForPlatform(
        IReadOnlyList<CompletedDownloadFile> evidenceFiles,
        string platformKey)
    {
        var platform = Platform(platformKey);
        if (platform is null)
        {
            return [];
        }

        if (string.Equals(platform.Key, "psx", StringComparison.OrdinalIgnoreCase))
        {
            // cue/bin is a set: the cue proves the platform, while its referenced tracks must travel
            // with it. CHD is accepted only after PS1 identity came from the request/manual resolution.
            var hasCue = evidenceFiles.Any(file =>
                string.Equals(Path.GetExtension(file.Path), ".cue", StringComparison.OrdinalIgnoreCase));
            return evidenceFiles
                .Where(file =>
                {
                    var extension = Path.GetExtension(file.Path);
                    return string.Equals(extension, ".cue", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(extension, ".bin", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(extension, ".chd", StringComparison.OrdinalIgnoreCase)
                           || (hasCue && string.Equals(extension, ".img", StringComparison.OrdinalIgnoreCase));
                })
                .ToArray();
        }

        return evidenceFiles
            .Where(file => platform.Extensions.Contains(Path.GetExtension(file.Path)))
            .ToArray();
    }

    public static async Task<IReadOnlyList<GameImportFileFingerprint>> FingerprintAsync(
        string sourcePath,
        IReadOnlyList<CompletedDownloadFile> files,
        CancellationToken cancellationToken)
    {
        var sourceIsDirectory = Directory.Exists(sourcePath);
        var root = sourceIsDirectory
            ? Path.GetFullPath(sourcePath)
            : Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;

        var result = new List<GameImportFileFingerprint>(files.Count);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var full = Path.GetFullPath(file.Path);
            var relative = sourceIsDirectory
                ? Path.GetRelativePath(root, full)
                : Path.GetFileName(full);
            EnsureSafeRelativePath(relative);

            await using var stream = new FileStream(
                full,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken);
            result.Add(
                new GameImportFileFingerprint(
                    relative.Replace('\\', '/'),
                    file.SizeBytes,
                    Convert.ToHexString(hash).ToLowerInvariant(),
                    full));
        }

        return result
            .OrderBy(x => x.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    public static string ManifestFingerprint(IReadOnlyList<GameImportFileFingerprint> files)
    {
        using var sha = SHA256.Create();
        var text = string.Join(
            "\n",
            files.OrderBy(x => x.RelativePath, StringComparer.Ordinal)
                .Select(x => $"{x.RelativePath}\t{x.SizeBytes}\t{x.Sha256}"));
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    public static string SourceDisplayName(string sourcePath)
    {
        var trimmed = sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetFileName(trimmed);
    }

    public static string SafeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            builder.Append(char.IsControl(ch) || invalid.Contains(ch) || ch is '/' or '\\' ? '_' : ch);
        }

        var result = builder.ToString().Trim(' ', '.', '_');
        if (result is "" or "." or "..")
        {
            return "_";
        }

        return result.Length <= 180 ? result : result[..180].TrimEnd();
    }

    public static string SafeDestinationPath(string destinationRoot, string relativePath)
    {
        EnsureSafeRelativePath(relativePath);
        var root = Path.GetFullPath(destinationRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var destination = Path.GetFullPath(
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !destination.Equals(root, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Game import destination escaped the configured LibraryRoot.");
        }

        return destination;
    }

    public static void EnsureSafeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || Path.IsPathRooted(relativePath)
            || relativePath.Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment is "." or ".."))
        {
            throw new InvalidDataException("Game import contains an unsafe relative path.");
        }
    }

    private static IReadOnlySet<string> Set(params string[] values) =>
        new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
}

public sealed record GameImportFileFingerprint(
    string RelativePath,
    long SizeBytes,
    string Sha256,
    string SourcePath);
