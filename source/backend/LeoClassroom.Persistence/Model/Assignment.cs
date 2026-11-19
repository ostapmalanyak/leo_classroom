using LeoClassroom.Shared;

namespace LeoClassroom.Persistence.Model;

public class Assignment
{
    public long Id { get; set; }
    public long CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public required string Title { get; set; }
    public required string Slug { get; set; }
    public string? Description { get; set; }
    public string? HintsInstructions { get; set; }
    public Instant? Deadline { get; set; }
    public DeadlineKind DeadlineKind { get; set; }
    public bool HardDeadlineRevokesRead { get; set; }
    public StarterSourceKind StarterSourceKind { get; set; }
    public string? StarterRepoUrl { get; set; }
    public string? ArchivePath { get; set; }
    public string? StarterTemplateRepoUrl { get; set; }
    public string? ReadmeMarkdown { get; set; }
    public bool AutoDeleteEnabled { get; set; }
    public Instant? AutoDeleteOn { get; set; }
    public DownloadSnapshotMode DownloadSnapshotMode { get; set; }
    /// <summary>Students only see published assignments; teachers see all of them</summary>
    public bool IsPublished { get; set; } = true;
    /// <summary>When an unpublished assignment goes live by itself; null means only an explicit publish does</summary>
    public Instant? PublishAt { get; set; }

    /// <summary>Whether students may see it at <paramref name="now" />; the scheduled publish needs no job to take effect</summary>
    public bool IsVisibleAt(Instant now) => IsPublished || (PublishAt is { } at && at <= now);
    public long OwnerId { get; set; }
    public User Owner { get; set; } = null!;
    public ICollection<User> CoTeachers { get; set; } = [];
    public ICollection<Acceptance> Acceptances { get; set; } = [];
}
