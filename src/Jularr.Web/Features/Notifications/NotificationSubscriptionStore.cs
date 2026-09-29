using System.Data;
using System.Data.Common;
using System.Globalization;
using Jularr.Web.Data;
using Jularr.Web.Features.Events;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Notifications;

/// <summary>
/// Per-profile event preferences (raw SQL against "NotificationSubscriptions", migration
/// 20260929240000). A profile with no row for a category gets
/// <see cref="NotificationSubscription.DefaultMode"/>, so new categories are opt-out, not opt-in.
/// </summary>
public sealed class NotificationSubscriptionStore(AppDbContext db)
{
    public async Task<NotificationMode> GetModeAsync(
        string profileId,
        JularrEventCategory category,
        CancellationToken cancellationToken = default)
    {
        var all = await GetAllAsync(profileId, cancellationToken);
        return all.TryGetValue(category, out var mode) ? mode : NotificationSubscription.DefaultMode;
    }

    /// <summary>Every category for a profile, defaulted where no explicit preference was saved.</summary>
    public async Task<IReadOnlyDictionary<JularrEventCategory, NotificationMode>> GetAllAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        var saved = await WithConnectionAsync(
            async connection =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT "Category", "Mode"
                    FROM "NotificationSubscriptions"
                    WHERE "ProfileId" = @profileId;
                    """;
                Add(command, "@profileId", profileId.Trim());

                var rows = new Dictionary<JularrEventCategory, NotificationMode>();
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    rows[(JularrEventCategory)Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture)] =
                        (NotificationMode)Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture);
                }

                return rows;
            },
            cancellationToken);

        var result = new Dictionary<JularrEventCategory, NotificationMode>();
        foreach (var category in Enum.GetValues<JularrEventCategory>())
        {
            result[category] = saved.TryGetValue(category, out var mode) ? mode : NotificationSubscription.DefaultMode;
        }

        return result;
    }

    public async Task SetAsync(
        string profileId,
        JularrEventCategory category,
        NotificationMode mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        await WithConnectionAsync(
            async connection =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    INSERT INTO "NotificationSubscriptions" ("ProfileId", "Category", "Mode", "UpdatedAtUtc")
                    VALUES (@profileId, @category, @mode, @now)
                    ON CONFLICT("ProfileId", "Category") DO UPDATE SET
                        "Mode" = excluded."Mode",
                        "UpdatedAtUtc" = excluded."UpdatedAtUtc";
                    """;
                Add(command, "@profileId", profileId.Trim());
                Add(command, "@category", (int)category);
                Add(command, "@mode", (int)mode);
                Add(command, "@now", Format(DateTime.UtcNow));
                await command.ExecuteNonQueryAsync(cancellationToken);
            },
            cancellationToken);
    }

    private async Task WithConnectionAsync(Func<DbConnection, Task> action, CancellationToken cancellationToken)
    {
        await WithConnectionAsync(
            async connection =>
            {
                await action(connection);
                return true;
            },
            cancellationToken);
    }

    private async Task<T> WithConnectionAsync<T>(Func<DbConnection, Task<T>> action, CancellationToken cancellationToken)
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

    private static string Format(DateTime value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
