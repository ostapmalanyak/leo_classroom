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
    DownloadSnapshotMode DownloadSnapshotMode) : IAssignmentFields
{
    public AssignmentSettings ToSettings() => new(
        Title, Description, HintsInstructions, Deadline, DeadlineKind, HardDeadlineRevokesRead,
        StarterSourceKind, StarterRepoUrl, ReadmeMarkdown, AutoDeleteEnabled, AutoDeleteOn, DownloadSnapshotMode);

    public sealed class Validator : AbstractValidator<AssignmentUpdateRequest>
    {
        public Validator() => AssignmentRules.Apply(this);
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
    DownloadSnapshotMode DownloadSnapshotMode) : IAssignmentFields
{
    public AssignmentSettings ToSettings() => new(
        Title, Description, HintsInstructions, Deadline, DeadlineKind, HardDeadlineRevokesRead,
        StarterSourceKind, StarterRepoUrl, ReadmeMarkdown, AutoDeleteEnabled, AutoDeleteOn, DownloadSnapshotMode);

    public sealed class Validator : AbstractValidator<AssignmentCreateRequest>
    {
        public Validator()
        {
            RuleFor(r => r.CourseId).GreaterThan(0);
            AssignmentRules.Apply(this);
        }
    }
}

internal static class AssignmentRules
{
    private const int MaxTitleLength = 200;

    public static void Apply<T>(AbstractValidator<T> validator) where T : IAssignmentFields
    {
        validator.RuleFor(r => r.Title).NotEmpty().MaximumLength(MaxTitleLength);
        validator.RuleFor(r => r.Deadline)
                 .NotNull()
                 .When(r => r.DeadlineKind != DeadlineKind.None)
                 .WithMessage("A deadline is required when a deadline kind is set.");
        validator.RuleFor(r => r.StarterRepoUrl)
                 .NotEmpty()
                 .Must(BeAnAbsoluteHttpUrl)
                 .When(r => r.StarterSourceKind is StarterSourceKind.ForkOwnRepo or StarterSourceKind.CopyRepo)
                 .WithMessage("An absolute http(s) starter repository URL is required for fork/copy starter sources.");
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
}
