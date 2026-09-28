using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Jularr.Web.Data;
using Jularr.Web.Features.Events;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Notifications;

/// <summary>
/// The in-app notification inbox (raw SQL against "Notifications", migration 20260929240000):
/// one durable, profile-scoped row per delivered event. When a draft carries a
/// <see cref="NotificationDraft.DedupKey"/> that already has a row for the same profile, the
/// existing row's occurrence count is bumped and it is surfaced as unread again instead of
/// inserting a duplicate — restarts, provider refreshes and repeated polling must not spam a
/// profile with the same underlying event (#429 "Deduplication").
/// </summary>
public sealed class NotificationStore(AppDbContext db)
{
    private const int MaxMediaTypeLength = 40;
    private const int MaxSubjectLength = 240;
    private const int MaxDeepLinkLength = 400;
    private const int MaxDedupKeyLength = 200;

    public async Task<Guid> CreateAsync(NotificationDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.ProfileId);

        var now = DateTime.UtcNow;
        var profileId = draft.ProfileId.Trim();
        var dedupKey = Trim(draft.DedupKey, MaxDedupKeyLength);
        var paramsJson = draft.MessageParams is { Count: > 0 } parameters
            ? JsonSerializer.Serialize(parameters)
            : null;

        return await WithConnectionAsync(
            async connection =>
            {
                if (dedupKey is not null)
                {
                    await using var find = connection.CreateCommand();
                    find.CommandText =
                        """
                        SELECT Id FROM Notifications
                        WHERE ProfileId = @profileId AND DedupKey = @dedupKey
                        LIMIT 1;
                        """;
                    Add(find, "@profileId", profileId);
                    Add(find, "@dedupKey", dedupKey);
                    var existing = await find.ExecuteScalarAsync(cancellationToken);
                    if (existing is string existingId)
                    {
                        await using var update = connection.CreateCommand();
                        update.CommandText =
                            """
                            UPDATE Notifications
                            SET OccurrenceCount = OccurrenceCount + 1,
                                MessageParamsJson = @messageParamsJson,
                                UpdatedAtUtc = @now,
                                ReadAtUtc = NULL
                            WHERE Id = @id;
                            """;
                        Add(update, "@messageParamsJson", paramsJson);
                        Add(update, "@now", Format(now));
                        Add(update, "@id", existingId);
                        await update.ExecuteNonQueryAsync(cancellationToken);
                        return Guid.Parse(existingId);
                    }
                }

                var id = Guid.NewGuid();
                await using var insert = connection.CreateCommand();
                insert.CommandText =
                    """
                    INSERT INTO Notifications (
                        Id, ProfileId, EventId, Category, Severity, MediaType, SubjectId,
                        MessageParamsJson, DeepLink, DedupKey, OccurrenceCount,
                        CreatedAtUtc, UpdatedAtUtc, ReadAtUtc)
                    VALUES (
                        @id, @profileId, @eventId, @category, @severity, @mediaType, @subjectId,
                        @messageParamsJson, @deepLink, @dedupKey, 1,
                        @now, @now, NULL);
                    """;
                Add(insert, "@id", id.ToString("D"));
                Add(insert, "@profileId", profileId);
                Add(insert, "@eventId", draft.EventId.ToString("D"));
                Add(insert, "@category", (int)draft.Category);
                Add(insert, "@severity", (int)draft.Severity);
                Add(insert, "@mediaType", Trim(draft.MediaType, MaxMediaTypeLength));
                Add(insert, "@subjectId", Trim(draft.SubjectId, MaxSubjectLength));
                Add(insert, "@messageParamsJson", paramsJson);
                Add(insert, "@deepLink", Trim(draft.DeepLink, MaxDeepLinkLength));
                Add(insert, "@dedupKey", dedupKey);
                Add(insert, "@now", Format(now));
                await insert.ExecuteNonQueryAsync(cancellationToken);
                return id;
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationItem>> ListAsync(
        string profileId,
        bool unreadOnly,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        var take = Math.Clamp(limit, 1, 500);
        var unreadClause = unreadOnly ? "AND ReadAtUtc IS NULL" : "";

        return await WithConnectionAsync(
            async connection =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    $"""
                    SELECT Id, ProfileId, EventId, Category, Severity, MediaType, SubjectId,
                           MessageParamsJson, DeepLink, DedupKey, OccurrenceCount,
                           CreatedAtUtc, UpdatedAtUtc, ReadAtUtc
                    FROM Notifications
                    WHERE ProfileId = @profileId {unreadClause}
                    ORDER BY UpdatedAtUtc DESC
                    LIMIT {take};
                    """;
                Add(command, "@profileId", profileId.Trim());

                var result = new List<NotificationItem>();
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    result.Add(Read(reader));
                }

                return (IReadOnlyList<NotificationItem>)result;
            },
            cancellationToken);
    }

