using System.Text.Json.Serialization;
using LeoClassroom.Shared;

namespace LeoClassroom.Services.Forgejo;

public readonly record struct ForgejoError(int StatusCode, string Reason);

public readonly record struct AlreadyExists;

public sealed record ForgejoOrg(long Id, string Username);

public sealed record ForgejoTeam(long Id, string Name);

/// <summary>
///     The envelope the team search returns: <c>{ "ok": true, "data": [ … ] }</c>
/// </summary>
internal sealed record TeamSearchResults(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("data")] IReadOnlyList<ForgejoTeam>? Data);

public sealed record ForgejoUser(long Id, string Login);

/// <param name="IsAdmin">Whether Forgejo considers this account a site administrator</param>
/// <remarks>
///     <c>is_admin</c> needs its mapping spelled out like every other multi-word field here. Without it the
///     property simply stays <c>false</c>: nothing fails, and the health check reports a site administrator
///     as not being one while the administrative call right below it succeeds.
/// </remarks>
public sealed record ForgejoCurrentUser(
    long Id,
    string Login,
    string Email,
    [property: JsonPropertyName("is_admin")] bool IsAdmin);

public sealed record ForgejoVersion(string Version);

public sealed record ForgejoRepo(
    long Id,
    string Name,
    [property: JsonPropertyName("full_name")] string FullName,
    [property: JsonPropertyName("clone_url")] string CloneUrl,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("default_branch")] string? DefaultBranch);

public sealed record ForgejoCommit(
    string Sha,
    [property: JsonPropertyName("commit")] ForgejoCommitDetails Details,
    ForgejoUser? Author);

public sealed record ForgejoCommitDetails(
    [property: JsonPropertyName("author")] ForgejoCommitAuthor Author);

public sealed record ForgejoCommitAuthor(
    [property: JsonPropertyName("date")] string Date);

public sealed record ForgejoPullRequest(
    long Number,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("head")] ForgejoPrBranch Head,
    [property: JsonPropertyName("base")] ForgejoPrBranch Base);

public sealed record ForgejoPrBranch(
    [property: JsonPropertyName("ref")] string Ref);

internal sealed record CreateOrgBody(
    [property: JsonPropertyName("username")] string Username);

internal sealed record CreateTeamBody(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("permission")] string Permission,
    [property: JsonPropertyName("units")] string[] Units);

internal sealed record CreateRepoBody(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("private")] bool Private,
    [property: JsonPropertyName("auto_init")] bool AutoInit);

internal sealed record ForkRepoBody(
    [property: JsonPropertyName("organization")] string Organization,
    [property: JsonPropertyName("name")] string Name);

internal sealed record MigrateRepoBody(
    [property: JsonPropertyName("clone_addr")] string CloneAddr,
    [property: JsonPropertyName("repo_owner")] string RepoOwner,
    [property: JsonPropertyName("repo_name")] string RepoName,
    [property: JsonPropertyName("mirror")] bool Mirror,
    [property: JsonPropertyName("private")] bool Private);

internal sealed record CollaboratorBody(
    [property: JsonPropertyName("permission")] string Permission);

internal sealed record CreateBranchBody(
    [property: JsonPropertyName("new_branch_name")] string NewBranchName,
    [property: JsonPropertyName("old_ref_name")] string OldRefName);

internal sealed record CreatePullRequestBody(
    [property: JsonPropertyName("head")] string Head,
    [property: JsonPropertyName("base")] string Base,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("body")] string Body);

internal sealed record CreateFileBody(
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("message")] string Message);

internal sealed record CreateUserBody(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("login_name")] string LoginName,
    [property: JsonPropertyName("source_id")] long SourceId,
    [property: JsonPropertyName("must_change_password")] bool MustChangePassword,
    [property: JsonPropertyName("password")] string? Password);

internal sealed record SetUserPasswordBody(
    [property: JsonPropertyName("login_name")] string LoginName,
    [property: JsonPropertyName("source_id")] long SourceId,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("must_change_password")] bool MustChangePassword);

internal sealed record EditUserBody(
    [property: JsonPropertyName("login_name")] string LoginName,
    [property: JsonPropertyName("source_id")] long SourceId,
    [property: JsonPropertyName("active")] bool Active,
    [property: JsonPropertyName("prohibit_login")] bool ProhibitLogin);

internal sealed record CreateWebhookBody(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("active")] bool Active,
    [property: JsonPropertyName("events")] string[] Events,
    [property: JsonPropertyName("config")] WebhookConfig Config);

internal sealed record WebhookConfig(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("content_type")] string ContentType,
    [property: JsonPropertyName("secret")] string Secret);

internal sealed record PermissionResult(
    [property: JsonPropertyName("permission")] string Permission);

internal static class CollaboratorPermissionExtensions
{
    public static string ToForgejoString(this CollaboratorPermission permission) => permission switch
    {
        CollaboratorPermission.Read => "read",
        CollaboratorPermission.Write => "write",
        CollaboratorPermission.Admin => "admin",
        _ => "none"
    };

    public static CollaboratorPermission ParsePermission(string? value) => value switch
    {
        "read" => CollaboratorPermission.Read,
        "write" => CollaboratorPermission.Write,
        "admin" => CollaboratorPermission.Admin,
        "owner" => CollaboratorPermission.Admin,
        _ => CollaboratorPermission.None
    };
}
