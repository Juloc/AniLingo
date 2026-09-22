using AniLingo.Web.Features.Subtitles;

namespace AniLingo.Tests;

[TestClass]
public sealed class SubtitleParserTests
{
    [TestMethod]
    public void ParsesSrtAndNormalizesFormatting()
    {
        const string content = """
            1
            00:00:01,250 --> 00:00:03,500
            <i>何してるの？</i>

            2
            00:00:04,000 --> 00:00:05,200
            まだ諦めてない。
            """;

        var cues = SubtitleParser.ParseSrt(content);

        Assert.AreEqual(2, cues.Count);
        Assert.AreEqual(1250, cues[0].StartMs);
        Assert.AreEqual("何してるの？", cues[0].Text);
    }

    [TestMethod]
    public void ParsesAssDialogueUsingDeclaredFormat()
    {
        const string content = """
            [Script Info]
            Title: Example

            [Events]
            Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
            Dialogue: 0,0:00:01.20,0:00:03.40,Default,,0,0,0,,{\i1}学校に行く{\i0}\N今から
            """;

        var cues = SubtitleParser.ParseAss(content);

        Assert.AreEqual(1, cues.Count);
        Assert.AreEqual(1200, cues[0].StartMs);
        Assert.AreEqual("学校に行く 今から", cues[0].Text);
    }
}
