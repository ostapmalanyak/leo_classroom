using System.Net.Http.Json;
using System.Text.Json;
using LeoClassroom.Services.Util;
using Microsoft.Extensions.Options;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Services.Moodle;

public readonly record struct MoodleError(string Reason);

public readonly record struct MoodleTarget(string BaseUrl, string Token, long MoodleCourseId);

public sealed record MoodleItemPayload(string Title, Instant? Deadline, string AcceptLink);

public interface IMoodleClient
{
    public ValueTask<OneOf<Success<long>, MoodleError>> CreateItemAsync(MoodleTarget target, MoodleItemPayload payload);
    public ValueTask<OneOf<Success, MoodleError>> UpdateItemAsync(
        MoodleTarget target, long itemId, MoodleItemPayload payload);
    public ValueTask<OneOf<Success, MoodleError>> DeleteItemAsync(MoodleTarget target, long itemId);
}

internal sealed class MoodleClient(
    IHttpClientFactory httpClientFactory,
    IOptions<MoodleSettings> options,
    ILogger<MoodleClient> logger) : IMoodleClient
{
    private const string ClientName = "moodle";

    public async ValueTask<OneOf<Success<long>, MoodleError>> CreateItemAsync(
        MoodleTarget target, MoodleItemPayload payload)
    {
        OneOf<JsonElement, MoodleError> result = await CallAsync(target, options.Value.CreateFunction, Fields(target, payload));

        return result.Match<OneOf<Success<long>, MoodleError>>(
            body => TryReadItemId(body, out long itemId)
                ? new Success<long>(itemId)
                : new MoodleError("Moodle create response did not contain an item id"),
            error => error);
    }

    public async ValueTask<OneOf<Success, MoodleError>> UpdateItemAsync(
        MoodleTarget target, long itemId, MoodleItemPayload payload)
    {
        List<KeyValuePair<string, string>> fields = Fields(target, payload);
        fields.Add(new("itemid", itemId.ToString()));
        OneOf<JsonElement, MoodleError> result = await CallAsync(target, options.Value.UpdateFunction, fields);

        return result.Match<OneOf<Success, MoodleError>>(_ => new Success(), error => error);
    }

    public async ValueTask<OneOf<Success, MoodleError>> DeleteItemAsync(MoodleTarget target, long itemId)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("courseid", target.MoodleCourseId.ToString()),
            new("itemid", itemId.ToString())
        };
        OneOf<JsonElement, MoodleError> result = await CallAsync(target, options.Value.DeleteFunction, fields);

        return result.Match<OneOf<Success, MoodleError>>(_ => new Success(), error => error);
    }

    private static List<KeyValuePair<string, string>> Fields(MoodleTarget target, MoodleItemPayload payload)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("courseid", target.MoodleCourseId.ToString()),
            new("title", payload.Title),
            new("acceptlink", payload.AcceptLink)
        };
        if (payload.Deadline is not null)
        {
            fields.Add(new("deadline", payload.Deadline.Value.ToUnixTimeSeconds().ToString()));
        }

        return fields;
    }

    private async ValueTask<OneOf<JsonElement, MoodleError>> CallAsync(
        MoodleTarget target, string function, List<KeyValuePair<string, string>> fields)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("wstoken", target.Token),
            new("wsfunction", function),
            new("moodlewsrestformat", "json")
        };
        form.AddRange(fields);

        HttpClient http = httpClientFactory.CreateClient(ClientName);
        string url = $"{target.BaseUrl.TrimEnd('/')}/webservice/rest/server.php";

        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync(url, new FormUrlEncodedContent(form));
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Moodle {Function} request failed: {Reason}", function, ex.Message);

            return new MoodleError(ex.Message);
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Moodle {Function} returned status {Status}", function, (int)response.StatusCode);

            return new MoodleError($"Moodle returned status {(int)response.StatusCode}");
        }

        JsonElement root;
        try
        {
            root = await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        catch (JsonException)
        {
            // Some Moodle functions return null/empty on success.
            return default(JsonElement);
        }

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("exception", out JsonElement exception))
        {
            string message = root.TryGetProperty("message", out JsonElement msg) ? msg.GetString() ?? "" : "";
            logger.LogWarning("Moodle {Function} returned an exception: {Exception}", function, exception.GetString());

            return new MoodleError(string.IsNullOrEmpty(message) ? "Moodle web-service error" : message);
        }

        return root;
    }

    private static bool TryReadItemId(JsonElement root, out long itemId)
    {
        itemId = 0;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("itemid", out JsonElement value))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out itemId))
            {
                return true;
            }
            if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out itemId))
            {
                return true;
            }
        }

        return false;
    }
}
