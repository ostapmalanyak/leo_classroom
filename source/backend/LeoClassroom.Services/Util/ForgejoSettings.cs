namespace LeoClassroom.Services.Util;

public sealed class ForgejoSettings
{
    public const string SectionKey = "Forgejo";

    public required string BaseUrl { get; init; }
    public string BotName { get; init; } = "leo-classroom-bot";
    public string BotEmail { get; init; } = "bot@leoclassroom.local";
    public required string AdminToken { get; init; }
    public required string WebhookSecret { get; init; }
    public required string WebhookTargetUrl { get; init; }
    public long AuthSourceId { get; init; }
}
