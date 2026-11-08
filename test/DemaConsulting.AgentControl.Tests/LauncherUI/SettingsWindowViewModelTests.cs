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
using DemaConsulting.AgentControl.Settings;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Unit tests for <see cref="SettingsWindowViewModel"/>.
/// </summary>
/// <remarks>
///     The folder status check of each source is driven by an injected delegate, so no real
///     folders are needed and the check can be held open on purpose to prove it never blocks.
/// </remarks>
public class SettingsWindowViewModelTests
{
    /// <summary>
    ///     Test that the constructor seeds every editable property from the initial settings.
    /// </summary>
    [Fact]
    public void SettingsWindowViewModel_Constructor_SeedsPropertiesFromInitialSettings()
    {
        // Arrange: a fully-populated initial settings object
        var initial = new AppSettings
        {
            GitExecutablePath = @"C:\tools\git.exe",
            AgentTool = AgentToolKind.Cursor,
            CustomAgentCommand = "some-command",
            ShellPreference = "pwsh"
        };

        // Act: construct the view model
        var viewModel = CreateViewModel(initial);

        // Assert: every property matches the initial settings
        Assert.Equal(@"C:\tools\git.exe", viewModel.GitExecutablePath);
        Assert.Equal(AgentToolKind.Cursor, viewModel.AgentTool);
        Assert.Equal("some-command", viewModel.CustomAgentCommand);
        Assert.Equal("pwsh", viewModel.ShellPreference);
    }

    /// <summary>
    ///     Test that IsCustomAgentToolSelected is false for a well-known agent tool.
    /// </summary>
    [Fact]
    public void SettingsWindowViewModel_IsCustomAgentToolSelected_WellKnownTool_ReturnsFalse()
    {
        // Arrange: an initial CopilotCli selection
        var viewModel = CreateViewModel(new AppSettings { AgentTool = AgentToolKind.CopilotCli });

        // Assert: the custom-command textbox should not be shown
        Assert.False(viewModel.IsCustomAgentToolSelected);
    }

    /// <summary>
    ///     Test that IsCustomAgentToolSelected becomes true when AgentTool is set to Custom, and
    ///     raises PropertyChanged for both properties.
    /// </summary>
    [Fact]
    public void SettingsWindowViewModel_AgentTool_SetToCustom_TogglesIsCustomAgentToolSelected()
    {
        // Arrange: a view model starting on a well-known tool, observed for property changes
        var viewModel = CreateViewModel(new AppSettings { AgentTool = AgentToolKind.CopilotCli });
        var changedProperties = new List<string>();
        viewModel.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName ?? string.Empty);

        // Act: select Custom
        viewModel.AgentTool = AgentToolKind.Custom;

