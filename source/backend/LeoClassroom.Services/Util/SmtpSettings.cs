namespace LeoClassroom.Services.Util;

public sealed class SmtpSettings
{
    public const string SectionKey = "Smtp";

    public required string Host { get; init; }
    public int Port { get; init; } = 25;
    public SmtpSecurity Security { get; init; } = SmtpSecurity.StartTlsWhenAvailable;
    public required string FromAddress { get; init; }
    public string FromName { get; init; } = "LEO Classroom";
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string DefaultLanguage { get; init; } = "en";
    public string AppBaseUrl { get; init; } = "https://localhost";
    public int MaxAttempts { get; init; } = 5;
    public int BaseBackoffSeconds { get; init; } = 60;
    public int PollSeconds { get; init; } = 10;
    public int BatchSize { get; init; } = 50;
}

public enum SmtpSecurity
{
    None = 0,
    StartTlsWhenAvailable = 1,
    StartTls = 2,
    SslOnConnect = 3
}
