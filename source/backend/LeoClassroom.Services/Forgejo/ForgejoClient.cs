using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LeoClassroom.Services.Security;
using LeoClassroom.Shared;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Services.Forgejo;

public interface IForgejoClient
{
    public ValueTask<OneOf<ForgejoOrg, NotFound, ForgejoError>> GetOrgAsync(string org);
    public ValueTask<OneOf<Success, ForgejoError>> EnsureOrgAsync(string org);
    public ValueTask<OneOf<Success, ForgejoError>> DeleteOrgAsync(string org);

    public ValueTask<OneOf<ForgejoTeam, NotFound, ForgejoError>> GetTeamAsync(string org, string teamName);
    public ValueTask<OneOf<Success<long>, ForgejoError>> EnsureTeamAsync(string org, string teamName,
                                                                         CollaboratorPermission permission);
    public ValueTask<OneOf<bool, ForgejoError>> IsTeamMemberAsync(long teamId, string username);
    public ValueTask<OneOf<IReadOnlyCollection<string>, ForgejoError>> ListTeamMembersAsync(long teamId);
    public ValueTask<OneOf<Success, ForgejoError>> AddTeamMemberAsync(long teamId, string username);
    public ValueTask<OneOf<Success, ForgejoError>> RemoveTeamMemberAsync(long teamId, string username);

    public ValueTask<OneOf<ForgejoUser, NotFound, ForgejoError>> GetUserAsync(string username);

    /// <summary>Forgejo's own version. Reachability and nothing else: the route needs no authentication.</summary>
    public ValueTask<OneOf<ForgejoVersion, ForgejoError>> GetVersionAsync();

    /// <summary>Who the admin token authenticates as, and whether Forgejo treats it as a site admin.</summary>
    public ValueTask<OneOf<ForgejoCurrentUser, ForgejoError>> GetAuthenticatedUserAsync();

    /// <summary>
    ///     Exercises one genuinely administrative route, which is the only way to learn that the token
    ///     carries <c>write:admin</c> and that nothing gates the account - a must-change-password flag is
    ///     answered here with a 403 while every unauthenticated route still works.
    /// </summary>
    public ValueTask<OneOf<Success, ForgejoError>> CheckAdminAccessAsync();

    public ValueTask<OneOf<ForgejoRepo, NotFound, ForgejoError>> GetRepoAsync(string owner, string repo);
    public ValueTask<OneOf<IReadOnlyCollection<ForgejoCommit>, NotFound, ForgejoError>> GetAllCommitsAsync(
        string owner, string repo);
    public ValueTask<OneOf<Success<ForgejoRepo>, AlreadyExists, ForgejoError>> CreateOrgRepoAsync(
        string org, string name, bool autoInit, bool isPrivate);
    public ValueTask<OneOf<Success, ForgejoError>> DeleteRepoAsync(string owner, string repo);
    public ValueTask<OneOf<Success<ForgejoRepo>, ForgejoError>> ForkRepoAsync(
        string sourceOwner, string sourceRepo, string targetOrg, string newName);
    public ValueTask<OneOf<Success<ForgejoRepo>, ForgejoError>> MigrateRepoAsync(
        string cloneUrl, string targetOrg, string newName, bool isPrivate);
    public ValueTask<OneOf<Success, ForgejoError>> CreateFileAsync(
        string owner, string repo, string path, string content, string message);

    public ValueTask<OneOf<CollaboratorPermission, NotFound, ForgejoError>> GetCollaboratorPermissionAsync(
        string owner, string repo, string username);
    public ValueTask<OneOf<Success, ForgejoError>> SetCollaboratorAsync(
        string owner, string repo, string username, CollaboratorPermission permission);
    public ValueTask<OneOf<Success, ForgejoError>> RemoveCollaboratorAsync(string owner, string repo, string username);

