using LeoClassroom.Services.Util;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace LeoClassroom.Auth;

public static class AuthPolicies
{
    public const string RequireAdmin = "RequireAdmin";
    public const string RequireTeacher = "RequireTeacher";
    public const string RequireStudent = "RequireStudent";

    public const string AdminRole = "admin";
    public const string TeacherRole = "teacher";
    public const string StudentRole = "student";
}

public static class AuthSetup
{
    extension(IServiceCollection services)
    {
        public KeycloakSettings LoadKeycloakSettings(IConfigurationManager configurationManager)
        {
            var section = configurationManager.GetSection(KeycloakSettings.SectionKey);
            services.Configure<KeycloakSettings>(s => section.Bind(s));

            var settings = Activator.CreateInstance<KeycloakSettings>();
            section.Bind(settings);

            return settings;
        }

        public void AddAuthN(KeycloakSettings keycloak)
        {
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                    .AddJwtBearer(options =>
                    {
                        options.Authority = keycloak.Authority;
                        options.Audience = keycloak.Audience;
                        options.RequireHttpsMetadata = keycloak.RequireHttpsMetadata;

                        // keep the JWT's own claim names: with inbound mapping on, Keycloak's `name` claim is
                        // rewritten to ClaimTypes.Name, which is not the IF number this application identifies
                        // users by
                        options.MapInboundClaims = false;

                        options.TokenValidationParameters.NameClaimType = AuthClaims.StudentIdClaim;
                        options.TokenValidationParameters.ValidateIssuer = true;

                        // the HTL Leonding realm sets no `aud` on its access tokens, so requiring one would
                        // reject every request; issuer, signature and lifetime are checked either way
                        options.TokenValidationParameters.ValidateAudience = keycloak.ValidateAudience;
                        options.TokenValidationParameters.ValidateLifetime = true;
                        options.TokenValidationParameters.ValidateIssuerSigningKey = true;
                        options.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
                    });
        }

        public void AddAuthZ(KeycloakSettings keycloak)
        {
            // this realm carries no roles claim; roles are derived per request from ldap_entry_dn plus the
            // configured administrator list, so the policies assert on the claims this application adds
            HashSet<string> adminUsers = new(keycloak.AdminUsers, StringComparer.Ordinal);
            services.AddSingleton<IClaimsTransformation>(new LeoRoleClaimsTransformation(adminUsers));

            services.AddAuthorizationBuilder()
                    .AddPolicy(AuthPolicies.RequireAdmin, p => p.RequireRole(AuthPolicies.AdminRole))
                    .AddPolicy(AuthPolicies.RequireTeacher,
                               p => p.RequireRole(AuthPolicies.TeacherRole, AuthPolicies.AdminRole))
                    .AddPolicy(AuthPolicies.RequireStudent,
                               p => p.RequireRole(AuthPolicies.StudentRole, AuthPolicies.AdminRole))
                    // an endpoint group that forgets RequireAuthorization is closed rather than public; the few
                    // genuinely public endpoints (health, version, the signed Forgejo webhook) say AllowAnonymous
                    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        }
    }
}
