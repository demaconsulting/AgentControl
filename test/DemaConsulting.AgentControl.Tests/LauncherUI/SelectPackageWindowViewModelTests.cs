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

using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.RepoSync;
using DemaConsulting.AgentControl.Tests.AgentPackageManagement;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Unit tests for <see cref="SelectPackageWindowViewModel"/>.
/// </summary>
/// <remarks>
///     No Avalonia <see cref="Avalonia.Controls.Window"/> is ever constructed here, matching
///     <c>SettingsWindowViewModelTests</c>'s headless pattern. Most tests use in-memory
///     <see cref="FakePackageSourceProvider"/>s behind a real <see cref="PackageSourceRegistry"/>
///     and <see cref="PackageVersionCache"/>, so which source was contacted can be asserted;
///     one test uses a real, missing zip folder to show an unreachable source's error.
/// </remarks>
public sealed class SelectPackageWindowViewModelTests
{

    /// <summary>
    ///     Test that the view model lists every configured source in configured order and
    ///     contacts none of them until loading is requested.
    /// </summary>
    [Fact]
    public void SelectPackageWindowViewModel_Constructor_ListsEverySourceWithoutReadingAny()
    {
        // Arrange: two sources
        var a = new FakePackageSourceProvider("A", ("contoso-agents", "1.0.0"));
        var b = new FakePackageSourceProvider("B", ("second-agents", "2.0.0"));

        // Act
        var viewModel = CreateViewModel(a, b);

        // Assert: both sources are listed, the first is selected, and no provider was contacted
        Assert.Equal(["A", "B"], viewModel.Sources.Select(source => source.Alias));
        Assert.Equal("A", viewModel.SelectedSource?.Alias);
        Assert.Equal(0, a.TotalCalls + b.TotalCalls);
    }

    /// <summary>
    ///     Test that the preferred source alias selects that source, and an unknown alias falls
    ///     back to the first source.
    /// </summary>
    [Fact]
    public void SelectPackageWindowViewModel_Constructor_PreferredSourceAlias_SelectsThatSourceOrFirst()
    {
        // Arrange / Act
        var a = new FakePackageSourceProvider("A");
        var b = new FakePackageSourceProvider("B");
        var preferred = CreateViewModel("b", a, b);
        var unknown = CreateViewModel("Z", a, b);

        // Assert: the alias match ignores case; an unknown alias starts on the first source
        Assert.Equal("B", preferred.SelectedSource?.Alias);
        Assert.Equal("A", unknown.SelectedSource?.Alias);
    }

    /// <summary>
    ///     Test that loading reads every source's packages and shows the selected source's.
    /// </summary>
    [Fact]
    public async Task SelectPackageWindowViewModel_LoadAsync_ReadsEverySourceAndShowsSelectedSourcesPackages()
    {
        // Arrange
        var a = new FakePackageSourceProvider("A", ("contoso-agents", "1.0.0"), ("other", "1.0.0"));
        var b = new FakePackageSourceProvider("B", ("second-agents", "2.0.0"));
        var viewModel = CreateViewModel(a, b);
        Assert.True(viewModel.IsLoading);

        // Act
        await viewModel.LoadAsync(CancellationToken.None);

        // Assert: both sources were read, the selected one's packages are listed, loading ended
        Assert.Equal(1, a.ListPackageIdsCalls);
        Assert.Equal(1, b.ListPackageIdsCalls);
        Assert.Equal(["contoso-agents", "other"], viewModel.PackageNames);
        Assert.Equal("2 packages", viewModel.Sources[0].StatusText);
        Assert.Equal("1 package", viewModel.Sources[1].StatusText);
        Assert.False(viewModel.IsLoading);
    }

    /// <summary>
    ///     Test that one unreachable source reports its own error without blocking the other
    ///     sources, which still load and can be selected from.
    /// </summary>
    [Fact]
    public async Task SelectPackageWindowViewModel_LoadAsync_OneSourceUnreachable_OthersStillLoadAndErrorIsInline()
    {
        // Arrange: source "Down" is a zip folder that does not exist; "Up" works
        var missing = Path.Combine(Path.GetTempPath(), "agentcontrol_missing_source_" + Guid.NewGuid());
        var up = new FakePackageSourceProvider("Up", ("contoso-agents", "1.0.0"));
        var registry = new PackageSourceRegistry(
            [
                new PackageSourceDefinition { Alias = "Down", Type = PackageSourceType.Zip, Location = missing },
                new PackageSourceDefinition { Alias = "Up", Type = PackageSourceType.Zip, Location = "unused" }
            ],
            definition => definition.Alias == "Up" ? up : new ZipFolderSourceProvider(definition));
        var viewModel = new SelectPackageWindowViewModel(registry, new PackageVersionCache());

        // Act
        await viewModel.LoadAsync(CancellationToken.None);

        // Assert: the failed source shows an inline error and offers nothing, the other loaded
        Assert.True(viewModel.HasSourceError);
        Assert.NotNull(viewModel.SourceErrorMessage);
        Assert.Empty(viewModel.PackageNames);
        Assert.Equal("Unavailable", viewModel.Sources[0].StatusText);
        Assert.Equal(["contoso-agents"], viewModel.Sources[1].PackageNames);

        // Act: switch to the working source
        viewModel.SelectedSource = viewModel.Sources[1];

        // Assert: its packages are shown and the error is gone
        Assert.False(viewModel.HasSourceError);
        Assert.Equal(["contoso-agents"], viewModel.PackageNames);
    }