    public ValueTask<OneOf<Success, ForgejoError>> CreateBranchAsync(
        string owner, string repo, string newBranch, string oldRefName);
    public ValueTask<OneOf<ForgejoPullRequest, NotFound, ForgejoError>> FindOpenPullRequestAsync(
        string owner, string repo, string head, string baseBranch);
    public ValueTask<OneOf<Success<ForgejoPullRequest>, AlreadyExists, ForgejoError>> CreatePullRequestAsync(
        string owner, string repo, string head, string baseBranch, string title, string body);

    public ValueTask<OneOf<Success, ForgejoError>> EnsureOrgWebhookAsync(string org, string url, string secret);

    public ValueTask<OneOf<Success, ForgejoError>> EnsureUserAsync(string studentId, string? email, long authSourceId);

    /// <summary>
    ///     Replaces the password of a local Forgejo account
    /// </summary>
    /// <remarks>
    ///     Only meaningful for accounts that are not bound to an external login source: with a source the
    ///     password is never consulted.
    /// </remarks>
    public ValueTask<OneOf<Success, NotFound, ForgejoError>> SetUserPasswordAsync(string studentId, string password);
    public ValueTask<OneOf<Success, ForgejoError>> SetUserActiveAsync(string studentId, bool active, long authSourceId);
}

internal sealed class ForgejoClient(HttpClient http, ILogger<ForgejoClient> logger) : IForgejoClient
{
    public async ValueTask<OneOf<ForgejoOrg, NotFound, ForgejoError>> GetOrgAsync(string org)
    {
        HttpResponseMessage response = await http.GetAsync($"api/v1/orgs/{Escape(org)}");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new NotFound();
        }
        if (!response.IsSuccessStatusCode)
        {
            return await ErrorAsync(response, "get org");
        }

