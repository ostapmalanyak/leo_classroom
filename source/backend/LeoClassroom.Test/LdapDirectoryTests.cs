using LeoClassroom.Services.Ldap;
using LeoClassroom.Services.Util;
using LeoClassroom.Shared;

namespace LeoClassroom.Test;

public sealed class LdapDirectoryTests
{
    [Fact]
    public void ParseLdif_MapsExporterAttributesAndClassFromDistinguishedName()
    {
        const string ldif = """
            dn: CN=Student One,OU=4AFELA,OU=Students,DC=edu,DC=example
            sAMAccountName: IF123456
            givenName: Student
            sn: One
            mail: student@example.edu
            cn: Student One

            """;
        var settings = new LdapSettings
        {
            Host = "ldap.example.edu",
            BindDn = "bind-user",
            BindPassword = "password",
            StudentBaseDn = "OU=Students,DC=edu,DC=example",
            TeacherBaseDn = "OU=Teachers,DC=edu,DC=example"
        };

        IReadOnlyCollection<LdapPerson> people = LdapDirectory.ParseLdif(ldif, settings, Role.Student);

        people.Should().ContainSingle().Which.Should().Be(
            new LdapPerson("IF123456", "Student", "One", "student@example.edu", "4AFELA", Role.Student));
    }
}
