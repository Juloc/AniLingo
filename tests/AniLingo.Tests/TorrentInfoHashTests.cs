using System.Security.Cryptography;
using System.Text;
using AniLingo.Web.Features.Acquisition.DownloadClients;

namespace AniLingo.Tests;

[TestClass]
public sealed class TorrentInfoHashTests
{
    [TestMethod]
    public void ExtractMagnetHashKeepsFortyCharacterHexAsLowercase()
    {
        var hash = TorrentInfoHash.ExtractMagnetHash(
            "magnet:?xt=urn:btih:ABCDEF0123ABCDEF0123ABCDEF0123ABCDEF0123&dn=test");

        Assert.AreEqual("abcdef0123abcdef0123abcdef0123abcdef0123", hash);
    }

    [TestMethod]
    public void ExtractMagnetHashDecodesThirtyTwoCharacterBase32()
    {
        // 20 zero bytes except the last, which is 1: as base32 that is 31 'A's followed by 'B'.
        var hash = TorrentInfoHash.ExtractMagnetHash(
            "magnet:?xt=urn:btih:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB&dn=test");

        Assert.AreEqual("0000000000000000000000000000000000000001", hash);
    }

    [TestMethod]
    public void ExtractMagnetHashReturnsNullWithoutBtih()
    {
        Assert.IsNull(TorrentInfoHash.ExtractMagnetHash("magnet:?dn=test"));
        Assert.IsNull(TorrentInfoHash.ExtractMagnetHash(""));
    }

    [TestMethod]
    public void ComputeInfoHashIgnoresBytesOutsideTheInfoDictionary()
    {
        var torrentA = BuildTorrent("http://tracker-a.example", "test", 10);
        var torrentB = BuildTorrent("http://a-differently-sized-tracker-url.example", "test", 10);

        Assert.AreEqual(TorrentInfoHash.ComputeInfoHash(torrentA), TorrentInfoHash.ComputeInfoHash(torrentB));
    }

    [TestMethod]
    public void ComputeInfoHashChangesWhenTheInfoDictionaryChanges()
    {
        var torrentA = BuildTorrent("http://tracker.example", "test", 10);
        var torrentB = BuildTorrent("http://tracker.example", "test", 11);

        Assert.AreNotEqual(TorrentInfoHash.ComputeInfoHash(torrentA), TorrentInfoHash.ComputeInfoHash(torrentB));
    }

    [TestMethod]
    public void ComputeInfoHashMatchesShaOfTheRawInfoBytes()
    {
        var torrent = BuildTorrent("http://tracker.example", "test", 10);
        var expectedInfoBytes = Encoding.ASCII.GetBytes(
            "d6:lengthi10e4:name4:test12:piece lengthi16384e6:pieces20:AAAAAAAAAAAAAAAAAAAAe");
        var expected = Convert.ToHexString(SHA1.HashData(expectedInfoBytes)).ToLowerInvariant();

        Assert.AreEqual(expected, TorrentInfoHash.ComputeInfoHash(torrent));
    }

    [TestMethod]
    public void ComputeInfoHashRejectsInvalidTorrentBytes()
    {
        Assert.ThrowsExactly<FormatException>(() => TorrentInfoHash.ComputeInfoHash([]));
        Assert.ThrowsExactly<FormatException>(() => TorrentInfoHash.ComputeInfoHash("not bencode"u8.ToArray()));
        Assert.ThrowsExactly<FormatException>(() => TorrentInfoHash.ComputeInfoHash("d8:announce4:teste"u8.ToArray()));
    }

    private static byte[] BuildTorrent(string announce, string name, int length)
    {
        var info = $"d6:lengthi{length}e4:name{name.Length}:{name}12:piece lengthi16384e6:pieces20:AAAAAAAAAAAAAAAAAAAAe";
        var full = $"d8:announce{announce.Length}:{announce}4:info{info}e";
        return Encoding.ASCII.GetBytes(full);
    }
}
