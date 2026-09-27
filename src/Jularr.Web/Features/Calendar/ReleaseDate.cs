using System.Globalization;
using System.Text.RegularExpressions;

namespace Jularr.Web.Features.Calendar;

/// <summary>How exact a release date is. Never more precise than the provider supplied.</summary>
public enum ReleaseDatePrecision
{
    Unknown = 0,
    Year = 1,
    Quarter = 2,
    Month = 3,
    Day = 4,
    DateTime = 5
}

/// <summary>
/// A release date with explicit precision: an instant (<see cref="ReleaseDatePrecision.DateTime"/>),
/// a calendar day, a month, a quarter, a year or unknown. Imprecise dates are periods, not a guessed
/// day: they are shown in the "date not exact yet" part of the calendar instead of on a fake day.
/// </summary>
public sealed partial record ReleaseDate
{
    private ReleaseDate(ReleaseDatePrecision precision, int year, int part, int day, DateTimeOffset? instant)
    {
        Precision = precision;
        Year = year;
        Part = part;
        Day = day;
        Instant = instant;
    }

    public ReleaseDatePrecision Precision { get; }

    public int Year { get; }

    /// <summary>Month (1-12) for Month/Day precision, quarter (1-4) for Quarter precision, else 0.</summary>
    public int Part { get; }

    public int Day { get; }

    /// <summary>The exact UTC instant for <see cref="ReleaseDatePrecision.DateTime"/>, else null.</summary>
    public DateTimeOffset? Instant { get; }

    public int? Month => Precision is ReleaseDatePrecision.Month or ReleaseDatePrecision.Day ? Part : null;

    public int? Quarter => Precision == ReleaseDatePrecision.Quarter ? Part : null;

    /// <summary>True when the date names one day (a day or an instant).</summary>
    public bool IsExactDay => Precision is ReleaseDatePrecision.Day or ReleaseDatePrecision.DateTime;

    public static ReleaseDate Unknown { get; } = new(ReleaseDatePrecision.Unknown, 0, 0, 0, null);

    public static ReleaseDate FromInstant(DateTimeOffset instant)
    {
        var utc = instant.ToUniversalTime();
        return new ReleaseDate(ReleaseDatePrecision.DateTime, utc.Year, utc.Month, utc.Day, utc);
    }

    public static ReleaseDate FromDay(DateOnly day) =>
        new(ReleaseDatePrecision.Day, day.Year, day.Month, day.Day, null);

    public static ReleaseDate FromMonth(int year, int month) =>
        IsYear(year) && month is >= 1 and <= 12
            ? new ReleaseDate(ReleaseDatePrecision.Month, year, month, 0, null)
            : FromYear(year);

    public static ReleaseDate FromQuarter(int year, int quarter) =>
        IsYear(year) && quarter is >= 1 and <= 4
            ? new ReleaseDate(ReleaseDatePrecision.Quarter, year, quarter, 0, null)
            : FromYear(year);

    public static ReleaseDate FromYear(int year) =>
        IsYear(year) ? new ReleaseDate(ReleaseDatePrecision.Year, year, 0, 0, null) : Unknown;

    /// <summary>
    /// A date from optional parts (such as AniList's fuzzy date): the precision is the most
    /// specific part that is present and valid; a missing month never becomes January.
    /// </summary>
    public static ReleaseDate FromParts(int? year, int? month, int? day)
    {
        if (year is not { } y || !IsYear(y))
        {
            return Unknown;
        }

        if (month is not { } m || m is < 1 or > 12)
        {
            return FromYear(y);
        }

        if (day is { } d && d >= 1 && d <= DateTime.DaysInMonth(y, m))
        {
            return FromDay(new DateOnly(y, m, d));
        }

        return FromMonth(y, m);
    }

    /// <summary>
    /// Parses stored and provider date text: ISO timestamps, "yyyy-MM-dd", "yyyy-MM", "yyyy-Qn",
    /// "Qn yyyy" and "yyyy". A timestamp at exactly midnight without a meaningful time (as EPUB
    /// dc:date and book APIs write plain dates) is a day, not an instant.
    /// </summary>
    public static bool TryParse(string? text, out ReleaseDate date)
    {
        date = Unknown;
        var value = text?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (YearOnly().Match(value) is { Success: true } yearOnly)
        {
            date = FromYear(int.Parse(yearOnly.Groups["y"].Value, CultureInfo.InvariantCulture));
            return date.Precision != ReleaseDatePrecision.Unknown;
        }

        if (YearQuarter().Match(value) is { Success: true } quarter)
        {
            date = FromQuarter(
                int.Parse(quarter.Groups["y"].Value, CultureInfo.InvariantCulture),
                int.Parse(quarter.Groups["q"].Value, CultureInfo.InvariantCulture));
            return date.Precision == ReleaseDatePrecision.Quarter;
        }

        if (YearMonth().Match(value) is { Success: true } yearMonth)
        {
            date = FromMonth(
                int.Parse(yearMonth.Groups["y"].Value, CultureInfo.InvariantCulture),
                int.Parse(yearMonth.Groups["m"].Value, CultureInfo.InvariantCulture));
            if (date.Precision != ReleaseDatePrecision.Month)
            {
                date = Unknown;
                return false;
            }

            return true;
        }

        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            date = FromDay(day);
            return true;
        }