    /// <summary>
    ///     Test that a source with a blank location (a hand-edited settings file) is reported on its
    ///     own row, without stopping the other sources or escaping as an exception.
    /// </summary>
    [Fact]
    public async Task SelectPackageWindowViewModel_LoadAsync_SourceWithBlankLocation_FailsOnlyThatRow()
    {
        // Arrange: "Bad" has a blank location, which its provider refuses to be built with
        var up = new FakePackageSourceProvider("Up", ("contoso-agents", "1.0.0"));
        var registry = new PackageSourceRegistry(
            [
                new PackageSourceDefinition { Alias = "Bad", Type = PackageSourceType.Zip, Location = "  " },
                new PackageSourceDefinition { Alias = "Up", Type = PackageSourceType.Zip, Location = "unused" }
            ],
            definition => definition.Alias == "Up" ? up : new ZipFolderSourceProvider(definition));
        var viewModel = new SelectPackageWindowViewModel(registry, new PackageVersionCache());

        // Act
        await viewModel.LoadAsync(CancellationToken.None);

        // Assert: only the bad row failed
        Assert.Equal("Unavailable", viewModel.Sources[0].StatusText);
        Assert.Equal(["contoso-agents"], viewModel.Sources[1].PackageNames);
    }

    /// <summary>
    ///     Test that a source that loads but offers no packages is reported as empty rather than
    ///     as a failure.
    /// </summary>
    [Fact]
    public async Task SelectPackageWindowViewModel_LoadAsync_SourceWithNoPackages_ReportsEmptyNotError()
    {
        // Arrange
        var viewModel = CreateViewModel(new FakePackageSourceProvider("A"));

        // Act
        await viewModel.LoadAsync(CancellationToken.None);

        // Assert
        Assert.True(viewModel.HasNoPackages);
        Assert.False(viewModel.HasSourceError);
        Assert.False(viewModel.CanConfirm);
    }

