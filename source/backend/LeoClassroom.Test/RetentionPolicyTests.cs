using LeoClassroom.Services.Util;
using LeoClassroom.Shared;

namespace LeoClassroom.Test;

public sealed class RetentionPolicyTests
{
    private static Instant OnDay(int year, int month, int day) =>
        new LocalDate(year, month, day).ToInstantInZone();

    [Theory]
    // last seen inside the 2025/26 school year -> that year ends in August 2026 -> deletable August 2027
    [InlineData(2025, 9, 1, 2027, 8, 1)]
    [InlineData(2026, 3, 15, 2027, 8, 1)]
    [InlineData(2026, 7, 31, 2027, 8, 1)]
    // 1 August starts the next school year, so it belongs to 2026/27 -> deletable August 2028
    [InlineData(2026, 8, 1, 2028, 8, 1)]
    [InlineData(2026, 9, 1, 2028, 8, 1)]
    public void DeletableFrom_IsTheAugustAfterTheSchoolYearPlusOneYear(
        int seenYear, int seenMonth, int seenDay, int dueYear, int dueMonth, int dueDay)
    {
        LocalDate deletableFrom = RetentionPolicy.DeletableFrom(OnDay(seenYear, seenMonth, seenDay));

        deletableFrom.Should().Be(new LocalDate(dueYear, dueMonth, dueDay));
    }

    [Fact]
    public void DeletableFrom_TreatsAWholeSchoolYearAsOneCohort()
    {
        // someone who left in September and someone who left the following June age out on the same day
        RetentionPolicy.DeletableFrom(OnDay(2025, 9, 2))
                       .Should().Be(RetentionPolicy.DeletableFrom(OnDay(2026, 6, 30)));
    }

    [Theory]
    // a user last seen strictly before the cutoff is deletable; the cutoff moves only on 1 August
    [InlineData(2027, 7, 31, 2025, 8, 1)]
    [InlineData(2027, 8, 1, 2026, 8, 1)]
    [InlineData(2028, 7, 31, 2026, 8, 1)]
    [InlineData(2028, 8, 1, 2027, 8, 1)]
    public void CutoffFor_IsTheAugustBoundaryOneYearBack(
        int nowYear, int nowMonth, int nowDay, int cutYear, int cutMonth, int cutDay)
    {
        Instant cutoff = RetentionPolicy.CutoffFor(OnDay(nowYear, nowMonth, nowDay));

        cutoff.Should().Be(new LocalDate(cutYear, cutMonth, cutDay).ToInstantInZone());
    }

    [Theory]
    [InlineData(2026, 3, 15)]
    [InlineData(2026, 7, 31)]
    public void CutoffAndDeletableFrom_AgreeOnTheExampleFromThePolicy(int seenYear, int seenMonth, int seenDay)
    {
        Instant lastSeen = OnDay(seenYear, seenMonth, seenDay);

        // "inactive before August 2026 cannot be deleted before August 2027"
        RetentionPolicy.DeletableFrom(lastSeen).Should().Be(new LocalDate(2027, 8, 1));
        lastSeen.Should().BeLessThan(RetentionPolicy.CutoffFor(OnDay(2027, 8, 1)));
        lastSeen.Should().BeGreaterThanOrEqualTo(RetentionPolicy.CutoffFor(OnDay(2027, 7, 31)));
    }
}
