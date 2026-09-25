using System.Reflection;
using AniLingo.Web.Data;

namespace AniLingo.Web.Features.Appearance;

public static class AppTheme
{
    public const string System = "system";
    public const string Light = "light";
    public const string Dark = "dark";

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized is System or Light or Dark)
        {
            return true;
        }

        normalized = System;
        return false;
    }

    public static string NormalizeOrSystem(string? value) =>
        TryNormalize(value, out var normalized)
            ? normalized
            : System;
}

public static class AppBuildInfo
{
    public static string Version { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        var assembly = typeof(AppDbContext).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            var metadataSeparator = informational.IndexOf('+');
            return metadataSeparator >= 0
                ? informational[..metadataSeparator]
                : informational;
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }
}
