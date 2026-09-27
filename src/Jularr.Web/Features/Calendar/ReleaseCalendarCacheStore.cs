using System.Data;
using System.Data.Common;
using System.Globalization;
using Jularr.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Calendar;

/// <summary>One cached, normalized provider release (table ReleaseCalendarEntries).</summary>
public sealed record CachedRelease(
    string Provider,
    string ExternalId,
    ReleaseKind Kind,
    int UnitNumber,
    ReleaseDate Date);

/// <summary>One fetched provider entry (table ReleaseCalendarSources).</summary>
public sealed record ReleaseCacheSource(
    string Provider,
    string ExternalId,
    string? ProviderStatus,
    DateTime? RefreshedAt,
    DateTime? LastAttemptAt,
    string? LastError);

/// <summary>The fresh provider data of one source, replacing what was cached for it.</summary>
public sealed record ReleaseSourceSnapshot(
    string ExternalId,
    string? ProviderStatus,
    IReadOnlyList<CachedRelease> Releases);

/// <summary>
/// Persistence of the provider release cache (migration 20260927180000). The calendar reads only
/// from here; refreshes replace one source's upcoming data at a time and leave the cache untouched
/// when a provider fails, so stale-but-usable data keeps the calendar working.
/// </summary>
public sealed class ReleaseCalendarCacheStore(AppDbContext db)
{
    /// <summary>Past releases older than this are pruned on refresh.</summary>
    public static readonly TimeSpan History = TimeSpan.FromDays(400);

