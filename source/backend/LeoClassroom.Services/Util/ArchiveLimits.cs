namespace LeoClassroom.Services.Util;

public sealed class ArchiveLimits
{
    public const string SectionKey = "ArchiveLimits";

    public long MaxTotalUncompressedBytes { get; init; } = 200L * 1024 * 1024;
    public long MaxEntryUncompressedBytes { get; init; } = 100L * 1024 * 1024;
    public int MaxEntryCount { get; init; } = 5000;
    public int MaxPathDepth { get; init; } = 32;
    public int MaxCompressionRatio { get; init; } = 100;
}
