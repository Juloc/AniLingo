using System.Globalization;

namespace Jularr.Web.Features.Providers;

/// <summary>
/// Resolves how long to pause after a rate-limit response, from the standard
/// signals in priority order: the <c>Retry-After</c> header (delta seconds or an
/// HTTP date), then an <c>X-RateLimit-Reset</c> epoch-seconds header, otherwise a
/// caller-supplied default. The result is always clamped to
/// [<paramref name="minimum"/>, <paramref name="maximum"/>].
/// This is the one shared implementation; provider-specific gates delegate to it.
/// </summary>
public static class ProviderRetryAfter
{
    public static TimeSpan Resolve(
        HttpResponseMessage response,
        DateTimeOffset now,
        TimeSpan @default,
        TimeSpan maximum,
        TimeSpan? minimum = null)
    {
        ArgumentNullException.ThrowIfNull(response);

        TimeSpan? wait = response.Headers.RetryAfter switch
        {
            { Delta: TimeSpan delta } => delta,
            { Date: DateTimeOffset date } => date - now,
            _ => null
        };

        if (wait is null &&
            response.Headers.TryGetValues("X-RateLimit-Reset", out var values) &&
            long.TryParse(
                values.FirstOrDefault(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var resetEpochSeconds))
        {
            wait = DateTimeOffset.FromUnixTimeSeconds(resetEpochSeconds) - now;
        }

        return Clamp(wait ?? @default, minimum ?? TimeSpan.FromSeconds(1), maximum);
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan minimum, TimeSpan maximum) =>
        value < minimum ? minimum : value > maximum ? maximum : value;
}
