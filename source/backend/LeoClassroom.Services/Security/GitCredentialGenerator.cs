using System.Security.Cryptography;

namespace LeoClassroom.Services.Security;

/// <summary>
///     Generates the passwords students use for git over HTTPS
/// </summary>
/// <remarks>
///     Forgejo can be configured to demand several character classes, so every generated password contains
///     all of them. The alphabet leaves out characters that are easy to confuse when read off a screen and
///     typed somewhere else (0/O, 1/l/I), because that is exactly what happens to these.
/// </remarks>
public static class GitCredentialGenerator
{
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Digits = "23456789";
    private const string Special = "!@#%^&*-_=+";

    private const int Length = 20;

    public static string Generate()
    {
        string alphabet = Lower + Upper + Digits + Special;

        // one from each class first, so the result satisfies any complexity rule Forgejo is set to
        char[] password =
        [
            Pick(Lower), Pick(Upper), Pick(Digits), Pick(Special),
            .. Enumerable.Range(0, Length - 4).Select(_ => Pick(alphabet))
        ];

        Shuffle(password);

        return new string(password);
    }

    private static char Pick(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

    private static void Shuffle(char[] characters)
    {
        for (int i = characters.Length - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (characters[i], characters[j]) = (characters[j], characters[i]);
        }
    }
}
