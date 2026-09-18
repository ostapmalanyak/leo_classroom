#!/usr/bin/env dotnet
#:package CliWrap@3.6.7

// Run with:  dotnet run LdapUserExport.cs
// 1. call `ldapsearch` (LDAPS, paged) against the AD domain controller
// 2. parse the returned LDIF (unfold folded lines, base64/UTF-8 decode)
// 3. derive `class` and `role` from the DN's OU path
// 4. return the rows as a List<LdapUser>
// Requires the OpenLDAP client (`ldapsearch`) on PATH:
//   Fedora:  sudo dnf install openldap-clients
//   Debian:  sudo apt install ldap-utils

using System.Text;
using System.Text.RegularExpressions;
using CliWrap;
using CliWrap.Buffered;

const string Host = "ldaps://addc01.edu.htl-leonding.ac.at:636";
const string BaseDn = "dc=edu,dc=htl-leonding,dc=ac,dc=at";
const string Filter = "(&(objectCategory=person)(objectClass=user))";

// Bind user (the account doing the query) and its password.
// Both can be set via environment variables (LDAP_BIND_DN / LDAP_PASSWORD).
const string BindDnPlaceholder = "<YOUR-USERNAME@edu.htl-leonding.ac.at>";
const string PasswordPlaceholder = "<YOUR-PASSWORD-HERE>";
var bindDn = Environment.GetEnvironmentVariable("LDAP_BIND_DN") ?? BindDnPlaceholder;
var password = Environment.GetEnvironmentVariable("LDAP_PASSWORD") ?? PasswordPlaceholder;

var users = await LdapDirectory.QueryUsersAsync(Host, bindDn, password, BaseDn, Filter);

Console.WriteLine($"Retrieved {users.Count} users.");
foreach (var g in users.GroupBy(u => string.IsNullOrEmpty(u.Role) ? "(none)" : u.Role)
                       .OrderByDescending(g => g.Count()))
{
    Console.WriteLine($"  {g.Count(),6}  {g.Key}");
}

internal static partial class LdapDirectory
{
    // Attributes requested from the directory
    private static readonly string[] attributes =
        ["sAMAccountName", "mail", "displayName", "givenName", "sn", "cn"];

    // A class OU looks like 3BHIF / 5AHITM / 4AFELA: leading digit(s) + letters
    [GeneratedRegex(@"^\d+[A-Z]{2,6}$")]
    private static partial Regex ClassPattern { get; }

    // The role OU is one of these fixed categories under the tree.
    private static readonly HashSet<string> roles =
        [with(StringComparer.OrdinalIgnoreCase), "Students", "Teachers", "Testusers", "Exams", "archivedusers"];
    
    public static async Task<List<LdapUser>> QueryUsersAsync(
        string host, string bindDn, string password, string baseDn, string filter)
    {
        // The password is passed via `ldapsearch -y <file>` rather than `-w <pw>`,
        // so it never appears on the command line
        var pwFile = Path.GetTempFileName();
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(pwFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            await File.WriteAllTextAsync(pwFile, password, new UTF8Encoding(false));
            
            var args = new List<string>
            {
                "-x",
                "-H", host,
                "-D", bindDn,
                "-y", pwFile,
                "-b", baseDn,
                "-E", "pr=1000/noprompt", // paged results, 1000 per page
                filter
            };
            args.AddRange(attributes);

            const string CommandName = "ldapsearch";
            var cmd = Cli.Wrap(CommandName)
                         .WithArguments(args)
                         .WithValidation(CommandResultValidation.None);

            var result = await cmd.ExecuteBufferedAsync(Encoding.UTF8);

            if (result.ExitCode != 0)
            {
                throw new
                    InvalidOperationException($"{CommandName} exited with code {result.ExitCode}.\n{result.StandardError}");
            }

            return ParseLdif(result.StandardOutput);
        }
        finally
        {
            try
            {
                File.Delete(pwFile);
            }
            catch
            {
                /* best effort */
            }
        }
    }

    private static List<LdapUser> ParseLdif(string ldif)
    {
        // 1. Unfold: continuation lines start with a single space and belong to
        //    the previous logical line (long DNs are wrapped this way)
        var lines = new List<string>();
        foreach (var raw in ldif.Replace("\r\n", "\n").Split('\n'))
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

        // 2. Group into records (blank-line separated), first value of each attr wins
        var users = new List<LdapUser>();
        var cur = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            if (line.StartsWith('#'))
            {
                continue;
            }

            if (line.Trim().Length == 0)
            {
                Flush();

                continue;
            }

            var colon = line.IndexOf(':');
            if (colon < 0)
            {
                continue;
            }

            var attr = line[..colon];
            string value;
            if (colon + 1 < line.Length && line[colon + 1] == ':')
            {
                value = Encoding.UTF8.GetString(Convert.FromBase64String(line[(colon + 2)..].Trim()));
            }
            else
            {
                value = line[(colon + 1)..].Trim();
            }

            cur.TryAdd(attr, value);
        }

        Flush();

        return users;

        void Flush()
        {
            if (cur.Count > 0)
            {
                users.Add(Build(cur));
                cur = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    private static LdapUser Build(Dictionary<string, string> r)
    {
        r.TryGetValue("dn", out var dn);

        // OU components of the DN, top-down as written (CN first, then OUs)
        var ous = (dn ?? "")
                  .Split(',')
                  .Select(p => p.Trim())
                  .Where(p => p.StartsWith("OU=", StringComparison.OrdinalIgnoreCase))
                  .Select(p => p[3..])
                  .ToList();

        var klass = ous.FirstOrDefault(o => ClassPattern.IsMatch(o)) ?? "";
        var role = ous.FirstOrDefault(roles.Contains) ?? "";

        return new LdapUser(Get(r, "cn"),
                            Get(r, "sn"),
                            Get(r, "givenName"),
                            Get(r, "mail"),
                            klass,
                            role);

        static string Get(Dictionary<string, string> d, string key) => d.GetValueOrDefault(key, "");
    }
}

public sealed record LdapUser(
    string Cn,
    string Sn,
    string GivenName,
    string Mail,
    string Class,
    string Role);
