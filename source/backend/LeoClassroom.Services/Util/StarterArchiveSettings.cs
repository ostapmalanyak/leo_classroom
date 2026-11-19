namespace LeoClassroom.Services.Util;

public sealed class StarterArchiveSettings
{
    public const string SectionKey = "StarterArchive";

    public string StoragePath { get; init; } = "/var/lib/leo-classroom/starter-archives";
    public long MaxUploadBytes { get; init; } = 50L * 1024 * 1024;
}
