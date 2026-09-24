using System.Data;
using System.Data.Common;
using System.Globalization;
using AniLingo.Web.Data;

namespace AniLingo.Web.Features.Operations;

public sealed class OperationStore(AppDbContext db)
{
    private const int MaxTitleLength = 240;
    private const int MaxSubjectLength = 500;
    private const int MaxMessageLength = 2000;
    private const int MaxErrorLength = 3000;
    private const int MaxModuleLength = 120;

    public async Task<Guid> CreateAsync(
        OperationDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await WithConnectionAsync(
            async connection =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    INSERT INTO Operations (
                        Id, Kind, Category, Lane, Status, ProfileId, Title, Subject,
                        ProgressPercent, Message, Error, IsDownload, BytesTotal,
                        BytesCompleted, BytesPerSecond, EtaUtc, Attempt, Retryable,
                        CreatedAtUtc, StartedAtUtc, FinishedAtUtc, UpdatedAtUtc)
                    VALUES (
                        @id, @kind, @category, @lane, @status, @profileId, @title, @subject,
                        NULL, NULL, NULL, @isDownload, @bytesTotal,
                        NULL, NULL, NULL, 1, @retryable,
                        @createdAt, NULL, NULL, @updatedAt);
                    """;
                Add(command, "@id", id.ToString("D"));
                Add(command, "@kind", Trim(descriptor.Kind, 100) ?? "background");
                Add(command, "@category", Trim(descriptor.Category, 80) ?? "Task");
                Add(command, "@lane", (int)descriptor.Lane);
                Add(command, "@status", (int)OperationStatus.Queued);
                Add(command, "@profileId", Trim(descriptor.ProfileId, 80));
                Add(command, "@title", Trim(descriptor.Title, MaxTitleLength) ?? "Background task");
                Add(command, "@subject", Trim(descriptor.Subject, MaxSubjectLength));
                Add(command, "@isDownload", descriptor.IsDownload ? 1 : 0);
                Add(command, "@bytesTotal", descriptor.BytesTotal);
                Add(command, "@retryable", descriptor.Retryable ? 1 : 0);
                Add(command, "@createdAt", Format(now));
                Add(command, "@updatedAt", Format(now));
                await command.ExecuteNonQueryAsync(cancellationToken);
            },
            cancellationToken);

        await AppendLogAsync(
            id,
            OperationLogLevel.Information,
            "Queue",
            "Operation queued.",
            cancellationToken);

        return id;
    }

    public async Task<OperationSnapshot?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        await WithConnectionAsync(
            async connection =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT Id, Kind, Category, Lane, Status, ProfileId, Title, Subject,
                           ProgressPercent, Message, Error, IsDownload, BytesTotal,
                           BytesCompleted, BytesPerSecond, EtaUtc, Attempt, Retryable,
                           CreatedAtUtc, StartedAtUtc, FinishedAtUtc, UpdatedAtUtc
                    FROM Operations
                    WHERE Id = @id
                    LIMIT 1;
                    """;
                Add(command, "@id", id.ToString("D"));

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                return await reader.ReadAsync(cancellationToken)
                    ? ReadOperation(reader)
                    : null;
            },
            cancellationToken);

    public async Task<IReadOnlyList<OperationSnapshot>> ListAsync(
        OperationListFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var conditions = new List<string>();
        var parameters = new List<(string Name, object? Value)>();

        switch (filter.View?.Trim().ToLowerInvariant())
        {
            case "active":
                conditions.Add("Status IN (1, 2)");
                break;
            case "queue":
                conditions.Add("Status = 1");
                break;
            case "downloads":
                conditions.Add("IsDownload = 1");
                break;
            case "history":
                conditions.Add("Status IN (3, 4, 5, 6)");
                break;
        }

        if (filter.Status is { } status)
        {
            conditions.Add("Status = @status");
            parameters.Add(("@status", (int)status));
        }

        if (!string.IsNullOrWhiteSpace(filter.Category))
        {
            conditions.Add("Category = @category");
            parameters.Add(("@category", filter.Category.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            conditions.Add("(Title LIKE @search OR Subject LIKE @search OR Message LIKE @search OR Kind LIKE @search)");
            parameters.Add(("@search", $"%{filter.Search.Trim()}%"));
        }

        var where = conditions.Count == 0
            ? string.Empty
            : "WHERE " + string.Join(" AND ", conditions);

        var limit = Math.Clamp(filter.Limit, 1, 500);

        return await WithConnectionAsync(
            async connection =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    $"""
                    SELECT Id, Kind, Category, Lane, Status, ProfileId, Title, Subject,
                           ProgressPercent, Message, Error, IsDownload, BytesTotal,
                           BytesCompleted, BytesPerSecond, EtaUtc, Attempt, Retryable,
                           CreatedAtUtc, StartedAtUtc, FinishedAtUtc, UpdatedAtUtc
                    FROM Operations
                    {where}
                    ORDER BY
                        CASE Status WHEN 2 THEN 0 WHEN 1 THEN 1 ELSE 2 END,
                        UpdatedAtUtc DESC
                    LIMIT {limit};
                    """;

                foreach (var parameter in parameters)
                {
                    Add(command, parameter.Name, parameter.Value);
                }

                var result = new List<OperationSnapshot>();
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    result.Add(ReadOperation(reader));
                }

                return (IReadOnlyList<OperationSnapshot>)result;
            },
            cancellationToken);
    }

