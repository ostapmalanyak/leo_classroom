using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace LeoClassroom.Cli;

public sealed record BackendError(string Reason);

// Typed client over the existing backend contracts. Carries the teacher's bearer token; introduces no
// privileged path — every call is authorized by the backend exactly as the same web-app action.
public sealed class BackendClient(HttpClient http, CliConfig config)
{
    public void UseToken(string accessToken) =>
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    public async Task<MeDto?> GetCurrentUserAsync(string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url("/api/me"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync(CliJsonContext.Default.MeDto, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return null;
        }
    }

    public async Task<string?> GetApiVersionAsync(CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(Url("/api/version"), ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            ApiVersionDto? dto = await response.Content.ReadFromJsonAsync(CliJsonContext.Default.ApiVersionDto, ct);

            return dto?.ApiVersion;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<CourseOverviewDto[]?> GetCoursesAsync(CancellationToken ct)
    {
        using var response = await http.GetAsync(Url("/api/courses"), ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync(
            CliJsonContext.Default.CourseOverviewDtoArray, ct);
    }

    public async Task<(AssignmentDto? Created, BackendError? Error)> CreateAssignmentAsync(
        AssignmentCreateRequestDto request, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(
            Url("/api/assignments"), request, CliJsonContext.Default.AssignmentCreateRequestDto, ct);
        if (response.StatusCode == HttpStatusCode.Created)
        {
            AssignmentDto? dto = await response.Content.ReadFromJsonAsync(CliJsonContext.Default.AssignmentDto, ct);

            return (dto, null);
        }

        return (null, new BackendError($"assignment creation failed ({(int)response.StatusCode})"));
    }

    public async Task<AcceptLinkDto?> GetAcceptLinkAsync(long assignmentId, CancellationToken ct)
    {
        using var response = await http.GetAsync(Url($"/api/assignments/{assignmentId}/accept-link"), ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync(CliJsonContext.Default.AcceptLinkDto, ct);
    }

    private string Url(string path) => $"{config.BackendBaseUrl.TrimEnd('/')}{path}";
}
