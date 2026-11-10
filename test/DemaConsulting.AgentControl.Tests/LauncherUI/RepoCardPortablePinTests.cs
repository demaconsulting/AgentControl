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
using System.Text;
using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.RepoConfig;
using DemaConsulting.AgentControl.Settings;
using DemaConsulting.AgentControl.Tests.AgentPackageManagement;
using Material.Icons;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Tests of the "portable pin" behavior of <see cref="RepoCardViewModel"/>: a repo's pin names a
///     preferred source alias, but teammates whose configuration has another alias (or another kind of
///     source) still get the same agent files, because a pin that recorded the identity of its files
///     accepts any configured source that delivers files with exactly that identity - and never a source
///     whose files differ, or any source at all for a pin that recorded no files identity.
/// </summary>
/// <remarks>
///     Uses real zip-folder sources (and, for the cross-kind cases, a real NuGet folder feed and a real
///     local git repository) and real repo folders: a "teammate" is a card whose settings configure other
///     sources than the ones the pin was made with. No window is opened.
/// </remarks>
public sealed class RepoCardPortablePinTests : IDisposable
{
    /// <summary>
    ///     The package every test uses.
    /// </summary>
    private const string PackageId = "contoso-agents";

    /// <summary>
    ///     The folders to delete after a test.
    /// </summary>
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     Test that a pin whose alias is not configured here but whose files another configured source
    ///     delivers identically (repacked, with other release notes) shows no source problem, names the
    ///     source that supplies the files on its chip with an explaining tooltip, and leaves the pin as it was.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_Refresh_PinnedAliasNotConfiguredAndOtherSourceHasIdenticalFiles_NamesSupplierWithoutProblem()
    {
        // Arrange: a repo pinned to "Contoso", and a teammate who only has "Artifactory" with the same files repacked
        var (repo, pinBefore) = CreatePinnedRepo("Contoso", "v1");
        var artifactory = CreateSourceFolder("v1", repack: true);
        var settings = new AppSettings { Sources = [ZipSource("Artifactory", artifactory)] };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());

        // Act
        card.RefreshCheap();
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert: no problem, the supplier is named, and the pin is exactly as it was
        Assert.False(card.HasSourceProblem);
        Assert.Equal(RepoCardStatus.UpToDate, card.Status);
        Assert.True(card.IsPinnedSourceSubstituted);
        Assert.Equal("Artifactory", card.PinnedSourceLabel);
        Assert.Equal("Contoso", card.PinnedSource);
        Assert.Equal(MaterialIconKind.FolderZip, card.PinnedSourceIcon);
        Assert.Contains(
            "Pinned source 'Contoso' is not configured; the identical files come from 'Artifactory'.",
            card.PackageChipTooltip,
            StringComparison.Ordinal);
        Assert.Equal(pinBefore, await File.ReadAllTextAsync(PinPath(repo), TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that when several configured sources deliver the identical files, the first of them in
    ///     Settings order supplies the pinned package, and that a source that offers the same name and
    ///     version with different files before it is passed over.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_Refresh_SeveralSources_UsesFirstSourceWithIdenticalFilesInSettingsOrder()
    {
        // Arrange: Mirror1 has other files under the same version, Mirror2 and Mirror3 have the pinned files
        var (repo, _) = CreatePinnedRepo("Contoso", "v1");
        var settings = new AppSettings
        {
            Sources =
            [
                ZipSource("Mirror1", CreateSourceFolder("other files")),
                ZipSource("Mirror2", CreateSourceFolder("v1")),
                ZipSource("Mirror3", CreateSourceFolder("v1"))
            ]
        };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());

        // Act
        card.RefreshCheap();
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(card.HasSourceProblem);
        Assert.Equal("Mirror2", card.PinnedSourceLabel);
    }

    /// <summary>
    ///     Test that a pin whose alias is not configured and whose files no other configured source
    ///     delivers identically is a source problem that says so: the pinned source is not configured and
    ///     no other configured source delivers the identical files, and no source is substituted.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_Refresh_PinnedAliasNotConfiguredAndOtherSourceHasDifferentFiles_ShowsSourceProblem()
    {
        // Arrange: the teammate's only source offers the same package name and version with other files
        var (repo, _) = CreatePinnedRepo("Contoso", "v1");
        var settings = new AppSettings { Sources = [ZipSource("Artifactory", CreateSourceFolder("other files"))] };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());

        // Act
        card.RefreshCheap();
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert: the red chip with an explanation, no substitute, no upgrade
        Assert.True(card.HasSourceProblem);
        Assert.Equal("Package source problem", card.SourceProblemChipText);
        Assert.Contains("'Contoso' is not configured", card.SourceProblem, StringComparison.Ordinal);
        Assert.Contains("no other configured source delivers the identical files", card.SourceProblem, StringComparison.Ordinal);
        Assert.False(card.IsPinnedSourceSubstituted);
        Assert.Equal("Contoso", card.PinnedSourceLabel);
        Assert.Equal(RepoCardStatus.Problem, card.Status);
        Assert.False(card.IsUpgradeAvailable);
    }

