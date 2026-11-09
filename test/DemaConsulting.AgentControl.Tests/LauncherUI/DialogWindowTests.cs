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

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.Settings;
using DemaConsulting.AgentControl.Tests.AgentPackageManagement;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Headless tests for the small dialog windows' code-behind (<see cref="MessageBoxWindow"/>,
///     <see cref="ConfirmationWindow"/>, <see cref="AboutWindow"/>, <see cref="SettingsWindow"/>).
/// </summary>
public class DialogWindowTests
{
    /// <summary>
    ///     Clicks the named button the way a user's click does: raises <see cref="Button.ClickEvent"/>
    ///     (for handlers wired in code-behind) and runs the button's <see cref="Button.Command"/>
    ///     (for buttons bound to a view-model command), which raising the event alone does not.
    /// </summary>
    /// <param name="window">The window containing the button.</param>
    /// <param name="name">The button's XAML name.</param>
    internal static void Click(Window window, string name)
    {
        var button = window.FindControl<Button>(name);
        Assert.NotNull(button);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (button.Command is { } command && command.CanExecute(button.CommandParameter))
        {
            command.Execute(button.CommandParameter);
        }
    }

    /// <summary>
    ///     Clicks the named item of a menu the way a user does: opens the menu from its button
    ///     (so the item is in the visual tree and inherits its data context, which is what binds its
    ///     command), then raises <see cref="MenuItem.ClickEvent"/> (which, as for a real click, runs
    ///     the item's <see cref="MenuItem.Command"/>), and closes the menu again.
    /// </summary>
    /// <param name="window">The window containing the menu button.</param>
    /// <param name="menuButtonName">The XAML name of the button that opens the menu.</param>
    /// <param name="name">The menu item's XAML name.</param>
    internal static void ClickMenuItem(Window window, string menuButtonName, string name)
    {
        var button = window.FindControl<Button>(menuButtonName);
        Assert.NotNull(button);
        Assert.NotNull(button.Flyout);
        button.Flyout.ShowAt(button);
        Dispatcher.UIThread.RunJobs();
        try
        {
            var menuItem = window.FindControl<MenuItem>(name);
            Assert.NotNull(menuItem);
            menuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }
        finally
        {
            button.Flyout.Hide();
        }
    }

    /// <summary>
    ///     Test that the message box shows its message and closes when OK is clicked.
    /// </summary>
    [Fact]
    public void MessageBoxWindow_OkClicked_ShowsMessageAndCloses() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var window = new MessageBoxWindow("Something failed");
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Show();

        // Act
        Click(window, "MessageBoxOkButton");

