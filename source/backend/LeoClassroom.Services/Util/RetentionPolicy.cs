using LeoClassroom.Shared;

namespace LeoClassroom.Services.Util;

/// <summary>
///     How long a user who has left the directory is kept before they may be deleted permanently
/// </summary>
/// <remarks>
///     <para>
///         Retention runs on school years, not on a rolling window: a user is kept until the end of the school year
///         in which they disappeared, and then for one full year after that. Someone who went inactive at any point
///         before August 2026 therefore becomes deletable in August 2027, whether they left in September or in
///         July - so a whole year group ages out together and nobody is deleted mid-year.
///     </para>
///     <para>
///         The boundary is a local date in <see cref="Const.TimeZone" />, because "August" means the school's
///         August.
///     </para>
/// </remarks>
public static class RetentionPolicy
{
    /// <summary>
    ///     The month whose first day separates two school years
    /// </summary>
    public const int SchoolYearBoundaryMonth = 8;

    /// <summary>
    ///     Full years to keep a user after the end of the school year in which they went inactive
    /// </summary>
    public const int RetainedYearsAfterSchoolYear = 1;

    /// <summary>
    ///     The date on which a user last seen at <paramref name="lastSeen" /> becomes deletable
    /// </summary>
    public static LocalDate DeletableFrom(Instant lastSeen)
    {
        LocalDate lastSeenDate = lastSeen.ToZonedDateTime().Date;

        return NextBoundaryAfter(lastSeenDate).PlusYears(RetainedYearsAfterSchoolYear);
    }

    /// <summary>
    ///     The newest <c>LdapLastSeen</c> that is already past its retention at <paramref name="now" />
    /// </summary>
    /// <remarks>
    ///     A single instant, so the candidate query stays one indexable comparison rather than a per-row
    ///     calculation. <see cref="DeletableFrom" /> is monotonic in its argument, which is what makes that
    ///     equivalent: a user is deletable exactly when their last-seen instant is older than this cutoff.
    /// </remarks>
    public static Instant CutoffFor(Instant now)
    {
        LocalDate today = now.ToZonedDateTime().Date;
        LocalDate limit = today.PlusYears(-RetainedYearsAfterSchoolYear);

        return LatestBoundaryOnOrBefore(limit).ToInstantInZone();
    }

    /// <summary>
    ///     The first 1 August strictly after the given date, treating 1 August as the start of a school year
    /// </summary>
    private static LocalDate NextBoundaryAfter(LocalDate date)
    {
        var boundaryThisYear = new LocalDate(date.Year, SchoolYearBoundaryMonth, 1);

        return date < boundaryThisYear
            ? boundaryThisYear
            : new LocalDate(date.Year + 1, SchoolYearBoundaryMonth, 1);
    }

    /// <summary>
    ///     The most recent 1 August that is not after the given date
    /// </summary>
    private static LocalDate LatestBoundaryOnOrBefore(LocalDate date)
    {
        var boundaryThisYear = new LocalDate(date.Year, SchoolYearBoundaryMonth, 1);

        return date < boundaryThisYear
            ? new LocalDate(date.Year - 1, SchoolYearBoundaryMonth, 1)
            : boundaryThisYear;
    }
}
