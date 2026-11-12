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
using DemaConsulting.AgentControl.RepoSync;
using DemaConsulting.AgentControl.Settings;
using DemaConsulting.AgentControl.Tests.AgentPackageManagement;
using Material.Icons;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Tests of the "portable pin" behavior of <see cref="RepoCardViewModel"/>: a repo's pin names no
///     package source (sources and their aliases are each user's own configuration), so the card finds
///     the source locally - the first configured source, in Settings order, that delivers files with
///     exactly the identity the pin recorded - whatever its alias or kind; never a source whose files
///     differ; and, for an older pin that recorded no files identity, by the hint order of last supplier,
///     the source an older version named, then the first source that offers it.
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
    ///     Test that a source named differently from the one the pin was made with, whose files are the
    ///     identical files (repacked, with other release notes), supplies the package without a source
    ///     problem: the chip names the source that supplies it, the tooltip names it without any "pinned
    ///     source" wording, and the pin is left as it was.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_Refresh_SourceNamedDifferentlyHasIdenticalFiles_NamesSupplierWithoutProblem()
    {
        // Arrange: a repo made from "Contoso", and a teammate who only has "Artifactory" with the same files repacked
        var (repo, pinBefore) = CreatePinnedRepo("v1");
        var artifactory = CreateSourceFolder("v1", repack: true);
        var settings = new AppSettings { Sources = [ZipSource("Artifactory", artifactory)] };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());

        // Act
        card.RefreshCheap();
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert: no problem, the supplier is named, and the pin is exactly as it was
        Assert.False(card.HasSourceProblem);
        Assert.Equal(RepoCardStatus.UpToDate, card.Status);
        Assert.Equal("Artifactory", card.SupplyingSource);
        Assert.True(card.HasSupplyingSource);
        Assert.Equal("Artifactory", card.PinnedSourceLabel);
        Assert.Equal(MaterialIconKind.FolderZip, card.PinnedSourceIcon);
        Assert.Contains("from source 'Artifactory'", card.PackageChipTooltip, StringComparison.Ordinal);
        Assert.DoesNotContain("pinned source", card.PackageChipTooltip, StringComparison.OrdinalIgnoreCase);
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
        var (repo, _) = CreatePinnedRepo("v1");
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
        Assert.Equal("Mirror2", card.SupplyingSource);
    }

    /// <summary>
    ///     Test that a pin whose version a configured source offers only with other files is a source
    ///     problem that says no configured source delivers the files the repo was set up with: nothing is
    ///     substituted, the chip names no source, and no upgrade is offered when nothing is newer.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_Refresh_OnlySourceHasDifferentFiles_ShowsNoMatchingFilesProblem()
    {
        // Arrange: the teammate's only source offers the same package name and version with other files
        var (repo, _) = CreatePinnedRepo("v1");
        var settings = new AppSettings { Sources = [ZipSource("Artifactory", CreateSourceFolder("other files"))] };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());

        // Act
        card.RefreshCheap();
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert: the red chip with an explanation, no supplier, no upgrade
        Assert.True(card.HasSourceProblem);
        Assert.Equal("Package source problem", card.SourceProblemChipText);
        Assert.Contains(
            "No configured source delivers contoso-agents 1.0.0 with the files this repo was set up with",
            card.SourceProblem,
            StringComparison.Ordinal);
        Assert.DoesNotContain("pinned source", card.SourceProblem, StringComparison.OrdinalIgnoreCase);
        Assert.Null(card.SupplyingSource);
        Assert.False(card.HasSupplyingSource);
        Assert.Equal(string.Empty, card.PinnedSourceLabel);
        Assert.Equal(RepoCardStatus.Problem, card.Status);
        Assert.False(card.IsUpgradeAvailable);
    }

    /// <summary>
    ///     Test that a pin whose package version no configured source offers at all is a source problem
    ///     that says so, and that a version older than the pin does not make it an upgrade.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_Refresh_NoSourceOffersPinnedVersion_ShowsNoSourceOffersProblem()
    {
        // Arrange: the only source has the package, but only an older version than the pinned one
        var (repo, _) = CreatePinnedRepo("v1");
        var older = CreateTempDirectory();
        WriteZip(older, "0.5.0", "old");
        var settings = new AppSettings { Sources = [ZipSource("Artifactory", older)] };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());

        // Act
        card.RefreshCheap();
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(card.HasSourceProblem);
        Assert.Contains("No configured source offers contoso-agents 1.0.0", card.SourceProblem, StringComparison.Ordinal);
        Assert.Equal(RepoCardStatus.Problem, card.Status);
        Assert.False(card.IsUpgradeAvailable);
        Assert.False(card.UpgradeCommand.CanExecute(null));
    }

    /// <summary>
    ///     Test that a pin with no source configured at all shows the same "no configured source offers it"
    ///     problem (the user has nothing to supply the files from).
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_Refresh_NoSourcesConfigured_ShowsNoSourceOffersProblem()
    {
        // Arrange: a pinned repo and settings without any source
        var (repo, _) = CreatePinnedRepo("v1");
        var settings = new AppSettings { Sources = [] };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());

        // Act
        card.RefreshCheap();
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(card.HasSourceProblem);
        Assert.Contains("No configured source offers contoso-agents 1.0.0", card.SourceProblem, StringComparison.Ordinal);
        Assert.Equal(RepoCardStatus.Problem, card.Status);
        Assert.False(card.HasSupplyingSource);
    }

    /// <summary>
    ///     Test that a source problem and a newer version coexist: when no configured source offers the
    ///     pinned version but one offers a newer one, the problem says upgrading is the way out, the status
    ///     is a problem, and the upgrade is offered through the chip data and the shared command.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_Refresh_NoSourceOffersPinnedVersionButNewerOffered_ShowsProblemAndStillOffersUpgrade()
    {
        // Arrange: the teammate's source only has 2.0.0 now
        var (repo, _) = CreatePinnedRepo("v1");
        var folder = CreateTempDirectory();
        WriteZip(folder, "2.0.0", "v2");
        var settings = new AppSettings { Sources = [ZipSource("Artifactory", folder)] };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());

        // Act
        card.RefreshCheap();
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert: the red chip and the upgrade coexist
        Assert.True(card.HasSourceProblem);
        Assert.Contains("No configured source offers contoso-agents 1.0.0", card.SourceProblem, StringComparison.Ordinal);
        Assert.Contains(
            "Upgrade to 2.0.0 to move to a version a configured source offers.", card.SourceProblem, StringComparison.Ordinal);
        Assert.Equal(RepoCardStatus.Problem, card.Status);
        Assert.True(card.IsUpgradeAvailable);
        Assert.Equal("2.0.0", card.LatestAvailableVersion);
        Assert.Equal("2.0.0", card.UpgradeChipText);
        Assert.True(card.UpgradeCommand.CanExecute(null));
        Assert.True(card.HasAgentFileActions);
    }

    /// <summary>
    ///     Test that a source problem about changed files also still offers a newer version the source has.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_Refresh_OnlyDifferentFilesButNewerOffered_ShowsProblemAndStillOffersUpgrade()
    {
        // Arrange: the teammate's source has 1.0.0 with other files, and 2.0.0
        var (repo, _) = CreatePinnedRepo("v1");
        var folder = CreateSourceFolder("other files");
        WriteZip(folder, "2.0.0", "v2");
        var settings = new AppSettings { Sources = [ZipSource("Artifactory", folder)] };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());

        // Act
        card.RefreshCheap();
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(card.HasSourceProblem);
        Assert.Contains("with the files this repo was set up with", card.SourceProblem, StringComparison.Ordinal);
        Assert.Equal(RepoCardStatus.Problem, card.Status);
        Assert.True(card.IsUpgradeAvailable);
        Assert.Equal("2.0.0", card.LatestAvailableVersion);
        Assert.True(card.UpgradeCommand.CanExecute(null));
    }

    /// <summary>
    ///     Test that when nothing offers the pinned version and a configured source could not be read,
    ///     nothing is known: the check is "unknown", never the "no configured source offers it" problem.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_Refresh_NothingOffersAndASourceCannotBeRead_IsUnknownNotAProblem()
    {
        // Arrange: one source whose folder is gone, and one that does not have the package
        var (repo, _) = CreatePinnedRepo("v1");
        var empty = CreateTempDirectory();
        var settings = new AppSettings
        {
            Sources = [ZipSource("Gone", Path.Combine(empty, "missing")), ZipSource("Empty", empty)]
        };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());

        // Act
        card.RefreshCheap();
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert: the failure handling, not a claim that no source offers the version
        Assert.False(card.HasSourceProblem);
        Assert.True(card.IsUpgradeStatusUnknown);
        Assert.False(card.IsUpgradeAvailable);
    }

    /// <summary>
    ///     Test that the source the card was last supplied from is accepted by the status check as long as
    ///     it still offers the version, without acquiring its files again: a source that lists the version
    ///     but cannot deliver it is still named, while without that cache the same source is not trusted.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_Refresh_CachedSupplierStillOffersVersion_IsAcceptedWithoutAcquiring()
    {
        // Arrange: the only source lists 1.0.0 but its zip is not readable
        var (repo, _) = CreatePinnedRepo("v1");
        var broken = CreateTempDirectory();
        await File.WriteAllBytesAsync(Path.Combine(broken, $"{PackageId}-1.0.0.zip"), [], TestContext.Current.CancellationToken);
        var settings = new AppSettings { Sources = [ZipSource("Broken", broken)] };
        var cached = new RepoCardViewModel(
            new RecentRepo { Path = repo, PinnedPackageName = PackageId, PinnedPackageVersion = "1.0.0", PinnedSource = "Broken" },
            () => settings,
            new PackageVersionCache());
        var uncached = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());

        // Act
        cached.RefreshCheap();
        await cached.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);
        uncached.RefreshCheap();
        await uncached.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert: accepted from the cache, but not verifiable (so a problem) without it
        Assert.False(cached.HasSourceProblem);
        Assert.Equal("Broken", cached.PinnedSourceLabel);
        Assert.True(uncached.HasSourceProblem);
        Assert.Equal(string.Empty, uncached.PinnedSourceLabel);
    }

    /// <summary>
    ///     Test that a remembered supplier that is no longer configured is dropped and the package is
    ///     supplied by a configured source that delivers the identical files.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_Refresh_CachedSupplierNoLongerConfigured_IsReplacedByAnotherSource()
    {
        // Arrange: the card remembers "Contoso", which the user has since removed from Settings
        var (repo, _) = CreatePinnedRepo("v1");
        var settings = new AppSettings { Sources = [ZipSource("Artifactory", CreateSourceFolder("v1"))] };
        var card = new RepoCardViewModel(
            new RecentRepo { Path = repo, PinnedPackageName = PackageId, PinnedPackageVersion = "1.0.0", PinnedSource = "Contoso" },
            () => settings,
            new PackageVersionCache());

        // Act
        card.RefreshCheap();
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(card.HasSourceProblem);
        Assert.Equal("Artifactory", card.SupplyingSource);
    }

    /// <summary>
    ///     Test that a pin that recorded no files identity (an older pin) is never refused: the source an
    ///     older version recorded in the file is used as a hint when it is configured and offers the version.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_Refresh_PinWithoutFilesIdentity_UsesLegacySourceHintWhenConfigured()
    {
        // Arrange: a schema 2 pin that names "Mirror2", and two configured sources offering the version
        var repo = CreateRepoWithLegacyPin("Mirror2");
        var settings = new AppSettings
        {
            Sources = [ZipSource("Mirror1", CreateSourceFolder("v1")), ZipSource("Mirror2", CreateSourceFolder("v1"))]
        };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());

        // Act
        card.RefreshCheap();
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(card.HasSourceProblem);
        Assert.Equal("Mirror2", card.SupplyingSource);
    }

    /// <summary>
    ///     Test that a pin that recorded no files identity whose legacy source hint is not configured is
    ///     supplied by the first configured source that offers the version, and is never refused.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_Refresh_PinWithoutFilesIdentityAndLegacyHintNotConfigured_UsesFirstOfferingSource()
    {
        // Arrange: a schema 2 pin naming "Contoso", which this machine does not configure
        var repo = CreateRepoWithLegacyPin("Contoso");
        var settings = new AppSettings
        {
            Sources = [ZipSource("Mirror1", CreateSourceFolder("v1")), ZipSource("Mirror2", CreateSourceFolder("other files"))]
        };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());

        // Act
        card.RefreshCheap();
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert: nothing to verify against, so nothing is refused
        Assert.False(card.HasSourceProblem);
        Assert.Equal("Mirror1", card.SupplyingSource);
        Assert.Equal(RepoCardStatus.UpToDate, card.Status);
    }

    /// <summary>
    ///     Test that, for a pin that recorded no files identity, the source the card was last supplied from
    ///     wins over the legacy source hint.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_Refresh_PinWithoutFilesIdentity_LastSupplierWinsOverLegacyHint()
    {
        // Arrange: the file's hint says "Mirror2", the card last used "Mirror1"
        var repo = CreateRepoWithLegacyPin("Mirror2");
        var settings = new AppSettings
        {
            Sources = [ZipSource("Mirror2", CreateSourceFolder("v1")), ZipSource("Mirror1", CreateSourceFolder("v1"))]
        };
        var card = new RepoCardViewModel(
            new RecentRepo { Path = repo, PinnedPackageName = PackageId, PinnedPackageVersion = "1.0.0", PinnedSource = "Mirror1" },
            () => settings,
            new PackageVersionCache());

        // Act
        card.RefreshCheap();
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Mirror1", card.SupplyingSource);
    }

    /// <summary>
    ///     Test that the launch re-sync of a fresh clone restores the pinned files from a configured source
    ///     with another alias that delivers the identical files, never writes the pin, and remembers the
    ///     source that supplied them on the card.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_SourceNamedDifferently_RestoresFromIdenticalSourceWithoutWritingPin()
    {
        // Arrange: a repo whose managed folders are missing, and a teammate with "Artifactory"
        var (repo, pinBefore) = CreatePinnedRepo("v1");
        Directory.Delete(Path.Combine(repo, ".github"), recursive: true);
        var settings = new AppSettings { Sources = [ZipSource("Artifactory", CreateSourceFolder("v1", repack: true))] };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());
        card.RefreshCheap();
        var errors = new List<string>();
        card.ErrorOccurred += (_, message) => errors.Add(message);

        // Act
        var result = await card.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);

        // Assert: restored, no warning, the pin untouched and the supplier remembered
        Assert.True(result);
        Assert.Empty(errors);
        Assert.Equal("v1", ReadMarker(repo));
        Assert.Equal(pinBefore, await File.ReadAllTextAsync(PinPath(repo), TestContext.Current.CancellationToken));
        Assert.Equal("Artifactory", card.SupplyingSource);
    }

    /// <summary>
    ///     Test that the launch re-sync skips a source that offers the same name and version with other
    ///     files, however early it is configured, and restores the files from the first source that delivers
    ///     the identical files.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_RepublishedSourceFirst_SkipsItAndRestoresFromMatchingSource()
    {
        // Arrange: the first source republished 1.0.0 with other files; the second still has the original files
        var (repo, _) = CreatePinnedRepo("v1");
        Directory.Delete(Path.Combine(repo, ".github"), recursive: true);
        var settings = new AppSettings
        {
            Sources = [ZipSource("Contoso", CreateSourceFolder("tampered")), ZipSource("Artifactory", CreateSourceFolder("v1"))]
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
        Assert.Equal("Artifactory", card.SupplyingSource);
    }

    /// <summary>
    ///     Test that the launch re-sync tries the source the card was last supplied from first when several
    ///     sources deliver the identical files, and otherwise the first in Settings order.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_SeveralIdenticalSources_PrefersLastSupplierThenSettingsOrder()
    {
        // Arrange: two repos pinned to the same files, two sources delivering them
        var (repoNoHistory, _) = CreatePinnedRepo("v1");
        var (repoWithHistory, _) = CreatePinnedRepo("v1");
        Directory.Delete(Path.Combine(repoNoHistory, ".github"), recursive: true);
        Directory.Delete(Path.Combine(repoWithHistory, ".github"), recursive: true);
        var settings = new AppSettings
        {
            Sources = [ZipSource("Mirror1", CreateSourceFolder("v1")), ZipSource("Mirror2", CreateSourceFolder("v1"))]
        };
        var noHistory = new RepoCardViewModel(new RecentRepo { Path = repoNoHistory }, () => settings, new PackageVersionCache());
        var withHistory = new RepoCardViewModel(
            new RecentRepo
            {
                Path = repoWithHistory,
                PinnedPackageName = PackageId,
                PinnedPackageVersion = "1.0.0",
                PinnedSource = "Mirror2"
            },
            () => settings,
            new PackageVersionCache());
        noHistory.RefreshCheap();
        withHistory.RefreshCheap();

        // Act
        await noHistory.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);
        await withHistory.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Mirror1", noHistory.SupplyingSource);
        Assert.Equal("Mirror2", withHistory.SupplyingSource);
    }

    /// <summary>
    ///     Test that the launch re-sync does not use a source whose package of that name and version has
    ///     other files: nothing is extracted, the pin is untouched, and the warning says the version now
    ///     contains different files and what to do about it.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_OnlyOtherFilesAvailable_RefusesWithExplanation()
    {
        // Arrange: the teammate's source has the version, but with other files
        var (repo, pinBefore) = CreatePinnedRepo("v1");
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
        Assert.Contains("The agent files were not synced", message, StringComparison.Ordinal);
        Assert.Contains("now contains different files", message, StringComparison.Ordinal);
        Assert.Contains("Launching without them", message, StringComparison.Ordinal);
        Assert.Contains("Change package", message, StringComparison.Ordinal);
        Assert.DoesNotContain("pinned source", message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(repo, ".github")));
        Assert.Equal(pinBefore, await File.ReadAllTextAsync(PinPath(repo), TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that the launch re-sync of a pin whose version no configured source offers says so, and
    ///     extracts nothing.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_NoSourceOffersVersion_ReportsNoSourceOffers()
    {
        // Arrange: the only source has another version of the package
        var (repo, pinBefore) = CreatePinnedRepo("v1");
        Directory.Delete(Path.Combine(repo, ".github"), recursive: true);
        var folder = CreateTempDirectory();
        WriteZip(folder, "2.0.0", "v2");
        var settings = new AppSettings { Sources = [ZipSource("Artifactory", folder)] };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());
        card.RefreshCheap();
        var errors = new List<string>();
        card.ErrorOccurred += (_, message) => errors.Add(message);

        // Act
        var result = await card.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
        var message = Assert.Single(errors);
        Assert.Contains("No configured source offers contoso-agents 1.0.0", message, StringComparison.Ordinal);
        Assert.Contains("Launching without syncing agent files", message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(repo, ".github")));
        Assert.Equal(pinBefore, await File.ReadAllTextAsync(PinPath(repo), TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that the launch re-sync, when one configured source cannot be read and the readable ones
    ///     do not offer the version, reports that a source could not be read rather than claiming that no
    ///     source offers the version (the unreadable one might).
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_SourceUnreadable_ReportsReadFailure()
    {
        // Arrange: a missing source folder, and a readable source with only another version
        var (repo, _) = CreatePinnedRepo("v1");
        Directory.Delete(Path.Combine(repo, ".github"), recursive: true);
        var missing = Path.Combine(CreateTempDirectory(), "missing");
        var other = CreateTempDirectory();
        WriteZip(other, "2.0.0", "v2");
        var settings = new AppSettings { Sources = [ZipSource("Artifactory", missing), ZipSource("Mirror", other)] };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());
        card.RefreshCheap();
        var errors = new List<string>();
        card.ErrorOccurred += (_, message) => errors.Add(message);

        // Act
        var result = await card.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);

        // Assert: the read failure is reported, not "no source offers"
        Assert.False(result);
        var message = Assert.Single(errors);
        Assert.Contains("A package source could not be read", message, StringComparison.Ordinal);
        Assert.DoesNotContain("No configured source offers", message, StringComparison.Ordinal);
        Assert.Contains("Launching without syncing agent files", message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the launch re-sync of a pin that recorded no files identity is never refused: it takes
    ///     the source an older version named when that is configured, and otherwise the first source that
    ///     offers the version.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_PinWithoutFilesIdentity_UsesLegacyHintThenFirstOfferingSource()
    {
        // Arrange: two repos with an older pin each (one names "Mirror2", one names an unconfigured source)
        var hinted = CreateRepoWithLegacyPin("Mirror2");
        var plain = CreateRepoWithLegacyPin("Contoso");
        var settings = new AppSettings
        {
            Sources = [ZipSource("Mirror1", CreateSourceFolder("first source")), ZipSource("Mirror2", CreateSourceFolder("v1"))]
        };
        var hintedCard = new RepoCardViewModel(new RecentRepo { Path = hinted }, () => settings, new PackageVersionCache());
        var plainCard = new RepoCardViewModel(new RecentRepo { Path = plain }, () => settings, new PackageVersionCache());
        hintedCard.RefreshCheap();
        plainCard.RefreshCheap();
        var errors = new List<string>();
        hintedCard.ErrorOccurred += (_, message) => errors.Add(message);
        plainCard.ErrorOccurred += (_, message) => errors.Add(message);

        // Act
        var hintedResult = await hintedCard.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);
        var plainResult = await plainCard.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);

        // Assert: never refused; the hint picks Mirror2, otherwise the first source that offers it
        Assert.True(hintedResult);
        Assert.True(plainResult);
        Assert.Empty(errors);
        Assert.Equal("v1", ReadMarker(hinted));
        Assert.Equal("first source", ReadMarker(plain));
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
    ///     two kinds delivering identical files in different containers, and that the pin file names neither.
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
        Assert.DoesNotContain("Feed", await File.ReadAllTextAsync(PinPath(repo), TestContext.Current.CancellationToken), StringComparison.Ordinal);
        var settings = new AppSettings { Sources = [ZipSource("Zips", CreateSourceFolder("v1"))] };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());

        // Act
        card.RefreshCheap();
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(card.HasSourceProblem);
        Assert.Equal("Zips", card.PinnedSourceLabel);
        Assert.Equal("Zips", card.SupplyingSource);
    }

    /// <summary>
    ///     Test that a pin made from a zip source is restored at launch from a real Git source of the same
    ///     files (the Git layout in a real local repository), and that the pin is not written.
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
        var (repo, pinBefore) = CreatePinnedRepo("v1");
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
    ///     Test that the upgrade check considers every configured source that offers the package, the newest
    ///     version wins, and Upgrade applies it from the source that offers it, recording only the package,
    ///     version and files identity in the pin (no source) and remembering the source on the card.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_UpgradeCommand_SeveralSources_UsesNewestVersionAcrossSourcesAndRecordsNoSource()
    {
        // Arrange: Mirror1 has the pinned files and 2.0.0, Mirror2 has the pinned files and 3.0.0
        var (repo, _) = CreatePinnedRepo("v1");
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

        // Assert: applied from Mirror2, whose files' identity the pin now records, and no source is written
        Assert.Empty(errors);
        var pin = RepoPinStore.Load(repo);
        Assert.Equal("3.0.0", pin?.Version);
        Assert.True(PackageContentIdentity.IsFilesIdentity(pin?.ContentIdentity));
        Assert.DoesNotContain("source", await File.ReadAllTextAsync(PinPath(repo), TestContext.Current.CancellationToken), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("v3", ReadMarker(repo));
        Assert.Equal("Mirror2", card.SupplyingSource);
        Assert.Equal("Mirror2", card.PinnedSourceLabel);
    }

    /// <summary>
    ///     Test that Upgrade, being user-initiated, lists the configured sources afresh: a version published
    ///     after the last (cached) check is the one it applies, although the chip still showed the older one.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_UpgradeCommand_VersionPublishedAfterCachedCheck_AppliesTheNewVersion()
    {
        // Arrange: a source with the pinned files and 2.0.0, checked once (the listing is now cached)
        var (repo, _) = CreatePinnedRepo("v1");
        var folder = CreateSourceFolder("v1");
        WriteZip(folder, "2.0.0", "v2");
        var settings = new AppSettings { Sources = [ZipSource("Mirror", folder)] };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());
        card.RefreshCheap();
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);
        Assert.Equal("2.0.0", card.LatestAvailableVersion);
        WriteZip(folder, "3.0.0", "v3");
        var errors = new List<string>();
        card.ErrorOccurred += (_, message) => errors.Add(message);

        // Act
        await card.UpgradeCommand.ExecuteAsync();

        // Assert: the newly published version was found and applied
        Assert.Empty(errors);
        Assert.Equal("3.0.0", RepoPinStore.Load(repo)?.Version);
        Assert.Equal("v3", ReadMarker(repo));
    }

    /// <summary>
    ///     Test that when several configured sources offer the same newest version, the first of them in
    ///     Settings order is the one Upgrade uses.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_UpgradeCommand_SameNewestVersionInSeveralSources_KeepsSettingsOrder()
    {
        // Arrange: both sources offer 2.0.0 (with different files)
        var (repo, _) = CreatePinnedRepo("v1");
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
        Assert.Equal("v2 from mirror 1", ReadMarker(repo));
        Assert.Equal("Mirror1", card.SupplyingSource);
    }

    /// <summary>
    ///     Test that Upgrade rescues a pin no configured source offers: the newer version a source does
    ///     offer is applied and the problem is gone.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_UpgradeCommand_FromSourceProblem_AppliesNewerVersionAndClearsProblem()
    {
        // Arrange: the teammate's source only has 2.0.0
        var (repo, _) = CreatePinnedRepo("v1");
        var folder = CreateTempDirectory();
        WriteZip(folder, "2.0.0", "v2");
        var settings = new AppSettings { Sources = [ZipSource("Artifactory", folder)] };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());
        card.RefreshCheap();
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);
        Assert.True(card.HasSourceProblem);

        // Act
        await card.UpgradeCommand.ExecuteAsync();

        // Assert
        Assert.False(card.HasSourceProblem);
        Assert.False(card.IsUpgradeAvailable);
        Assert.Equal("2.0.0", RepoPinStore.Load(repo)?.Version);
        Assert.Equal("Artifactory", card.SupplyingSource);
        Assert.Equal(RepoCardStatus.UpToDate, card.Status);
    }

    /// <summary>
    ///     Test that Change package from a configured source with another alias but the same files asks for
    ///     no confirmation (nothing changed), keeps the files identity, writes no source into the pin and
    ///     remembers the source used on the card.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_ApplySelectedPackage_FromOtherSourceWithSameFiles_KeepsIdentityAndRecordsNoSource()
    {
        // Arrange: a repo made from "Contoso", and a teammate who selects the same version from "Artifactory"
        var (repo, _) = CreatePinnedRepo("v1");
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

        // Assert: no confirmation (nothing changed), the identity is unchanged, no source is in the file
        Assert.Empty(events);
        Assert.Equal(identityBefore, RepoPinStore.Load(repo)?.ContentIdentity);
        Assert.DoesNotContain("source", await File.ReadAllTextAsync(PinPath(repo), TestContext.Current.CancellationToken), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Artifactory", card.SupplyingSource);
        Assert.Equal("Artifactory", card.PinnedSourceLabel);
    }

    /// <summary>
    ///     Test that Change package of the same version from a source that republished it with other files
    ///     asks for confirmation instead of replacing the files - whichever source the repo was set up from -
    ///     and changes nothing while the question is open.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_ApplySelectedPackage_SameVersionFromRepublishedSource_AsksForConfirmation()
    {
        // Arrange: a repo made from "Contoso", and a teammate whose "Artifactory" delivers other files for 1.0.0
        var (repo, pinBefore) = CreatePinnedRepo("v1");
        var settings = new AppSettings { Sources = [ZipSource("Artifactory", CreateSourceFolder("republished"))] };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());
        card.RefreshCheap();
        var requests = new List<ContentChangeConfirmationRequest>();
        card.ContentChangeConfirmationRequested += (_, request) => requests.Add(request);

        // Act
        await card.ApplySelectedPackageAsync(
            new PackageReference("Artifactory", PackageId, "1.0.0"), TestContext.Current.CancellationToken);

        // Assert: the user is asked, and the repo and its pin are untouched
        var asked = Assert.Single(requests);
        Assert.Contains("now contains different files", asked.Message, StringComparison.Ordinal);
        Assert.Equal("v1", ReadMarker(repo));
        Assert.Equal(pinBefore, await File.ReadAllTextAsync(PinPath(repo), TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a pin whose only offering source refuses access (HTTP 403 while its files are read for
    ///     verification) shows the access-denied problem with the server's reason - not "no configured source
    ///     delivers the files" - and offers no sign-in.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_Refresh_OnlySourceRefusesAccessWhileVerifying_ShowsAccessDeniedReason()
    {
        // Arrange: the teammate's only source lists the pinned version but answers 403 when it is read
        var (repo, _) = CreatePinnedRepo("v1");
        var settings = new AppSettings { Sources = [ZipSource("Denied", CreateTempDirectory())] };
        var card = new RepoCardViewModel(
            new RecentRepo { Path = repo }, () => settings, new PackageVersionCache(), registryFactory: RegistryWithDeniedSource);

        // Act
        card.RefreshCheap();
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert: the server's reason is on the card, and it is not the "no matching files" explanation
        Assert.True(card.HasSourceProblem);
        Assert.Contains(DeniedMessage, card.SourceProblem, StringComparison.Ordinal);
        Assert.DoesNotContain("No configured source delivers", card.SourceProblem, StringComparison.Ordinal);
        Assert.False(card.SourceNeedsSignIn);
        Assert.True(card.IsUpgradeStatusUnknown);
        Assert.Null(card.SupplyingSource);
    }

    /// <summary>
    ///     Test that a source that refuses access does not stop another source from verifying: the working
    ///     source supplies the package and nothing is reported.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_Refresh_OneSourceRefusesAccessAnotherVerifies_UsesTheWorkingSourceWithoutProblem()
    {
        // Arrange: the first source answers 403, the second delivers the pinned files
        var (repo, _) = CreatePinnedRepo("v1");
        var settings = new AppSettings
        {
            Sources = [ZipSource("Denied", CreateTempDirectory()), ZipSource("Artifactory", CreateSourceFolder("v1"))]
        };
        var card = new RepoCardViewModel(
            new RecentRepo { Path = repo }, () => settings, new PackageVersionCache(), registryFactory: RegistryWithDeniedSource);

        // Act
        card.RefreshCheap();
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(card.HasSourceProblem);
        Assert.Equal("Artifactory", card.SupplyingSource);
        Assert.False(card.IsUpgradeStatusUnknown);
        Assert.Equal(RepoCardStatus.UpToDate, card.Status);
    }

    /// <summary>
    ///     Test that a source that refuses access and another that delivers other files report the refusal
    ///     (the refusing source may be the one that has the files), not "no matching files".
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_Refresh_OneSourceRefusesAccessAnotherHasDifferentFiles_ShowsAccessDeniedReason()
    {
        // Arrange
        var (repo, _) = CreatePinnedRepo("v1");
        var settings = new AppSettings
        {
            Sources = [ZipSource("Artifactory", CreateSourceFolder("other files")), ZipSource("Denied", CreateTempDirectory())]
        };
        var card = new RepoCardViewModel(
            new RecentRepo { Path = repo }, () => settings, new PackageVersionCache(), registryFactory: RegistryWithDeniedSource);

        // Act
        card.RefreshCheap();
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(card.HasSourceProblem);
        Assert.Contains(DeniedMessage, card.SourceProblem, StringComparison.Ordinal);
        Assert.DoesNotContain("No configured source delivers", card.SourceProblem, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the launch re-sync, when the only source refuses access while its files are read, warns
    ///     with the server's reason (not "no matching files") and still lets the launch go on.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_SourceRefusesAccess_WarnsWithTheServersReason()
    {
        // Arrange: a repo whose managed folders are missing, and a source that answers 403
        var (repo, _) = CreatePinnedRepo("v1");
        Directory.Delete(Path.Combine(repo, ".github"), recursive: true);
        var settings = new AppSettings { Sources = [ZipSource("Denied", CreateTempDirectory())] };
        var card = new RepoCardViewModel(
            new RecentRepo { Path = repo }, () => settings, new PackageVersionCache(), registryFactory: RegistryWithDeniedSource);
        card.RefreshCheap();
        var errors = new List<string>();
        card.ErrorOccurred += (_, message) => errors.Add(message);

        // Act
        var result = await card.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);

        // Assert: nothing restored, and the one warning carries the reason
        Assert.False(result);
        var warning = Assert.Single(errors);
        Assert.Contains(DeniedMessage, warning, StringComparison.Ordinal);
        Assert.DoesNotContain("No configured source delivers", warning, StringComparison.Ordinal);
        Assert.Contains("Launching anyway", warning, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The message a refusing source reports.
    /// </summary>
    private const string DeniedMessage = "Signed in, but 'feed.example' refused access: You need ReadPackages.";

    /// <summary>
    ///     Builds the registry of a test: the source aliased "Denied" lists the package but answers 403 when
    ///     it is read, every other source is a real zip folder.
    /// </summary>
    /// <param name="settings">The settings, whose sources are configured.</param>
    /// <returns>The registry.</returns>
    private static PackageSourceRegistry RegistryWithDeniedSource(AppSettings settings) =>
        new(
            settings.Sources,
            definition => definition.Alias == "Denied"
                ? new AccessDeniedOnAcquireProvider()
                : new ZipFolderSourceProvider(definition));

    /// <summary>
    ///     A source that lists version 1.0.0 of the package and refuses access (HTTP 403) when its files are
    ///     read.
    /// </summary>
    private sealed class AccessDeniedOnAcquireProvider : IPackageSourceProvider
    {
        /// <summary>
        ///     The in-memory source that answers the listings.
        /// </summary>
        private readonly FakePackageSourceProvider _listing = new("Denied", (PackageId, "1.0.0"));

        /// <inheritdoc />
        public string Alias => "Denied";

        /// <inheritdoc />
        public Task<IReadOnlyList<string>> ListPackageIdsAsync(CancellationToken cancellationToken) =>
            _listing.ListPackageIdsAsync(cancellationToken);

        /// <inheritdoc />
        public Task<IReadOnlyList<DiscoveredPackage>> ListVersionsAsync(string packageId, CancellationToken cancellationToken) =>
            _listing.ListVersionsAsync(packageId, cancellationToken);

        /// <inheritdoc />
        public Task<IPackageContentReader> AcquireAsync(DiscoveredPackage package, CancellationToken cancellationToken) =>
            throw new PackageSourceAccessException(PackageSourceFailureKind.AccessDenied, DeniedMessage);
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
    ///     Creates a repo that was really applied from version 1.0.0 of a zip source (aliased "Contoso"
    ///     on the machine that made it), so its pin is exactly what the application writes.
    /// </summary>
    /// <param name="marker">The text of the agents file.</param>
    /// <returns>The repo folder and the text of its pin file.</returns>
    private (string Repo, string PinJson) CreatePinnedRepo(string marker)
    {
        var repo = CreateTempDirectory();
        var settings = new AppSettings { Sources = [ZipSource("Contoso", CreateSourceFolder(marker))] };
        var card = new RepoCardViewModel(new RecentRepo { Path = repo }, () => settings, new PackageVersionCache());
        card.ApplySelectedPackage(PackageId, "1.0.0", "Contoso");
        var pinJson = File.ReadAllText(PinPath(repo));
        Assert.DoesNotContain("source", pinJson, StringComparison.OrdinalIgnoreCase);
        Assert.True(PackageContentIdentity.IsFilesIdentity(RepoPinStore.Load(repo)?.ContentIdentity));
        return (repo, pinJson);
    }

    /// <summary>
    ///     Creates a repo with an older pin (schema 2) that names a source and recorded no files identity.
    /// </summary>
    /// <param name="legacySource">The source alias the older pin names.</param>
    /// <returns>The repo folder.</returns>
    private string CreateRepoWithLegacyPin(string legacySource)
    {
        var repo = CreateTempDirectory();
        File.WriteAllText(
            PinPath(repo),
            $$"""{ "SchemaVersion": 2, "Source": "{{legacySource}}", "PackageName": "{{PackageId}}", "Version": "1.0.0" }""");
        return repo;
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
