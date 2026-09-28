using System.Data;
using System.Data.Common;
using System.Globalization;
using Jularr.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Mapping;

/// <summary>Durable record of one applied mapping change (table from migration 20260929150000).</summary>
public sealed record MappingAuditEntry(
    Guid Id,
    Guid AnimeId,
    string Action,
    string Summary,
    string Details,
    string Actor,
    DateTimeOffset CreatedAt);

/// <summary>
/// Append-only audit history of anime provider-mapping changes: what changed, when and by whom.
/// Raw-ADO.NET derived-state store (same pattern as <c>PresentationGroupStore</c>); the table is
/// never mapped as an EF entity so the model snapshot stays clean.
/// </summary>
public sealed class MappingAuditStore(AppDbContext db)
{
    public const string ActionApply = "apply";
    public const string ActionRemove = "remove";
    public const string ActionUnmapped = "unmapped";
    public const string ActionRole = "role";

    private const int DefaultLimit = 50;

    public Task AppendAsync(
        Guid animeId,
        string action,
        string summary,
        string details,
        string actor,
        CancellationToken cancellationToken) =>
        WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO "AnimeMappingAuditEntries"
                    ("Id", "AnimeId", "Action", "Summary", "Details", "Actor", "CreatedAt")
                VALUES ($id, $anime, $action, $summary, $details, $actor, $now);
                """;
            Add(command, "$id", Key(Guid.NewGuid()));
            Add(command, "$anime", Key(animeId));
            Add(command, "$action", (action ?? "").Trim());
            Add(command, "$summary", (summary ?? "").Trim());
            Add(command, "$details", details ?? "");
            Add(command, "$actor", (actor ?? "").Trim());
            Add(command, "$now", DateTimeOffset.UtcNow.UtcDateTime);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return 0;
        }, cancellationToken);

    public async Task<IReadOnlyList<MappingAuditEntry>> ListForAnimeAsync(
        Guid animeId,
        CancellationToken cancellationToken,
        int limit = DefaultLimit) =>
        await WithConnectionAsync(async connection =>
        {
            var entries = new List<MappingAuditEntry>();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT "Id", "AnimeId", "Action", "Summary", "Details", "Actor", "CreatedAt"
                FROM "AnimeMappingAuditEntries"
                WHERE "AnimeId" = $anime
                ORDER BY "CreatedAt" DESC, "Id" DESC
                LIMIT $limit;
                """;
            Add(command, "$anime", Key(animeId));
            Add(command, "$limit", Math.Clamp(limit, 1, 500));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                entries.Add(new MappingAuditEntry(
                    Guid.Parse(reader.GetString(0)),
                    Guid.Parse(reader.GetString(1)),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    ParseDate(reader.GetString(6))));
            }

            return (IReadOnlyList<MappingAuditEntry>)entries;
        }, cancellationToken);

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

    private static string Key(Guid value) => value.ToString("D");

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

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
