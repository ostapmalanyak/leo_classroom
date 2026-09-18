using LeoClassroom.Shared;

namespace LeoClassroom.Test;

public sealed class LdapDistinguishedNameTests
{
    [Theory]
    [InlineData("CN=IF001,OU=Teachers,DC=school", true)]
    [InlineData("CN=Last\\, First, ou = teachers ,DC=school", true)]
    [InlineData("CN=IF001+OU=Teachers,DC=school", true)]
    [InlineData("CN=IF001,OU=TeachersAlumni,DC=school", false)]
    [InlineData("CN=OU=Teachers,OU=Students,DC=school", false)]
    [InlineData("CN=Name\\,OU=Teachers,OU=Students,DC=school", false)]
    [InlineData("CN=Name\\+OU=Teachers,OU=Students,DC=school", false)]
    [InlineData("CN=\"Name,OU=Teachers\",OU=Students,DC=school", false)]
    [InlineData("", false)]
    public void ContainsOrganisationalUnit_MatchesCompleteUnescapedAttributes(string dn, bool expected)
    {
        LdapDistinguishedName.ContainsOrganisationalUnit(dn, "Teachers").Should().Be(expected);
    }
}