        return (await ReadAsync<ForgejoOrg>(response))!;
    }

    public async ValueTask<OneOf<Success, ForgejoError>> EnsureOrgAsync(string org)
    {
        OneOf<ForgejoOrg, NotFound, ForgejoError> existing = await GetOrgAsync(org);

        return await existing.Match<ValueTask<OneOf<Success, ForgejoError>>>(
            found => ValueTask.FromResult<OneOf<Success, ForgejoError>>(new Success()),
            notFound => CreateOrgAsync(org),
            error => ValueTask.FromResult<OneOf<Success, ForgejoError>>(error));
    }

    private async ValueTask<OneOf<Success, ForgejoError>> CreateOrgAsync(string org)
    {
        HttpResponseMessage response = await http.PostAsJsonAsync("api/v1/orgs", new CreateOrgBody(org));
        if (response.IsSuccessStatusCode)
        {
            return new Success();
        }

        // 422/409 after a lookup that said "not there" is usually a race - something created it in between -
        // but it is equally what Forgejo answers when it refuses the name outright. Accepting either as
        // success would let the caller go on to create repositories inside an organisation that does not
        // exist, and meet a 404 with nothing to say why.
        if (response.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict)
        {
            OneOf<ForgejoOrg, NotFound, ForgejoError> raced = await GetOrgAsync(org);

            return await raced.Match<ValueTask<OneOf<Success, ForgejoError>>>(
                found => ValueTask.FromResult<OneOf<Success, ForgejoError>>(new Success()),
                async notFound => await ErrorAsync(response, "create org"),
                error => ValueTask.FromResult<OneOf<Success, ForgejoError>>(error));
        }

        return await ErrorAsync(response, "create org");
    }

    /// <summary>
    ///     Finds a team in an organisation by its exact name
    /// </summary>
    /// <remarks>
    ///     There is no "get team by name" route - <c>/orgs/{org}/teams/{name}</c> does not exist and answers
    ///     404 for every team, which silently turns every "ensure" into a "create". The search route is the
    ///     supported way, and it matches loosely, so the exact name is picked out of the results here.
    /// </remarks>
    public async ValueTask<OneOf<ForgejoTeam, NotFound, ForgejoError>> GetTeamAsync(string org, string teamName)
    {
        HttpResponseMessage response =
            await http.GetAsync($"api/v1/orgs/{Escape(org)}/teams/search?q={Escape(teamName)}");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new NotFound();
        }
        if (!response.IsSuccessStatusCode)
        {
            return await ErrorAsync(response, "search teams");
        }

        TeamSearchResults? results = await ReadAsync<TeamSearchResults>(response);
        ForgejoTeam? match = results?.Data?
            .FirstOrDefault(team => string.Equals(team.Name, teamName, StringComparison.OrdinalIgnoreCase));

        return match is null ? new NotFound() : match;
    }

    public async ValueTask<OneOf<Success<long>, ForgejoError>> EnsureTeamAsync(string org, string teamName,
                                                                               CollaboratorPermission permission)
    {
        OneOf<ForgejoTeam, NotFound, ForgejoError> existing = await GetTeamAsync(org, teamName);

        return await existing.Match<ValueTask<OneOf<Success<long>, ForgejoError>>>(
            team => ValueTask.FromResult<OneOf<Success<long>, ForgejoError>>(new Success<long>(team.Id)),
            notFound => CreateTeamAsync(org, teamName, permission),
            error => ValueTask.FromResult<OneOf<Success<long>, ForgejoError>>(error));
    }

    private async ValueTask<OneOf<Success<long>, ForgejoError>> CreateTeamAsync(
        string org, string teamName, CollaboratorPermission permission)
    {
        var body = new CreateTeamBody(teamName, permission.ToForgejoString(),
                                      ["repo.code", "repo.issues", "repo.pulls", "repo.releases", "repo.wiki"]);
        HttpResponseMessage response = await http.PostAsJsonAsync($"api/v1/orgs/{Escape(org)}/teams", body);
        if (response.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict)
        {
            // created between the search above and this call - look it up rather than failing the caller
            OneOf<ForgejoTeam, NotFound, ForgejoError> raced = await GetTeamAsync(org, teamName);

            return raced.Match<OneOf<Success<long>, ForgejoError>>(
                team => new Success<long>(team.Id),
                notFound => new ForgejoError((int) response.StatusCode,
                                             $"team '{teamName}' could neither be created nor found"),
                error => error);
        }
        if (!response.IsSuccessStatusCode)
        {
            return await ErrorAsync(response, "create team");
        }

        ForgejoTeam created = (await ReadAsync<ForgejoTeam>(response))!;

        return new Success<long>(created.Id);
    }

    public async ValueTask<OneOf<bool, ForgejoError>> IsTeamMemberAsync(long teamId, string username)
    {
        HttpResponseMessage response = await http.GetAsync($"api/v1/teams/{teamId}/members/{Escape(username)}");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        return response.IsSuccessStatusCode ? true : await ErrorAsync(response, "check team membership");
    }

    public async ValueTask<OneOf<IReadOnlyCollection<string>, ForgejoError>> ListTeamMembersAsync(long teamId)
    {
        HttpResponseMessage response = await http.GetAsync($"api/v1/teams/{teamId}/members");
        if (!response.IsSuccessStatusCode)
        {
            return await ErrorAsync(response, "list team members");
        }

        ForgejoUser[] members = (await ReadAsync<ForgejoUser[]>(response)) ?? [];

        return members.Select(m => m.Login).ToList().AsReadOnly();
    }

    public async ValueTask<OneOf<Success, ForgejoError>> AddTeamMemberAsync(long teamId, string username)
    {
        HttpResponseMessage response = await http.PutAsync($"api/v1/teams/{teamId}/members/{Escape(username)}", null);

        return response.IsSuccessStatusCode ? new Success() : await ErrorAsync(response, "add team member");
    }

    public async ValueTask<OneOf<Success, ForgejoError>> RemoveTeamMemberAsync(long teamId, string username)
    {
        HttpResponseMessage response = await http.DeleteAsync($"api/v1/teams/{teamId}/members/{Escape(username)}");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new Success();
        }

        return response.IsSuccessStatusCode ? new Success() : await ErrorAsync(response, "remove team member");
    }

    public async ValueTask<OneOf<ForgejoUser, NotFound, ForgejoError>> GetUserAsync(string username)
    {
        HttpResponseMessage response = await http.GetAsync($"api/v1/users/{Escape(username)}");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new NotFound();
        }
        if (!response.IsSuccessStatusCode)
        {
            return await ErrorAsync(response, "get user");
        }

        return (await ReadAsync<ForgejoUser>(response))!;
    }

    public async ValueTask<OneOf<ForgejoVersion, ForgejoError>> GetVersionAsync()
    {
        HttpResponseMessage response = await http.GetAsync("api/v1/version");

        return response.IsSuccessStatusCode
            ? (await ReadAsync<ForgejoVersion>(response))!
            : await ErrorAsync(response, "get version");
    }

    public async ValueTask<OneOf<ForgejoCurrentUser, ForgejoError>> GetAuthenticatedUserAsync()
    {
        HttpResponseMessage response = await http.GetAsync("api/v1/user");

        return response.IsSuccessStatusCode
            ? (await ReadAsync<ForgejoCurrentUser>(response))!
            : await ErrorAsync(response, "get authenticated user");
    }

    public async ValueTask<OneOf<Success, ForgejoError>> CheckAdminAccessAsync()
    {
        HttpResponseMessage response = await http.GetAsync("api/v1/admin/users?limit=1");

        return response.IsSuccessStatusCode ? new Success() : await ErrorAsync(response, "list users as admin");
    }

    public async ValueTask<OneOf<ForgejoRepo, NotFound, ForgejoError>> GetRepoAsync(string owner, string repo)
    {
        HttpResponseMessage response = await http.GetAsync($"api/v1/repos/{Escape(owner)}/{Escape(repo)}");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new NotFound();
        }
        if (!response.IsSuccessStatusCode)
        {
            return await ErrorAsync(response, "get repo");
        }

        return (await ReadAsync<ForgejoRepo>(response))!;
    }

    public async ValueTask<OneOf<IReadOnlyCollection<ForgejoCommit>, NotFound, ForgejoError>> GetAllCommitsAsync(
        string owner, string repo)
    {
        var commits = new List<ForgejoCommit>();
        const int pageSize = 50;

        for (int page = 1; ; page++)
        {
            HttpResponseMessage response = await http.GetAsync(
                $"api/v1/repos/{Escape(owner)}/{Escape(repo)}/commits?page={page}&limit={pageSize}");
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new NotFound();
            }
            if (!response.IsSuccessStatusCode)
            {
                return await ErrorAsync(response, "list commits");
            }

            ForgejoCommit[] pageCommits = (await ReadAsync<ForgejoCommit[]>(response)) ?? [];
            commits.AddRange(pageCommits);
            if (pageCommits.Length < pageSize)
            {
                return commits.AsReadOnly();
            }
        }
    }

    public async ValueTask<OneOf<Success<ForgejoRepo>, AlreadyExists, ForgejoError>> CreateOrgRepoAsync(
        string org, string name, bool autoInit, bool isPrivate)
    {
        var body = new CreateRepoBody(name, isPrivate, autoInit);
        HttpResponseMessage response = await http.PostAsJsonAsync($"api/v1/orgs/{Escape(org)}/repos", body);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return new AlreadyExists();
        }
        if (!response.IsSuccessStatusCode)
        {
            return await ErrorAsync(response, "create repo");
        }

        return new Success<ForgejoRepo>((await ReadAsync<ForgejoRepo>(response))!);
    }

    /// <summary>
    ///     Removes an organisation and the teams inside it
    /// </summary>
    /// <remarks>
    ///     Forgejo refuses this while the organisation still owns repositories, so its repositories have to be
    ///     deleted first. An organisation that is already gone counts as success, so a retried cascade converges.
    /// </remarks>
    public async ValueTask<OneOf<Success, ForgejoError>> DeleteOrgAsync(string org)
    {
        HttpResponseMessage response = await http.DeleteAsync($"api/v1/orgs/{Escape(org)}");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new Success();
        }

        return response.IsSuccessStatusCode ? new Success() : await ErrorAsync(response, "delete org");
    }

    public async ValueTask<OneOf<Success, ForgejoError>> DeleteRepoAsync(string owner, string repo)
    {
        HttpResponseMessage response = await http.DeleteAsync($"api/v1/repos/{Escape(owner)}/{Escape(repo)}");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new Success();
        }

        return response.IsSuccessStatusCode ? new Success() : await ErrorAsync(response, "delete repo");
    }

    public async ValueTask<OneOf<Success<ForgejoRepo>, ForgejoError>> ForkRepoAsync(
        string sourceOwner, string sourceRepo, string targetOrg, string newName)
    {
        var body = new ForkRepoBody(targetOrg, newName);
        HttpResponseMessage response = await http.PostAsJsonAsync(
            $"api/v1/repos/{Escape(sourceOwner)}/{Escape(sourceRepo)}/forks", body);
        if (!response.IsSuccessStatusCode)
        {
            return await ErrorAsync(response, "fork repo");
        }

        return new Success<ForgejoRepo>((await ReadAsync<ForgejoRepo>(response))!);
    }

    public async ValueTask<OneOf<Success<ForgejoRepo>, ForgejoError>> MigrateRepoAsync(
        string cloneUrl, string targetOrg, string newName, bool isPrivate)
    {
        var body = new MigrateRepoBody(cloneUrl, targetOrg, newName, false, isPrivate);
        HttpResponseMessage response = await http.PostAsJsonAsync("api/v1/repos/migrate", body);
        if (!response.IsSuccessStatusCode)
        {
            return await ErrorAsync(response, "migrate repo");
        }

        return new Success<ForgejoRepo>((await ReadAsync<ForgejoRepo>(response))!);
    }

    public async ValueTask<OneOf<Success, ForgejoError>> CreateFileAsync(
        string owner, string repo, string path, string content, string message)
    {
        string encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(content));
        var body = new CreateFileBody(encoded, message);
        HttpResponseMessage response = await http.PostAsJsonAsync(
            $"api/v1/repos/{Escape(owner)}/{Escape(repo)}/contents/{path}", body);

        return response.IsSuccessStatusCode ? new Success() : await ErrorAsync(response, "create file");
    }

    public async ValueTask<OneOf<CollaboratorPermission, NotFound, ForgejoError>> GetCollaboratorPermissionAsync(
        string owner, string repo, string username)
    {
        HttpResponseMessage response = await http.GetAsync(
            $"api/v1/repos/{Escape(owner)}/{Escape(repo)}/collaborators/{Escape(username)}/permission");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new NotFound();
        }
        if (!response.IsSuccessStatusCode)
        {
            return await ErrorAsync(response, "get collaborator permission");
        }

        PermissionResult result = (await ReadAsync<PermissionResult>(response))!;

        return CollaboratorPermissionExtensions.ParsePermission(result.Permission);
    }

    public async ValueTask<OneOf<Success, ForgejoError>> SetCollaboratorAsync(
        string owner, string repo, string username, CollaboratorPermission permission)
    {
        var body = new CollaboratorBody(permission.ToForgejoString());
        HttpResponseMessage response = await http.PutAsJsonAsync(
            $"api/v1/repos/{Escape(owner)}/{Escape(repo)}/collaborators/{Escape(username)}", body);

        return response.IsSuccessStatusCode ? new Success() : await ErrorAsync(response, "set collaborator");
    }

    public async ValueTask<OneOf<Success, ForgejoError>> RemoveCollaboratorAsync(
        string owner, string repo, string username)
    {
        HttpResponseMessage response = await http.DeleteAsync(
            $"api/v1/repos/{Escape(owner)}/{Escape(repo)}/collaborators/{Escape(username)}");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new Success();
        }

        return response.IsSuccessStatusCode ? new Success() : await ErrorAsync(response, "remove collaborator");
    }

    public async ValueTask<OneOf<Success, ForgejoError>> CreateBranchAsync(
        string owner, string repo, string newBranch, string oldRefName)
    {
        var body = new CreateBranchBody(newBranch, oldRefName);
        HttpResponseMessage response = await http.PostAsJsonAsync(
            $"api/v1/repos/{Escape(owner)}/{Escape(repo)}/branches", body);
        if (response.IsSuccessStatusCode)
        {
            return new Success();
        }

        // "the branch is already there" and "the ref you branched from does not exist" arrive as the same
        // two statuses. Only the first is success, and the difference is visible by asking.
        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
        {
            OneOf<bool, ForgejoError> exists = await BranchExistsAsync(owner, repo, newBranch);

            return await exists.Match<ValueTask<OneOf<Success, ForgejoError>>>(
                async present =>
                {
                    if (present)
                    {
                        return new Success();
                    }

                    return await ErrorAsync(response, "create branch");
                },
                error => ValueTask.FromResult<OneOf<Success, ForgejoError>>(error));
        }

        return await ErrorAsync(response, "create branch");
    }

    private async ValueTask<OneOf<bool, ForgejoError>> BranchExistsAsync(string owner, string repo, string branch)
    {
        HttpResponseMessage response = await http.GetAsync(
            $"api/v1/repos/{Escape(owner)}/{Escape(repo)}/branches/{Escape(branch)}");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        return response.IsSuccessStatusCode ? true : await ErrorAsync(response, "get branch");
    }

    public async ValueTask<OneOf<ForgejoPullRequest, NotFound, ForgejoError>> FindOpenPullRequestAsync(
        string owner, string repo, string head, string baseBranch)
    {
        HttpResponseMessage response = await http.GetAsync(
            $"api/v1/repos/{Escape(owner)}/{Escape(repo)}/pulls?state=open");
        if (!response.IsSuccessStatusCode)
        {
            return await ErrorAsync(response, "list pull requests");
        }

        ForgejoPullRequest[] pulls = (await ReadAsync<ForgejoPullRequest[]>(response)) ?? [];
        ForgejoPullRequest? match = pulls.FirstOrDefault(
            p => string.Equals(p.Head.Ref, head, StringComparison.Ordinal)
                 && string.Equals(p.Base.Ref, baseBranch, StringComparison.Ordinal));

        return match is null ? new NotFound() : match;
    }

    public async ValueTask<OneOf<Success<ForgejoPullRequest>, AlreadyExists, ForgejoError>> CreatePullRequestAsync(
        string owner, string repo, string head, string baseBranch, string title, string body)
    {
        var request = new CreatePullRequestBody(head, baseBranch, title, body);
        HttpResponseMessage response = await http.PostAsJsonAsync(
            $"api/v1/repos/{Escape(owner)}/{Escape(repo)}/pulls", request);
        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
        {
            return new AlreadyExists();
        }
        if (!response.IsSuccessStatusCode)
        {
            return await ErrorAsync(response, "create pull request");
        }

        return new Success<ForgejoPullRequest>((await ReadAsync<ForgejoPullRequest>(response))!);
    }

    public async ValueTask<OneOf<Success, ForgejoError>> EnsureOrgWebhookAsync(string org, string url, string secret)
    {
        var body = new CreateWebhookBody("forgejo", true, ["push", "pull_request_review"],
                                         new WebhookConfig(url, "json", secret));
        HttpResponseMessage response = await http.PostAsJsonAsync($"api/v1/orgs/{Escape(org)}/hooks", body);
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            return new Success();
        }

        return response.IsSuccessStatusCode ? new Success() : await ErrorAsync(response, "ensure webhook");
    }

    public async ValueTask<OneOf<Success, ForgejoError>> EnsureUserAsync(string studentId, string? email,
                                                                         long authSourceId)
    {
        // a local account needs some password to be created at all; it is random and immediately forgotten,
        // because the student's usable one is issued on demand by IGitCredentialService
        string? initialPassword = authSourceId == 0 ? GitCredentialGenerator.Generate() : null;

        var body = new CreateUserBody(studentId, email ?? $"{studentId}@users.noreply.local", studentId, authSourceId,
                                      MustChangePassword: false, initialPassword);
        HttpResponseMessage response = await http.PostAsJsonAsync("api/v1/admin/users", body);
        if (response.IsSuccessStatusCode)
        {
            return new Success();
        }

        // 422 and 409 are the normal answer on every call after the first - the account is already there.
        // They are also what Forgejo returns when the request is genuinely rejected, and a **duplicate
        // e-mail** is the one that happens in practice: addresses are unique across Forgejo, so a user whose
        // address already belongs to another account (an operator account created with a real person's
        // e-mail, say) can never be created. The status alone cannot tell the two apart, so ask.
        //
        // Reporting the rejection as success is worse than it sounds: the caller goes on to PATCH a user
        // that does not exist, gets a 404, and the failure surfaces far away from its cause.
        if (response.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict)
        {
            OneOf<ForgejoUser, NotFound, ForgejoError> existing = await GetUserAsync(studentId);

            return await existing.Match<ValueTask<OneOf<Success, ForgejoError>>>(
                found => ValueTask.FromResult<OneOf<Success, ForgejoError>>(new Success()),
                async notFound => await ErrorAsync(response, "ensure user"),
                error => ValueTask.FromResult<OneOf<Success, ForgejoError>>(error));
        }

        return await ErrorAsync(response, "ensure user");
    }

    public async ValueTask<OneOf<Success, NotFound, ForgejoError>> SetUserPasswordAsync(
        string studentId, string password)
    {
        var body = new SetUserPasswordBody(studentId, SourceId: 0, password, MustChangePassword: false);
        HttpResponseMessage response = await http.PatchAsJsonAsync($"api/v1/admin/users/{Escape(studentId)}", body);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new NotFound();
        }

        return response.IsSuccessStatusCode ? new Success() : await ErrorAsync(response, "set user password");
    }

    public async ValueTask<OneOf<Success, ForgejoError>> SetUserActiveAsync(string studentId, bool active,
                                                                            long authSourceId)
    {
        var body = new EditUserBody(studentId, authSourceId, active, ProhibitLogin: !active);
        HttpResponseMessage response = await http.PatchAsJsonAsync($"api/v1/admin/users/{Escape(studentId)}", body);

        return response.IsSuccessStatusCode ? new Success() : await ErrorAsync(response, "set user active");
    }

    private static string Escape(string segment) => Uri.EscapeDataString(segment);

    private static async ValueTask<T?> ReadAsync<T>(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<T>();

    /// <summary>
    ///     Turns a failed Forgejo response into an error, keeping what Forgejo said about it
    /// </summary>
    /// <remarks>
    ///     The status alone is rarely enough to act on - "422" could be a duplicate e-mail, a rejected
    ///     password or a name that is already taken, and Forgejo says which in the body. It is logged in full
    ///     and a trimmed form travels back to the caller, which is what the 502's problem detail shows.
    /// </remarks>
    private async ValueTask<ForgejoError> ErrorAsync(HttpResponseMessage response, string operation)
    {
        int status = (int) response.StatusCode;
        string reason = response.ReasonPhrase ?? response.StatusCode.ToString();

        string body = string.Empty;
        try
        {
            body = (await response.Content.ReadAsStringAsync()).Trim();
        }
        catch (HttpRequestException)
        {
            // the status is the useful part; a body we cannot read must not mask it
        }

        logger.LogWarning("Forgejo {Operation} failed with status {Status} ({Reason}): {Body}",
                          operation, status, reason, body.Length == 0 ? "<no body>" : body);

        string detail = ExtractMessage(body) ?? reason;

        return new ForgejoError(status, $"{operation}: {detail}");
    }

    /// <summary>
    ///     Pulls the <c>message</c> out of Forgejo's error body, capped so a stack trace cannot travel on
    /// </summary>
    private static string? ExtractMessage(string body)
    {
        if (body.Length == 0)
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("message", out JsonElement message)
                && message.GetString() is { Length: > 0 } text)
            {
                return text.Length <= MaxDetailLength ? text : text[..MaxDetailLength];
            }
        }
        catch (JsonException)
        {
            // not JSON; the status and the logged body are enough
        }

        return null;
    }

    private const int MaxDetailLength = 200;
}
