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
using DemaConsulting.AgentControl.AgentPackageManagement;

namespace DemaConsulting.AgentControl.Tests.AgentPackageManagement;

/// <summary>
///     Unit tests for <see cref="PackageContentIdentity"/>.
/// </summary>
public class PackageContentIdentityTests
{
    /// <summary>
    ///     The SHA-256 of the ASCII text <c>abc</c>, a published test vector.
    /// </summary>
    private const string AbcSha256 = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    /// <summary>
    ///     Test that hashing a stream yields the <c>sha256:</c> identity of its bytes, matching a
    ///     published test vector, and leaves the stream open.
    /// </summary>
    [Fact]
    public void PackageContentIdentity_ComputeSha256_KnownContent_ReturnsPrefixedLowerCaseHash()
    {
        // Arrange: a stream holding the bytes of "abc"
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("abc"));

        // Act
        var identity = PackageContentIdentity.ComputeSha256(stream);

        // Assert: the published SHA-256 vector, formatted as sha256:{lower-case hex}, and the stream is still usable
        Assert.Equal("sha256:" + AbcSha256, identity);
        Assert.True(stream.CanRead);
    }

    /// <summary>
    ///     Test that a null stream is rejected.
    /// </summary>
    [Fact]
    public void PackageContentIdentity_ComputeSha256_NullStream_ThrowsArgumentNullException()
    {
        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => PackageContentIdentity.ComputeSha256(null!));
    }

    /// <summary>
    ///     Test that a commit hash is formatted as a lower-case <c>git-commit:</c> identity, for
    ///     both object-format widths.
    /// </summary>
    [Theory]
    [InlineData("0123456789ABCDEF0123456789abcdef01234567")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public void PackageContentIdentity_FromGitCommit_ValidHash_ReturnsLowerCasePrefixedIdentity(string commit)
    {
        // Act
        var identity = PackageContentIdentity.FromGitCommit(commit);

        // Assert
        Assert.Equal("git-commit:" + commit.ToLowerInvariant(), identity);
    }

    /// <summary>
    ///     Test that a malformed commit hash is rejected rather than formatted into an identity.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("abc123")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    [InlineData("0123456789abcdef0123456789abcdef012345678")]
    public void PackageContentIdentity_FromGitCommit_MalformedHash_ThrowsArgumentException(string commit)
    {
        // Act / Assert
        Assert.Throws<ArgumentException>(() => PackageContentIdentity.FromGitCommit(commit));
    }

    /// <summary>
    ///     Test the comparison outcomes: nothing to compare when either side is missing, same when
    ///     equal (ignoring case), changed when different or of a different kind.
    /// </summary>
    [Theory]
    [InlineData(null, "sha256:aa", "NotComparable")]
    [InlineData("", "sha256:aa", "NotComparable")]
    [InlineData("  ", "sha256:aa", "NotComparable")]
    [InlineData("sha256:aa", null, "NotComparable")]
    [InlineData(null, null, "NotComparable")]
    [InlineData("sha256:aa", "sha256:aa", "Same")]
    [InlineData("sha256:AA", "sha256:aa", "Same")]
    [InlineData("sha256:aa", "sha256:bb", "Changed")]
    [InlineData("sha256:aa", "git-commit:aa", "Changed")]
    public void PackageContentIdentity_Compare_PinnedAndActual_ReturnsExpectedOutcome(
        string? pinned, string? actual, string expected)
    {
        // Act
        var outcome = PackageContentIdentity.Compare(pinned, actual);

        // Assert
        Assert.Equal(expected, outcome.ToString());
    }

    /// <summary>
    ///     Test that the short description cuts the value so two identities can be told apart
    ///     without printing a whole hash, and reports "none" when no identity is known.
    /// </summary>
    [Fact]
    public void PackageContentIdentity_Describe_LongAndMissingIdentities_ReturnsShortForm()
    {
        // Arrange
        var identity = "sha256:" + AbcSha256;

        // Act / Assert
        Assert.Equal("sha256:ba7816bf8f01", PackageContentIdentity.Describe(identity));
        Assert.Equal("git-commit:abc", PackageContentIdentity.Describe("git-commit:abc"));
        Assert.Equal("none", PackageContentIdentity.Describe(null));
        Assert.Equal("none", PackageContentIdentity.Describe(" "));
    }
}