        // Assert
        Assert.Equal("Something failed", window.FindControl<TextBlock>("MessageText")!.Text);
        Assert.True(closed);
    });

    /// <summary>
    ///     Test that the message box constructors reject a null message and support the
    ///     parameterless previewer constructor.
    /// </summary>
    [Fact]
    public void MessageBoxWindow_Constructors_NullMessageThrows_ParameterlessWorks() => HeadlessTestApp.Run(() =>
    {
        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => new MessageBoxWindow(null!));
        new MessageBoxWindow().Close();
    });

    /// <summary>
    ///     Test that Yes closes the confirmation dialog with a true result.
    /// </summary>
    [Fact]
    public void ConfirmationWindow_YesClicked_ReturnsTrue() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var owner = new Window();
        owner.Show();
        try
        {
            var dialog = new ConfirmationWindow("Are you sure?");
            var result = dialog.ShowDialog<bool>(owner);

            // Act
            Click(dialog, "ConfirmationYesButton");

            // Assert
            Assert.True(await result);
            Assert.Equal("Are you sure?", dialog.FindControl<TextBlock>("ConfirmationMessageText")!.Text);
        }
        finally
        {
            owner.Close();
        }
    });

    /// <summary>
    ///     Test that No closes the confirmation dialog with a false result.
    /// </summary>
    [Fact]
    public void ConfirmationWindow_NoClicked_ReturnsFalse() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var owner = new Window();
        owner.Show();
        try
        {
            var dialog = new ConfirmationWindow("Are you sure?");
            var result = dialog.ShowDialog<bool>(owner);

            // Act
            Click(dialog, "ConfirmationNoButton");

            // Assert
            Assert.False(await result);
        }
        finally
        {
            owner.Close();
        }
    });

    /// <summary>
    ///     Test that the confirmation window rejects a null message and supports the
    ///     parameterless previewer constructor.
    /// </summary>
    [Fact]
    public void ConfirmationWindow_Constructors_NullMessageThrows_ParameterlessWorks() => HeadlessTestApp.Run(() =>
    {
        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => new ConfirmationWindow(null!));
        new ConfirmationWindow().Close();
    });

    /// <summary>
    ///     Test that the About window shows its text and dependency list, and closes on Close.
    /// </summary>
    [Fact]
    public void AboutWindow_Constructed_ShowsInfoAndClosesOnCloseClick() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var window = new AboutWindow();
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Show();

        // Assert: static text and dependency list are populated
        Assert.Equal(AboutWindow.DisplayVersion(Program.Version), window.FindControl<TextBlock>("AboutVersionText")!.Text);
        Assert.Equal("Copyright (c) DEMA Consulting", window.FindControl<TextBlock>("AboutCopyrightText")!.Text);
        Assert.Contains("MIT", window.FindControl<TextBlock>("AboutLicenseText")!.Text);
        Assert.NotEmpty(window.FindControl<TextBlock>("AboutTaglineText")!.Text!);
        Assert.NotNull(window.FindControl<ItemsControl>("AboutDependenciesList")!.ItemsSource);

        // Act
        Click(window, "AboutCloseButton");

        // Assert
        Assert.True(closed);
    });

    /// <summary>
    ///     Test that the agent tool picker shows friendly names (not the raw enum names) and that
    ///     choosing one still stores the enum value, so the saved setting is unchanged.
    /// </summary>
    [Fact]
    public void SettingsWindow_AgentToolPicker_ShowsFriendlyNamesAndKeepsEnumValue() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        AppSettings? saved = null;
        var viewModel = new SettingsWindowViewModel(
            new AppSettings { AgentTool = AgentToolKind.ClaudeCode }, updated => saved = updated);
        var window = new SettingsWindow(viewModel);
        window.Show();
        try
        {
            var combo = window.FindControl<ComboBox>("AgentToolComboBox")!;
            Dispatcher.UIThread.RunJobs();

            // Assert: the closed picker names the current tool in words
            Assert.Equal(AgentToolKind.ClaudeCode, combo.SelectedItem);
            var shown = combo.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToList();
            Assert.Contains("Claude Code", shown);
            Assert.DoesNotContain("ClaudeCode", shown);

            // Act: choose another tool and save
            combo.SelectedItem = AgentToolKind.CopilotCli;
            viewModel.SaveCommand.Execute(null);

            // Assert: the stored value is the enum, not its friendly name
            Assert.Equal(AgentToolKind.CopilotCli, saved!.AgentTool);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that saving through the bound view model closes the settings window.
    /// </summary>
    [Fact]
    public void SettingsWindow_SavedRaised_ClosesWindow() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        AppSettings? saved = null;
        var viewModel = new SettingsWindowViewModel(new AppSettings(), updated => saved = updated);
        var window = new SettingsWindow(viewModel);
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Show();

        // Act
        viewModel.SaveCommand.Execute(null);

        // Assert
        Assert.NotNull(saved);
        Assert.True(closed);
    });

    /// <summary>
    ///     Test that the settings window rejects a null view model and the parameterless previewer
    ///     constructor yields an unbound window.
    /// </summary>
    [Fact]
    public void SettingsWindow_NullViewModel_ThrowsAndParameterlessWindowIsUnbound() => HeadlessTestApp.Run(() =>
    {
        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => new SettingsWindow(null!));

        var window = new SettingsWindow();
        window.Show();
        Assert.Null(window.DataContext);
        window.Close();
    });

    /// <summary>
    ///     Test that the settings window lists the configured sources and that "Add Zip-Package
    ///     Folder" opens the editor dialog, whose save adds the source to the list.
    /// </summary>
    [Fact]
    public void SettingsWindow_AddZipSourceClicked_ShowsEditorAndSavingAddsSource() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: one configured source
        var viewModel = new SettingsWindowViewModel(
            new AppSettings
            {
                Sources = [new PackageSourceDefinition { Alias = "Existing", Type = PackageSourceType.Zip, Location = @"C:\x" }]
            },
            _ => { },
            (_, _) => Task.FromResult(true));
        var window = new SettingsWindow(viewModel);
        window.Show();
        try
        {
            Assert.NotNull(window.FindControl<ListBox>("SourcesList"));
            Assert.Single(viewModel.Sources);

            // Act: click the add button; the editor dialog opens
            ClickMenuItem(window, "AddSourceButton", "AddZipSourceButton");
            Dispatcher.UIThread.RunJobs();
            var editorWindow = Assert.Single(window.OwnedWindows.OfType<SourceEditorWindow>());
            var editor = Assert.IsType<SourceEditorViewModel>(editorWindow.DataContext);
            Assert.Equal("Add Zip-Package Folder", editorWindow.Title);
            Assert.False(editorWindow.FindControl<StackPanel>("SourceEditorPackageIdsSection")!.IsVisible);

            // Act: fill it in and save
            editor.Alias = "Contoso";
            editor.Location = Path.Combine(Path.GetTempPath(), "agentcontrol_headless_zips");
            Click(editorWindow, "SourceEditorSaveButton");
            Dispatcher.UIThread.RunJobs();
            await viewModel.WaitForStatusChecksAsync();

            // Assert: the dialog closed and the source was added and selected
            Assert.Empty(window.OwnedWindows);
            Assert.Equal(["Existing", "Contoso"], viewModel.Sources.Select(source => source.Alias));
            Assert.Equal("Contoso", viewModel.SelectedSource?.Alias);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that "Add NuGet-Package Folder" shows the package ID section and that canceling the
    ///     editor adds nothing.
    /// </summary>
    [Fact]
    public void SettingsWindow_AddNuGetFolderClickedThenCancelled_AddsNothing() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var viewModel = new SettingsWindowViewModel(new AppSettings(), _ => { }, (_, _) => Task.FromResult(true));
        var window = new SettingsWindow(viewModel);
        window.Show();

        // Act
        ClickMenuItem(window, "AddSourceButton", "AddNuGetFolderSourceButton");
        Dispatcher.UIThread.RunJobs();
        var editorWindow = Assert.Single(window.OwnedWindows.OfType<SourceEditorWindow>());

        // Assert: NuGet specific section visible; cancel closes without adding
        Assert.Equal("Add NuGet-Package Folder", editorWindow.Title);
        Assert.True(editorWindow.FindControl<StackPanel>("SourceEditorPackageIdsSection")!.IsVisible);
        Click(editorWindow, "SourceEditorCancelButton");
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(window.OwnedWindows);
        Assert.Empty(viewModel.Sources);
        window.Close();
    });

    /// <summary>
    ///     Test that removing a source asks for confirmation (naming the pinned repos) and removes
    ///     the source only after the user answers Yes.
    /// </summary>
    [Fact]
    public void SettingsWindow_RemoveClicked_ConfirmationDecidesRemoval() => HeadlessTestApp.Run(() =>
    {
        // Arrange: a source with a repo pinned to it
        var viewModel = new SettingsWindowViewModel(
            new AppSettings
            {
                Sources = [new PackageSourceDefinition { Alias = "Contoso", Type = PackageSourceType.Zip, Location = @"C:\x" }],
                RecentRepos = [new RecentRepo { Path = @"C:\r", PinnedPackageName = "p", PinnedSource = "Contoso" }]
            },
            _ => { },
            (_, _) => Task.FromResult(true));
        var window = new SettingsWindow(viewModel);
        window.Show();
        viewModel.SelectedSource = viewModel.Sources[0];

        // Act: decline first
        Click(window, "RemoveSourceButton");
        Dispatcher.UIThread.RunJobs();
        var confirmation = Assert.Single(window.OwnedWindows.OfType<ConfirmationWindow>());
        Assert.Contains("1 repo is pinned", confirmation.FindControl<TextBlock>("ConfirmationMessageText")!.Text);
        Click(confirmation, "ConfirmationNoButton");
        Dispatcher.UIThread.RunJobs();

        // Assert: still listed
        Assert.Single(viewModel.Sources);

        // Act: accept
        Click(window, "RemoveSourceButton");
        Dispatcher.UIThread.RunJobs();
        confirmation = Assert.Single(window.OwnedWindows.OfType<ConfirmationWindow>());
        Click(confirmation, "ConfirmationYesButton");
        Dispatcher.UIThread.RunJobs();

        // Assert: removed
        Assert.Empty(viewModel.Sources);
        window.Close();
    });

    /// <summary>
    ///     Test that closing the settings window cancels the view model's folder checks.
    /// </summary>
    [Fact]
    public void SettingsWindow_Closed_CancelsFolderChecks() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: a folder check that only ends by cancellation
        var viewModel = new SettingsWindowViewModel(
            new AppSettings
            {
                Sources = [new PackageSourceDefinition { Alias = "Slow", Type = PackageSourceType.Zip, Location = @"C:\x" }]
            },
            _ => { },
            async (_, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return true;
            });
        var window = new SettingsWindow(viewModel);
        window.Show();

        // Act
        window.Close();
        await viewModel.WaitForStatusChecksAsync();

        // Assert: the check ended without a result
        Assert.True(viewModel.Sources[0].IsChecking);
    });

    /// <summary>
    ///     Test that the source editor window rejects a null view model and supports the
    ///     parameterless previewer constructor.
    /// </summary>
    [Fact]
    public void SourceEditorWindow_Constructors_NullViewModelThrows_ParameterlessWorks() => HeadlessTestApp.Run(() =>
    {
        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => new SourceEditorWindow(null!));
        new SourceEditorWindow().Close();
    });

    /// <summary>
    ///     Test that a valid editor saved through the window closes it with the definition, and
    ///     that an invalid editor's save button is disabled with the error text shown.
    /// </summary>
    [Fact]
    public void SourceEditorWindow_SaveClicked_ClosesWithDefinitionOnlyWhenValid() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var owner = new Window();
        owner.Show();
        try
        {
            var editor = new SourceEditorViewModel(PackageSourceType.Zip, null, _ => false);
            var dialog = new SourceEditorWindow(editor);
            var result = dialog.ShowDialog<PackageSourceDefinition?>(owner);

            // Assert: invalid so far, save disabled; typing a bad alias shows its error
            Assert.False(dialog.FindControl<Button>("SourceEditorSaveButton")!.IsEffectivelyEnabled);
            editor.Alias = "bad/alias";
            Assert.True(dialog.FindControl<TextBlock>("SourceAliasErrorText")!.IsVisible);

            // Act: make it valid and save
            editor.Alias = "Contoso";
            editor.Location = Path.Combine(Path.GetTempPath(), "agentcontrol_headless_zips");
            Assert.True(dialog.FindControl<Button>("SourceEditorSaveButton")!.IsEffectivelyEnabled);
            Click(dialog, "SourceEditorSaveButton");

            // Assert
            var saved = await result;
            Assert.NotNull(saved);
            Assert.Equal("Contoso", saved.Alias);
        }
        finally
        {
            owner.Close();
        }
    });

    /// <summary>
    ///     Test that the select-package window loads every source when it opens, shows a source's
    ///     error inline without blocking the other, and returns the full package reference when the
    ///     user confirms a source, package and version.
    /// </summary>
    [Fact]
    public void SelectPackageWindow_OpenedWithTwoSources_LoadsAndConfirmReturnsPackageReference() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: source "Down" cannot be read, source "Up" offers a package with two versions
        var up = new FakePackageSourceProvider("Up", ("contoso-agents", "1.0.0"), ("contoso-agents", "1.1.0"));
        var registry = new PackageSourceRegistry(
            [
                new PackageSourceDefinition
                {
                    Alias = "Down",
                    Type = PackageSourceType.Zip,
                    Location = Path.Combine(Path.GetTempPath(), "agentcontrol_missing_" + Guid.NewGuid())
                },
                new PackageSourceDefinition { Alias = "Up", Type = PackageSourceType.Zip, Location = "unused" }
            ],
            definition => definition.Alias == "Up" ? up : new ZipFolderSourceProvider(definition));
        var viewModel = new SelectPackageWindowViewModel(registry, new PackageVersionCache());
        var owner = new Window();
        owner.Show();
        try
        {
            var dialog = new SelectPackageWindow(viewModel);
            var result = dialog.ShowDialog<PackageReference?>(owner);

            // Act: wait for the sources to load (the window stays usable meanwhile)
            await WaitUntilAsync(() => viewModel.Sources.All(source => !source.IsLoading));

            // Assert: the failing source shows its error inline; the working one has its package
            Assert.True(viewModel.HasSourceError);
            Assert.True(dialog.FindControl<TextBlock>("SelectPackageSourceErrorText")!.IsVisible);
            viewModel.SelectedSource = viewModel.Sources[1];
            viewModel.SelectedPackageName = "contoso-agents";
            await viewModel.PendingVersionsLoad;
            Assert.Equal(["1.1.0", "1.0.0"], viewModel.AvailableVersions);

            // Act: confirm
            Click(dialog, "SelectPackageConfirmButton");

            // Assert
            Assert.Equal(new PackageReference("Up", "contoso-agents", "1.1.0"), await result);
        }
        finally
        {
            owner.Close();
        }
    });

    /// <summary>
    ///     Polls on the UI thread until a condition holds, processing queued UI work meanwhile.
    /// </summary>
    /// <param name="condition">The condition to wait for.</param>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }
}
