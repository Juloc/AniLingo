using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Threading.Channels;
using Jularr.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Ai;

public sealed record AiUsageTotals(
    long Requests,
    long EstimatedRequests,
    long InputTokens,
    long CachedInputTokens,
    long OutputTokens,
    long ReasoningOutputTokens,
    long EstimatedInputTokens,
    long EstimatedOutputTokens,
    long ContextTokens,
    long CacheHits,
    long ResumedChunks,
    long Retries,
    long Failures,
    long Cancellations,
    long DurationMs)
{
    public static AiUsageTotals Zero { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>Input tokens including the estimated share; see <see cref="HasEstimates"/>.</summary>
    public long AllInputTokens => InputTokens + EstimatedInputTokens;

    public long AllOutputTokens => OutputTokens + EstimatedOutputTokens;

    public bool HasEstimates => EstimatedRequests > 0;

    public bool IsEmpty => Requests == 0 && CacheHits == 0 && ResumedChunks == 0;

    public AiUsageTotals Add(AiUsageTotals other) =>
        new(
            Requests + other.Requests,
            EstimatedRequests + other.EstimatedRequests,
            InputTokens + other.InputTokens,
            CachedInputTokens + other.CachedInputTokens,
            OutputTokens + other.OutputTokens,
            ReasoningOutputTokens + other.ReasoningOutputTokens,
            EstimatedInputTokens + other.EstimatedInputTokens,
            EstimatedOutputTokens + other.EstimatedOutputTokens,
            ContextTokens + other.ContextTokens,
            CacheHits + other.CacheHits,
            ResumedChunks + other.ResumedChunks,
            Retries + other.Retries,
            Failures + other.Failures,
            Cancellations + other.Cancellations,
            DurationMs + other.DurationMs);

    public static AiUsageTotals From(AiUsageMeasurement measurement)
    {
        if (!measurement.IsRequest)
        {
            return Zero with
            {
                CacheHits = measurement.CacheHit ? 1 : 0,
                ResumedChunks = measurement.ResumedChunk ? 1 : 0
            };
        }

        var estimated = measurement.Estimated;
        return new AiUsageTotals(
            Requests: 1,
            EstimatedRequests: estimated ? 1 : 0,
            InputTokens: estimated ? 0 : measurement.InputTokens,
            CachedInputTokens: estimated ? 0 : measurement.CachedInputTokens,
            OutputTokens: estimated ? 0 : measurement.OutputTokens,
            ReasoningOutputTokens: estimated ? 0 : measurement.ReasoningOutputTokens,
            EstimatedInputTokens: estimated ? measurement.InputTokens : 0,
            EstimatedOutputTokens: estimated ? measurement.OutputTokens : 0,
            ContextTokens: measurement.ContextTokens,
            CacheHits: 0,
            ResumedChunks: 0,
            Retries: measurement.Retries,
            Failures: measurement.Outcome == AiUsageOutcome.Failed ? 1 : 0,
            Cancellations: measurement.Outcome == AiUsageOutcome.Cancelled ? 1 : 0,
            DurationMs: Math.Max(0, measurement.DurationMs));
    }
}

public sealed record AiUsageRow(
    DateOnly Day,
    string ProfileId,
    string ProviderId,
    string Model,
    string Operation,
    AiUsageTotals Totals);

public sealed record AiUsageGroup(string Key, string? Detail, AiUsageTotals Totals);

public enum AiUsagePeriod
{
    Today,
    Last7Days,
    Last30Days
}

/// <summary>Today / 7 / 30 day totals plus per-feature and per-model breakdowns of one period.</summary>
public sealed record AiUsageReport(
    AiUsagePeriod Period,
    AiUsageTotals Today,
    AiUsageTotals Last7Days,
    AiUsageTotals Last30Days,
    IReadOnlyList<AiUsageGroup> ByOperation,
    IReadOnlyList<AiUsageGroup> ByModel,
    IReadOnlyList<AiUsageGroup> ByProfile)
{
    public static AiUsageReport Empty(AiUsagePeriod period) =>
        new(period, AiUsageTotals.Zero, AiUsageTotals.Zero, AiUsageTotals.Zero, [], [], []);

    public AiUsageTotals Selected => Period switch
    {
        AiUsagePeriod.Today => Today,
        AiUsagePeriod.Last7Days => Last7Days,
        _ => Last30Days
    };

    public static AiUsageReport Build(
        IReadOnlyList<AiUsageRow> rows,
        DateOnly today,
        AiUsagePeriod period)
    {
        AiUsageTotals Sum(IEnumerable<AiUsageRow> selection) =>
            selection.Aggregate(AiUsageTotals.Zero, (total, row) => total.Add(row.Totals));

        var firstDay = today.AddDays(period switch
        {
            AiUsagePeriod.Today => 0,
            AiUsagePeriod.Last7Days => -6,
            _ => -29
        });
        var inPeriod = rows.Where(x => x.Day >= firstDay && x.Day <= today).ToArray();

        IReadOnlyList<AiUsageGroup> Group(Func<AiUsageRow, string> key, Func<AiUsageRow, string?> detail) =>
            inPeriod
                .GroupBy(key, StringComparer.Ordinal)
                .Select(group => new AiUsageGroup(group.Key, detail(group.First()), Sum(group)))
                .OrderByDescending(x => x.Totals.AllInputTokens + x.Totals.AllOutputTokens)
                .ThenBy(x => x.Key, StringComparer.Ordinal)
                .ToArray();

        return new AiUsageReport(
            period,
            Sum(rows.Where(x => x.Day == today)),
            Sum(rows.Where(x => x.Day >= today.AddDays(-6) && x.Day <= today)),
            Sum(rows.Where(x => x.Day >= today.AddDays(-29) && x.Day <= today)),
            Group(x => x.Operation, _ => null),
            Group(x => $"{x.ProviderId}\u001f{x.Model}", x => x.ProviderId),
            Group(x => x.ProfileId, _ => null));
    }
}

/// <summary>Daily usage aggregates (table from migration 20260927210000). Counters only.</summary>
public sealed class AiUsageStore(AppDbContext db)
{
    private static readonly string[] CounterColumns =
    [
        "Requests", "EstimatedRequests", "InputTokens", "CachedInputTokens", "OutputTokens",
        "ReasoningOutputTokens", "EstimatedInputTokens", "EstimatedOutputTokens", "ContextTokens",
        "CacheHits", "ResumedChunks", "Retries", "Failures", "Cancellations", "DurationMs"
    ];

    public async Task AddAsync(
        IReadOnlyList<(string ProfileId, AiUsageMeasurement Measurement)> measurements,
        CancellationToken cancellationToken)
    {
        if (measurements.Count == 0)
        {
            return;
        }

        var aggregated = measurements
            .GroupBy(x => (
                x.ProfileId,
                Day: DateOnly.FromDateTime(x.Measurement.Timestamp.UtcDateTime),
                x.Measurement.ProviderId,
                Model: x.Measurement.Model ?? string.Empty,
                x.Measurement.Operation))
            .Select(group => (group.Key, Totals: group.Aggregate(
                AiUsageTotals.Zero,
                (total, item) => total.Add(AiUsageTotals.From(item.Measurement)))))
            .ToArray();

        await WithConnectionAsync(async connection =>
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            foreach (var (key, totals) in aggregated)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                var columns = string.Join(", ", CounterColumns.Select(x => $"\"{x}\""));
                var values = string.Join(", ", CounterColumns.Select(x => "$" + x));
                var updates = string.Join(", ", CounterColumns.Select(x => $"\"{x}\" = \"{x}\" + excluded.\"{x}\""));
                command.CommandText =
                    $"""
                    INSERT INTO "AiUsageDaily" ("ProfileId", "Day", "ProviderId", "Model", "Operation", {columns})
                    VALUES ($profile, $day, $provider, $model, $operation, {values})
                    ON CONFLICT("ProfileId", "Day", "ProviderId", "Model", "Operation") DO UPDATE SET {updates};
                    """;
                Add(command, "$profile", key.ProfileId);
                Add(command, "$day", key.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                Add(command, "$provider", key.ProviderId);
                Add(command, "$model", key.Model);
                Add(command, "$operation", key.Operation);
                var counters = Counters(totals);
                for (var index = 0; index < CounterColumns.Length; index++)
                {
                    Add(command, "$" + CounterColumns[index], counters[index]);
                }

                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

    /// <summary>Rows for one profile, or for every profile when <paramref name="profileId"/> is null.</summary>
    public Task<IReadOnlyList<AiUsageRow>> QueryAsync(
        string? profileId,
        DateOnly fromDay,
        DateOnly toDay,
        CancellationToken cancellationToken) =>
        WithConnectionAsync<IReadOnlyList<AiUsageRow>>(async connection =>
        {
            await using var command = connection.CreateCommand();
            var columns = string.Join(", ", CounterColumns.Select(x => $"\"{x}\""));
            command.CommandText =
                $"""
                SELECT "Day", "ProfileId", "ProviderId", "Model", "Operation", {columns}
                FROM "AiUsageDaily"
                WHERE "Day" >= $from AND "Day" <= $to
                  AND ($profile IS NULL OR "ProfileId" = $profile)
                ORDER BY "Day";
                """;
            Add(command, "$from", fromDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Add(command, "$to", toDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Add(command, "$profile", profileId);

            var rows = new List<AiUsageRow>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var counters = new long[CounterColumns.Length];
                for (var index = 0; index < counters.Length; index++)
                {
                    counters[index] = reader.GetInt64(5 + index);
                }

                rows.Add(new AiUsageRow(
                    DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    FromCounters(counters)));
            }

            return rows;
        }, cancellationToken);

    public async Task<AiUsageReport> GetReportAsync(
        string? profileId,
        DateOnly today,
        AiUsagePeriod period,
        CancellationToken cancellationToken)
    {
        var rows = await QueryAsync(profileId, today.AddDays(-29), today, cancellationToken);
        return AiUsageReport.Build(rows, today, period);
    }

    private static long[] Counters(AiUsageTotals totals) =>
    [
        totals.Requests, totals.EstimatedRequests, totals.InputTokens, totals.CachedInputTokens, totals.OutputTokens,
        totals.ReasoningOutputTokens, totals.EstimatedInputTokens, totals.EstimatedOutputTokens, totals.ContextTokens,
        totals.CacheHits, totals.ResumedChunks, totals.Retries, totals.Failures, totals.Cancellations, totals.DurationMs
    ];

    private static AiUsageTotals FromCounters(long[] values) =>
        new(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7],
            values[8], values[9], values[10], values[11], values[12], values[13], values[14]);

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

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}

/// <summary>Hands measurements from the request path to the background writer without blocking.</summary>
public sealed class AiUsagePersistenceQueue : IAiUsageSink
{
    private readonly Channel<(string ProfileId, AiUsageMeasurement Measurement)> channel =
        Channel.CreateUnbounded<(string, AiUsageMeasurement)>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<(string ProfileId, AiUsageMeasurement Measurement)> Reader => channel.Reader;

    public void Enqueue(string profileId, AiUsageMeasurement measurement) =>
        channel.Writer.TryWrite((profileId, measurement));
}

public sealed class AiUsagePersistenceWorker(
    AiUsagePersistenceQueue queue,
    IServiceScopeFactory scopes,
    ILogger<AiUsagePersistenceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await queue.Reader.WaitToReadAsync(stoppingToken))
            {
                await FlushAsync(CancellationToken.None);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        await FlushAsync(CancellationToken.None);
    }

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        var batch = new List<(string, AiUsageMeasurement)>();
        while (batch.Count < 500 && queue.Reader.TryRead(out var item))
        {
            batch.Add(item);
        }

        if (batch.Count == 0)
        {
            return;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AiUsageStore>().AddAsync(batch, cancellationToken);
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException)
        {
            logger.LogWarning(exception, "Could not persist {Count} AI usage measurements.", batch.Count);
        }
    }
}
