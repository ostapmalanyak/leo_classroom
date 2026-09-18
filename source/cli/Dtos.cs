using System.Text.Json.Serialization;

namespace LeoClassroom.Cli;

// Backend DTOs the CLI exchanges. Enum-typed fields are sent as their PascalCase string values (the backend
// uses a string enum converter), and instants as ISO-8601 strings, so the source-generated context stays
// reflection-free and AOT/trim-safe.

public sealed record ApiVersionDto(
    [property: JsonPropertyName("apiVersion")] string ApiVersion);

public sealed record MeDto(
    [property: JsonPropertyName("studentId")] string StudentId,
    [property: JsonPropertyName("roles")] string[] Roles);

public sealed record CourseOverviewDto(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("rosterName")] string RosterName);

public sealed record AssignmentCreateRequestDto(
    [property: JsonPropertyName("courseId")] long CourseId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("hintsInstructions")] string? HintsInstructions,
    [property: JsonPropertyName("deadline")] string? Deadline,
    [property: JsonPropertyName("deadlineKind")] string DeadlineKind,
    [property: JsonPropertyName("hardDeadlineRevokesRead")] bool HardDeadlineRevokesRead,
    [property: JsonPropertyName("starterSourceKind")] string StarterSourceKind,
    [property: JsonPropertyName("starterRepoUrl")] string? StarterRepoUrl,
    [property: JsonPropertyName("readmeMarkdown")] string? ReadmeMarkdown,
    [property: JsonPropertyName("autoDeleteEnabled")] bool AutoDeleteEnabled,
    [property: JsonPropertyName("autoDeleteOn")] string? AutoDeleteOn,
    [property: JsonPropertyName("downloadSnapshotMode")] string DownloadSnapshotMode);

public sealed record AssignmentDto(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("slug")] string Slug);

public sealed record AcceptLinkDto(
    [property: JsonPropertyName("assignmentId")] long AssignmentId,
    [property: JsonPropertyName("acceptLink")] string AcceptLink);

public sealed record DeviceCodeResponse(
    [property: JsonPropertyName("device_code")] string DeviceCode,
    [property: JsonPropertyName("user_code")] string UserCode,
    [property: JsonPropertyName("verification_uri")] string VerificationUri,
    [property: JsonPropertyName("verification_uri_complete")] string? VerificationUriComplete,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("interval")] int Interval);

public sealed record TokenResponse(
    [property: JsonPropertyName("access_token")] string? AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("error_description")] string? ErrorDescription);

public sealed record CachedTokens(
    [property: JsonPropertyName("accessToken")] string AccessToken,
    [property: JsonPropertyName("refreshToken")] string RefreshToken,
    [property: JsonPropertyName("accessExpiresAtUnix")] long AccessExpiresAtUnix);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ApiVersionDto))]
[JsonSerializable(typeof(MeDto))]
[JsonSerializable(typeof(CourseOverviewDto[]))]
[JsonSerializable(typeof(AssignmentCreateRequestDto))]
[JsonSerializable(typeof(AssignmentDto))]
[JsonSerializable(typeof(AcceptLinkDto))]
[JsonSerializable(typeof(DeviceCodeResponse))]
[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(CachedTokens))]
public sealed partial class CliJsonContext : JsonSerializerContext;
