using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Jularr.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Ai;

/// <summary>Cached model catalogs (table from migration 20260928094213).</summary>
public sealed class AiModelCatalogStore(AppDbContext db) : IAiModelCatalogStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<AiModelCatalog?> GetAsync(string providerKey, CancellationToken cancellationToken) =>
        WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT "ModelsJson", "Discovery", "FetchedAt", "LastAttemptAt", "LastError"
                FROM "AiModelCatalogs" WHERE "ProviderKey" = @key;
                """;
            Add(command, "@key", providerKey);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            IReadOnlyList<AiModelDescriptor> models;
            try
            {
                models = JsonSerializer.Deserialize<AiModelDescriptor[]>(reader.GetString(0), JsonOptions) ?? [];
            }
            catch (JsonException)
            {
                models = [];
            }

            return new AiModelCatalog(
                providerKey,
                models,
                reader.GetString(1) switch
                {
                    "supported" => AiModelDiscovery.Supported,
                    "unsupported" => AiModelDiscovery.Unsupported,
                    _ => AiModelDiscovery.Unknown
                },
                ReadTimestamp(reader, 2),
                ReadTimestamp(reader, 3),
                reader.IsDBNull(4) ? null : reader.GetString(4));
        }, cancellationToken);

    public Task SaveAsync(AiModelCatalog catalog, CancellationToken cancellationToken) =>
        WithConnectionAsync<AiModelCatalog?>(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO "AiModelCatalogs" ("ProviderKey", "ModelsJson", "Discovery", "FetchedAt", "LastAttemptAt", "LastError")
                VALUES (@key, @models, @discovery, @fetched, @attempt, @error)
                ON CONFLICT("ProviderKey") DO UPDATE SET
                    "ModelsJson" = excluded."ModelsJson",
                    "Discovery" = excluded."Discovery",
                    "FetchedAt" = excluded."FetchedAt",
                    "LastAttemptAt" = excluded."LastAttemptAt",
                    "LastError" = excluded."LastError";
                """;
            Add(command, "@key", catalog.ProviderKey);
            Add(command, "@models", JsonSerializer.Serialize(catalog.Models, JsonOptions));
            Add(command, "@discovery", catalog.Discovery switch
            {
                AiModelDiscovery.Supported => "supported",
                AiModelDiscovery.Unsupported => "unsupported",
                _ => "unknown"
            });
            Add(command, "@fetched", catalog.FetchedAt?.ToString("O", CultureInfo.InvariantCulture));
            Add(command, "@attempt", catalog.LastAttemptAt?.ToString("O", CultureInfo.InvariantCulture));
            Add(command, "@error", catalog.LastError);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return null;
        }, cancellationToken);

    private static DateTimeOffset? ReadTimestamp(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

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
