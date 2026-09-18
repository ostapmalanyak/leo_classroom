using LeoClassroom.Services.Notifications;
using LeoClassroom.Services.Util;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Repositories;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Test;

public sealed class NotificationServiceTests
{
    private const long AssignmentId = 11L;

    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly INotificationRepository _repo = Substitute.For<INotificationRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly NotificationService _sut;

    public NotificationServiceTests()
    {
        _uow.NotificationRepository.Returns(_repo);
        _clock.GetCurrentInstant().Returns(Instant.FromUtc(2026, 6, 14, 9, 0));
        var options = Options.Create(new SmtpSettings
        {
            Host = "localhost", FromAddress = "no-reply@test", DefaultLanguage = "en"
        });
        _sut = new NotificationService(_uow, _clock, options, Substitute.For<ILogger<NotificationService>>());
    }

    private static Assignment Assignment() => new()
    {
        Id = AssignmentId, CourseId = 7, Title = "Algorithms", Slug = "algorithms",
        Deadline = Instant.FromUtc(2026, 6, 20, 21, 59), DeadlineKind = DeadlineKind.Soft
    };

    [Fact]
    public async Task EnqueueAssignmentCreated_AddsRowsForRecipientsAndSaves()
    {
        _repo.GetRecipientsAsync(AssignmentId, NotificationTrigger.NewAssignment)
             .Returns(new ValueTask<IReadOnlyCollection<NotificationRecipient>>(
                 new List<NotificationRecipient> { new(50, "a@s"), new(51, "b@s") }.AsReadOnly()));
        _repo.ExistsAsync(Arg.Any<string>(), Arg.Any<long>()).Returns(new ValueTask<bool>(false));

        await _sut.EnqueueAssignmentCreatedAsync(Assignment());

        _repo.Received(2).Add(Arg.Is<Notification>(n => n.Trigger == NotificationTrigger.NewAssignment
                                                        && n.Status == NotificationStatus.Pending
                                                        && n.EventKey == $"new-assignment:{AssignmentId}"));
        await _uow.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task EnqueueAssignmentCreated_SkipsAlreadyQueuedRecipient()
    {
        _repo.GetRecipientsAsync(AssignmentId, NotificationTrigger.NewAssignment)
             .Returns(new ValueTask<IReadOnlyCollection<NotificationRecipient>>(
                 new List<NotificationRecipient> { new(50, "a@s"), new(51, "b@s") }.AsReadOnly()));
        _repo.ExistsAsync(Arg.Any<string>(), 50).Returns(new ValueTask<bool>(true));
        _repo.ExistsAsync(Arg.Any<string>(), 51).Returns(new ValueTask<bool>(false));

        await _sut.EnqueueAssignmentCreatedAsync(Assignment());

        _repo.Received(1).Add(Arg.Is<Notification>(n => n.RecipientUserId == 51));
        _repo.DidNotReceive().Add(Arg.Is<Notification>(n => n.RecipientUserId == 50));
    }

    [Fact]
    public async Task EnqueueDeadlineChanged_NoRecipients_DoesNotSave()
    {
        _repo.GetRecipientsAsync(AssignmentId, NotificationTrigger.DeadlineChanged)
             .Returns(new ValueTask<IReadOnlyCollection<NotificationRecipient>>(
                 new List<NotificationRecipient>().AsReadOnly()));

        await _sut.EnqueueDeadlineChangedAsync(Assignment());

        await _uow.DidNotReceive().SaveChangesAsync();
    }
}

public sealed class NotificationDispatcherTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly INotificationRepository _repo = Substitute.For<INotificationRepository>();
    private readonly IEmailSender _sender = Substitute.For<IEmailSender>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly SmtpSettings _settings = new()
    {
        Host = "localhost", FromAddress = "no-reply@test", MaxAttempts = 3, BaseBackoffSeconds = 60, AppBaseUrl = "https://app"
    };

    public NotificationDispatcherTests()
    {
        _uow.NotificationRepository.Returns(_repo);
        _clock.GetCurrentInstant().Returns(Instant.FromUtc(2026, 6, 14, 9, 0));
    }

