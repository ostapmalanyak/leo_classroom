using NodaTime;
using NodaTime.Text;

namespace LeoClassroom.Cli;

public enum DeadlineKind
{
    None,
    Soft,
    Hard
}

public static class DeadlineParser
{
    private static readonly LocalDateTimePattern Pattern =
        LocalDateTimePattern.CreateWithInvariantCulture("yyyy-MM-dd HH:mm");

    // Parses "yyyy-MM-dd HH:mm" in the school timezone to a UTC Instant rendered as ISO-8601 (NodaTime form).
    public static bool TryParse(string input, string timeZoneId, out string isoInstant, out string error)
    {
        isoInstant = string.Empty;
        error = string.Empty;

        ParseResult<LocalDateTime> parsed = Pattern.Parse(input.Trim());
        if (!parsed.Success)
        {
            error = "Expected a date/time like 2026-06-30 23:59";

            return false;
        }

        DateTimeZone zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(timeZoneId)
                            ?? throw new InvalidOperationException($"Unknown timezone {timeZoneId}");
        Instant instant = parsed.Value.InZoneLeniently(zone).ToInstant();
        isoInstant = InstantPattern.ExtendedIso.Format(instant);

        return true;
    }
}
