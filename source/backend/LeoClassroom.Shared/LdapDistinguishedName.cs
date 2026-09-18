namespace LeoClassroom.Shared;

public static class LdapDistinguishedName
{
    public static bool ContainsOrganisationalUnit(string distinguishedName, string unit)
    {
        int start = 0;
        bool quoted = false;
        for (int i = 0; i <= distinguishedName.Length; i++)
        {
            if (i < distinguishedName.Length)
            {
                char character = distinguishedName[i];
                if (character == '\\')
                {
                    i++;
                    continue;
                }
                if (character == '"')
                {
                    quoted = !quoted;
                }
                if (quoted || character is not (',' or '+'))
                {
                    continue;
                }
            }

            // An escaped comma or a comma inside a quoted CN is part of its value, not a new OU.
            ReadOnlySpan<char> component = distinguishedName.AsSpan(start, i - start).Trim();
            int equals = component.IndexOf('=');
            if (!quoted && equals >= 0
                && component[..equals].Trim().Equals("OU", StringComparison.OrdinalIgnoreCase)
                && component[(equals + 1)..].Trim().Equals(unit, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            start = i + 1;
        }

        return false;
    }
}
