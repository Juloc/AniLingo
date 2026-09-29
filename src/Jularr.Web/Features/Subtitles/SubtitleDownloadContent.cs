using System.Text;

namespace Jularr.Web.Features.Subtitles;

/// <summary>
/// The one place downloaded subtitle bytes are bounded and decoded, shared by Jimaku
/// (<see cref="SubtitleImportService"/>) and every <see cref="ISubtitleProvider"/> so a provider can
/// neither exhaust memory with an oversized body nor disagree on text decoding.
/// </summary>
public static class SubtitleDownloadContent
{
    /// <summary>Largest subtitle file (text) Jularr will parse.</summary>
    public const int MaxSubtitleBytes = 8 * 1024 * 1024;

    /// <summary>UTF-8 text with any byte-order mark removed.</summary>
    public static string Decode(byte[] bytes)
    {
        var content = Encoding.UTF8.GetString(bytes);
        return content.Length > 0 && content[0] == '﻿'
            ? content[1..]
            : content;
    }

    /// <summary>The response body, or <see langword="null"/> when it is larger than <paramref name="maxBytes"/>.</summary>
    public static async Task<byte[]?> ReadLimitedBytesAsync(
        HttpContent content,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[81920];

        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return output.ToArray();
            }

            if (output.Length + read > maxBytes)
            {
                return null;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
}
