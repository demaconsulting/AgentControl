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

namespace DemaConsulting.AgentControl.Tests.PackageCredentials;

/// <summary>
///     Unit tests for <see cref="FeedOrigin"/>.
/// </summary>
public sealed class FeedOriginTests
{
    /// <summary>
    ///     Test that an origin keeps only scheme, host and port, and drops the path, query string
    ///     and fragment.
    /// </summary>
    [Fact]
    public void FeedOrigin_TryParse_FullFeedUrl_KeepsSchemeHostAndPortOnly()
    {
        // Act
        var parsed = FeedOrigin.TryParse("https://Feeds.Example.com:8443/artifactory/api/nuget/v3/repo/index.json", out var origin);

        // Assert
        Assert.True(parsed);
        Assert.NotNull(origin);
        Assert.Equal("https", origin.Scheme);
        Assert.Equal("feeds.example.com", origin.Host);
        Assert.Equal(8443, origin.Port);
        Assert.Equal("https://feeds.example.com:8443", origin.ToString());
        Assert.True(origin.IsSecure);
    }

    /// <summary>
    ///     Test that the default port is omitted from the text form and that an explicit default
    ///     port compares equal to none.
    /// </summary>
    [Fact]
    public void FeedOrigin_Equals_ExplicitDefaultPort_EqualsOriginWithoutPort()
    {
        // Arrange
        var implicitPort = FeedOrigin.Create(new Uri("https://feeds.example.com/v3/index.json"));
        var explicitPort = FeedOrigin.Create(new Uri("HTTPS://FEEDS.example.com:443/other/index.json"));

        // Assert
        Assert.Equal(implicitPort, explicitPort);
        Assert.Equal(implicitPort.GetHashCode(), explicitPort.GetHashCode());
        Assert.Equal("https://feeds.example.com", implicitPort.ToString());
    }

    /// <summary>
    ///     Test that a URL with a user name or password is not an origin, because a feed must not
    ///     be addressed with an embedded credential.
    /// </summary>
    [Theory]
    [InlineData("https://user:pw@feeds.example.com/v3/index.json")]
    [InlineData("https://token@feeds.example.com/v3/index.json")]
    public void FeedOrigin_TryParse_UserInfo_IsRefused(string url)
    {
        // Act
        var parsed = FeedOrigin.TryParse(url, out var origin);

        // Assert
        Assert.False(parsed);
        Assert.Null(origin);
    }

    /// <summary>
    ///     Test that an absolute https address that really carries user information - built from
    ///     its parts, so the test does not depend on how a literal is written - is refused and the
    ///     refusal never repeats the user name or password.
    /// </summary>
    [Fact]
    public void FeedOrigin_TryParse_UserInfoOnAbsoluteUri_IsRefusedWithoutRepeatingIt()
    {
        // Arrange: the precondition is asserted, so the test cannot pass on a malformed address
        var address = new UriBuilder("https", "feeds.example.com")
        {
            UserName = "svc-user",
            Password = "sentinel-pw-123",
            Path = "/v3/index.json"
        }.Uri;
        Assert.True(address.IsAbsoluteUri);
        Assert.Equal("svc-user:sentinel-pw-123", address.UserInfo);

        // Act
        var parsed = FeedOrigin.TryParse(address.OriginalString, out var origin);
        var ex = Assert.Throws<ArgumentException>(() => FeedOrigin.Create(address));

        // Assert
        Assert.False(parsed);
        Assert.Null(origin);
        Assert.NotEmpty(ex.Message);
        Assert.DoesNotContain("sentinel-pw-123", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("svc-user", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that blank, relative, non-web and malformed text is not an origin.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("feeds/index.json")]
    [InlineData(@"C:\feeds")]
    [InlineData("ftp://feeds.example.com/index.json")]
    [InlineData("file:///C:/feeds")]
    public void FeedOrigin_TryParse_NotAWebAddress_IsRefused(string? url)
    {
        // Act
        var parsed = FeedOrigin.TryParse(url, out var origin);

        // Assert
        Assert.False(parsed);
        Assert.Null(origin);
    }

    /// <summary>
    ///     Test that Create refuses a credential-bearing URI without repeating it in the message.
    /// </summary>
    [Fact]
    public void FeedOrigin_Create_UserInfo_ThrowsWithoutRepeatingTheSecret()
    {
        // Act
        var ex = Assert.Throws<ArgumentException>(
            () => FeedOrigin.Create(new Uri("https://svc:sentinel-pw-123@feeds.example.com/index.json")));

        // Assert
        Assert.DoesNotContain("sentinel-pw-123", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that Matches accepts any URL of the same origin and refuses other hosts, ports and
    ///     schemes - the scoping rule that keeps a credential from leaking to a redirect target.
    /// </summary>
    [Theory]
    [InlineData("https://feeds.example.com/v3/flat/pkg/index.json", true)]
    [InlineData("https://FEEDS.example.com:443/anything?query=1", true)]
    [InlineData("https://evil.example.com/v3/index.json", false)]
    [InlineData("https://feeds.example.com.evil.com/v3/index.json", false)]
    [InlineData("https://feeds.example.com:8443/v3/index.json", false)]
    [InlineData("http://feeds.example.com/v3/index.json", false)]
    [InlineData("https://sub.feeds.example.com/v3/index.json", false)]
    public void FeedOrigin_Matches_VariousUris_OnlyTheSameOriginMatches(string uri, bool expected)
    {
        // Arrange
        var origin = FeedOrigin.Create(new Uri("https://feeds.example.com/v3/index.json"));

        // Act / Assert
        Assert.Equal(expected, origin.Matches(new Uri(uri)));
    }

    /// <summary>
    ///     Test that Matches never matches null or a relative URI.
    /// </summary>
    [Fact]
    public void FeedOrigin_Matches_NullOrRelativeUri_ReturnsFalse()
    {
        // Arrange
        var origin = FeedOrigin.Create(new Uri("https://feeds.example.com/v3/index.json"));

        // Act / Assert
        Assert.False(origin.Matches(null));
        Assert.False(origin.Matches(new Uri("/v3/index.json", UriKind.Relative)));
    }

    /// <summary>
    ///     Test that an IPv6 literal host is shown in brackets and loopback origins on different
    ///     ports are different origins.
    /// </summary>
    [Fact]
    public void FeedOrigin_ToString_Ipv6AndPorts_BracketsHostAndKeepsPorts()
    {
        // Arrange
        var ipv6 = FeedOrigin.Create(new Uri("http://[::1]:5000/v3/index.json"));
        var a = FeedOrigin.Create(new Uri("http://127.0.0.1:5000/v3/index.json"));
        var b = FeedOrigin.Create(new Uri("http://127.0.0.1:5001/v3/index.json"));

        // Assert
        Assert.Equal("http://[::1]:5000", ipv6.ToString());
        Assert.False(a.IsSecure);
        Assert.NotEqual(a, b);
        Assert.False(a.Equals((object?)null));
    }
}
