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
using System.Security.Cryptography;
using System.Text;
using DemaConsulting.AgentControl.RepoSync;

namespace DemaConsulting.AgentControl.Tests.RepoSync;

/// <summary>
///     Subsystem tests for RepoSync: a package read by a content reader and applied to a repo by the
///     extractor, the way the application drives them - the reader and the extractor together against
///     real temporary folders and a real zip - with attention to the content identity a reader reports
///     (it must describe the content that is applied) and to a folder whose root is the package layout
///     (the form a Git package is delivered in).
/// </summary>
public sealed class RepoSyncSubsystemTests : IDisposable
{
    /// <summary>
    ///     The temporary folders created by the running test, removed on disposal.
    /// </summary>
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     Test that the identity a zip reader reports is that of exactly the bytes that are extracted
    ///     and that a republished zip is told apart: the identity equals an independently computed
    ///     SHA-256 of the file, the applied files are the ones the zip held (the reader hashes and reads
    ///     the one handle, so the identity cannot describe other bytes than the ones read), on Windows the
    ///     file cannot be replaced while the reader holds it (elsewhere file locks are advisory, so no
    ///     replacement is attempted), and a zip republished with other content reports another identity
    ///     with the other content applied.
    /// </summary>
    [Fact]
    public void RepoSync_ContentIdentity_ZipIdentityDescribesTheBytesThatAreApplied()
    {
        // Arrange: a zip, and the identity computed here independently of the production hashing
        var folder = CreateTempDirectory();
        var zip = Path.Combine(folder, "package-1.0.0.zip");
        WriteZip(zip, "original");
        var expectedOriginal = Sha256Identity(File.ReadAllBytes(zip));
        var repo = CreateTempDirectory();

        // Act: read the zip, try to replace it while it is held (Windows only: elsewhere the lock is
        // advisory and a write would succeed and corrupt the zip), and apply it
        string identityOfRead;
        using (var reader = ZipPackageContentReader.Open(zip))
        {
            identityOfRead = reader.ContentIdentity;
            var replaceWhileHeld = OperatingSystem.IsWindows()
                ? Record.Exception(() => File.WriteAllBytes(zip, [1, 2, 3]))
                : null;
            PackageContentExtractor.Extract(reader, repo);

            // Assert (while held): on Windows the file could not be swapped under the reader
            if (OperatingSystem.IsWindows())
            {
                Assert.IsAssignableFrom<IOException>(replaceWhileHeld);
            }
        }

        // Assert: the reported identity is that of the file, and the applied file is the one in the zip
        Assert.Equal(expectedOriginal, identityOfRead);
        Assert.Equal("original", File.ReadAllText(Path.Combine(repo, ".github", "agents", "copilot.md")));

        // Act: the zip is republished with other content, read and applied to another repo
        File.Delete(zip);
        WriteZip(zip, "republished");
        var expectedRepublished = Sha256Identity(File.ReadAllBytes(zip));
        var otherRepo = CreateTempDirectory();
        string identityOfRepublished;
        using (var reader = ZipPackageContentReader.Open(zip))
        {
            identityOfRepublished = reader.ContentIdentity;
            PackageContentExtractor.Extract(reader, otherRepo);
        }

        // Assert: another identity, with the other content applied
        Assert.Multiple(
            () => Assert.Equal(expectedRepublished, identityOfRepublished),
            () => Assert.NotEqual(identityOfRead, identityOfRepublished),
            () => Assert.Equal(
                "republished", File.ReadAllText(Path.Combine(otherRepo, ".github", "agents", "copilot.md"))));
    }

