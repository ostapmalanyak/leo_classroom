using System.Security.Cryptography;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Services.Auth;
using LeoClassroom.Services.Util;
using LeoClassroom.Shared;
using Microsoft.Extensions.Options;

namespace LeoClassroom.Services.GitSetup;

/// <param name="Configured">The user says the setup commands ran on their computer</param>
/// <param name="Authorized">A git operation proved the stored credential works</param>
/// <param name="Token">The opaque part of the setup URL; it only ever marks the user as configured</param>
/// <param name="ClientId">The Forgejo OAuth2 application id the git config names; public by nature</param>
public sealed record GitSetupStatus(bool Configured, bool Authorized, string Token, string Host, string ClientId);

/// <summary>
///     The one-time git login setup of a single user
/// </summary>
/// <remarks>
///     <para>
///         Git has no memory of the Forgejo password, so every clone asks for it again until the credential
///         helper is configured once per computer. This service owns the per-user flags that let the page show
///         whether that happened, and the settings the page's commands are built from.
///     </para>
///     <para>
///         Nothing secret is stored: the token is a random identifier, not an authentication for anything.
///     </para>
/// </remarks>
public interface IGitSetupService
{
    public ValueTask<GitSetupStatus?> GetStatusAsync();

    public ValueTask<bool> MarkConfiguredAsync(string token);

    /// <summary>
    ///     Records that a git operation by this Forgejo user succeeded, so the login is known to work
    /// </summary>
    public ValueTask MarkAuthorizedAsync(string forgejoLogin);
}

internal sealed class GitSetupService(
    IUnitOfWork uow,
    ICurrentUser currentUser,
    IOptions<ForgejoSettings> forgejo,
    IClock clock,
    ILogger<GitSetupService> logger) : IGitSetupService
{
    public async ValueTask<GitSetupStatus?> GetStatusAsync()
    {
        User? user = await uow.UserRepository.GetTrackedByStudentIdAsync(currentUser.StudentId);
        if (user is null)
        {
            return null;
        }

        if (user.GitSetupToken is null)
        {
            user.GitSetupToken = NewToken();
            await uow.SaveChangesAsync();
        }

        var uri = new Uri(forgejo.Value.PublicBaseUrl ?? forgejo.Value.BaseUrl);
        string host = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";

        return new GitSetupStatus(
            user.GitSetupConfiguredAt is not null, user.GitSetupAuthorizedAt is not null, user.GitSetupToken,
            host, forgejo.Value.OAuthClientId);
    }

    public async ValueTask<bool> MarkConfiguredAsync(string token)
    {
        User? user = await uow.UserRepository.GetTrackedByGitSetupTokenAsync(token);
        if (user is null)
        {
            return false;
        }

        // Idempotent: running the script twice is a supported thing to do.
        user.GitSetupConfiguredAt ??= clock.GetCurrentInstant();
        await uow.SaveChangesAsync();

        // Deliberately not logged: Serilog attaches the request path to every entry, and that path is the token.
        return true;
    }

    public async ValueTask MarkAuthorizedAsync(string forgejoLogin)
    {
        User? user = await uow.UserRepository.GetTrackedByStudentIdIgnoreCaseAsync(forgejoLogin);
        if (user is null || user.GitSetupAuthorizedAt is not null)
        {
            return;
        }

        user.GitSetupAuthorizedAt = clock.GetCurrentInstant();
        await uow.SaveChangesAsync();

        logger.LogInformation("Git login confirmed by a push from {StudentId}", user.StudentId);
    }

    /// <summary>
    ///     A 256-bit URL-safe token. Only whoever reads this user's page ever sees it, and it authorises nothing
    ///     beyond marking the setup as done.
    /// </summary>
    private static string NewToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
}