        if (value.Length > 10 &&
            value[4] == '-' &&
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var written))
        {
            // The calendar day as written in the source: a plain date written as midnight must not
            // move to the previous day when converted to UTC.
            date = written.TimeOfDay == TimeSpan.Zero
                ? FromDay(DateOnly.FromDateTime(written.DateTime))
                : FromInstant(written);
            return true;
        }

        return false;
    }

    /// <summary>Canonical text for the cache table; round-trips through <see cref="TryParse"/>.</summary>
    public string ToStorage() => Precision switch
    {
        ReleaseDatePrecision.DateTime => Instant!.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        ReleaseDatePrecision.Day => $"{Year:D4}-{Part:D2}-{Day:D2}",
        ReleaseDatePrecision.Month => $"{Year:D4}-{Part:D2}",
        ReleaseDatePrecision.Quarter => $"{Year:D4}-Q{Part}",
        ReleaseDatePrecision.Year => $"{Year:D4}",
        _ => ""
    };

    public static ReleaseDate FromStorage(string? value, ReleaseDatePrecision precision)
    {
        if (precision == ReleaseDatePrecision.DateTime)
        {
            // Stored instants keep their precision even at exactly midnight UTC.
            return DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var instant)
                ? FromInstant(instant)
                : Unknown;
        }

        return precision != ReleaseDatePrecision.Unknown && TryParse(value, out var date) && date.Precision == precision
            ? date
            : Unknown;
    }

    /// <summary>
    /// The calendar days the date covers in <paramref name="zone"/>: one day for exact dates, the
    /// whole month, quarter or year otherwise; null when unknown.
    /// </summary>
    public (DateOnly Start, DateOnly End)? Period(TimeZoneInfo zone)
    {
        switch (Precision)
        {
            case ReleaseDatePrecision.DateTime:
                var local = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Instant!.Value, zone).DateTime);
                return (local, local);
            case ReleaseDatePrecision.Day:
                var day = new DateOnly(Year, Part, Day);
                return (day, day);
            case ReleaseDatePrecision.Month:
                var month = new DateOnly(Year, Part, 1);
                return (month, month.AddMonths(1).AddDays(-1));
            case ReleaseDatePrecision.Quarter:
                var quarter = new DateOnly(Year, (Part - 1) * 3 + 1, 1);
                return (quarter, quarter.AddMonths(3).AddDays(-1));
            case ReleaseDatePrecision.Year:
                return (new DateOnly(Year, 1, 1), new DateOnly(Year, 12, 31));
            default:
                return null;
        }
    }

    /// <summary>The single day of an exact date in <paramref name="zone"/>; null for periods.</summary>
    public DateOnly? ExactDay(TimeZoneInfo zone) => IsExactDay ? Period(zone)!.Value.Start : null;

    /// <summary>Whether the date overlaps the inclusive day range.</summary>
    public bool Overlaps(DateOnly start, DateOnly end, TimeZoneInfo zone) =>
        Period(zone) is { } period && period.Start <= end && period.End >= start;

    /// <summary>
    /// Released only when that is certain: the instant has passed, the day has begun, or the whole
    /// imprecise period lies in the past. Unknown dates are never released.
    /// </summary>
    public bool IsReleased(DateTimeOffset now, TimeZoneInfo zone)
    {
        if (Precision == ReleaseDatePrecision.DateTime)
        {
            return Instant!.Value <= now;
        }

        if (Period(zone) is not { } period)
        {
            return false;
        }

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        return IsExactDay ? period.Start <= today : period.End < today;
    }

    /// <summary>Chronological order: earlier first, and a precise date before the period it lies in.</summary>
    public (DateTime Start, int Rank) SortKey => Precision switch
    {
        ReleaseDatePrecision.DateTime => (Instant!.Value.UtcDateTime, 0),
        ReleaseDatePrecision.Unknown => (DateTime.MaxValue, 9),
        _ => (Period(TimeZoneInfo.Utc)!.Value.Start.ToDateTime(TimeOnly.MinValue), 6 - (int)Precision)
    };

    private static bool IsYear(int year) => year is >= 1000 and <= 9999;

    [GeneratedRegex(@"^(?<y>\d{4})$")]
    private static partial Regex YearOnly();

    [GeneratedRegex(@"^(?:(?<y>\d{4})-?Q(?<q>[1-4])|Q(?<q>[1-4])\s+(?<y>\d{4}))$", RegexOptions.IgnoreCase)]
    private static partial Regex YearQuarter();

    [GeneratedRegex(@"^(?<y>\d{4})-(?<m>\d{1,2})$")]
    private static partial Regex YearMonth();
}
