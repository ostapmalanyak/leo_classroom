using System.Text;

namespace LeoClassroom.Services.Util;

public static class Slugifier
{
    public static string Slugify(string value)
    {
        var builder = new StringBuilder(value.Length);
        bool lastDash = false;
        foreach (char c in value.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
                lastDash = false;
            }
            else if (!lastDash && builder.Length > 0)
            {
                builder.Append('-');
                lastDash = true;
            }
        }

        return builder.ToString().Trim('-');
    }
}
