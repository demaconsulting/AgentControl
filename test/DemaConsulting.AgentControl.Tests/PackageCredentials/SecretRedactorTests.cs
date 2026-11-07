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
using System.Text.Encodings.Web;
using System.Text.Json;
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
    ///     Test that a header or key value in quotes is redacted up to its real closing quote when
    ///     the value contains escaped quotes - written as <c>\"</c>, <c>\u0022</c>, <c>\\"</c>,
    ///     doubled quotes or the other quote character - so no suffix of the credential survives.
    /// </summary>
    [Theory]
    [InlineData("""{"Authorization":"Digest value with \"quoted\" secret-token"}""")]
    [InlineData("""{"Authorization":"Basic abc\u0022def secret-token"}""")]
    [InlineData("""{"Authorization":"Basic abc\\\"def secret-token"}""")]
    [InlineData("""{"Authorization":"Basic abc\\"}""")]
    [InlineData("""{"Proxy-Authorization":"NTLM a\"b\"c\"d secret-token"}""")]
    [InlineData("""Authorization: "Basic it's \"secret-token\" more secret-token" """)]
    [InlineData("""Authorization: 'Basic it\'s secret-token'""")]
    [InlineData("""Authorization: "Basic a""b secret-token" """)]
    [InlineData("""Authorization: 'Basic a''b secret-token' """)]
    [InlineData("""{"password":"abc\"def secret-token"}""")]
    [InlineData("""{"sig":"abc\u0022def secret-token"}""")]
    [InlineData("""password='abc\'def secret-token'""")]
    [InlineData("""password=abc\"def\"secret-token""")]
    [InlineData("""token=abc\'def\'secret-token""")]
    public void SecretRedactor_Redact_QuotedValueWithEscapedQuotes_NoSuffixSurvives(string text)
    {
        // Act
        var redacted = SecretRedactor.Redact(text);

        // Assert
        Assert.DoesNotContain("secret-token", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("def", redacted.Replace("redacted", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("[redacted]", redacted, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the part of the text after the real closing quote is kept, and that an even run
    ///     of backslashes before a quote leaves the quote as the terminator.
    /// </summary>
    [Theory]
    [InlineData("""{"Authorization":"Basic x \"y\" z","Accept":"*/*"}""", """{"Authorization":[redacted],"Accept":"*/*"}""")]
    [InlineData("""{"Authorization":"Basic x\\","Accept":"*/*"}""", """{"Authorization":[redacted],"Accept":"*/*"}""")]
    [InlineData("""{"password":"a\"b","name":"keep"}""", """{"password":[redacted],"name":"keep"}""")]
    [InlineData("""{"url":"https://h.example.com/p?sig=s\\","next":"keep"}""", """{"url":"https://h.example.com/p","next":"keep"}""")]
    public void SecretRedactor_Redact_QuotedValue_StopsAtTheRealTerminator(string text, string expected)
    {
        // Act / Assert
        Assert.Equal(expected, SecretRedactor.Redact(text));
    }

    /// <summary>
    ///     Test that a header value inside JSON that is itself serialized into a JSON string
    ///     (nested JSON, escaped twice) is redacted entirely, with the serializer's default and
    ///     relaxed escaping alike.
    /// </summary>
    [Theory]
    [InlineData(false, "Basic abc\"def secret-token tail")]
    [InlineData(true, "Basic abc\"def secret-token tail")]
    [InlineData(false, "Digest a=\"b\", response=\"secret-token\", c=\\\"d")]
    [InlineData(true, "Digest a=\"b\", response=\"secret-token\", c=\\\"d")]
    [InlineData(false, "Bearer it's secret-token \\ tail")]
    [InlineData(true, "Bearer it's secret-token \\ tail")]
    public void SecretRedactor_Redact_JsonSerializedAuthorizationValue_IsRedactedWholeAtEveryNestingLevel(bool relaxedEscaping, string value)
    {
        // Arrange: the serializer writes the quotes as \u0022 by default and as \" when relaxed
        var options = new JsonSerializerOptions
        {
            Encoder = relaxedEscaping ? JavaScriptEncoder.UnsafeRelaxedJsonEscaping : JavaScriptEncoder.Default
        };
        var headers = JsonSerializer.Serialize(new Dictionary<string, string> { ["Authorization"] = value, ["Accept"] = "*/*" }, options);
        var nested = JsonSerializer.Serialize(new Dictionary<string, string> { ["headers"] = headers }, options);
        var twiceNested = JsonSerializer.Serialize(new Dictionary<string, string> { ["log"] = nested }, options);

        // Act / Assert
        foreach (var text in new[] { headers, nested, twiceNested })
        {
            var redacted = SecretRedactor.Redact(text);
            Assert.DoesNotContain("secret-token", redacted, StringComparison.Ordinal);
            Assert.DoesNotContain("tail", redacted, StringComparison.Ordinal);
            Assert.Contains("[redacted]", redacted, StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     Test that a URL whose query contains escaped quotes inside a quoted or JSON string is
    ///     reduced as a whole - nothing after an escaped, doubled or entity-encoded quote survives.
    /// </summary>
    [Theory]
    [InlineData("""{"url":"https://host/path?sig=secret-token\"&other=secret-token"}""", """{"url":"https://host/path"}""")]
    [InlineData("""{"url":"https://host/path?sig=secret-token\u0022&other=secret-token"}""", """{"url":"https://host/path"}""")]
    [InlineData("""{"url":"https://host/path?sig=a\\\"&other=secret-token"}""", """{"url":"https://host/path"}""")]
    [InlineData("""{"url":"http://[::1]:5000/p?sig=a\"&other=secret-token#secret-token"}""", """{"url":"http://[::1]:5000/p"}""")]
    [InlineData("""{"url":"https://u:secret-token@host/p?sig=a\"b"}""", """{"url":"https://host/p"}""")]
    [InlineData("""'https://host/path?sig=it\'s&other=secret-token'""", """'https://host/path'""")]
    [InlineData("""x "https://host/path?sig=a""b&other=secret-token" y""", """x "https://host/path" y""")]
    [InlineData("""{"msg":"{\"url\":\"https://host/p?sig=a\\\"&o=secret-token\"}"}""", """{"msg":"{\"url\":\"https://host/p""")]
    [InlineData("""{"msg":"{\u0022url\u0022:\u0022https://host/p?sig=a\\u0022&o=secret-token\u0022}"}""", """{"msg":"{\u0022url\u0022:\u0022https://host/p""")]
    public void SecretRedactor_Redact_UrlWithEscapedQuotes_NoSuffixSurvives(string text, string expectedPrefix)
    {
        // Act
        var redacted = SecretRedactor.Redact(text);

        // Assert
        Assert.DoesNotContain("secret-token", redacted, StringComparison.Ordinal);
        Assert.StartsWith(expectedPrefix, redacted, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a URL inside JSON serialized by <see cref="JsonSerializer"/> (default and relaxed
    ///     escaping, also nested) never leaks its query or fragment.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SecretRedactor_Redact_JsonSerializedUrl_NeverLeaksQueryOrFragment(bool relaxedEscaping)
    {
        // Arrange
        var options = new JsonSerializerOptions
        {
            Encoder = relaxedEscaping ? JavaScriptEncoder.UnsafeRelaxedJsonEscaping : JavaScriptEncoder.Default
        };
        var url = "https://h.example.com/p?sig=a\"b&other=secret-token#secret-token";
        var json = JsonSerializer.Serialize(new Dictionary<string, string> { ["url"] = url, ["note"] = "fetched \"" + url + "\" ok" }, options);
        var nested = JsonSerializer.Serialize(new Dictionary<string, string> { ["log"] = json }, options);

        // Act / Assert
        foreach (var text in new[] { json, nested })
        {
            var redacted = SecretRedactor.Redact(text);
            Assert.DoesNotContain("secret-token", redacted, StringComparison.Ordinal);
            Assert.Contains("https://h.example.com/p", redacted, StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     Test that a URL inside parentheses - as a bare parenthesized link or in a sentence - is
    ///     reduced, its query and fragment removed, and the closing parenthesis and surrounding
    ///     punctuation kept.
    /// </summary>
    [Theory]
    [InlineData("see (https://h.example.com/p?token=sentinel).", "see (https://h.example.com/p).")]
    [InlineData("(see https://h.example.com/p?sig=sentinel)", "(see https://h.example.com/p)")]
    [InlineData("fetched (https://h.example.com:8443/a/b?x=sentinel#sentinel) ok", "fetched (https://h.example.com:8443/a/b) ok")]
    [InlineData("((https://h.example.com/p?x=sentinel))", "((https://h.example.com/p))")]
    public void SecretRedactor_Redact_UrlInParentheses_RedactsQueryAndKeepsClosingParenthesis(string text, string expected)
    {
        // Act
        var redacted = SecretRedactor.Redact(text);

        // Assert
        Assert.Equal(expected, redacted);
        Assert.DoesNotContain("sentinel", redacted, StringComparison.Ordinal);
        Assert.Equal(text.Count(c => c == ')'), redacted.Count(c => c == ')'));
    }

    /// <summary>
    ///     Test that parentheses that belong to the URL - balanced in the path, query or fragment,
    ///     or an unmatched closer followed by more query text - never end the URL early, so no part
    ///     of the query or fragment survives.
    /// </summary>
    [Theory]
    [InlineData("https://en.example.com/wiki/Foo_(bar)?token=sentinel#frag(x)", "https://en.example.com/wiki/Foo_(bar)")]
    [InlineData("https://h.example.com/p?sig=a(b)c&t=sentinel", "https://h.example.com/p")]
    [InlineData("https://h.example.com/p?sig=ab)cd&t=sentinel next", "https://h.example.com/p next")]
    [InlineData("https://h.example.com/p#frag)sentinel", "https://h.example.com/p")]
    public void SecretRedactor_Redact_UrlWithBalancedParenthesesInPathAndQuery_RedactsWholeQueryAndFragment(
        string text, string expected)
    {
        // Act
        var redacted = SecretRedactor.Redact(text);

        // Assert
        Assert.Equal(expected, redacted);
        Assert.DoesNotContain("sentinel", redacted, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a markdown-style link whose URL carries user information, a query and a
    ///     fragment is reduced to scheme, host and path, and that the text around it is kept.
    /// </summary>
    [Fact]
    public void SecretRedactor_Redact_MarkdownLinkWithUserInfoAndQuery_RedactsUserInfoQueryAndFragment()
    {
        // Arrange
        var link = "https://svc:sentinel-pw@h.example.com/a(b)?sig=sentinel#sentinel";
        var text = $"[docs]({link}) and [more](https://h2.example.com/x?t=sentinel).";

        // Act
        var redacted = SecretRedactor.Redact(text);

        // Assert
        Assert.Equal("[docs](https://h.example.com/a(b)) and [more](https://h2.example.com/x).", redacted);
    }

    /// <summary>
    ///     Test that header and secret-key values are redacted when the quotes around them were
    ///     written by <see cref="JsonSerializer"/> with its default encoder (<c>\u0022</c> and
    ///     <c>\u0027</c>, as happens when a JSON document is logged inside a string).
    /// </summary>
    [Theory]
    [InlineData("Authorization")]
    [InlineData("Proxy-Authorization")]
    [InlineData("X-NuGet-ApiKey")]
    [InlineData("Cookie")]
    [InlineData("password")]
    [InlineData("token")]
    [InlineData("access_token")]
    [InlineData("client_secret")]
    [InlineData("api-key")]
    [InlineData("sig")]
    public void SecretRedactor_Redact_JsonSerializerDefaultEscaping_RedactsHeaderAndKeyValues(string name)
    {
        // Arrange: a JSON document inside a JSON string, so the default encoder writes its quotes as \u0022
        var inner = $$"""{"{{name}}":"sentinel-value","other":1}""";
        var withSingle = $$"""{'{{name}}':'sentinel-value'}""";
        var json = JsonSerializer.Serialize(new Dictionary<string, string> { ["message"] = inner, ["single"] = withSingle });
        Assert.Contains("\\u0022", json, StringComparison.Ordinal);
        Assert.Contains("\\u0027", json, StringComparison.Ordinal);

        // Act
        var redacted = SecretRedactor.Redact(json);

        // Assert
        Assert.DoesNotContain("sentinel", redacted, StringComparison.Ordinal);
        Assert.Contains("[redacted]", redacted, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that header and key values delimited by <c>\u0022</c> or <c>\u0027</c> - written
    ///     directly, or escaped once more (<c>\\u0022</c>) - are redacted, in every position
    ///     (start of text, after a delimiter, after the opposite quote form).
    /// </summary>
    [Theory]
    [InlineData("""\u0022Authorization\u0022: \u0022Bearer sentinel-a\u0022""")]
    [InlineData("""x \u0027password\u0027=\u0027sentinel-b\u0027 y""")]
    [InlineData("""\\u0022token\\u0022:\\u0022sentinel-c\\u0022""")]
    [InlineData("""{"m":"\u0022api_key\u0022:\u0022sentinel-d\u0022,\u0022other\u0022:1"}""")]
    [InlineData("""\u0022Proxy-Authorization\u0022:\u0027Basic sentinel-e\u0027""")]
    [InlineData("""\u0027sig\u0027 : \u0027sentinel-f\u0027""")]
    [InlineData("""\\\u0022secret\\\u0022:\\\u0022sentinel-g\\\u0022""")]
    public void SecretRedactor_Redact_UnicodeEscapedQuoteDelimiters_RedactsHeaderAndKeyValues(string text)
    {
        // Act
        var redacted = SecretRedactor.Redact(text);

        // Assert: the escaped text is replaced, not merely reordered or kept alongside the placeholder
        Assert.DoesNotContain("sentinel", redacted, StringComparison.Ordinal);
        Assert.Contains("[redacted]", redacted, StringComparison.Ordinal);
        Assert.NotEqual(text, redacted);
    }

    /// <summary>
    ///     Test that header and key values survive no level of JSON nesting: a document serialized
    ///     into a string, that string serialized into another document, and so on, with the default
    ///     and the relaxed encoder.
    /// </summary>
    [Theory]
    [InlineData(false, "Authorization")]
    [InlineData(true, "Authorization")]
    [InlineData(false, "password")]
    [InlineData(true, "password")]
    [InlineData(false, "access_token")]
    [InlineData(true, "access_token")]
    public void SecretRedactor_Redact_NestedJsonText_RedactsHeaderAndKeyValues(bool relaxedEscaping, string name)
    {
        // Arrange
        var options = new JsonSerializerOptions
        {
            Encoder = relaxedEscaping ? JavaScriptEncoder.UnsafeRelaxedJsonEscaping : JavaScriptEncoder.Default
        };
        var level0 = JsonSerializer.Serialize(new Dictionary<string, string> { [name] = "sentinel-value", ["Accept"] = "*/*" }, options);
        var level1 = JsonSerializer.Serialize(new Dictionary<string, string> { ["log"] = level0 }, options);
        var level2 = JsonSerializer.Serialize(new Dictionary<string, string> { ["entry"] = level1 }, options);
        var level3 = JsonSerializer.Serialize(new Dictionary<string, string> { ["batch"] = level2 }, options);

        // Act / Assert
        foreach (var text in new[] { level0, level1, level2, level3 })
        {
            var redacted = SecretRedactor.Redact(text);
            Assert.DoesNotContain("sentinel", redacted, StringComparison.Ordinal);
            Assert.Contains("[redacted]", redacted, StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     Test that a <see cref="SanitizedException"/> redacts whatever text it is constructed
    ///     with, so the secret-free contract holds for every construction path.
    /// </summary>
    [Fact]
    public void SanitizedException_Constructor_UnredactedText_IsRedactedByTheConstructor()
    {
        // Act
        var sanitized = new SanitizedException(
            "Some.Type",
            "failed https://h.example.com/x?sig=sentinel-q with Authorization: Bearer sentinel-b",
            "at Some.Method() https://svc:sentinel-p@h.example.com/y?t=sentinel-s");

        // Assert
        var text = sanitized.Message + sanitized.StackTrace + sanitized;
        Assert.DoesNotContain("sentinel", text, StringComparison.Ordinal);
        Assert.Equal("Some.Type", sanitized.OriginalTypeName);
        Assert.Contains("https://h.example.com/x", sanitized.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that each lease withdraws exactly its own registration: disposing one lease twice
    ///     does not end redaction while another holder's lease is alive, and the secret is only
    ///     released when the last holder is done.
    /// </summary>
    [Fact]
    public void SecretRedactor_Lease_DisposedTwiceOrAlongsideOtherHolders_WithdrawsOnlyItsOwnRegistration()
    {
        // Arrange
        var secret = "sec-" + Guid.NewGuid().ToString("N");
        var credential = new NuGetCredential("u", secret);
        var first = SecretRedactor.Lease(credential);
        var second = SecretRedactor.Lease(credential);

        // Act: the first holder finishes, twice
        first.Dispose();
        first.Dispose();
        var whileSecondHolds = SecretRedactor.Redact($"value {secret} end");
        second.Dispose();
        var afterAll = SecretRedactor.Redact($"value {secret} end");

        // Assert
        Assert.Equal("value [redacted] end", whileSecondHolds);
        Assert.Equal($"value {secret} end", afterAll);
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
