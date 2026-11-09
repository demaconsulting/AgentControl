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
///     Tests of <see cref="GitTagPattern"/>: validation of a pattern, matching a tag to a strict
///     semantic version with case-sensitive literal parts, formatting a tag from a version, the
///     listing glob, and the vetting of tag names a server reports. Pure; no git is run.
/// </summary>
public sealed class GitTagPatternTests
{
    /// <summary>
    ///     Test that patterns with exactly one lower-case placeholder and legal literal text are
    ///     accepted.
    /// </summary>
    [Theory]
    [InlineData("v{version}")]
    [InlineData("release/{version}")]
    [InlineData("{version}")]
    [InlineData("{version}-final")]
    [InlineData("contoso-agents-{version}")]
    [InlineData("a/b/{version}")]
    [InlineData("v.{version}")]
    public void GitTagPattern_TryCreate_ValidPattern_IsAccepted(string text)
    {
        // Act
        var accepted = GitTagPattern.TryCreate(text, out var pattern, out var problem);

        // Assert
        Assert.True(accepted, problem);
        Assert.Null(problem);
        Assert.Equal(text, pattern!.Text);
        Assert.Null(GitTagPattern.GetProblem(text));
    }

    /// <summary>
    ///     Test that a pattern without exactly one case-sensitive placeholder, with braces, characters
    ///     git forbids in a ref name, or a form that cannot be a legal tag name is refused.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v")]
    [InlineData("v{version}{version}")]
    [InlineData("{version}-{version}")]
    [InlineData("v{Version}")]
    [InlineData("{VERSION}")]
    [InlineData("v{version")]
    [InlineData("v{{version}}")]
    [InlineData("v{version}{")]
    [InlineData("v {version}")]
    [InlineData("v~{version}")]
    [InlineData("v^{version}")]
    [InlineData("v:{version}")]
    [InlineData("v?{version}")]
    [InlineData("v*{version}")]
    [InlineData("v[{version}")]
    [InlineData("v\\{version}")]
    [InlineData("../{version}")]
    [InlineData("a//{version}")]
    [InlineData("/{version}")]
    [InlineData("{version}/")]
    [InlineData("-{version}")]
    [InlineData(".{version}")]
    [InlineData("x.lock/{version}")]
    [InlineData("{version}.lock")]
    [InlineData("{version}.")]
    [InlineData("@{version}")]
    [InlineData("\u00e9{version}")]
    [InlineData("v{version}\n")]
    public void GitTagPattern_TryCreate_InvalidPattern_IsRefused(string? text)
    {
        // Act
        var accepted = GitTagPattern.TryCreate(text, out var pattern, out var problem);

        // Assert
        Assert.False(accepted);
        Assert.Null(pattern);
        Assert.False(string.IsNullOrWhiteSpace(problem));
        Assert.Equal(problem, GitTagPattern.GetProblem(text));
    }

    /// <summary>
    ///     Test that a pattern longer than the limit is refused, and that the message never repeats
    ///     the pattern text.
    /// </summary>
    [Fact]
    public void GitTagPattern_TryCreate_TooLong_IsRefusedWithoutEchoing()
    {
        // Arrange: a pattern one character over the 200-character limit
        var text = new string('a', 192) + "{version}";
        var atLimit = new string('a', 191) + "{version}";

        // Act
        var problem = GitTagPattern.GetProblem(text);

        // Assert: refused without repeating the long text, while a pattern of exactly 200 characters is accepted
        Assert.NotNull(problem);
        Assert.DoesNotContain(text, problem, StringComparison.Ordinal);
        Assert.Equal(200, atLimit.Length);
        Assert.Null(GitTagPattern.GetProblem(atLimit));
    }

