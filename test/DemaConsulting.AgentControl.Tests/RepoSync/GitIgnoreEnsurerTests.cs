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

using DemaConsulting.AgentControl.RepoSync;

namespace DemaConsulting.AgentControl.Tests.RepoSync;

/// <summary>
///     Unit tests for <see cref="GitIgnoreEnsurer"/>.
/// </summary>
public class GitIgnoreEnsurerTests
{
    private const string Marker = "# Added by AgentControl - agent package folders";

    /// <summary>
    ///     Test that when the marker comment is already present anywhere in an existing
    ///     .gitignore, Ensure leaves the file byte-for-byte unchanged (no write, no duplicate
    ///     block).
    /// </summary>
    [Fact]
    public void GitIgnoreEnsurer_Ensure_MarkerAlreadyPresent_DoesNotModifyFile()
    {
        // Arrange: a .gitignore that already contains the marker (and its lines)
        var repoRoot = CreateTempDirectory();
        try
        {
            var gitIgnorePath = Path.Combine(repoRoot, ".gitignore");
            var original = "bin/\nobj/\n\n" + Marker + "\n.github/agents/\n.github/standards/\n.github/templates/\n.github/skills/\n";
            File.WriteAllText(gitIgnorePath, original);

            // Act: ensure again
            GitIgnoreEnsurer.Ensure(repoRoot);

            // Assert: the file is completely unchanged - no duplicate block was appended
            Assert.Equal(original, File.ReadAllText(gitIgnorePath));
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    /// <summary>
    ///     Test that when no .gitignore file exists at all, Ensure creates one containing the
    ///     marker comment and the four managed-folder lines.
    /// </summary>
    [Fact]
    public void GitIgnoreEnsurer_Ensure_NoGitIgnoreFile_CreatesFileWithManagedFoldersBlock()
    {
        // Arrange: a repo root with no .gitignore file
        var repoRoot = CreateTempDirectory();
        try
        {
            var gitIgnorePath = Path.Combine(repoRoot, ".gitignore");
            Assert.False(File.Exists(gitIgnorePath));

            // Act: ensure
            GitIgnoreEnsurer.Ensure(repoRoot);

            // Assert: a new file was created with the marker and the four managed-folder lines
            var content = File.ReadAllText(gitIgnorePath);
            Assert.Contains(Marker, content, StringComparison.Ordinal);
            Assert.Contains(".github/agents/", content, StringComparison.Ordinal);
            Assert.Contains(".github/standards/", content, StringComparison.Ordinal);
            Assert.Contains(".github/templates/", content, StringComparison.Ordinal);
            Assert.Contains(".github/skills/", content, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    /// <summary>
    ///     Test that when a .gitignore has existing content without a trailing blank line and no
    ///     marker, Ensure appends the block after a separating blank line, leaving the original
    ///     lines untouched and unreordered.
    /// </summary>
    [Fact]
    public void GitIgnoreEnsurer_Ensure_ExistingContentWithoutMarker_AppendsBlockPreservingExistingLines()
    {
        // Arrange: a .gitignore with unrelated content, no trailing blank line, no marker
        var repoRoot = CreateTempDirectory();
        try
        {
            var gitIgnorePath = Path.Combine(repoRoot, ".gitignore");
            const string original = "bin/\nobj/\n";
            File.WriteAllText(gitIgnorePath, original);

            // Act: ensure
            GitIgnoreEnsurer.Ensure(repoRoot);

            // Assert: original lines are preserved unchanged, followed by a blank-line separator
            // and the new marker-delimited block
            var content = File.ReadAllText(gitIgnorePath);
            Assert.StartsWith(original, content, StringComparison.Ordinal);
            var expected = original + Environment.NewLine + Marker + Environment.NewLine
                + ".github/agents/" + Environment.NewLine + ".github/standards/" + Environment.NewLine
                + ".github/templates/" + Environment.NewLine + ".github/skills/" + Environment.NewLine;
            Assert.Equal(expected, content);
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    /// <summary>
    ///     Test that when a .gitignore's existing content already ends in a blank line, Ensure
    ///     does not introduce a redundant extra blank line before the appended block.
    /// </summary>
    [Fact]
    public void GitIgnoreEnsurer_Ensure_ExistingContentEndsWithBlankLine_AppendsBlockWithoutExtraBlankLine()
    {
        // Arrange: a .gitignore whose content already ends with a blank line
        var repoRoot = CreateTempDirectory();
        try
        {
            var gitIgnorePath = Path.Combine(repoRoot, ".gitignore");
            var original = "bin/\nobj/\n" + Environment.NewLine;
            File.WriteAllText(gitIgnorePath, original);

            // Act: ensure
            GitIgnoreEnsurer.Ensure(repoRoot);

            // Assert: no additional blank line was introduced beyond the one already present
            var content = File.ReadAllText(gitIgnorePath);
            var expected = original + Marker + Environment.NewLine
                + ".github/agents/" + Environment.NewLine + ".github/standards/" + Environment.NewLine
                + ".github/templates/" + Environment.NewLine + ".github/skills/" + Environment.NewLine;
            Assert.Equal(expected, content);
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    /// <summary>
    ///     Test that when the .gitignore target path is actually a directory (forcing a real
    ///     I/O failure without mocking), Ensure throws InvalidOperationException naming the
    ///     path, and does not leave any partial content behind.
    /// </summary>
    [Fact]
    public void GitIgnoreEnsurer_Ensure_PathIsDirectory_ThrowsInvalidOperationException()
    {
        // Arrange: a repo root where ".gitignore" is itself a directory, not a file
        var repoRoot = CreateTempDirectory();
        try
        {
            var gitIgnorePath = Path.Combine(repoRoot, ".gitignore");
            Directory.CreateDirectory(gitIgnorePath);

            // Act / Assert: the directory-in-place-of-a-file forces a real I/O exception,
            // wrapped in InvalidOperationException naming the path
            var ex = Assert.Throws<InvalidOperationException>(() => GitIgnoreEnsurer.Ensure(repoRoot));
            Assert.Contains(gitIgnorePath, ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    /// <summary>
    ///     Creates a unique temporary directory for test isolation.
    /// </summary>
    /// <returns>The created directory's path.</returns>
    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_gitignore_ensurer_test_" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        return path;
    }
}
