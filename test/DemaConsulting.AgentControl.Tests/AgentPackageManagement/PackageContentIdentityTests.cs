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

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.RepoSync;

// cspell:ignore xabc ghij
namespace DemaConsulting.AgentControl.Tests.AgentPackageManagement;

/// <summary>
///     Unit tests for <see cref="PackageContentIdentity"/>.
/// </summary>
/// <remarks>
///     The identity is computed here from an in-memory <see cref="IPackageContentReader"/>, so what is
///     under test is the encoding and the membership rule, not a particular kind of source. That the
///     real zip, NuGet and Git readers agree is shown at subsystem level.
/// </remarks>
public class PackageContentIdentityTests
{
    /// <summary>
    ///     The SHA-256 of the ASCII text <c>abc</c>, a published test vector.
    /// </summary>
    private const string AbcSha256 = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    /// <summary>
    ///     The SHA-256 of no bytes at all, a published test vector: the identity of a package that
    ///     delivers no managed file.
    /// </summary>
    private const string EmptySha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    /// <summary>
    ///     The identity of the two files <see cref="TwoFilesInPackageOrder"/> computed independently
    ///     of the production code (a script that follows the documented encoding), so the encoding
    ///     itself is pinned and cannot drift unnoticed.
    /// </summary>
    private const string TwoFilesVector = "files-sha256:9096a274ab5f7130afd0058d54a339568abd3e74ab8c05aecb07d18fd1985d87";

    /// <summary>
    ///     Test that the identity of the agent files is exactly the SHA-256 of the documented
    ///     canonical encoding (path, a zero byte, the length as eight little-endian bytes, the bytes;
    ///     files in ordinal path order), matching a vector computed independently.
    /// </summary>
    [Fact]
    public void PackageContentIdentity_ComputeFilesIdentity_KnownFiles_MatchesCanonicalEncoding()
    {
        // Arrange: two files listed out of order
        using var reader = new InMemoryReader(
            (".github/standards/b.md", []),
            (".github/agents/a.md", Encoding.ASCII.GetBytes("abc")));

        // Act
        var identity = PackageContentIdentity.ComputeFilesIdentity(reader);

        // Assert: the pinned vector, the independent encoding below, and the written form
        Assert.Equal(TwoFilesVector, identity);
        Assert.Equal(
            "files-sha256:" + Sha256Hex(Encode(TwoFilesInPackageOrder())),
            identity);
        Assert.Equal(identity.ToLowerInvariant(), identity);
    }

    /// <summary>
    ///     Test that a package that delivers no managed file has the identity of the empty stream.
    /// </summary>
    [Fact]
    public void PackageContentIdentity_ComputeFilesIdentity_NoManagedFiles_IsIdentityOfEmptyStream()
    {
        // Arrange: only files that are never extracted
        using var reader = new InMemoryReader(("release-notes.md", [1]), ("README.md", [2]));

        // Act
        var identity = PackageContentIdentity.ComputeFilesIdentity(reader);

        // Assert
        Assert.Equal("files-sha256:" + EmptySha256, identity);
    }

    /// <summary>
    ///     Test that the order the reader lists the files in does not change the identity.
    /// </summary>
    [Fact]
    public void PackageContentIdentity_ComputeFilesIdentity_EntryOrderDiffers_IsEqual()
    {
        // Arrange: the same files listed in opposite orders
        var files = new (string Path, byte[] Content)[]
        {
            (".github/agents/a.md", [1, 2, 3]),
            (".github/skills/s/SKILL.md", [4]),
            (".github/standards/b.md", [5, 6]),
            (".github/templates/t.md", [7])
        };
        using var forward = new InMemoryReader(files);
        using var backward = new InMemoryReader([.. files.Reverse()]);

        // Act
        var first = PackageContentIdentity.ComputeFilesIdentity(forward);
        var second = PackageContentIdentity.ComputeFilesIdentity(backward);

        // Assert
        Assert.Equal(first, second);
    }

