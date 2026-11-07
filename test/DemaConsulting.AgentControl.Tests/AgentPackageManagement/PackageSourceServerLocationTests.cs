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

using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.PackageCredentials;

namespace DemaConsulting.AgentControl.Tests.AgentPackageManagement;

/// <summary>
///     Unit tests for the server-feed validation of <see cref="PackageSourceDefinition"/>
///     (<see cref="PackageSourceDefinition.GetServerLocationProblem"/>) and for
///     <see cref="PackageSourceAccessException"/>.
/// </summary>
public class PackageSourceServerLocationTests
{
    /// <summary>
    ///     Test that acceptable feed addresses have no problem: https, and http only with the
    ///     explicit opt-in.
    /// </summary>
    [Theory]
    [InlineData("https://feeds.example.com/artifactory/api/nuget/v3/agents/index.json", false)]
    [InlineData("https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json", false)]
    [InlineData("HTTPS://FEEDS.EXAMPLE.COM/V3/INDEX.JSON", false)]
    [InlineData("  https://feeds.example.com/v3/index.json  ", false)]
    [InlineData("http://127.0.0.1:5000/v3/index.json", true)]
    public void PackageSourceDefinition_GetServerLocationProblem_AcceptableAddress_ReturnsNull(string location, bool allowHttp)
    {
        // Arrange
        var definition = Server(location, allowHttp);

        // Act / Assert
        Assert.Null(definition.GetServerLocationProblem());
    }

    /// <summary>
    ///     Test that each way an address can be unacceptable gets its own clear message, and no
    ///     message repeats the address.
    /// </summary>
    [Theory]
    [InlineData("", "Enter the address")]
    [InlineData("   ", "Enter the address")]
    [InlineData("feeds/index.json", "web address")]
    [InlineData("C:\\feeds", "web address")]
    [InlineData("ftp://feeds.example.com/index.json", "web address")]
    [InlineData("https://feeds.example.com/v3/", "must end in index.json")]
    [InlineData("https://feeds.example.com/v3/index.json/", "must end in index.json")]
    [InlineData("https://feeds.example.com/v3/index.json?x=1", "must not contain")]
    [InlineData("https://feeds.example.com/v3/index.json#x", "must not contain")]
    [InlineData("https://u:p@feeds.example.com/v3/index.json", "must not contain")]
    [InlineData("http://feeds.example.com/v3/index.json", "Plain http")]
    public void PackageSourceDefinition_GetServerLocationProblem_UnacceptableAddress_ReturnsClearMessage(
        string location, string expected)
    {
        // Arrange
        var definition = Server(location, allowHttp: false);

        // Act
        var problem = definition.GetServerLocationProblem();

        // Assert
        Assert.NotNull(problem);
        Assert.Contains(expected, problem, StringComparison.Ordinal);
        if (location.Trim().Length > 0)
        {
            Assert.DoesNotContain("feeds.example.com", problem, StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     Test that the access exception carries its cause and message and is an
    ///     <see cref="InvalidOperationException"/>, so existing handlers keep working.
    /// </summary>
    [Fact]
    public void PackageSourceAccessException_Constructor_CarriesKindMessageAndInnerException()
    {
        // Arrange
        var inner = new InvalidOperationException("inner detail");

        // Act
        var exception = new PackageSourceAccessException(PackageSourceFailureKind.TimedOut, "took too long", inner);

        // Assert
        Assert.Equal(PackageSourceFailureKind.TimedOut, exception.Kind);
        Assert.Equal("took too long", exception.Message);

        // Only a sanitized description of the cause is kept, never the original object
        var cause = Assert.IsType<SanitizedException>(exception.InnerException);
        Assert.NotSame(inner, cause);
        Assert.Contains("inner detail", cause.Message, StringComparison.Ordinal);
        Assert.IsAssignableFrom<InvalidOperationException>(exception);
    }

    /// <summary>
    ///     Creates a NuGet source definition with a location.
    /// </summary>
    /// <param name="location">The location.</param>
    /// <param name="allowHttp">Whether plain http is allowed.</param>
    /// <returns>The definition.</returns>
    private static PackageSourceDefinition Server(string location, bool allowHttp) =>
        new()
        {
            Alias = "Feed",
            Type = PackageSourceType.NuGet,
            Location = location,
            AllowInsecureHttp = allowHttp
        };
}
