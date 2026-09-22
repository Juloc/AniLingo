using AniLingo.Web.Features.Vocabulary;

namespace AniLingo.Tests;

[TestClass]
public sealed class JapaneseDictionaryTests
{
    private string directory = "";

    [TestInitialize]
    public void Initialize()
    {
        directory = Path.Combine(Path.GetTempPath(), $"anilingo-dictionary-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void PrefersGermanAndUsesEnglishFallback()
    {
        File.WriteAllLines(
            Path.Combine(directory, "jmdict-ger.tsv"),
            [
                "食べる\tたべる\t1\tessen",
                "学校\tがっこう\t1\tSchule"
            ]);

        File.WriteAllLines(
            Path.Combine(directory, "jmdict-eng-common.tsv"),
            [
                "食べる\tたべる\t1\tto eat",
                "走る\tはしる\t1\tto run"
            ]);

        var dictionary = new JapaneseDictionary(directory);

        var eat = dictionary.Find("食べる");
        var run = dictionary.Find("走る");

        Assert.IsNotNull(eat);
        Assert.AreEqual("essen", eat.Meaning);
        Assert.AreEqual("de", eat.Language);

        Assert.IsNotNull(run);
        Assert.AreEqual("to run", run.Meaning);
        Assert.AreEqual("en", run.Language);
    }

    [TestMethod]
    public void PrefersCommonGermanVariantForSameWriting()
    {
        File.WriteAllLines(
            Path.Combine(directory, "jmdict-ger.tsv"),
            [
                "言葉\tことば\t0\tAusdruck",
                "言葉\tことば\t1\tWort"
            ]);

        File.WriteAllText(Path.Combine(directory, "jmdict-eng-common.tsv"), "");

        var dictionary = new JapaneseDictionary(directory);

        Assert.AreEqual("Wort", dictionary.Find("言葉")?.Meaning);
    }
}
