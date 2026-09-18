namespace LeoClassroom.Services.Util;

public sealed class AuditSettings
{
    public const string SectionKey = "Audit";

    public int RetentionYears { get; init; } = 5;
    public string RetentionCron { get; init; } = "0 0 3 * * ?";
}
