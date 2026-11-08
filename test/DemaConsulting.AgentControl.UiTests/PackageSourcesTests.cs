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

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     End-to-end tests for managing several package sources through the Settings window's
///     list and add dialogs, for selecting a package from a particular source, and for the repo
///     card's chips that state which agent package (and version) a repo has.
/// </summary>
/// <remarks>
///     Each test drives the published application through UI Automation patterns, reusing
///     <see cref="AgentControlTestContext"/>; dialogs are found by an element they contain
///     because a modal dialog's title is not a dependable match target.
/// </remarks>
public sealed class PackageSourcesTests
{
    /// <summary>
    ///     Adding a zip-package folder through the Settings window's dialog lists it with its alias
    ///     and a "Folder found" status, and saving the settings persists it.
    /// </summary>
    [Fact]
    public void SettingsWindow_AddZipPackageFolderViaDialog_ListsSourceAndPersistsIt()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        var zipFolder = CreateTempFolder();
        try
        {
            using var context = new AgentControlTestContext();
            var mainWindow = context.Launch();

            // Act: open Settings and the Add Zip-Package Folder dialog
            var settingsWindow = OpenSettings(context, mainWindow);
            AgentControlTestContext.InvokeAddSourceMenuItem(settingsWindow, "AddZipSourceButton");
            var editor = context.WaitForWindowWithDescendant("SourceAliasTextBox", TimeSpan.FromSeconds(15));
            Assert.NotNull(editor);

            // Assert: the dialog starts invalid, so Save is disabled
            Assert.False(Find(editor, "SourceEditorSaveButton").AsButton().IsEnabled);

            // Act: fill in the alias and folder, then save
            Find(editor, "SourceAliasTextBox").AsTextBox().Text = "Contoso";
            Find(editor, "SourceLocationTextBox").AsTextBox().Text = zipFolder;
            var save = Find(editor, "SourceEditorSaveButton").AsButton();
            Assert.True(Retry.WhileFalse(() => save.IsEnabled, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200)).Result);
            save.Invoke();

            // Assert: the source is listed with a found folder
            Assert.True(WaitForText(settingsWindow, "SourceAliasText", "Contoso"));
            Assert.True(WaitForText(settingsWindow, "SourceStatusText", "Folder found"));

            // Act: save the settings
            Find(settingsWindow, "SaveSettingsButton").AsButton().Invoke();

