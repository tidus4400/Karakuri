using System.Security.Cryptography;
using System.Text;
using AutomationPlatform.Shared;

namespace AutomationPlatform.Shared.Tests;

public sealed class HmacSigningTests
{
    [Fact]
    public void ComputeBodySha256Hex_EmptyBody_ReturnsKnownHash()
    {
        var hash = HmacSigning.ComputeBodySha256Hex(Array.Empty<byte>());

        Assert.Equal(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            hash);
    }

    [Fact]
    public void BuildCanonicalString_UppercasesMethod_AndPreservesPath()
    {
        var canonical = HmacSigning.BuildCanonicalString("post", "/api/jobs/123/events", "1700000000", "abc123");

        Assert.Equal("POST\n/api/jobs/123/events\n1700000000\nabc123", canonical);
    }

    [Fact]
    public void ComputeSignatureBase64_AndConstantTimeEquals_RoundTrip()
    {
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var canonical = "GET\n/api/agents/a/jobs/next\n1700000000\nhash";

        var sig = HmacSigning.ComputeSignatureBase64(secret, canonical);

        Assert.True(HmacSigning.ConstantTimeEqualsBase64(sig, sig));
        Assert.False(HmacSigning.ConstantTimeEqualsBase64(sig, Convert.ToBase64String(Encoding.UTF8.GetBytes("bad"))));
    }

    [Fact]
    public void ConstantTimeEqualsBase64_InvalidBase64_ReturnsFalse()
    {
        Assert.False(HmacSigning.ConstantTimeEqualsBase64("not-base64", "also-not-base64"));
    }

    [Fact]
    public void HashSecretForStorage_IsDeterministic()
    {
        var left = HmacSigning.HashSecretForStorage("secret-value");
        var right = HmacSigning.HashSecretForStorage("secret-value");
        var different = HmacSigning.HashSecretForStorage("secret-value-2");

        Assert.Equal(left, right);
        Assert.NotEqual(left, different);
        Assert.Equal(64, left.Length);
    }
}
