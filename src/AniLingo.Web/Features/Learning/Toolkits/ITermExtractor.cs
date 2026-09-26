namespace AniLingo.Web.Features.Learning.Toolkits;

/// <summary>
/// One lexical candidate extracted from content text: a dictionary-form
/// canonical and, when the toolkit produces one, a reading (Japanese kana;
/// generic tokenizers never produce a reading).
/// </summary>
public sealed record TermCandidate(string Canonical, string? Reading = null);

/// <summary>
/// Language-specific tokenization/term extraction, resolved through
/// <see cref="AniLingo.Web.Features.Learning.Courses.ILearningLanguageToolkit"/>.
/// Japanese uses <c>JapaneseTermExtractor</c> (MeCab); every other language uses
/// <see cref="GenericTermExtractor"/> (Unicode word boundaries, case folding, an
/// optional per-language stopword list). Callers must not run their own
/// language-detection to pick an extractor; the toolkit is the single place
/// that decides which implementation backs a language tag.
/// </summary>
public interface ITermExtractor
{
    IReadOnlyList<TermCandidate> Extract(string text);
}
