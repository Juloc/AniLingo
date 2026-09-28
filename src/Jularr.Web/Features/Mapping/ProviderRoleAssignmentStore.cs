using System.Data;
using System.Data.Common;
using System.Globalization;
using Jularr.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Mapping;

/// <summary>
/// Persistence of provider-role assignments (table from migration 20260929150000). Each of the six
/// <see cref="MappingProviderRole"/> values can be assigned a provider globally (a default) and
/// overridden per work. Resolution is most-specific-first: work override → global default →
/// <see cref="MappingProviderRoles.BuiltInDefault"/>, so with nothing stored the resolved roles
/// reproduce Jularr's behaviour today.
///
/// Follows the raw-ADO.NET derived-state store pattern (see <c>PresentationGroupStore</c>): the
/// table is never mapped as an EF entity, so the model snapshot is untouched and
/// <c>dotnet ef migrations has-pending-model-changes</c> stays clean. The global scope is stored
/// with an empty <c>WorkId</c> so the unique (MediaType, WorkId, Role) index also constrains it.
/// </summary>
public sealed class ProviderRoleAssignmentStore(AppDbContext db)
{
    public const string DefaultMediaType = "anime";
    private const string GlobalScope = "";

    /// <summary>Resolved role → provider for a work, most-specific-first, with the source of each.</summary>
    public async Task<IReadOnlyList<ProviderRoleAssignment>> ResolveForWorkAsync(
        Guid workId,
        CancellationToken cancellationToken,
        string mediaType = DefaultMediaType)
    {
        var media = Normalize(mediaType);
        var globals = await ReadScopeAsync(media, GlobalScope, cancellationToken);
        var overrides = await ReadScopeAsync(media, Key(workId), cancellationToken);

        return MappingProviderRoles.All
            .Select(role =>
            {
                if (overrides.TryGetValue(role, out var workProvider))
                {
                    return new ProviderRoleAssignment(role, workProvider, ProviderRoleSource.WorkOverride);
                }

                if (globals.TryGetValue(role, out var globalProvider))
                {
                    return new ProviderRoleAssignment(role, globalProvider, ProviderRoleSource.GlobalDefault);
                }

                return new ProviderRoleAssignment(
                    role,
                    MappingProviderRoles.BuiltInDefault(role),
                    ProviderRoleSource.BuiltIn);
            })
            .ToArray();
    }

    /// <summary>
    /// Resolved role → provider for exactly one role of a work (work override → global default →
    /// built-in). Feature services use this to look up the single role they care about instead of
    /// hard-wiring a provider, so a work configured with a non-default role for that concern uses
    /// the configured provider while every other concern (and every work with nothing stored)
    /// keeps today's behaviour.
    /// </summary>
    public async Task<ProviderRoleAssignment> ResolveRoleForWorkAsync(
        Guid workId,
        MappingProviderRole role,
        CancellationToken cancellationToken,
        string mediaType = DefaultMediaType)
    {
        var resolved = await ResolveForWorkAsync(workId, cancellationToken, mediaType);
        return resolved.Single(assignment => assignment.Role == role);
    }

    /// <summary>Resolved global defaults (stored value or built-in) for every role.</summary>
    public async Task<IReadOnlyList<ProviderRoleAssignment>> ResolveGlobalDefaultsAsync(
        CancellationToken cancellationToken,
        string mediaType = DefaultMediaType)
    {
        var media = Normalize(mediaType);
        var globals = await ReadScopeAsync(media, GlobalScope, cancellationToken);

        return MappingProviderRoles.All
            .Select(role => globals.TryGetValue(role, out var provider)
                ? new ProviderRoleAssignment(role, provider, ProviderRoleSource.GlobalDefault)
                : new ProviderRoleAssignment(role, MappingProviderRoles.BuiltInDefault(role), ProviderRoleSource.BuiltIn))
            .ToArray();
    }

    /// <summary>Sets or clears the global default for a role. Passing the built-in default clears the row.</summary>
    public Task SetGlobalDefaultAsync(
        MappingProviderRole role,
        string provider,
        CancellationToken cancellationToken,
        string mediaType = DefaultMediaType) =>
        SetAsync(Normalize(mediaType), GlobalScope, role, provider, clearWhenBuiltIn: true, cancellationToken);

