using LeoClassroom.Services;
using LeoClassroom.Services.Moodle;
using LeoClassroom.Services.Notifications;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Repositories;
using LeoClassroom.Persistence.Util;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LeoClassroom.Test;

public sealed class AssignmentPublishServiceTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IAssignmentRepository _repo = Substitute.For<IAssignmentRepository>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IMoodleSyncService _moodle = Substitute.For<IMoodleSyncService>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly AssignmentPublishService _sut;

    public AssignmentPublishServiceTests()
    {
        _uow.AssignmentRepository.Returns(_repo);
        _clock.GetCurrentInstant().Returns(Instant.FromUtc(2026, 6, 13, 12, 0));
        _sut = new AssignmentPublishService(_uow, _notifications, _moodle, _clock,
                                            Substitute.For<ILogger<AssignmentPublishService>>());
    }

    private static Assignment Hidden(long id) => new()
    {
        Id = id, CourseId = 1, Title = "T", Slug = "t", IsPublished = false,
        PublishAt = Instant.FromUtc(2026, 6, 13, 11, 0)
    };

    [Fact]
    public async Task Run_PublishesEveryDueAssignmentAndAnnouncesIt()
    {
        Assignment first = Hidden(1);
        Assignment second = Hidden(2);
        _repo.GetDueForPublishAsync(Arg.Any<Instant>())
             .Returns(new ValueTask<IReadOnlyCollection<long>>(new long[] { 1, 2 }));
        _repo.GetTrackedWithTeachersAsync(1).Returns(new ValueTask<Assignment?>(first));
        _repo.GetTrackedWithTeachersAsync(2).Returns(new ValueTask<Assignment?>(second));

        int published = await _sut.RunAsync();

        published.Should().Be(2);
        first.IsPublished.Should().BeTrue();
        first.PublishAt.Should().BeNull();
        await _notifications.Received(1).EnqueueAssignmentCreatedAsync(first);
        await _moodle.Received(1).EnqueueCreateAsync(second);
    }

    [Fact]
    public async Task Run_OneFailure_DoesNotStopTheOthers()
    {
        Assignment second = Hidden(2);
        _repo.GetDueForPublishAsync(Arg.Any<Instant>())
             .Returns(new ValueTask<IReadOnlyCollection<long>>(new long[] { 1, 2 }));
        _repo.GetTrackedWithTeachersAsync(1).Returns<ValueTask<Assignment?>>(_ => throw new InvalidOperationException());
        _repo.GetTrackedWithTeachersAsync(2).Returns(new ValueTask<Assignment?>(second));

        int published = await _sut.RunAsync();

        published.Should().Be(1);
        second.IsPublished.Should().BeTrue();
    }
}