        // Assert: the custom-command textbox should now be shown, and both properties raised
        // PropertyChanged
        Assert.True(viewModel.IsCustomAgentToolSelected);
        Assert.Contains(nameof(SettingsWindowViewModel.AgentTool), changedProperties);
        Assert.Contains(nameof(SettingsWindowViewModel.IsCustomAgentToolSelected), changedProperties);
    }

    /// <summary>
    ///     Test that the constructor lists every configured source, in order, with its alias,
    ///     location and kind; and that a legacy single path appears as the Default source.
    /// </summary>
    [Fact]
    public void SettingsWindowViewModel_Constructor_ListsConfiguredSourcesInOrder()
    {
        // Arrange: a zip folder and a NuGet folder
        var initial = new AppSettings
        {
            Sources = [ZipSource("Zips", @"C:\zips"), NuGetSource("Feed", @"C:\feed", "contoso.agents")]
        };

        // Act
        var viewModel = CreateViewModel(initial);

        // Assert
        Assert.Equal(["Zips", "Feed"], viewModel.Sources.Select(source => source.Alias));
        Assert.Equal(@"C:\feed", viewModel.Sources[1].Location);
        Assert.Equal("Zip-package folder", viewModel.Sources[0].TypeName);
        Assert.Equal("NuGet-package folder", viewModel.Sources[1].TypeName);
        Assert.False(viewModel.HasNoSources);

        // Act / Assert: a legacy single path is shown as the Default zip source
        var legacy = CreateViewModel(new AppSettings { PackageSourcePath = @"E:\legacy" });
        var only = Assert.Single(legacy.Sources);
        Assert.Equal(PackageSourceDefinition.DefaultAlias, only.Alias);
        Assert.Equal(@"E:\legacy", only.Location);
    }

    /// <summary>
    ///     Test that with no source configured the window reports an empty list.
    /// </summary>
    [Fact]
    public void SettingsWindowViewModel_Constructor_NoSources_ReportsHasNoSources()
    {
        // Act
        var viewModel = CreateViewModel(new AppSettings());

        // Assert
        Assert.True(viewModel.HasNoSources);
        Assert.Empty(viewModel.Sources);
    }

    /// <summary>
    ///     Test that each source's folder status is checked without blocking the caller: a row
    ///     shows "checking" while the check is pending and its result afterwards.
    /// </summary>
    [Fact]
    public async Task SettingsWindowViewModel_Constructor_FolderCheckSlow_ShowsCheckingThenResultWithoutBlocking()
    {
        // Arrange: a check that stays pending until released
        var gate = new TaskCompletionSource<bool>();
        var viewModel = new SettingsWindowViewModel(
            new AppSettings { Sources = [ZipSource("Zips", @"C:\zips")] }, _ => { }, (_, _) => gate.Task);
        var row = viewModel.Sources[0];

        // Assert: the constructor returned while the check is pending
        Assert.True(row.IsChecking);
        Assert.Equal("Checking...", row.StatusText);
        Assert.False(row.IsAvailable);
        Assert.False(row.IsMissing);

        // Act: the check answers that the folder exists
        gate.SetResult(true);
        await viewModel.WaitForStatusChecksAsync();

        // Assert
        Assert.False(row.IsChecking);
        Assert.True(row.IsAvailable);
        Assert.Equal("Folder found", row.StatusText);
    }

    /// <summary>
    ///     Test that a folder that does not exist, or whose check fails, is shown as missing.
    /// </summary>
    [Fact]
    public async Task SettingsWindowViewModel_FolderCheck_MissingOrFailing_ShowsMissing()
    {
        // Arrange: one folder that is absent and one whose check throws
        var viewModel = new SettingsWindowViewModel(
            new AppSettings { Sources = [ZipSource("Absent", @"C:\absent"), ZipSource("Broken", @"C:\broken")] },
            _ => { },
            (path, _) => path.Contains("broken", StringComparison.Ordinal)
                ? throw new IOException("share offline")
                : Task.FromResult(false));

        // Act
        await viewModel.WaitForStatusChecksAsync();

        // Assert
        Assert.All(viewModel.Sources, row =>
        {
            Assert.True(row.IsMissing);
            Assert.Equal("Folder not found", row.StatusText);
        });
    }

    /// <summary>
    ///     Test that disposing the view model cancels folder checks still running, leaving their
    ///     rows in the "checking" state rather than hanging.
    /// </summary>
    [Fact]
    public async Task SettingsWindowViewModel_Dispose_CancelsPendingFolderChecks()
    {
        // Arrange: a check that never finishes unless cancelled
        var viewModel = new SettingsWindowViewModel(
            new AppSettings { Sources = [ZipSource("Zips", @"C:\zips")] },
            _ => { },
            async (_, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return true;
            });

        // Act
        viewModel.Dispose();
        await viewModel.WaitForStatusChecksAsync();

        // Assert: the check ended without a result
        Assert.True(viewModel.Sources[0].IsChecking);
    }

    /// <summary>
    ///     Test that the add commands raise EditorRequested with an editor for a new source of the
    ///     right kind, and that the add NuGet command does not offer a server kind.
    /// </summary>
    [Fact]
    public void SettingsWindowViewModel_AddCommands_RaiseEditorRequestedForNewSourceOfEachKind()
    {
        // Arrange
        var viewModel = CreateViewModel(new AppSettings());
        var editors = new List<SourceEditorViewModel>();
        viewModel.EditorRequested += (_, editor) => editors.Add(editor);

        // Act
        viewModel.AddZipSourceCommand.Execute(null);
        viewModel.AddNuGetFolderSourceCommand.Execute(null);

        // Assert
        Assert.Equal(2, editors.Count);
        Assert.All(editors, editor => Assert.True(editor.IsNew));
        Assert.Equal(PackageSourceType.Zip, editors[0].Type);
        Assert.Equal(PackageSourceType.NuGet, editors[1].Type);
        Assert.Equal("Add Zip-Package Folder", editors[0].Title);
        Assert.Equal("Add NuGet-Package Folder", editors[1].Title);
    }

    /// <summary>
    ///     Test that saving a new zip-source editor adds the source, selects it and checks its
    ///     folder.
    /// </summary>
    [Fact]
    public async Task SettingsWindowViewModel_AddZipSource_EditorSaved_AddsAndSelectsSource()
    {
        // Arrange
        var viewModel = CreateViewModel(new AppSettings());
        SourceEditorViewModel? editor = null;
        viewModel.EditorRequested += (_, requested) => editor = requested;
        viewModel.AddZipSourceCommand.Execute(null);
        Assert.NotNull(editor);
        editor.Alias = "Contoso";
        editor.Location = FullPath("contoso");

        // Act
        editor.SaveCommand.Execute(null);
        await viewModel.WaitForStatusChecksAsync();

        // Assert
        var added = Assert.Single(viewModel.Sources);
        Assert.Equal("Contoso", added.Alias);
        Assert.Equal(PackageSourceType.Zip, added.Definition.Type);
        Assert.Same(added, viewModel.SelectedSource);
        Assert.True(added.IsAvailable);
        Assert.False(viewModel.HasNoSources);
    }

    /// <summary>
    ///     Test that saving a new NuGet-folder editor keeps the package IDs entered.
    /// </summary>
    [Fact]
    public void SettingsWindowViewModel_AddNuGetFolderSource_EditorSaved_KeepsPackageIds()
    {
        // Arrange
        var viewModel = CreateViewModel(new AppSettings());
        SourceEditorViewModel? editor = null;
        viewModel.EditorRequested += (_, requested) => editor = requested;
        viewModel.AddNuGetFolderSourceCommand.Execute(null);
        Assert.NotNull(editor);
        editor.Alias = "Feed";
        editor.Location = FullPath("feed");
        editor.NewPackageId = "contoso.agents";
        editor.AddPackageIdCommand.Execute(null);

        // Act
        editor.SaveCommand.Execute(null);

        // Assert
        var added = Assert.Single(viewModel.Sources);
        Assert.Equal(PackageSourceType.NuGet, added.Definition.Type);
        Assert.Equal(["contoso.agents"], added.Definition.PackageIds);
    }

    /// <summary>
    ///     Test that the alias of a new source must be unique among the configured sources,
    ///     ignoring case.
    /// </summary>
    [Fact]
    public void SettingsWindowViewModel_AddSource_AliasAlreadyConfiguredIgnoringCase_EditorCannotSave()
    {
        // Arrange: a source named Contoso exists
        var viewModel = CreateViewModel(new AppSettings { Sources = [ZipSource("Contoso", @"C:\one")] });
        SourceEditorViewModel? editor = null;
        viewModel.EditorRequested += (_, requested) => editor = requested;
        viewModel.AddZipSourceCommand.Execute(null);
        Assert.NotNull(editor);

        // Act: type the same alias in a different case, with a valid folder
        editor.Alias = "CONTOSO";
        editor.Location = FullPath("two");

        // Assert: the duplicate is reported and saving is refused
        Assert.Contains("already exists", editor.AliasError, StringComparison.Ordinal);
        Assert.False(editor.CanSave);
        Assert.False(editor.SaveCommand.CanExecute(null));
        Assert.Single(viewModel.Sources);
    }

    /// <summary>
    ///     Test that the edit command is disabled without a selection and, once a source is
    ///     selected, opens an editor for that source whose save replaces it in place.
    /// </summary>
    [Fact]
    public void SettingsWindowViewModel_EditSource_EditorSaved_ReplacesSourceInPlaceKeepingAlias()
    {
        // Arrange: two sources, none selected
        var initial = new AppSettings { Sources = [ZipSource("A", @"C:\a"), ZipSource("B", @"C:\b")] };
        var viewModel = CreateViewModel(initial);
        SourceEditorViewModel? editor = null;
        viewModel.EditorRequested += (_, requested) => editor = requested;
        Assert.False(viewModel.EditSourceCommand.CanExecute(null));

        // Act: select the first source and edit it
        viewModel.SelectedSource = viewModel.Sources[0];
        Assert.True(viewModel.EditSourceCommand.CanExecute(null));
        viewModel.EditSourceCommand.Execute(null);
        Assert.NotNull(editor);
        Assert.False(editor.IsNew);
        Assert.False(editor.IsAliasEditable);
        editor.Location = FullPath("moved");
        editor.SaveCommand.Execute(null);

        // Assert: replaced in place, alias unchanged, still selected, the live settings untouched
        Assert.Equal(["A", "B"], viewModel.Sources.Select(source => source.Alias));
        Assert.Equal(FullPath("moved"), viewModel.Sources[0].Location);
        Assert.Same(viewModel.Sources[0], viewModel.SelectedSource);
        Assert.Equal(@"C:\a", initial.Sources[0].Location);
    }

    /// <summary>
    ///     Test that the remove command raises RemoveRequested for the selected source only, and is
    ///     disabled without a selection.
    /// </summary>
    [Fact]
    public void SettingsWindowViewModel_RemoveSourceCommand_RaisesRemoveRequestedForSelectedSource()
    {
        // Arrange
        var viewModel = CreateViewModel(new AppSettings { Sources = [ZipSource("A", @"C:\a")] });
        var requested = new List<PackageSourceItemViewModel>();
        viewModel.RemoveRequested += (_, source) => requested.Add(source);
        Assert.False(viewModel.RemoveSourceCommand.CanExecute(null));

        // Act
        viewModel.SelectedSource = viewModel.Sources[0];
        viewModel.RemoveSourceCommand.Execute(null);

        // Assert: requested but not yet removed - the view confirms first
        Assert.Same(viewModel.Sources[0], Assert.Single(requested));
        Assert.Single(viewModel.Sources);
    }

    /// <summary>
    ///     Test that the removal warning names the source and says how many repos are pinned to it,
    ///     and that removing the source clears the selection and the list.
    /// </summary>
    [Fact]
    public void SettingsWindowViewModel_RemoveSource_WarnsAboutPinnedReposAndRemoves()
    {
        // Arrange: two repos pinned to "Contoso" (matched ignoring case) and one to another source
        var initial = new AppSettings
        {
            Sources = [ZipSource("Contoso", @"C:\contoso"), ZipSource("Other", @"C:\other")],
            RecentRepos =
            [
                new RecentRepo { Path = @"C:\r1", PinnedPackageName = "p", PinnedSource = "Contoso" },
                new RecentRepo { Path = @"C:\r2", PinnedPackageName = "p", PinnedSource = "contoso" },
                new RecentRepo { Path = @"C:\r3", PinnedPackageName = "p", PinnedSource = "Other" }
            ]
        };
        var viewModel = CreateViewModel(initial);
        var contoso = viewModel.Sources[0];
        var other = viewModel.Sources[1];
        viewModel.SelectedSource = contoso;

        // Act
        var warning = SettingsWindowViewModel.GetRemovalWarning(contoso);
        var otherWarning = SettingsWindowViewModel.GetRemovalWarning(other);
        viewModel.RemoveSource(contoso);

        // Assert: the warning counts the pinned repos and mentions the source problem
        Assert.Contains("'Contoso'", warning, StringComparison.Ordinal);
        Assert.Contains("2 repos are pinned", warning, StringComparison.Ordinal);
        Assert.Contains("Package source problem", warning, StringComparison.Ordinal);
        Assert.Contains("1 repo is pinned", otherWarning, StringComparison.Ordinal);
        Assert.Equal(["Other"], viewModel.Sources.Select(source => source.Alias));
        Assert.Null(viewModel.SelectedSource);
    }

    /// <summary>
    ///     Test that Save builds an AppSettings from the current properties, invokes the
    ///     onSave callback, and raises Saved.
    /// </summary>
    [Fact]
    public void SettingsWindowViewModel_SaveCommand_Execute_InvokesOnSaveWithCurrentValuesAndRaisesSaved()
    {
        // Arrange: a view model with edited values, capturing the settings passed to onSave
        AppSettings? savedSettings = null;
        var viewModel = new SettingsWindowViewModel(
            new AppSettings { Sources = [ZipSource("Zips", @"D:\packages")] },
            settings => savedSettings = settings,
            (_, _) => Task.FromResult(true))
        {
            GitExecutablePath = "git",
            AgentTool = AgentToolKind.ClaudeCode,
            ShellPreference = "cmd"
        };
        var savedRaised = false;
        viewModel.Saved += (_, _) => savedRaised = true;

        // Act: execute the save command
        viewModel.SaveCommand.Execute(null);

        // Assert: onSave received the edited values, the source list (never the legacy single
        // path), and Saved was raised
        Assert.NotNull(savedSettings);
        var source = Assert.Single(savedSettings.Sources);
        Assert.Equal("Zips", source.Alias);
        Assert.Equal(PackageSourceType.Zip, source.Type);
        Assert.Equal(@"D:\packages", source.Location);
        Assert.Null(savedSettings.PackageSourcePath);
        Assert.Equal("git", savedSettings.GitExecutablePath);
        Assert.Equal(AgentToolKind.ClaudeCode, savedSettings.AgentTool);
        Assert.Equal("cmd", savedSettings.ShellPreference);
        Assert.True(savedRaised);
    }

    /// <summary>
    ///     Test that the Appearance choice starts from the saved setting (the system default when
    ///     none was saved), offers system, light and dark in that order, and is saved with the
    ///     other settings.
    /// </summary>
    [Fact]
    public void SettingsWindowViewModel_Theme_DefaultsToSystemAndIsSavedWithSettings()
    {
        // Arrange
        AppSettings? savedSettings = null;
        var viewModel = new SettingsWindowViewModel(new AppSettings(), settings => savedSettings = settings);
        Assert.Equal(ThemePreference.System, viewModel.Theme);
        Assert.Equal(
            [ThemePreference.System, ThemePreference.Light, ThemePreference.Dark],
            SettingsWindowViewModel.AvailableThemes);

        // Act: choose dark and save
        viewModel.Theme = ThemePreference.Dark;
        viewModel.SaveCommand.Execute(null);

        // Assert
        Assert.NotNull(savedSettings);
        Assert.Equal(ThemePreference.Dark, savedSettings.Theme);
        Assert.Equal(
            ThemePreference.Light,
            new SettingsWindowViewModel(new AppSettings { Theme = ThemePreference.Light }, _ => { }).Theme);
    }

    /// <summary>
    ///     Test that a settings callback that rejects the save keeps the window open with the
    ///     reason shown, and does not report the settings as saved.
    /// </summary>
    [Fact]
    public void SettingsWindowViewModel_SaveCommand_OnSaveThrowsInvalidOperation_ShowsErrorAndDoesNotRaiseSaved()
    {
        // Arrange
        var viewModel = new SettingsWindowViewModel(
            new AppSettings { Sources = [ZipSource("Zips", @"D:\packages")] },
            _ => throw new InvalidOperationException("Settings were not applied. The settings file cannot be written."),
            (_, _) => Task.FromResult(true));
        var savedRaised = false;
        viewModel.Saved += (_, _) => savedRaised = true;

        // Act
        viewModel.SaveCommand.Execute(null);

        // Assert
        Assert.False(savedRaised);
        Assert.True(viewModel.HasError);
        Assert.Contains("cannot be written", viewModel.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the saved source list reflects added, edited and removed sources in order,
    ///     carrying the signature settings of a source whose folder was edited.
    /// </summary>
    [Fact]
    public void SettingsWindowViewModel_Save_AfterAddEditRemove_WritesResultingSourcesInOrder()
    {
        // Arrange: a NuGet source that requires signatures from a pinned signer, a zip source
        const string fingerprint = "A3AF7AF11EBB8EF729D2D91548509717E7E0FF55A129ABC3AEAA8A6940267641";
        var feed = NuGetSource("Feed", @"C:\feed", "contoso.agents");
        feed.RequireSignedPackages = true;
        feed.SignerFingerprint = fingerprint;
        AppSettings? savedSettings = null;
        var viewModel = new SettingsWindowViewModel(
            new AppSettings { Sources = [feed, ZipSource("Old", @"C:\old")] },
            settings => savedSettings = settings,
            (_, _) => Task.FromResult(true));
        SourceEditorViewModel? editor = null;
        viewModel.EditorRequested += (_, requested) => editor = requested;

        // Act: edit the feed's folder, remove "Old", add "New"
        viewModel.SelectedSource = viewModel.Sources[0];
        viewModel.EditSourceCommand.Execute(null);
        editor!.Location = FullPath("feed2");
        editor.SaveCommand.Execute(null);
        viewModel.RemoveSource(viewModel.Sources[1]);
        viewModel.AddZipSourceCommand.Execute(null);
        editor.Alias = "New";
        editor.Location = FullPath("new");
        editor.SaveCommand.Execute(null);
        viewModel.SaveCommand.Execute(null);

        // Assert
        Assert.NotNull(savedSettings);
        Assert.Equal(["Feed", "New"], savedSettings.Sources.Select(source => source.Alias));
        Assert.Equal(FullPath("feed2"), savedSettings.Sources[0].Location);
        Assert.Equal(["contoso.agents"], savedSettings.Sources[0].PackageIds);
        Assert.Equal(fingerprint, savedSettings.Sources[0].SignerFingerprint);
        Assert.True(savedSettings.Sources[0].RequireSignedPackages);
    }

    /// <summary>
    ///     Test that a source location embedding a credential (here from a hand-edited settings
    ///     file) is refused on save: the callback is not invoked, the window does not report
    ///     saved, and the reason is shown without the secret.
    /// </summary>
    [Fact]
    public void SettingsWindowViewModel_Save_SourceLocationWithEmbeddedCredential_IsRefusedWithErrorMessage()
    {
        // Arrange
        var saveCalled = false;
        var savedRaised = false;
        var viewModel = new SettingsWindowViewModel(
            new AppSettings { Sources = [NuGetSource("Feed", "https://user:SentinelSecretValue@feeds.example.com/index.json", "p")] },
            _ => saveCalled = true,
            (_, _) => Task.FromResult(true));
        viewModel.Saved += (_, _) => savedRaised = true;

        // Act
        viewModel.SaveCommand.Execute(null);

        // Assert
        Assert.False(saveCalled);
        Assert.False(savedRaised);
        Assert.True(viewModel.HasError);
        Assert.NotNull(viewModel.ErrorMessage);
        Assert.DoesNotContain("SentinelSecretValue", viewModel.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that constructing with a null initial settings throws ArgumentNullException.
    /// </summary>
    [Fact]
    public void SettingsWindowViewModel_Constructor_NullInitial_ThrowsArgumentNullException()
    {
        // Act / Assert: a null initial settings object is rejected
        Assert.Throws<ArgumentNullException>(() => new SettingsWindowViewModel(null!, _ => { }));
    }

    /// <summary>
    ///     Test that AvailableShellPreferences is non-empty and its first entry is the blank
    ///     "auto-detect" default.
    /// </summary>
    [Fact]
    public void SettingsWindowViewModel_AvailableShellPreferences_IsNonEmptyWithBlankFirstEntry()
    {
        // Assert: the list is non-empty, and the first entry is the blank auto-detect default
        Assert.NotEmpty(SettingsWindowViewModel.AvailableShellPreferences);
        Assert.Equal(string.Empty, SettingsWindowViewModel.AvailableShellPreferences[0]);
    }

    /// <summary>
    ///     Test that AvailableShellPreferences has no duplicate entries.
    /// </summary>
    [Fact]
    public void SettingsWindowViewModel_AvailableShellPreferences_NoDuplicateEntries()
    {
        // Assert: every entry is unique
        var distinctCount = SettingsWindowViewModel.AvailableShellPreferences.Distinct().Count();
        Assert.Equal(SettingsWindowViewModel.AvailableShellPreferences.Count, distinctCount);
    }

    /// <summary>
    ///     Test that AvailableShellPreferences contains the shell presets recognized for the
    ///     current OS: pwsh/powershell/cmd on Windows, bash/zsh/sh elsewhere.
    /// </summary>
    [Fact]
    public void SettingsWindowViewModel_AvailableShellPreferences_CurrentOs_ContainsExpectedPresets()
    {
        if (OperatingSystem.IsWindows())
        {
            // Assert: the Windows preset list
            Assert.Equal(
                [string.Empty, "pwsh", "powershell", "cmd"],
                SettingsWindowViewModel.AvailableShellPreferences);
        }
        else
        {
            // Assert: the non-Windows preset list
            Assert.Equal(
                [string.Empty, "bash", "zsh", "sh"],
                SettingsWindowViewModel.AvailableShellPreferences);
        }
    }

    /// <summary>
    ///     Creates the view model under test with a folder check that finds every folder.
    /// </summary>
    private static SettingsWindowViewModel CreateViewModel(AppSettings initial) =>
        new(initial, _ => { }, (_, _) => Task.FromResult(true));

    /// <summary>
    ///     Builds a fully-qualified folder path valid on every platform the tests run on.
    /// </summary>
    private static string FullPath(string name) => Path.Combine(Path.GetTempPath(), "agentcontrol_settings_vm", name);

    /// <summary>
    ///     Creates a zip-folder source definition.
    /// </summary>
    private static PackageSourceDefinition ZipSource(string alias, string folder) =>
        new() { Alias = alias, Type = PackageSourceType.Zip, Location = folder };

    /// <summary>
    ///     Creates a NuGet source definition offering the given package IDs.
    /// </summary>
    private static PackageSourceDefinition NuGetSource(string alias, string location, params string[] packageIds) =>
        new() { Alias = alias, Type = PackageSourceType.NuGet, Location = location, PackageIds = [.. packageIds] };
}
