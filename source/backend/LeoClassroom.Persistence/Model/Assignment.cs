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
    public string? ReadmeMarkdown { get; set; }
    public bool AutoDeleteEnabled { get; set; }
    public Instant? AutoDeleteOn { get; set; }
    public DownloadSnapshotMode DownloadSnapshotMode { get; set; }
    public long OwnerId { get; set; }
    public User Owner { get; set; } = null!;
    public ICollection<User> CoTeachers { get; set; } = [];
    public ICollection<Acceptance> Acceptances { get; set; } = [];
}
