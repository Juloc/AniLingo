using System.Numerics;

namespace AniLingo.Web.Features.MediaSegments;

// Pure signal-processing core of cross-episode OP/ED detection: no file, process or database
// I/O. PCM samples in, a compact per-frame fingerprint out; two fingerprint sequences in, the
// best shared-segment alignment out. Kept dependency-free so it is unit-testable with synthetic
// PCM and never needs ffmpeg or a real media file to verify.
public static class AudioFingerprint
{
    // Decoded PCM is always mono, 16-bit, at this rate (chosen low so decoding and hashing an
    // intro/outro window is cheap; speech/music energy bands of interest fit comfortably below
    // the 4 kHz Nyquist limit).
    public const int SampleRateHz = 8_000;

    // ~256 ms analysis frame, 50% overlap (128 ms hop). Fine enough to localise a segment to a
    // fraction of a second, coarse enough that a whole intro/outro window is a few thousand frames.
    public const int FrameSamples = 2_048;
    public const int HopSamples = 1_024;
    public const double FrameHopSeconds = HopSamples / (double)SampleRateHz;

    // One Haitsma/Kalker-style robust hash per frame: bit m is set when the energy slope between
    // adjacent frequency bands m and m+1 increased since the previous frame. Comparing 33 log-spaced
    // bands yields 32 bits (a uint) per frame. The hash is invariant to overall gain (loudness)
    // and is stable under light re-encoding, which is what makes it usable for matching the same
    // OP/ED audio muxed differently across episodes.
    private const int BandCount = 33;
    private const int HashBits = BandCount - 1;

    public static IReadOnlyList<uint> ComputeFrameHashes(IReadOnlyList<short> pcm)
    {
        if (pcm.Count < FrameSamples)
        {
            return [];
        }

        var window = HannWindow(FrameSamples);
        var bandBinEdges = LogBandEdges(BandCount, SampleRateHz, FrameSamples);
        var frameCount = (pcm.Count - FrameSamples) / HopSamples + 1;
        var hashes = new uint[frameCount];
        var previousBandEnergy = new double[BandCount];
        var bandEnergy = new double[BandCount];
        var buffer = new double[FrameSamples];
        var hasPrevious = false;

        for (var frame = 0; frame < frameCount; frame++)
        {
            var offset = frame * HopSamples;
            for (var i = 0; i < FrameSamples; i++)
            {
                buffer[i] = pcm[offset + i] * window[i];
            }

            GoertzelBandEnergies(buffer, bandBinEdges, bandEnergy);

            uint hash = 0;
            if (hasPrevious)
            {
                for (var band = 0; band < HashBits; band++)
                {
                    var currentSlope = bandEnergy[band] - bandEnergy[band + 1];
                    var previousSlope = previousBandEnergy[band] - previousBandEnergy[band + 1];
                    if (currentSlope - previousSlope > 0)
                    {
                        hash |= 1u << band;
                    }
                }
            }

            hashes[frame] = hash;
            (previousBandEnergy, bandEnergy) = (bandEnergy, previousBandEnergy);
            hasPrevious = true;
        }

        // Frame 0 has no predecessor and is not a meaningful hash; matching starts at frame 1.
        return frameCount > 1 ? hashes[1..] : [];
    }

