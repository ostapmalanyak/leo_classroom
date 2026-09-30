using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using CliWrap;
using CliWrap.Buffered;

const string defaultHost = "ldaps://addc01.edu.htl-leonding.ac.at:636";
const string defaultBaseDn = "dc=edu,dc=htl-leonding,dc=ac,dc=at";
const string defaultFilter = "(&(objectCategory=person)(objectClass=user))";

string host = Environment.GetEnvironmentVariable("LDAP_HOST") ?? defaultHost;
string baseDn = Environment.GetEnvironmentVariable("LDAP_BASE_DN") ?? defaultBaseDn;
string filter = Environment.GetEnvironmentVariable("LDAP_FILTER") ?? defaultFilter;
string credentialsPath = FindCredentialsFile();
LdapCredentials credentials = JsonSerializer.Deserialize<LdapCredentials>(
    await File.ReadAllTextAsync(credentialsPath))
    ?? throw new InvalidOperationException($"Could not read LDAP credentials from {credentialsPath}.");
if (string.IsNullOrWhiteSpace(credentials.BindDn) || string.IsNullOrWhiteSpace(credentials.Password))
{
    throw new InvalidOperationException(
        $"Set both BindDn and Password in {credentialsPath} before running the export.");
}

List<LdapUser> users = await LdapDirectory.QueryUsersAsync(
    host, credentials.BindDn, credentials.Password, baseDn, filter);

Console.WriteLine($"Retrieved {users.Count} users.");
foreach (IGrouping<string, LdapUser> group in users
             .GroupBy(user => string.IsNullOrEmpty(user.Role) ? "(none)" : user.Role)
             .OrderByDescending(group => group.Count()))
{
    Console.WriteLine($"  {group.Count(),6}  {group.Key}");
    if (string.Equals(group.Key, "Students", StringComparison.OrdinalIgnoreCase))
    {
        foreach (LdapUser user in group.OrderBy(user => user.Class).ThenBy(user => user.Cn))
        {
            Console.WriteLine(
                $"           {user.Class,-6}  {user.Cn} {user.Mail} {user.GivenName} {user.Sn} {user.Role}");
        }
    }
}

static string FindCredentialsFile()
{
    string[] candidates =
    {
        Path.Combine(Environment.CurrentDirectory, "source", "ldap-export", "credentials.json"),
        Path.Combine(Environment.CurrentDirectory, "credentials.json")
    };

    foreach (string candidate in candidates)
    {
        if (File.Exists(candidate))
        {
            return candidate;
        }
    }

    throw new FileNotFoundException(
        "Create source/ldap-export/credentials.json using credentials.example.json and enter your BindDn and Password.");
}

internal sealed class LdapCredentials
{
    public string BindDn { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}

internal static partial class LdapDirectory
{
    private static readonly string[] attributes =
        ["sAMAccountName", "mail", "displayName", "givenName", "sn", "cn"];

    private static readonly HashSet<string> roles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Students", "Teachers", "Testusers", "Exams", "archivedusers"
    };

    [GeneratedRegex(@"^\d+[A-Z]{2,6}$", RegexOptions.IgnoreCase)]
    private static partial Regex ClassPattern();

    public static async Task<List<LdapUser>> QueryUsersAsync(
        string host, string bindDn, string password, string baseDn, string filter)
    {
        string passwordFile = Path.GetTempFileName();
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(passwordFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            await File.WriteAllTextAsync(passwordFile, password, new UTF8Encoding(false));

            List<string> arguments =
            [
                "-x",
                "-H", host,
                "-D", bindDn,
                "-y", passwordFile,
                "-o", "TLS_REQCERT=never",
                "-b", baseDn,
                "-E", "pr=1000/noprompt",
                filter
            ];
            arguments.AddRange(attributes);

            const string commandName = "ldapsearch";
            var result = await Cli.Wrap(commandName)
                .WithArguments(arguments)
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(Encoding.UTF8);

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"{commandName} exited with code {result.ExitCode}.{Environment.NewLine}{result.StandardError}");
            }

            return ParseLdif(result.StandardOutput);
        }
        finally
        {
            File.Delete(passwordFile);
        }
    }

    private static List<LdapUser> ParseLdif(string ldif)
    {
        List<string> lines = [];
        foreach (string raw in ldif.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (raw.StartsWith(' ') && lines.Count > 0)
            {
                lines[^1] += raw[1..];
            }
            else
            {
                lines.Add(raw);
            }
        }

        List<LdapUser> users = [];
        Dictionary<string, string> current = new(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines)
        {
            if (line.StartsWith('#'))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                FlushRecord();
                continue;
            }

            int colon = line.IndexOf(':');
            if (colon < 0)
            {
                continue;
            }

            string name = line[..colon];
            bool isBase64 = colon + 1 < line.Length && line[colon + 1] == ':';
            string value = isBase64
                ? Encoding.UTF8.GetString(Convert.FromBase64String(line[(colon + 2)..].Trim()))
                : line[(colon + 1)..].Trim();
            current.TryAdd(name, value);
        }

        FlushRecord();
        return users;

        void FlushRecord()
        {
            if (current.Count == 0)
            {
                return;
            }

            users.Add(Build(current));
            current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static LdapUser Build(Dictionary<string, string> record)
    {
        record.TryGetValue("dn", out string? dn);

        List<string> organizationalUnits = (dn ?? string.Empty)
            .Split(',')
            .Select(part => part.Trim())
            .Where(part => part.StartsWith("OU=", StringComparison.OrdinalIgnoreCase))
            .Select(part => part[3..])
            .ToList();

        string studentClass = organizationalUnits.FirstOrDefault(unit => ClassPattern().IsMatch(unit)) ?? string.Empty;
        string role = organizationalUnits.FirstOrDefault(roles.Contains) ?? string.Empty;

        return new LdapUser(
            Get(record, "sAMAccountName"),
            Get(record, "cn"),
            Get(record, "sn"),
            Get(record, "givenName"),
            Get(record, "mail"),
            studentClass,
            role);
    }

    private static string Get(Dictionary<string, string> record, string key) =>
        record.GetValueOrDefault(key, string.Empty);
}

public sealed record LdapUser(
    string StudentId,
    string Cn,
    string Sn,
    string GivenName,
    string Mail,
    string Class,
    string Role);
