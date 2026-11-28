using LeoClassroom.Services.Scheduling;
using Quartz;
using Quartz.Impl;
using Quartz.Impl.Matchers;

namespace LeoClassroom.Test;

public sealed class DeadlineSchedulerTests
{
    [Fact]
    public async Task Schedule_Again_MovesTheOneTriggerToJustAfterTheNewDeadline()
    {
        var factory = new StdSchedulerFactory();
        var sut = new DeadlineScheduler(factory);
        Instant moved = Instant.FromUtc(2099, 6, 14, 12, 0);

        await sut.ScheduleAsync(7, moved.Minus(Duration.FromDays(1)));
        await sut.ScheduleAsync(7, moved);

        IScheduler scheduler = await factory.GetScheduler(TestContext.Current.CancellationToken);
        var keys = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.AnyGroup(),
                                                  TestContext.Current.CancellationToken);
        ITrigger? trigger = await scheduler.GetTrigger(keys.Single(), TestContext.Current.CancellationToken);
        trigger!.StartTimeUtc.Should().Be(moved.Plus(Duration.FromSeconds(1)).ToDateTimeOffset());
    }
}
