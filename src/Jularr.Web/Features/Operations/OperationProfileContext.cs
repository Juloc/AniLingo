namespace Jularr.Web.Features.Operations;

public sealed class OperationProfileContext
{
    public string? ProfileId { get; private set; }

    public IDisposable Enter(string? profileId)
    {
        var previousProfileId = ProfileId;
        ProfileId = string.IsNullOrWhiteSpace(profileId)
            ? null
            : profileId;

        return new ProfileScope(this, previousProfileId);
    }

    private sealed class ProfileScope(
        OperationProfileContext context,
        string? previousProfileId) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            context.ProfileId = previousProfileId;
        }
    }
}
