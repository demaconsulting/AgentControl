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
