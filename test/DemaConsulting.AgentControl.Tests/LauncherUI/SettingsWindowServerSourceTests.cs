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
using Avalonia.Threading;
using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.PackageCredentials;
using DemaConsulting.AgentControl.Settings;
using DemaConsulting.AgentControl.Tests.AgentPackageManagement;
using DemaConsulting.AgentControl.Tests.PackageCredentials;
using Material.Icons;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Tests of the NuGet-package-server parts of the settings dialogs: the third "Add" action,
///     how a server is listed, and the server editor window with its connection test.
/// </summary>
public class SettingsWindowServerSourceTests
{
    /// <summary>
    ///     A valid https feed address.
    /// </summary>
    private const string FeedAddress = "https://feeds.example.com/artifactory/api/nuget/v3/agents/index.json";

    /// <summary>
    ///     Test that the "Add NuGet-Package Server" action asks for an editor in server mode.
    /// </summary>
    [Fact]
    public void SettingsWindowViewModel_AddNuGetServerSourceCommand_RequestsServerEditor()
    {
        // Arrange
        var viewModel = new SettingsWindowViewModel(new AppSettings(), _ => { }, (_, _) => Task.FromResult(true));
        SourceEditorViewModel? requested = null;
        viewModel.EditorRequested += (_, editor) => requested = editor;

        // Act
        viewModel.AddNuGetServerSourceCommand.Execute(null);

        // Assert
        Assert.NotNull(requested);
        Assert.True(requested.IsServer);
        Assert.True(requested.IsNew);
        Assert.Equal("Add NuGet-Package Server", requested.Title);
    }

    /// <summary>
    ///     Test that a saved server appears in the list with the server icon and kind name, is
    ///     "Not checked" (the folder check is never run for it), and is written to the settings
    ///     with its insecure-HTTP choice.
    /// </summary>
    [Fact]
    public async Task SettingsWindowViewModel_SaveServerSource_ListsItAsNotCheckedAndPersistsIt()
    {
        // Arrange: a folder check that must never be asked about a server
        var asked = new List<string>();
        AppSettings? saved = null;
        var viewModel = new SettingsWindowViewModel(
            new AppSettings(),
            settings => saved = settings,
            (path, _) =>
            {
                asked.Add(path);
                return Task.FromResult(true);
            });
        var editor = viewModel.CreateEditor(PackageSourceType.NuGet, null, server: true);
        editor.Alias = "Feed";
        editor.Location = "http://127.0.0.1:5000/v3/index.json";
        editor.AllowInsecureHttp = true;
        editor.NewPackageId = "contoso-agents";
        editor.AddPackageIdCommand.Execute(null);

        // Act
        editor.SaveCommand.Execute(null);
        await viewModel.WaitForStatusChecksAsync();
        viewModel.SaveCommand.Execute(null);

        // Assert: listed as a server whose status says it is not checked
        var row = Assert.Single(viewModel.Sources);
        Assert.True(row.IsServer);
        Assert.Equal(MaterialIconKind.ServerNetwork, row.TypeIcon);
        Assert.Equal("NuGet-package server", row.TypeName);
        Assert.Equal("Not checked", row.StatusText);
        Assert.Contains("Test connection", row.StatusTooltip, StringComparison.Ordinal);
        Assert.False(row.IsAvailable);
        Assert.False(row.IsMissing);
        Assert.False(row.IsChecking);
        Assert.Empty(asked);

        // Assert: persisted with its settings
        Assert.NotNull(saved);
        var source = Assert.Single(saved.Sources);
        Assert.Equal("http://127.0.0.1:5000/v3/index.json", source.Location);
        Assert.True(source.AllowInsecureHttp);
        Assert.Equal(["contoso-agents"], source.PackageIds);
    }

    /// <summary>
    ///     Test that editing a listed server opens the server editor, and that the editor uses the
    ///     supplied provider factory for its connection test.
    /// </summary>
    [Fact]
    public async Task SettingsWindowViewModel_EditServerSource_OpensServerEditorThatTestsThroughTheFactory()
    {
        // Arrange
        var fake = new FakePackageSourceProvider("Feed", ("contoso-agents", "2.0.0"));
        var factoryCalls = 0;
        var settings = new AppSettings
        {
            Sources =
            [
                new PackageSourceDefinition
                {
                    Alias = "Feed",
                    Type = PackageSourceType.NuGet,
                    Location = FeedAddress,
                    PackageIds = ["contoso-agents"]
                }
            ]
        };
        var viewModel = new SettingsWindowViewModel(
            settings,
            _ => { },
            (_, _) => Task.FromResult(true),
            _ =>
            {
                factoryCalls++;
                return fake;
            });
        SourceEditorViewModel? requested = null;
        viewModel.EditorRequested += (_, editor) => requested = editor;
        viewModel.SelectedSource = viewModel.Sources[0];

        // Act
        viewModel.EditSourceCommand.Execute(null);
        Assert.NotNull(requested);
        await requested.TestConnectionCommand.ExecuteAsync();

        // Assert
        Assert.True(requested.IsServer);
        Assert.Equal("Edit NuGet-Package Server", requested.Title);
        Assert.Equal(1, factoryCalls);
        Assert.Equal("Connection OK", requested.TestResultTitle);
    }

