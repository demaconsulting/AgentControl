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

using System.Diagnostics;
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
    ///     Test that AllManagedFoldersExist returns false when a managed folder itself (not an
    ///     ancestor such as <c>.github</c>) is a junction, distinguishing this case from
    ///     <see cref="PackageZipExtractor_AllManagedFoldersExist_AncestorIsJunctionWithRealFolders_ReturnsFalse"/>.
    /// </summary>
    [Fact]
    public void PackageZipExtractor_AllManagedFoldersExist_ManagedFolderItselfIsJunction_ReturnsFalse()
    {
        // NTFS directory junctions are a Windows-only concept; skip on other CI runners.
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Directory junctions are a Windows-only filesystem feature.");
        }

        // Arrange: a repo root with three genuine managed folders, and a fourth
        // (".github/agents") that is itself a junction to a separate, isolated directory with
        // real content - so the naive Directory.Exists-based check alone would (incorrectly)
        // report it as present.
        var repoRoot = CreateTempDirectory();
        var linkTarget = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(repoRoot, ".github", "standards"));
        Directory.CreateDirectory(Path.Combine(repoRoot, ".github", "templates"));
        Directory.CreateDirectory(Path.Combine(repoRoot, ".github", "skills"));
        try
        {
            CreateJunction(Path.Combine(repoRoot, ".github", "agents"), linkTarget);

            Assert.False(PackageZipExtractor.AllManagedFoldersExist(repoRoot));
        }
        finally
        {
            Directory.Delete(Path.Combine(repoRoot, ".github", "agents"));
            Directory.Delete(repoRoot, recursive: true);
            Directory.Delete(linkTarget, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a zip entry whose name textually starts with a managed-folder prefix but
    ///     uses ".." components to resolve to a location outside every managed folder (while
    ///     still remaining under the repo root) is not extracted anywhere - closing the traversal
    ///     bypass where the managed-folder membership check ran against the raw, unnormalized
    ///     entry path instead of its canonical (".."-resolved) form.
    /// </summary>
    [Fact]
    public void PackageZipExtractor_Extract_TraversalEntryWithinRepoRoot_DoesNotEscapeManagedFolders()
    {
        // Arrange: an entry that textually starts with ".github/agents/" but resolves (via "..")
        // to a repo-root-level file outside every managed folder
        var zipPath = Path.Combine(Path.GetTempPath(), "agentcontrol_package_" + Guid.NewGuid() + ".zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry(".github/agents/../../outside.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("should not be extracted");
        }

        var repoRoot = CreateTempDirectory();
        try
        {
            // Act: extract the crafted package
            PackageZipExtractor.Extract(zipPath, repoRoot);

            // Assert: the traversal entry was skipped, not extracted to the repo root
            Assert.False(File.Exists(Path.Combine(repoRoot, "outside.txt")));
        }
        finally
        {
            File.Delete(zipPath);
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    /// <summary>
    ///     Test that extracting into a repo whose <c>.github</c> folder is a junction (reparse
    ///     point) pointing outside the repo root is refused, rather than silently writing through
    ///     the junction to the linked-to location.
    /// </summary>
    [Fact]
    public void PackageZipExtractor_Extract_ManagedFolderAncestorIsJunction_ThrowsAndDoesNotWriteThroughLink()
    {
        // NTFS directory junctions (and the 'mklink /J' tool used to create them) are a
        // Windows-only concept; this test project also runs on Linux/macOS CI runners, so skip
        // there rather than shelling out to a nonexistent 'cmd.exe'.
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Directory junctions are a Windows-only filesystem feature.");
        }

        // Arrange: a repo root whose ".github" entry is a junction to a separate, isolated
        // directory standing in for a location outside the repo. The link target starts empty so
        // this test exercises the extraction-step guard specifically (DeleteManagedFolder is a
        // no-op here since no managed folder exists through the junction yet).
        var zipPath = CreatePackageZip(("agents/copilot.md", "should not be extracted"));
        var repoRoot = CreateTempDirectory();
        var linkTarget = CreateTempDirectory();
        try
        {
            CreateJunction(Path.Combine(repoRoot, ".github"), linkTarget);

            // Act / Assert: extraction is refused rather than writing through the junction
            Assert.Throws<UnsafeRepositoryStateException>(() => PackageZipExtractor.Extract(zipPath, repoRoot));
            Assert.False(File.Exists(Path.Combine(linkTarget, "agents", "copilot.md")));
        }
        finally
        {
            File.Delete(zipPath);
            // The ".github" junction entry itself must be removed (not recursively, since that
            // would delete the link target's contents) before the repo root can be deleted.
            Directory.Delete(Path.Combine(repoRoot, ".github"));
            Directory.Delete(repoRoot, recursive: true);
            Directory.Delete(linkTarget, recursive: true);
        }
    }

    /// <summary>
    ///     Test that the blind-delete step itself refuses to recurse through a junctioned
    ///     <c>.github</c> ancestor, so pre-existing content at the link's target survives even
    ///     though it happens to be reachable at a path that lexically looks like a managed folder.
    /// </summary>
    [Fact]
    public void PackageZipExtractor_Extract_ManagedFolderAncestorIsJunctionWithExistingContent_DoesNotBlindDeleteThroughLink()
    {
        // NTFS directory junctions are a Windows-only concept; skip on other CI runners.
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Directory junctions are a Windows-only filesystem feature.");
        }

        // Arrange: a repo root whose ".github" entry is a junction to a separate, isolated
        // directory that *already* has a real "agents" folder with content - so
        // Directory.Exists(folderPath) is true through the junction, and without the
        // DeleteManagedFolder symlink-ancestor guard, Extract's blind-delete step 2 would recurse
        // through the junction and remove it before extraction's own guard is ever reached.
        var zipPath = CreatePackageZip(("agents/copilot.md", "should not be extracted"));
        var repoRoot = CreateTempDirectory();
        var linkTarget = CreateTempDirectory();
        var linkTargetAgentsDir = Path.Combine(linkTarget, "agents");
        Directory.CreateDirectory(linkTargetAgentsDir);
        var keepFilePath = Path.Combine(linkTargetAgentsDir, "keepme.md");
        File.WriteAllText(keepFilePath, "must survive");
        try
        {
            CreateJunction(Path.Combine(repoRoot, ".github"), linkTarget);

            // Act / Assert: extraction is refused, and the pre-existing content behind the
            // junction was never blind-deleted
            Assert.Throws<UnsafeRepositoryStateException>(() => PackageZipExtractor.Extract(zipPath, repoRoot));
            Assert.Equal("must survive", File.ReadAllText(keepFilePath));
        }
        finally
        {
            File.Delete(zipPath);
            // The ".github" junction entry itself must be removed (not recursively, since that
            // would delete the link target's contents) before the repo root can be deleted.
            Directory.Delete(Path.Combine(repoRoot, ".github"));
            Directory.Delete(repoRoot, recursive: true);
            Directory.Delete(linkTarget, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a repo root which is itself a junction to another location is refused, since
    ///     every managed-folder operation would otherwise silently write through it.
    /// </summary>
    [Fact]
    public void PackageZipExtractor_Extract_RepoRootIsJunction_ThrowsAndDoesNotWriteThroughLink()
    {
        // NTFS directory junctions are a Windows-only concept; skip on other CI runners.
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Directory junctions are a Windows-only filesystem feature.");
        }

        // Arrange: a "repo root" that is itself nothing but a junction to a separate, isolated
        // directory - simulating a caller-supplied path that resolves through a link before any
        // managed-folder segment is even appended.
        var zipPath = CreatePackageZip(("agents/copilot.md", "should not be extracted"));
        var linkTarget = CreateTempDirectory();
        var repoRootParent = CreateTempDirectory();
        var repoRoot = Path.Combine(repoRootParent, "repo-root-link");
        try
        {
            CreateJunction(repoRoot, linkTarget);

            // Act / Assert: extraction is refused, and nothing was written through the link
            Assert.Throws<UnsafeRepositoryStateException>(() => PackageZipExtractor.Extract(zipPath, repoRoot));
            Assert.False(File.Exists(Path.Combine(linkTarget, ".github", "agents", "copilot.md")));
        }
        finally
        {
            File.Delete(zipPath);
            // The repo-root junction entry itself must be removed (not recursively, since that
            // would delete the link target's contents) before the parent can be deleted.
            Directory.Delete(repoRoot);
            Directory.Delete(repoRootParent, recursive: true);
            Directory.Delete(linkTarget, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a junction/symlink nested *inside* a managed folder (not just an ancestor of
    ///     it) is rejected during the blind-delete step, since a plain recursive
    ///     <see cref="Directory.Delete(string, bool)"/> would otherwise follow it and delete
    ///     content outside the repo root.
    /// </summary>
    [Fact]
    public void PackageZipExtractor_Extract_ManagedFolderContainsNestedJunction_ThrowsAndDoesNotDeleteThroughLink()
    {
        // NTFS directory junctions are a Windows-only concept; skip on other CI runners.
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Directory junctions are a Windows-only filesystem feature.");
        }

        // Arrange: a normal (non-linked) ".github/agents" managed folder that itself contains a
        // nested junction pointing to a separate, isolated directory with content that must
        // survive.
        var zipPath = CreatePackageZip(("agents/copilot.md", "should not be extracted"));
        var repoRoot = CreateTempDirectory();
        var linkTarget = CreateTempDirectory();
        var keepFilePath = Path.Combine(linkTarget, "keepme.md");
        File.WriteAllText(keepFilePath, "must survive");
        var agentsDir = Path.Combine(repoRoot, ".github", "agents");
        Directory.CreateDirectory(agentsDir);
        try
        {
            CreateJunction(Path.Combine(agentsDir, "linked"), linkTarget);

            // Act / Assert: the blind delete is refused, and the linked content was never deleted
            Assert.Throws<UnsafeRepositoryStateException>(() => PackageZipExtractor.Extract(zipPath, repoRoot));
            Assert.Equal("must survive", File.ReadAllText(keepFilePath));
        }
        finally
        {
            File.Delete(zipPath);
            // The nested junction entry itself must be removed (not recursively) before the repo
            // root can be deleted.
            Directory.Delete(Path.Combine(agentsDir, "linked"));
            Directory.Delete(repoRoot, recursive: true);
            Directory.Delete(linkTarget, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a *dangling* symlink/junction ancestor (one whose target no longer exists) is
    ///     still rejected, exercising the <see cref="File.GetAttributes(string)"/>-based reparse
    ///     check regardless of whether the link's target exists - unlike the former
    ///     <see cref="Directory.Exists(string)"/>-based check, which follows the link to test for
    ///     the target's existence and would silently report "not a directory" (skipping the
    ///     guard entirely) for a dangling link. Runs on every platform: real symbolic links on
    ///     Linux/macOS via <see cref="Directory.CreateSymbolicLink(string, string)"/>, and NTFS
    ///     junctions on Windows (created against a real target, then made dangling by deleting
    ///     that target, since <c>mklink /J</c> itself requires an existing target).
    /// </summary>
    [Fact]
    public void PackageZipExtractor_Extract_ManagedFolderAncestorIsDanglingLink_ThrowsAndDoesNotBypassGuard()
    {
        var zipPath = CreatePackageZip(("agents/copilot.md", "should not be extracted"));
        var repoRoot = CreateTempDirectory();
        var githubPath = Path.Combine(repoRoot, ".github");
        try
        {
            CreateDanglingLink(githubPath);

            // Act / Assert: extraction is refused, not silently allowed through the dangling link
            Assert.Throws<UnsafeRepositoryStateException>(() => PackageZipExtractor.Extract(zipPath, repoRoot));
        }
        finally
        {
            File.Delete(zipPath);
            // The dangling link entry itself must be removed before the repo root can be
            // deleted. Directory.Delete follows the link (via stat/lstat) to confirm it is a
            // directory before removing it, which fails with DirectoryNotFoundException for a
            // *dangling* link whose target no longer exists - this only works here because
            // Windows junction metadata lives on the link entry itself, independent of target
            // validity. On Linux/macOS, a dangling symlink must instead be removed with
            // File.Delete, which unlinks the directory entry directly without following it.
            if (OperatingSystem.IsWindows())
            {
                Directory.Delete(githubPath);
            }
            else
            {
                File.Delete(githubPath);
            }

            Directory.Delete(repoRoot, recursive: true);
        }
    }

    /// <summary>
    ///     Test that <see cref="PackageZipExtractor.AllManagedFoldersExist"/> refuses to trust a
    ///     managed folder that is only reachable through a reparse-point ancestor, rather than
    ///     silently reporting it as present (which would let a caller such as
    ///     <c>RepoCardViewModel.EnsureAgentFilesSyncedBeforeLaunch</c> skip <c>Extract</c>
    ///     entirely and launch using files outside the repo root).
    /// </summary>
    [Fact]
    public void PackageZipExtractor_AllManagedFoldersExist_AncestorIsJunctionWithRealFolders_ReturnsFalse()
    {
        // NTFS directory junctions are a Windows-only concept; skip on other CI runners.
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Directory junctions are a Windows-only filesystem feature.");
        }

        // Arrange: a repo root whose ".github" entry is a junction to a separate, isolated
        // directory that genuinely has all four managed folders - so the naive
        // Directory.Exists-based check alone would (incorrectly) report every folder as present.
        var repoRoot = CreateTempDirectory();
        var linkTarget = CreateTempDirectory();
        foreach (var folder in new[] { "agents", "standards", "templates", "skills" })
        {
            Directory.CreateDirectory(Path.Combine(linkTarget, folder));
        }

        try
        {
            CreateJunction(Path.Combine(repoRoot, ".github"), linkTarget);

            Assert.False(PackageZipExtractor.AllManagedFoldersExist(repoRoot));
        }
        finally
        {
            Directory.Delete(Path.Combine(repoRoot, ".github"));
            Directory.Delete(repoRoot, recursive: true);
            Directory.Delete(linkTarget, recursive: true);
        }
    }

    /// <summary>
    ///     Creates a dangling directory symlink/junction at <paramref name="linkPath"/> - one
    ///     whose target does not exist - using a real symbolic link on Linux/macOS (created
    ///     without any target validation) or an NTFS junction on Windows (created against a real
    ///     temporary target that is deleted immediately afterward).
    /// </summary>
    /// <param name="linkPath">The link's path; its parent must exist and it must not already
    ///     exist.</param>
    private static void CreateDanglingLink(string linkPath)
    {
        if (OperatingSystem.IsWindows())
        {
            var target = CreateTempDirectory();
            CreateJunction(linkPath, target);
            Directory.Delete(target);
        }
        else
        {
            Directory.CreateSymbolicLink(linkPath, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        }
    }

    /// <summary>
    ///     Creates an NTFS directory junction at <paramref name="linkPath"/> pointing to
    ///     <paramref name="targetPath"/>, using <c>mklink /J</c> since junctions (unlike symbolic
    ///     links) do not require elevated privileges or Developer Mode on Windows.
    /// </summary>
    /// <param name="linkPath">The junction's path; its parent must exist and it must not already
    ///     exist.</param>
    /// <param name="targetPath">The existing directory the junction points to.</param>
    private static void CreateJunction(string linkPath, string targetPath)
    {
        var startInfo = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{linkPath}\" \"{targetPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)
                             ?? throw new InvalidOperationException("Failed to start 'cmd.exe' to create junction.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Failed to create junction '{linkPath}' -> '{targetPath}': {process.StandardError.ReadToEnd()}");
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