    /// <summary>
    ///     Test that a reader over a folder reports exactly the identity its opener supplied (the package
    ///     of a NuGet source, or the commit of a Git tag) and none when it was supplied none, and that the
    ///     reader never computes one of its own from the folder.
    /// </summary>
    [Fact]
    public void RepoSync_ContentIdentity_FolderReadersReportTheSuppliedIdentityOrNone()
    {
        // Arrange: an unpacked package folder and a layout-root folder
        var unpacked = CreateTempDirectory();
        WriteFile(Path.Combine(unpacked, "content"), ".github/agents/copilot.md", "nuget");
        var layout = CreateTempDirectory();
        WriteFile(layout, ".github/agents/copilot.md", "git");
        var commitIdentity = "git-commit:" + new string('e', 40);
        var nugetIdentity = "sha256:" + new string('f', 64);

        // Act
        using var supplied = DirectoryPackageContentReader.Open(unpacked, nugetIdentity);
        using var none = DirectoryPackageContentReader.Open(unpacked);
        using var suppliedLayout = DirectoryPackageContentReader.OpenLayoutRoot(layout, commitIdentity);
        using var noneLayout = DirectoryPackageContentReader.OpenLayoutRoot(layout);

        // Assert
        Assert.Multiple(
            () => Assert.Equal(nugetIdentity, supplied.ContentIdentity),
            () => Assert.Null(none.ContentIdentity),
            () => Assert.Equal(commitIdentity, suppliedLayout.ContentIdentity),
            () => Assert.Null(noneLayout.ContentIdentity));
    }

