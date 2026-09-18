using System.Text.Json;

namespace LeoClassroom.Services.Forgejo;

public readonly record struct PushDetails(int CommitCount, string? HeadSha);

public static class ForgejoPushPayload
{
    public static PushDetails Parse(string payload)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;

            int commitCount = 0;
            if (root.TryGetProperty("commits", out JsonElement commits) && commits.ValueKind == JsonValueKind.Array)
            {
                commitCount = commits.GetArrayLength();
            }
            else if (root.TryGetProperty("total_commits", out JsonElement total)
                     && total.TryGetInt32(out int totalCommits))
            {
                commitCount = totalCommits;
            }

            string? headSha = root.TryGetProperty("after", out JsonElement after) ? after.GetString() : null;

            return new PushDetails(commitCount, headSha);
        }
        catch (JsonException)
        {
            return new PushDetails(0, null);
        }
    }
}
