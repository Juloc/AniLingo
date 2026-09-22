namespace AniLingo.Web.Features.Subtitles;

public sealed class SubtitleTrack
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EpisodeId { get; set; }
    public string Path { get; set; } = "";
    public string Language { get; set; } = "ja";
    public string Format { get; set; } = "";
    public DateTimeOffset SourceUpdatedAt { get; set; }
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SubtitleCue
{
    public long Id { get; set; }
    public Guid SubtitleTrackId { get; set; }
    public int StartMs { get; set; }
    public int EndMs { get; set; }
    public string Text { get; set; } = "";
}

public sealed record SubtitleCueData(int StartMs, int EndMs, string Text);
