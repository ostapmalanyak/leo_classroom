namespace LeoClassroom.Services.Util;

public sealed class DownloadSettings
{
    public const string SectionKey = "Download";

    public string ArtifactStoragePath { get; init; } = "/var/lib/leo-classroom/downloads";
    public int ArtifactTtlHours { get; init; } = 24;
    public int MaxRepos { get; init; } = 500;
    public long MaxTotalSizeBytes { get; init; } = 2L * 1024 * 1024 * 1024;
    public int DrainSeconds { get; init; } = 10;
    public string CleanupCron { get; init; } = "0 15 * * * ?";
}
