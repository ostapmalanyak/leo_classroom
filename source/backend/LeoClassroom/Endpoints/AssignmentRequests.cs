using FluentValidation;
using LeoClassroom.Services;
using LeoClassroom.Shared;

namespace LeoClassroom.Endpoints;

public sealed record AssignmentUpdateRequest(
    string Title,
    string? Description,
    string? HintsInstructions,
    Instant? Deadline,
    DeadlineKind DeadlineKind,
    bool HardDeadlineRevokesRead,
    StarterSourceKind StarterSourceKind,
    string? StarterRepoUrl,
    string? ReadmeMarkdown,
    bool AutoDeleteEnabled,
    Instant? AutoDeleteOn,
    DownloadSnapshotMode DownloadSnapshotMode,
    bool IsPublished = true,
    Instant? PublishAt = null) : IAssignmentFields
{
    public AssignmentSettings ToSettings() => new(
        Title, Description, HintsInstructions, Deadline, DeadlineKind, HardDeadlineRevokesRead,
        StarterSourceKind, StarterRepoUrl, ReadmeMarkdown, AutoDeleteEnabled, AutoDeleteOn, DownloadSnapshotMode, IsPublished, PublishAt);

    public sealed class Validator : AbstractValidator<AssignmentUpdateRequest>
    {
        public Validator(IClock clock) => AssignmentRules.Apply(this, clock);
    }
}

public sealed record AssignmentCreateRequest(
    long CourseId,
    string Title,
    string? Description,
    string? HintsInstructions,
    Instant? Deadline,
    DeadlineKind DeadlineKind,
    bool HardDeadlineRevokesRead,
    StarterSourceKind StarterSourceKind,
    string? StarterRepoUrl,
    string? ReadmeMarkdown,
    bool AutoDeleteEnabled,
    Instant? AutoDeleteOn,
    DownloadSnapshotMode DownloadSnapshotMode,
    bool IsPublished = true,
    Instant? PublishAt = null) : IAssignmentFields
{
    public AssignmentSettings ToSettings() => new(
        Title, Description, HintsInstructions, Deadline, DeadlineKind, HardDeadlineRevokesRead,
        StarterSourceKind, StarterRepoUrl, ReadmeMarkdown, AutoDeleteEnabled, AutoDeleteOn, DownloadSnapshotMode, IsPublished, PublishAt);

    public sealed class Validator : AbstractValidator<AssignmentCreateRequest>
    {
        public Validator(IClock clock)
        {
            RuleFor(r => r.CourseId).GreaterThan(0);
            AssignmentRules.Apply(this, clock);
        }
    }
}

internal static class AssignmentRules
{
    private const int MaxTitleLength = 200;

    public static void Apply<T>(AbstractValidator<T> validator, IClock clock) where T : IAssignmentFields
    {
        validator.RuleFor(r => r.Title).NotEmpty().MaximumLength(MaxTitleLength);
        validator.RuleFor(r => r.Deadline)
                 .NotNull()
                 .When(r => r.DeadlineKind != DeadlineKind.None)
                 .WithMessage("A deadline is required when a deadline kind is set.");
        // The form refuses a date that has already passed; the API refuses it too, so no
        // client can create an assignment that is born expired.
        validator.RuleFor(r => r.Deadline)
                 .GreaterThan(clock.GetCurrentInstant())
                 .When(r => r.DeadlineKind != DeadlineKind.None && r.Deadline != null)
                 .WithMessage("The deadline has passed. Pick a date in the future.");
        // a hidden assignment may wait for an explicit publish (no date), but a date must still lie ahead
        validator.RuleFor(r => r.PublishAt)
                 .GreaterThan(SystemClock.Instance.GetCurrentInstant())
                 .When(r => !r.IsPublished && r.PublishAt != null)
                 .WithMessage("The publish date has passed. Pick a date in the future.");
        validator.RuleFor(r => r.StarterRepoUrl)
                 .NotEmpty()
                 .Must(BeAnAbsoluteHttpUrl)
                 .When(r => r.StarterSourceKind is StarterSourceKind.ForkOwnRepo or StarterSourceKind.CopyRepo)
                 .WithMessage("An absolute http(s) starter repository URL is required for fork/copy starter sources.");
        // a fork is created under the student's own account, so the student owns it and no collaborator change
        // can take their write access away - a hard deadline would be silently unenforceable
        validator.RuleFor(r => r.DeadlineKind)
                 .NotEqual(DeadlineKind.Hard)
                 .When(r => r.StarterSourceKind == StarterSourceKind.ForkOwnRepo)
                 .WithMessage("A hard deadline cannot be enforced on a forked repository, because the student owns it.");
    }

    // the starter URL is fetched and cloned server-side, so anything but http(s) - file://, ssh://, git:// and the
    // like - has to be rejected before it reaches the provisioning worker
    private static bool BeAnAbsoluteHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed)
        && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);
}

public interface IAssignmentFields
{
    public string Title { get; }
    public Instant? Deadline { get; }
    public DeadlineKind DeadlineKind { get; }
    public StarterSourceKind StarterSourceKind { get; }
    public string? StarterRepoUrl { get; }
    public bool IsPublished { get; }
    public Instant? PublishAt { get; }
}
