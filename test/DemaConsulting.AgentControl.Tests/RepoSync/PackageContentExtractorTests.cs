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
///     Unit tests for <see cref="PackageContentExtractor"/> and the package content readers it
///     works through (<see cref="ZipPackageContentReader"/>,
///     <see cref="DirectoryPackageContentReader"/>).
/// </summary>
/// <remarks>
///     The zip-file behaviors are covered through the <c>PackageZipExtractor</c> facade tests; these
///     tests focus on the shared extractor driven by the unpacked-folder reader and by a stub
///     reader that can supply hostile paths, plus link rejection where the operating system lets a
///     test create a symbolic link.
/// </remarks>
public sealed class PackageContentExtractorTests : IDisposable
{
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     Test that extracting an unpacked package folder into a fresh repo creates the managed
    ///     folders with their contents and does not copy root-level files.
    /// </summary>
    [Fact]
    public void PackageContentExtractor_Extract_DirectoryPackageIntoFreshRepo_CreatesManagedFoldersOnly()
    {
        // Arrange: an unpacked package with managed folders plus root-level files, and an empty repo
        var package = CreatePackageFolder(
            (".github/agents/copilot.md", "agents v1"),
            (".github/standards/style.md", "standards v1"),
            ("release-notes.md", "notes"),
            ("AGENTS.md", "template"));
        var repo = CreateTempDirectory();
        using var reader = DirectoryPackageContentReader.Open(package);

        // Act
        PackageContentExtractor.Extract(reader, repo);

        // Assert: managed content copied, root-level files and everything else not
        Assert.Equal("agents v1", File.ReadAllText(Path.Combine(repo, ".github", "agents", "copilot.md")));
        Assert.Equal("standards v1", File.ReadAllText(Path.Combine(repo, ".github", "standards", "style.md")));
        Assert.False(File.Exists(Path.Combine(repo, "release-notes.md")));
        Assert.False(File.Exists(Path.Combine(repo, "AGENTS.md")));
        Assert.Equal([".github"], Directory.GetFileSystemEntries(repo).Select(Path.GetFileName));
    }

    /// <summary>
    ///     Test that extracting from a package folder blind-deletes a managed folder's old contents
    ///     before copying the new ones.
    /// </summary>
    [Fact]
    public void PackageContentExtractor_Extract_DirectoryPackageOverExistingFolder_ReplacesOldContents()
    {
        // Arrange: a repo with a stale file in a managed folder
        var package = CreatePackageFolder((".github/agents/copilot.md", "agents v2"));
        var repo = CreateTempDirectory();
        var agents = Path.Combine(repo, ".github", "agents");
        Directory.CreateDirectory(agents);
        File.WriteAllText(Path.Combine(agents, "old-agent.md"), "stale");
        using var reader = DirectoryPackageContentReader.Open(package);

        // Act
        PackageContentExtractor.Extract(reader, repo);

        // Assert
        Assert.False(File.Exists(Path.Combine(agents, "old-agent.md")));
        Assert.Equal("agents v2", File.ReadAllText(Path.Combine(agents, "copilot.md")));
    }

    /// <summary>
    ///     Test that the extractor never writes into the package folder (a shared NuGet cache): its
    ///     entire file tree is identical before and after an extraction and reads.
    /// </summary>
    [Fact]
    public void PackageContentExtractor_Extract_DirectoryPackage_NeverModifiesPackageFolder()
    {
        // Arrange
        var package = CreatePackageFolder(
            (".github/agents/copilot.md", "agents"), ("release-notes.md", "notes"), ("AGENTS.md", "template"));
        var repo = CreateTempDirectory();
        var before = SnapshotTree(package);
        using var reader = DirectoryPackageContentReader.Open(package);

        // Act
        PackageContentExtractor.Extract(reader, repo);
        PackageContentExtractor.ReadReleaseNotes(reader);
        PackageContentExtractor.ReadAgentsMdTemplate(reader);

        // Assert
        Assert.Equal(before, SnapshotTree(package));
    }

    /// <summary>
    ///     Test that release notes are read from the folder package's content root, or null when
    ///     absent, without copying anything to disk.
    /// </summary>
    [Fact]
    public void PackageContentExtractor_ReadReleaseNotes_DirectoryPackage_ReturnsContentOrNull()
    {
        // Arrange
        var withNotes = CreatePackageFolder(("release-notes.md", "## 1.0.0"));
        var withoutNotes = CreatePackageFolder((".github/agents/a.md", "x"));

        // Act
        using var readerWith = DirectoryPackageContentReader.Open(withNotes);
        using var readerWithout = DirectoryPackageContentReader.Open(withoutNotes);

        // Assert
        Assert.Equal("## 1.0.0", PackageContentExtractor.ReadReleaseNotes(readerWith));
        Assert.Null(PackageContentExtractor.ReadReleaseNotes(readerWithout));
    }

