namespace Jularr.Web.Features.Operations;

/// <summary>
/// The account that started the work in progress. It is set for the length of a signed-in web request,
/// so an operation created while the request runs records who asked for it; operations created by
/// scheduled or watching services have no actor and are shown as the system. The value flows with the
/// async context, so concurrent requests never see each other's account.
/// </summary>
public static class OperationActor
{
    private static readonly AsyncLocal<string?> Holder = new();

    public static string? Current => Holder.Value;

    public static IDisposable Enter(string? profileId)
    {
        var previous = Holder.Value;
        Holder.Value = string.IsNullOrWhiteSpace(profileId) ? null : profileId;
        return new Scope(previous);
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Holder.Value = previous;
        }
    }
}