    /// <summary>
    ///     Test that the settings window offers one Add button whose menu holds the four source
    ///     types (each with its icon and a description), and that the server editor
    ///     window shows the feed-address field with examples, the insecure-HTTP checkbox, the test
    ///     button, and no Browse button.
    /// </summary>
    [Fact]
    public void SettingsWindow_AddNuGetServerSourceButton_OpensServerEditorWithServerControls() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var viewModel = new SettingsWindowViewModel(new AppSettings(), _ => { }, (_, _) => Task.FromResult(true));
        var settingsWindow = new SettingsWindow(viewModel);
        settingsWindow.Show();
        try
        {
            // Assert: one Add button with a menu of all four actions (items, not separate buttons)
            var addButton = settingsWindow.FindControl<Button>("AddSourceButton");
            Assert.NotNull(addButton);
            var menu = Assert.IsType<MenuFlyout>(addButton.Flyout);
            Assert.Equal(
                ["AddZipSourceButton", "AddNuGetFolderSourceButton", "AddNuGetServerSourceButton", "AddGitSourceButton"],
                menu.Items.OfType<MenuItem>().Select(item => item.Name));
            Assert.All(menu.Items.OfType<MenuItem>(), item => Assert.NotNull(item.Icon));
            Assert.IsType<MenuItem>(settingsWindow.FindControl<Control>("AddZipSourceButton"));
            Assert.IsType<MenuItem>(settingsWindow.FindControl<Control>("AddNuGetFolderSourceButton"));
            Assert.IsType<MenuItem>(settingsWindow.FindControl<Control>("AddNuGetServerSourceButton"));

            // Act: ask for the server editor and show it
            SourceEditorViewModel? requested = null;
            viewModel.EditorRequested += (_, editor) => requested = editor;
            DialogWindowTests.ClickMenuItem(settingsWindow, "AddSourceButton", "AddNuGetServerSourceButton");
            Assert.NotNull(requested);
            var editorWindow = new SourceEditorWindow(requested);
            var result = editorWindow.ShowDialog<PackageSourceDefinition?>(settingsWindow);

            // Assert: server controls are shown, the folder-only Browse button is not
            Assert.Equal("Feed address", editorWindow.FindControl<TextBlock>("SourceLocationLabel")!.Text);
            Assert.True(editorWindow.FindControl<TextBlock>("SourceLocationExamplesText")!.IsVisible);
            Assert.Contains("JFrog Artifactory", editorWindow.FindControl<TextBlock>("SourceLocationExamplesText")!.Text, StringComparison.Ordinal);
            Assert.True(editorWindow.FindControl<CheckBox>("SourceAllowInsecureHttpCheckBox")!.IsVisible);
            Assert.False(editorWindow.FindControl<TextBlock>("SourceInsecureHttpWarningText")!.IsVisible);
            Assert.True(editorWindow.FindControl<StackPanel>("SourceEditorTestSection")!.IsVisible);
            Assert.False(editorWindow.FindControl<Button>("SourceBrowseButton")!.IsVisible);
            Assert.False(editorWindow.FindControl<Button>("SourceTestConnectionButton")!.IsEffectivelyEnabled);

            // Act: tick the insecure-HTTP box
            editorWindow.FindControl<CheckBox>("SourceAllowInsecureHttpCheckBox")!.IsChecked = true;

            // Assert: the warning shows
            Assert.True(editorWindow.FindControl<TextBlock>("SourceInsecureHttpWarningText")!.IsVisible);

            DialogWindowTests.Click(editorWindow, "SourceEditorCancelButton");
            Assert.Null(await result);
        }
        finally
        {
            settingsWindow.Close();
        }
    });

    /// <summary>
    ///     Test that the server editor window runs the connection test: while it runs the progress
    ///     and Cancel button show, and afterwards the result is shown with its headline and detail.
    /// </summary>
    [Fact]
    public void SourceEditorWindow_TestConnection_ShowsProgressCancelAndResult() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: a provider that answers when released
        var release = new TaskCompletionSource();
        var fake = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0")) { ListVersionsGate = release.Task };
        var editor = new SourceEditorViewModel(PackageSourceType.NuGet, null, _ => false, isServer: true, _ => fake);
        editor.Location = FeedAddress;
        editor.NewPackageId = "contoso-agents";
        editor.AddPackageIdCommand.Execute(null);
        var owner = new Window();
        owner.Show();
        try
        {
            var window = new SourceEditorWindow(editor);
            var result = window.ShowDialog<PackageSourceDefinition?>(owner);
            Assert.True(window.FindControl<Button>("SourceTestConnectionButton")!.IsEffectivelyEnabled);
            Assert.False(window.FindControl<Button>("SourceTestCancelButton")!.IsVisible);

            // Act: start the test
            DialogWindowTests.Click(window, "SourceTestConnectionButton");

            // Assert: progress and Cancel are shown, the result is not
            Assert.True(window.FindControl<StackPanel>("SourceTestProgressPanel")!.IsVisible);
            Assert.True(window.FindControl<Button>("SourceTestCancelButton")!.IsVisible);

            // The test's own button says what it cancels, so it cannot be mistaken for the dialog's Cancel
            Assert.Equal("Cancel test", window.FindControl<Button>("SourceTestCancelButton")!.Content);
            Assert.Equal("Cancel", window.FindControl<Button>("SourceEditorCancelButton")!.Content);
            Assert.False(window.FindControl<Border>("SourceTestResultPanel")!.IsVisible);

            // Act: the server answers
            release.SetResult();
            await WaitUntilAsync(() => !editor.IsTesting);

            // Assert: the result replaces the progress
            Assert.False(window.FindControl<StackPanel>("SourceTestProgressPanel")!.IsVisible);
            Assert.True(window.FindControl<Border>("SourceTestResultPanel")!.IsVisible);
            Assert.Equal("Connection OK", window.FindControl<TextBlock>("SourceTestResultTitleText")!.Text);
            Assert.Contains("1 version", window.FindControl<TextBlock>("SourceTestResultDetailText")!.Text, StringComparison.Ordinal);

            DialogWindowTests.Click(window, "SourceEditorCancelButton");
            Assert.Null(await result);
        }
        finally
        {
            owner.Close();
        }
    });

    /// <summary>
    ///     Test that closing the editor while a test runs abandons the test instead of leaving it
    ///     running.
    /// </summary>
    [Fact]
    public void SourceEditorWindow_ClosedWhileTesting_CancelsTheTest() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var release = new TaskCompletionSource();
        var fake = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0")) { ListVersionsGate = release.Task };
        var editor = new SourceEditorViewModel(PackageSourceType.NuGet, null, _ => false, isServer: true, _ => fake);
        editor.Location = FeedAddress;
        editor.NewPackageId = "contoso-agents";
        editor.AddPackageIdCommand.Execute(null);
        var owner = new Window();
        owner.Show();
        try
        {
            var window = new SourceEditorWindow(editor);
            var result = window.ShowDialog<PackageSourceDefinition?>(owner);
            DialogWindowTests.Click(window, "SourceTestConnectionButton");
            Assert.True(editor.IsTesting);

            // Act
            DialogWindowTests.Click(window, "SourceEditorCancelButton");
            Assert.Null(await result);
            await WaitUntilAsync(() => !editor.IsTesting);

            // Assert
            Assert.False(editor.IsTesting);
            Assert.True(editor.TestWasCanceled);
        }
        finally
        {
            owner.Close();
        }
    });

    /// <summary>
    ///     Test that the application's view model hands out a real NuGet provider for the editor's
    ///     connection test, wired to the same credential facilities as the cards.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_CreateConnectionTestProvider_ReturnsNuGetProviderWithCredentials()
    {
        // Arrange
        var credentials = new PackageCredentialContext(new SessionCredentialStore(), new FakeCredentialPrompt());
        var viewModel = new MainWindowViewModel(new AppSettings(), credentials: credentials);
        var definition = new PackageSourceDefinition
        {
            Alias = "Feed",
            Type = PackageSourceType.NuGet,
            Location = FeedAddress,
            PackageIds = ["contoso-agents"]
        };

        // Act
        var provider = viewModel.CreateConnectionTestProvider(definition);

        // Assert
        Assert.IsType<NuGetSourceProvider>(provider);
        Assert.Same(credentials, viewModel.Credentials);
        Assert.Equal("Feed", provider.Alias);
    }

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
