using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FootballFormation.Core.Push;

/// RFC 8291 payload encryption and RFC 8292 VAPID over the BCL's own primitives. A package was considered and rejected: the only .NET
/// option has been unmaintained for years, and this is the whole of it.
public static class WebPush
{
    private const int SaltLength = 16;
    private const int KeyLength = 16;
    private const int NonceLength = 12;
    private const int PointLength = 65;
    private const int TagLength = 16;

    /// Big enough for any payload this app sends, and the value the RFC's own example uses.
    private const int RecordSize = 4096;

    /// VAPID tokens may not outlive 24 hours; half that leaves room for a clock that disagrees.
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(12);

    /// The `aes128gcm` body to POST at the endpoint: salt, record size, the ephemeral public key, then the sealed payload.
    public static byte[] Encrypt(ReadOnlySpan<byte> payload, string p256dh, string auth)
    {
        using var ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        return Encrypt(payload, p256dh, auth, RandomNumberGenerator.GetBytes(SaltLength), ephemeral);
    }

    internal static byte[] Encrypt(
        ReadOnlySpan<byte> payload, string p256dh, string auth, byte[] salt, ECDiffieHellman ephemeral)
    {
        var clientPublic = Base64Url.DecodeFromChars(p256dh);
        var authSecret = Base64Url.DecodeFromChars(auth);
        var serverPublic = UncompressedPoint(ephemeral.ExportParameters(false).Q);

        using var client = ECDiffieHellman.Create();
        client.ImportParameters(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = PointOf(clientPublic) });

        // The raw agreement, not the hashed one: RFC 8291 feeds Z itself into the first extract.
        var shared = ephemeral.DeriveRawSecretAgreement(client.PublicKey);

        var keyInfo = Concat("WebPush: info\0"u8, clientPublic, serverPublic);
        var ikm = HKDF.Expand(HashAlgorithmName.SHA256, HKDF.Extract(HashAlgorithmName.SHA256, shared, authSecret), 32, keyInfo);

        var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        var contentKey = HKDF.Expand(HashAlgorithmName.SHA256, prk, KeyLength, "Content-Encoding: aes128gcm\0"u8.ToArray());
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, NonceLength, "Content-Encoding: nonce\0"u8.ToArray());

        // 0x02 rather than 0x01: this is the last (and only) record of the message.
        var plaintext = new byte[payload.Length + 1];
        payload.CopyTo(plaintext);
        plaintext[^1] = 0x02;

        var sealedPayload = new byte[plaintext.Length + TagLength];
        using (var aes = new AesGcm(contentKey, TagLength))
        {
            aes.Encrypt(nonce, plaintext, sealedPayload.AsSpan(0, plaintext.Length), sealedPayload.AsSpan(plaintext.Length));
        }

        var body = new byte[SaltLength + 4 + 1 + PointLength + sealedPayload.Length];
        var at = body.AsSpan();
        salt.CopyTo(at);
        BinaryPrimitives.WriteUInt32BigEndian(at[SaltLength..], RecordSize);
        at[SaltLength + 4] = PointLength;
        serverPublic.CopyTo(at[(SaltLength + 5)..]);
        sealedPayload.CopyTo(at[(SaltLength + 5 + PointLength)..]);

        return body;
    }

    /// The `Authorization` header the push service checks the sender by. <paramref name="subject"/> is a mailto: or https: URL it can
    /// reach the operator at if this app ever misbehaves.
    public static string Authorization(string endpoint, VapidKeys keys, DateTimeOffset now)
    {
        var audience = new Uri(endpoint).GetLeftPart(UriPartial.Authority);

        var header = Encode("""{"typ":"JWT","alg":"ES256"}"""u8.ToArray());
        var claims = Encode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            aud = audience,
            exp = now.Add(TokenLifetime).ToUnixTimeSeconds(),
            sub = keys.Subject
        }));

        var signingInput = $"{header}.{claims}";

        using var signer = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = Base64Url.DecodeFromChars(keys.PrivateKey),
            Q = PointOf(Base64Url.DecodeFromChars(keys.PublicKey))
        });

        // SignData already answers in the r||s form ES256 wants, not the DER sequence.
        var signature = signer.SignData(Encoding.UTF8.GetBytes(signingInput), HashAlgorithmName.SHA256);

        return $"vapid t={signingInput}.{Encode(signature)}, k={keys.PublicKey}";
    }

    private static ECPoint PointOf(byte[] uncompressed) => new()
    {
        X = uncompressed[1..33],
        Y = uncompressed[33..PointLength]
    };

    private static byte[] UncompressedPoint(ECPoint point)
    {
        var bytes = new byte[PointLength];
        bytes[0] = 0x04;
        point.X!.CopyTo(bytes, 1);
        point.Y!.CopyTo(bytes, 33);
        return bytes;
    }

    private static byte[] Concat(ReadOnlySpan<byte> label, byte[] first, byte[] second)
    {
        var bytes = new byte[label.Length + first.Length + second.Length];
        label.CopyTo(bytes);
        first.CopyTo(bytes, label.Length);
        second.CopyTo(bytes, label.Length + first.Length);
        return bytes;
    }

    private static string Encode(byte[] bytes) => Base64Url.EncodeToString(bytes);
}

/// The application server's identity to every push service. Generated once and kept in Fly secrets; the public half is also what the
/// browser is handed as its applicationServerKey, so changing it invalidates every subscription on file.
public sealed record VapidKeys(string PublicKey, string PrivateKey, string Subject);