    /// <summary>
    ///     Test that a stream that cannot state its length (as an entry of a compressed zip cannot)
    ///     gives the same identity as one that can.
    /// </summary>
    [Fact]
    public void PackageContentIdentity_ComputeFilesIdentity_NonSeekableStreams_IsEqualToSeekable()
    {
        // Arrange: the same files served through seekable and through non-seekable streams
        var files = new (string Path, byte[] Content)[]
        {
            (".github/agents/a.md", Encoding.ASCII.GetBytes("abc")),
            (".github/standards/b.md", [])
        };
        using var seekable = new InMemoryReader(files);
        using var sequential = new InMemoryReader(files) { Seekable = false };

        // Act
        var first = PackageContentIdentity.ComputeFilesIdentity(seekable);
        var second = PackageContentIdentity.ComputeFilesIdentity(sequential);

        // Assert
        Assert.Equal(first, second);
        Assert.Equal(TwoFilesVector, second);
    }

    /// <summary>
    ///     Test that a package path that resolves to the same canonical path through different spellings
    ///     (a <c>.</c> or a <c>..</c> segment) has the same identity as the plain path.
    /// </summary>
    [Fact]
    public void PackageContentIdentity_ComputeFilesIdentity_EquivalentSpellingOfPath_IsEqual()
    {
        // Arrange: one file spelled plainly and with redundant segments
        using var plain = new InMemoryReader((".github/agents/a.md", [1]));
        using var spelled = new InMemoryReader((".github/agents/./sub/../a.md", [1]));

        // Act
        var first = PackageContentIdentity.ComputeFilesIdentity(plain);
        var second = PackageContentIdentity.ComputeFilesIdentity(spelled);

        // Assert
        Assert.Equal(first, second);
    }

    /// <summary>
    ///     Test that <c>release-notes.md</c>, <c>AGENTS.md</c> and every file outside the four managed
    ///     folders are not part of the identity, so changing them never changes it.
    /// </summary>
    [Fact]
    public void PackageContentIdentity_ComputeFilesIdentity_NonExtractedFiles_AreExcluded()
    {
        // Arrange: the same managed file with and without files that are never written to a repo
        var managed = (".github/agents/a.md", new byte[] { 1, 2, 3 });
        using var bare = new InMemoryReader(managed);
        using var decorated = new InMemoryReader(
            managed,
            ("release-notes.md", [9, 9]),
            ("AGENTS.md", [8]),
            ("README.md", [7]),
            (".github/workflows/build.yml", [6]),
            (".github/agents-extra/not-managed.md", [5]),
            ("docs/.github/agents/nested.md", [4]));

        // Act
        var first = PackageContentIdentity.ComputeFilesIdentity(bare);
        var second = PackageContentIdentity.ComputeFilesIdentity(decorated);

        // Assert
        Assert.Equal(first, second);
    }

    /// <summary>
    ///     Test that changing a single byte of one file changes the identity.
    /// </summary>
    [Fact]
    public void PackageContentIdentity_ComputeFilesIdentity_OneByteChanged_Differs()
    {
        // Arrange: two packages that differ in one byte of one file
        using var original = new InMemoryReader((".github/agents/a.md", [1, 2, 3]), (".github/standards/b.md", [4]));
        using var changed = new InMemoryReader((".github/agents/a.md", [1, 2, 4]), (".github/standards/b.md", [4]));

        // Act
        var first = PackageContentIdentity.ComputeFilesIdentity(original);
        var second = PackageContentIdentity.ComputeFilesIdentity(changed);

        // Assert
        Assert.NotEqual(first, second);
    }

    /// <summary>
    ///     Test that renaming a file, adding a file and removing a file each change the identity.
    /// </summary>
    [Fact]
    public void PackageContentIdentity_ComputeFilesIdentity_PathOrMembershipChanged_Differs()
    {
        // Arrange: a package, and variants that rename, add and remove a file
        using var original = new InMemoryReader((".github/agents/a.md", [1]), (".github/standards/b.md", [2]));
        using var renamed = new InMemoryReader((".github/agents/a2.md", [1]), (".github/standards/b.md", [2]));
        using var added = new InMemoryReader(
            (".github/agents/a.md", [1]), (".github/standards/b.md", [2]), (".github/templates/c.md", []));
        using var removed = new InMemoryReader((".github/agents/a.md", [1]));

        // Act
        var baseline = PackageContentIdentity.ComputeFilesIdentity(original);
        var identities = new[]
        {
            PackageContentIdentity.ComputeFilesIdentity(renamed),
            PackageContentIdentity.ComputeFilesIdentity(added),
            PackageContentIdentity.ComputeFilesIdentity(removed)
        };

        // Assert: each differs from the baseline and from each other
        Assert.Multiple(
            () => Assert.All(identities, identity => Assert.NotEqual(baseline, identity)),
            () => Assert.Equal(3, identities.Distinct().Count()));
    }

