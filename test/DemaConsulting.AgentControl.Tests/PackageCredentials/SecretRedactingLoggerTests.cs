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

using DemaConsulting.AgentControl.PackageCredentials;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.AgentControl.Tests.PackageCredentials;

/// <summary>
///     Unit tests for <see cref="SecretRedactingLogger"/>.
/// </summary>
public sealed class SecretRedactingLoggerTests
{
    /// <summary>
    ///     Test that a URL's user information, query string and fragment are removed while scheme,
    ///     host, port and path stay, so a log is still useful for diagnosis.
    /// </summary>
    [Theory]
    [InlineData("GET https://feeds.example.com/v3/index.json?sig=sentinel#frag", "GET https://feeds.example.com/v3/index.json")]
    [InlineData("see https://user:sentinel@feeds.example.com:8443/a/b.json.", "see https://feeds.example.com:8443/a/b.json.")]
    [InlineData("(https://h.example.com/p?token=sentinel)", "(https://h.example.com/p)")]
    public void SecretRedactingLogger_Redact_UrlsWithSecrets_KeepsOnlySchemeHostPortAndPath(string text, string expected)
    {
        // Act
        var redacted = SecretRedactingLogger.Redact(text);

        // Assert
        Assert.Equal(expected, redacted);
        Assert.DoesNotContain("sentinel", redacted, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that authorization header values are replaced by a placeholder.
    /// </summary>
    [Theory]
    [InlineData("Authorization: Basic c2VudGluZWw6cHc=")]
    [InlineData("authorization=Bearer sentinel-token-value")]
    [InlineData("sending Bearer sentinel-token-value to server")]
    public void SecretRedactingLogger_Redact_AuthorizationValues_AreReplaced(string text)
    {
        // Act
        var redacted = SecretRedactingLogger.Redact(text);

        // Assert
        Assert.DoesNotContain("c2VudGluZWw6cHc=", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("sentinel-token-value", redacted, StringComparison.Ordinal);
        Assert.Contains("[redacted]", redacted, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that text without secrets, including null, passes through unchanged.
    /// </summary>
    [Fact]
    public void SecretRedactingLogger_Redact_PlainText_IsUnchanged()
    {
        // Act / Assert
        Assert.Equal("Retrying the request in 1 second", SecretRedactingLogger.Redact("Retrying the request in 1 second"));
        Assert.Equal(string.Empty, SecretRedactingLogger.Redact(null));
    }

    /// <summary>
    ///     Test that a message and its exception reach the wrapped logger redacted, as plain text.
    /// </summary>
    [Fact]
    public void SecretRedactingLogger_Log_MessageAndExceptionWithSecrets_ForwardsRedactedText()
    {
        // Arrange
        var inner = new ListLogger();
        var logger = new SecretRedactingLogger(inner);
        var exception = new InvalidOperationException("failed https://h.example.com/x?sig=sentinel-secret");

        // Act
        logger.LogWarning(exception, "GET {Url} Authorization: Basic sentinel-header", "https://h.example.com/v3?apikey=sentinel-secret");

        // Assert
        var line = Assert.Single(inner.Lines);
        Assert.DoesNotContain("sentinel", line, StringComparison.Ordinal);
        Assert.Contains("https://h.example.com/v3", line, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", line, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that scope and level checks are delegated, and that a null inner logger is refused.
    /// </summary>
    [Fact]
    public void SecretRedactingLogger_IsEnabledAndScope_DelegatesToTheInnerLogger()
    {
        // Arrange
        var inner = new ListLogger { MinimumLevel = LogLevel.Warning };
        var logger = new SecretRedactingLogger(inner);

        // Act
        logger.LogInformation("below the minimum level");

        // Assert
        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Error));
        Assert.Empty(inner.Lines);
        Assert.Null(logger.BeginScope("scope"));
        Assert.Throws<ArgumentNullException>(() => new SecretRedactingLogger(null!));
    }
}
