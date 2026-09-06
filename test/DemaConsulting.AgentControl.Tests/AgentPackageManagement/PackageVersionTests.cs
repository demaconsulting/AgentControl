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

namespace DemaConsulting.AgentControl.Tests.AgentPackageManagement;

/// <summary>
///     Unit tests for <see cref="PackageVersion"/>.
/// </summary>
public class PackageVersionTests
{
    /// <summary>
    ///     Test that a well-formed release version string parses successfully.
    /// </summary>
    [Fact]
    public void PackageVersion_TryParse_ReleaseVersion_ParsesComponents()
    {
        // Act: parse a plain release version
        var parsed = PackageVersion.TryParse("1.2.3", out var version);

        // Assert: parsing succeeds and components match
        Assert.True(parsed);
        Assert.NotNull(version);
        Assert.Equal(1, version.Major);
        Assert.Equal(2, version.Minor);
        Assert.Equal(3, version.Patch);
        Assert.Null(version.Prerelease);
    }

    /// <summary>
    ///     Test that a well-formed prerelease version string parses successfully.
    /// </summary>
    [Fact]
    public void PackageVersion_TryParse_PrereleaseVersion_ParsesPrereleaseIdentifier()
    {
        // Act: parse a version with a prerelease suffix
        var parsed = PackageVersion.TryParse("1.0.0-beta.1", out var version);

        // Assert: parsing succeeds and the prerelease identifier is captured
        Assert.True(parsed);
        Assert.NotNull(version);
        Assert.Equal("beta.1", version.Prerelease);
    }

    /// <summary>
    ///     Test that malformed version strings fail to parse without throwing.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("a.b.c")]
    [InlineData("1.2.-")]
    [InlineData(null)]
    public void PackageVersion_TryParse_MalformedInput_ReturnsFalse(string? input)
    {
        // Act: attempt to parse malformed input
        var parsed = PackageVersion.TryParse(input, out var version);

        // Assert: parsing fails cleanly, no exception, and no version is produced
        Assert.False(parsed);
        Assert.Null(version);
    }

    /// <summary>
    ///     Test that a higher numeric version compares greater than a lower one.
    /// </summary>
    [Fact]
    public void PackageVersion_CompareTo_HigherNumericVersion_ComparesGreater()
    {
        // Arrange: two release versions
        PackageVersion.TryParse("2.0.0", out var higher);
        PackageVersion.TryParse("1.9.9", out var lower);
        Assert.NotNull(higher);
        Assert.NotNull(lower);

        // Act / Assert: the higher version compares as greater
        Assert.True(higher > lower);
        Assert.True(lower < higher);
    }

    /// <summary>
    ///     Test that a release version compares greater than a prerelease of the same numeric
    ///     version, per semver precedence.
    /// </summary>
    [Fact]
    public void PackageVersion_CompareTo_ReleaseVsPrereleaseSameNumeric_ReleaseIsGreater()
    {
        // Arrange: a release and a prerelease sharing the same major.minor.patch
        PackageVersion.TryParse("1.0.0", out var release);
        PackageVersion.TryParse("1.0.0-beta", out var prerelease);
        Assert.NotNull(release);

        // Act / Assert: the release outranks the prerelease
        Assert.True(release > prerelease);
    }

    /// <summary>
    ///     Test that two versions parsed from the same string compare equal.
    /// </summary>
    [Fact]
    public void PackageVersion_Equals_SameVersionString_ReturnsTrue()
    {
        // Arrange: two independently parsed instances of the same version
        PackageVersion.TryParse("3.4.5", out var first);
        PackageVersion.TryParse("3.4.5", out var second);
        Assert.NotNull(first);

        // Act / Assert: they compare equal via both Equals and ==
        Assert.Equal(first, second);
        Assert.True(first == second);
    }

    /// <summary>
    ///     Test that ToString round-trips through TryParse for both release and prerelease
    ///     versions.
    /// </summary>
    [Theory]
    [InlineData("1.2.3")]
    [InlineData("1.2.3-rc.2")]
    public void PackageVersion_ToString_ParsedVersion_RoundTripsThroughParse(string input)
    {
        // Arrange: parse the input
        PackageVersion.TryParse(input, out var version);
        Assert.NotNull(version);

        // Act: format it back to a string
        var formatted = version.ToString();

        // Assert: the formatted string matches the original input
        Assert.Equal(input, formatted);
    }

    /// <summary>
    ///     Test that constructing with a negative component throws an ArgumentOutOfRangeException.
    /// </summary>
    [Fact]
    public void PackageVersion_Constructor_NegativeMajor_ThrowsArgumentOutOfRangeException()
    {
        // Act / Assert: a negative major version is rejected
        Assert.Throws<ArgumentOutOfRangeException>(() => new PackageVersion(-1, 0, 0));
    }
}