    public Task<IReadOnlyDictionary<string, ReleaseCacheSource>> GetSourcesAsync(string provider, CancellationToken cancellationToken) =>
        WithConnectionAsync<IReadOnlyDictionary<string, ReleaseCacheSource>>(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT "ExternalId", "ProviderStatus", "RefreshedAt", "LastAttemptAt", "LastError"
                FROM "ReleaseCalendarSources" WHERE "Provider" = $provider;
                """;
            Add(command, "$provider", provider);
            var rows = new Dictionary<string, ReleaseCacheSource>(StringComparer.Ordinal);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var externalId = reader.GetString(0);
                rows[externalId] = new ReleaseCacheSource(
                    provider,
                    externalId,
                    NullableString(reader, 1),
                    ParseNullableDate(NullableString(reader, 2)),
                    ParseNullableDate(NullableString(reader, 3)),
                    NullableString(reader, 4));
            }

            return rows;
        }, cancellationToken);

    /// <summary>Cached releases overlapping the inclusive UTC day range, plus undated ones when asked.</summary>
    public Task<IReadOnlyList<CachedRelease>> GetReleasesAsync(
        string provider,
        DateOnly startUtc,
        DateOnly endUtc,
        bool includeUndated,
        IReadOnlyCollection<string>? externalIds,
        CancellationToken cancellationToken) =>
        WithConnectionAsync<IReadOnlyList<CachedRelease>>(async connection =>
        {
            await using var command = connection.CreateCommand();
            var idFilter = externalIds is null
                ? ""
                : $"""AND "ExternalId" IN ({string.Join(", ", externalIds.Select((_, index) => $"$id{index}"))})""";
            command.CommandText =
                $"""
                SELECT "ExternalId", "Kind", "UnitNumber", "DateValue", "Precision"
                FROM "ReleaseCalendarEntries"
                WHERE "Provider" = $provider {idFilter}
                  AND (("RangeStart" <= $end AND "RangeEnd" >= $start){(includeUndated ? """ OR "RangeStart" IS NULL""" : "")});
                """;
            Add(command, "$provider", provider);
            Add(command, "$start", startUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Add(command, "$end", endUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            var index = 0;
            foreach (var id in externalIds ?? [])
            {
                Add(command, $"$id{index++}", id);
            }

            var rows = new List<CachedRelease>();
            if (externalIds is { Count: 0 })
            {
                return rows;
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new CachedRelease(
                    provider,
                    reader.GetString(0),
                    ReleaseCalendarNames.ParseKind(reader.GetString(1)),
                    reader.GetInt32(2),
                    ReleaseDate.FromStorage(reader.GetString(3), ReleaseCalendarNames.ParsePrecision(reader.GetString(4)))));
            }

            return rows;
        }, cancellationToken);

    /// <summary>
    /// Stores fresh provider data. For each source, cached releases from <paramref name="windowStartUtc"/>
    /// on and undated ones are replaced by the snapshot; older history is kept (the provider no
    /// longer lists it) until it falls out of <see cref="History"/>.
    /// </summary>
    public Task SaveAsync(
        string provider,
        IReadOnlyCollection<ReleaseSourceSnapshot> snapshots,
        DateTimeOffset windowStartUtc,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        WithConnectionAsync(async connection =>
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var windowStart = windowStartUtc.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            foreach (var snapshot in snapshots)
            {
                await ExecuteAsync(connection, transaction,
                    """
                    INSERT INTO "ReleaseCalendarSources" ("Provider", "ExternalId", "ProviderStatus", "RefreshedAt", "LastAttemptAt", "LastError")
                    VALUES ($provider, $id, $status, $now, $now, NULL)
                    ON CONFLICT("Provider", "ExternalId") DO UPDATE SET
                        "ProviderStatus" = excluded."ProviderStatus",
                        "RefreshedAt" = excluded."RefreshedAt",
                        "LastAttemptAt" = excluded."LastAttemptAt",
                        "LastError" = NULL;
                    """,
                    command =>
                    {
                        Add(command, "$provider", provider);
                        Add(command, "$id", snapshot.ExternalId);
                        Add(command, "$status", snapshot.ProviderStatus);
                        Add(command, "$now", nowUtc);
                    },
                    cancellationToken);

                await ExecuteAsync(connection, transaction,
                    """
                    DELETE FROM "ReleaseCalendarEntries"
                    WHERE "Provider" = $provider AND "ExternalId" = $id
                      AND ("RangeStart" IS NULL OR "RangeEnd" >= $windowStart);
                    """,
                    command =>
                    {
                        Add(command, "$provider", provider);
                        Add(command, "$id", snapshot.ExternalId);
                        Add(command, "$windowStart", windowStart);
                    },
                    cancellationToken);

                foreach (var release in snapshot.Releases)
                {
                    var range = release.Date.Period(TimeZoneInfo.Utc);
                    await ExecuteAsync(connection, transaction,
                        """
                        INSERT INTO "ReleaseCalendarEntries"
                            ("Provider", "ExternalId", "Kind", "UnitNumber", "DateValue", "Precision", "RangeStart", "RangeEnd", "FetchedAt")
                        VALUES ($provider, $id, $kind, $unit, $date, $precision, $rangeStart, $rangeEnd, $now)
                        ON CONFLICT("Provider", "ExternalId", "Kind", "UnitNumber") DO UPDATE SET
                            "DateValue" = excluded."DateValue",
                            "Precision" = excluded."Precision",
                            "RangeStart" = excluded."RangeStart",
                            "RangeEnd" = excluded."RangeEnd",
                            "FetchedAt" = excluded."FetchedAt";
                        """,
                        command =>
                        {
                            Add(command, "$provider", provider);
                            Add(command, "$id", snapshot.ExternalId);
                            Add(command, "$kind", ReleaseCalendarNames.Kind(release.Kind));
                            Add(command, "$unit", release.UnitNumber);
                            Add(command, "$date", release.Date.ToStorage());
                            Add(command, "$precision", ReleaseCalendarNames.Precision(release.Date.Precision));
                            Add(command, "$rangeStart", range?.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                            Add(command, "$rangeEnd", range?.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                            Add(command, "$now", nowUtc);
                        },
                        cancellationToken);
                }
            }

            await ExecuteAsync(connection, transaction,
                """DELETE FROM "ReleaseCalendarEntries" WHERE "Provider" = $provider AND "RangeEnd" < $cutoff;""",
                command =>
                {
                    Add(command, "$provider", provider);
                    Add(command, "$cutoff", (nowUtc - History).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                },
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return true;
        }, cancellationToken);

    /// <summary>Records a failed refresh attempt; cached releases stay as they are.</summary>
    public Task MarkFailedAsync(
        string provider,
        IReadOnlyCollection<string> externalIds,
        string error,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        WithConnectionAsync(async connection =>
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            foreach (var externalId in externalIds)
            {
                await ExecuteAsync(connection, transaction,
                    """
                    INSERT INTO "ReleaseCalendarSources" ("Provider", "ExternalId", "LastAttemptAt", "LastError")
                    VALUES ($provider, $id, $now, $error)
                    ON CONFLICT("Provider", "ExternalId") DO UPDATE SET
                        "LastAttemptAt" = excluded."LastAttemptAt",
                        "LastError" = excluded."LastError";
                    """,
                    command =>
                    {
                        Add(command, "$provider", provider);
                        Add(command, "$id", externalId);
                        Add(command, "$now", nowUtc);
                        Add(command, "$error", error.Length > 300 ? error[..300] : error);
                    },
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return true;
        }, cancellationToken);

    /// <summary>When the provider data was last refreshed successfully, if ever.</summary>
    public Task<DateTime?> GetLastRefreshAsync(string provider, CancellationToken cancellationToken) =>
        WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """SELECT MAX("RefreshedAt") FROM "ReleaseCalendarSources" WHERE "Provider" = $provider;""";
            Add(command, "$provider", provider);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return value is string text ? ParseNullableDate(text) : null;
        }, cancellationToken);

    private static async Task ExecuteAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        Action<DbCommand> bind,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        bind(command);
        await command.ExecuteNonQueryAsync(cancellationToken);
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

    private static string? NullableString(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static DateTime? ParseNullableDate(string? value) =>
        value is null ? null : DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

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

/// <summary>Stored names of calendar enums (CHECK constraints of migration 20260927180000).</summary>
public static class ReleaseCalendarNames
{
    public static string Kind(ReleaseKind kind) => kind switch
    {
        ReleaseKind.Episode => "episode",
        ReleaseKind.SeasonPremiere => "seasonPremiere",
        ReleaseKind.SeriesStart => "seriesStart",
        ReleaseKind.Chapter => "chapter",
        ReleaseKind.Volume => "volume",
        ReleaseKind.Publication => "publication",
        ReleaseKind.Cinema => "cinema",
        ReleaseKind.Digital => "digital",
        ReleaseKind.Streaming => "streaming",
        ReleaseKind.Physical => "physical",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static ReleaseKind ParseKind(string value) =>
        Enum.GetValues<ReleaseKind>().First(kind => Kind(kind) == value);

    public static string Precision(ReleaseDatePrecision precision) => precision switch
    {
        ReleaseDatePrecision.Year => "year",
        ReleaseDatePrecision.Quarter => "quarter",
        ReleaseDatePrecision.Month => "month",
        ReleaseDatePrecision.Day => "day",
        ReleaseDatePrecision.DateTime => "dateTime",
        _ => "unknown"
    };

    public static ReleaseDatePrecision ParsePrecision(string value) =>
        Enum.GetValues<ReleaseDatePrecision>().FirstOrDefault(precision => Precision(precision) == value);
}
