using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AniLingo.Web.Features.Acquisition.DownloadClients;

/// <summary>
/// Computes a BitTorrent v1 info-hash: the SHA-1 hash of the raw bencoded
/// bytes of the torrent file's "info" dictionary. qBittorrent's add-torrent
/// endpoint does not return the hash of an uploaded file, so this is
/// computed locally to track the resulting torrent (used as the download
/// client's external ID).
/// </summary>
public static class TorrentInfoHash
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private static readonly Regex MagnetHashPattern =
        new("urn:btih:([A-Za-z0-9]+)", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));

    public static string ComputeInfoHash(byte[] torrentBytes)
    {
        ArgumentNullException.ThrowIfNull(torrentBytes);
        if (torrentBytes.Length == 0 || torrentBytes[0] != (byte)'d')
        {
            throw new FormatException("Not a valid bencoded torrent file.");
        }

        var cursor = 1;
        while (cursor < torrentBytes.Length && torrentBytes[cursor] != (byte)'e')
        {
            cursor = SkipString(torrentBytes, cursor, out var contentStart, out var contentLength);
            var key = Encoding.ASCII.GetString(torrentBytes, contentStart, contentLength);

            if (key == "info")
            {
                var valueStart = cursor;
                var valueEnd = SkipValue(torrentBytes, cursor);
                var infoBytes = torrentBytes[valueStart..valueEnd];
                return Convert.ToHexString(SHA1.HashData(infoBytes)).ToLowerInvariant();
            }

            cursor = SkipValue(torrentBytes, cursor);
        }

        throw new FormatException("Torrent file has no 'info' dictionary.");
    }

    public static string? ExtractMagnetHash(string magnetUri)
    {
        if (string.IsNullOrWhiteSpace(magnetUri))
        {
            return null;
        }

        var match = MagnetHashPattern.Match(magnetUri);
        if (!match.Success)
        {
            return null;
        }

        var raw = match.Groups[1].Value;
        return raw.Length switch
        {
            40 => raw.ToLowerInvariant(),
            32 => Convert.ToHexString(Base32Decode(raw)).ToLowerInvariant(),
            _ => raw.ToLowerInvariant()
        };
    }

    private static int SkipString(
        byte[] data,
        int index,
        out int contentStart,
        out int contentLength)
    {
        var start = index;
        while (data[index] != (byte)':')
        {
            index++;
        }

        var length = int.Parse(
            Encoding.ASCII.GetString(data, start, index - start),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture);
        index++;
        contentStart = index;
        contentLength = length;
        return index + length;
    }

    private static int SkipValue(byte[] data, int index)
    {
        var token = (char)data[index];

        if (token == 'i')
        {
            var end = index + 1;
            while (data[end] != (byte)'e')
            {
                end++;
            }

            return end + 1;
        }

        if (token == 'l')
        {
            var cursor = index + 1;
            while (data[cursor] != (byte)'e')
            {
                cursor = SkipValue(data, cursor);
            }

            return cursor + 1;
        }

        if (token == 'd')
        {
            var cursor = index + 1;
            while (data[cursor] != (byte)'e')
            {
                cursor = SkipString(data, cursor, out _, out _);
                cursor = SkipValue(data, cursor);
            }

            return cursor + 1;
        }

        if (char.IsAsciiDigit(token))
        {
            return SkipString(data, index, out _, out _);
        }

        throw new FormatException($"Invalid bencode token '{token}'.");
    }

    private static byte[] Base32Decode(string input)
    {
        var cleaned = input.TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>(cleaned.Length * 5 / 8 + 1);
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var character in cleaned)
        {
            var value = Base32Alphabet.IndexOf(character);
            if (value < 0)
            {
                continue;
            }

            buffer = (buffer << 5) | value;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                output.Add((byte)((buffer >> (bitsLeft - 8)) & 0xFF));
                bitsLeft -= 8;
            }
        }

        return output.ToArray();
    }
}