    private NotificationDispatcher Sut() =>
        new(_uow, _sender, _clock, Options.Create(_settings), Substitute.For<ILogger<NotificationDispatcher>>());

    private static Notification Pending(int attempts = 0) => new()
    {
        Id = 1, EventKey = "new-assignment:11", RecipientUserId = 50, RecipientEmail = "a@s",
        Trigger = NotificationTrigger.NewAssignment, AssignmentId = 11, AssignmentTitle = "Algorithms",
        Language = "en", Status = NotificationStatus.Pending, Attempts = attempts
    };

    private void Batch(Notification notification) =>
        _repo.GetSendableAsync(Arg.Any<Instant>(), Arg.Any<int>())
             .Returns(new ValueTask<IReadOnlyCollection<Notification>>(
                 new List<Notification> { notification }.AsReadOnly()));

    [Fact]
    public async Task Dispatch_OnSuccess_MarksSent()
    {
        Notification notification = Pending();
        Batch(notification);
        OneOf<Success, EmailError> ok = new Success();
        _sender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
               .Returns(new ValueTask<OneOf<Success, EmailError>>(ok));

        await Sut().DispatchPendingAsync(CancellationToken.None);

        notification.Status.Should().Be(NotificationStatus.Sent);
        notification.SentAt.Should().Be(Instant.FromUtc(2026, 6, 14, 9, 0));
        await _uow.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task Dispatch_OnFailure_StaysPendingWithBackoff()
    {
        Notification notification = Pending();
        Batch(notification);
        OneOf<Success, EmailError> fail = new EmailError("smtp down");
        _sender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
               .Returns(new ValueTask<OneOf<Success, EmailError>>(fail));

        await Sut().DispatchPendingAsync(CancellationToken.None);

        notification.Status.Should().Be(NotificationStatus.Pending);
        notification.Attempts.Should().Be(1);
        notification.LastError.Should().Be("smtp down");
        notification.NextAttemptAt.Should().Be(Instant.FromUtc(2026, 6, 14, 9, 0).Plus(Duration.FromSeconds(60)));
    }

    [Fact]
    public async Task Dispatch_AtMaxAttempts_DeadLetters()
    {
        Notification notification = Pending(attempts: 2);
        Batch(notification);
        OneOf<Success, EmailError> fail = new EmailError("smtp down");
        _sender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
               .Returns(new ValueTask<OneOf<Success, EmailError>>(fail));

        await Sut().DispatchPendingAsync(CancellationToken.None);

        notification.Attempts.Should().Be(3);
        notification.Status.Should().Be(NotificationStatus.Failed);
    }
}

public sealed class EmailTemplatesTests
{
    private static Notification Notification(NotificationTrigger trigger, string language) => new()
    {
        Id = 1, EventKey = "k", RecipientUserId = 50, RecipientEmail = "a@s", Trigger = trigger,
        AssignmentId = 11, AssignmentTitle = "Algorithms", Language = language, Status = NotificationStatus.Pending,
        Deadline = new LocalDateTime(2026, 6, 20, 21, 59).InZoneLeniently(Const.TimeZone).ToInstant()
    };

    [Fact]
    public void Render_English_NewAssignment()
    {
        EmailMessage message = EmailTemplates.Render(Notification(NotificationTrigger.NewAssignment, "en"), "https://app");

        message.ToAddress.Should().Be("a@s");
        message.Subject.Should().Be("New assignment: Algorithms");
        message.Body.Should().Contain("https://app/my-assignments/11");
        message.Body.Should().Contain("20.06.2026 21:59");
    }

    [Fact]
    public void Render_German_DeadlineChanged()
    {
        EmailMessage message =
            EmailTemplates.Render(Notification(NotificationTrigger.DeadlineChanged, "de"), "https://app");

        message.Subject.Should().Be("Frist geändert: Algorithms");
        message.Body.Should().Contain("Neue Frist");
    }
}
