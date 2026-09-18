namespace LeoClassroom.Services.Util;

public sealed class MoodleSettings
{
    public const string SectionKey = "Moodle";

    public string CreateFunction { get; init; } = "local_leoclassroom_create_item";
    public string UpdateFunction { get; init; } = "local_leoclassroom_update_item";
    public string DeleteFunction { get; init; } = "local_leoclassroom_delete_item";
    public string AppBaseUrl { get; init; } = "https://localhost";
    public int MaxAttempts { get; init; } = 5;
    public int BaseBackoffSeconds { get; init; } = 60;
    public int OutboxDrainSeconds { get; init; } = 15;
    public int BatchSize { get; init; } = 50;
    public string BackfillCron { get; init; } = "0 0 4 * * ?";
}
