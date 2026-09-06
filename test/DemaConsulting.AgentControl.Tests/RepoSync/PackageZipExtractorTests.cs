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

using System.IO.Compression;
using DemaConsulting.AgentControl.RepoSync;

namespace DemaConsulting.AgentControl.Tests.RepoSync;

/// <summary>
///     Unit tests for <see cref="PackageZipExtractor"/>.
/// </summary>
public class PackageZipExtractorTests
{
    /// <summary>
    ///     Test that extracting a package into a fresh repo creates the four managed folders
    ///     with their contents, and does not extract root-level files.
    /// </summary>
    [Fact]
    public void PackageZipExtractor_Extract_FreshRepo_CreatesManagedFoldersOnly()
    {
        // Arrange: a package zip with the four managed folders plus a root-level file, and an
        // empty repo root
        var zipPath = CreatePackageZip(("agents/copilot.md", "agents v1"), ("standards/style.md", "standards v1"));
        var repoRoot = CreateTempDirectory();
        try
        {
            // Act: extract the package into the repo
            PackageZipExtractor.Extract(zipPath, repoRoot);

            // Assert: managed folder contents are extracted, root-level files are not
            Assert.Equal("agents v1", File.ReadAllText(Path.Combine(repoRoot, ".github", "agents", "copilot.md")));
            Assert.Equal("standards v1", File.ReadAllText(Path.Combine(repoRoot, ".github", "standards", "style.md")));
            Assert.False(File.Exists(Path.Combine(repoRoot, "release-notes.md")));
        }
        finally
        {
            File.Delete(zipPath);
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    /// <summary>
    ///     Test that extracting over an existing managed folder blind-deletes its old contents
    ///     before extracting the new package's contents.
    /// </summary>
    [Fact]
    public void PackageZipExtractor_Extract_ExistingManagedFolder_ReplacesOldContents()
    {
        // Arrange: a repo with a pre-existing agents folder containing a file the new package
        // does not include
        var zipPath = CreatePackageZip(("agents/copilot.md", "agents v2"));
        var repoRoot = CreateTempDirectory();
        try
        {
            var agentsDir = Path.Combine(repoRoot, ".github", "agents");
            Directory.CreateDirectory(agentsDir);
            File.WriteAllText(Path.Combine(agentsDir, "old-agent.md"), "stale content");

            // Act: extract the new package
            PackageZipExtractor.Extract(zipPath, repoRoot);

            // Assert: the old file is gone (blind delete) and the new file is present
            Assert.False(File.Exists(Path.Combine(agentsDir, "old-agent.md")));
            Assert.Equal("agents v2", File.ReadAllText(Path.Combine(agentsDir, "copilot.md")));
        }
        finally
        {
            File.Delete(zipPath);
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    /// <summary>
    ///     Test that opening a file that is not a valid zip archive throws an
    ///     InvalidOperationException with a clear message.
    /// </summary>
    [Fact]
    public void PackageZipExtractor_Extract_InvalidZipFile_ThrowsInvalidOperationException()
    {
        // Arrange: a file with a .zip extension but garbage content
        var zipPath = Path.Combine(Path.GetTempPath(), "agentcontrol_invalid_" + Guid.NewGuid() + ".zip");
        File.WriteAllText(zipPath, "this is not a zip file");
        var repoRoot = CreateTempDirectory();
        try
        {
            // Act / Assert: the invalid archive produces a clear exception that can be caught
            var ex = Assert.Throws<InvalidOperationException>(() => PackageZipExtractor.Extract(zipPath, repoRoot));
            Assert.Contains(zipPath, ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(zipPath);
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    /// <summary>
    ///     Test that ReadReleaseNotes returns the root-level release notes content without
    ///     extracting anything to disk.
    /// </summary>
    [Fact]
    public void PackageZipExtractor_ReadReleaseNotes_EntryPresent_ReturnsContentWithoutExtracting()
    {
        // Arrange: a package zip containing a release-notes.md entry
        var zipPath = CreatePackageZipWithReleaseNotes("## Version 2.0.0\n\nBug fixes.");
        try
        {
            // Act: read the release notes
            var notes = PackageZipExtractor.ReadReleaseNotes(zipPath);

            // Assert: the content is returned as a string
            Assert.Equal("## Version 2.0.0\n\nBug fixes.", notes);
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    /// <summary>
    ///     Test that ReadReleaseNotes returns null when the zip has no release-notes.md entry.
    /// </summary>
    [Fact]
    public void PackageZipExtractor_ReadReleaseNotes_NoEntry_ReturnsNull()
    {
        // Arrange: a package zip with no release-notes.md entry
        var zipPath = CreatePackageZip(("agents/copilot.md", "agents v1"));
        try
        {
            // Act: attempt to read release notes
            var notes = PackageZipExtractor.ReadReleaseNotes(zipPath);

            // Assert: no entry means null, not an exception
            Assert.Null(notes);
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    /// <summary>
    ///     Test that AllManagedFoldersExist returns true only when all four managed folders exist
    ///     under the repo root.
    /// </summary>
    [Fact]
    public void PackageZipExtractor_AllManagedFoldersExist_AllFourPresent_ReturnsTrue()
    {
        // Arrange: a repo root with all four managed folders created
        var repoRoot = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(repoRoot, ".github", "agents"));
            Directory.CreateDirectory(Path.Combine(repoRoot, ".github", "standards"));
            Directory.CreateDirectory(Path.Combine(repoRoot, ".github", "templates"));
            Directory.CreateDirectory(Path.Combine(repoRoot, ".github", "skills"));

            // Act / Assert
            Assert.True(PackageZipExtractor.AllManagedFoldersExist(repoRoot));
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    /// <summary>
    ///     Test that AllManagedFoldersExist returns false when any subset of the four managed
    ///     folders is missing.
    /// </summary>
    [Fact]
    public void PackageZipExtractor_AllManagedFoldersExist_SomeMissing_ReturnsFalse()
    {
        // Arrange: only two of the four managed folders present
        var repoRoot = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(repoRoot, ".github", "agents"));
            Directory.CreateDirectory(Path.Combine(repoRoot, ".github", "standards"));

            // Act / Assert
            Assert.False(PackageZipExtractor.AllManagedFoldersExist(repoRoot));
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    /// <summary>
    ///     Test that AllManagedFoldersExist returns false for a repo root with none of the four
    ///     managed folders.
    /// </summary>
    [Fact]
    public void PackageZipExtractor_AllManagedFoldersExist_NoneExist_ReturnsFalse()
    {
        // Arrange: an empty repo root
        var repoRoot = CreateTempDirectory();
        try
        {
            // Act / Assert
            Assert.False(PackageZipExtractor.AllManagedFoldersExist(repoRoot));
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    /// <summary>
    ///     Creates a temporary package zip with the four managed folders populated from the
    ///     given (relative-path-under-.github, content) pairs, plus a root-level file that must
    ///     never be extracted.
    /// </summary>
    /// <param name="entries">Pairs of (path relative to <c>.github/</c>, file content).</param>
    /// <returns>The path to the created zip file.</returns>
    private static string CreatePackageZip(params (string RelativePath, string Content)[] entries)
    {
        var zipPath = Path.Combine(Path.GetTempPath(), "agentcontrol_package_" + Guid.NewGuid() + ".zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var (relativePath, content) in entries)
            {
                var entry = archive.CreateEntry(".github/" + relativePath);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }

            // A root-level file that must never be extracted to the repo root
            var rootEntry = archive.CreateEntry("README.md");
            using var rootWriter = new StreamWriter(rootEntry.Open());
            rootWriter.Write("This must not be extracted.");
        }

        return zipPath;
    }

    /// <summary>
    ///     Creates a temporary package zip containing only a root-level release-notes.md entry.
    /// </summary>
    /// <param name="content">The release notes content.</param>
    /// <returns>The path to the created zip file.</returns>
    private static string CreatePackageZipWithReleaseNotes(string content)
    {
        var zipPath = Path.Combine(Path.GetTempPath(), "agentcontrol_package_" + Guid.NewGuid() + ".zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("release-notes.md");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }

        return zipPath;
    }

    /// <summary>
    ///     Creates a unique temporary directory for test isolation.
    /// </summary>
    /// <returns>The created directory's path.</returns>
    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_repo_sync_test_" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        return path;
    }
}