    /// <summary>
    ///     Test that the message for a secret-looking pattern never repeats the pattern text.
    /// </summary>
    [Fact]
    public void GitTagPattern_TryCreate_SecretLooking_IsRefusedWithoutEchoing()
    {
        // Arrange: a pattern that carries a secret and is refused (it contains a space)
        var text = "s3cr3t token {version}";

        // Act
        var problem = GitTagPattern.GetProblem(text);

        // Assert: refused, and neither the secret nor the complete pattern is repeated
        Assert.NotNull(problem);
        Assert.DoesNotContain("s3cr3t", problem, StringComparison.Ordinal);
        Assert.DoesNotContain(text, problem, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that tags match when they are the literal prefix, a strict semantic version and the
    ///     literal suffix, with literal parts compared case-sensitively.
    /// </summary>
    [Theory]
    [InlineData("v{version}", "v1.2.3", "1.2.3")]
    [InlineData("v{version}", "v1.2.3-beta.1", "1.2.3-beta.1")]
    [InlineData("v{version}", "v10.20.30", "10.20.30")]
    [InlineData("release/{version}", "release/1.0.0", "1.0.0")]
    [InlineData("release/{version}", "release/2.0.0-rc.1", "2.0.0-rc.1")]
    [InlineData("{version}-final", "1.2.3-final", "1.2.3")]
    [InlineData("{version}-final", "1.2.3-beta-final", "1.2.3-beta")]
    [InlineData("{version}", "1.2.3", "1.2.3")]
    public void GitTagPattern_TryMatch_VersionTag_ReturnsVersion(string patternText, string tag, string expected)
    {
        // Arrange
        Assert.True(GitTagPattern.TryCreate(patternText, out var pattern, out _));

        // Act
        var matched = pattern!.TryMatch(tag, out var version);

        // Assert
        Assert.True(matched);
        Assert.Equal(expected, version!.ToString());
        Assert.Equal(tag, pattern.Format(version));
    }

    /// <summary>
    ///     Test that tags that are not strict versions under the pattern do not match: wrong case,
    ///     missing prefix or suffix, two or four numeric parts, build metadata, and hostile names.
    /// </summary>
    [Theory]
    [InlineData("v{version}", "V1.2.3")]
    [InlineData("v{version}", "1.2.3")]
    [InlineData("v{version}", "v1.2")]
    [InlineData("v{version}", "v1.2.3.4")]
    [InlineData("v{version}", "v1.2.3+build")]
    [InlineData("v{version}", "v")]
    [InlineData("v{version}", "latest")]
    [InlineData("v{version}", "v1.2.3 ")]
    [InlineData("v{version}", "v1.2.3\n")]
    [InlineData("v{version}", "v1.2.3;rm")]
    [InlineData("v{version}", "v1.2.3/../x")]
    [InlineData("v{version}", "v-1.2.3")]
    [InlineData("Release/{version}", "release/1.0.0")]
    [InlineData("release/{version}", "release/1.0.0/extra")]
    [InlineData("{version}-final", "1.2.3")]
    [InlineData("{version}-final", "1.2.3-Final")]
    [InlineData("v{version}", null)]
    [InlineData("v{version}", "")]
    public void GitTagPattern_TryMatch_NotAVersionTag_DoesNotMatch(string patternText, string? tag)
    {
        // Arrange
        Assert.True(GitTagPattern.TryCreate(patternText, out var pattern, out _));

        // Act / Assert
        Assert.False(pattern!.TryMatch(tag, out var version));
        Assert.Null(version);
    }

    /// <summary>
    ///     Test that a tag whose version breaks strict Semantic Versioning 2.0 does not match: a
    ///     leading zero in a numeric part or in a numeric prerelease identifier, an empty
    ///     prerelease or identifier, a character outside letters, digits and hyphen in the
    ///     prerelease, a missing or extra numeric part, and build metadata.
    /// </summary>
    [Theory]
    [InlineData("v01.2.3")]
    [InlineData("v1.02.3")]
    [InlineData("v1.2.03")]
    [InlineData("v00.0.0")]
    [InlineData("v1.2.3-01")]
    [InlineData("v1.2.3-alpha.01")]
    [InlineData("v1.2.3-")]
    [InlineData("v1.2.3-.a")]
    [InlineData("v1.2.3-a..b")]
    [InlineData("v1.2.3-a_b")]
    [InlineData("v1.2.3+meta")]
    [InlineData("v1.2.3-rc.1+meta")]
    [InlineData("v1.2")]
    [InlineData("v1.2.3.4")]
    [InlineData("v1..3")]
    [InlineData("v+1.2.3")]
    public void GitTagPattern_TryMatch_NotStrictSemVer_DoesNotMatch(string tag)
    {
        // Arrange: a pattern that adds only the letter v
        Assert.True(GitTagPattern.TryCreate("v{version}", out var pattern, out _));

        // Act
        var matched = pattern!.TryMatch(tag, out var version);

        // Assert: not a package version
        Assert.False(matched);
        Assert.Null(version);
    }

    /// <summary>
    ///     Test that versions that are strict Semantic Versioning 2.0 are matched, including a zero
    ///     part, a single <c>0</c> as a numeric prerelease identifier, hyphens inside identifiers and
    ///     an alphanumeric identifier that starts with a zero.
    /// </summary>
    [Theory]
    [InlineData("v0.0.0", "0.0.0")]
    [InlineData("v1.0.0-0", "1.0.0-0")]
    [InlineData("v1.0.0-0.3.7", "1.0.0-0.3.7")]
    [InlineData("v1.0.0-x.7.z.92", "1.0.0-x.7.z.92")]
    [InlineData("v1.0.0-alpha-1", "1.0.0-alpha-1")]
    [InlineData("v1.0.0-01a", "1.0.0-01a")]
    public void GitTagPattern_TryMatch_StrictSemVer_Matches(string tag, string expected)
    {
        // Arrange
        Assert.True(GitTagPattern.TryCreate("v{version}", out var pattern, out _));

        // Act
        var matched = pattern!.TryMatch(tag, out var version);

        // Assert
        Assert.True(matched);
        Assert.Equal(expected, version!.ToString());
    }

    /// <summary>
    ///     Test that the listing glob is <c>refs/tags/</c> plus the literal text before the
    ///     placeholder plus a wildcard.
    /// </summary>
    [Theory]
    [InlineData("v{version}", "refs/tags/v*")]
    [InlineData("release/{version}", "refs/tags/release/*")]
    [InlineData("{version}-final", "refs/tags/*")]
    public void GitTagPattern_ListingGlob_UsesLiteralPrefix(string patternText, string expected)
    {
        // Arrange
        Assert.True(GitTagPattern.TryCreate(patternText, out var pattern, out _));

        // Assert
        Assert.Equal(expected, pattern!.ListingGlob);
    }

    /// <summary>
    ///     Test the vetting of a tag name a server reports before it is put on a command line.
    /// </summary>
    [Theory]
    [InlineData("v1.0.0", true)]
    [InlineData("release/1.0.0-rc.1", true)]
    [InlineData("a+b_c-d", true)]
    [InlineData("-oProxyCommand=calc", false)]
    [InlineData("--upload-pack=calc", false)]
    [InlineData("v1 0", false)]
    [InlineData("v1..0", false)]
    [InlineData("v1.", false)]
    [InlineData(".hidden", false)]
    [InlineData("a/.hidden", false)]
    [InlineData("a//b", false)]
    [InlineData("/a", false)]
    [InlineData("a/", false)]
    [InlineData("tag.lock", false)]
    [InlineData("a.lock/b", false)]
    [InlineData("v1:2", false)]
    [InlineData("v1~2", false)]
    [InlineData("v1^2", false)]
    [InlineData("v1*", false)]
    [InlineData("$(calc)", false)]
    [InlineData("v1\n", false)]
    [InlineData("\u00e9", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void GitTagPattern_IsSafeTagName_ChecksCharactersAndRefNameRules(string? name, bool expected)
    {
        // Arrange: a tag name as a server might report it

        // Act
        var safe = GitTagPattern.IsSafeTagName(name);

        // Assert
        Assert.Equal(expected, safe);
    }

    /// <summary>
    ///     Test that a tag name longer than the 255-character limit is not safe, and one of exactly
    ///     255 characters is.
    /// </summary>
    [Fact]
    public void GitTagPattern_IsSafeTagName_TooLong_IsNotSafe()
    {
        // Arrange: names on both sides of the limit
        var atLimit = new string('a', 255);
        var overLimit = new string('a', 256);

        // Act
        var atLimitSafe = GitTagPattern.IsSafeTagName(atLimit);
        var overLimitSafe = GitTagPattern.IsSafeTagName(overLimit);

        // Assert
        Assert.True(atLimitSafe);
        Assert.False(overLimitSafe);
    }

    /// <summary>
    ///     Test that a version whose tag would be exactly 255 characters is formatted, and one a
    ///     character longer has no tag, instead of producing a name that could never be fetched.
    /// </summary>
    [Fact]
    public void GitTagPattern_Format_PrereleaseAtAndOverTagLengthLimit_FormatsOrReturnsNull()
    {
        // Arrange: "v" + "1.2.3-" is 7 characters, so a 248-character prerelease makes 255
        Assert.True(GitTagPattern.TryCreate("v{version}", out var pattern, out _));
        Assert.True(PackageVersion.TryParseStrict("1.2.3-" + new string('a', 248), out var atLimit));
        Assert.True(PackageVersion.TryParseStrict("1.2.3-" + new string('a', 249), out var overLimit));

        // Act
        var formattedAtLimit = pattern!.Format(atLimit!);
        var formattedOverLimit = pattern.Format(overLimit!);

        // Assert
        Assert.Equal(255, formattedAtLimit!.Length);
        Assert.Null(formattedOverLimit);
    }

    /// <summary>
    ///     Test that a long pattern and a version together that exceed the tag-name limit have no tag,
    ///     while the same pattern with a version one character shorter does.
    /// </summary>
    [Fact]
    public void GitTagPattern_Format_LongPatternPlusVersion_FormatsAtLimitAndReturnsNullOverIt()
    {
        // Arrange: a 95-character prefix and suffix (a 199-character pattern, within its limit)
        Assert.True(GitTagPattern.TryCreate(new string('p', 95) + "{version}" + new string('s', 95), out var pattern, out _));
        Assert.True(PackageVersion.TryParseStrict("1.2.3-" + new string('a', 59), out var atLimit));
        Assert.True(PackageVersion.TryParseStrict("1.2.3-" + new string('a', 60), out var overLimit));

        // Act
        var formattedAtLimit = pattern!.Format(atLimit!);
        var formattedOverLimit = pattern.Format(overLimit!);

        // Assert: 95 + 65 + 95 = 255 characters is formatted, 256 is not
        Assert.Equal(255, formattedAtLimit!.Length);
        Assert.Null(formattedOverLimit);
    }

    /// <summary>
    ///     Test that a version this pattern would not read back as the same version has no tag: build
    ///     metadata, a fourth numeric part, and a leading zero (parsed by the looser zip-name grammar).
    /// </summary>
    [Fact]
    public void GitTagPattern_Format_VersionThatDoesNotRoundTrip_ReturnsNull()
    {
        // Arrange
        Assert.True(GitTagPattern.TryCreate("v{version}", out var pattern, out _));
        Assert.True(PackageVersion.TryParse("1.2.3+build", out var withMetadata));
        Assert.True(PackageVersion.TryParse("1.2.3.4", out var fourParts));
        Assert.True(PackageVersion.TryParseStrict("01.2.3", out var leadingZero));

        // Act
        var results = new[] { withMetadata!, fourParts!, leadingZero! }.Select(pattern!.Format).ToList();

        // Assert
        Assert.All(results, Assert.Null);
    }

    /// <summary>
    ///     Test that a null version is rejected.
    /// </summary>
    [Fact]
    public void GitTagPattern_Format_NullVersion_ThrowsArgumentNullException()
    {
        // Arrange
        Assert.True(GitTagPattern.TryCreate("v{version}", out var pattern, out _));

        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => pattern!.Format(null!));
    }
}
