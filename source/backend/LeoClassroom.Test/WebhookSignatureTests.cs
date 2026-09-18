using System.Security.Cryptography;
using System.Text;
using LeoClassroom.Services.Forgejo;
using LeoClassroom.Services.Util;
using LeoClassroom.Persistence.Util;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LeoClassroom.Test;

public sealed class WebhookSignatureTests
{
    private const string Secret = "the-shared-webhook-secret";

    private static WebhookService Build(string secret) =>
        new(Substitute.For<IUnitOfWork>(),
            Substitute.For<IWebhookDispatcher>(),
            Substitute.For<IClock>(),
            Options.Create(new ForgejoSettings
            {
                BaseUrl = "https://git.test",
                AdminToken = "token",
                WebhookSecret = secret,
                WebhookTargetUrl = "https://api.test/hook"
            }),
            Substitute.For<ILogger<WebhookService>>());

    private static string Sign(string secret, byte[] body) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));

    [Fact]
    public void VerifySignature_CorrectSignature_IsAccepted()
    {
        byte[] body = Encoding.UTF8.GetBytes("""{"ref":"refs/heads/main"}""");

        Build(Secret).VerifySignature(body, Sign(Secret, body)).Should().BeTrue();
    }

    [Fact]
    public void VerifySignature_SignatureFromAnotherSecret_IsRejected()
    {
        byte[] body = Encoding.UTF8.GetBytes("""{"ref":"refs/heads/main"}""");

        Build(Secret).VerifySignature(body, Sign("a-different-secret", body)).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-hex")]
    public void VerifySignature_MissingOrMalformedSignature_IsRejected(string? signature)
    {
        byte[] body = Encoding.UTF8.GetBytes("{}");

        Build(Secret).VerifySignature(body, signature).Should().BeFalse();
    }

    [Fact]
    public void VerifySignature_SecretNotConfigured_IsRejectedEvenWithAMatchingSignature()
    {
        // with an empty key any caller could compute a valid HMAC, so the webhook must be refused outright
        byte[] body = Encoding.UTF8.GetBytes("{}");

        Build(string.Empty).VerifySignature(body, Sign(string.Empty, body)).Should().BeFalse();
    }
}