    /// <summary>
    ///     Test that the framing of path and length keeps bytes that move from one file to another from
    ///     producing the same identity.
    /// </summary>
    [Fact]
    public void PackageContentIdentity_ComputeFilesIdentity_BytesMovedBetweenFiles_Differs()
    {
        // Arrange: the same four bytes split differently over the same two files
        using var first = new InMemoryReader((".github/agents/a.md", [1, 2]), (".github/agents/b.md", [3, 4]));
        using var second = new InMemoryReader((".github/agents/a.md", [1]), (".github/agents/b.md", [2, 3, 4]));

        // Act
        var firstIdentity = PackageContentIdentity.ComputeFilesIdentity(first);
        var secondIdentity = PackageContentIdentity.ComputeFilesIdentity(second);

        // Assert
        Assert.NotEqual(firstIdentity, secondIdentity);
    }

    /// <summary>
    ///     Test that two files that resolve to the same canonical path are refused: the identity would
    ///     otherwise depend on which of them extraction lets win.
    /// </summary>
    [Fact]
    public void PackageContentIdentity_ComputeFilesIdentity_DuplicateCanonicalPath_ThrowsInvalidOperationException()
    {
        // Arrange: one file listed twice under spellings that mean the same path
        using var reader = new InMemoryReader((".github/agents/a.md", [1]), (".github/agents/./a.md", [2]));

        // Act / Assert
        Assert.Throws<InvalidOperationException>(() => PackageContentIdentity.ComputeFilesIdentity(reader));
    }

    /// <summary>
    ///     Test that a path that resolves outside the package root - which extraction refuses - is
    ///     refused here too, and that the non-throwing form reports no identity instead.
    /// </summary>
    [Fact]
    public void PackageContentIdentity_ComputeFilesIdentity_PathEscapesPackageRoot_ThrowsAndTryReportsNone()
    {
        // Arrange: a managed-looking path that climbs out of the package
        using var reader = new InMemoryReader((".github/agents/../../../outside.txt", [1]));

        // Act / Assert
        Assert.Throws<InvalidOperationException>(() => PackageContentIdentity.ComputeFilesIdentity(reader));
        Assert.Null(PackageContentIdentity.TryComputeFilesIdentity(reader));
    }

    /// <summary>
    ///     Test that a file that changes length while it is read is refused rather than hashed
    ///     inconsistently.
    /// </summary>
    [Fact]
    public void PackageContentIdentity_ComputeFilesIdentity_FileChangesWhileRead_ThrowsInvalidOperationException()
    {
        // Arrange: a reader whose file grows between the two reads
        using var reader = new InMemoryReader((".github/agents/a.md", [1, 2, 3])) { GrowAfterFirstOpen = true };

        // Act / Assert
        Assert.Throws<InvalidOperationException>(() => PackageContentIdentity.ComputeFilesIdentity(reader));
    }

