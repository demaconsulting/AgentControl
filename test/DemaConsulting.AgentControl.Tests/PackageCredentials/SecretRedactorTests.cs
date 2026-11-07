// Copyright (c) DEMA Consulting
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.Text;
using DemaConsulting.AgentControl.PackageCredentials;
using DemaConsulting.NuGet.Caching;

namespace DemaConsulting.AgentControl.Tests.PackageCredentials;

/// <summary>
///     Unit tests for <see cref="SecretRedactor"/> and <see cref="SanitizedException"/>, the
///     redaction engine behind <see cref="SecretRedactingLogger"/>.
/// </summary>
public sealed class SecretRedactorTests
{
    /// <summary>
    ///     Test that the complete value of an Authorization or Proxy-Authorization header is
    ///     replaced whatever its scheme - known, unknown, custom, or absent.
    /// </summary>
    [Theory]
    [InlineData("Authorization: Basic sentinel-basic-credential")]
    [InlineData("Authorization: Bearer sentinel-bearer-token.part2")]
    [InlineData("Authorization: Digest username=\"u\", realm=\"r\", nonce=\"n\", uri=\"/x\", response=\"sentinel-digest\"")]
    [InlineData("Authorization: NTLM sentinel-ntlm-blob==")]
    [InlineData("Authorization: Negotiate sentinel-negotiate-blob")]
    [InlineData("Authorization: AWS4-HMAC-SHA256 Credential=sentinel-key/20260101/r/s/aws4_request, Signature=sentinel-sig")]
    [InlineData("Authorization: Totally-Unknown-Scheme sentinel-custom value with spaces")]
    [InlineData("Authorization: sentinel-value-without-a-scheme")]
    [InlineData("authorization=sentinel-lowercase-value")]
    [InlineData("Proxy-Authorization: Basic sentinel-proxy-value")]
    [InlineData("PROXY-AUTHORIZATION : Digest response=sentinel-proxy-digest")]
    [InlineData("{\"Authorization\":\"Bearer sentinel-json-token\",\"Accept\":\"*/*\"}")]
    [InlineData("Authorization header: sentinel-header-value")]
    [InlineData("X-NuGet-ApiKey: sentinel-api-key-value")]
    [InlineData("Cookie: session=sentinel-cookie-value; other=1")]
    public void SecretRedactor_Redact_AuthorizationHeaders_ValueIsReplacedWhateverTheScheme(string text)
    {
        // Act
        var redacted = SecretRedactor.Redact(text);

        // Assert
        Assert.DoesNotContain("sentinel", redacted, StringComparison.Ordinal);
        Assert.Contains("[redacted]", redacted, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that only the header value goes: the header name stays, and the lines around it are
    ///     untouched.
    /// </summary>
    [Fact]
    public void SecretRedactor_Redact_HeaderInsideMultiLineText_KeepsOtherLines()
    {
        // Arrange
        var text = "GET /index.json\nAuthorization: Digest a=\"b\", response=\"sentinel-digest\"\nAccept: */*";

        // Act
        var redacted = SecretRedactor.Redact(text);

        // Assert
        Assert.Equal("GET /index.json\nAuthorization: [redacted]\nAccept: */*", redacted);
    }

    /// <summary>
    ///     Test that a credential that appears without a header or key around it is still caught.
    /// </summary>
    [Theory]
    [InlineData("sending Bearer sentinel-bare-token-123 to the server")]
    [InlineData("token sentinel eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJzZW50aW5lbCJ9.c2lnbmF0dXJl end")]
    [InlineData("password=sentinel-password&other=1")]
    [InlineData("the sig: sentinel-signature was rejected")]
    public void SecretRedactor_Redact_BareCredentialsAndSecretKeys_AreReplaced(string text)
    {
        // Act
        var redacted = SecretRedactor.Redact(text);

        // Assert
        Assert.DoesNotContain("sentinel-b", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("sentinel-password", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("sentinel-signature", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJhbGci", redacted, StringComparison.Ordinal);
        Assert.Contains("[redacted]", redacted, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that every URL form is reduced to scheme, host, port and path: IPv4, IPv6 literals,
    ///     user information (also with characters that are not allowed there), fragments, URLs
    ///     inside larger messages and several URLs in one message.
    /// </summary>
    [Theory]
    [InlineData("GET https://feeds.example.com/v3/index.json?sig=sentinel#frag", "GET https://feeds.example.com/v3/index.json")]
    [InlineData("see https://feeds.example.com:8443/a/b.json.", "see https://feeds.example.com:8443/a/b.json.")]
    [InlineData("(https://h.example.com/p?token=sentinel)", "(https://h.example.com/p)")]
    [InlineData("failed: http://[::1]:5000/x?token=sentinel end", "failed: http://[::1]:5000/x end")]
    [InlineData("http://[2001:db8::1]/v3/index.json?sig=sentinel", "http://[2001:db8::1]/v3/index.json")]
    [InlineData("http://[::1]/p#sentinel", "http://[::1]/p")]
    [InlineData("https://svc:sentinel@feeds.example.com/v3/index.json", "https://feeds.example.com/v3/index.json")]
    [InlineData("https://svc:sentinel@[::1]:5000/v3/index.json?x=1", "https://[::1]:5000/v3/index.json")]
    [InlineData("https://svc:sen/tinel@feeds.example.com/v3/index.json", "https://feeds.example.com/v3/index.json")]
    [InlineData("https://svc:sen?tinel@feeds.example.com/v3/index.json", "https://feeds.example.com/v3/index.json")]
    [InlineData("https://svc:sen#tinel@feeds.example.com/v3/index.json", "https://feeds.example.com/v3/index.json")]
    [InlineData("{\"url\":\"https://a.example.com/x?sig=sentinel\"}", "{\"url\":\"https://a.example.com/x\"}")]
    [InlineData("<https://a.example.com/x?sig=sentinel>", "<https://a.example.com/x>")]
    [InlineData("from https://a.example.com/x?sig=sentinel to https://b.example.com:81/y?sv=sentinel;", "from https://a.example.com/x to https://b.example.com:81/y;")]
    [InlineData("ftp://svc:sentinel@files.example.com/pub?x=sentinel", "ftp://files.example.com/pub")]
    public void SecretRedactor_Redact_UrlForms_KeepOnlySchemeHostPortAndPath(string text, string expected)
    {
        // Act
        var redacted = SecretRedactor.Redact(text);

        // Assert
        Assert.Equal(expected, redacted);
        Assert.DoesNotContain("sentinel", redacted, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that text without secrets, including null and empty, passes through unchanged.
    /// </summary>
    [Fact]
    public void SecretRedactor_Redact_PlainText_IsUnchanged()
    {
        // Act / Assert
        Assert.Equal("Retrying the request in 1 second", SecretRedactor.Redact("Retrying the request in 1 second"));
        Assert.Equal("https://feeds.example.com/v3/index.json", SecretRedactor.Redact("https://feeds.example.com/v3/index.json"));
        Assert.Equal(string.Empty, SecretRedactor.Redact(null));
        Assert.Equal(string.Empty, SecretRedactor.Redact(string.Empty));
    }

    /// <summary>
    ///     Test that a registered secret is replaced wherever it appears - in text no pattern
    ///     anticipates - together with its URL-encoded and Basic-header forms, and that
    ///     withdrawing it stops the replacement.
    /// </summary>
    [Fact]
    public void SecretRedactor_RegisterAndUnregister_ReplacesEveryOccurrenceOfTheSecretWhileRegistered()
    {
        // Arrange
        var secret = "sec-" + Guid.NewGuid().ToString("N") + " &x";
        var credential = new NuGetCredential("svc-agents", secret);
        var encoded = Uri.EscapeDataString(secret);
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"svc-agents:{secret}"));
        var text = $"weird: <<{secret}>> and {encoded} and {basic} done";

        // Act / Assert: unknown text is left alone before registration
        Assert.Contains(secret, SecretRedactor.Redact(text), StringComparison.Ordinal);
        SecretRedactor.Register(credential);
        try
        {
            var redacted = SecretRedactor.Redact(text);
            Assert.DoesNotContain(secret, redacted, StringComparison.Ordinal);
            Assert.DoesNotContain(encoded, redacted, StringComparison.Ordinal);
            Assert.DoesNotContain(basic, redacted, StringComparison.Ordinal);
            Assert.Equal("weird: <<[redacted]>> and [redacted] and [redacted] done", redacted);
        }
        finally
        {
            SecretRedactor.Unregister(credential);
        }

        Assert.Contains(secret, SecretRedactor.Redact(text), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that registrations are counted: a secret registered twice stays redacted after one
    ///     withdrawal, and an empty secret registers nothing.
    /// </summary>
    [Fact]
    public void SecretRedactor_Unregister_SecretRegisteredTwice_StaysRedactedUntilTheLastWithdrawal()
    {
        // Arrange
        var secret = "sec-" + Guid.NewGuid().ToString("N");
        var credential = new NuGetCredential("u", secret);
        SecretRedactor.Register(credential);
        SecretRedactor.Register(credential);
        SecretRedactor.Register(new NuGetCredential("u", string.Empty));

        // Act / Assert
        SecretRedactor.Unregister(credential);
        Assert.DoesNotContain(secret, SecretRedactor.Redact($"x {secret} y"), StringComparison.Ordinal);
        SecretRedactor.Unregister(credential);
        Assert.Contains(secret, SecretRedactor.Redact($"x {secret} y"), StringComparison.Ordinal);
        SecretRedactor.Unregister(credential);
        Assert.Equal("a b", SecretRedactor.Redact("a b"));
    }

    /// <summary>
    ///     Test that registering and redacting from many threads at once neither throws nor misses a
    ///     registered secret.
    /// </summary>
    [Fact]
    public void SecretRedactor_RegisterAndRedact_FromManyThreads_IsThreadSafe()
    {
        // Act / Assert
        Parallel.For(0, 200, i =>
        {
            var secret = $"sec-{i}-" + Guid.NewGuid().ToString("N");
            var credential = new NuGetCredential("u", secret);
            SecretRedactor.Register(credential);
            Assert.DoesNotContain(secret, SecretRedactor.Redact($"value {secret}"), StringComparison.Ordinal);
            SecretRedactor.Unregister(credential);
        });
    }

    /// <summary>
    ///     Test that a sanitized exception keeps the type name, a redacted message chain (inner and
    ///     aggregated exceptions included) and a redacted stack trace, and nothing that can carry a
    ///     secret: not the original, its inner exceptions, its data or the secrets in its text.
    /// </summary>
    [Fact]
    public void SecretRedactor_Sanitize_ExceptionWithSecretsInChain_KeepsOnlyRedactedText()
    {
        // Arrange
        var secret = "sec-" + Guid.NewGuid().ToString("N");
        var credential = new NuGetCredential("u", secret);
        var innermost = new InvalidOperationException($"Authorization: Bearer sentinel-inner for {secret}");
        var original = new AggregateException(
            "outer https://h.example.com/x?sig=sentinel-outer",
            new IOException("read https://u:sentinel-userinfo@h.example.com/y?t=sentinel-query failed", innermost),
            new TimeoutException("timeout"));
        original.Data["token"] = "sentinel-data";
        SecretRedactor.Register(credential);

        try
        {
            // Act
            var sanitized = SecretRedactor.Sanitize(original);

            // Assert
            var text = sanitized.ToString() + sanitized.Message + sanitized.StackTrace;
            Assert.DoesNotContain("sentinel", text, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
            Assert.Contains("AggregateException", sanitized.OriginalTypeName, StringComparison.Ordinal);
            Assert.Contains("IOException", sanitized.Message, StringComparison.Ordinal);
            Assert.Contains("TimeoutException", sanitized.Message, StringComparison.Ordinal);
            Assert.Contains("https://h.example.com/x", sanitized.Message, StringComparison.Ordinal);
            Assert.Null(sanitized.InnerException);
            Assert.Empty(sanitized.Data);
        }
        finally
        {
            SecretRedactor.Unregister(credential);
        }
    }

    /// <summary>
    ///     Test that a thrown exception's stack trace is kept (redacted) and that a null exception
    ///     is refused.
    /// </summary>
    [Fact]
    public void SecretRedactor_Sanitize_ThrownException_KeepsStackTrace()
    {
        // Arrange
        Exception thrown;
        try
        {
            throw new NotSupportedException("boom");
        }
        catch (NotSupportedException ex)
        {
            thrown = ex;
        }

        // Act
        var sanitized = SecretRedactor.Sanitize(thrown);

        // Assert
        Assert.Contains(nameof(SecretRedactor_Sanitize_ThrownException_KeepsStackTrace), sanitized.StackTrace, StringComparison.Ordinal);
        Assert.StartsWith("System.NotSupportedException: NotSupportedException: boom", sanitized.ToString(), StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => SecretRedactor.Sanitize(null!));
        Assert.Throws<ArgumentNullException>(() => SecretRedactor.SafeMessage(null!));
    }

    /// <summary>
    ///     Test that property values are redacted by kind: text and arbitrary objects are redacted,
    ///     plain value types pass through with their type.
    /// </summary>
    [Fact]
    public void SecretRedactor_RedactValue_RedactsTextAndKeepsPlainValueTypes()
    {
        // Act / Assert
        Assert.Null(SecretRedactor.RedactValue(null));
        Assert.Equal(3, SecretRedactor.RedactValue(3));
        Assert.True(Assert.IsType<bool>(SecretRedactor.RedactValue(true)));
        Assert.Equal(DayOfWeek.Monday, SecretRedactor.RedactValue(DayOfWeek.Monday));
        Assert.Equal("https://h.example.com/x", SecretRedactor.RedactValue("https://h.example.com/x?sig=sentinel"));
        Assert.Equal("https://h.example.com/x", SecretRedactor.RedactValue(new Uri("https://h.example.com/x?sig=sentinel")));
        Assert.Equal("Authorization: [redacted]", SecretRedactor.RedactValue(new ToStringObject("Authorization: Digest sentinel")));
    }

    /// <summary>
    ///     An object whose text is fixed, to prove arbitrary objects are redacted via their text.
    /// </summary>
    /// <param name="Text">The text.</param>
    private sealed record ToStringObject(string Text)
    {
        /// <inheritdoc />
        public override string ToString() => Text;
    }
}