    /// <summary>
    ///     Test that loading can be cancelled and the cancellation surfaces to the caller.
    /// </summary>
    [Fact]
    public async Task SelectPackageWindowViewModel_LoadAsync_Cancelled_ThrowsOperationCanceledException()
    {
        // Arrange: a source that never answers unless cancelled
        var registry = new PackageSourceRegistry(
            [new PackageSourceDefinition { Alias = "A", Type = PackageSourceType.Zip, Location = "unused" }],
            _ => new NeverAnsweringPackageSourceProvider("A"));
        var viewModel = new SelectPackageWindowViewModel(registry, new PackageVersionCache());
        using var cancellation = new CancellationTokenSource();

        // Act: start loading, then cancel
        var loading = viewModel.LoadAsync(cancellation.Token);
        await cancellation.CancelAsync();

        // Assert: the cancellation surfaces and the progress indicator ends
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loading);
        Assert.False(viewModel.IsLoading);
    }

    /// <summary>
    ///     Test that opening the dialog again reads a source whose list changed since the previous
    ///     listing (new package, new version) instead of serving the cached one.
    /// </summary>
    [Fact]
    public async Task SelectPackageWindowViewModel_LoadAsync_SourceChangedSincePreviousListing_ShowsTheNewContent()
    {
        // Arrange: a dialog was opened over a shared cache and selected a package
        var provider = new FakePackageSourceProvider("A", ("contoso-agents", "1.0.0"));
        var registry = CreateRegistry(provider);
        var cache = new PackageVersionCache();
        var first = new SelectPackageWindowViewModel(registry, cache);
        await first.LoadAsync(CancellationToken.None);
        first.SelectedPackageName = "contoso-agents";
        await first.PendingVersionsLoad;
        Assert.Equal(["1.0.0"], first.AvailableVersions);

        // Act: a version and a package are published, then the dialog is opened again
        provider.Publish("contoso-agents", "1.1.0");
        provider.Publish("other-agents", "1.0.0");
        var second = new SelectPackageWindowViewModel(registry, cache);
        await second.LoadAsync(CancellationToken.None);
        second.SelectedPackageName = "contoso-agents";
        await second.PendingVersionsLoad;

        // Assert: the new package and version are listed
        Assert.Equal(["contoso-agents", "other-agents"], second.PackageNames);
        Assert.Equal(["1.1.0", "1.0.0"], second.AvailableVersions);
    }

    /// <summary>
    ///     Test that what the dialog fetched is reused while it stays open: switching between
    ///     packages and back does not read the source again.
    /// </summary>
    [Fact]
    public async Task SelectPackageWindowViewModel_SwitchingPackagesWithinOpenDialog_DoesNotReadSourceAgain()
    {
        // Arrange: an open dialog over a source with two packages
        var provider = new FakePackageSourceProvider("A", ("contoso-agents", "1.0.0"), ("other-agents", "2.0.0"));
        var viewModel = CreateViewModel(provider);
        await viewModel.LoadAsync(CancellationToken.None);

        // Act: select one package, another, and the first again
        foreach (var name in new[] { "contoso-agents", "other-agents", "contoso-agents" })
        {
            viewModel.SelectedPackageName = name;
            await viewModel.PendingVersionsLoad;
        }

        // Assert: the package list was read once, and each package's versions once
        Assert.Equal(1, provider.ListPackageIdsCalls);
        Assert.Equal(2, provider.ListVersionsCalls);
    }

    /// <summary>
    ///     Test that Retry of a package's version list reads the source's current content rather
    ///     than a listing cached earlier.
    /// </summary>
    [Fact]
    public async Task SelectPackageWindowViewModel_RetryVersions_ReadsTheSourceFresh()
    {
        // Arrange: a package with no versions yet (cached as "none found", with an error and Retry)
        var provider = new FakePackageSourceProvider("A", ("contoso-agents", "1.0.0"));
        var viewModel = CreateViewModel(provider);
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.SelectedPackageName = "ghost-agents";
        await viewModel.PendingVersionsLoad;
        Assert.True(viewModel.CanRetry);
        Assert.Empty(viewModel.AvailableVersions);
        provider.Publish("ghost-agents", "1.1.0");

        // Act
        viewModel.RetryCommand.Execute(null);
        await viewModel.PendingVersionsLoad;

        // Assert
        Assert.Equal(["1.1.0"], viewModel.AvailableVersions);
    }

    /// <summary>
    ///     Test that selecting a package populates AvailableVersions descending, defaulting the
    ///     selected version to the latest, without blocking the caller.
    /// </summary>

    [Fact]
    public async Task SelectPackageWindowViewModel_SelectPackageName_PopulatesVersionsDescendingAndDefaultsToLatest()
    {
        // Arrange: a source with three versions of one package
        var viewModel = CreateViewModel(new FakePackageSourceProvider(
            "A", ("contoso-agents", "1.0.0"), ("contoso-agents", "2.5.0"), ("contoso-agents", "2.4.9")));
        await viewModel.LoadAsync(CancellationToken.None);

        // Act
        viewModel.SelectedPackageName = "contoso-agents";
        await viewModel.PendingVersionsLoad;

        // Assert: descending order, defaulting to the latest
        Assert.Equal(["2.5.0", "2.4.9", "1.0.0"], viewModel.AvailableVersions);
        Assert.Equal("2.5.0", viewModel.SelectedVersion);
        Assert.False(viewModel.IsLoadingVersions);
    }

    /// <summary>
    ///     Test that the version list shows a progress state while versions are read, and that the
    ///     read does not block the caller.
    /// </summary>
    [Fact]
    public async Task SelectPackageWindowViewModel_SelectPackageName_VersionsSlow_ShowsProgressWithoutBlocking()
    {
        // Arrange: a source whose version listing is held open
        var gate = new TaskCompletionSource();
        var a = new FakePackageSourceProvider("A", ("contoso-agents", "1.0.0")) { ListVersionsGate = gate.Task };
        var viewModel = CreateViewModel(a);
        await viewModel.LoadAsync(CancellationToken.None);

        // Act: select the package; the call returns while the read is still pending
        viewModel.SelectedPackageName = "contoso-agents";

        // Assert: progress is shown and nothing is selectable yet
        Assert.True(viewModel.IsLoadingVersions);
        Assert.Empty(viewModel.AvailableVersions);
        Assert.False(viewModel.CanConfirm);

        // Act: the source answers
        gate.SetResult();
        await viewModel.PendingVersionsLoad;

        // Assert
        Assert.False(viewModel.IsLoadingVersions);
        Assert.Equal(["1.0.0"], viewModel.AvailableVersions);
    }

    /// <summary>
    ///     Test that a newer package selection supersedes a version load still running for the
    ///     older one: the late result of the older load is discarded.
    /// </summary>
    [Fact]
    public async Task SelectPackageWindowViewModel_ChangeSelectedPackageName_SupersedesSlowerEarlierLoad()
    {
        // Arrange: a source with two packages; the first listing is held open
        var gate = new TaskCompletionSource();
        var a = new FakePackageSourceProvider("A", ("contoso-agents", "1.0.0"), ("other-package", "3.0.0"));
        var viewModel = CreateViewModel(a);
        await viewModel.LoadAsync(CancellationToken.None);
        a.ListVersionsGate = gate.Task;
        viewModel.SelectedPackageName = "contoso-agents";
        var earlier = viewModel.PendingVersionsLoad;
        a.ListVersionsGate = null;

        // Act: select the other package, which answers immediately, then release the earlier read
        viewModel.SelectedPackageName = "other-package";
        await viewModel.PendingVersionsLoad;
        gate.SetResult();
        await earlier;

        // Assert: only the newer package's versions are shown
        Assert.Equal(["3.0.0"], viewModel.AvailableVersions);
        Assert.Equal("3.0.0", viewModel.SelectedVersion);
        Assert.False(viewModel.IsLoadingVersions);
    }

    /// <summary>
    ///     Test that a failing version read is reported inline for the selected package.
    /// </summary>
    [Fact]
    public async Task SelectPackageWindowViewModel_SelectPackageName_VersionReadFails_ShowsInlineError()
    {
        // Arrange: a source that lists packages but cannot list versions
        var failing = new FailingPackageSourceProvider("A");
        var registry = new PackageSourceRegistry(
            [new PackageSourceDefinition { Alias = "A", Type = PackageSourceType.Zip, Location = "unused" }],
            _ => failing);
        var viewModel = new SelectPackageWindowViewModel(registry, new PackageVersionCache());
        await viewModel.LoadAsync(CancellationToken.None);

        // Act
        viewModel.SelectedPackageName = "contoso-agents";
        await viewModel.PendingVersionsLoad;

        // Assert
        Assert.True(viewModel.HasVersionsError);
        Assert.Contains("could not be read", viewModel.VersionsError, StringComparison.Ordinal);
        Assert.False(viewModel.CanConfirm);
        Assert.False(viewModel.IsLoadingVersions);
    }

    /// <summary>
    ///     Test that the version column tells the user what to do until a package is selected, and
    ///     stops saying so once one is.
    /// </summary>
    [Fact]
    public async Task SelectPackageWindowViewModel_ShowVersionsHint_UntilPackageSelected()
    {
        // Arrange
        var viewModel = CreateViewModel(new FakePackageSourceProvider("A", ("contoso-agents", "1.0.0")));
        await viewModel.LoadAsync(CancellationToken.None);

        // Assert: no package yet
        Assert.True(viewModel.ShowVersionsHint);

        // Act
        viewModel.SelectedPackageName = "contoso-agents";
        await viewModel.PendingVersionsLoad;

        // Assert
        Assert.False(viewModel.ShowVersionsHint);

        // Act: back to no package
        viewModel.SelectedPackageName = null;

        // Assert
        Assert.True(viewModel.ShowVersionsHint);
    }

    /// <summary>
    ///     Test that a source whose folder is missing is explained in words a user can act on,
    ///     naming the folder, instead of the raw operating-system message.
    /// </summary>
    [Fact]
    public async Task SelectPackageWindowViewModel_LoadAsync_FolderMissing_ExplainsFolderNotFound()
    {
        // Arrange: a source whose package list throws DirectoryNotFoundException
        var registry = new PackageSourceRegistry(
            [new PackageSourceDefinition { Alias = "Share", Type = PackageSourceType.Zip, Location = @"Z:\missing-share" }],
            _ => new DirectoryMissingPackageSourceProvider("Share"));
        var viewModel = new SelectPackageWindowViewModel(registry, new PackageVersionCache());

        // Act
        await viewModel.LoadAsync(CancellationToken.None);

        // Assert
        Assert.True(viewModel.HasSourceError);
        Assert.Contains("folder was not found", viewModel.SourceErrorMessage, StringComparison.Ordinal);
        Assert.Contains(@"Z:\missing-share", viewModel.SourceErrorMessage, StringComparison.Ordinal);
        Assert.False(viewModel.ShowVersionsHint);
        Assert.Equal("Share, Zip-package folder, Unavailable", viewModel.Sources[0].AutomationName);
        Assert.Equal(viewModel.Sources[0].AutomationName, viewModel.Sources[0].ToString());
    }

    /// <summary>
    ///     Test that changing the selected source clears the package and version selection.
    /// </summary>
    [Fact]
    public async Task SelectPackageWindowViewModel_ChangeSelectedSource_ClearsPackageAndVersionSelection()
    {
        // Arrange: a package is selected at source A
        var viewModel = CreateViewModel(
            new FakePackageSourceProvider("A", ("contoso-agents", "1.0.0")),
            new FakePackageSourceProvider("B", ("second-agents", "2.0.0")));
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.SelectedPackageName = "contoso-agents";
        await viewModel.PendingVersionsLoad;
        Assert.True(viewModel.CanConfirm);

        // Act
        viewModel.SelectedSource = viewModel.Sources[1];

        // Assert
        Assert.Null(viewModel.SelectedPackageName);
        Assert.Null(viewModel.SelectedVersion);
        Assert.Empty(viewModel.AvailableVersions);
        Assert.Equal(["second-agents"], viewModel.PackageNames);
        Assert.False(viewModel.CanConfirm);
    }

    /// <summary>
    ///     Test that CanConfirm/ConfirmCommand.CanExecute is false until a source, a name and a
    ///     version are selected.
    /// </summary>
    [Fact]
    public async Task SelectPackageWindowViewModel_NoSelection_CanConfirmIsFalse()
    {
        // Arrange
        var viewModel = CreateViewModel(new FakePackageSourceProvider("A", ("contoso-agents", "1.0.0")));
        await viewModel.LoadAsync(CancellationToken.None);

        // Assert: nothing selected yet
        Assert.False(viewModel.CanConfirm);
        Assert.False(viewModel.ConfirmCommand.CanExecute(null));

        // Act: select a name (the latest version defaults)
        viewModel.SelectedPackageName = "contoso-agents";
        await viewModel.PendingVersionsLoad;
        Assert.True(viewModel.CanConfirm);

        // Act: clear the version explicitly
        viewModel.SelectedVersion = null;

        // Assert: no version selected means confirm is not available
        Assert.False(viewModel.CanConfirm);
        Assert.False(viewModel.ConfirmCommand.CanExecute(null));
    }

    /// <summary>
    ///     Test that executing ConfirmCommand raises Confirmed exactly once with the full package
    ///     reference: source alias, package ID and version.
    /// </summary>
    [Fact]
    public async Task SelectPackageWindowViewModel_ConfirmCommand_RaisesConfirmedWithFullPackageReference()
    {
        // Arrange
        var viewModel = CreateViewModel(new FakePackageSourceProvider("A", ("contoso-agents", "1.0.0")));
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.SelectedPackageName = "contoso-agents";
        await viewModel.PendingVersionsLoad;
        var raised = new List<PackageReference>();
        viewModel.Confirmed += (_, reference) => raised.Add(reference);

        // Act
        viewModel.ConfirmCommand.Execute(null);

        // Assert
        var confirmed = Assert.Single(raised);
        Assert.Equal(new PackageReference("A", "contoso-agents", "1.0.0"), confirmed);
    }

    /// <summary>
    ///     Test that the same package ID offered by two sources is resolved only at the selected
    ///     source: its versions come from that source and the confirmed reference names it.
    /// </summary>
    [Fact]
    public async Task SelectPackageWindowViewModel_SamePackageInTwoSources_UsesOnlySelectedSource()
    {
        // Arrange: A offers 9.0.0, B offers 1.0.0 of the same package ID
        var a = new FakePackageSourceProvider("A", ("contoso-agents", "9.0.0"));
        var b = new FakePackageSourceProvider("B", ("contoso-agents", "1.0.0"));
        var viewModel = CreateViewModel(a, b);
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.SelectedSource = viewModel.Sources[1];
        var raised = new List<PackageReference>();
        viewModel.Confirmed += (_, reference) => raised.Add(reference);

        // Act
        viewModel.SelectedPackageName = "contoso-agents";
        await viewModel.PendingVersionsLoad;
        viewModel.ConfirmCommand.Execute(null);

        // Assert: only B's version is offered, A's versions were never read, and B is named
        Assert.Equal(["1.0.0"], viewModel.AvailableVersions);
        Assert.Equal(0, a.ListVersionsCalls);
        Assert.Equal(new PackageReference("B", "contoso-agents", "1.0.0"), Assert.Single(raised));
    }

    /// <summary>
    ///     Test that a preferred source, package and version (the ones a pinned repo uses, for
    ///     "Change package...") are selected once the source has loaded, so the dialog opens on the
    ///     current choice and can be confirmed at once.
    /// </summary>
    [Fact]
    public async Task SelectPackageWindowViewModel_LoadAsync_PreferredPackageAndVersion_AreSelected()
    {
        // Arrange
        var a = new FakePackageSourceProvider(
            "A", ("contoso-agents", "1.0.0"), ("contoso-agents", "1.1.0"), ("contoso-agents", "2.0.0"), ("other", "1.0.0"));
        var viewModel = CreatePreferringViewModel("A", "contoso-agents", "1.1.0", a);

        // Act
        await viewModel.LoadAsync(CancellationToken.None);

        // Assert
        Assert.Equal("A", viewModel.SelectedSource?.Alias);
        Assert.Equal("contoso-agents", viewModel.SelectedPackageName);
        Assert.Equal("1.1.0", viewModel.SelectedVersion);
        Assert.True(viewModel.CanConfirm);
    }

    /// <summary>
    ///     Test that when the preferred version is no longer listed the package is still selected and
    ///     the version stays the newest one.
    /// </summary>
    [Fact]
    public async Task SelectPackageWindowViewModel_LoadAsync_PreferredVersionNotListed_KeepsNewestVersion()
    {
        // Arrange
        var a = new FakePackageSourceProvider("A", ("contoso-agents", "1.0.0"), ("contoso-agents", "2.0.0"));
        var viewModel = CreatePreferringViewModel("A", "contoso-agents", "0.5.0", a);

        // Act
        await viewModel.LoadAsync(CancellationToken.None);

        // Assert
        Assert.Equal("contoso-agents", viewModel.SelectedPackageName);
        Assert.Equal("2.0.0", viewModel.SelectedVersion);
    }

    /// <summary>
    ///     Test that a preferred package the source does not offer selects nothing.
    /// </summary>
    [Fact]
    public async Task SelectPackageWindowViewModel_LoadAsync_PreferredPackageNotOffered_SelectsNothing()
    {
        // Arrange
        var a = new FakePackageSourceProvider("A", ("contoso-agents", "1.0.0"));
        var viewModel = CreatePreferringViewModel("A", "gone-agents", "1.0.0", a);

        // Act
        await viewModel.LoadAsync(CancellationToken.None);

        // Assert
        Assert.Null(viewModel.SelectedPackageName);
        Assert.Null(viewModel.SelectedVersion);
        Assert.False(viewModel.CanConfirm);
    }

    /// <summary>
    ///     Test that a preferred source that failed to load selects nothing.
    /// </summary>
    [Fact]
    public async Task SelectPackageWindowViewModel_LoadAsync_PreferredSourceFailed_SelectsNothing()
    {
        // Arrange: the preferred source is a zip folder that does not exist
        var missing = Path.Combine(Path.GetTempPath(), "agentcontrol_missing_source_" + Guid.NewGuid());
        var registry = new PackageSourceRegistry(
            [new PackageSourceDefinition { Alias = "Down", Type = PackageSourceType.Zip, Location = missing }],
            definition => new ZipFolderSourceProvider(definition));
        var viewModel = new SelectPackageWindowViewModel(registry, new PackageVersionCache(), "Down", "contoso-agents", "1.0.0");

        // Act
        await viewModel.LoadAsync(CancellationToken.None);

        // Assert
        Assert.True(viewModel.HasSourceError);
        Assert.Null(viewModel.SelectedPackageName);
        Assert.Null(viewModel.SelectedVersion);
    }

    /// <summary>
    ///     Test that a preferred alias that names no configured source preselects nothing: the first
    ///     source is shown and no package is chosen for the user.
    /// </summary>
    [Fact]
    public async Task SelectPackageWindowViewModel_LoadAsync_PreferredAliasUnknown_SelectsNothing()
    {
        // Arrange
        var a = new FakePackageSourceProvider("A", ("contoso-agents", "1.0.0"));
        var viewModel = CreatePreferringViewModel("Z", "contoso-agents", "1.0.0", a);

        // Act
        await viewModel.LoadAsync(CancellationToken.None);

        // Assert
        Assert.Equal("A", viewModel.SelectedSource?.Alias);
        Assert.Null(viewModel.SelectedPackageName);
        Assert.Null(viewModel.SelectedVersion);
    }

    /// <summary>
    ///     Test that a package the user picks while other sources are still loading is not replaced by
    ///     the preferred one when loading ends.
    /// </summary>
    [Fact]
    public async Task SelectPackageWindowViewModel_LoadAsync_UserChoiceBeforeLoadEnds_IsNotOverridden()
    {
        // Arrange: the preferred source A loads at once, B only when released
        var gate = new TaskCompletionSource();
        var a = new FakePackageSourceProvider("A", ("contoso-agents", "1.0.0"), ("other", "1.0.0"));
        var registry = new PackageSourceRegistry(
            [
                new PackageSourceDefinition { Alias = "A", Type = PackageSourceType.Zip, Location = "unused" },
                new PackageSourceDefinition { Alias = "B", Type = PackageSourceType.Zip, Location = "unused" }
            ],
            definition => definition.Alias == "A" ? a : new GatedPackageListProvider("B", gate.Task));
        var viewModel = new SelectPackageWindowViewModel(registry, new PackageVersionCache(), "A", "contoso-agents", "1.0.0");
        var loading = viewModel.LoadAsync(CancellationToken.None);
        for (var attempt = 0; attempt < 1000 && viewModel.Sources[0].IsLoading; attempt++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        // Act: the user chooses another package, then loading ends
        viewModel.SelectedPackageName = "other";
        await viewModel.PendingVersionsLoad;
        gate.SetResult();
        await loading;

        // Assert
        Assert.Equal("other", viewModel.SelectedPackageName);
    }

    /// <summary>
    ///     Test that a user who switched to another source and back to the preferred one while the
    ///     sources were still loading keeps their choice: the preferred package is not selected.
    /// </summary>
    [Fact]
    public async Task SelectPackageWindowViewModel_LoadAsync_UserSwitchedSourceAndBackBeforeLoadEnds_PreferredNotApplied()
    {
        // Arrange: the preferred source A loads at once, B only when released
        var gate = new TaskCompletionSource();
        var a = new FakePackageSourceProvider("A", ("contoso-agents", "1.0.0"), ("other", "1.0.0"));
        var registry = new PackageSourceRegistry(
            [
                new PackageSourceDefinition { Alias = "A", Type = PackageSourceType.Zip, Location = "unused" },
                new PackageSourceDefinition { Alias = "B", Type = PackageSourceType.Zip, Location = "unused" }
            ],
            definition => definition.Alias == "A" ? a : new GatedPackageListProvider("B", gate.Task));
        var viewModel = new SelectPackageWindowViewModel(registry, new PackageVersionCache(), "A", "contoso-agents", "1.0.0");
        var loading = viewModel.LoadAsync(CancellationToken.None);
        for (var attempt = 0; attempt < 1000 && viewModel.Sources[0].IsLoading; attempt++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        // Act: the user looks at B and returns to A, then loading ends
        viewModel.SelectedSource = viewModel.Sources[1];
        viewModel.SelectedSource = viewModel.Sources[0];
        gate.SetResult();
        await loading;

        // Assert: nothing was chosen for the user
        Assert.Equal("A", viewModel.SelectedSource?.Alias);
        Assert.Null(viewModel.SelectedPackageName);
        Assert.Null(viewModel.SelectedVersion);
        Assert.False(viewModel.CanConfirm);
    }

    /// <summary>
    ///     Test that canceling the load while the preferred package's versions are being read cancels
    ///     that read as well, and the cancellation surfaces to the caller.
    /// </summary>
    [Fact]
    public async Task SelectPackageWindowViewModel_LoadAsync_CancelledWhilePreferredVersionsLoad_CancelsThatReadToo()
    {
        // Arrange: the preferred package's versions never arrive unless the read is cancelled
        var a = new FakePackageSourceProvider("A", ("contoso-agents", "1.0.0"))
        {
            ListVersionsGate = new TaskCompletionSource().Task
        };
        var viewModel = CreatePreferringViewModel("A", "contoso-agents", "1.0.0", a);
        using var cancellation = new CancellationTokenSource();
        var loading = viewModel.LoadAsync(cancellation.Token);
        for (var attempt = 0; attempt < 1000 && !viewModel.IsLoadingVersions; attempt++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        // Act
        Assert.True(viewModel.IsLoadingVersions);
        await cancellation.CancelAsync();

        // Assert: the load ends with the cancellation (it would wait for ever if the version read
        // were not cancelled) and the version read is over
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => loading.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        await viewModel.PendingVersionsLoad.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.False(viewModel.IsLoadingVersions);
        Assert.Null(viewModel.SelectedVersion);
        Assert.False(viewModel.IsLoading);
    }

    /// <summary>
    ///     Test that canceling the load after the user chose another package cancels nothing of the
    ///     user's read: the registration made for the preferred read cancels exactly the read it was
    ///     made for, never the newer one.
    /// </summary>
    [Fact]
    public async Task SelectPackageWindowViewModel_LoadAsync_CancelledAfterUserChoseAnotherPackage_DoesNotCancelTheUsersRead()
    {
        // Arrange: the preferred read ignores cancellation (so it is still pending when the token
        // fires); the user's own read honors it
        var provider = new PreferenceRaceProvider("A");
        var registry = new PackageSourceRegistry(
            [new PackageSourceDefinition { Alias = "A", Type = PackageSourceType.Zip, Location = "unused" }],
            _ => provider);
        var viewModel = new SelectPackageWindowViewModel(registry, new PackageVersionCache(), "A", "contoso-agents", "1.0.0");
        using var cancellation = new CancellationTokenSource();
        var loading = viewModel.LoadAsync(cancellation.Token);
        for (var attempt = 0; attempt < 1000 && provider.VersionCalls < 1; attempt++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        // Act: the user chooses another package (a newer read), then the load is cancelled
        viewModel.SelectedPackageName = "other-agents";
        Assert.Equal(2, provider.VersionCalls);
        await cancellation.CancelAsync();

        // Assert: the user's read is still running, and delivers its versions once the source answers
        Assert.True(viewModel.IsLoadingVersions);
        Assert.False(provider.UserReadToken.IsCancellationRequested);
        provider.ReleaseUserRead();
        await viewModel.PendingVersionsLoad.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal(["2.0.0"], viewModel.AvailableVersions);
        Assert.Equal("2.0.0", viewModel.SelectedVersion);

        // Clean up: let the preferred read end, which surfaces the cancellation to the caller
        provider.ReleasePreferredRead();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => loading.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a load whose token is already cancelled when the preferred package would be
    ///     selected makes no selection and starts no version read.
    /// </summary>
    [Fact]
    public async Task SelectPackageWindowViewModel_LoadAsync_CancelledBeforePreferredSelection_StartsNoVersionRead()
    {
        // Arrange: the source's package list arrives, and cancels the load while doing so
        using var cancellation = new CancellationTokenSource();
        var provider = new PreferenceRaceProvider("A") { OnListPackageIds = cancellation.Cancel };
        var registry = new PackageSourceRegistry(
            [new PackageSourceDefinition { Alias = "A", Type = PackageSourceType.Zip, Location = "unused" }],
            _ => provider);
        var viewModel = new SelectPackageWindowViewModel(registry, new PackageVersionCache(), "A", "contoso-agents", "1.0.0");

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => viewModel.LoadAsync(cancellation.Token));

        // Assert: nothing was selected and no version read was begun
        Assert.Null(viewModel.SelectedPackageName);
        Assert.Equal(0, provider.VersionCalls);
        Assert.False(viewModel.IsLoadingVersions);
        Assert.False(viewModel.IsLoading);
    }

    /// <summary>
    ///     Test that a Git source is listed with the Git icon and its kind name, like the other kinds.
    /// </summary>
    [Fact]
    public void SelectPackageWindowViewModel_Constructor_GitSource_ShowsGitIconAndTypeName()
    {
        // Arrange
        var provider = new FakePackageSourceProvider("Repo", ("contoso-agents", "1.0.0"));
        var registry = new PackageSourceRegistry(
            [
                new PackageSourceDefinition
                {
                    Alias = "Repo",
                    Type = PackageSourceType.Git,
                    Location = "https://git.example.com/org/agents.git",
                    TagPattern = "v{version}",
                    PackageIds = ["contoso-agents"]
                }
            ],
            _ => provider);

        // Act
        var viewModel = new SelectPackageWindowViewModel(registry, new PackageVersionCache());

        // Assert
        var row = Assert.Single(viewModel.Sources);
        Assert.Equal(Material.Icons.MaterialIconKind.Git, row.TypeIcon);
        Assert.Equal("Git repository", row.TypeName);
        Assert.Contains("Git repository", row.AutomationName, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that constructing with a null registry or cache throws ArgumentNullException.
    /// </summary>
    [Fact]
    public void SelectPackageWindowViewModel_Constructor_NullArguments_ThrowsArgumentNullException()
    {
        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => new SelectPackageWindowViewModel(null!, new PackageVersionCache()));
        Assert.Throws<ArgumentNullException>(
            () => new SelectPackageWindowViewModel(CreateRegistry(new FakePackageSourceProvider("A")), null!));
    }

    /// <summary>
    ///     Creates the view model under test with a preferred source, package and version.
    /// </summary>
    private static SelectPackageWindowViewModel CreatePreferringViewModel(
        string alias, string package, string version, params FakePackageSourceProvider[] providers) =>
        new(CreateRegistry(providers), new PackageVersionCache(), alias, package, version);

    /// <summary>
    ///     Creates the view model under test over in-memory sources, starting on the first.
    /// </summary>
    private static SelectPackageWindowViewModel CreateViewModel(params FakePackageSourceProvider[] providers) =>
        CreateViewModel(null, providers);

    /// <summary>
    ///     Creates the view model under test over in-memory sources, starting on a given source.
    /// </summary>
    private static SelectPackageWindowViewModel CreateViewModel(
        string? preferredAlias, params FakePackageSourceProvider[] providers) =>
        new(CreateRegistry(providers), new PackageVersionCache(), preferredAlias);

    /// <summary>
    ///     Creates a registry whose sources are served by the given fake providers.
    /// </summary>
    private static PackageSourceRegistry CreateRegistry(params FakePackageSourceProvider[] providers) =>
        new(
            providers.Select(p => new PackageSourceDefinition { Alias = p.Alias, Type = PackageSourceType.Zip, Location = "unused" }),
            definition => providers.Single(p => p.Alias == definition.Alias));

    /// <summary>
    ///     A source offering two packages whose version reads a test controls: the read of
    ///     <c>contoso-agents</c> (the preferred package) ignores cancellation and ends only when
    ///     released, the read of any other package honors it and ends when released.
    /// </summary>
    /// <param name="alias">The alias this provider reports.</param>
    private sealed class PreferenceRaceProvider(string alias) : IPackageSourceProvider
    {
        /// <summary>
        ///     The gate of the preferred package's read.
        /// </summary>
        private readonly TaskCompletionSource _preferredGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        ///     The gate of the other package's read.
        /// </summary>
        private readonly TaskCompletionSource _userGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        ///     The number of version reads begun.
        /// </summary>
        private int _versionCalls;

        /// <inheritdoc />
        public string Alias { get; } = alias;

        /// <summary>
        ///     Gets or sets an action run while the package list is read.
        /// </summary>
        public Action? OnListPackageIds { get; set; }

        /// <summary>
        ///     Gets the number of version reads begun.
        /// </summary>
        public int VersionCalls => Volatile.Read(ref _versionCalls);

        /// <summary>
        ///     Gets the token the read of the other package received.
        /// </summary>
        public CancellationToken UserReadToken { get; private set; }

        /// <summary>
        ///     Lets the read of the preferred package end.
        /// </summary>
        public void ReleasePreferredRead() => _preferredGate.TrySetResult();

        /// <summary>
        ///     Lets the read of the other package end.
        /// </summary>
        public void ReleaseUserRead() => _userGate.TrySetResult();

        /// <inheritdoc />
        public Task<IReadOnlyList<string>> ListPackageIdsAsync(CancellationToken cancellationToken)
        {
            OnListPackageIds?.Invoke();
            return Task.FromResult<IReadOnlyList<string>>(["contoso-agents", "other-agents"]);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<DiscoveredPackage>> ListVersionsAsync(
            string packageId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _versionCalls);
            if (packageId == "contoso-agents")
            {
                await _preferredGate.Task;
                return [new DiscoveredPackage(packageId, Version("1.0.0"), "1.0.0", Alias, null)];
            }

            UserReadToken = cancellationToken;
            await _userGate.Task.WaitAsync(cancellationToken);
            return [new DiscoveredPackage(packageId, Version("2.0.0"), "2.0.0", Alias, null)];
        }

        /// <inheritdoc />
        public Task<IPackageContentReader> AcquireAsync(DiscoveredPackage package, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        /// <summary>
        ///     Parses a version the test wrote.
        /// </summary>
        private static PackageVersion Version(string text) =>
            PackageVersion.TryParse(text, out var parsed) && parsed is not null
                ? parsed
                : throw new ArgumentException("Invalid test version.", nameof(text));
    }

    /// <summary>
    ///     A source whose package list arrives (empty) only when its gate opens, so a test can keep
    ///     the dialog loading while the user acts.
    /// </summary>
    /// <param name="alias">The alias this provider reports.</param>
    /// <param name="gate">The task the package list waits for.</param>
    private sealed class GatedPackageListProvider(string alias, Task gate) : IPackageSourceProvider
    {
        /// <inheritdoc />
        public string Alias { get; } = alias;

        /// <inheritdoc />
        public async Task<IReadOnlyList<string>> ListPackageIdsAsync(CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken);
            return [];
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<DiscoveredPackage>> ListVersionsAsync(
            string packageId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        /// <inheritdoc />
        public Task<IPackageContentReader> AcquireAsync(DiscoveredPackage package, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    /// <summary>
    ///     A source whose package list never arrives unless the caller cancels, for the
    ///     cancellation test.
    /// </summary>
    private sealed class NeverAnsweringPackageSourceProvider : IPackageSourceProvider
    {
        /// <summary>
        ///     Initializes the provider.
        /// </summary>
        /// <param name="alias">The alias this provider reports.</param>
        public NeverAnsweringPackageSourceProvider(string alias) => Alias = alias;

        /// <inheritdoc />
        public string Alias { get; }

        /// <inheritdoc />
        public async Task<IReadOnlyList<string>> ListPackageIdsAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return [];
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<DiscoveredPackage>> ListVersionsAsync(
            string packageId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        /// <inheritdoc />
        public Task<IPackageContentReader> AcquireAsync(DiscoveredPackage package, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    /// <summary>
    ///     A source that can list a package but whose versions cannot be read, for the inline
    ///     error test.
    /// </summary>
    private sealed class FailingPackageSourceProvider : IPackageSourceProvider
    {
        /// <summary>
        ///     Initializes the failing provider.
        /// </summary>
        /// <param name="alias">The alias this provider reports.</param>
        public FailingPackageSourceProvider(string alias) => Alias = alias;

        /// <inheritdoc />
        public string Alias { get; }

        /// <inheritdoc />
        public Task<IReadOnlyList<string>> ListPackageIdsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(["contoso-agents"]);

        /// <inheritdoc />
        public Task<IReadOnlyList<DiscoveredPackage>> ListVersionsAsync(
            string packageId, CancellationToken cancellationToken) =>
            throw new IOException("The source went away.");

        /// <inheritdoc />
        public Task<IPackageContentReader> AcquireAsync(DiscoveredPackage package, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    /// <summary>
    ///     A source whose package list fails because its folder does not exist, like a zip or
    ///     NuGet folder on a share that is offline.
    /// </summary>
    private sealed class DirectoryMissingPackageSourceProvider : IPackageSourceProvider
    {
        /// <summary>
        ///     Initializes the provider.
        /// </summary>
        /// <param name="alias">The alias this provider reports.</param>
        public DirectoryMissingPackageSourceProvider(string alias) => Alias = alias;

        /// <inheritdoc />
        public string Alias { get; }

        /// <inheritdoc />
        public Task<IReadOnlyList<string>> ListPackageIdsAsync(CancellationToken cancellationToken) =>
            throw new DirectoryNotFoundException("Could not find a part of the path 'Z:\\missing-share'.");

        /// <inheritdoc />
        public Task<IReadOnlyList<DiscoveredPackage>> ListVersionsAsync(
            string packageId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        /// <inheritdoc />
        public Task<IPackageContentReader> AcquireAsync(DiscoveredPackage package, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
