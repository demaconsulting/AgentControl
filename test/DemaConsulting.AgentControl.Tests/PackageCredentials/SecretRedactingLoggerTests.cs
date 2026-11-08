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
using DemaConsulting.NuGet.Caching;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.AgentControl.Tests.PackageCredentials;

/// <summary>
///     Unit tests for <see cref="SecretRedactingLogger"/>: what reaches the wrapped logger is
///     redacted - the message, every structured property value, scope state and the exception.
/// </summary>
public sealed class SecretRedactingLoggerTests
{
    /// <summary>
    ///     Test that a message and its exception reach the wrapped logger redacted.
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
    ///     Test that structured property values are redacted individually, while the template and
    ///     the names stay and plain value types keep their value, so structured sinks still work.
    /// </summary>
    [Fact]
    public void SecretRedactingLogger_Log_StructuredPropertyValues_AreRedactedAndStructureIsKept()
    {
        // Arrange
        var inner = new ListLogger();
        var logger = new SecretRedactingLogger(inner);

        // Act
        logger.LogInformation(
            "Fetched {Url} with {Header} ({Count} items) for {Feed}",
            "http://[::1]:5000/x?token=sentinel-url",
            "Authorization: Digest response=sentinel-header",
            3,
            new Uri("https://svc:sentinel-userinfo@h.example.com/v3/index.json?sig=sentinel-query"));

        // Assert: the formatted message and each property are clean; the structure is intact
        var line = Assert.Single(inner.Lines);
        Assert.DoesNotContain("sentinel", line, StringComparison.Ordinal);
        Assert.Contains("Url=http://[::1]:5000/x", line, StringComparison.Ordinal);
        Assert.Contains("Header=Authorization: [redacted]", line, StringComparison.Ordinal);
        Assert.Contains("Count=3", line, StringComparison.Ordinal);
        Assert.Contains("Feed=https://h.example.com/v3/index.json", line, StringComparison.Ordinal);
        Assert.Contains("{OriginalFormat}=Fetched {Url} with {Header} ({Count} items) for {Feed}", line, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that scope state is redacted - as text and as structured pairs - before it reaches
    ///     the wrapped logger.
    /// </summary>
    [Fact]
    public void SecretRedactingLogger_BeginScope_TextAndStructuredState_AreRedacted()
    {
        // Arrange
        var inner = new ListLogger();
        var logger = new SecretRedactingLogger(inner);

        // Act
        logger.BeginScope("operation https://h.example.com/x?sig=sentinel-text Authorization: NTLM sentinel-ntlm");
        logger.BeginScope(new Dictionary<string, object?>
        {
            ["Request"] = "http://[::1]:5000/y?token=sentinel-pair",
            ["Header"] = "Proxy-Authorization: Basic sentinel-proxy",
            ["Attempt"] = 2
        });

        // Assert
        Assert.Equal(2, inner.Scopes.Count);
        Assert.All(inner.Scopes, scope => Assert.DoesNotContain("sentinel", scope, StringComparison.Ordinal));
        Assert.Contains("https://h.example.com/x", inner.Scopes[0], StringComparison.Ordinal);
        Assert.Contains("Request=http://[::1]:5000/y", inner.Scopes[1], StringComparison.Ordinal);
        Assert.Contains("Attempt=2", inner.Scopes[1], StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a registered password is removed wherever it appears in what is logged - the
    ///     message text, a property value and an exception message (also in an inner exception) -
    ///     even though no pattern matches it.
    /// </summary>
    [Fact]
    public void SecretRedactingLogger_Log_RegisteredPasswordInArbitraryTextPropertiesAndExceptions_IsRemoved()
    {
        // Arrange
        var password = "sec-" + Guid.NewGuid().ToString("N");
        var credential = new NuGetCredential("svc-agents", password);
        var inner = new ListLogger();
        var logger = new SecretRedactingLogger(inner);
        var exception = new InvalidOperationException(
            $"outer says {password}", new IOException($"inner says {password} too"));
        SecretRedactor.Register(credential);

        try
        {
            // Act
            logger.LogError(exception, "the user typed {Typed} into the box and {Value}", password, $"value {password}");
            logger.LogInformation("and {Value} again", password);
            logger.BeginScope($"scope {password}");

            // Assert
            Assert.Equal(2, inner.Lines.Count);
            Assert.All(inner.Lines, line => Assert.DoesNotContain(password, line, StringComparison.Ordinal));
            Assert.All(inner.Scopes, scope => Assert.DoesNotContain(password, scope, StringComparison.Ordinal));
            Assert.Contains("[redacted]", inner.Lines[0], StringComparison.Ordinal);
            Assert.Contains("outer says [redacted]", inner.Lines[0], StringComparison.Ordinal);
            Assert.Contains("inner says [redacted] too", inner.Lines[0], StringComparison.Ordinal);
        }
        finally
        {
            SecretRedactor.Unregister(credential);
        }
    }

    /// <summary>
    ///     Test that what reaches the wrapped logger is a sanitized stand-in: the original exception
    ///     object, with its inner exceptions and data, is not forwarded.
    /// </summary>
    [Fact]
    public void SecretRedactingLogger_Log_Exception_IsForwardedAsASanitizedStandIn()
    {
        // Arrange
        var capture = new ExceptionCapturingLogger();
        var logger = new SecretRedactingLogger(capture);
        var original = new InvalidOperationException("boom Authorization: Bearer sentinel-token", new IOException("io"));

        // Act
        logger.LogError(original, "failed");

        // Assert
        var forwarded = Assert.IsType<SanitizedException>(capture.Exception);
        Assert.NotSame(original, forwarded);
        Assert.Null(forwarded.InnerException);
        Assert.DoesNotContain("sentinel", forwarded.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that level checks are delegated, below-minimum messages are dropped, and a null
    ///     inner logger is refused.
    /// </summary>
    [Fact]
    public void SecretRedactingLogger_IsEnabled_DelegatesToTheInnerLogger()
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
        Assert.Throws<ArgumentNullException>(() => new SecretRedactingLogger(null!));
    }

    /// <summary>
    ///     A logger that remembers the exception it was given.
    /// </summary>
    private sealed class ExceptionCapturingLogger : ILogger
    {
        /// <summary>
        ///     Gets the exception of the last message.
        /// </summary>
        public Exception? Exception { get; private set; }

        /// <inheritdoc />
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Exception = exception;
    }
}
