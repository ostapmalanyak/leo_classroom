using System.Net.Http.Json;

namespace LeoClassroom.Cli;

public sealed record AuthResult(bool Ok, string? AccessToken, string? Username, string? Message);

// OAuth 2.0 device authorization grant against Keycloak (public client, no embedded secret) plus a token
// cache with silent refresh. Tokens are never written to logs.
public sealed class DeviceAuth(HttpClient http, CliConfig config, TokenCache cache)
{
    public async Task<AuthResult> AuthenticateAsync(CancellationToken ct)
    {
        CachedTokens? cached = cache.Read();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (cached is not null && TokenCache.IsAccessValid(cached, now))
        {
            return await GateAsync(cached.AccessToken, ct);
        }
        if (cached is not null)
        {
            AuthResult refreshed = await TryRefreshAsync(cached.RefreshToken, ct);
            if (refreshed.Ok)
            {
                return refreshed;
            }
        }

        return await RunDeviceFlowAsync(ct);
    }

    private async Task<AuthResult> RunDeviceFlowAsync(CancellationToken ct)
    {
        using var deviceResponse = await http.PostAsync(config.DeviceEndpoint, Form(new()
        {
            ["client_id"] = config.ClientId,
            ["scope"] = "openid profile email"
        }), ct);
        if (!deviceResponse.IsSuccessStatusCode)
        {
            return new AuthResult(false, null, null, "Could not start the device login flow.");
        }

        DeviceCodeResponse device = (await deviceResponse.Content
            .ReadFromJsonAsync(CliJsonContext.Default.DeviceCodeResponse, ct))!;

        Console.WriteLine();
        Console.WriteLine("To sign in, open this URL in a browser and enter the code:");
        Console.WriteLine($"  URL:  {device.VerificationUriComplete ?? device.VerificationUri}");
        Console.WriteLine($"  Code: {device.UserCode}");
        Console.WriteLine("Waiting for approval...");

        int interval = Math.Max(device.Interval, 1);
        long deadline = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + device.ExpiresIn;
        while (DateTimeOffset.UtcNow.ToUnixTimeSeconds() < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), ct);
            TokenResponse token = await PostTokenAsync(new()
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["client_id"] = config.ClientId,
                ["device_code"] = device.DeviceCode
            }, ct);

            if (token.AccessToken is not null)
            {
                return await PersistAsync(token, ct);
            }
            if (token.Error == "slow_down")
            {
                interval += 5;
                continue;
            }
            if (token.Error == "authorization_pending")
            {
                continue;
            }

            return new AuthResult(false, null, null, $"Login failed: {token.ErrorDescription ?? token.Error}");
        }

        return new AuthResult(false, null, null, "Login timed out before approval.");
    }

    private async Task<AuthResult> TryRefreshAsync(string refreshToken, CancellationToken ct)
    {
        TokenResponse token = await PostTokenAsync(new()
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = config.ClientId,
            ["refresh_token"] = refreshToken
        }, ct);

        return token.AccessToken is not null
            ? await PersistAsync(token, ct)
            : new AuthResult(false, null, null, "refresh failed");
    }

    private async Task<AuthResult> PersistAsync(TokenResponse token, CancellationToken ct)
    {
        if (token.AccessToken is not { Length: > 0 } accessToken)
        {
            return new AuthResult(false, null, null, "Login returned no access token.");
        }

        AuthResult result = await GateAsync(accessToken, ct);
        if (!result.Ok)
        {
            return result;
        }
        long expiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + token.ExpiresIn;
        cache.Write(new CachedTokens(accessToken, token.RefreshToken ?? string.Empty, expiresAt));

        return result;
    }

    // The realm has no roles claim and administrators are configured only on the backend.
    // Ask the same authoritative endpoint as the SPA, which also rejects retired accounts.
    private async Task<AuthResult> GateAsync(string accessToken, CancellationToken ct)
    {
        MeDto? me = await new BackendClient(http, config).GetCurrentUserAsync(accessToken, ct);
        if (me is null)
        {
            return new AuthResult(false, null, null, "The backend could not verify your account. Sign in again.");
        }
        if (string.IsNullOrWhiteSpace(me.StudentId))
        {
            return new AuthResult(false, null, null, "The backend returned no username for your account.");
        }
        if (me.Roles is not null && (me.Roles.Contains("Teacher") || me.Roles.Contains("Admin")))
        {
            return new AuthResult(true, accessToken, me.StudentId, null);
        }

        return new AuthResult(false, null, null, "A teacher account is required to use this tool.");
    }

    private async Task<TokenResponse> PostTokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var response = await http.PostAsync(config.TokenEndpoint, Form(form), ct);

        return (await response.Content.ReadFromJsonAsync(CliJsonContext.Default.TokenResponse, ct))!;
    }

    private static FormUrlEncodedContent Form(Dictionary<string, string> values) => new(values);
}
