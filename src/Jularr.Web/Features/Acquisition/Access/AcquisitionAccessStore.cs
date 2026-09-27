using System.Data;
using System.Data.Common;
using System.Globalization;
using Jularr.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Acquisition.Access;

/// <summary>Persistence of the access policies and acquisition requests (tables from migration 20260927120000).</summary>
public sealed class AcquisitionAccessStore(AppDbContext db)
{
    private const string Columns =
        """
        "Id", "Kind", "Provider", "ExternalId", "Title", "Subtitle", "CoverImageUrl", "PayloadJson",
        "RequestedByProfileId", "Status", "StatusMessage", "OperationId", "ResultUrl",
        "CreatedAt", "UpdatedAt", "DecidedByProfileId", "DecidedAt"
        """;

    public async Task<IReadOnlyList<AcquisitionAccessPolicy>> GetPoliciesAsync(CancellationToken cancellationToken)
    {
        var stored = await WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """SELECT "Kind", "UserAddMode", "ManualAddMode" FROM "AcquisitionAccessPolicies";""";
            var rows = new Dictionary<MediaAcquisitionKind, AcquisitionAccessPolicy>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var kind = AcquisitionAccessNames.ParseKind(reader.GetString(0));
                rows[kind] = new AcquisitionAccessPolicy(
                    kind,
                    AcquisitionAccessNames.ParseUserAdd(reader.GetString(1)),
                    AcquisitionAccessNames.ParseManual(reader.GetString(2)));
            }

