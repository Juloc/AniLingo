using System.Data;
using System.Data.Common;
using System.Globalization;
using AniLingo.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Appearance;

/// <summary>A profile's appearance: theme mode and optional accent seed (null = Jularr red).</summary>
public sealed record ProfileAppearance(string ThemeMode, string? AccentColor)
{
    public static ProfileAppearance Default { get; } = new(AppTheme.System, null);

    public AccentPalette Palette => AccentPalette.Build(AccentColor);
}

/// <summary>
/// The single place that persists per-profile appearance (the <c>UiProfileThemes</c> row).
/// </summary>
public sealed class ProfileAppearanceStore(AppDbContext db)
{
    public async Task<ProfileAppearance> GetAsync(
        string? profileId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return ProfileAppearance.Default;
        }

        return await WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT "ThemeMode", "AccentColor"
                FROM "UiProfileThemes"
                WHERE "ProfileId" = $profileId
                LIMIT 1;
                """;
            Add(command, "$profileId", profileId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return ProfileAppearance.Default;
            }

            var mode = AppTheme.NormalizeOrSystem(reader.IsDBNull(0) ? null : reader.GetString(0));
            var accent = reader.IsDBNull(1) ? null : reader.GetString(1);
            return new ProfileAppearance(
                mode,
                AppAccent.TryNormalize(accent, out var normalized) ? normalized : null);
        }, cancellationToken);
    }

    public async Task SetThemeAsync(
        string profileId,
        string theme,
        CancellationToken cancellationToken)
    {
        RequireProfile(profileId);
        if (!AppTheme.TryNormalize(theme, out var normalized))
        {
            throw new ArgumentException("Theme must be system, light or dark.", nameof(theme));
        }

        await WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO "UiProfileThemes" ("ProfileId", "ThemeMode", "UpdatedAt")
                VALUES ($profileId, $theme, $updatedAt)
                ON CONFLICT("ProfileId") DO UPDATE SET
                    "ThemeMode" = excluded."ThemeMode",
                    "UpdatedAt" = excluded."UpdatedAt";
                """;
            Add(command, "$profileId", profileId);
            Add(command, "$theme", normalized);
            Add(command, "$updatedAt", DateTime.UtcNow);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

    /// <summary>Stores the accent seed; an empty value resets the profile to the brand red.</summary>
    public async Task<string?> SetAccentAsync(
        string profileId,
        string? accent,
        CancellationToken cancellationToken)
    {
        RequireProfile(profileId);
        if (!AppAccent.TryNormalize(accent, out var normalized))
        {
            throw new ArgumentException("Accent must be a #rrggbb colour.", nameof(accent));
        }

        await WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO "UiProfileThemes" ("ProfileId", "ThemeMode", "AccentColor", "UpdatedAt")
                VALUES ($profileId, 'system', $accent, $updatedAt)
                ON CONFLICT("ProfileId") DO UPDATE SET
                    "AccentColor" = excluded."AccentColor",
                    "UpdatedAt" = excluded."UpdatedAt";
                """;
            Add(command, "$profileId", profileId);
            Add(command, "$accent", normalized);
            Add(command, "$updatedAt", DateTime.UtcNow);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }, cancellationToken);

        return normalized;
    }

    private async Task<T> WithConnectionAsync<T>(
        Func<DbConnection, Task<T>> action,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            return await action(connection);
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static void RequireProfile(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException("Profile ID is required.", nameof(profileId));
        }
    }

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value switch
        {
            null => DBNull.Value,
            DateTime timestamp => timestamp.ToString("O", CultureInfo.InvariantCulture),
            _ => value
        };
        command.Parameters.Add(parameter);
    }
}
