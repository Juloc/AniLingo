using System.Text;
using DotNetG2P;
using DotNetG2P.MeCab;

namespace AniLingo.Web.Features.Vocabulary;

public sealed record JapaneseMorphToken(
    string Surface,
    string Canonical,
    string Reading,
    string PartOfSpeech);

public interface IJapaneseMorphology
{
    IReadOnlyList<JapaneseMorphToken> Analyze(string text);
}

public sealed class MeCabJapaneseMorphology : IJapaneseMorphology, IDisposable
{
    public const string DefaultDictionaryPath = "/var/lib/mecab/dic/open-jtalk/naist-jdic";

    private readonly MeCabTokenizer tokenizer;
    private readonly object gate = new();

    public MeCabJapaneseMorphology()
        : this(DefaultDictionaryPath)
    {
    }

    public MeCabJapaneseMorphology(string dictionaryPath)
    {
        tokenizer = new MeCabTokenizer(dictionaryPath);
    }

    public IReadOnlyList<JapaneseMorphToken> Analyze(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var normalized = text.Normalize(NormalizationForm.FormKC);

        lock (gate)
        {
            return tokenizer.Tokenize(normalized)
                .Select(token =>
                {
                    var canonical = token.OriginalForm is "*" or ""
                        ? token.Surface
                        : token.OriginalForm;
                    var reading = token.Reading is "*" or ""
                        ? token.Surface
                        : token.Reading;

                    return new JapaneseMorphToken(
                        token.Surface,
                        canonical,
                        reading,
                        token.POS);
                })
                .ToArray();
        }
    }

    public void Dispose() => tokenizer.Dispose();
}
