namespace LeoClassroom.Services.Util;

public sealed class DataProtectionSettings
{
    public const string SectionKey = "DataProtection";

    public required string MasterKey { get; init; }
}
