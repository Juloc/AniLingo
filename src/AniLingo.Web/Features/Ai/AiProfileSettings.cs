namespace AniLingo.Web.Features.Ai;

public static class AiProviderIds
{
    public const string Server = "server";
    public const string OpenAiCompatible = "openai-compatible";

    public static bool IsSupported(string? value) =>
        string.Equals(value, Server, StringComparison.Ordinal)
        || string.Equals(value, OpenAiCompatible, StringComparison.Ordinal);
}

public enum AiTranslationMode
{
    Efficient = 0,
    Quality = 1,
    Maximum = 2
}

public sealed record AiProfileSettings(
    string ProviderId,
    string? BaseUrl,
    string? Model,
    string? ApiKey,
    AiTranslationMode TranslationMode)
{
    public static AiProfileSettings Default { get; } =
        new(
            AiProviderIds.Server,
            null,
            null,
            null,
            AiTranslationMode.Efficient);
}