    /// <summary>
    ///     Test that a null reader is rejected.
    /// </summary>
    [Fact]
    public void PackageContentIdentity_ComputeFilesIdentity_NullReader_ThrowsArgumentNullException()
    {
        // Arrange: no reader

        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => PackageContentIdentity.ComputeFilesIdentity(null!));
    }

    /// <summary>
    ///     Test that only a well-formed files identity is recognized as one: not an identity recorded by
    ///     an older version, not one of the wrong length, and not one with non-hexadecimal digits.
    /// </summary>
    [Theory]
    [InlineData("files-sha256:9096a274ab5f7130afd0058d54a339568abd3e74ab8c05aecb07d18fd1985d87", true)]
    [InlineData("  FILES-SHA256:9096A274AB5F7130AFD0058D54A339568ABD3E74AB8C05AECB07D18FD1985D87 ", true)]
    [InlineData("sha256:9096a274ab5f7130afd0058d54a339568abd3e74ab8c05aecb07d18fd1985d87", false)]
    [InlineData("git-commit:0123456789abcdef0123456789abcdef01234567", false)]
    [InlineData("files-sha256:9096a274", false)]
    [InlineData("files-sha256:zz96a274ab5f7130afd0058d54a339568abd3e74ab8c05aecb07d18fd1985d87", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void PackageContentIdentity_IsFilesIdentity_VariousTexts_RecognizesOnlyFilesIdentity(string? text, bool expected)
    {
        // Arrange: a text as a pin might hold it

        // Act
        var recognized = PackageContentIdentity.IsFilesIdentity(text);

        // Assert
        Assert.Equal(expected, recognized);
    }

    /// <summary>
    ///     Test that a commit hash is formatted as a lower-case <c>git-commit:</c> revision, for
    ///     both object-format widths.
    /// </summary>
    [Theory]
    [InlineData("0123456789ABCDEF0123456789abcdef01234567")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public void PackageContentIdentity_FromGitCommit_ValidHash_ReturnsLowerCasePrefixedIdentity(string commit)
    {
        // Arrange: a commit hash of either width, in either case

        // Act
        var revision = PackageContentIdentity.FromGitCommit(commit);

        // Assert
        Assert.Equal("git-commit:" + commit.ToLowerInvariant(), revision);
        Assert.True(PackageContentIdentity.IsGitCommitRevision(revision));
    }

    /// <summary>
    ///     Test that a malformed commit hash is rejected rather than formatted into a revision.
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
    ///     Test that a text is recognized as a Git commit revision only when it is the prefix and a
    ///     40 or 64 digit hexadecimal hash.
    /// </summary>
    [Theory]
    [InlineData("git-commit:0123456789abcdef0123456789abcdef01234567", true)]
    [InlineData("GIT-COMMIT:0123456789ABCDEF0123456789ABCDEF01234567", true)]
    [InlineData("git-commit:abc", false)]
    [InlineData("files-sha256:9096a274ab5f7130afd0058d54a339568abd3e74ab8c05aecb07d18fd1985d87", false)]
    [InlineData(null, false)]
    public void PackageContentIdentity_IsGitCommitRevision_VariousTexts_RecognizesOnlyCommitRevision(string? text, bool expected)
    {
        // Arrange: a text as a pin might hold it

        // Act / Assert
        Assert.Equal(expected, PackageContentIdentity.IsGitCommitRevision(text));
    }

    /// <summary>
    ///     Test that nothing is compared, so there is never a false alarm, when either identity is
    ///     missing or is not a files identity (one recorded by an older version, which was computed
    ///     from a container and cannot be compared with one computed from the files).
    /// </summary>
    [Theory]
    [InlineData(null, "files-sha256:9096a274ab5f7130afd0058d54a339568abd3e74ab8c05aecb07d18fd1985d87")]
    [InlineData("", "files-sha256:9096a274ab5f7130afd0058d54a339568abd3e74ab8c05aecb07d18fd1985d87")]
    [InlineData("files-sha256:9096a274ab5f7130afd0058d54a339568abd3e74ab8c05aecb07d18fd1985d87", null)]
    [InlineData("files-sha256:9096a274ab5f7130afd0058d54a339568abd3e74ab8c05aecb07d18fd1985d87", "")]
    [InlineData("sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "files-sha256:9096a274ab5f7130afd0058d54a339568abd3e74ab8c05aecb07d18fd1985d87")]
    [InlineData("git-commit:0123456789abcdef0123456789abcdef01234567", "files-sha256:9096a274ab5f7130afd0058d54a339568abd3e74ab8c05aecb07d18fd1985d87")]
    [InlineData(null, null)]
    public void PackageContentIdentity_Compare_EitherIdentityMissingOrLegacy_IsNotComparable(string? pinned, string? actual)
    {
        // Arrange: a pin or a source that has no files identity

        // Act
        var outcome = PackageContentIdentity.Compare(pinned, actual);

        // Assert
        Assert.Equal(ContentIdentityComparison.NotComparable, outcome);
    }

    /// <summary>
    ///     Test that equal identities are the same, without regard to the letter case of the hash.
    /// </summary>
    [Fact]
    public void PackageContentIdentity_Compare_EqualIdentities_IsSameIgnoringCase()
    {
        // Arrange: two spellings of one identity
        const string lower = "files-sha256:9096a274ab5f7130afd0058d54a339568abd3e74ab8c05aecb07d18fd1985d87";
        const string upper = "FILES-SHA256:9096A274AB5F7130AFD0058D54A339568ABD3E74AB8C05AECB07D18FD1985D87";

        // Act
        var outcome = PackageContentIdentity.Compare(lower, upper);

        // Assert
        Assert.Equal(ContentIdentityComparison.Same, outcome);
    }

    /// <summary>
    ///     Test that two different files identities mean the content changed.
    /// </summary>
    [Fact]
    public void PackageContentIdentity_Compare_DifferentValues_IsChanged()
    {
        // Arrange: two files identities
        var other = "files-sha256:" + AbcSha256;

        // Act
        var outcome = PackageContentIdentity.Compare(TwoFilesVector, other);

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
        var identity = "files-sha256:" + AbcSha256;

        // Act
        var described = PackageContentIdentity.Describe(identity);

        // Assert: twelve characters of the value, and a short value is not cut
        Assert.Equal("files-sha256:ba7816bf8f01", described);
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

    /// <summary>
    ///     The files of <see cref="TwoFilesVector"/> in the canonical (ordinal path) order.
    /// </summary>
    /// <returns>The files.</returns>
    private static (string Path, byte[] Content)[] TwoFilesInPackageOrder() =>
    [
        (".github/agents/a.md", Encoding.ASCII.GetBytes("abc")),
        (".github/standards/b.md", [])
    ];

    /// <summary>
    ///     Encodes files exactly as the documented canonical encoding says, independently of the
    ///     production code.
    /// </summary>
    /// <param name="files">The files in canonical order.</param>
    /// <returns>The byte stream that is hashed.</returns>
    private static byte[] Encode(IEnumerable<(string Path, byte[] Content)> files)
    {
        using var stream = new MemoryStream();
        foreach (var (path, content) in files)
        {
            stream.Write(Encoding.UTF8.GetBytes(path));
            stream.WriteByte(0);
            var length = new byte[sizeof(long)];
            BinaryPrimitives.WriteInt64LittleEndian(length, content.Length);
            stream.Write(length);
            stream.Write(content);
        }

        return stream.ToArray();
    }

    /// <summary>
    ///     Computes the lower-case hexadecimal SHA-256 of some bytes.
    /// </summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The hash.</returns>
    private static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>
    ///     An in-memory package: files given as path and bytes, listed in the order given.
    /// </summary>
    /// <param name="files">The files of the package.</param>
    private sealed class InMemoryReader(params (string Path, byte[] Content)[] files) : IPackageContentReader
    {
        /// <summary>
        ///     How many times a file has been opened, so a file can be made to change between reads.
        /// </summary>
        private int _opens;

        /// <summary>
        ///     Gets or sets whether the streams can state their length and be repositioned.
        /// </summary>
        public bool Seekable { get; init; } = true;

        /// <summary>
        ///     Gets or sets whether a file gets an extra byte after the first time it was opened.
        /// </summary>
        public bool GrowAfterFirstOpen { get; init; }

        /// <inheritdoc />
        public string Description => "in-memory package";

        /// <inheritdoc />
        public IReadOnlyList<string> ListFiles() => [.. files.Select(file => file.Path)];

        /// <inheritdoc />
        public Stream OpenRead(string relativePath)
        {
            var content = files.First(file => file.Path == relativePath).Content;
            if (GrowAfterFirstOpen && _opens++ > 0)
            {
                content = [.. content, 0];
            }

            Stream stream = new MemoryStream(content, writable: false);
            return Seekable ? stream : new SequentialStream(stream);
        }

        /// <inheritdoc />
        public string? ReadText(string relativePath) => null;

        /// <inheritdoc />
        public void Dispose()
        {
            // Nothing is held
        }
    }

    /// <summary>
    ///     A read-only stream that cannot state its length or be repositioned, like an entry of a
    ///     compressed archive.
    /// </summary>
    /// <param name="inner">The stream that supplies the bytes.</param>
    private sealed class SequentialStream(Stream inner) : Stream
    {
        /// <inheritdoc />
        public override bool CanRead => true;

        /// <inheritdoc />
        public override bool CanSeek => false;

        /// <inheritdoc />
        public override bool CanWrite => false;

        /// <inheritdoc />
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc />
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override void Flush()
        {
            // Read-only
        }

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
