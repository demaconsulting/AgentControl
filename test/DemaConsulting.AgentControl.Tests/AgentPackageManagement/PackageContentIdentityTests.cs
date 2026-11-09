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

// cspell:ignore xabc ghij
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
    ///     published test vector, written as lower-case hexadecimal after the algorithm prefix.
    /// </summary>
    [Fact]
    public void PackageContentIdentity_ComputeSha256_KnownContent_ReturnsPrefixedLowerCaseHash()
    {
        // Arrange: a stream holding the bytes of "abc"
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("abc"));

        // Act
        var identity = PackageContentIdentity.ComputeSha256(stream);

        // Assert: the published SHA-256 vector, formatted as sha256:{lower-case hex}
        Assert.Equal("sha256:" + AbcSha256, identity);
        Assert.Equal(identity.ToLowerInvariant(), identity);
    }

    /// <summary>
    ///     Test that hashing leaves the stream open and positioned at its end, and that only the
    ///     bytes from the stream's current position are hashed, so a caller that must read the same
    ///     handle afterwards knows to rewind it.
    /// </summary>
    [Fact]
    public void PackageContentIdentity_ComputeSha256_Stream_StaysOpenAtEndAndHashesFromCurrentPosition()
    {
        // Arrange: "xabc" with the position after the first byte, so only "abc" is hashed
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("xabc"));
        stream.Position = 1;

        // Act
        var identity = PackageContentIdentity.ComputeSha256(stream);

        // Assert: the identity of "abc", the stream is still open, and its position is at the end
        Assert.Equal("sha256:" + AbcSha256, identity);
        Assert.True(stream.CanRead);
        Assert.Equal(stream.Length, stream.Position);
    }

    /// <summary>
    ///     Test that a null stream is rejected.
    /// </summary>
    [Fact]
    public void PackageContentIdentity_ComputeSha256_NullStream_ThrowsArgumentNullException()
    {
        // Arrange: no stream

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
        // Arrange: a commit hash of either width, in either case

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
        // Arrange: a value that is not 40 or 64 hexadecimal digits

        // Act / Assert
        Assert.Throws<ArgumentException>(() => PackageContentIdentity.FromGitCommit(commit));
    }

    /// <summary>
    ///     Test that a null commit hash is rejected with the null-specific exception.
    /// </summary>
    [Fact]
    public void PackageContentIdentity_FromGitCommit_NullCommit_ThrowsArgumentNullException()
    {
        // Arrange: no commit hash

        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => PackageContentIdentity.FromGitCommit(null!));
    }

    /// <summary>
    ///     Test that nothing is compared, so there is never a false alarm, when either identity is
    ///     missing.
    /// </summary>
    [Theory]
    [InlineData(null, "sha256:aa")]
    [InlineData("", "sha256:aa")]
    [InlineData("  ", "sha256:aa")]
    [InlineData("sha256:aa", null)]
    [InlineData("sha256:aa", "")]
    [InlineData(null, null)]
    public void PackageContentIdentity_Compare_EitherIdentityMissing_IsNotComparable(string? pinned, string? actual)
    {
        // Arrange: a pin or a source that has no identity

        // Act
        var outcome = PackageContentIdentity.Compare(pinned, actual);

        // Assert
        Assert.Equal(ContentIdentityComparison.NotComparable, outcome);
    }

    /// <summary>
    ///     Test that equal identities are the same, without regard to the letter case of the hash.
    /// </summary>
    [Theory]
    [InlineData("sha256:aa", "sha256:aa")]
    [InlineData("sha256:AA", "sha256:aa")]
    [InlineData("git-commit:ab12", "GIT-COMMIT:AB12")]
    public void PackageContentIdentity_Compare_EqualIdentities_IsSameIgnoringCase(string pinned, string actual)
    {
        // Arrange: two spellings of one identity

        // Act
        var outcome = PackageContentIdentity.Compare(pinned, actual);

        // Assert
        Assert.Equal(ContentIdentityComparison.Same, outcome);
    }

    /// <summary>
    ///     Test that two different identities of the same kind mean the content changed.
    /// </summary>
    [Fact]
    public void PackageContentIdentity_Compare_DifferentValues_IsChanged()
    {
        // Arrange: two hashes of the same kind

        // Act
        var outcome = PackageContentIdentity.Compare("sha256:aa", "sha256:bb");

        // Assert
        Assert.Equal(ContentIdentityComparison.Changed, outcome);
    }

    /// <summary>
    ///     Test that identities of different kinds mean the content changed, because the same
    ///     version is now delivered by a different kind of source.
    /// </summary>
    [Fact]
    public void PackageContentIdentity_Compare_DifferentKinds_IsChanged()
    {
        // Arrange: a file hash against a commit

        // Act
        var outcome = PackageContentIdentity.Compare("sha256:aa", "git-commit:aa");

        // Assert
        Assert.Equal(ContentIdentityComparison.Changed, outcome);
    }

    /// <summary>
    ///     Test that the short description cuts the value so two identities can be told apart
    ///     without printing a whole hash, and keeps a short value whole.
    /// </summary>
    [Fact]
    public void PackageContentIdentity_Describe_LongIdentity_ReturnsShortForm()
    {
        // Arrange
        var identity = "sha256:" + AbcSha256;

        // Act
        var described = PackageContentIdentity.Describe(identity);

        // Assert: twelve characters of the value, and a short value is not cut
        Assert.Equal("sha256:ba7816bf8f01", described);
        Assert.Equal("git-commit:abc", PackageContentIdentity.Describe("git-commit:abc"));
    }

    /// <summary>
    ///     Test that the short description reports "none" when no identity is known.
    /// </summary>
    [Fact]
    public void PackageContentIdentity_Describe_MissingIdentity_ReturnsNone()
    {
        // Arrange: no identity, or only white space

        // Act / Assert
        Assert.Equal("none", PackageContentIdentity.Describe(null));
        Assert.Equal("none", PackageContentIdentity.Describe(" "));
    }

    /// <summary>
    ///     Test that an identity of an unrecognized shape is cut after its first colon and never
    ///     echoed in full.
    /// </summary>
    [Fact]
    public void PackageContentIdentity_Describe_UnrecognizedShape_CutsAfterFirstColon()
    {
        // Arrange: a long value that itself contains a colon
        var identity = "other:0123456789abcdef:ghij";

        // Act
        var described = PackageContentIdentity.Describe(identity);

        // Assert: the part after the first colon is cut to twelve characters
        Assert.Equal("other:0123456789ab", described);
    }
}
