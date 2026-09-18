using System.Text.Json;

namespace LeoClassroom.Cli;

// Caches the access/refresh tokens in the per-user config dir with owner-only permissions. Never logged.
public sealed class TokenCache
{
    private readonly string _path;

    public TokenCache(string path) => _path = path;

    public static string DefaultPath()
    {
        string baseDir = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
                         ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

        return Path.Combine(baseDir, "leo", "token.json");
    }

    public CachedTokens? Read()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            string json = File.ReadAllText(_path);

            return JsonSerializer.Deserialize(json, CliJsonContext.Default.CachedTokens);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    public void Write(CachedTokens tokens)
    {
        string path = Path.GetFullPath(_path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)
                                  ?? throw new InvalidOperationException("The token cache needs a parent directory."));
        string json = JsonSerializer.Serialize(tokens, CliJsonContext.Default.CachedTokens);
        var options = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            // Apply permissions at creation, before any token bytes are written.
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }
        using var stream = new FileStream(path, options);
        if (!OperatingSystem.IsWindows())
        {
            // Also repair permissions on an existing cache before replacing its contents.
            File.SetUnixFileMode(stream.SafeFileHandle, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        stream.SetLength(0);
        using var writer = new StreamWriter(stream);
        writer.Write(json);
    }

    public void Clear()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    // Access token is considered usable while at least this many seconds remain before expiry.
    public static bool IsAccessValid(CachedTokens tokens, long nowUnix, int skewSeconds = 30) =>
        tokens.AccessExpiresAtUnix - skewSeconds > nowUnix;
}