    public async Task<int> CountUnreadAsync(string profileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        return await WithConnectionAsync(
            async connection =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT COUNT(*) FROM Notifications
                    WHERE ProfileId = @profileId AND ReadAtUtc IS NULL;
                    """;
                Add(command, "@profileId", profileId.Trim());
                var count = await command.ExecuteScalarAsync(cancellationToken);
                return Convert.ToInt32(count, CultureInfo.InvariantCulture);
            },
            cancellationToken);
    }

    /// <summary>Marks one notification read; scoped to the owning profile so a profile can never touch another's inbox.</summary>
    public Task<bool> MarkReadAsync(Guid id, string profileId, CancellationToken cancellationToken = default) =>
        SetReadAsync(id, profileId, read: true, cancellationToken);

    public Task<bool> MarkUnreadAsync(Guid id, string profileId, CancellationToken cancellationToken = default) =>
        SetReadAsync(id, profileId, read: false, cancellationToken);

    private async Task<bool> SetReadAsync(Guid id, string profileId, bool read, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        var rows = await WithConnectionAsync(
            async connection =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    UPDATE Notifications
                    SET ReadAtUtc = @readAt, UpdatedAtUtc = @now
                    WHERE Id = @id AND ProfileId = @profileId;
                    """;
                Add(command, "@readAt", read ? Format(DateTime.UtcNow) : null);
                Add(command, "@now", Format(DateTime.UtcNow));
                Add(command, "@id", id.ToString("D"));
                Add(command, "@profileId", profileId.Trim());
                return await command.ExecuteNonQueryAsync(cancellationToken);
            },
            cancellationToken);

        return rows > 0;
    }

    public async Task<int> MarkAllReadAsync(string profileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        return await WithConnectionAsync(
            async connection =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    UPDATE Notifications
                    SET ReadAtUtc = @now, UpdatedAtUtc = @now
                    WHERE ProfileId = @profileId AND ReadAtUtc IS NULL;
                    """;
                Add(command, "@now", Format(DateTime.UtcNow));
                Add(command, "@profileId", profileId.Trim());
                return await command.ExecuteNonQueryAsync(cancellationToken);
            },
            cancellationToken);
    }

    /// <summary>Clears (deletes) every read notification for a profile; unread ones are kept.</summary>
    public async Task<int> ClearReadAsync(string profileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        return await WithConnectionAsync(
            async connection =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    DELETE FROM Notifications
                    WHERE ProfileId = @profileId AND ReadAtUtc IS NOT NULL;
                    """;
                Add(command, "@profileId", profileId.Trim());
                return await command.ExecuteNonQueryAsync(cancellationToken);
            },
            cancellationToken);
    }

    private static NotificationItem Read(DbDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            Guid.Parse(reader.GetString(2)),
            (JularrEventCategory)Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture),
            (JularrEventSeverity)Convert.ToInt32(reader.GetValue(4), CultureInfo.InvariantCulture),
            ReadNullableString(reader, 5),
            ReadNullableString(reader, 6),
            ReadParams(ReadNullableString(reader, 7)),
            ReadNullableString(reader, 8),
            ReadNullableString(reader, 9),
            Convert.ToInt32(reader.GetValue(10), CultureInfo.InvariantCulture),
            ParseDate(reader.GetString(11)),
            ParseDate(reader.GetString(12)),
            ReadNullableDate(reader, 13));

    private static IReadOnlyDictionary<string, string>? ReadParams(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, string>>(json);

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

    private static string? ReadNullableString(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static DateTime? ReadNullableDate(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ParseDate(reader.GetString(ordinal));

    private static DateTime ParseDate(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();

    private static string Format(DateTime value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string? Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = value.Trim();
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
