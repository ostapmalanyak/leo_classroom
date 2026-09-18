namespace LeoClassroom.Services.Util;

public enum LdapTlsMode
{
    None = 0,
    Ldaps = 1,
    StartTls = 2
}

public sealed class LdapSettings
{
    public const string SectionKey = "Ldap";

    public required string Host { get; init; }
    public int Port { get; init; } = 636;
    public LdapTlsMode TlsMode { get; init; } = LdapTlsMode.Ldaps;
    public required string BindDn { get; init; }
    public required string BindPassword { get; init; }

    public required string StudentBaseDn { get; init; }
    public required string TeacherBaseDn { get; init; }
    public string SearchFilter { get; init; } = "(objectClass=person)";
    public int PageSize { get; init; } = 500;

    public string StudentIdAttribute { get; init; } = "uid";
    public string GivenNameAttribute { get; init; } = "givenName";
    public string FamilyNameAttribute { get; init; } = "sn";
    public string MailAttribute { get; init; } = "mail";
    public string ClassAttribute { get; init; } = "ou";

    public int SoftDeleteThreshold { get; init; } = 50;
    public string NightlyCron { get; init; } = "0 0 2 * * ?";
}
