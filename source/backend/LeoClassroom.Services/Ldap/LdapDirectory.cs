using System.Net;
using System.Text;
using LeoClassroom.Services.Util;
using LeoClassroom.Shared;
using Microsoft.Extensions.Options;
using OneOf;
using OneOf.Types;
using System.DirectoryServices.Protocols;

namespace LeoClassroom.Services.Ldap;

public sealed record LdapPerson(string StudentId, string FirstName, string LastName, string? Email, string? Class,
                                Role Role);

public readonly record struct LdapError(string Reason);

public interface ILdapDirectory
{
    public ValueTask<OneOf<IReadOnlyCollection<LdapPerson>, LdapError>> SearchPeopleAsync();
}

internal sealed class LdapDirectory(IOptions<LdapSettings> settings, ILogger<LdapDirectory> logger) : ILdapDirectory
{
    public ValueTask<OneOf<IReadOnlyCollection<LdapPerson>, LdapError>> SearchPeopleAsync()
    {
        LdapSettings config = settings.Value;
        try
        {
            using var connection = CreateConnection(config);
            connection.Bind();

            List<LdapPerson> people = [];
            people.AddRange(Search(connection, config, config.StudentBaseDn, Role.Student));
            people.AddRange(Search(connection, config, config.TeacherBaseDn, Role.Teacher));

            return ValueTask.FromResult<OneOf<IReadOnlyCollection<LdapPerson>, LdapError>>(people.AsReadOnly());
        }
        catch (LdapException ex)
        {
            logger.LogWarning(ex, "LDAP search failed against {Host}", config.Host);

            return ValueTask.FromResult<OneOf<IReadOnlyCollection<LdapPerson>, LdapError>>(
                new LdapError(ex.Message));
        }
    }

    private static LdapConnection CreateConnection(LdapSettings config)
    {
        // a simple bind sends the bind DN and password in the clear, so it may only happen over TLS
        if (config.TlsMode == LdapTlsMode.None && !string.IsNullOrEmpty(config.BindPassword))
        {
            throw new InvalidOperationException(
                "Refusing an LDAP simple bind without TLS - set Ldap:TlsMode to Ldaps or StartTls");
        }

        var identifier = new LdapDirectoryIdentifier(config.Host, config.Port);
        var connection = new LdapConnection(identifier)
        {
            AuthType = AuthType.Basic,
            Credential = new NetworkCredential(config.BindDn, config.BindPassword)
        };
        connection.SessionOptions.ProtocolVersion = 3;

        if (config.TlsMode == LdapTlsMode.Ldaps)
        {
            connection.SessionOptions.SecureSocketLayer = true;
        }
        else if (config.TlsMode == LdapTlsMode.StartTls)
        {
            connection.SessionOptions.StartTransportLayerSecurity(null);
        }

        return connection;
    }

    private IEnumerable<LdapPerson> Search(LdapConnection connection, LdapSettings config, string baseDn, Role role)
    {
        string[] attributes =
        [
            config.StudentIdAttribute, config.GivenNameAttribute, config.FamilyNameAttribute,
            config.MailAttribute, config.ClassAttribute
        ];

        var request = new SearchRequest(baseDn, config.SearchFilter, SearchScope.Subtree, attributes);
        var pageControl = new PageResultRequestControl(config.PageSize);
        request.Controls.Add(pageControl);

        while (true)
        {
            var response = (SearchResponse)connection.SendRequest(request);
            foreach (SearchResultEntry entry in response.Entries)
            {
                LdapPerson? person = MapEntry(entry, config, role);
                if (person is not null)
                {
                    yield return person;
                }
            }

            PageResultResponseControl? pageResponse = response.Controls
                                                              .OfType<PageResultResponseControl>()
                                                              .FirstOrDefault();
            if (pageResponse is null || pageResponse.Cookie.Length == 0)
            {
                yield break;
            }

            pageControl.Cookie = pageResponse.Cookie;
        }
    }

    private static LdapPerson? MapEntry(SearchResultEntry entry, LdapSettings config, Role role)
    {
        string? studentId = Attribute(entry, config.StudentIdAttribute);
        if (string.IsNullOrWhiteSpace(studentId))
        {
            return null;
        }

        return new LdapPerson(
            studentId,
            Attribute(entry, config.GivenNameAttribute) ?? string.Empty,
            Attribute(entry, config.FamilyNameAttribute) ?? string.Empty,
            Attribute(entry, config.MailAttribute),
            role == Role.Student ? Attribute(entry, config.ClassAttribute) : null,
            role);
    }

    private static string? Attribute(SearchResultEntry entry, string name)
    {
        if (!entry.Attributes.Contains(name))
        {
            return null;
        }

        DirectoryAttribute attribute = entry.Attributes[name];

        return attribute.Count == 0 ? null : attribute[0] switch
        {
            string s => s,
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            var value => value?.ToString()
        };
    }
}