            // Assert: the source was persisted as a zip source
            var settingsPath = Path.Combine(context.ConfigDirectory, "settings.json");
            Assert.True(Retry.WhileFalse(
                () => File.Exists(settingsPath) && File.ReadAllText(settingsPath).Contains("\"Contoso\"", StringComparison.Ordinal),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromMilliseconds(200)).Result);
            var json = File.ReadAllText(settingsPath);
            Assert.Contains("\"Type\": \"Zip\"", json, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(zipFolder);
        }
    }

    /// <summary>
    ///     The Settings window's Add button opens a menu that offers all three source types - a
    ///     zip-package folder, a NuGet-package folder and a NuGet-package server - each with a drawn icon
    ///     and a one-line description, and choosing the NuGet-package folder item opens its own dialog.
    /// </summary>
    [Fact]
    public void SettingsWindow_AddMenu_OffersThreeSourceTypesWithIconAndDescription()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: the Settings window
        using var context = new AgentControlTestContext();
        var mainWindow = context.Launch();
        var settingsWindow = OpenSettings(context, mainWindow);

        // Act: open the Add menu
        Find(settingsWindow, "AddSourceButton").AsButton().Invoke();

        // Assert: each source type has its item, titled and described, with an icon drawn at its left
        (string Id, string Title, string Description)[] expected =
        [
            ("AddZipSourceButton", "Zip-package folder", "A folder of agent package .zip files"),
            ("AddNuGetFolderSourceButton", "NuGet-package folder", "A folder of NuGet packages (.nupkg files)"),
            ("AddNuGetServerSourceButton", "NuGet-package server", "A server such as Artifactory or Azure Artifacts")
        ];
        foreach (var (id, title, description) in expected)
        {
            var item = Find(settingsWindow, id);
            Assert.NotNull(item.FindFirstDescendant(cf => cf.ByName(title)));
            Assert.NotNull(item.FindFirstDescendant(cf => cf.ByName(description)));

            // The menu slides open: measure once the item has stopped moving
            var previous = default(System.Drawing.Rectangle);
            Assert.True(Retry.WhileFalse(
                () =>
                {
                    var current = item.BoundingRectangle;
                    var still = current == previous;
                    previous = current;
                    return still;
                },
                TimeSpan.FromSeconds(10),
                TimeSpan.FromMilliseconds(300)).Result);
            var bounds = previous;
            var iconArea = new System.Drawing.Rectangle(
                bounds.Left + 4, bounds.Top + 4, 28, Math.Max(bounds.Height - 8, 1));
            Assert.True(RenderedTextProbe.CountInkPixels(iconArea) > 20, $"{id} shows no icon.");
        }

        // Act: choose the NuGet-package folder item
        Find(settingsWindow, "AddNuGetFolderSourceButton").AsMenuItem().Invoke();

        // Assert: its own dialog opens, with the package-ID entry a NuGet source needs
        Assert.NotNull(context.WaitForDialog("SourcePackageIdTextBox", TimeSpan.FromSeconds(15)));
    }

    /// <summary>
    ///     Adding a NuGet-package folder through the dialog requires at least one package ID, and the
    ///     saved source lists the IDs entered.
    /// </summary>
    [Fact]
    public void SettingsWindow_AddNuGetPackageFolderViaDialog_RequiresPackageIdAndPersistsIt()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        var feedFolder = CreateTempFolder();
        try
        {
            using var context = new AgentControlTestContext();
            var mainWindow = context.Launch();

            // Act: open the Add NuGet-Package Folder dialog and fill in alias and folder only
            var settingsWindow = OpenSettings(context, mainWindow);
            AgentControlTestContext.InvokeAddSourceMenuItem(settingsWindow, "AddNuGetFolderSourceButton");
            var editor = context.WaitForWindowWithDescendant("SourcePackageIdTextBox", TimeSpan.FromSeconds(15));
            Assert.NotNull(editor);
            Find(editor, "SourceAliasTextBox").AsTextBox().Text = "Feed";
            Find(editor, "SourceLocationTextBox").AsTextBox().Text = feedFolder;

            // Assert: without a package ID the source cannot be saved, and the dialog says why
            Assert.False(Find(editor, "SourceEditorSaveButton").AsButton().IsEnabled);
            Assert.Contains(
                "at least one package ID",
                Find(editor, "SourcePackageIdsErrorText").Name,
                StringComparison.Ordinal);

            // Act: add a package ID, then save
            Find(editor, "SourcePackageIdTextBox").AsTextBox().Text = "contoso-agents";
            Find(editor, "SourceAddPackageIdButton").AsButton().Invoke();
            var save = Find(editor, "SourceEditorSaveButton").AsButton();
            Assert.True(Retry.WhileFalse(() => save.IsEnabled, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200)).Result);
            save.Invoke();

            // Assert: listed, and persisted as a NuGet source with its package ID
            Assert.True(WaitForText(settingsWindow, "SourceAliasText", "Feed"));
            Find(settingsWindow, "SaveSettingsButton").AsButton().Invoke();
            var settingsPath = Path.Combine(context.ConfigDirectory, "settings.json");
            Assert.True(Retry.WhileFalse(
                () => File.Exists(settingsPath) && File.ReadAllText(settingsPath).Contains("\"Feed\"", StringComparison.Ordinal),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromMilliseconds(200)).Result);
            var json = File.ReadAllText(settingsPath);
            Assert.Contains("\"Type\": \"NuGet\"", json, StringComparison.Ordinal);
            Assert.Contains("contoso-agents", json, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(feedFolder);
        }
    }

    /// <summary>
    ///     A package can be selected from the NuGet-package folder source in the Select Package
    ///     window (the source is chosen first); the repo is pinned to that source, its files come
    ///     from the NuGet package, and the card's package chip shows the package name and version.
    /// </summary>
    [Fact]
    public void SelectPackage_FromNuGetFolderSource_PinsThatSourceAndCardShowsPackageChip()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        const string packageName = "contoso-agents";
        var zipDirectory = CreateTempFolder();
        var feedDirectory = CreateTempFolder();
        try
        {
            FixturePackageBuilder.Create(zipDirectory, packageName, "9.0.0", "# zip 9.0.0\n");
            NuGetFixturePackageBuilder.Create(feedDirectory, packageName, "1.0.0", "from feed");

            // A never-pinned repo with two sources offering the same package ID
            using var context = new AgentControlTestContext(
                packageSourcePath: zipDirectory,
                nuGetFeedPath: feedDirectory,
                nuGetPackageIds: [packageName]);
            var mainWindow = context.Launch();

            // Assert: the card says plainly that no agent package is pinned yet
            Assert.NotNull(Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("NoPackageChip")),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromMilliseconds(200)).Result);

            // Act: open the Select Package window from the card's menu. Under full-suite load the
            // first Invoke can land before the card's flyout is ready (the card is still running
            // its background source checks), so re-open the menu a few times before giving up.
            AutomationElement? selectPackageElement = null;
            for (var attempt = 0; attempt < 3 && selectPackageElement is null; attempt++)
            {
                Find(mainWindow, "MoreActionsButton").AsButton().Invoke();
                selectPackageElement = Retry.WhileNull(
                    () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("SelectPackageMenuItem")),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromMilliseconds(200)).Result;
            }

            var selectPackageMenuItem = selectPackageElement?.AsMenuItem();
            Assert.NotNull(selectPackageMenuItem);
            selectPackageMenuItem.Click();
            var dialog = context.WaitForWindowWithDescendant("SelectPackageSourceList", TimeSpan.FromSeconds(15));
            Assert.NotNull(dialog);

            // Act: choose the NuGet source ("Feed"), then its package
            SelectListItemByText(dialog, "SelectPackageSourceAliasText", "Feed");
            var nameItem = Retry.WhileNull(
                () => dialog.FindFirstDescendant(cf => cf.ByAutomationId("SelectPackageNameList"))
                    ?.FindFirstDescendant(cf => cf.ByName(packageName)),
                TimeSpan.FromSeconds(15),
                TimeSpan.FromMilliseconds(200)).Result?.AsListBoxItem();
            Assert.NotNull(nameItem);
            nameItem.Select();

            // Act: the version defaults to the latest once loaded; confirm
            var confirm = Find(dialog, "SelectPackageConfirmButton").AsButton();
            Assert.True(Retry.WhileFalse(() => confirm.IsEnabled, TimeSpan.FromSeconds(15), TimeSpan.FromMilliseconds(200)).Result);
            confirm.Invoke();

            // Assert: the release notes window opens once the package is applied
            Assert.NotNull(context.WaitForWindow(
                title => title.StartsWith("Release Notes", StringComparison.Ordinal), TimeSpan.FromSeconds(30)));

            // Assert: the files came from the NuGet package and the pin names the NuGet source
            Assert.Equal("from feed", File.ReadAllText(Path.Combine(context.RepoPath, ".github", "agents", "feed.md")));
            var pinJson = File.ReadAllText(Path.Combine(context.RepoPath, ".agentcontrol.json"));
            Assert.Contains("\"Source\": \"Feed\"", pinJson, StringComparison.Ordinal);

            // Assert: the card's package chip shows the package name and version, and the "no
            // package" chip is gone
            Assert.True(WaitForText(mainWindow, "RepoPinnedVersionText", $"{packageName} 1.0.0"));
            Assert.Null(mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("NoPackageChip")));
        }
        finally
        {
            TryDelete(zipDirectory);
            TryDelete(feedDirectory);
        }
    }

    /// <summary>
    ///     A pinned repo's card shows a package chip with the package name and version, an upgrade
    ///     chip with the version an upgrade would move to, and the Launch button names the tool
    ///     generically when the configured tool has no short name.
    /// </summary>
    [Fact]
    public void RepoCard_PinnedRepoWithNewerVersion_ShowsPackageAndUpgradeChips()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        const string packageName = "contoso-agents";
        var zipDirectory = CreateTempFolder();
        try
        {
            FixturePackageBuilder.Create(zipDirectory, packageName, "1.0.0");
            FixturePackageBuilder.Create(zipDirectory, packageName, "1.1.0");
            using var context = new AgentControlTestContext(
                packageSourcePath: zipDirectory,
                pinnedPackageName: packageName,
                pinnedPackageVersion: "1.0.0");
            var mainWindow = context.Launch();

            // Assert: package chip, then the upgrade chip once the background check has run
            Assert.True(WaitForText(mainWindow, "RepoPinnedVersionText", $"{packageName} 1.0.0"));
            Assert.True(WaitForText(mainWindow, "UpgradeChipText", "1.1.0"));
            Assert.Null(mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("NoPackageChip")));

            // Assert: the Launch button carries its label (the arg-logger stub's name is too long to
            // spell out in the Launch label) and Git Pull is in the card's "..." menu
            Assert.True(WaitForText(mainWindow, "LaunchButtonText", "Launch"));
            Assert.Null(mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("PullButton")));
            mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("MoreActionsButton"))!.AsButton().Invoke();
            Assert.NotNull(Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("PullButton")),
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200)).Result);
        }
        finally
        {
            TryDelete(zipDirectory);
        }
    }

    /// <summary>
    ///     Opens the Settings window from the main window.
    /// </summary>
    /// <param name="context">The running test context.</param>
    /// <param name="mainWindow">The main window.</param>
    /// <returns>The Settings window.</returns>
    private static Window OpenSettings(AgentControlTestContext context, Window mainWindow)
    {
        Find(mainWindow, "OpenSettingsButton").AsButton().Invoke();
        var settingsWindow = context.WaitForWindowWithDescendant("AddSourceButton", TimeSpan.FromSeconds(15));
        Assert.NotNull(settingsWindow);
        return settingsWindow;
    }

    /// <summary>
    ///     Finds an element by AutomationId, waiting briefly for it to appear.
    /// </summary>
    /// <param name="parent">The window or element to search under.</param>
    /// <param name="automationId">The AutomationId to find.</param>
    /// <returns>The element.</returns>
    private static AutomationElement Find(AutomationElement parent, string automationId)
    {
        var element = Retry.WhileNull(
            () => parent.FindFirstDescendant(cf => cf.ByAutomationId(automationId)),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result;
        Assert.NotNull(element);
        return element;
    }

    /// <summary>
    ///     Waits until an element with the given AutomationId shows the given text.
    /// </summary>
    /// <param name="parent">The window or element to search under.</param>
    /// <param name="automationId">The AutomationId of the text element.</param>
    /// <param name="expected">The text expected.</param>
    /// <returns><see langword="true"/> when the text appeared in time.</returns>
    private static bool WaitForText(AutomationElement parent, string automationId, string expected) =>
        Retry.WhileFalse(
            () => parent.FindAllDescendants(cf => cf.ByAutomationId(automationId))
                .Any(element => string.Equals(element.Name, expected, StringComparison.Ordinal)),
            TimeSpan.FromSeconds(20),
            TimeSpan.FromMilliseconds(200)).Result;

    /// <summary>
    ///     Selects the list item that contains a text element with the given AutomationId and text.
    /// </summary>
    /// <param name="window">The window containing the list.</param>
    /// <param name="textAutomationId">The AutomationId of the text element inside each item.</param>
    /// <param name="text">The text of the wanted item.</param>
    private static void SelectListItemByText(AutomationElement window, string textAutomationId, string text)
    {
        var item = Retry.WhileNull(
            () =>
            {
                var textElement = window.FindAllDescendants(cf => cf.ByAutomationId(textAutomationId))
                    .FirstOrDefault(element => string.Equals(element.Name, text, StringComparison.Ordinal));
                var current = textElement;
                while (current is not null && current.ControlType != ControlType.ListItem)
                {
                    current = current.Parent;
                }

                return current;
            },
            TimeSpan.FromSeconds(15),
            TimeSpan.FromMilliseconds(200)).Result;
        Assert.NotNull(item);
        item.AsListBoxItem().Select();
    }

    /// <summary>
    ///     Creates a new empty temporary folder.
    /// </summary>
    /// <returns>The folder's absolute path.</returns>
    private static string CreateTempFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "AgentControlUiTests-sources-" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    ///     Best-effort recursive delete of a temporary fixture directory.
    /// </summary>
    /// <param name="path">The directory to delete.</param>
    private static void TryDelete(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup only.
        }
    }
}
