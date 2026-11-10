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
using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.RepoConfig;
using DemaConsulting.AgentControl.RepoSync;
using DemaConsulting.AgentControl.Settings;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Tests of the content-identity policy of <see cref="RepoCardViewModel"/>: the pin records
///     which bytes a package version delivered, and a later Upgrade, Select Package or launch
///     re-sync of the same version that delivers different bytes is refused or confirmed.
/// </summary>
/// <remarks>
///     Uses real zip-folder sources and real repo folders (a zip is republished by replacing the
///     file, as a maintainer would), and a small stub provider for the source that reports no
///     identity. No git executable and no Avalonia window is involved.
/// </remarks>
public sealed class RepoCardContentIdentityTests : IDisposable
{
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     Test that applying a package records the identity of its files in the pin, and no source
    ///     revision for a zip.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_ApplySelectedPackage_FirstApply_RecordsFilesIdentityInPin()
    {
        // Arrange: an unpinned repo and a source with one zip
        var (repo, sourceDir, card) = CreateRepoWithSource();
        CreateZip(sourceDir, "1.0.0", "v1");

        // Act
        card.ApplySelectedPackage("contoso-agents", "1.0.0");

        // Assert: the pin names the identity of the four agent files, and the zip has no source revision
        var pin = RepoPinStore.Load(repo);
        Assert.Equal(FilesIdentity("v1"), pin?.ContentIdentity);
        Assert.Null(pin?.SourceRevision);
    }