    public async Task<OperationSummary> GetSummaryAsync(
        CancellationToken cancellationToken = default) =>
        await WithConnectionAsync(
            async connection =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT
                        SUM(CASE WHEN Status = 2 THEN 1 ELSE 0 END),
                        SUM(CASE WHEN Status = 1 THEN 1 ELSE 0 END),
                        SUM(CASE WHEN Status = 4 THEN 1 ELSE 0 END),
                        SUM(CASE WHEN Status = 6 THEN 1 ELSE 0 END),
                        SUM(CASE WHEN IsDownload = 1 AND Status IN (1, 2) THEN 1 ELSE 0 END),
                        SUM(CASE WHEN Status = 3 AND FinishedAtUtc >= @today THEN 1 ELSE 0 END)
                    FROM Operations;
                    """;
                Add(command, "@today", Format(DateTime.UtcNow.Date));

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                await reader.ReadAsync(cancellationToken);

                return new OperationSummary(
                    ReadCount(reader, 0),
                    ReadCount(reader, 1),
                    ReadCount(reader, 2),
                    ReadCount(reader, 3),
                    ReadCount(reader, 4),
                    ReadCount(reader, 5));
            },
            cancellationToken);

    public async Task MarkRunningAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        await UpdateAsync(
            id,
            """
            Status = @status,
            StartedAtUtc = COALESCE(StartedAtUtc, @now),
            FinishedAtUtc = NULL,
            Error = NULL,
            UpdatedAtUtc = @now
            """,
            [
                ("@status", (object?)(int)OperationStatus.Running),
                ("@now", Format(now))
            ],
            cancellationToken);

        await AppendLogAsync(
            id,
            OperationLogLevel.Information,
            "Worker",
            "Operation started.",
            cancellationToken);
    }

    public async Task MarkSucceededAsync(
        Guid id,
        string? message = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        await UpdateAsync(
            id,
            """
            Status = @status,
            ProgressPercent = 100,
            Message = COALESCE(@message, Message),
            Error = NULL,
            FinishedAtUtc = @now,
            UpdatedAtUtc = @now
            """,
            [
                ("@status", (object?)(int)OperationStatus.Succeeded),
                ("@message", Trim(message, MaxMessageLength)),
                ("@now", Format(now))
            ],
            cancellationToken);

        await AppendLogAsync(
            id,
            OperationLogLevel.Information,
            "Worker",
            "Operation completed.",
            cancellationToken);
    }

    public async Task MarkFailedAsync(
        Guid id,
        string error,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        await UpdateAsync(
            id,
            """
            Status = @status,
            Error = @error,
            FinishedAtUtc = @now,
            UpdatedAtUtc = @now
            """,
            [
                ("@status", (object?)(int)OperationStatus.Failed),
                ("@error", Trim(error, MaxErrorLength)),
                ("@now", Format(now))
            ],
            cancellationToken);

        await AppendLogAsync(
            id,
            OperationLogLevel.Error,
            "Worker",
            Trim(error, MaxMessageLength) ?? "Operation failed.",
            cancellationToken);
    }

    public async Task MarkCancelledAsync(
        Guid id,
        string? message = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        await UpdateAsync(
            id,
            """
            Status = @status,
            Message = COALESCE(@message, Message),
            FinishedAtUtc = @now,
            UpdatedAtUtc = @now
            """,
            [
                ("@status", (object?)(int)OperationStatus.Cancelled),
                ("@message", Trim(message, MaxMessageLength)),
                ("@now", Format(now))
            ],
            cancellationToken);

        await AppendLogAsync(
            id,
            OperationLogLevel.Warning,
            "Queue",
            message ?? "Operation cancelled.",
            cancellationToken);
    }

    public async Task MarkInterruptedAsync(
        Guid id,
        string? message = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        await UpdateAsync(
            id,
            """
            Status = @status,
            Message = COALESCE(@message, Message),
            FinishedAtUtc = @now,
            UpdatedAtUtc = @now
            """,
            [
                ("@status", (object?)(int)OperationStatus.Interrupted),
                ("@message", Trim(message, MaxMessageLength)),
                ("@now", Format(now))
            ],
            cancellationToken);

        await AppendLogAsync(
            id,
            OperationLogLevel.Warning,
            "Worker",
            message ?? "Operation interrupted.",
            cancellationToken);
    }

    public async Task<int> RecoverInterruptedAsync(
        OperationLane lane,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return await WithConnectionAsync(
            async connection =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    UPDATE Operations
                    SET Status = @interrupted,
                        Message = 'Interrupted by server restart.',
                        FinishedAtUtc = @now,
                        UpdatedAtUtc = @now
                    WHERE Lane = @lane
                      AND Status IN (@queued, @running);
                    """;
                Add(command, "@interrupted", (int)OperationStatus.Interrupted);
                Add(command, "@now", Format(now));
                Add(command, "@lane", (int)lane);
                Add(command, "@queued", (int)OperationStatus.Queued);
                Add(command, "@running", (int)OperationStatus.Running);
                return await command.ExecuteNonQueryAsync(cancellationToken);
            },
            cancellationToken);
    }

    public async Task<bool> PrepareRetryAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var rows = await WithConnectionAsync(
            async connection =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    UPDATE Operations
                    SET Status = @queued,
                        Attempt = Attempt + 1,
                        ProgressPercent = NULL,
                        Message = 'Retry queued.',
                        Error = NULL,
                        StartedAtUtc = NULL,
                        FinishedAtUtc = NULL,
                        UpdatedAtUtc = @now
                    WHERE Id = @id
                      AND Retryable = 1
                      AND Status IN (@failed, @cancelled, @interrupted);
                    """;
                Add(command, "@queued", (int)OperationStatus.Queued);
                Add(command, "@now", Format(now));
                Add(command, "@id", id.ToString("D"));
                Add(command, "@failed", (int)OperationStatus.Failed);
                Add(command, "@cancelled", (int)OperationStatus.Cancelled);
                Add(command, "@interrupted", (int)OperationStatus.Interrupted);
                return await command.ExecuteNonQueryAsync(cancellationToken);
            },
            cancellationToken);

        if (rows > 0)
        {
            await AppendLogAsync(
                id,
                OperationLogLevel.Information,
                "Queue",
                "Retry queued.",
                cancellationToken);
        }

        return rows > 0;
    }

    public Task ReportProgressAsync(
        Guid id,
        int? percent,
        string? message = null,
        long? bytesCompleted = null,
        long? bytesTotal = null,
        double? bytesPerSecond = null,
        DateTime? etaUtc = null,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            id,
            """
            ProgressPercent = @progress,
            Message = COALESCE(@message, Message),
            BytesCompleted = COALESCE(@bytesCompleted, BytesCompleted),
            BytesTotal = COALESCE(@bytesTotal, BytesTotal),
            BytesPerSecond = @bytesPerSecond,
            EtaUtc = @etaUtc,
            UpdatedAtUtc = @now
            """,
            [
                ("@progress", percent is null ? null : Math.Clamp(percent.Value, 0, 100)),
                ("@message", Trim(message, MaxMessageLength)),
                ("@bytesCompleted", bytesCompleted),
                ("@bytesTotal", bytesTotal),
                ("@bytesPerSecond", bytesPerSecond),
                ("@etaUtc", etaUtc is null ? null : Format(etaUtc.Value)),
                ("@now", Format(DateTime.UtcNow))
            ],
            cancellationToken);

    public async Task AppendLogAsync(
        Guid operationId,
        OperationLogLevel level,
        string module,
        string message,
        CancellationToken cancellationToken = default)
    {
        await WithConnectionAsync(
            async connection =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    INSERT INTO OperationLogs (
                        OperationId, CreatedAtUtc, Level, Module, Message)
                    VALUES (
                        @operationId, @createdAt, @level, @module, @message);
                    """;
                Add(command, "@operationId", operationId.ToString("D"));
                Add(command, "@createdAt", Format(DateTime.UtcNow));
                Add(command, "@level", (int)level);
                Add(command, "@module", Trim(module, MaxModuleLength) ?? "Operation");
                Add(command, "@message", Trim(message, MaxMessageLength) ?? string.Empty);
                await command.ExecuteNonQueryAsync(cancellationToken);
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<OperationLogEntry>> ListLogsAsync(
        OperationLogFilter filter,
        CancellationToken cancellationToken = default)
    {
        var conditions = new List<string>();
        var parameters = new List<(string Name, object? Value)>();

        if (filter.Level is { } level)
        {
            conditions.Add("Level = @level");
            parameters.Add(("@level", (int)level));
        }

        if (filter.OperationId is { } operationId)
        {
            conditions.Add("OperationId = @operationId");
            parameters.Add(("@operationId", operationId.ToString("D")));
        }

        if (!string.IsNullOrWhiteSpace(filter.Module))
        {
            conditions.Add("Module = @module");
            parameters.Add(("@module", filter.Module.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            conditions.Add("(Message LIKE @search OR Module LIKE @search)");
            parameters.Add(("@search", $"%{filter.Search.Trim()}%"));
        }

        var where = conditions.Count == 0
            ? string.Empty
            : "WHERE " + string.Join(" AND ", conditions);
        var limit = Math.Clamp(filter.Limit, 1, 1000);

        return await WithConnectionAsync(
            async connection =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    $"""
                    SELECT Id, OperationId, CreatedAtUtc, Level, Module, Message
                    FROM OperationLogs
                    {where}
                    ORDER BY Id DESC
                    LIMIT {limit};
                    """;

                foreach (var parameter in parameters)
                {
                    Add(command, parameter.Name, parameter.Value);
                }

                var rows = new List<OperationLogEntry>();
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    rows.Add(new OperationLogEntry(
                        reader.GetInt64(0),
                        Guid.Parse(reader.GetString(1)),
                        ParseDate(reader.GetString(2)),
                        (OperationLogLevel)Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture),
                        reader.GetString(4),
                        reader.GetString(5)));
                }

                return (IReadOnlyList<OperationLogEntry>)rows;
            },
            cancellationToken);
    }

    private Task UpdateAsync(
        Guid id,
        string assignments,
        IReadOnlyList<(string Name, object? Value)> parameters,
        CancellationToken cancellationToken) =>
        WithConnectionAsync(
            async connection =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    $"""
                    UPDATE Operations
                    SET {assignments}
                    WHERE Id = @id;
                    """;
                Add(command, "@id", id.ToString("D"));
                foreach (var parameter in parameters)
                {
                    Add(command, parameter.Name, parameter.Value);
                }

                await command.ExecuteNonQueryAsync(cancellationToken);
            },
            cancellationToken);

    private async Task WithConnectionAsync(
        Func<DbConnection, Task> action,
        CancellationToken cancellationToken)
    {
        await WithConnectionAsync(
            async connection =>
            {
                await action(connection);
                return true;
            },
            cancellationToken);
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

    private static OperationSnapshot ReadOperation(DbDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            (OperationLane)Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture),
            (OperationStatus)Convert.ToInt32(reader.GetValue(4), CultureInfo.InvariantCulture),
            ReadNullableString(reader, 5),
            reader.GetString(6),
            ReadNullableString(reader, 7),
            ReadNullableInt(reader, 8),
            ReadNullableString(reader, 9),
            ReadNullableString(reader, 10),
            Convert.ToInt32(reader.GetValue(11), CultureInfo.InvariantCulture) != 0,
            ReadNullableLong(reader, 12),
            ReadNullableLong(reader, 13),
            ReadNullableDouble(reader, 14),
            ReadNullableDate(reader, 15),
            Convert.ToInt32(reader.GetValue(16), CultureInfo.InvariantCulture),
            Convert.ToInt32(reader.GetValue(17), CultureInfo.InvariantCulture) != 0,
            ParseDate(reader.GetString(18)),
            ReadNullableDate(reader, 19),
            ReadNullableDate(reader, 20),
            ParseDate(reader.GetString(21)));

    private static int ReadCount(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? 0
            : Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static int? ReadNullableInt(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static long? ReadNullableLong(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static double? ReadNullableDouble(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : Convert.ToDouble(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static string? ReadNullableString(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static DateTime? ReadNullableDate(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ParseDate(reader.GetString(ordinal));

    private static DateTime ParseDate(string value) =>
        DateTime.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind).ToUniversalTime();

    private static string Format(DateTime value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string? Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = value.Trim();
        return cleaned.Length <= maxLength
            ? cleaned
            : cleaned[..maxLength];
    }

    private static void Add(
        DbCommand command,
        string name,
        object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
