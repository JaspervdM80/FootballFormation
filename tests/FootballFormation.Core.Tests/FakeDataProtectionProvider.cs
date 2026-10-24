using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace FootballFormation.Core.Tests;

/// The host's key ring without the full DataProtection package, whose dependencies fail the audit. Each instance is its own ring, so a
/// second one cannot open what the first protected — the way a laptop's ring cannot open the live volume's.
public sealed class FakeDataProtectionProvider : IDataProtectionProvider, IDataProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    public IDataProtector CreateProtector(string purpose) => this;

    public byte[] Protect(byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var ciphertext = new byte[plaintext.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        return [.. nonce, .. tag, .. ciphertext];
    }

    public byte[] Unprotect(byte[] protectedData)
    {
        var ciphertext = protectedData[(NonceSize + TagSize)..];
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(protectedData[..NonceSize], ciphertext, protectedData[NonceSize..(NonceSize + TagSize)], plaintext);
        return plaintext;
    }
}
