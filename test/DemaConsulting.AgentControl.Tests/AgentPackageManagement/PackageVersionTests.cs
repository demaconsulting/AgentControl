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
    [InlineData("1.2.3.4.5")]
    [InlineData("1.2.3.")]
    [InlineData("1.2.3.x")]
    [InlineData("a.b.c")]
    [InlineData("1.2.-")]
    [InlineData("1.2.3+")]
    [InlineData("1.2.3+a..b")]
    [InlineData("1.2.3+a_b")]
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
    ///     Test that an optional fourth numeric segment is parsed into the revision, and that its
    ///     presence ranks the version after the same three-part version.
    /// </summary>
    [Fact]
    public void PackageVersion_TryParse_FourPartVersion_ParsesRevisionAndOrdersAfterThreePart()
    {
        // Act: parse a four-part version and its three-part base
        Assert.True(PackageVersion.TryParse("1.2.3.4", out var fourPart));
        Assert.True(PackageVersion.TryParse("1.2.3", out var threePart));
        Assert.NotNull(fourPart);
        Assert.NotNull(threePart);

        // Assert: the revision is captured, defaults to zero when absent, and orders after patch
        Assert.Equal(4, fourPart.Revision);
        Assert.Equal(0, threePart.Revision);
        Assert.True(fourPart > threePart);
        Assert.Equal("1.2.3.4", fourPart.ToString());
    }

    /// <summary>
    ///     Test that a zero fourth segment is the same version as the three-part form, as NuGet
    ///     treats <c>1.0.0.0</c> and <c>1.0.0</c>.
    /// </summary>
    [Fact]
    public void PackageVersion_Equals_ZeroRevisionAndThreePart_AreEqualWithSameHashCode()
    {
        // Arrange
        Assert.True(PackageVersion.TryParse("1.0.0.0", out var fourPart));
        Assert.True(PackageVersion.TryParse("1.0.0", out var threePart));
        Assert.NotNull(fourPart);

        // Act / Assert: equal, with matching hash codes, but each keeps its own original text
        Assert.NotNull(threePart);
        Assert.Equal(threePart, fourPart);
        Assert.Equal(threePart.GetHashCode(), fourPart.GetHashCode());
        Assert.Equal("1.0.0.0", fourPart.Original);
    }

    /// <summary>
    ///     Test that build metadata is captured and preserved in the original text, but ignored by
    ///     comparison, equality and hashing.
    /// </summary>
    [Fact]
    public void PackageVersion_TryParse_BuildMetadata_PreservedButIgnoredInComparison()
    {
        // Arrange: the same version with different metadata, and one without
        Assert.True(PackageVersion.TryParse("1.2.3+build.5", out var withMetadata));
        Assert.True(PackageVersion.TryParse("1.2.3+other", out var otherMetadata));
        Assert.True(PackageVersion.TryParse("1.2.3", out var plain));
        Assert.NotNull(withMetadata);
        Assert.NotNull(otherMetadata);

        // Assert: metadata and original text are kept, yet all three are the same version
        Assert.Equal("build.5", withMetadata.Metadata);
        Assert.Equal("1.2.3+build.5", withMetadata.Original);
        Assert.Equal("1.2.3+build.5", withMetadata.ToString());
        Assert.Equal(0, withMetadata.CompareTo(plain));
        Assert.Equal(withMetadata, otherMetadata);
        Assert.Equal(withMetadata.GetHashCode(), otherMetadata.GetHashCode());
    }

    /// <summary>
    ///     Test that prerelease, revision and metadata combine, with the prerelease still ranking
    ///     below the release.
    /// </summary>
    [Fact]
    public void PackageVersion_TryParse_PrereleaseRevisionAndMetadata_AllParsed()
    {
        // Act
        Assert.True(PackageVersion.TryParse("1.2.3.4-beta.1+sha.abc", out var version));
        Assert.True(PackageVersion.TryParse("1.2.3.4", out var release));

        // Assert
        Assert.NotNull(version);
        Assert.Equal(4, version.Revision);
        Assert.Equal("beta.1", version.Prerelease);
        Assert.Equal("sha.abc", version.Metadata);
        Assert.True(version < release);
    }

    /// <summary>
    ///     Test that the strict parser used for zip file names still rejects the extended forms
    ///     (a fourth segment or build metadata), keeping zip name/version splitting unchanged.
    /// </summary>
    [Theory]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.3+build")]
    [InlineData("1.2.3-beta+build")]
    [InlineData("1.2")]
    [InlineData("")]
    [InlineData(null)]
    public void PackageVersion_TryParseStrict_ExtendedOrMalformedForms_ReturnsFalse(string? input)
    {
        // Act
        var parsed = PackageVersion.TryParseStrict(input, out var version);

        // Assert: strict parsing accepts only MAJOR.MINOR.PATCH[-PRERELEASE]
        Assert.False(parsed);
        Assert.Null(version);
    }

    /// <summary>
    ///     Test that the strict parser accepts the plain release and prerelease forms.
    /// </summary>
    [Theory]
    [InlineData("1.2.3")]
    [InlineData("1.2.3-beta.1")]
    public void PackageVersion_TryParseStrict_ThreePartForms_ReturnsTrue(string input)
    {
        // Act
        var parsed = PackageVersion.TryParseStrict(input, out var version);

        // Assert
        Assert.True(parsed);
        Assert.NotNull(version);
        Assert.Equal(input, version.ToString());
    }

    /// <summary>
    ///     Parity test: for a range of versions (including four-part, prerelease, and metadata
    ///     forms) the comparison and equality results agree with
    ///     <see cref="global::NuGet.Versioning.NuGetVersion"/>, so ordering a NuGet source's versions
    ///     here picks the same "latest" NuGet would.
    /// </summary>
    /// <remarks>
    ///     Prerelease labels differing only by letter case are deliberately excluded: this type
    ///     follows semver (case-sensitive) while NuGet compares labels case-insensitively.
    /// </remarks>
    [Fact]
    public void PackageVersion_CompareTo_VersionsAgreeWithNuGetVersionOrdering()
    {
        // Arrange: a spread of versions exercising every precedence rule
        string[] versions =
        [
            "0.0.1", "0.1.0", "1.0.0", "1.0.0.0", "1.0.0.1", "1.0.1", "1.0.0-alpha", "1.0.0-alpha.1",
            "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0+build1",
            "1.0.0+build2", "1.0.0-beta+meta", "1.2.3", "1.2.3.4", "2.0.0", "10.0.0", "2.0.0-rc.1", "1.10.0",
            "1.9.0"
        ];

        // Act / Assert: every ordered pair compares with the same sign as NuGet's own ordering
        foreach (var left in versions)
        {
            foreach (var right in versions)
            {
                Assert.True(PackageVersion.TryParse(left, out var ours1));
                Assert.True(PackageVersion.TryParse(right, out var ours2));
                var nuget1 = global::NuGet.Versioning.NuGetVersion.Parse(left);
                var nuget2 = global::NuGet.Versioning.NuGetVersion.Parse(right);
                Assert.Equal(
                    Math.Sign(nuget1.CompareTo(nuget2)),
                    Math.Sign(ours1!.CompareTo(ours2)));
                Assert.Equal(nuget1.Equals(nuget2), ours1.Equals(ours2));
            }
        }
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