    /// <summary>
    ///     Test that a folder whose root is the package layout - with a repository's metadata and other
    ///     content next to it - is read and applied as a package: only the four managed folders and the
    ///     two root files are listed and readable, a path outside the layout is refused, applying it
    ///     writes only the managed folders, the release notes and the template are readable, and the
    ///     identity of the folder's content (a commit) is carried along.
    /// </summary>
    [Fact]
    public void RepoSync_LayoutRoot_OnlyTheLayoutIsSeenReadAndApplied()
    {
        // Arrange: a layout root with the package files and things that are not part of the package
        var root = CreateTempDirectory();
        WriteFile(root, ".github/agents/copilot.md", "agents");
        WriteFile(root, ".github/standards/style.md", "standards");
        WriteFile(root, ".github/templates/t.md", "templates");
        WriteFile(root, ".github/skills/s.md", "skills");
        WriteFile(root, ".github/workflows/ci.yml", "not managed");
        WriteFile(root, "release-notes.md", "# Notes");
        WriteFile(root, "AGENTS.md", "# Template");
        WriteFile(root, "README.md", "not part of the package");
        WriteFile(root, "src/app.txt", "not part of the package");
        WriteFile(root, ".git/config", "repository metadata");
        var identity = "git-commit:" + new string('1', 40);
        var repo = CreateTempDirectory();

        // Act
        using var reader = DirectoryPackageContentReader.OpenLayoutRoot(root, identity);
        var listed = reader.ListFiles().Order(StringComparer.Ordinal).ToList();
        PackageContentExtractor.Extract(reader, repo);

        // Assert: only the layout is seen, outside paths are refused, and the identity is carried
        Assert.Multiple(
            () => Assert.Equal(
                new[]
                {
                    ".github/agents/copilot.md", ".github/skills/s.md", ".github/standards/style.md",
                    ".github/templates/t.md", "AGENTS.md", "release-notes.md"
                }.Order(StringComparer.Ordinal),
                listed),
            () => Assert.Throws<InvalidOperationException>(() => reader.OpenRead("README.md")),
            () => Assert.Throws<InvalidOperationException>(() => reader.OpenRead(".git/config")),
            () => Assert.Throws<InvalidOperationException>(() => reader.OpenRead(".github/workflows/ci.yml")),
            () => Assert.Equal(identity, reader.ContentIdentity),
            () => Assert.Equal("# Notes", PackageContentExtractor.ReadReleaseNotes(reader)),
            () => Assert.Equal("# Template", PackageContentExtractor.ReadAgentsMdTemplate(reader)));

        // Assert: the repo got the managed folders and nothing else
        var written = Directory.EnumerateFiles(repo, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(repo, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.Equal(
            [".github/agents/copilot.md", ".github/skills/s.md", ".github/standards/style.md", ".github/templates/t.md"],
            written);
    }

    /// <summary>
    ///     Test that a link which is part of the layout of a layout-root folder is refused before the repo
    ///     is touched, and that a link outside the layout is neither inspected nor followed: applying
    ///     the folder with only an unrelated link succeeds, and replacing a managed folder with a link
    ///     leaves the repo exactly as it was.
    /// </summary>
    [Fact]
    public void RepoSync_LayoutRoot_LinkInTheLayoutIsRefusedWhileLinkOutsideItIsIgnored()
    {
        // Arrange: a layout root with an unrelated link, a repo with files that must survive, and a target outside
        var outside = CreateTempDirectory();
        WriteFile(outside, "payload.md", "outside");
        var root = CreateTempDirectory();
        WriteFile(root, ".github/agents/copilot.md", "agents");
        WriteFile(root, ".github/skills/s.md", "skills");
        WriteFile(root, "docs/readme.md", "docs");
        CreateDirectoryLinkOrSkip(Path.Combine(root, "docs", "linked"), outside);
        var repo = CreateTempDirectory();
        WriteFile(repo, ".github/agents/old.md", "old");
        using var reader = DirectoryPackageContentReader.OpenLayoutRoot(root);

        // Act: the unrelated link is no obstacle
        PackageContentExtractor.Extract(reader, repo);

        // Assert: applied
        Assert.Equal(
            "agents", File.ReadAllText(Path.Combine(repo, ".github", "agents", "copilot.md")));

        // Act: a managed folder of the package is replaced by a link, and the package is applied again
        Directory.Delete(Path.Combine(root, ".github", "skills"), recursive: true);
        CreateDirectoryLinkOrSkip(Path.Combine(root, ".github", "skills"), outside);
        var before = Snapshot(repo);
        var refusal = Record.Exception(() => PackageContentExtractor.Extract(reader, repo));

        // Assert: refused, and the repo is untouched
        Assert.IsType<InvalidOperationException>(refusal);
        Assert.Equal(before, Snapshot(repo));
        Assert.Equal(["payload.md"], Directory.GetFiles(outside).Select(Path.GetFileName));
    }

    /// <summary>
    ///     Writes a minimal agent package zip whose agents file holds the given text.
    /// </summary>
    /// <param name="zipPath">The zip file to create.</param>
    /// <param name="marker">The text of the agents file.</param>
    private static void WriteZip(string zipPath, string marker)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        using var writer = new StreamWriter(archive.CreateEntry(".github/agents/copilot.md").Open(), new UTF8Encoding(false));
        writer.Write(marker);
    }

    /// <summary>
    ///     Computes the expected <c>sha256:</c> identity of some bytes directly with the platform's hash.
    /// </summary>
    /// <param name="bytes">The bytes of the package file.</param>
    /// <returns><c>sha256:</c> followed by the lower-case hexadecimal digest.</returns>
    private static string Sha256Identity(byte[] bytes) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>
    ///     Writes a text file below a folder, creating the folders on the way.
    /// </summary>
    /// <param name="root">The folder.</param>
    /// <param name="relativePath">The <c>/</c>-separated path below the folder.</param>
    /// <param name="content">The file's text.</param>
    private static void WriteFile(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <summary>
    ///     Captures a folder's relative file paths and contents for before/after comparison.
    /// </summary>
    /// <param name="root">The folder to capture.</param>
    /// <returns>A sorted list of <c>path=content</c> lines.</returns>
    private static List<string> Snapshot(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/') + "=" + File.ReadAllText(file))
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    ///     Creates a directory link - a junction on Windows (which needs no privilege), a symbolic link
    ///     elsewhere - or skips the test when the environment does not allow one.
    /// </summary>
    /// <param name="linkPath">The link to create.</param>
    /// <param name="target">The existing directory it points at.</param>
    private static void CreateDirectoryLinkOrSkip(string linkPath, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                Directory.CreateSymbolicLink(linkPath, target);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                Assert.Skip($"Cannot create symbolic links in this environment: {ex.Message}");
            }

            return;
        }

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
        Assert.True(process.ExitCode == 0, $"Creating the junction failed ({process.ExitCode}): {output}");
    }

    /// <summary>
    ///     Creates a unique temporary directory removed when the test ends.
    /// </summary>
    /// <returns>The directory path.</returns>
    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_repo_sync_subsystem_" + Guid.NewGuid().ToString("N"));
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
}
