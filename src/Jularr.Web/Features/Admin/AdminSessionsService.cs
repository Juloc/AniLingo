using Jularr.Web.Data;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Playback.Decision;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Admin;

/// <summary>One live playback session, joined with the human-readable title and viewer name.</summary>
public sealed record AdminSessionRow(
    Guid SessionId,
    string ProfileId,
    string ProfileName,
    string Title,
    PlaybackPlan Plan,
    string ClientKind,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset LastSeenUtc)
{
    public bool IsTranscoding => Plan.Mode == PlaybackDeliveryMode.Transcode;

    public string PlaybackMethodKey => Plan.Mode switch
    {
        PlaybackDeliveryMode.DirectPlay => "playback.mode.direct_play",
        PlaybackDeliveryMode.DirectStream => "playback.mode.direct_stream",
        PlaybackDeliveryMode.Transcode => "playback.mode.transcode",
        _ => "playback.mode.unavailable"
    };

    public string? TranscodeReason(UiTextBundle ui)
    {
        if (Plan.Mode == PlaybackDeliveryMode.DirectPlay)
        {
            return null;
        }

        var reason = Plan.Reasons.FirstOrDefault(x =>
                x.Severity is PlaybackReasonSeverity.Blocker or PlaybackReasonSeverity.Limit)
            ?? Plan.Reasons.FirstOrDefault();
        if (reason is null)
        {
            return null;
        }

        var replacements = reason.Values?
            .Select(pair => (pair.Key, (object?)pair.Value))
            .ToArray() ?? [];
        return ui.Format($"playback.reason.{reason.Code}", replacements);
    }

    public string SourceSummary(UiTextBundle ui) => FormatEndpoint(
        Plan.SourceContainer ?? Plan.Container,
        Plan.Video?.SourceCodec,
        Plan.Quality.SourceBitrateKbps,
        ui);

    public string DeliveredSummary(UiTextBundle ui) => FormatEndpoint(
        Plan.Container,
        Plan.Video is { Copy: true } ? Plan.Video.SourceCodec : Plan.Video?.OutputCodec,
        Plan.Quality.DeliveredBitrateKbps,
        ui);

    private static string FormatEndpoint(string? container, string? codec, int? bitrateKbps, UiTextBundle ui)
    {
        var containerAndCodec = string.Join('/', new[] { container, codec }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var bitrate = bitrateKbps is > 0
            ? ui.Format("playback.quality.mbps", ("value", Math.Round(bitrateKbps.Value / 1000d, 1)))
            : null;

        return string.Join(
            " · ",
            new[] { containerAndCodec, bitrate }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }
}

/// <summary>
/// Joins live <see cref="PlaybackStreamSession"/> entries with the episode title and viewer name
/// for the Admin &gt; Sessions and Profile &gt; Devices pages. Read-only; stopping a session goes
/// straight through <see cref="PlaybackStreamSessionStore"/>.
/// </summary>
public sealed class AdminSessionsService(AppDbContext db, PlaybackStreamSessionStore sessions)
{
    /// <summary>Every live session (Admin &gt; Sessions, owner-only).</summary>
    public Task<IReadOnlyList<AdminSessionRow>> ListAllAsync(CancellationToken cancellationToken) =>
        BuildRowsAsync(sessions.ListAll(), cancellationToken);

    /// <summary>One profile's own live sessions (Profile &gt; Devices).</summary>
    public Task<IReadOnlyList<AdminSessionRow>> ListForProfileAsync(string profileId, CancellationToken cancellationToken) =>
        BuildRowsAsync(sessions.ListForProfile(profileId), cancellationToken);

    private async Task<IReadOnlyList<AdminSessionRow>> BuildRowsAsync(
        IReadOnlyList<PlaybackStreamSession> live,
        CancellationToken cancellationToken)
    {
        if (live.Count == 0)
        {
            return [];
        }

        var episodeIds = live.Select(x => x.EpisodeId).Distinct().ToArray();
        var episodes = await (
                from episode in db.Episodes.AsNoTracking()
                join anime in db.Anime.AsNoTracking() on episode.AnimeId equals anime.Id
                where episodeIds.Contains(episode.Id)
                select new
                {
                    episode.Id,
                    AnimeTitle = anime.Title,
                    episode.SeasonNumber,
                    episode.Number,
                    episode.Title
                })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var profileIds = live.Select(x => x.ProfileId).Distinct().ToArray();
        var names = await db.OwnerAccounts
            .AsNoTracking()
            .Where(x => profileIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.UserName, cancellationToken);

        return live
            .Select(session =>
            {
                var title = episodes.TryGetValue(session.EpisodeId, out var episode)
                    ? $"{episode.AnimeTitle} · S{episode.SeasonNumber:00}E{episode.Number:00}"
                        + (string.IsNullOrWhiteSpace(episode.Title) ? "" : $" – {episode.Title}")
                    : session.EpisodeId.ToString();

                return new AdminSessionRow(
                    session.Id,
                    session.ProfileId,
                    names.GetValueOrDefault(session.ProfileId, session.ProfileId),
                    title,
                    session.Plan,
                    session.Selections.ClientKind,
                    session.CreatedAtUtc,
                    session.LastSeenUtc);
            })
            .ToArray();
    }
}
