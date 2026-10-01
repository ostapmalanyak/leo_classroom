using System.Text;
using System.Text.RegularExpressions;
using CliWrap;
using CliWrap.Buffered;
using LeoClassroom.Services.Util;
using LeoClassroom.Shared;
using Microsoft.Extensions.Options;
using OneOf;

namespace LeoClassroom.Services.Ldap;

public sealed record LdapPerson(string StudentId, string FirstName, string LastName, string? Email, string? Class,
                                Role Role);

public readonly record struct LdapError(
    string Reason, int? ExitCode = null, int? ResultCode = null, string? BaseDn = null);

public interface ILdapDirectory
{
    public ValueTask<OneOf<IReadOnlyCollection<LdapPerson>, LdapError>> SearchPeopleAsync();
}

internal sealed partial class LdapDirectory(
    IOptions<LdapSettings> settings, ILogger<LdapDirectory> logger) : ILdapDirectory
{
    public async ValueTask<OneOf<IReadOnlyCollection<LdapPerson>, LdapError>> SearchPeopleAsync()
    {
        LdapSettings config = settings.Value;
        if (string.IsNullOrWhiteSpace(config.Host)
            || string.IsNullOrWhiteSpace(config.BindDn)
            || string.IsNullOrWhiteSpace(config.BindPassword)
            || string.IsNullOrWhiteSpace(config.StudentBaseDn)
            || string.IsNullOrWhiteSpace(config.TeacherBaseDn))
        {
            return new LdapError(
                "LDAP host, bind DN, bind password, student base DN, and teacher base DN must all be configured.");
        }

        List<LdapPerson> people = [];
        foreach (var search in new[]
                 {
                     (BaseDn: config.StudentBaseDn, Role: Role.Student),
                     (BaseDn: config.TeacherBaseDn, Role: Role.Teacher)
                 })
        {
            try
            {
                OneOf<IReadOnlyCollection<LdapPerson>, LdapError> result =
                    await SearchAsync(config, search.BaseDn, search.Role);
                LdapError? error = null;
                result.Switch(found => people.AddRange(found), failure => error = failure);
                if (error is { } failed)
                {
                    logger.LogWarning(
                        "LDAP search failed against {Host} for {BaseDn} with ldapsearch exit code {ExitCode} " +
                        "and LDAP result code {ResultCode}: {Reason}",
                        config.Host, failed.BaseDn ?? search.BaseDn, failed.ExitCode, failed.ResultCode, failed.Reason);

                    return failed;
                }
            }
            catch (Exception ex) when (ex is IOException or CliWrap.Exceptions.CommandExecutionException)
            {
                logger.LogWarning(ex, "Could not execute LDAP search against {Host} for {BaseDn}",
                                  config.Host, search.BaseDn);

                return new LdapError(ex.Message, BaseDn: search.BaseDn);
            }
        }

        return OneOf<IReadOnlyCollection<LdapPerson>, LdapError>.FromT0(people.AsReadOnly());
    }

    private static async Task<OneOf<IReadOnlyCollection<LdapPerson>, LdapError>> SearchAsync(
        LdapSettings config, string baseDn, Role role)
    {
        if (config.TlsMode == LdapTlsMode.None && !string.IsNullOrEmpty(config.BindPassword))
        {
            return new LdapError(
                "Refusing an LDAP simple bind without TLS; set Ldap:TlsMode to Ldaps or StartTls.",
                BaseDn: baseDn);
        }

        string passwordFile = Path.GetTempFileName();
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(passwordFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            await File.WriteAllTextAsync(passwordFile, config.BindPassword, new UTF8Encoding(false));

            List<string> arguments = ["-x"];
            if (config.TlsMode == LdapTlsMode.StartTls)
            {
                arguments.Add("-ZZ");
            }
            arguments.AddRange(
            [
                "-H", BuildLdapUrl(config),
                "-D", config.BindDn,
                "-y", passwordFile,
                // Match the standalone exporter for the school's internal CA; this disables certificate validation.
                "-o", "TLS_REQCERT=never",
                "-b", baseDn,
                "-E", $"pr={config.PageSize}/noprompt",
                config.SearchFilter
            ]);
            arguments.AddRange(Attributes(config));

            const string commandName = "ldapsearch";
            var result = await Cli.Wrap(commandName)
                .WithArguments(arguments)
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(Encoding.UTF8);

            int? resultCode = ParseResultCode(result.StandardOutput);
            if (result.ExitCode != 0 || resultCode is not null and not 0)
            {
                string reason = result.StandardError.Trim();
                if (string.IsNullOrWhiteSpace(reason))
                {
                    reason = string.Join(
                        Environment.NewLine,
                        result.StandardOutput.Split('\n')
                            .Where(line => line.StartsWith("result:", StringComparison.OrdinalIgnoreCase)
                                           || line.StartsWith("text:", StringComparison.OrdinalIgnoreCase)));
                }
                if (string.IsNullOrWhiteSpace(reason))
                {
                    reason = $"ldapsearch failed with exit code {result.ExitCode} and no diagnostic text.";
                }

                return new LdapError(reason, result.ExitCode, resultCode, baseDn);
            }

            return OneOf<IReadOnlyCollection<LdapPerson>, LdapError>.FromT0(
                ParseLdif(result.StandardOutput, config, role));
        }
        finally
        {
            File.Delete(passwordFile);
        }
    }

    private static string BuildLdapUrl(LdapSettings config)
    {
        string host = config.Host;
        if (Uri.TryCreate(config.Host, UriKind.Absolute, out Uri? configuredUri)
            && (configuredUri.Scheme == "ldap" || configuredUri.Scheme == "ldaps"))
        {
            host = configuredUri.Host;
        }

        string scheme = config.TlsMode == LdapTlsMode.Ldaps ? "ldaps" : "ldap";

        return new UriBuilder(scheme, host, config.Port).Uri.AbsoluteUri;
    }

    private static string[] Attributes(LdapSettings config) =>
        new[]
        {
            config.StudentIdAttribute,
            config.MailAttribute,
            "displayName",
            config.GivenNameAttribute,
            config.FamilyNameAttribute,
            "cn",
            config.ClassAttribute
        }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    internal static IReadOnlyCollection<LdapPerson> ParseLdif(string ldif, LdapSettings config, Role role)
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

        List<LdapPerson> people = [];
        Dictionary<string, string> record = new(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines)
        {
            if (line.StartsWith('#'))
            {
                continue;
            }
            if (string.IsNullOrWhiteSpace(line))
            {
                Flush();
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
            record.TryAdd(name, value);
        }
        Flush();

        return people.AsReadOnly();

        void Flush()
        {
            if (record.Count == 0)
            {
                return;
            }

            string? studentId = Get(record, config.StudentIdAttribute);
            if (!string.IsNullOrWhiteSpace(studentId))
            {
                string? dn = Get(record, "dn");
                string? schoolClass = GetClassFromDn(dn) ?? Get(record, config.ClassAttribute);
                people.Add(new LdapPerson(
                    studentId,
                    Get(record, config.GivenNameAttribute) ?? string.Empty,
                    Get(record, config.FamilyNameAttribute) ?? string.Empty,
                    Get(record, config.MailAttribute),
                    role == Role.Student ? schoolClass : null,
                    role));
            }

            record = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string? GetClassFromDn(string? dn) =>
        dn?.Split(',')
           .Select(part => part.Trim())
           .Where(part => part.StartsWith("OU=", StringComparison.OrdinalIgnoreCase))
           .Select(part => part[3..])
           .FirstOrDefault(part => ClassPattern().IsMatch(part));

    private static string? Get(Dictionary<string, string> record, string key) =>
        record.TryGetValue(key, out string? value) ? value : null;

    private static int? ParseResultCode(string output)
    {
        foreach (string line in output.Split('\n'))
        {
            if (line.StartsWith("result:", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line.AsSpan("result:".Length).Trim(), out int code))
            {
                return code;
            }
        }

        return null;
    }

    [GeneratedRegex(@"^\d+[A-Z]{2,6}$", RegexOptions.IgnoreCase)]
    private static partial Regex ClassPattern();
}
