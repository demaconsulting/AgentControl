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
    ///     Test that a pattern longer than the limit is refused, and that a message never repeats the
    ///     pattern text.
    /// </summary>
    [Fact]
    public void GitTagPattern_TryCreate_TooLongOrSecretLooking_IsRefusedWithoutEchoing()
    {
        // Act / Assert
        Assert.NotNull(GitTagPattern.GetProblem(new string('a', 201) + "{version}"));
        Assert.DoesNotContain("s3cr3t", GitTagPattern.GetProblem("s3cr3t token {version}")!, StringComparison.Ordinal);
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
    public void GitTagPattern_IsSafeTagName_ChecksCharactersAndRefNameRules(string? name, bool expected) =>
        Assert.Equal(expected, GitTagPattern.IsSafeTagName(name));

    /// <summary>
    ///     Test that a tag name longer than the limit is not safe.
    /// </summary>
    [Fact]
    public void GitTagPattern_IsSafeTagName_TooLong_IsNotSafe() =>
        Assert.False(GitTagPattern.IsSafeTagName(new string('a', 256)));
}
