using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeoClassroom.TestInt.Util;

public sealed class TestAuthState
{
    public const string Scheme = "Test";

    public string? StudentId { get; private set; }
    public string[] Roles { get; private set; } = [];
    public string? Class { get; private set; }

    public void SetUser(string studentId, string[] roles, string? @class = null)
    {
        StudentId = studentId;
        Roles = roles;
        Class = @class;
    }

    public void SetAnonymous()
    {
        StudentId = null;
        Roles = [];
        Class = null;
    }
}

internal sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    TestAuthState state) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (state.StudentId is null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(TestAuthState.Scheme, nameType: "preferred_username",
                                         roleType: ClaimTypes.Role);
        identity.AddClaim(new Claim("preferred_username", state.StudentId));
        identity.AddClaim(new Claim("given_name", "Test"));
        identity.AddClaim(new Claim("family_name", state.StudentId));
        identity.AddClaim(new Claim("email", $"{state.StudentId}@school.at"));
        if (state.Class is not null)
        {
            identity.AddClaim(new Claim("class", state.Class));
        }

        // the school realm carries no roles claim: a caller's group is read from their LDAP distinguished
        // name, and administrators come from configuration. Mirror both so the tests exercise the real path.
        string[] organisationalUnits =
        [
            .. state.Roles.Contains("teacher") ? new[] { "OU=Teachers" } : [],
            .. state.Roles.Contains("student") ? new[] { "OU=Students" } : []
        ];
        if (organisationalUnits.Length > 0)
        {
            identity.AddClaim(new Claim("ldap_entry_dn",
                                        $"CN={state.StudentId},{string.Join(',', organisationalUnits)},DC=htl,DC=local"));
        }

        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, TestAuthState.Scheme);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