            return rows;
        }, cancellationToken);

        return Enum.GetValues<MediaAcquisitionKind>()
            .Select(kind => stored.GetValueOrDefault(kind) ?? AcquisitionAccessPolicy.Default(kind))
            .ToArray();
    }

    public async Task<AcquisitionAccessPolicy> GetPolicyAsync(MediaAcquisitionKind kind, CancellationToken cancellationToken) =>
        (await GetPoliciesAsync(cancellationToken)).Single(policy => policy.Kind == kind);

    public Task SavePolicyAsync(AcquisitionAccessPolicy policy, CancellationToken cancellationToken) =>
        WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO "AcquisitionAccessPolicies" ("Kind", "UserAddMode", "ManualAddMode", "UpdatedAt")
                VALUES ($kind, $userAdd, $manual, $now)
                ON CONFLICT("Kind") DO UPDATE SET
                    "UserAddMode" = excluded."UserAddMode",
                    "ManualAddMode" = excluded."ManualAddMode",
                    "UpdatedAt" = excluded."UpdatedAt";
                """;
            Add(command, "$kind", AcquisitionAccessNames.Kind(policy.Kind));
            Add(command, "$userAdd", AcquisitionAccessNames.UserAdd(policy.UserAdd));
            Add(command, "$manual", AcquisitionAccessNames.Manual(policy.Manual));
            Add(command, "$now", DateTime.UtcNow);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }, cancellationToken);

    /// <summary>The open request for the same title, if any — a title is only ever requested once at a time.</summary>
    public Task<AcquisitionRequest?> FindOpenAsync(
        MediaAcquisitionKind kind,
        string provider,
        string externalId,
        CancellationToken cancellationToken) =>
        QuerySingleAsync(
            $"""
            SELECT {Columns} FROM "AcquisitionRequests"
            WHERE "Kind" = $kind AND "Provider" = $provider AND "ExternalId" = $externalId
              AND "Status" IN ('pending', 'approved', 'searching', 'downloading', 'importing')
            LIMIT 1;
            """,
            command =>
            {
                Add(command, "$kind", AcquisitionAccessNames.Kind(kind));
                Add(command, "$provider", provider);
                Add(command, "$externalId", externalId);
            },
            cancellationToken);

    public Task<AcquisitionRequest?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        QuerySingleAsync(
            $"""SELECT {Columns} FROM "AcquisitionRequests" WHERE "Id" = $id LIMIT 1;""",
            command => Add(command, "$id", id.ToString()),
            cancellationToken);

    /// <summary>The request whose current download is this operation, if any.</summary>
    public Task<AcquisitionRequest?> FindByOperationAsync(Guid operationId, CancellationToken cancellationToken) =>
        QuerySingleAsync(
            $"""SELECT {Columns} FROM "AcquisitionRequests" WHERE "OperationId" = $operationId ORDER BY "UpdatedAt" DESC LIMIT 1;""",
            command => Add(command, "$operationId", operationId.ToString()),
            cancellationToken);

    public Task<IReadOnlyList<AcquisitionRequest>> ListAsync(
        MediaAcquisitionKind? kind,
        string? requestedByProfileId,
        bool openOnly,
        int limit,
        CancellationToken cancellationToken) =>
        QueryAsync(
            $"""
            SELECT {Columns} FROM "AcquisitionRequests"
            WHERE ($kind IS NULL OR "Kind" = $kind)
              AND ($profile IS NULL OR "RequestedByProfileId" = $profile)
              AND ($openOnly = 0 OR "Status" IN ('pending', 'approved', 'searching', 'downloading', 'importing'))
            ORDER BY CASE "Status" WHEN 'pending' THEN 0 ELSE 1 END, "UpdatedAt" DESC
            LIMIT $limit;
            """,
            command =>
            {
                Add(command, "$kind", kind is { } value ? AcquisitionAccessNames.Kind(value) : null);
                Add(command, "$profile", requestedByProfileId);
                Add(command, "$openOnly", openOnly ? 1 : 0);
                Add(command, "$limit", Math.Clamp(limit, 1, 500));
            },
            cancellationToken);

    public Task<IReadOnlyList<AcquisitionRequest>> ListDownloadingAsync(
        MediaAcquisitionKind kind,
        CancellationToken cancellationToken) =>
        QueryAsync(
            $"""
            SELECT {Columns} FROM "AcquisitionRequests"
            WHERE "Kind" = $kind AND "Status" = 'downloading';
            """,
            command => Add(command, "$kind", AcquisitionAccessNames.Kind(kind)),
            cancellationToken);

    public Task<IReadOnlyList<AcquisitionRequest>> ListByStatusAsync(
        MediaAcquisitionKind kind,
        AcquisitionRequestStatus status,
        CancellationToken cancellationToken) =>
        QueryAsync(
            $"""
            SELECT {Columns} FROM "AcquisitionRequests"
            WHERE "Kind" = $kind AND "Status" = $status
            ORDER BY "UpdatedAt";
            """,
            command =>
            {
                Add(command, "$kind", AcquisitionAccessNames.Kind(kind));
                Add(command, "$status", AcquisitionAccessNames.Status(status));
            },
            cancellationToken);

    /// <summary>Replaces the media-specific payload (for example which releases were already tried).</summary>
    public Task UpdatePayloadAsync(Guid id, string? payloadJson, CancellationToken cancellationToken) =>
        WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """UPDATE "AcquisitionRequests" SET "PayloadJson" = $payload WHERE "Id" = $id;""";
            Add(command, "$id", id.ToString());
            Add(command, "$payload", payloadJson);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }, cancellationToken);

    public async Task<int> CountPendingAsync(CancellationToken cancellationToken) =>
        await WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """SELECT COUNT(*) FROM "AcquisitionRequests" WHERE "Status" = 'pending';""";
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        }, cancellationToken);

    public async Task<AcquisitionRequest> CreateAsync(
        AcquisitionRequestDraft draft,
        string requestedByProfileId,
        AcquisitionRequestStatus status,
        string? decidedByProfileId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var request = new AcquisitionRequest(
            Guid.NewGuid(),
            draft.Kind,
            draft.Provider,
            draft.ExternalId,
            draft.Title,
            draft.Subtitle,
            draft.CoverImageUrl,
            draft.PayloadJson,
            requestedByProfileId,
            status,
            null,
            null,
            null,
            now,
            now,
            decidedByProfileId,
            decidedByProfileId is null ? null : now);

        await WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"""
                INSERT INTO "AcquisitionRequests" ({Columns})
                VALUES ($id, $kind, $provider, $externalId, $title, $subtitle, $cover, $payload,
                        $requestedBy, $status, NULL, NULL, NULL, $now, $now, $decidedBy, $decidedAt);
                """;
            Add(command, "$id", request.Id.ToString());
            Add(command, "$kind", AcquisitionAccessNames.Kind(request.Kind));
            Add(command, "$provider", request.Provider);
            Add(command, "$externalId", request.ExternalId);
            Add(command, "$title", request.Title);
            Add(command, "$subtitle", request.Subtitle);
            Add(command, "$cover", request.CoverImageUrl);
            Add(command, "$payload", request.PayloadJson);
            Add(command, "$requestedBy", request.RequestedByProfileId);
            Add(command, "$status", AcquisitionAccessNames.Status(request.Status));
            Add(command, "$now", now);
            Add(command, "$decidedBy", decidedByProfileId);
            Add(command, "$decidedAt", request.DecidedAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }, cancellationToken);

        return request;
    }

    public Task UpdateStatusAsync(
        Guid id,
        AcquisitionRequestStatus status,
        string? message,
        Guid? operationId,
        string? resultUrl,
        string? decidedByProfileId,
        CancellationToken cancellationToken) =>
        WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE "AcquisitionRequests"
                SET "Status" = $status,
                    "StatusMessage" = $message,
                    "OperationId" = COALESCE($operationId, "OperationId"),
                    "ResultUrl" = COALESCE($resultUrl, "ResultUrl"),
                    "DecidedByProfileId" = COALESCE($decidedBy, "DecidedByProfileId"),
                    "DecidedAt" = CASE WHEN $decidedBy IS NULL THEN "DecidedAt" ELSE $now END,
                    "UpdatedAt" = $now
                WHERE "Id" = $id;
                """;
            Add(command, "$id", id.ToString());
            Add(command, "$status", AcquisitionAccessNames.Status(status));
            Add(command, "$message", message);
            Add(command, "$operationId", operationId?.ToString());
            Add(command, "$resultUrl", resultUrl);
            Add(command, "$decidedBy", decidedByProfileId);
            Add(command, "$now", DateTime.UtcNow);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }, cancellationToken);

    private async Task<AcquisitionRequest?> QuerySingleAsync(
        string sql,
        Action<DbCommand> bind,
        CancellationToken cancellationToken) =>
        (await QueryAsync(sql, bind, cancellationToken)).FirstOrDefault();

    private Task<IReadOnlyList<AcquisitionRequest>> QueryAsync(
        string sql,
        Action<DbCommand> bind,
        CancellationToken cancellationToken) =>
        WithConnectionAsync<IReadOnlyList<AcquisitionRequest>>(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            bind(command);
            var rows = new List<AcquisitionRequest>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new AcquisitionRequest(
                    Guid.Parse(reader.GetString(0)),
                    AcquisitionAccessNames.ParseKind(reader.GetString(1)),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    NullableString(reader, 5),
                    NullableString(reader, 6),
                    NullableString(reader, 7),
                    reader.GetString(8),
                    AcquisitionAccessNames.ParseStatus(reader.GetString(9)),
                    NullableString(reader, 10),
                    NullableString(reader, 11) is { } operation ? Guid.Parse(operation) : null,
                    NullableString(reader, 12),
                    ParseDate(reader.GetString(13)),
                    ParseDate(reader.GetString(14)),
                    NullableString(reader, 15),
                    NullableString(reader, 16) is { } decided ? ParseDate(decided) : null));
            }

            return rows;
        }, cancellationToken);

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

    private static DateTime ParseDate(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

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