    /// <summary>Sets a per-work override for a role, or clears it when <paramref name="provider"/> is blank.</summary>
    public Task SetWorkOverrideAsync(
        Guid workId,
        MappingProviderRole role,
        string? provider,
        CancellationToken cancellationToken,
        string mediaType = DefaultMediaType) =>
        string.IsNullOrWhiteSpace(provider)
            ? ClearWorkOverrideAsync(workId, role, cancellationToken, mediaType)
            : SetAsync(Normalize(mediaType), Key(workId), role, provider, clearWhenBuiltIn: false, cancellationToken);

    /// <summary>Removes one per-work override so the role falls back to the global/built-in default.</summary>
    public Task ClearWorkOverrideAsync(
        Guid workId,
        MappingProviderRole role,
        CancellationToken cancellationToken,
        string mediaType = DefaultMediaType) =>
        DeleteAsync(Normalize(mediaType), Key(workId), MappingProviderRoles.StorageKey(role), cancellationToken);

    /// <summary>Removes every per-work override for a work (e.g. when the work is deleted).</summary>
    public Task ClearAllWorkOverridesAsync(
        Guid workId,
        CancellationToken cancellationToken,
        string mediaType = DefaultMediaType) =>
        DeleteAsync(Normalize(mediaType), Key(workId), role: null, cancellationToken);

    private async Task SetAsync(
        string media,
        string scope,
        MappingProviderRole role,
        string provider,
        bool clearWhenBuiltIn,
        CancellationToken cancellationToken)
    {
        var normalizedProvider = MappingProviders.Normalize(provider);
        if (!MappingProviders.IsAllowedFor(role, normalizedProvider))
        {
            throw new InvalidOperationException(
                $"Provider '{normalizedProvider}' cannot fill the {role} role.");
        }

        // A global default equal to the built-in is redundant: store nothing so the built-in shows through.
        if (clearWhenBuiltIn &&
            string.Equals(normalizedProvider, MappingProviderRoles.BuiltInDefault(role), StringComparison.Ordinal))
        {
            await DeleteAsync(media, scope, MappingProviderRoles.StorageKey(role), cancellationToken);
            return;
        }

        await WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO "ProviderRoleAssignments" ("Id", "MediaType", "WorkId", "Role", "Provider", "UpdatedAt")
                VALUES ($id, $media, $work, $role, $provider, $now)
                ON CONFLICT ("MediaType", "WorkId", "Role")
                DO UPDATE SET "Provider" = excluded."Provider", "UpdatedAt" = excluded."UpdatedAt";
                """;
            Add(command, "$id", Key(Guid.NewGuid()));
            Add(command, "$media", media);
            Add(command, "$work", scope);
            Add(command, "$role", MappingProviderRoles.StorageKey(role));
            Add(command, "$provider", normalizedProvider);
            Add(command, "$now", DateTime.UtcNow);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return 0;
        }, cancellationToken);
    }

    private Task DeleteAsync(
        string media,
        string scope,
        string? role,
        CancellationToken cancellationToken) =>
        WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = role is null
                ? """DELETE FROM "ProviderRoleAssignments" WHERE "MediaType" = $media AND "WorkId" = $work;"""
                : """DELETE FROM "ProviderRoleAssignments" WHERE "MediaType" = $media AND "WorkId" = $work AND "Role" = $role;""";
            Add(command, "$media", media);
            Add(command, "$work", scope);
            if (role is not null)
            {
                Add(command, "$role", role);
            }

            await command.ExecuteNonQueryAsync(cancellationToken);
            return 0;
        }, cancellationToken);

    private async Task<Dictionary<MappingProviderRole, string>> ReadScopeAsync(
        string media,
        string scope,
        CancellationToken cancellationToken) =>
        await WithConnectionAsync(async connection =>
        {
            var result = new Dictionary<MappingProviderRole, string>();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT "Role", "Provider" FROM "ProviderRoleAssignments"
                WHERE "MediaType" = $media AND "WorkId" = $work;
                """;
            Add(command, "$media", media);
            Add(command, "$work", scope);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (MappingProviderRoles.TryParse(reader.GetString(0), out var role))
                {
                    result[role] = MappingProviders.Normalize(reader.GetString(1));
                }
            }

            return result;
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

    private static string Normalize(string mediaType)
    {
        var normalized = MappingProviders.Normalize(mediaType);
        return normalized.Length == 0 ? DefaultMediaType : normalized;
    }

    private static string Key(Guid value) => value.ToString("D");

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
