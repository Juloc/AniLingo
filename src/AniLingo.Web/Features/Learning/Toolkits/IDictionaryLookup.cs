namespace AniLingo.Web.Features.Learning.Toolkits;

/// <summary>
/// One bundled-dictionary entry for a canonical form. <see cref="Meaning"/> is
/// null when a toolkit can only supply a reading (Japanese morphological
/// fallback when JMdict has no entry) but no gloss.
/// </summary>
public sealed record DictionaryLookupEntry(
    string? Reading,
    string? Meaning,
    string? Language,
    bool Common = false);

/// <summary>
/// Bundled dictionary lookup for a language, resolved through
/// <see cref="AniLingo.Web.Features.Learning.Courses.ILearningLanguageToolkit"/>.
/// Only toolkits that actually bundle a dictionary expose one (Japanese/JMdict);
/// every other language's toolkit has none. Consumers must treat a null
/// <see cref="AniLingo.Web.Features.Learning.Courses.ILearningLanguageToolkit.Dictionary"/>
/// as "no bundled dictionary" and show that state rather than inventing one.
/// </summary>
public interface IDictionaryLookup
{
    DictionaryLookupEntry? Find(string canonical);
}
