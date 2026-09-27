namespace Jularr.Web.Features.Playback.Decision;

/// <summary>
/// What the server can do for this decision right now. <see cref="ProcessingAvailable"/> is
/// false when ffmpeg is missing (no remux, no transcode); <see cref="TranscodingEnabled"/> is
/// the administrator's switch; the free slots bound concurrent encodes.
/// </summary>
public sealed record PlaybackServerCapabilities(
    bool ProcessingAvailable,
    bool TranscodingEnabled,
    int AvailableTranscodeSlots,
    string H264Encoder,
    int MaxTranscodeHeight,
    bool CanToneMap,
    bool CanBurnInSubtitles)
{
    public const string SoftwareH264Encoder = "libx264";

    /// <summary>Software encoding defaults: realistic up to 1080p on a typical home server CPU.</summary>
    public static PlaybackServerCapabilities Software(int availableSlots = 2) =>
        new(
            ProcessingAvailable: true,
            TranscodingEnabled: true,
            AvailableTranscodeSlots: availableSlots,
            H264Encoder: SoftwareH264Encoder,
            MaxTranscodeHeight: 1080,
            CanToneMap: true,
            CanBurnInSubtitles: true);
}
