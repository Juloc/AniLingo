using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Jularr.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Events;

/// <summary>
/// Durable, append-only log of every <see cref="JularrEvent"/> ever published (raw SQL against
/// the "Events" table added by migration 20260929240000, mirroring <c>OperationStore</c>'s style).
/// This is the audit trail; per-profile inbox rows live separately in
/// Features/Notifications so a delivery failure never loses the original event.
/// </summary>
public sealed class EventLogStore(AppDbContext db)
{
    private const int MaxSubjectLength = 240;
    private const int MaxDeepLinkLength = 400;
    private const int MaxDedupKeyLength = 200;

    public async Task AppendAsync(JularrEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        await WithConnectionAsync(
            async connection =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    INSERT INTO Events (
                        Id, Category, Audience, ProfileId, MediaType, SubjectId,
                        MessageParamsJson, Severity, DeepLink, DedupKey, RelatedOperationId, CreatedAtUtc)
                    VALUES (
                        @id, @category, @audience, @profileId, @mediaType, @subjectId,
                        @messageParamsJson, @severity, @deepLink, @dedupKey, @relatedOperationId, @createdAt);
                    """;
                Add(command, "@id", domainEvent.Id.ToString("D"));
                Add(command, "@category", (int)domainEvent.Category);
                Add(command, "@audience", (int)domainEvent.Audience);
                Add(command, "@profileId", Trim(domainEvent.ProfileId, 80));
                Add(command, "@mediaType", Trim(domainEvent.MediaType, 40));
                Add(command, "@subjectId", Trim(domainEvent.SubjectId, MaxSubjectLength));
                Add(command, "@messageParamsJson", domainEvent.MessageParams is { Count: > 0 } parameters
                    ? JsonSerializer.Serialize(parameters)
                    : null);
                Add(command, "@severity", (int)domainEvent.Severity);
                Add(command, "@deepLink", Trim(domainEvent.DeepLink, MaxDeepLinkLength));
                Add(command, "@dedupKey", Trim(domainEvent.DedupKey, MaxDedupKeyLength));
                Add(command, "@relatedOperationId", domainEvent.RelatedOperationId?.ToString("D"));
                Add(command, "@createdAt", Format(domainEvent.CreatedAtUtc));
                await command.ExecuteNonQueryAsync(cancellationToken);
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<JularrEvent>> ListRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(limit, 1, 500);
        return await WithConnectionAsync(
            async connection =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    $"""
                    SELECT Id, Category, Audience, ProfileId, MediaType, SubjectId,
                           MessageParamsJson, Severity, DeepLink, DedupKey, RelatedOperationId, CreatedAtUtc
                    FROM Events
                    ORDER BY CreatedAtUtc DESC
                    LIMIT {take};
                    """;

                var result = new List<JularrEvent>();
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    result.Add(Read(reader));
                }

                return (IReadOnlyList<JularrEvent>)result;
            },
            cancellationToken);
    }

    private static JularrEvent Read(DbDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            (JularrEventCategory)Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture),
            (JularrEventAudience)Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture),
            ReadNullableString(reader, 3),
            ReadNullableString(reader, 4),
            ReadNullableString(reader, 5),
            ReadParams(ReadNullableString(reader, 6)),
            (JularrEventSeverity)Convert.ToInt32(reader.GetValue(7), CultureInfo.InvariantCulture),
            ReadNullableString(reader, 8),
            ReadNullableString(reader, 9),
            ReadNullableString(reader, 10) is { } related ? Guid.Parse(related) : null,
            ParseDate(reader.GetString(11)));

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