    // The contiguous frame range (in `a`'s index space) that best matches somewhere in `b`,
    // found by scanning every relative offset and keeping the longest fuzzy-matching run.
    // Returns null when nothing clears the match-ratio bar.
    public static FingerprintMatch? FindBestMatch(
        IReadOnlyList<uint> a,
        IReadOnlyList<uint> b,
        int maxHammingDistance,
        double minMatchRatio,
        int minRunFrames,
        int maxRunFrames,
        int maxGapFrames = 1)
    {
        if (a.Count == 0 || b.Count == 0)
        {
            return null;
        }

        FingerprintMatch? best = null;

        for (var offset = -(b.Count - 1); offset <= a.Count - 1; offset++)
        {
            var i0 = Math.Max(0, -offset);
            var i1 = Math.Min(a.Count, b.Count - offset);
            if (i1 - i0 < minRunFrames)
            {
                continue;
            }

            var runStart = -1;
            var runLength = 0;
            var runMatched = 0;
            var gap = 0;

            // A run outside [minRunFrames, maxRunFrames] is rejected outright rather than
            // clamped: a genuine repeated segment far longer than a real OP/ED (e.g. a long
            // stretch of shared silence) is not a plausible marker and reporting a truncated
            // window for it would misrepresent where playback actually diverges.
            void Evaluate(int start, int length, int matched)
            {
                if (length < minRunFrames || length > maxRunFrames)
                {
                    return;
                }

                var ratio = matched / (double)length;
                if (ratio < minMatchRatio)
                {
                    return;
                }

                if (best is null || length > best.Value.EndFrame - best.Value.StartFrame ||
                    (length == best.Value.EndFrame - best.Value.StartFrame && ratio > best.Value.MatchRatio))
                {
                    best = new FingerprintMatch(start, start + length, ratio);
                }
            }

            for (var i = i0; i < i1; i++)
            {
                var distance = BitOperations.PopCount(a[i] ^ b[i + offset]);
                if (distance <= maxHammingDistance)
                {
                    if (runLength == 0)
                    {
                        runStart = i;
                    }

                    runLength++;
                    runMatched++;
                    gap = 0;
                }
                else
                {
                    gap++;
                    if (gap > maxGapFrames)
                    {
                        Evaluate(runStart, runLength, runMatched);
                        runLength = 0;
                        runMatched = 0;
                        gap = 0;
                    }
                    else if (runLength > 0)
                    {
                        runLength++;
                    }
                }
            }

            Evaluate(runStart, runLength, runMatched);
        }

        return best;
    }

    private static double[] HannWindow(int size)
    {
        var window = new double[size];
        for (var i = 0; i < size; i++)
        {
            window[i] = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (size - 1)));
        }

        return window;
    }

    // Log-spaced band edges (in samples, i.e. Goertzel bin indices) from ~60 Hz to just under
    // the Nyquist frequency, matching how the ear (and OP/ED music) distributes energy.
    private static int[] LogBandEdges(int bandCount, int sampleRateHz, int frameSamples)
    {
        const double minFrequencyHz = 60;
        var maxFrequencyHz = sampleRateHz / 2.0 * 0.95;
        var edges = new int[bandCount];
        var logMin = Math.Log(minFrequencyHz);
        var logMax = Math.Log(maxFrequencyHz);

        for (var i = 0; i < bandCount; i++)
        {
            var t = i / (double)(bandCount - 1);
            var frequencyHz = Math.Exp(logMin + t * (logMax - logMin));
            edges[i] = Math.Clamp(
                (int)Math.Round(frequencyHz * frameSamples / sampleRateHz),
                1,
                frameSamples / 2 - 1);
        }

        // Bands must be strictly increasing so each Goertzel bin is evaluated once.
        for (var i = 1; i < bandCount; i++)
        {
            if (edges[i] <= edges[i - 1])
            {
                edges[i] = edges[i - 1] + 1;
            }
        }

        return edges;
    }

    // One Goertzel evaluation per band edge: cheaper than a full FFT when only a few dozen
    // frequency bins are needed, and simple enough to keep dependency-free.
    private static void GoertzelBandEnergies(double[] windowed, int[] binIndices, double[] energies)
    {
        var n = windowed.Length;
        for (var band = 0; band < binIndices.Length; band++)
        {
            var k = binIndices[band];
            var omega = 2 * Math.PI * k / n;
            var coefficient = 2 * Math.Cos(omega);
            double s0 = 0, s1 = 0, s2 = 0;
            for (var i = 0; i < n; i++)
            {
                s0 = windowed[i] + coefficient * s1 - s2;
                s2 = s1;
                s1 = s0;
            }

            var real = s1 - s2 * Math.Cos(omega);
            var imaginary = s2 * Math.Sin(omega);
            energies[band] = real * real + imaginary * imaginary;
        }
    }
}

// A matched, contiguous frame range in the "own" fingerprint sequence (EndFrame exclusive).
public readonly record struct FingerprintMatch(int StartFrame, int EndFrame, double MatchRatio)
{
    public int LengthFrames => EndFrame - StartFrame;
}
