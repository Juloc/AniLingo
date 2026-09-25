using System.Text.Json;

namespace AniLingo.Web.Features.Acquisition.Sabnzbd;

public enum SabnzbdAcquisitionState
{
    Queued,
    Downloading,
    Processing,
    Completed,
    Failed,
    Cancelled
}

public sealed record SabnzbdAcquisitionJob(
    Guid Id,
    string NzoId,
    Guid AnimeId,
    Guid[] EpisodeIds,
    string ReleaseIdentity,
    string ReleaseTitle,
    SabnzbdAcquisitionState State,
    int Attempt,
    SabnzbdFailureKind FailureKind,
    string? FailureMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed class SabnzbdAcquisitionStore
{
    private const string FileName = "sabnzbd-jobs.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string storePath;

    public SabnzbdAcquisitionStore()
        : this(new DirectoryInfo("/data/acquisition"))
    {
    }

    public SabnzbdAcquisitionStore(DirectoryInfo directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        storePath = Path.Combine(directory.FullName, FileName);
    }

    public async Task<IReadOnlyList<SabnzbdAcquisitionJob>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadUnsafeAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<SabnzbdAcquisitionJob> AddAsync(
        SabnzbdAcquisitionJob job,
        CancellationToken cancellationToken = default)
    {
        Validate(job);

        await gate.WaitAsync(cancellationToken);
        try
        {
            var jobs = await ReadUnsafeAsync(cancellationToken);
            if (jobs.Any(existing =>
                    existing.Id == job.Id ||
                    existing.NzoId.Equals(job.NzoId, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    "SABnzbd acquisition job already exists.");
            }

            jobs.Add(job);
            await WriteUnsafeAsync(jobs, cancellationToken);
            return job;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<SabnzbdAcquisitionJob?> UpdateStateAsync(
        string nzoId,
        SabnzbdAcquisitionState state,
        SabnzbdFailureKind failureKind = SabnzbdFailureKind.None,
        string? failureMessage = null,
        string? replacementNzoId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(nzoId))
        {
            return null;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            var jobs = await ReadUnsafeAsync(cancellationToken);
            var index = jobs.FindIndex(job =>
                job.NzoId.Equals(nzoId.Trim(), StringComparison.OrdinalIgnoreCase));

            if (index < 0)
            {
                return null;
            }

            var current = jobs[index];
            var next = current with
            {
                NzoId = string.IsNullOrWhiteSpace(replacementNzoId)
                    ? current.NzoId
                    : replacementNzoId.Trim(),
                State = state,
                FailureKind = failureKind,
                FailureMessage = failureMessage,
                Attempt = string.IsNullOrWhiteSpace(replacementNzoId)
                    ? current.Attempt
                    : current.Attempt + 1,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            Validate(next);
            jobs[index] = next;
            await WriteUnsafeAsync(jobs, cancellationToken);
            return next;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<List<SabnzbdAcquisitionJob>> ReadUnsafeAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(storePath))
        {
            return [];
        }

        try
        {
            var json = await File.ReadAllTextAsync(storePath, cancellationToken);
            var jobs = JsonSerializer.Deserialize<List<SabnzbdAcquisitionJob>>(
                    json,
                    JsonOptions)
                ?? [];

            foreach (var job in jobs)
            {
                Validate(job);
            }

            return jobs;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "SABnzbd acquisition state contains invalid JSON.",
                exception);
        }
    }

    private async Task WriteUnsafeAsync(
        IReadOnlyCollection<SabnzbdAcquisitionJob> jobs,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(storePath)
            ?? throw new InvalidOperationException(
                "SABnzbd acquisition path has no directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = $"{storePath}.tmp-{Guid.NewGuid():N}";
        try
        {
            var json = JsonSerializer.Serialize(jobs, JsonOptions);
            await File.WriteAllTextAsync(
                temporaryPath,
                json,
                cancellationToken);
            File.Move(temporaryPath, storePath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static void Validate(SabnzbdAcquisitionJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (job.Id == Guid.Empty ||
            job.AnimeId == Guid.Empty ||
            string.IsNullOrWhiteSpace(job.NzoId) ||
            string.IsNullOrWhiteSpace(job.ReleaseIdentity) ||
            string.IsNullOrWhiteSpace(job.ReleaseTitle) ||
            job.Attempt < 1 ||
            job.EpisodeIds is null ||
            job.EpisodeIds.Any(id => id == Guid.Empty))
        {
            throw new InvalidDataException(
                "SABnzbd acquisition job contains invalid required fields.");
        }
    }
}
