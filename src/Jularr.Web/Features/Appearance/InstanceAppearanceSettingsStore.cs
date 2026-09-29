using System.Data;
using System.Data.Common;
using System.Globalization;
using Jularr.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Appearance;

/// <summary>
/// The one instance-wide appearance policy. Profiles may only override the default when this
/// policy permits it; the layout resolves every request through this store.
/// </summary>
public sealed record InstanceAppearanceSettings(
    string DefaultThemeId,
    bool AllowProfileThemeOverride,
    bool AllowProfileAccentOverride)
{
    public static InstanceAppearanceSettings Default { get; } = new(
        ThemeCatalog.CleanPurple,
        AllowProfileThemeOverride: true,
        AllowProfileAccentOverride: true);
}

public sealed class InstanceAppearanceSettingsStore(AppDbContext db)
{
    public async Task<InstanceAppearanceSettings> LoadAsync(CancellationToken cancellationToken)
    {
        return await WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT "DefaultThemeId", "AllowProfileThemeOverride", "AllowProfileAccentOverride"
                FROM "InstanceAppearanceSettings"
                WHERE "Id" = 1;
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return InstanceAppearanceSettings.Default;
            }

            return new InstanceAppearanceSettings(
                ThemeCatalog.NormalizeOrOriginal(reader.GetString(0)),
                reader.GetInt32(1) != 0,
                reader.GetInt32(2) != 0);
        }, cancellationToken);
    }

    public async Task<InstanceAppearanceSettings> SaveAsync(
        InstanceAppearanceSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = settings with
        {
            DefaultThemeId = ThemeCatalog.NormalizeOrOriginal(settings.DefaultThemeId)
        };

        await WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO "InstanceAppearanceSettings" (
                    "Id", "DefaultThemeId", "AllowProfileThemeOverride", "AllowProfileAccentOverride", "UpdatedAt")
                VALUES (1, @theme, @allowTheme, @allowAccent, @updatedAt)
                ON CONFLICT("Id") DO UPDATE SET
                    "DefaultThemeId" = excluded."DefaultThemeId",
                    "AllowProfileThemeOverride" = excluded."AllowProfileThemeOverride",
                    "AllowProfileAccentOverride" = excluded."AllowProfileAccentOverride",
                    "UpdatedAt" = excluded."UpdatedAt";
                """;
            Add(command, "@theme", normalized.DefaultThemeId);
            Add(command, "@allowTheme", normalized.AllowProfileThemeOverride ? 1 : 0);
            Add(command, "@allowAccent", normalized.AllowProfileAccentOverride ? 1 : 0);
            Add(command, "@updatedAt", DateTime.UtcNow);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }, cancellationToken);

        return normalized;
    }

    private async Task<T> WithConnectionAsync<T>(Func<DbConnection, Task<T>> action, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(cancellationToken);
        try { return await action(connection); }
        finally { if (openedHere) await connection.CloseAsync(); }
    }

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value is DateTime timestamp
            ? timestamp.ToString("O", CultureInfo.InvariantCulture)
            : value;
        command.Parameters.Add(parameter);
    }
}