    /// <summary>
    ///     Test that a pin that recorded no files identity never falls back to another source, however
    ///     well another source's package name and version match: there is nothing to verify it against.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_Refresh_PinWithoutFilesIdentityAndAliasNotConfigured_NeverFallsBack()
    {
        // Arrange: a pin without an identity (schema 2) to "Contoso", and another source with that very package
        var repo = CreateTempDirectory();
        await File.WriteAllTextAsync(
            PinPath(repo),
            $$"""{ "SchemaVersion": 2, "Source": "Contoso", "PackageName": "{{PackageId}}", "Version": "1.0.0" }""",
            TestContext.Current.CancellationToken);
        var settings = new AppSettings { Sources = [ZipSource("Artifactory", CreateSourceFolder("v1"))] };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());

        // Act
        card.RefreshCheap();
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert: a source problem that explains why nothing else was tried, and nothing substituted
        Assert.True(card.HasSourceProblem);
        Assert.Contains("'Contoso' is not configured", card.SourceProblem, StringComparison.Ordinal);
        Assert.Contains("no file fingerprint", card.SourceProblem, StringComparison.Ordinal);
        Assert.False(card.IsPinnedSourceSubstituted);
        Assert.False(card.IsUpgradeAvailable);
    }

    /// <summary>
    ///     Test that a pin whose alias is configured and offers the pinned version is served by that
    ///     source as always: its alias is on the chip and the tooltip says nothing about a substitute.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_Refresh_PinnedAliasConfigured_ShowsPinnedSourceWithoutSubstitution()
    {
        // Arrange: the pin's own alias is configured, besides another source with the same files
        var (repo, _) = CreatePinnedRepo("Contoso", "v1", out var contosoFolder);
        var settings = new AppSettings
        {
            Sources = [ZipSource("Artifactory", CreateSourceFolder("v1")), ZipSource("Contoso", contosoFolder)]
        };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());

        // Act
        card.RefreshCheap();
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(card.HasSourceProblem);
        Assert.False(card.IsPinnedSourceSubstituted);
        Assert.Equal("Contoso", card.PinnedSourceLabel);
        Assert.DoesNotContain("Pinned source", card.PackageChipTooltip, StringComparison.Ordinal);
        Assert.Equal(string.Empty, card.SourceSubstitutionNote);
    }

    /// <summary>
    ///     Test that a pin whose alias is configured but does not offer the pinned version is supplied by
    ///     another source with the identical files, and that the tooltip says the pinned source does not
    ///     offer the version.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_Refresh_PinnedAliasDoesNotOfferVersionAndOtherSourceHasIdenticalFiles_NamesSupplier()
    {
        // Arrange: "Contoso" is configured but now only has another version; "Artifactory" has the pinned files
        var (repo, _) = CreatePinnedRepo("Contoso", "v1", out var contosoFolder);
        File.Delete(Path.Combine(contosoFolder, $"{PackageId}-1.0.0.zip"));
        WriteZip(contosoFolder, "2.0.0", "v2");
        var settings = new AppSettings
        {
            Sources = [ZipSource("Contoso", contosoFolder), ZipSource("Artifactory", CreateSourceFolder("v1"))]
        };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());

        // Act
        card.RefreshCheap();
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(card.HasSourceProblem);
        Assert.True(card.IsPinnedSourceSubstituted);
        Assert.Equal("Artifactory", card.PinnedSourceLabel);
        Assert.Contains(
            "Pinned source 'Contoso' does not offer contoso-agents 1.0.0; the identical files come from 'Artifactory'.",
            card.PackageChipTooltip,
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the substitute is dropped when the pinned alias is configured again: the chip shows
    ///     the pinned source once more.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_Refresh_PinnedAliasConfiguredAgain_DropsSubstitute()
    {
        // Arrange: the substitute is in use
        var (repo, _) = CreatePinnedRepo("Contoso", "v1", out var contosoFolder);
        var settings = new AppSettings { Sources = [ZipSource("Artifactory", CreateSourceFolder("v1"))] };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());
        card.RefreshCheap();
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);
        Assert.True(card.IsPinnedSourceSubstituted);

        // Act: the user adds the "Contoso" source to Settings
        settings.Sources = [ZipSource("Artifactory", CreateSourceFolder("v1")), ZipSource("Contoso", contosoFolder)];
        card.RefreshCheap();
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(card.IsPinnedSourceSubstituted);
        Assert.Equal("Contoso", card.PinnedSourceLabel);
        Assert.False(card.HasSourceProblem);
    }

    /// <summary>
    ///     Test that the launch re-sync of a fresh clone restores the pinned files from another configured
    ///     source that delivers the identical files, and never writes the pin.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_PinnedAliasNotConfigured_RestoresFromIdenticalSourceWithoutWritingPin()
    {
        // Arrange: a repo pinned to "Contoso" whose managed folders are missing, and a teammate with "Artifactory"
        var (repo, pinBefore) = CreatePinnedRepo("Contoso", "v1");
        Directory.Delete(Path.Combine(repo, ".github"), recursive: true);
        var settings = new AppSettings { Sources = [ZipSource("Artifactory", CreateSourceFolder("v1", repack: true))] };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());
        card.RefreshCheap();
        var errors = new List<string>();
        card.ErrorOccurred += (_, message) => errors.Add(message);

        // Act
        var result = await card.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);

        // Assert: restored, no warning, and the pin keeps the alias it had
        Assert.True(result);
        Assert.Empty(errors);
        Assert.Equal("v1", ReadMarker(repo));
        Assert.Equal(pinBefore, await File.ReadAllTextAsync(PinPath(repo), TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that the launch re-sync does not use a source whose package of that name and version has
    ///     other files: nothing is extracted, and the warning says the pinned source is not configured and
    ///     no other configured source delivers the identical files.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_OnlyOtherFilesAvailable_RefusesWithExplanation()
    {
        // Arrange: the teammate's source has the version, but with other files
        var (repo, pinBefore) = CreatePinnedRepo("Contoso", "v1");
        Directory.Delete(Path.Combine(repo, ".github"), recursive: true);
        var settings = new AppSettings { Sources = [ZipSource("Artifactory", CreateSourceFolder("other files"))] };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());
        card.RefreshCheap();
        var errors = new List<string>();
        card.ErrorOccurred += (_, message) => errors.Add(message);

        // Act
        var result = await card.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
        var message = Assert.Single(errors);
        Assert.Contains("'Contoso' is not configured", message, StringComparison.Ordinal);
        Assert.Contains("no other configured source delivers the identical files", message, StringComparison.Ordinal);
        Assert.Contains("Launching without syncing agent files", message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(repo, ".github")));
        Assert.Equal(pinBefore, await File.ReadAllTextAsync(PinPath(repo), TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that the launch re-sync of a pin that recorded no files identity never uses another source,
    ///     even one with that package name and version: nothing is extracted.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_PinWithoutFilesIdentity_NeverFallsBack()
    {
        // Arrange: a schema 3 pin whose identity is the old kind (which counts as no recorded identity)
        var repo = CreateTempDirectory();
        await File.WriteAllTextAsync(
            PinPath(repo),
            $$"""{ "SchemaVersion": 3, "Source": "Contoso", "PackageName": "{{PackageId}}", "Version": "1.0.0", "ContentIdentity": "sha256:{{new string('a', 64)}}" }""",
            TestContext.Current.CancellationToken);
        var settings = new AppSettings { Sources = [ZipSource("Artifactory", CreateSourceFolder("v1"))] };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());
        card.RefreshCheap();
        var errors = new List<string>();
        card.ErrorOccurred += (_, message) => errors.Add(message);

        // Act
        var result = await card.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
        Assert.Contains("no file fingerprint", Assert.Single(errors), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(repo, ".github")));
    }

    /// <summary>
    ///     Test that the launch re-sync restores from another source when the pinned alias is configured
    ///     but no longer offers the pinned version.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_PinnedAliasLacksVersion_RestoresFromIdenticalSource()
    {
        // Arrange: "Contoso" is configured but only has 2.0.0 now; "Artifactory" has the pinned 1.0.0 files
        var (repo, pinBefore) = CreatePinnedRepo("Contoso", "v1", out var contosoFolder);
        Directory.Delete(Path.Combine(repo, ".github"), recursive: true);
        File.Delete(Path.Combine(contosoFolder, $"{PackageId}-1.0.0.zip"));
        WriteZip(contosoFolder, "2.0.0", "v2");
        var settings = new AppSettings
        {
            Sources = [ZipSource("Contoso", contosoFolder), ZipSource("Artifactory", CreateSourceFolder("v1"))]
        };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());
        card.RefreshCheap();
        var errors = new List<string>();
        card.ErrorOccurred += (_, message) => errors.Add(message);

        // Act
        var result = await card.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        Assert.Empty(errors);
        Assert.Equal("v1", ReadMarker(repo));
        Assert.Equal(pinBefore, await File.ReadAllTextAsync(PinPath(repo), TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a pinned source that is configured and offers the version but now delivers other files
    ///     is refused as a republished package, even though another configured source has the original files:
    ///     the fallback is for a pinned source that cannot supply the version, not for one that changed it.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_PinnedSourceRepublished_IsRefusedNotServedByAnotherSource()
    {
        // Arrange: "Contoso" republished 1.0.0 with other files; "Artifactory" still has the original files
        var (repo, _) = CreatePinnedRepo("Contoso", "v1", out var contosoFolder);
        Directory.Delete(Path.Combine(repo, ".github"), recursive: true);
        File.Delete(Path.Combine(contosoFolder, $"{PackageId}-1.0.0.zip"));
        WriteZip(contosoFolder, "1.0.0", "tampered");
        var settings = new AppSettings
        {
            Sources = [ZipSource("Contoso", contosoFolder), ZipSource("Artifactory", CreateSourceFolder("v1"))]
        };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());
        card.RefreshCheap();
        var errors = new List<string>();
        card.ErrorOccurred += (_, message) => errors.Add(message);

        // Act
        var result = await card.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
        Assert.Contains("now contains different files", Assert.Single(errors), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(repo, ".github")));
    }

    /// <summary>
    ///     Test that a schema 3 pin whose identity is the old SHA-256 of the package file is never a reason
    ///     to refuse the launch re-sync: the identity is not comparable with one of the files, so the
    ///     package is simply synced (and the pin is not written).
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_Schema3PinWithContainerIdentity_SyncsWithoutRefusing()
    {
        // Arrange: a schema 3 pin to the configured "Default" source whose old identity matches no current bytes
        var sourceDir = CreateSourceFolder("v1");
        var repo = CreateTempDirectory();
        const string pinJson =
            $$"""{ "SchemaVersion": 3, "Source": "Default", "PackageName": "{{PackageId}}", "Version": "1.0.0", "ContentIdentity": "sha256:0000000000000000000000000000000000000000000000000000000000000000" }""";
        await File.WriteAllTextAsync(PinPath(repo), pinJson, TestContext.Current.CancellationToken);
        var settings = new AppSettings { PackageSourcePath = sourceDir };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());
        card.RefreshCheap();
        var errors = new List<string>();
        card.ErrorOccurred += (_, message) => errors.Add(message);

        // Act
        var result = await card.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        Assert.Empty(errors);
        Assert.Equal("v1", ReadMarker(repo));
        Assert.Equal(pinJson, await File.ReadAllTextAsync(PinPath(repo), TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a pin made from a NuGet folder source is served by a zip source of the same files, the
    ///     two kinds delivering identical files in different containers.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_Refresh_PinMadeFromNuGetAndTeammateHasZipSource_IsSuppliedByZip()
    {
        // Arrange: a repo applied from a NuGet folder feed, and a teammate with a zip folder of the same files
        var feed = CreateTempDirectory();
        NuGetFeedBuilder.CreateAgentPackage(feed, PackageId, "1.0.0", marker: "v1");
        var repo = CreateTempDirectory();
        var nugetRoot = CreateTempDirectory();
        var pinnedSettings = new AppSettings
        {
            Sources = [new PackageSourceDefinition { Alias = "Feed", Type = PackageSourceType.NuGet, Location = feed }]
        };
        var pinnedCard = new RepoCardViewModel(
            new RecentRepo { Path = repo },
            () => pinnedSettings,
            new PackageVersionCache(),
            registryFactory: current => PackageSourceRegistry.Create(current.GetEffectiveSources(), nugetRoot));
        await pinnedCard.ApplySelectedPackageAsync(
            new PackageReference("Feed", PackageId, "1.0.0"), TestContext.Current.CancellationToken);
        Assert.Equal("Feed", RepoPinStore.Load(repo)?.Source);
        var settings = new AppSettings { Sources = [ZipSource("Zips", CreateSourceFolder("v1"))] };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());

        // Act
        card.RefreshCheap();
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(card.HasSourceProblem);
        Assert.Equal("Zips", card.PinnedSourceLabel);
        Assert.Equal("Feed", card.PinnedSource);
    }

    /// <summary>
    ///     Test that a pin made from a zip source is restored at launch from a real Git source of the same
    ///     files (the Git layout in a real local repository), and that the pin - which has no source revision
    ///     for a zip - is not written.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_PinMadeFromZipAndTeammateHasGitSource_RestoresFromGit()
    {
        // Arrange: the same files committed and tagged in a repository, and a repo pinned from a zip
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        var files = AgentFiles("v1").ToDictionary(file => file.Path, file => file.Text);
        files["release-notes.md"] = "notes of the tag";
        fixture.Commit(files, "one");
        fixture.Tag("v1.0.0", annotated: true);
        fixture.Push();
        var (repo, pinBefore) = CreatePinnedRepo("Contoso", "v1");
        Directory.Delete(Path.Combine(repo, ".github"), recursive: true);
        var settings = new AppSettings { Sources = [fixture.CreateDefinition()] };
        var card = new RepoCardViewModel(
            new RecentRepo { Path = repo },
            () => settings,
            new PackageVersionCache(),
            registryFactory: current => PackageSourceRegistry.Create(current.GetEffectiveSources(), git: fixture.CreateOptions()));
        card.RefreshCheap();
        var errors = new List<string>();
        card.ErrorOccurred += (_, message) => errors.Add(message);

        // Act
        var result = await card.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert: restored from the Git source, which the chip names, and the pin is as it was
        Assert.True(result);
        Assert.Empty(errors);
        Assert.Equal("v1", ReadMarker(repo));
        Assert.Equal(pinBefore, await File.ReadAllTextAsync(PinPath(repo), TestContext.Current.CancellationToken));
        Assert.False(card.HasSourceProblem);
        Assert.Equal("Repo", card.PinnedSourceLabel);
        Assert.Equal(MaterialIconKind.Git, card.PinnedSourceIcon);
    }

    /// <summary>
    ///     Test that, for a pin whose alias is not configured, the upgrade check considers the configured
    ///     sources that offer the package and the newest version wins, and that Upgrade applies it from the
    ///     source that offers it and records that source's alias.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_UpgradeCommand_PinnedAliasNotConfigured_UsesNewestVersionAcrossSourcesAndRecordsItsAlias()
    {
        // Arrange: Mirror1 has the pinned files and 2.0.0, Mirror2 has the pinned files and 3.0.0
        var (repo, _) = CreatePinnedRepo("Contoso", "v1");
        var mirror1 = CreateSourceFolder("v1");
        WriteZip(mirror1, "2.0.0", "v2");
        var mirror2 = CreateSourceFolder("v1");
        WriteZip(mirror2, "3.0.0", "v3");
        var settings = new AppSettings { Sources = [ZipSource("Mirror1", mirror1), ZipSource("Mirror2", mirror2)] };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());
        card.RefreshCheap();
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);
        Assert.True(card.IsUpgradeAvailable);
        Assert.Equal("3.0.0", card.LatestAvailableVersion);
        var errors = new List<string>();
        card.ErrorOccurred += (_, message) => errors.Add(message);

        // Act
        await card.UpgradeCommand.ExecuteAsync();

        // Assert: applied from Mirror2, whose alias and the new files' identity the pin now records
        Assert.Empty(errors);
        var pin = RepoPinStore.Load(repo);
        Assert.Equal("Mirror2", pin?.Source);
        Assert.Equal("3.0.0", pin?.Version);
        Assert.True(PackageContentIdentity.IsFilesIdentity(pin?.ContentIdentity));
        Assert.Equal("v3", ReadMarker(repo));
        Assert.False(card.IsPinnedSourceSubstituted);
        Assert.Equal("Mirror2", card.PinnedSourceLabel);
    }

    /// <summary>
    ///     Test that when several configured sources offer the same newest version, the first of them in
    ///     Settings order is the one Upgrade uses and records.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_UpgradeCommand_PinnedAliasNotConfiguredAndTie_KeepsSettingsOrder()
    {
        // Arrange: both sources offer 2.0.0 (with different files), and the second also has the pinned files
        var (repo, _) = CreatePinnedRepo("Contoso", "v1");
        var mirror1 = CreateSourceFolder("v1");
        WriteZip(mirror1, "2.0.0", "v2 from mirror 1");
        var mirror2 = CreateSourceFolder("v1");
        WriteZip(mirror2, "2.0.0", "v2 from mirror 2");
        var settings = new AppSettings { Sources = [ZipSource("Mirror1", mirror1), ZipSource("Mirror2", mirror2)] };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());
        card.RefreshCheap();
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Act
        await card.UpgradeCommand.ExecuteAsync();

        // Assert
        Assert.Equal("Mirror1", RepoPinStore.Load(repo)?.Source);
        Assert.Equal("v2 from mirror 1", ReadMarker(repo));
    }

    /// <summary>
    ///     Test that Change package from another configured source records the alias of the source that was
    ///     actually used, with the identity of its files (which are the same files as before).
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_ApplySelectedPackage_FromOtherSourceWithSameFiles_RecordsThatSourceAndSameIdentity()
    {
        // Arrange: a repo pinned from "Contoso", and a teammate who selects the same version from "Artifactory"
        var (repo, _) = CreatePinnedRepo("Contoso", "v1");
        var identityBefore = RepoPinStore.Load(repo)?.ContentIdentity;
        var settings = new AppSettings { Sources = [ZipSource("Artifactory", CreateSourceFolder("v1", repack: true))] };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());
        card.RefreshCheap();
        var events = new List<string>();
        card.ErrorOccurred += (_, message) => events.Add("error: " + message);
        card.ContentChangeConfirmationRequested += (_, _) => events.Add("confirmation");

        // Act
        await card.ApplySelectedPackageAsync(
            new PackageReference("Artifactory", PackageId, "1.0.0"), TestContext.Current.CancellationToken);

        // Assert: no confirmation (nothing changed), the alias is the one used, the identity is unchanged
        Assert.Empty(events);
        var pin = RepoPinStore.Load(repo);
        Assert.Equal("Artifactory", pin?.Source);
        Assert.Equal(identityBefore, pin?.ContentIdentity);
        Assert.False(card.IsPinnedSourceSubstituted);
        Assert.Equal("Artifactory", card.PinnedSourceLabel);
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
            catch (IOException)
            {
                // Best-effort cleanup; leftover temp folders do not fail the test.
            }
        }
    }

    /// <summary>
    ///     Creates a zip source definition.
    /// </summary>
    /// <param name="alias">The source alias.</param>
    /// <param name="folder">The folder that holds the zips.</param>
    /// <returns>The definition.</returns>
    private static PackageSourceDefinition ZipSource(string alias, string folder) =>
        new() { Alias = alias, Type = PackageSourceType.Zip, Location = folder };

    /// <summary>
    ///     The four agent files every package of these tests delivers (the same files a NuGet test
    ///     package holds), whose agents file holds the given marker.
    /// </summary>
    /// <param name="marker">The text of the agents file.</param>
    /// <returns>The package-relative path and text of each file.</returns>
    private static (string Path, string Text)[] AgentFiles(string marker) =>
    [
        (".github/agents/copilot.md", marker),
        (".github/standards/style.md", "standards content"),
        (".github/templates/template.md", "templates content"),
        (".github/skills/skill.md", "skills content")
    ];

    /// <summary>
    ///     Writes a package zip of <see cref="PackageId"/> with the four agent files.
    /// </summary>
    /// <param name="folder">The source folder.</param>
    /// <param name="version">The package version.</param>
    /// <param name="marker">The text of the agents file.</param>
    /// <param name="repack">Whether the zip is packed differently (stored, other entry order, other
    ///     release notes) without changing the files.</param>
    private static void WriteZip(string folder, string version, string marker, bool repack = false)
    {
        var path = Path.Combine(folder, $"{PackageId}-{version}.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var entries = AgentFiles(marker).Append(("release-notes.md", repack ? "different notes" : "notes"));
        foreach (var (name, text) in repack ? entries.Reverse() : entries)
        {
            var entry = archive.CreateEntry(name, repack ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(text);
        }
    }

    /// <summary>
    ///     Creates a zip source folder with version 1.0.0 of the package.
    /// </summary>
    /// <param name="marker">The text of the agents file.</param>
    /// <param name="repack">Whether the zip is packed differently without changing the files.</param>
    /// <returns>The folder.</returns>
    private string CreateSourceFolder(string marker, bool repack = false)
    {
        var folder = CreateTempDirectory();
        WriteZip(folder, "1.0.0", marker, repack);
        return folder;
    }

    /// <summary>
    ///     Creates a repo that was really applied from version 1.0.0 of a zip source with the given alias,
    ///     so its pin is exactly what the application writes.
    /// </summary>
    /// <param name="alias">The alias of the source the repo is pinned to.</param>
    /// <param name="marker">The text of the agents file.</param>
    /// <returns>The repo folder and the text of its pin file.</returns>
    private (string Repo, string PinJson) CreatePinnedRepo(string alias, string marker) =>
        CreatePinnedRepo(alias, marker, out _);

    /// <summary>
    ///     Creates a repo that was really applied from version 1.0.0 of a zip source with the given alias.
    /// </summary>
    /// <param name="alias">The alias of the source the repo is pinned to.</param>
    /// <param name="marker">The text of the agents file.</param>
    /// <param name="sourceFolder">Receives the folder of the source the repo was pinned from.</param>
    /// <returns>The repo folder and the text of its pin file.</returns>
    private (string Repo, string PinJson) CreatePinnedRepo(string alias, string marker, out string sourceFolder)
    {
        var repo = CreateTempDirectory();
        sourceFolder = CreateSourceFolder(marker);
        var settings = new AppSettings { Sources = [ZipSource(alias, sourceFolder)] };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());
        card.ApplySelectedPackage(PackageId, "1.0.0", alias);
        Assert.Equal(alias, RepoPinStore.Load(repo)?.Source);
        return (repo, File.ReadAllText(PinPath(repo)));
    }

    /// <summary>
    ///     Reads the marker the package wrote into the repo's agents file.
    /// </summary>
    /// <param name="repo">The repo folder.</param>
    /// <returns>The text of the agents file.</returns>
    private static string ReadMarker(string repo) =>
        File.ReadAllText(Path.Combine(repo, ".github", "agents", "copilot.md"));

    /// <summary>
    ///     Gets the path of a repo's pin file.
    /// </summary>
    /// <param name="repo">The repo folder.</param>
    /// <returns>The path.</returns>
    private static string PinPath(string repo) => Path.Combine(repo, ".agentcontrol.json");

    /// <summary>
    ///     Creates a unique temporary directory removed by <see cref="Dispose"/>.
    /// </summary>
    /// <returns>The folder.</returns>
    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_portable_pin_test_" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        return path;
    }
}