    /// <summary>
    ///     Test that the AGENTS.md template is read from the folder package's content root, or null
    ///     when absent.
    /// </summary>
    [Fact]
    public void PackageContentExtractor_ReadAgentsMdTemplate_DirectoryPackage_ReturnsContentOrNull()
    {
        // Arrange
        var withTemplate = CreatePackageFolder(("AGENTS.md", "# Template"));
        var withoutTemplate = CreatePackageFolder((".github/agents/a.md", "x"));

        // Act
        using var readerWith = DirectoryPackageContentReader.Open(withTemplate);
        using var readerWithout = DirectoryPackageContentReader.Open(withoutTemplate);

        // Assert
        Assert.Equal("# Template", PackageContentExtractor.ReadAgentsMdTemplate(readerWith));
        Assert.Null(PackageContentExtractor.ReadAgentsMdTemplate(readerWithout));
    }

    /// <summary>
    ///     Test that the same content supplied as a zip and as an unpacked folder produces an
    ///     identical repo tree.
    /// </summary>
    [Fact]
    public void PackageContentExtractor_Extract_ZipAndDirectoryWithSameContent_ProduceIdenticalRepoTrees()
    {
        // Arrange: the same files as a zip and as a content folder
        (string Path, string Content)[] files =
        [
            (".github/agents/copilot.md", "agents"),
            (".github/skills/deep/skill.md", "skill"),
            (".github/templates/t.md", "template"),
            ("release-notes.md", "notes")
        ];
        var zipPath = Path.Combine(CreateTempDirectory(), "pkg-1.0.0.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var (path, content) in files)
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open());
                writer.Write(content);
            }
        }

        var package = CreatePackageFolder(files);
        var zipRepo = CreateTempDirectory();
        var folderRepo = CreateTempDirectory();

        // Act
        using (var zipReader = ZipPackageContentReader.Open(zipPath))
        {
            PackageContentExtractor.Extract(zipReader, zipRepo);
        }

        using (var folderReader = DirectoryPackageContentReader.Open(package))
        {
            PackageContentExtractor.Extract(folderReader, folderRepo);
        }

        // Assert
        Assert.Equal(SnapshotTree(zipRepo), SnapshotTree(folderRepo));
    }

    /// <summary>
    ///     Test that a package folder without a content sub-folder is rejected when opened, so the
    ///     extractor is never run (and no repo folder deleted) for a wrong-layout package.
    /// </summary>
    [Fact]
    public void DirectoryPackageContentReader_Open_NoContentFolder_ThrowsInvalidOperationException()
    {
        // Arrange: a package folder that keeps its files at the top level instead of content/
        var package = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(package, ".github", "agents"));

        // Act / Assert
        var ex = Assert.Throws<InvalidOperationException>(() => DirectoryPackageContentReader.Open(package));
        Assert.Contains("content", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a reader asked for a path that tries to leave the content root (or for a file
    ///     that does not exist) refuses it.
    /// </summary>
    [Fact]
    public void DirectoryPackageContentReader_OpenReadAndReadText_TraversalOrMissingPath_Rejected()
    {
        // Arrange: a package, and a secret file outside its content folder
        var package = CreatePackageFolder(("release-notes.md", "notes"));
        File.WriteAllText(Path.Combine(package, "secret.txt"), "secret");
        using var reader = DirectoryPackageContentReader.Open(package);

        // Act / Assert
        Assert.Throws<InvalidOperationException>(() => reader.ReadText("../secret.txt"));
        Assert.Throws<InvalidOperationException>(() => reader.OpenRead("../secret.txt"));
        Assert.Throws<InvalidOperationException>(() => reader.OpenRead("missing.md"));
        Assert.Null(reader.ReadText("missing.md"));
    }

    /// <summary>
    ///     Test that a hostile path supplied by a reader (traversal out of the repo) is rejected
    ///     before any managed folder is deleted, leaving the repo untouched.
    /// </summary>
    [Theory]
    [InlineData("../outside.txt")]
    [InlineData(".github/agents/../../../outside.txt")]
    public void PackageContentExtractor_Extract_TraversalPathFromReader_ThrowsBeforeDeletingManagedFolders(string hostilePath)
    {
        // Arrange: a repo with an existing managed folder, and a reader that lists a hostile path
        var repo = CreateTempDirectory();
        var agents = Path.Combine(repo, ".github", "agents");
        Directory.CreateDirectory(agents);
        File.WriteAllText(Path.Combine(agents, "keep.md"), "keep me");
        var reader = new StubPackageContentReader([".github/agents/ok.md", hostilePath]);

        // Act / Assert: the extraction fails, and the existing managed content was not deleted
        Assert.Throws<InvalidOperationException>(() => PackageContentExtractor.Extract(reader, repo));
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(agents, "keep.md")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(repo)!, "outside.txt")));
    }

    /// <summary>
    ///     Test that a symbolic link to a file inside a managed folder of an unpacked package is
    ///     rejected before any managed folder in the repo is deleted.
    /// </summary>
    [Fact]
    public void PackageContentExtractor_Extract_SymlinkedFileInPackage_ThrowsBeforeDeletingManagedFolders()
    {
        // Arrange: a package whose managed file is a link to a file outside the package
        var package = CreatePackageFolder((".github/agents/real.md", "real"));
        var outside = Path.Combine(CreateTempDirectory(), "outside.txt");
        File.WriteAllText(outside, "outside content");
        TryCreateFileSymlinkOrSkip(Path.Combine(package, "content", ".github", "agents", "link.md"), outside);
        var repo = CreateTempDirectory();
        var agents = Path.Combine(repo, ".github", "agents");
        Directory.CreateDirectory(agents);
        File.WriteAllText(Path.Combine(agents, "keep.md"), "keep me");
        using var reader = DirectoryPackageContentReader.Open(package);

        // Act / Assert: rejected, and the repo's existing managed content survives
        var ex = Assert.Throws<InvalidOperationException>(() => PackageContentExtractor.Extract(reader, repo));
        Assert.Contains("link", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(agents, "keep.md")));
    }

    /// <summary>
    ///     Test that a symbolic link to a directory inside an unpacked package is rejected rather
    ///     than followed.
    /// </summary>
    [Fact]
    public void DirectoryPackageContentReader_ListFiles_SymlinkedDirectoryInPackage_ThrowsInvalidOperationException()
    {
        // Arrange: a package whose skills folder is a link to a directory outside the package
        var package = CreatePackageFolder((".github/agents/real.md", "real"));
        var outsideDir = CreateTempDirectory();
        File.WriteAllText(Path.Combine(outsideDir, "payload.md"), "payload");
        TryCreateDirectorySymlinkOrSkip(Path.Combine(package, "content", ".github", "skills"), outsideDir);
        using var reader = DirectoryPackageContentReader.Open(package);

        // Act / Assert
        Assert.Throws<InvalidOperationException>(() => reader.ListFiles());
    }

    /// <summary>
    ///     Test that a content folder that is itself a symbolic link is rejected when opened.
    /// </summary>
    [Fact]
    public void DirectoryPackageContentReader_Open_ContentFolderIsSymlink_ThrowsInvalidOperationException()
    {
        // Arrange: a package folder whose content folder is a link to a real content folder elsewhere
        var realContent = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(realContent, ".github", "agents"));
        var package = CreateTempDirectory();
        TryCreateDirectorySymlinkOrSkip(Path.Combine(package, "content"), realContent);

        // Act / Assert
        Assert.Throws<InvalidOperationException>(() => DirectoryPackageContentReader.Open(package));
    }

    /// <summary>
    ///     Test that a release-notes file that is a symbolic link is refused rather than read.
    /// </summary>
    [Fact]
    public void DirectoryPackageContentReader_ReadText_SymlinkedReleaseNotes_ThrowsInvalidOperationException()
    {
        // Arrange
        var package = CreatePackageFolder((".github/agents/a.md", "x"));
        var outside = Path.Combine(CreateTempDirectory(), "notes.md");
        File.WriteAllText(outside, "outside notes");
        TryCreateFileSymlinkOrSkip(Path.Combine(package, "content", "release-notes.md"), outside);
        using var reader = DirectoryPackageContentReader.Open(package);

        // Act / Assert
        Assert.Throws<InvalidOperationException>(() => reader.ReadText("release-notes.md"));
    }

    /// <summary>
    ///     Test that a broken symbolic link standing in for a root-level file (one whose target does
    ///     not exist) is refused like any other link, rather than being treated as a missing file.
    /// </summary>
    [Fact]
    public void DirectoryPackageContentReader_ReadText_BrokenFileSymlinkAtRoot_ThrowsInvalidOperationException()
    {
        // Arrange: content/release-notes.md is a link to a file that does not exist
        var package = CreatePackageFolder((".github/agents/a.md", "x"));
        var missingTarget = Path.Combine(CreateTempDirectory(), "does-not-exist.md");
        TryCreateFileSymlinkOrSkip(Path.Combine(package, "content", "release-notes.md"), missingTarget);
        using var reader = DirectoryPackageContentReader.Open(package);

        // Act / Assert
        var ex = Assert.Throws<InvalidOperationException>(() => reader.ReadText("release-notes.md"));
        Assert.Contains("link", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that a zip path the platform cannot use (for example one with an embedded null
    ///     character) is reported as the documented <see cref="InvalidOperationException"/> rather
    ///     than escaping as a path exception.
    /// </summary>
    [Fact]
    public void ZipPackageContentReader_Open_InvalidPath_ThrowsInvalidOperationException()
    {
        // Act / Assert
        var ex = Assert.Throws<InvalidOperationException>(() => ZipPackageContentReader.Open("not\0valid.zip"));
        Assert.Contains("Failed to open package zip", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a file that is not a zip archive is reported as the documented
    ///     <see cref="InvalidOperationException"/>.
    /// </summary>
    [Fact]
    public void ZipPackageContentReader_Open_NotAZip_ThrowsInvalidOperationException()
    {
        // Arrange
        var notAZip = Path.Combine(CreateTempDirectory(), "fake-1.0.0.zip");
        File.WriteAllText(notAZip, "this is not a zip archive");

        // Act / Assert
        Assert.Throws<InvalidOperationException>(() => ZipPackageContentReader.Open(notAZip));
    }

    /// <summary>
    ///     Test that an unpacked package folder that is itself a symbolic link is rejected when
    ///     opened.
    /// </summary>
    [Fact]
    public void DirectoryPackageContentReader_Open_PackageFolderIsSymlink_ThrowsInvalidOperationException()
    {
        // Arrange: a real package elsewhere, and a link to it standing in for the package folder
        var realPackage = CreatePackageFolder((".github/agents/real.md", "real"));
        var link = Path.Combine(CreateTempDirectory(), "linked-package");
        TryCreateDirectorySymlinkOrSkip(link, realPackage);

        // Act / Assert
        var ex = Assert.Throws<InvalidOperationException>(() => DirectoryPackageContentReader.Open(link));
        Assert.Contains("link", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that a file reached through a symbolic-link parent directory is refused even though
    ///     the file itself is an ordinary file.
    /// </summary>
    [Fact]
    public void DirectoryPackageContentReader_OpenRead_LinkedParentDirectoryWithOrdinaryFile_ThrowsInvalidOperationException()
    {
        // Arrange: content/.github/agents is a link to an outside folder holding an ordinary file
        var package = CreatePackageFolder((".github/skills/s.md", "skill"));
        var outsideDir = CreateTempDirectory();
        File.WriteAllText(Path.Combine(outsideDir, "payload.md"), "payload");
        TryCreateDirectorySymlinkOrSkip(Path.Combine(package, "content", ".github", "agents"), outsideDir);
        using var reader = DirectoryPackageContentReader.Open(package);

        // Act / Assert: reading through the linked parent is refused
        var ex = Assert.Throws<InvalidOperationException>(() => reader.OpenRead(".github/agents/payload.md"));
        Assert.Contains("link", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that an unpacked package folder that is a directory junction is rejected when
    ///     opened. Junctions need no privilege on Windows, so this test runs there.
    /// </summary>
    [Fact]
    public void DirectoryPackageContentReader_Open_PackageFolderIsJunction_ThrowsInvalidOperationException()
    {
        // Arrange
        var realPackage = CreatePackageFolder((".github/agents/real.md", "real"));
        var link = Path.Combine(CreateTempDirectory(), "junction-package");
        CreateJunctionOrSkip(link, realPackage);

        // Act / Assert
        Assert.Throws<InvalidOperationException>(() => DirectoryPackageContentReader.Open(link));
    }

    /// <summary>
    ///     Test that a content folder that is a directory junction is rejected when opened.
    /// </summary>
    [Fact]
    public void DirectoryPackageContentReader_Open_ContentFolderIsJunction_ThrowsInvalidOperationException()
    {
        // Arrange: the content folder is a junction to a real content folder elsewhere
        var realContent = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(realContent, ".github", "agents"));
        var package = CreateTempDirectory();
        CreateJunctionOrSkip(Path.Combine(package, "content"), realContent);

        // Act / Assert
        Assert.Throws<InvalidOperationException>(() => DirectoryPackageContentReader.Open(package));
    }

    /// <summary>
    ///     Test that a file reached through a directory-junction parent is refused even though the
    ///     file itself is an ordinary file.
    /// </summary>
    [Fact]
    public void DirectoryPackageContentReader_OpenRead_JunctionParentDirectoryWithOrdinaryFile_ThrowsInvalidOperationException()
    {
        // Arrange: content/.github/agents is a junction to an outside folder holding an ordinary file
        var package = CreatePackageFolder((".github/skills/s.md", "skill"));
        var outsideDir = CreateTempDirectory();
        File.WriteAllText(Path.Combine(outsideDir, "payload.md"), "payload");
        CreateJunctionOrSkip(Path.Combine(package, "content", ".github", "agents"), outsideDir);
        using var reader = DirectoryPackageContentReader.Open(package);

        // Act / Assert
        var ex = Assert.Throws<InvalidOperationException>(() => reader.OpenRead(".github/agents/payload.md"));
        Assert.Contains("link", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that listing a package that contains a directory junction is refused rather than
    ///     following the junction.
    /// </summary>
    [Fact]
    public void DirectoryPackageContentReader_ListFiles_JunctionInPackage_ThrowsInvalidOperationException()
    {
        // Arrange: content/.github/skills is a junction to an outside folder
        var package = CreatePackageFolder((".github/agents/real.md", "real"));
        var outsideDir = CreateTempDirectory();
        File.WriteAllText(Path.Combine(outsideDir, "payload.md"), "payload");
        CreateJunctionOrSkip(Path.Combine(package, "content", ".github", "skills"), outsideDir);
        using var reader = DirectoryPackageContentReader.Open(package);

        // Act / Assert
        Assert.Throws<InvalidOperationException>(() => reader.ListFiles());
    }

    /// <summary>
    ///     Test that extracting a package that contains a directory junction is refused before any
    ///     managed folder in the repo is deleted.
    /// </summary>
    [Fact]
    public void PackageContentExtractor_Extract_JunctionInPackage_ThrowsBeforeDeletingManagedFolders()
    {
        // Arrange: a package whose skills folder is a junction, and a repo with existing content
        var package = CreatePackageFolder((".github/agents/real.md", "real"));
        var outsideDir = CreateTempDirectory();
        File.WriteAllText(Path.Combine(outsideDir, "payload.md"), "payload");
        CreateJunctionOrSkip(Path.Combine(package, "content", ".github", "skills"), outsideDir);
        var repo = CreateTempDirectory();
        var agents = Path.Combine(repo, ".github", "agents");
        Directory.CreateDirectory(agents);
        File.WriteAllText(Path.Combine(agents, "keep.md"), "keep me");
        using var reader = DirectoryPackageContentReader.Open(package);

        // Act / Assert: rejected, and the repo's existing managed content survives
        Assert.Throws<InvalidOperationException>(() => PackageContentExtractor.Extract(reader, repo));
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(agents, "keep.md")));
    }

    /// <summary>
    ///     Test that a repo whose <c>.github</c> folder is a directory junction is refused without
    ///     deleting or writing anything, including at the junction's target.
    /// </summary>
    [Fact]
    public void PackageContentExtractor_Extract_GitHubFolderIsJunction_RefusesAndLeavesTargetUntouched() =>
        AssertGitHubFolderLinkRefused(CreateJunctionOrSkip);

    /// <summary>
    ///     Test that a repo whose <c>.github</c> folder is a symbolic link is refused without
    ///     deleting or writing anything, including at the link's target.
    /// </summary>
    [Fact]
    public void PackageContentExtractor_Extract_GitHubFolderIsSymlink_RefusesAndLeavesTargetUntouched() =>
        AssertGitHubFolderLinkRefused(TryCreateDirectorySymlinkOrSkip);

    /// <summary>
    ///     Test that a repo with a managed folder that is a directory junction is refused before
    ///     any managed folder is deleted, and the junction's target is untouched.
    /// </summary>
    [Fact]
    public void PackageContentExtractor_Extract_ManagedFolderIsJunction_RefusesAndLeavesTargetUntouched() =>
        AssertManagedFolderLinkRefused(CreateJunctionOrSkip);

    /// <summary>
    ///     Test that a repo with a managed folder that is a symbolic link is refused before any
    ///     managed folder is deleted, and the link's target is untouched.
    /// </summary>
    [Fact]
    public void PackageContentExtractor_Extract_ManagedFolderIsSymlink_RefusesAndLeavesTargetUntouched() =>
        AssertManagedFolderLinkRefused(TryCreateDirectorySymlinkOrSkip);

    /// <summary>
    ///     Test that a link on a package file's destination path below a managed folder (a
    ///     subfolder that is a junction) is refused before anything is deleted.
    /// </summary>
    [Fact]
    public void PackageContentExtractor_Extract_DestinationSubfolderIsJunction_RefusesAndLeavesTargetUntouched() =>
        AssertDestinationSubfolderLinkRefused(CreateJunctionOrSkip);

    /// <summary>
    ///     Test that a link on a package file's destination path below a managed folder (a
    ///     subfolder that is a symbolic link) is refused before anything is deleted.
    /// </summary>
    [Fact]
    public void PackageContentExtractor_Extract_DestinationSubfolderIsSymlink_RefusesAndLeavesTargetUntouched() =>
        AssertDestinationSubfolderLinkRefused(TryCreateDirectorySymlinkOrSkip);

    /// <summary>
    ///     Test that a repo root that itself lives under a junction is accepted: only components
    ///     at or below the repo root must be link-free.
    /// </summary>
    [Fact]
    public void PackageContentExtractor_Extract_RepoRootUnderJunction_Succeeds() =>
        AssertRepoRootUnderLinkSucceeds(CreateJunctionOrSkip);

    /// <summary>
    ///     Test that a repo root that itself lives under a symbolic link is accepted: only
    ///     components at or below the repo root must be link-free.
    /// </summary>
    [Fact]
    public void PackageContentExtractor_Extract_RepoRootUnderSymlink_Succeeds() =>
        AssertRepoRootUnderLinkSucceeds(TryCreateDirectorySymlinkOrSkip);

    /// <summary>
    ///     Test that a file where a managed folder belongs is refused before any other managed
    ///     folder is deleted, so the repo is never left partially erased.
    /// </summary>
    [Fact]
    public void PackageContentExtractor_Extract_ManagedFolderIsAFile_RefusesBeforeDeletingAnything()
    {
        // Arrange: .github/agents is a regular file; .github/standards is an ordinary folder
        var repo = CreateTempDirectory();
        var github = Path.Combine(repo, ".github");
        Directory.CreateDirectory(Path.Combine(github, "standards"));
        File.WriteAllText(Path.Combine(github, "standards", "keep.md"), "keep me");
        File.WriteAllText(Path.Combine(github, "agents"), "not a folder");
        var package = CreatePackageFolder((".github/agents/new.md", "new"), (".github/standards/new.md", "new"));
        using var reader = DirectoryPackageContentReader.Open(package);

        // Act
        var ex = Assert.Throws<InvalidOperationException>(() => PackageContentExtractor.Extract(reader, repo));

        // Assert: refused naming the offending component, nothing deleted or written
        Assert.Contains(Path.Combine(".github", "agents"), ex.Message, StringComparison.Ordinal);
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(github, "standards", "keep.md")));
        Assert.False(File.Exists(Path.Combine(github, "standards", "new.md")));
        Assert.Equal("not a folder", File.ReadAllText(Path.Combine(github, "agents")));
    }

    /// <summary>
    ///     Test that a symbolic link that appears at a file's destination after the folders were
    ///     checked is never written through.
    /// </summary>
    [Fact]
    public void PackageContentExtractor_Extract_DestinationFileBecomesSymlink_RefusesAndLeavesTargetUntouched()
    {
        // Arrange: a file outside the repo, and a reader that - while the first file is copied -
        // plants a symbolic link at the destination of the second one
        var outside = CreateTempDirectory();
        var outsideFile = Path.Combine(outside, "precious.txt");
        File.WriteAllText(outsideFile, "precious");
        var repo = CreateTempDirectory();
        var secondDestination = Path.Combine(repo, ".github", "agents", "b.md");
        using var reader = new LinkPlantingPackageContentReader(
            [".github/agents/a.md", ".github/agents/b.md"],
            () => TryCreateFileSymlinkOrSkip(secondDestination, outsideFile));

        // Act
        var ex = Assert.Throws<InvalidOperationException>(() => PackageContentExtractor.Extract(reader, repo));

        // Assert: refused naming the link, and the link's target was not written
        Assert.Contains("link", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("precious", File.ReadAllText(outsideFile));
    }

    /// <summary>
    ///     Test that a managed folder that is a directory junction does not count as an existing
    ///     managed folder, so callers do not treat the repo as already synced.
    /// </summary>
    [Fact]
    public void PackageContentExtractor_AllManagedFoldersExist_ManagedFolderIsJunction_ReturnsFalse() =>
        AssertLinkedManagedFolderIsNotValid(CreateJunctionOrSkip);

    /// <summary>
    ///     Test that a managed folder that is a symbolic link does not count as an existing
    ///     managed folder, so callers do not treat the repo as already synced.
    /// </summary>
    [Fact]
    public void PackageContentExtractor_AllManagedFoldersExist_ManagedFolderIsSymlink_ReturnsFalse() =>
        AssertLinkedManagedFolderIsNotValid(TryCreateDirectorySymlinkOrSkip);

    /// <summary>
    ///     Test that all four ordinary managed folders count as existing.
    /// </summary>
    [Fact]
    public void PackageContentExtractor_AllManagedFoldersExist_AllFoldersOrdinary_ReturnsTrue()
    {
        // Arrange
        var repo = CreateTempDirectory();
        foreach (var folder in new[] { "agents", "standards", "templates", "skills" })
        {
            Directory.CreateDirectory(Path.Combine(repo, ".github", folder));
        }

        // Act / Assert
        Assert.True(PackageContentExtractor.AllManagedFoldersExist(repo));
    }

    /// <summary>
    ///     Scenario: <c>&lt;repo&gt;/.github</c> is a link to an outside folder holding content;
    ///     extraction must be refused and the outside folder must be unchanged.
    /// </summary>
    /// <param name="createLink">Creates the directory link (link path, target).</param>
    private void AssertGitHubFolderLinkRefused(Action<string, string> createLink)
    {
        // Arrange: .github is a link to an outside folder that holds managed-looking content
        var outside = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(outside, "agents"));
        File.WriteAllText(Path.Combine(outside, "agents", "precious.md"), "precious");
        File.WriteAllText(Path.Combine(outside, "unrelated.md"), "unrelated");
        var before = SnapshotTree(outside);
        var repo = CreateTempDirectory();
        var link = Path.Combine(repo, ".github");
        createLink(link, outside);
        var package = CreatePackageFolder((".github/agents/new.md", "new"));
        using var reader = DirectoryPackageContentReader.Open(package);

        // Act
        var ex = Assert.Throws<InvalidOperationException>(() => PackageContentExtractor.Extract(reader, repo));

        // Assert: refused with a clear error, nothing deleted or written through the link
        Assert.Contains("link", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, SnapshotTree(outside));
        Assert.NotNull(new DirectoryInfo(link).LinkTarget);
    }

    /// <summary>
    ///     Scenario: a managed folder is a link to an outside folder; extraction must be refused
    ///     before any other managed folder is deleted, and the outside folder must be unchanged.
    /// </summary>
    /// <param name="createLink">Creates the directory link (link path, target).</param>
    private void AssertManagedFolderLinkRefused(Action<string, string> createLink)
    {
        // Arrange: .github/agents is a link to an outside folder; .github/standards is ordinary
        var outside = CreateTempDirectory();
        File.WriteAllText(Path.Combine(outside, "precious.md"), "precious");
        var before = SnapshotTree(outside);
        var repo = CreateTempDirectory();
        var standards = Path.Combine(repo, ".github", "standards");
        Directory.CreateDirectory(standards);
        File.WriteAllText(Path.Combine(standards, "keep.md"), "keep me");
        createLink(Path.Combine(repo, ".github", "agents"), outside);
        var package = CreatePackageFolder((".github/agents/new.md", "new"), (".github/standards/new.md", "new"));
        using var reader = DirectoryPackageContentReader.Open(package);

        // Act
        var ex = Assert.Throws<InvalidOperationException>(() => PackageContentExtractor.Extract(reader, repo));

        // Assert: refused, the link target is unchanged, and the ordinary folder was not deleted
        Assert.Contains("link", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, SnapshotTree(outside));
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(standards, "keep.md")));
        Assert.False(File.Exists(Path.Combine(standards, "new.md")));
    }

    /// <summary>
    ///     Scenario: a subfolder on a destination path is a link; extraction must be refused
    ///     before the managed folders are deleted, and the outside folder must be unchanged.
    /// </summary>
    /// <param name="createLink">Creates the directory link (link path, target).</param>
    private void AssertDestinationSubfolderLinkRefused(Action<string, string> createLink)
    {
        // Arrange: .github/agents is ordinary but its "sub" folder is a link to an outside folder
        var outside = CreateTempDirectory();
        File.WriteAllText(Path.Combine(outside, "precious.md"), "precious");
        var before = SnapshotTree(outside);
        var repo = CreateTempDirectory();
        var agents = Path.Combine(repo, ".github", "agents");
        Directory.CreateDirectory(agents);
        File.WriteAllText(Path.Combine(agents, "keep.md"), "keep me");
        createLink(Path.Combine(agents, "sub"), outside);
        var package = CreatePackageFolder((".github/agents/sub/new.md", "new"));
        using var reader = DirectoryPackageContentReader.Open(package);

        // Act
        var ex = Assert.Throws<InvalidOperationException>(() => PackageContentExtractor.Extract(reader, repo));

        // Assert
        Assert.Contains("link", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, SnapshotTree(outside));
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(agents, "keep.md")));
    }

    /// <summary>
    ///     Scenario: the repo root is reached through a link; extraction must still succeed
    ///     because only components at or below the root are checked.
    /// </summary>
    /// <param name="createLink">Creates the directory link (link path, target).</param>
    private void AssertRepoRootUnderLinkSucceeds(Action<string, string> createLink)
    {
        // Arrange: the repo is reached through a link (like a redirected user profile)
        var realRepo = CreateTempDirectory();
        var linkedRoot = Path.Combine(CreateTempDirectory(), "linked-repo");
        createLink(linkedRoot, realRepo);
        var package = CreatePackageFolder((".github/agents/new.md", "new"));
        using var reader = DirectoryPackageContentReader.Open(package);

        // Act
        PackageContentExtractor.Extract(reader, linkedRoot);

        // Assert: the content landed in the real repo
        Assert.Equal("new", File.ReadAllText(Path.Combine(realRepo, ".github", "agents", "new.md")));
    }

    /// <summary>
    ///     Scenario: one of the four managed folders is a link; the folders must not be reported
    ///     as all existing.
    /// </summary>
    /// <param name="createLink">Creates the directory link (link path, target).</param>
    private void AssertLinkedManagedFolderIsNotValid(Action<string, string> createLink)
    {
        // Arrange: three ordinary managed folders and a fourth that is a link
        var repo = CreateTempDirectory();
        foreach (var folder in new[] { "agents", "standards", "templates" })
        {
            Directory.CreateDirectory(Path.Combine(repo, ".github", folder));
        }

        createLink(Path.Combine(repo, ".github", "skills"), CreateTempDirectory());

        // Act / Assert
        Assert.False(PackageContentExtractor.AllManagedFoldersExist(repo));
    }

    /// <summary>
    ///     Test that a null reader or repo root is rejected.
    /// </summary>
    [Fact]
    public void PackageContentExtractor_Extract_NullArguments_ThrowsArgumentNullException()
    {
        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => PackageContentExtractor.Extract(null!, CreateTempDirectory()));
        Assert.Throws<ArgumentNullException>(
            () => PackageContentExtractor.Extract(new StubPackageContentReader([]), null!));
    }

    /// <summary>
    ///     Creates an unpacked package folder whose files live under its <c>content</c> folder.
    /// </summary>
    /// <param name="files">The files as (path relative to content, text) pairs.</param>
    /// <returns>The package folder path.</returns>
    private string CreatePackageFolder(params (string Path, string Content)[] files)
    {
        var package = CreateTempDirectory();
        var content = Path.Combine(package, "content");
        Directory.CreateDirectory(content);
        foreach (var (path, text) in files)
        {
            var full = Path.Combine(content, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
        }

        // A nuspec beside content, as in a real unpacked package, must be ignored by the reader
        File.WriteAllText(Path.Combine(package, "pkg.nuspec"), "<package />");
        return package;
    }

    /// <summary>
    ///     Captures a folder's relative file paths and contents for before/after comparison.
    /// </summary>
    /// <param name="root">The folder to snapshot.</param>
    /// <returns>A sorted list of <c>path=content</c> lines.</returns>
    private static List<string> SnapshotTree(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/') + "=" + File.ReadAllText(file))
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    ///     Creates a file symbolic link, or skips the test when the platform or privileges do not
    ///     permit creating one.
    /// </summary>
    private static void TryCreateFileSymlinkOrSkip(string linkPath, string target)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
            File.CreateSymbolicLink(linkPath, target);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Skip($"Cannot create symbolic links in this environment: {ex.Message}");
        }
    }

    /// <summary>
    ///     Creates a directory symbolic link, or skips the test when the platform or privileges do
    ///     not permit creating one.
    /// </summary>
    private static void TryCreateDirectorySymlinkOrSkip(string linkPath, string target)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
            Directory.CreateSymbolicLink(linkPath, target);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Skip($"Cannot create symbolic links in this environment: {ex.Message}");
        }
    }

    /// <summary>
    ///     Creates a directory junction (needs no privilege on Windows), or skips the test on
    ///     platforms that do not have junctions.
    /// </summary>
    /// <param name="linkPath">The junction to create.</param>
    /// <param name="target">The existing directory it points at.</param>
    /// <exception cref="InvalidOperationException">Thrown on Windows when the junction cannot be
    ///     created, which would indicate a broken test environment rather than a skip.</exception>
    private static void CreateJunctionOrSkip(string linkPath, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Directory junctions exist only on Windows.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
        var startInfo = new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("/c");
        // cspell:disable-next-line
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(linkPath);
        startInfo.ArgumentList.Add(target);
        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start cmd.exe to create a junction.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Creating the junction failed ({process.ExitCode}): {output}");
        }
    }

    /// <summary>
    ///     Creates a unique temporary directory tracked for cleanup in <see cref="Dispose"/>.
    /// </summary>
    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_content_extractor_test_" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        return path;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var path in _tempPaths.Where(Directory.Exists))
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup; leftover temp directories do not fail the test.
            }
        }
    }

    /// <summary>
    ///     A reader that lists caller-supplied paths with fixed content and runs an action the first
    ///     time a file is opened, so a test can change the repo while an extraction is running.
    /// </summary>
    private sealed class LinkPlantingPackageContentReader : IPackageContentReader
    {
        /// <summary>
        ///     The paths this reader lists.
        /// </summary>
        private readonly IReadOnlyList<string> _paths;

        /// <summary>
        ///     The action run once, the first time a file is opened.
        /// </summary>
        private readonly Action _onFirstOpen;

        /// <summary>
        ///     Whether <see cref="_onFirstOpen"/> has run.
        /// </summary>
        private bool _opened;

        /// <summary>
        ///     Initializes the reader.
        /// </summary>
        /// <param name="paths">The paths to list.</param>
        /// <param name="onFirstOpen">The action to run when the first file is opened.</param>
        public LinkPlantingPackageContentReader(IReadOnlyList<string> paths, Action onFirstOpen)
        {
            _paths = paths;
            _onFirstOpen = onFirstOpen;
        }

        /// <inheritdoc />
        public string Description => "link planting package";

        /// <inheritdoc />
        public IReadOnlyList<string> ListFiles() => _paths;

        /// <inheritdoc />
        public Stream OpenRead(string relativePath)
        {
            if (!_opened)
            {
                _opened = true;
                _onFirstOpen();
            }

            return new MemoryStream("package content"u8.ToArray());
        }

        /// <inheritdoc />
        public string? ReadText(string relativePath) => null;

        /// <inheritdoc />
        public void Dispose()
        {
            // Nothing to release.
        }
    }

    /// <summary>
    ///     A reader that lists caller-supplied paths with empty content, so a test can present
    ///     paths (such as traversals) that a real package reader would never produce.
    /// </summary>
    private sealed class StubPackageContentReader : IPackageContentReader
    {
        /// <summary>
        ///     The paths this stub lists.
        /// </summary>
        private readonly IReadOnlyList<string> _paths;

        /// <summary>
        ///     Initializes the stub.
        /// </summary>
        /// <param name="paths">The paths to list.</param>
        public StubPackageContentReader(IReadOnlyList<string> paths) => _paths = paths;

        /// <inheritdoc />
        public string Description => "stub package";

        /// <inheritdoc />
        public IReadOnlyList<string> ListFiles() => _paths;

        /// <inheritdoc />
        public Stream OpenRead(string relativePath) => new MemoryStream();

        /// <inheritdoc />
        public string? ReadText(string relativePath) => null;

        /// <inheritdoc />
        public void Dispose()
        {
            // Nothing to release.
        }
    }
}
