using LeoClassroom.Services.Forgejo;
using LeoClassroom.Services.Util;
using Microsoft.Extensions.Options;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Services;

/// <param name="Name">Stable identifier for the check, so the UI can label it</param>
/// <param name="Passed">Whether this particular check succeeded</param>
/// <param name="Detail">What happened, in the words of whoever answered - Forgejo's own message on failure</param>
public sealed record ForgejoCheck(string Name, bool Passed, string Detail);

/// <param name="Ok">True only when every check passed</param>
public sealed record ForgejoHealthReport(bool Ok, string BaseUrl, IReadOnlyList<ForgejoCheck> Checks);

/// <summary>
///     Answers "can this application still drive Forgejo?" in one call
/// </summary>
/// <remarks>
///     The checks run in order of dependence, each one narrower than the last, so the first failure names the
///     layer that broke: the network, then the token, then the account's standing, then the admin scope. The
///     motivating case is real - a bot account carrying Forgejo's must-change-password flag answers 403 to
///     every administrative call while remaining perfectly reachable and perfectly authenticated, and
///     nothing short of an administrative request reveals it.
/// </remarks>
public interface IForgejoDiagnosticsService
{
    public ValueTask<ForgejoHealthReport> RunAsync();
}

internal sealed class ForgejoDiagnosticsService(
    IForgejoClient forgejo,
    IOptions<ForgejoSettings> settings,
    ILogger<ForgejoDiagnosticsService> logger) : IForgejoDiagnosticsService
{
    public async ValueTask<ForgejoHealthReport> RunAsync()
    {
        List<ForgejoCheck> checks = [];

        OneOf<ForgejoVersion, ForgejoError> version = await forgejo.GetVersionAsync();
        checks.Add(version.Match(
                       ok => new ForgejoCheck("reachable", true, $"Forgejo {ok.Version} answered"),
                       error => new ForgejoCheck("reachable", false, error.Reason)));

        if (version.Failure is not null)
        {
            // nothing below can succeed, and each would just repeat the same connection error
            return Report(checks);
        }

        OneOf<ForgejoCurrentUser, ForgejoError> identity = await forgejo.GetAuthenticatedUserAsync();

        // Run the administrative call before reporting on the account's standing. `is_admin` is a claim in a
        // response; an administrative call succeeding is evidence. When they disagree the evidence wins -
        // otherwise the report contradicts itself, which is what it did while `is_admin` was not being read
        // off the wire at all: a working deployment showed a red "not a site administrator" row directly
        // above a green administrative call.
        OneOf<Success, ForgejoError> admin = await forgejo.CheckAdminAccessAsync();
        bool adminApiWorks = admin.Match(success => true, error => false);

        checks.Add(identity.Match(
                       ok => new ForgejoCheck("token accepted", true, $"authenticated as {ok.Login}"),
                       error => new ForgejoCheck("token accepted", false, error.Reason)));

        identity.Switch(
            user =>
            {
                checks.Add(user.Login == settings.Value.BotName
                               ? new ForgejoCheck("expected account", true, $"the token belongs to {user.Login}")
                               : new ForgejoCheck("expected account", false,
                                                  $"the token belongs to {user.Login}, but Forgejo:BotName is "
                                                  + $"{settings.Value.BotName}"));

                checks.Add((user.IsAdmin, adminApiWorks) switch
                {
                    (true, _) => new ForgejoCheck("site administrator", true,
                                                  $"{user.Login} is a site administrator"),
                    (false, true) => new ForgejoCheck("site administrator", true,
                                                      $"Forgejo did not report {user.Login} as a site administrator, "
                                                      + "but administrative calls succeed - taking the call as the "
                                                      + "answer"),
                    (false, false) => new ForgejoCheck("site administrator", false,
                                                       $"{user.Login} is not a site administrator, so it cannot "
                                                       + "create users, organisations or repositories")
                });
            },
            // the failure is already reported by the "token accepted" check above
            error => { });

        checks.Add(admin.Match(
                       _ => new ForgejoCheck("administrative API", true, "an administrative call succeeded"),
                       error => new ForgejoCheck("administrative API", false, error.Reason)));

        return Report(checks);
    }

    private ForgejoHealthReport Report(List<ForgejoCheck> checks)
    {
        bool ok = checks.TrueForAll(check => check.Passed);
        if (!ok)
        {
            logger.LogWarning("Forgejo health check failed: {Failures}",
                              string.Join("; ", checks.Where(c => !c.Passed).Select(c => $"{c.Name}: {c.Detail}")));
        }

        return new ForgejoHealthReport(ok, settings.Value.BaseUrl, checks);
    }
}
