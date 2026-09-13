using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FootballFormation.Core.Push;

namespace FootballFormation.Core.Tests;

/// Crypto nobody can eyeball. The encryption case is RFC 8291 §5 verbatim — its keys, its salt, its expected bytes — because a payload
/// that encrypts without error but decrypts to nothing looks exactly like a working feature until a phone stays silent.
public class WebPushTests
{
    private const string ClientPublic = "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4";
    private const string ClientAuth = "BTBZMqHH6r4Tts7J_aSIgg";
    private const string ServerPublic = "BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8";
    private const string ServerPrivate = "yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw";
    private const string Salt = "DGv6ra1nlYgDCS1FRnbzlw";

    private const string Expected =
        "DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6Tlz" +
        "AC8wEqKK6PBru3jl7A_yl95bQpu6cVPTpK4Mqgkf1CXztLVBSt2Ks3oZwbuwXPXLWyouBWLVWGNWQexSgSxsj_Qulcy4a-fN";

    [Fact]
    public void The_sealed_payload_matches_the_specs_own_example()
    {
        using var ephemeral = EphemeralKey();

        var body = WebPush.Encrypt(
            "When I grow up, I want to be a watermelon"u8,
            ClientPublic,
            ClientAuth,
            Base64Url.DecodeFromChars(Salt),
            ephemeral);

        Assert.Equal(Expected, Base64Url.EncodeToString(body));
    }

    [Fact]
    public void A_second_send_of_the_same_text_is_different_ciphertext()
    {
        var first = WebPush.Encrypt("Goal"u8, ClientPublic, ClientAuth);
        var second = WebPush.Encrypt("Goal"u8, ClientPublic, ClientAuth);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void The_authorization_header_carries_a_token_the_push_service_can_check()
    {
        var keys = new VapidKeys(ServerPublic, ServerPrivate, "mailto:coach@gjs-meiden.nl");
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

        var header = WebPush.Authorization("https://fcm.googleapis.com/fcm/send/abc123", keys, now);

        Assert.StartsWith("vapid t=", header);
        Assert.EndsWith($", k={ServerPublic}", header);

        var token = header["vapid t=".Length..header.IndexOf(',', StringComparison.Ordinal)];
        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);

        var claims = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1])).RootElement;
        // The audience is the endpoint's origin alone: a token scoped to the whole push service would be replayable against it.
        Assert.Equal("https://fcm.googleapis.com", claims.GetProperty("aud").GetString());
        Assert.Equal("mailto:coach@gjs-meiden.nl", claims.GetProperty("sub").GetString());
        Assert.Equal(now.AddHours(12).ToUnixTimeSeconds(), claims.GetProperty("exp").GetInt64());

        using var verifier = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = PointOf(Base64Url.DecodeFromChars(ServerPublic))
        });

        Assert.True(verifier.VerifyData(
            Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}"),
            Base64Url.DecodeFromChars(parts[2]),
            HashAlgorithmName.SHA256));
    }

    [Fact]
    public void A_token_is_signed_for_the_endpoint_it_is_sent_to()
    {
        var keys = new VapidKeys(ServerPublic, ServerPrivate, "mailto:coach@gjs-meiden.nl");
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

        var apple = WebPush.Authorization("https://web.push.apple.com/abc", keys, now);
        var claims = ClaimsOf(apple);

        Assert.Equal("https://web.push.apple.com", claims.GetProperty("aud").GetString());
    }

    private static JsonElement ClaimsOf(string header)
    {
        var token = header["vapid t=".Length..header.IndexOf(',', StringComparison.Ordinal)];
        return JsonDocument.Parse(Base64Url.DecodeFromChars(token.Split('.')[1])).RootElement;
    }

    private static ECDiffieHellman EphemeralKey() => ECDiffieHellman.Create(new ECParameters
    {
        Curve = ECCurve.NamedCurves.nistP256,
        D = Base64Url.DecodeFromChars(ServerPrivate),
        Q = PointOf(Base64Url.DecodeFromChars(ServerPublic))
    });

    private static ECPoint PointOf(byte[] uncompressed) => new()
    {
        X = uncompressed[1..33],
        Y = uncompressed[33..65]
    };
}
