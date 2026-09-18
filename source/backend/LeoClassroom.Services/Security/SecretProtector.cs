using System.Security.Cryptography;
using System.Text;
using LeoClassroom.Services.Util;
using Microsoft.Extensions.Options;

namespace LeoClassroom.Services.Security;

public interface ISecretProtector
{
    public string Protect(string plaintext);
    public bool TryUnprotect(string protectedValue, out string plaintext);
}

internal sealed class SecretProtector : ISecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _masterKey;

    public SecretProtector(IOptions<DataProtectionSettings> options)
    {
        _masterKey = Convert.FromBase64String(options.Value.MasterKey);
        if (_masterKey.Length is not (16 or 24 or 32))
        {
            throw new InvalidOperationException("DataProtection master key must be a 128/192/256-bit base64 value");
        }
    }

    public string Protect(string plaintext)
    {
        byte[] plain = Encoding.UTF8.GetBytes(plaintext);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] cipher = new byte[plain.Length];
        byte[] tag = new byte[TagSize];

        using var aes = new AesGcm(_masterKey, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);

        byte[] combined = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, combined, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, combined, NonceSize + TagSize, cipher.Length);

        return Convert.ToBase64String(combined);
    }

    public bool TryUnprotect(string protectedValue, out string plaintext)
    {
        plaintext = string.Empty;
        byte[] combined;
        try
        {
            combined = Convert.FromBase64String(protectedValue);
        }
        catch (FormatException)
        {
            return false;
        }

        if (combined.Length < NonceSize + TagSize)
        {
            return false;
        }

        byte[] nonce = combined[..NonceSize];
        byte[] tag = combined[NonceSize..(NonceSize + TagSize)];
        byte[] cipher = combined[(NonceSize + TagSize)..];
        byte[] plain = new byte[cipher.Length];

        try
        {
            using var aes = new AesGcm(_masterKey, TagSize);
            aes.Decrypt(nonce, cipher, tag, plain);
        }
        catch (CryptographicException)
        {
            return false;
        }

        plaintext = Encoding.UTF8.GetString(plain);

        return true;
    }
}
