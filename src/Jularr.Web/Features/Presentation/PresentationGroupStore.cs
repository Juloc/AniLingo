using System.Data;
using System.Data.Common;
using System.Globalization;
using Jularr.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Presentation;

/// <summary>
/// Persistence of presentation groups and their ranges (tables from migration 20260929130000).
/// Follows the raw-ADO.NET store pattern used by other derived-state features (e.g.
/// <c>ChapterArtworkStore</c>): the tables are never mapped as EF entities, so the model snapshot is
/// untouched and migrations stay clean. Reads and writes go through the shared <see cref="AppDbContext"/>
/// connection.
/// </summary>
public sealed class PresentationGroupStore(AppDbContext db)
{
    /// <summary>Groups for a work, ordered by sort order, each with its ranges ordered.</summary>
    public async Task<IReadOnlyList<PresentationGroup>> ListForWorkAsync(
        PresentationMediaType mediaType,
        Guid workId,
        CancellationToken cancellationToken)
    {
        var media = PresentationMediaTypes.ToStorage(mediaType);

        return await WithConnectionAsync<IReadOnlyList<PresentationGroup>>(async connection =>
        {
            var rangesByGroup = new Dictionary<Guid, List<PresentationRange>>();
            await using (var rangeCommand = connection.CreateCommand())
            {
                rangeCommand.CommandText =
                    """
                    SELECT r."GroupId", r."StartUnit", r."EndUnit"
                    FROM "PresentationGroupRanges" r
                    JOIN "PresentationGroups" g ON g."Id" = r."GroupId"
                    WHERE g."MediaType" = @media AND g."WorkId" = @work
                    ORDER BY r."SortOrder", r."StartUnit";
                    """;
                Add(rangeCommand, "@media", media);
                Add(rangeCommand, "@work", Key(workId));
                await using var reader = await rangeCommand.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var groupId = Guid.Parse(reader.GetString(0));
                    if (!rangesByGroup.TryGetValue(groupId, out var list))
                    {
                        list = [];
                        rangesByGroup[groupId] = list;
                    }

                    list.Add(new PresentationRange((int)reader.GetInt64(1), (int)reader.GetInt64(2)));
                }
            }

            var groups = new List<PresentationGroup>();
            await using (var groupCommand = connection.CreateCommand())
            {
                groupCommand.CommandText =
                    """
                    SELECT "Id", "Name", "SortOrder", "CreatedAt", "UpdatedAt"
                    FROM "PresentationGroups"
                    WHERE "MediaType" = @media AND "WorkId" = @work
                    ORDER BY "SortOrder", "CreatedAt";
                    """;
                Add(groupCommand, "@media", media);
                Add(groupCommand, "@work", Key(workId));
                await using var reader = await groupCommand.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var id = Guid.Parse(reader.GetString(0));
                    groups.Add(new PresentationGroup(
                        id,
                        mediaType,
                        workId,
                        reader.GetString(1),
                        (int)reader.GetInt64(2),
                        rangesByGroup.TryGetValue(id, out var ranges) ? ranges : [],
                        ParseDate(reader.GetString(3)),
                        ParseDate(reader.GetString(4))));
                }
            }

            return groups;
        }, cancellationToken);
    }

    public async Task<bool> HasGroupsAsync(
        PresentationMediaType mediaType,
        Guid workId,
        CancellationToken cancellationToken) =>
        (await ListForWorkAsync(mediaType, workId, cancellationToken)).Count > 0;

    /// <summary>
    /// Atomically replaces every group for a work with <paramref name="drafts"/>. Drafts are stored
    /// in list order (sort order = index); each draft's ranges keep their list order too. Groups
    /// with a blank name or no ranges are skipped so the store never persists empty display sections.
    /// </summary>
    public Task ReplaceForWorkAsync(
        PresentationMediaType mediaType,
        Guid workId,
        IReadOnlyList<PresentationGroupDraft> drafts,
        CancellationToken cancellationToken)
    {
        var media = PresentationMediaTypes.ToStorage(mediaType);
        var now = DateTime.UtcNow;

        return WithConnectionAsync(async connection =>
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await using (var deleteRanges = connection.CreateCommand())
            {
                deleteRanges.Transaction = transaction;
                deleteRanges.CommandText =
                    """
                    DELETE FROM "PresentationGroupRanges"
                    WHERE "GroupId" IN (
                        SELECT "Id" FROM "PresentationGroups" WHERE "MediaType" = @media AND "WorkId" = @work);
                    """;
                Add(deleteRanges, "@media", media);
                Add(deleteRanges, "@work", Key(workId));
                await deleteRanges.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var deleteGroups = connection.CreateCommand())
            {
                deleteGroups.Transaction = transaction;
                deleteGroups.CommandText =
                    """DELETE FROM "PresentationGroups" WHERE "MediaType" = @media AND "WorkId" = @work;""";
                Add(deleteGroups, "@media", media);
                Add(deleteGroups, "@work", Key(workId));
                await deleteGroups.ExecuteNonQueryAsync(cancellationToken);
            }

            var order = 0;
            foreach (var draft in drafts)
            {
                var name = draft.Name?.Trim();
                var ranges = (draft.Ranges ?? [])
                    .Where(range => range.StartUnit > 0 && range.EndUnit > 0)
                    .ToArray();
                if (string.IsNullOrWhiteSpace(name) || ranges.Length == 0)
                {
                    continue;
                }

                var groupId = Guid.NewGuid();
                await using (var insertGroup = connection.CreateCommand())
                {
                    insertGroup.Transaction = transaction;
                    insertGroup.CommandText =
                        """
                        INSERT INTO "PresentationGroups"
                            ("Id", "MediaType", "WorkId", "Name", "SortOrder", "CreatedAt", "UpdatedAt")
                        VALUES (@id, @media, @work, @name, @order, @now, @now);
                        """;
                    Add(insertGroup, "@id", Key(groupId));
                    Add(insertGroup, "@media", media);
                    Add(insertGroup, "@work", Key(workId));
                    Add(insertGroup, "@name", name.Length <= NameMaxLength ? name : name[..NameMaxLength]);
                    Add(insertGroup, "@order", order);
                    Add(insertGroup, "@now", now);
                    await insertGroup.ExecuteNonQueryAsync(cancellationToken);
                }

                var rangeOrder = 0;
                foreach (var range in ranges)
                {
                    await using var insertRange = connection.CreateCommand();
                    insertRange.Transaction = transaction;
                    insertRange.CommandText =
                        """
                        INSERT INTO "PresentationGroupRanges"
                            ("Id", "GroupId", "StartUnit", "EndUnit", "SortOrder")
                        VALUES (@id, @group, @start, @end, @order);
                        """;
                    Add(insertRange, "@id", Key(Guid.NewGuid()));
                    Add(insertRange, "@group", Key(groupId));
                    Add(insertRange, "@start", range.Low);
                    Add(insertRange, "@end", range.High);
                    Add(insertRange, "@order", rangeOrder);
                    await insertRange.ExecuteNonQueryAsync(cancellationToken);
                    rangeOrder++;
                }

                order++;
            }

            await transaction.CommitAsync(cancellationToken);
            return 0;
        }, cancellationToken);
    }

    /// <summary>Removes every group (and its ranges) for a work, e.g. when the work is deleted.</summary>
    public Task DeleteForWorkAsync(
        PresentationMediaType mediaType,
        Guid workId,
        CancellationToken cancellationToken) =>
        ReplaceForWorkAsync(mediaType, workId, [], cancellationToken);

    public const int NameMaxLength = 80;

    private async Task<int> WithConnectionAsync(
        Func<DbConnection, Task<int>> action,
        CancellationToken cancellationToken) =>
        await WithConnectionAsync<int>(action, cancellationToken);

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