    /// <summary>
    ///     Test that Upgrade refuses a same-version package whose content changed, leaving the
    ///     repo files and the pin untouched and telling the user in plain language.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_UpgradeCommand_SameVersionRepublished_RefusesWithoutChangingRepoOrPin()
    {
        // Arrange: a repo applied from 1.0.0, then the 1.0.0 zip is republished with other content
        var (repo, sourceDir, card) = CreateRepoWithSource();
        CreateZip(sourceDir, "1.0.0", "v1");
        card.ApplySelectedPackage("contoso-agents", "1.0.0");
        var pinBefore = await File.ReadAllTextAsync(PinPath(repo), TestContext.Current.CancellationToken);
        Republish(sourceDir, "1.0.0", "tampered");
        var errors = new List<string>();
        card.ErrorOccurred += (_, message) => errors.Add(message);

        // Act: upgrade, which finds the same version as the latest
        await card.UpgradeCommand.ExecuteAsync();

        // Assert: refused with a clear message, nothing extracted, pin byte-for-byte unchanged
        var message = Assert.Single(errors);
        Assert.Contains("now contains different files", message, StringComparison.Ordinal);
        Assert.Contains("Nothing was changed", message, StringComparison.Ordinal);
        Assert.Contains("Change package...", message, StringComparison.Ordinal);
        Assert.DoesNotContain(".agentcontrol.json", message, StringComparison.Ordinal);
        Assert.DoesNotContain("sha256", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            RepoPinStore.Load(repo)!.ContentIdentity!["files-sha256:".Length..], message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("v1", ReadMarker(repo));
        Assert.Equal(pinBefore, await File.ReadAllTextAsync(PinPath(repo), TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that the launch re-sync refuses a same-version package whose content changed: no
    ///     file is extracted, the pin is not written, and the launch is told it was not synced.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_SameVersionRepublished_RefusesWithoutExtractingOrWritingPin()
    {
        // Arrange: a pinned repo whose managed folders are then missing, and a republished zip
        var (repo, sourceDir, card) = CreateRepoWithSource();
        CreateZip(sourceDir, "1.0.0", "v1");
        card.ApplySelectedPackage("contoso-agents", "1.0.0");
        var pinBefore = File.ReadAllText(PinPath(repo));
        Directory.Delete(Path.Combine(repo, ".github"), recursive: true);
        Republish(sourceDir, "1.0.0", "tampered");
        var errors = new List<string>();
        card.ErrorOccurred += (_, message) => errors.Add(message);

        // Act
        var result = card.EnsureAgentFilesSyncedBeforeLaunch();

        // Assert: not synced, explained, nothing written anywhere in the repo
        Assert.False(result);
        var message = Assert.Single(errors);
        Assert.Contains("not synced", message, StringComparison.Ordinal);
        Assert.Contains("now contains different files", message, StringComparison.Ordinal);
        Assert.Contains("Change package...", message, StringComparison.Ordinal);
        Assert.DoesNotContain(".agentcontrol.json", message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(repo, ".github", "agents")));
        Assert.Equal(pinBefore, File.ReadAllText(PinPath(repo)));
    }

    /// <summary>
    ///     Test that Select Package of the pinned version with changed content asks for
    ///     confirmation, extracts nothing and reports no error until the user answers.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_ApplySelectedPackage_SameVersionRepublished_RequestsConfirmationAndChangesNothing()
    {
        // Arrange: applied from 1.0.0, then republished
        var (repo, sourceDir, card) = CreateRepoWithSource();
        CreateZip(sourceDir, "1.0.0", "v1");
        card.ApplySelectedPackage("contoso-agents", "1.0.0");
        var pinBefore = File.ReadAllText(PinPath(repo));
        Republish(sourceDir, "1.0.0", "tampered");
        var requests = new List<ContentChangeConfirmationRequest>();
        var errors = new List<string>();
        card.ContentChangeConfirmationRequested += (_, request) => requests.Add(request);
        card.ErrorOccurred += (_, message) => errors.Add(message);

        // Act: select the same version again
        card.ApplySelectedPackage("contoso-agents", "1.0.0");

        // Assert: one question naming the package, nothing changed, no error
        var asked = Assert.Single(requests);
        Assert.Equal(new PackageReference("Default", "contoso-agents", "1.0.0"), asked.Selection);
        Assert.Contains("contoso-agents 1.0.0", asked.Message, StringComparison.Ordinal);
        Assert.Contains("now contains different files", asked.Message, StringComparison.Ordinal);
        Assert.Empty(errors);
        Assert.Equal("v1", ReadMarker(repo));
        Assert.Equal(pinBefore, File.ReadAllText(PinPath(repo)));
    }

    /// <summary>
    ///     Test that, once the user has confirmed, the changed package is applied and the new
    ///     identity replaces the recorded one.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_ApplySelectedPackage_SameVersionRepublishedAndAccepted_AppliesAndRecordsNewIdentity()
    {
        // Arrange: applied from 1.0.0, then republished
        var (repo, sourceDir, card) = CreateRepoWithSource();
        CreateZip(sourceDir, "1.0.0", "v1");
        card.ApplySelectedPackage("contoso-agents", "1.0.0");
        Republish(sourceDir, "1.0.0", "tampered");
        var errors = new List<string>();
        card.ErrorOccurred += (_, message) => errors.Add(message);

        // Act: select it again with the user's confirmation
        await card.ApplySelectedPackageAsync(
            new PackageReference("Default", "contoso-agents", "1.0.0"),
            TestContext.Current.CancellationToken,
            acceptContentChange: true);

        // Assert: the changed content is in the repo and the pin names its identity
        Assert.Empty(errors);
        Assert.Equal("tampered", ReadMarker(repo));
        Assert.Equal(FilesIdentity("tampered"), RepoPinStore.Load(repo)?.ContentIdentity);
    }

    /// <summary>
    ///     Test that a changed package with nobody to ask is refused rather than applied.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_ApplySelectedPackage_SameVersionRepublishedWithoutSubscriber_IsRefused()
    {
        // Arrange: applied from 1.0.0, then republished; nothing subscribes to the confirmation
        var (repo, sourceDir, card) = CreateRepoWithSource();
        CreateZip(sourceDir, "1.0.0", "v1");
        card.ApplySelectedPackage("contoso-agents", "1.0.0");
        Republish(sourceDir, "1.0.0", "tampered");
        var errors = new List<string>();
        card.ErrorOccurred += (_, message) => errors.Add(message);

        // Act
        card.ApplySelectedPackage("contoso-agents", "1.0.0");

        // Assert
        Assert.Contains("now contains different files", Assert.Single(errors), StringComparison.Ordinal);
        Assert.Equal("v1", ReadMarker(repo));
    }

    /// <summary>
    ///     Test that a pin without an identity (made before identities were recorded) adopts one
    ///     on Select Package without any warning, even though the zip is whatever it is now.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_ApplySelectedPackage_PinWithoutIdentity_AdoptsIdentityWithoutWarning()
    {
        // Arrange: a v2-style pin (no identity) for 1.0.0, and the zip as it is now
        var (repo, sourceDir, card) = CreateRepoWithSource();
        CreateZip(sourceDir, "1.0.0", "v1");
        RepoPinStore.Save(repo, new RepoPin { Source = "Default", PackageName = "contoso-agents", Version = "1.0.0" });
        var events = new List<string>();
        card.ErrorOccurred += (_, message) => events.Add("error: " + message);
        card.ContentChangeConfirmationRequested += (_, _) => events.Add("confirmation");

        // Act: select the same version
        card.ApplySelectedPackage("contoso-agents", "1.0.0");

        // Assert: applied silently and the identity is now recorded
        Assert.Empty(events);
        Assert.Equal("v1", ReadMarker(repo));
        Assert.Equal(FilesIdentity("v1"), RepoPinStore.Load(repo)?.ContentIdentity);
    }

    /// <summary>
    ///     Test that moving to a different version never compares identities and records the new
    ///     version's identity.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_UpgradeCommand_NewerVersion_RecordsNewIdentityWithoutWarning()
    {
        // Arrange: applied from 1.0.0, and a 2.0.0 zip appears
        var (repo, sourceDir, card) = CreateRepoWithSource();
        CreateZip(sourceDir, "1.0.0", "v1");
        card.ApplySelectedPackage("contoso-agents", "1.0.0");
        CreateZip(sourceDir, "2.0.0", "v2");
        var events = new List<string>();
        card.ErrorOccurred += (_, message) => events.Add("error: " + message);
        card.ContentChangeConfirmationRequested += (_, _) => events.Add("confirmation");

        // Act
        await card.UpgradeCommand.ExecuteAsync();

        // Assert
        Assert.Empty(events);
        var pin = RepoPinStore.Load(repo);
        Assert.Equal("2.0.0", pin?.Version);
        Assert.Equal(FilesIdentity("v2"), pin?.ContentIdentity);
        Assert.Equal("v2", ReadMarker(repo));
    }

    /// <summary>
    ///     Test that the launch re-sync of a pin without an identity extracts silently and never
    ///     writes the pin (so the working tree is not dirtied and nothing is adopted at launch).
    /// </summary>
    [Fact]
    public void RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_PinWithoutIdentity_SyncsSilentlyAndNeverWritesPin()
    {
        // Arrange: a v2-style pin without identity, missing managed folders, and a source zip
        var (repo, sourceDir, card) = CreateRepoWithSource();
        CreateZip(sourceDir, "1.0.0", "v1");
        File.WriteAllText(
            PinPath(repo),
            """{ "SchemaVersion": 2, "Source": "Default", "PackageName": "contoso-agents", "Version": "1.0.0" }""");
        var pinBefore = File.ReadAllText(PinPath(repo));
        card = CreateCard(repo, sourceDir, "contoso-agents", "1.0.0");
        var errors = new List<string>();
        card.ErrorOccurred += (_, message) => errors.Add(message);

        // Act
        var result = card.EnsureAgentFilesSyncedBeforeLaunch();

        // Assert: synced, no warning, and the pin file is exactly as it was
        Assert.True(result);
        Assert.Empty(errors);
        Assert.Equal("v1", ReadMarker(repo));
        Assert.Equal(pinBefore, File.ReadAllText(PinPath(repo)));
    }

    /// <summary>
    ///     Test that the launch re-sync of unchanged content extracts and leaves the pin untouched.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_UnchangedContent_SyncsAndNeverWritesPin()
    {
        // Arrange: applied (identity recorded), then the managed folders go missing
        var (repo, sourceDir, card) = CreateRepoWithSource();
        CreateZip(sourceDir, "1.0.0", "v1");
        card.ApplySelectedPackage("contoso-agents", "1.0.0");
        var pinBefore = File.ReadAllText(PinPath(repo));
        Directory.Delete(Path.Combine(repo, ".github"), recursive: true);
        var errors = new List<string>();
        card.ErrorOccurred += (_, message) => errors.Add(message);

        // Act
        var result = card.EnsureAgentFilesSyncedBeforeLaunch();

        // Assert
        Assert.True(result);
        Assert.Empty(errors);
        Assert.Equal("v1", ReadMarker(repo));
        Assert.Equal(pinBefore, File.ReadAllText(PinPath(repo)));
    }

    /// <summary>
    ///     Test that declining the AGENTS.md template offer raised by the launch re-sync does not
    ///     write the pin, because the launch re-sync never writes the pin.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_AgentsMdOfferDeclined_NeverWritesPin()
    {
        // Arrange: a pinned repo whose package carries an AGENTS.md template and whose managed
        // folders are missing; the offer that the re-sync raises is declined at once
        var (repo, sourceDir, card) = CreateRepoWithSource();
        CreateZip(sourceDir, "1.0.0", "v1", includeAgentsMd: true);
        card.ApplySelectedPackage("contoso-agents", "1.0.0");
        var pinBefore = File.ReadAllBytes(PinPath(repo));
        Directory.Delete(Path.Combine(repo, ".github"), recursive: true);
        var offers = 0;
        card.AgentsMdTemplateOfferRequested += (_, _) =>
        {
            offers++;
            card.DeclineAgentsMdTemplate();
        };

        // Act
        var result = card.EnsureAgentFilesSyncedBeforeLaunch();

        // Assert: the offer was made and declined, and the pin file is byte-for-byte unchanged
        Assert.True(result);
        Assert.Equal(1, offers);
        Assert.Equal(pinBefore, File.ReadAllBytes(PinPath(repo)));
        Assert.False(RepoPinStore.Load(repo)?.AgentsMdTemplateDeclined);
    }

    /// <summary>
    ///     Test that declining the AGENTS.md template offer raised by an explicit apply is still
    ///     remembered in the pin, as before.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_ApplySelectedPackage_AgentsMdOfferDeclined_RecordsDeclineInPin()
    {
        // Arrange
        var (repo, sourceDir, card) = CreateRepoWithSource();
        CreateZip(sourceDir, "1.0.0", "v1", includeAgentsMd: true);
        card.AgentsMdTemplateOfferRequested += (_, _) => card.DeclineAgentsMdTemplate();

        // Act
        card.ApplySelectedPackage("contoso-agents", "1.0.0");

        // Assert
        Assert.True(RepoPinStore.Load(repo)?.AgentsMdTemplateDeclined);
    }

    /// <summary>
    ///     Test that a source that reports no identity is never treated as a change, even when the
    ///     pin recorded one: there is nothing to compare, so the package is applied.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_ApplySelectedPackage_SourceReportsNoIdentity_IsNeverTreatedAsChanged()
    {
        // Arrange: a pin that recorded an identity, and a source whose reader reports none
        var repo = CreateTempDirectory();
        RepoPinStore.Save(
            repo,
            new RepoPin
            {
                Source = "Default",
                PackageName = "contoso-agents",
                Version = "1.0.0",
                ContentIdentity = "files-sha256:" + new string('a', 64)
            });
        var provider = new NoIdentityProvider();
        var settings = new AppSettings { Sources = [new PackageSourceDefinition { Alias = "Default", Type = PackageSourceType.Zip, Location = "unused" }] };
        var registry = new PackageSourceRegistry(settings.Sources, _ => provider);
        var card = new RepoCardViewModel(
            new RecentRepo { Path = repo }, () => settings, new PackageVersionCache(), null, _ => registry);
        var events = new List<string>();
        card.ErrorOccurred += (_, message) => events.Add("error: " + message);
        card.ContentChangeConfirmationRequested += (_, _) => events.Add("confirmation");

        // Act
        card.ApplySelectedPackage("contoso-agents", "1.0.0");

        // Assert: applied, and the pin no longer claims an identity the source cannot vouch for
        Assert.Empty(events);
        Assert.Equal("stub", ReadMarker(repo));
        Assert.Null(RepoPinStore.Load(repo)?.ContentIdentity);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var path in _tempPaths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup; leftover temp folders do not fail the test.
            }
        }
    }

    /// <summary>
    ///     Creates a repo folder, a zip source folder, and a card for the repo with the source
    ///     configured as the legacy Default source.
    /// </summary>
    private (string Repo, string SourceDir, RepoCardViewModel Card) CreateRepoWithSource()
    {
        var repo = CreateTempDirectory();
        var sourceDir = CreateTempDirectory();
        return (repo, sourceDir, CreateCard(repo, sourceDir, null, null));
    }

    /// <summary>
    ///     Creates a card for a repo whose cached pin fields are as given.
    /// </summary>
    private static RepoCardViewModel CreateCard(string repo, string sourceDir, string? pinnedName, string? pinnedVersion)
    {
        var settings = new AppSettings { PackageSourcePath = sourceDir };
        var recentRepo = new RecentRepo
        {
            Path = repo,
            PinnedPackageName = pinnedName,
            PinnedPackageVersion = pinnedVersion,
            PinnedSource = pinnedName is null ? null : "Default"
        };
        return new RepoCardViewModel(recentRepo, () => settings, new PackageVersionCache());
    }

    /// <summary>
    ///     Creates a package zip of <c>contoso-agents</c> whose agents file holds the given marker.
    /// </summary>
    private static void CreateZip(string sourceDir, string version, string marker, bool includeAgentsMd = false)
    {
        var zipPath = Path.Combine(sourceDir, $"contoso-agents-{version}.zip");
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var entries = new List<(string Name, string Content)>
        {
            (".github/agents/copilot.md", marker),
            (".github/standards/style.md", "standards"),
            (".github/templates/template.md", "templates"),
            (".github/skills/skill.md", "skills")
        };
        if (includeAgentsMd)
        {
            entries.Add(("AGENTS.md", "# Agents template"));
        }

        foreach (var (name, content) in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write(content);
        }
    }

    /// <summary>
    ///     Replaces a version's zip with one holding different content, as a republishing
    ///     maintainer would.
    /// </summary>
    private static void Republish(string sourceDir, string version, string marker)
    {
        File.Delete(Path.Combine(sourceDir, $"contoso-agents-{version}.zip"));
        CreateZip(sourceDir, version, marker);
    }

    /// <summary>
    ///     Computes the files identity of the four agent files <see cref="CreateZip"/> writes, independently
    ///     of the code under test: SHA-256 of, per file in ordinal path order, the UTF-8 path, a zero byte,
    ///     the length as eight little-endian bytes and the bytes.
    /// </summary>
    /// <param name="marker">The text of the agents file.</param>
    /// <returns><c>files-sha256:</c> followed by the lower-case hexadecimal digest.</returns>
    private static string FilesIdentity(string marker)
    {
        var files = new (string Path, string Text)[]
        {
            (".github/agents/copilot.md", marker),
            (".github/skills/skill.md", "skills"),
            (".github/standards/style.md", "standards"),
            (".github/templates/template.md", "templates")
        };
        using var stream = new MemoryStream();
        foreach (var (path, text) in files)
        {
            var content = System.Text.Encoding.UTF8.GetBytes(text);
            stream.Write(System.Text.Encoding.UTF8.GetBytes(path));
            stream.WriteByte(0);
            stream.Write(BitConverter.GetBytes((long)content.Length));
            stream.Write(content);
        }

        return "files-sha256:" + Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    /// <summary>
    ///     Reads the marker the package wrote into the repo's agents file.
    /// </summary>
    private static string ReadMarker(string repo) =>
        File.ReadAllText(Path.Combine(repo, ".github", "agents", "copilot.md"));

    /// <summary>
    ///     Gets the path of a repo's pin file.
    /// </summary>
    private static string PinPath(string repo) => Path.Combine(repo, ".agentcontrol.json");

    /// <summary>
    ///     Creates a unique temporary directory removed by <see cref="Dispose"/>.
    /// </summary>
    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_content_identity_test_" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        return path;
    }

    /// <summary>
    ///     A source that serves one package whose reader reports no content identity.
    /// </summary>
    private sealed class NoIdentityProvider : IPackageSourceProvider
    {
        /// <inheritdoc />
        public string Alias => "Default";

        /// <inheritdoc />
        public Task<IReadOnlyList<string>> ListPackageIdsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(["contoso-agents"]);

        /// <inheritdoc />
        public Task<IReadOnlyList<DiscoveredPackage>> ListVersionsAsync(
            string packageId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DiscoveredPackage>>(
                [new DiscoveredPackage("contoso-agents", new PackageVersion(1, 0, 0), "1.0.0", "Default")]);

        /// <inheritdoc />
        public Task<IPackageContentReader> AcquireAsync(DiscoveredPackage package, CancellationToken cancellationToken) =>
            Task.FromResult<IPackageContentReader>(new NoIdentityReader());
    }

    /// <summary>
    ///     A reader over a fixed set of files that does not override
    ///     <see cref="IPackageContentReader.ContentIdentity"/>, so it reports none.
    /// </summary>
    private sealed class NoIdentityReader : IPackageContentReader
    {
        private static readonly string[] ManagedFiles =
        [
            ".github/agents/copilot.md",
            ".github/standards/style.md",
            ".github/templates/template.md",
            ".github/skills/skill.md"
        ];

        /// <inheritdoc />
        public string Description => "stub package";

        /// <inheritdoc />
        public IReadOnlyList<string> ListFiles() => ManagedFiles;

        /// <inheritdoc />
        public Stream OpenRead(string relativePath) =>
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(relativePath.EndsWith("copilot.md", StringComparison.Ordinal) ? "stub" : "other"));

        /// <inheritdoc />
        public string? ReadText(string relativePath) => null;

        /// <inheritdoc />
        public void Dispose()
        {
            // Nothing to release.
        }
    }
}
