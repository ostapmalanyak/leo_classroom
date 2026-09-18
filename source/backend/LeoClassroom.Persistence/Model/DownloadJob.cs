using LeoClassroom.Shared;

namespace LeoClassroom.Persistence.Model;

public class DownloadJob
{
    public long Id { get; set; }
    public long AssignmentId { get; set; }
    public required string RequestedByStudentId { get; set; }
    public DownloadSnapshotMode Mode { get; set; }
    public DownloadJobStatus Status { get; set; }
    public string? ArtifactPath { get; set; }
    public string? Notes { get; set; }
    public string? Error { get; set; }
    public Instant CreatedAt { get; set; }
    public Instant? StartedAt { get; set; }
    public Instant? CompletedAt { get; set; }
    public Instant? ExpiresAt { get; set; }
}
