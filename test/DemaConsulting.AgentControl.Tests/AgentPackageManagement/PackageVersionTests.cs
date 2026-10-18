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
        Assert.True(PackageVersion.TryParse("2.0.0", out var higher));
        Assert.True(PackageVersion.TryParse("1.9.9", out var lower));
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
        Assert.True(PackageVersion.TryParse("1.0.0", out var release));
        Assert.True(PackageVersion.TryParse("1.0.0-beta", out var prerelease));
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
        Assert.True(PackageVersion.TryParse("3.4.5", out var first));
        Assert.True(PackageVersion.TryParse("3.4.5", out var second));
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
        Assert.True(PackageVersion.TryParse(input, out var version));
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

    /// <summary>
    ///     Test that negative minor and patch components are rejected.
    /// </summary>
    [Fact]
    public void PackageVersion_Constructor_NegativeMinorOrPatch_ThrowsArgumentOutOfRangeException()
    {
        // Act / Assert: each negative component is rejected
        Assert.Throws<ArgumentOutOfRangeException>(() => new PackageVersion(0, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PackageVersion(0, 0, -1));
    }

    /// <summary>
    ///     Test that a trailing hyphen with no prerelease identifier fails to parse.
    /// </summary>
    [Fact]
    public void PackageVersion_TryParse_EmptyPrerelease_ReturnsFalse()
    {
        // Act / Assert: empty prerelease is invalid
        Assert.False(PackageVersion.TryParse("1.2.3-", out var version));
        Assert.Null(version);
    }

    /// <summary>
    ///     Test that comparison follows semver precedence for all components and prereleases.
    /// </summary>
    [Fact]
    public void PackageVersion_CompareTo_VariousVersions_FollowsPrecedence()
    {
        // Arrange
        var release = new PackageVersion(1, 2, 3);
        var beta = new PackageVersion(1, 2, 3, "beta");
        var alpha = new PackageVersion(1, 2, 3, "alpha");

        // Assert: null, minor, patch, and prerelease ordering
        Assert.True(release.CompareTo(null) > 0);
        Assert.True(release.CompareTo(new PackageVersion(1, 3, 0)) < 0);
        Assert.True(release.CompareTo(new PackageVersion(1, 2, 4)) < 0);
        Assert.True(new PackageVersion(1, 2, 4).CompareTo(release) > 0);
        Assert.True(release.CompareTo(beta) > 0);
        Assert.True(beta.CompareTo(release) < 0);
        Assert.True(beta.CompareTo(alpha) > 0);
    }

    /// <summary>
    ///     Test that prerelease precedence follows the semver 2.0.0 specification's canonical
    ///     ordering, including numeric (not lexical) comparison of numeric identifiers.
    /// </summary>
    [Fact]
    public void PackageVersion_CompareTo_PrereleaseIdentifiers_FollowSemverPrecedence()
    {
        // Arrange: the ascending example chain from the semver 2.0.0 specification
        string[] ascending =
        [
            "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta",
            "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0"
        ];
        var versions = ascending.Select(text =>
        {
            Assert.True(PackageVersion.TryParse(text, out var parsed));
            return parsed!;
        }).ToList();

        // Assert: every earlier entry precedes every later one
        for (var i = 0; i < versions.Count; i++)
        {
            for (var j = i + 1; j < versions.Count; j++)
            {
                Assert.True(versions[i].CompareTo(versions[j]) < 0, $"{ascending[i]} < {ascending[j]}");
                Assert.True(versions[j].CompareTo(versions[i]) > 0, $"{ascending[j]} > {ascending[i]}");
            }
        }
    }

    /// <summary>
    ///     Test that very large numeric identifiers compare numerically without overflowing.
    /// </summary>
    [Fact]
    public void PackageVersion_CompareTo_HugeNumericIdentifiers_ComparesNumerically()
    {
        // Arrange: identifiers larger than any integer type
        var small = new PackageVersion(1, 0, 0, "rc.99999999999999999999");
        var large = new PackageVersion(1, 0, 0, "rc.100000000000000000000");

        // Assert
        Assert.True(small.CompareTo(large) < 0);
        Assert.True(large.CompareTo(small) > 0);
    }

    /// <summary>
    ///     Test that prerelease strings that are not valid semver fail to parse.
    /// </summary>
    [Theory]
    [InlineData("1.2.3-beta..1")]
    [InlineData("1.2.3-.beta")]
    [InlineData("1.2.3-beta.")]
    [InlineData("1.2.3-beta.01")]
    [InlineData("1.2.3-be_ta")]
    public void PackageVersion_TryParse_InvalidPrereleaseIdentifiers_ReturnsFalse(string input)
    {
        // Act / Assert
        Assert.False(PackageVersion.TryParse(input, out var version));
        Assert.Null(version);
    }

    /// <summary>
    ///     Test that versions differing only in numeric-identifier leading zeros are equal and
    ///     share a hash code, keeping hash-based collections consistent with equality.
    /// </summary>
    [Fact]
    public void PackageVersion_Equals_LeadingZeroNumericIdentifier_EqualVersionsShareHashCode()
    {
        // Arrange: the constructor accepts the non-canonical form
        var canonical = new PackageVersion(1, 0, 0, "rc.1");
        var padded = new PackageVersion(1, 0, 0, "rc.01");

        // Assert
        Assert.True(canonical.Equals(padded));
        Assert.Equal(canonical.GetHashCode(), padded.GetHashCode());
    }

    /// <summary>
    ///     Test equality, hash code, and comparison operators.
    /// </summary>
    [Fact]
    public void PackageVersion_Operators_ComparisonAndEquality_Work()
    {
        // Arrange
        var low = new PackageVersion(1, 0, 0);
        var high = new PackageVersion(2, 0, 0);
        var lowCopy = new PackageVersion(1, 0, 0);

        // Assert
        Assert.True(low == lowCopy);
        Assert.True(low != high);
        Assert.False(low != lowCopy);
        Assert.True(low.Equals((object)lowCopy));
        Assert.Equal(low.GetHashCode(), lowCopy.GetHashCode());
        Assert.True(low < high);
        Assert.True(low <= lowCopy);
        Assert.True(high > low);
        Assert.True(high >= lowCopy);
        Assert.Throws<ArgumentNullException>(() => (null as PackageVersion)! <= low);
        Assert.Throws<ArgumentNullException>(() => (null as PackageVersion)! >= low);
    }
}
