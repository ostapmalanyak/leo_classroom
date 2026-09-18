using LeoClassroom;
using LeoClassroom.Auth;
using LeoClassroom.Services.Auth;
using LeoClassroom.Shared;
using Microsoft.AspNetCore.Http.Json;

var builder = WebApplication.CreateBuilder(args);

bool isDev = builder.Environment.IsDevelopment();
builder.AddFileMountedSecrets();
builder.VerifyRequiredSecrets();
var configurationManager = builder.Configuration;
var settings = builder.Services.LoadAndConfigureSettings(configurationManager);
var keycloak = builder.Services.LoadKeycloakSettings(configurationManager);

builder.AddLogging();
builder.Services.AddApplicationServices(configurationManager, isDev);
builder.Services.ConfigureForgejoSettings(configurationManager);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddAuthN(keycloak);
builder.Services.AddAuthZ(keycloak);
builder.Services.AddOpenApi();
builder.Services.AddCors(settings);
builder.Services.ConfigureProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o => ConfigureJsonSerialization(o, isDev));
builder.Services.ConfigureAdditionalRouteConstraints();
builder.Services.ConfigureSchemaGuard(configurationManager);
Setup.ConfigureValidation();

var app = builder.Build();

// not using HTTPS, because all production backends are behind a reverse proxy which handles SSL termination

app.UseCors(Setup.CorsPolicyName);
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

// after authorization on purpose: the policies are claim-based and need no user row, so a caller who is about to be
// refused must not cause a provisioning write first
app.UseMiddleware<UserProvisioningMiddleware>();

app.MapApplicationEndpoints();

if (app.Environment.IsDevelopment())
{
    // mapped only in development, and explicitly anonymous: the fallback policy would otherwise close it too
    app.MapOpenApi().AllowAnonymous();
}

await app.RunAsync();

return;

static void ConfigureJsonSerialization(JsonOptions options, bool isDev)
{
    JsonConfig.ConfigureJsonSerialization(options.SerializerOptions, isDev);
}

// used for integration testing
public partial class Program { }
