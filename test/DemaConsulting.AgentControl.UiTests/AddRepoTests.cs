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

using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     End-to-end tests of adding repos in the real application through the app bar's Add button and
///     the operating system's own folder picker: several folders chosen at once are added with one
///     message about the ones that were not, and only the top folder of a Git repository is added.
/// </summary>
/// <remarks>
///     The folder picker is the Windows Common Item Dialog, which the application shows (owned by its
///     main window) when Add is invoked; <see cref="NativeFolderPicker"/> clicks Add once the main
///     window is ready and finds the picker as a native window owned by the main window. It is
///     driven through UI Automation, and real keyboard input is sent to it only after
///     <see cref="DialogInputProbe.BringToForeground"/> made it the foreground window. A picker left
///     open by a failed assertion is canceled by the teardown that closes every window the
///     application still has open.
/// </remarks>
public sealed class AddRepoTests
{
    /// <summary>
    ///     The folder picker's title; it must match the title <c>MainWindow.BrowseForRepoPathsAsync</c>
    ///     gives the picker.
    /// </summary>
    private const string FolderPickerTitle = "Select one or more repository folders";

    /// <summary>
    ///     Choosing two Git repositories and a plain folder at once in the folder picker adds the two
    ///     repositories, shows one message that says how many were added and names the plain folder as
    ///     not the top folder of a Git repository, and writes nothing into the plain folder.
    /// </summary>
    [Fact]
    public void AddRepoButton_FolderPickerWithTwoReposAndAPlainFolder_AddsTheReposAndSummarizesTheRest()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: a folder holding two Git repositories and a plain folder (no .git)
        using var context = new AgentControlTestContext();
        var picked = Path.Combine(context.RootDirectory, "picked");
        CreateGitRepo(Path.Combine(picked, "alpha-repo"));
        CreateGitRepo(Path.Combine(picked, "beta-repo"));
        var plain = Path.Combine(picked, "plain-folder");
        Directory.CreateDirectory(plain);

        var mainWindow = context.Launch();
        mainWindow.SetForeground();

        // The list shows only the rows that fit, so give it the whole screen now, while no dialog is
        // open (a window that owns an open dialog cannot change its state)
        mainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Maximized);
        Assert.Equal(["repo"], RepoNames(mainWindow));

        // Act: Add opens the folder picker; open the folder and select all three, as a user does
        var picker = NativeFolderPicker.Open(context, mainWindow, Find(mainWindow, "AddRepoButton"), FolderPickerTitle);
        NavigatePicker(context, picker, picked);
        FindListItem(picker, "alpha-repo").Patterns.SelectionItem.Pattern.Select();
        FindListItem(picker, "beta-repo").Patterns.SelectionItem.Pattern.AddToSelection();
        FindListItem(picker, "plain-folder").Patterns.SelectionItem.Pattern.AddToSelection();

        // Fallback for a view that does not echo a selection made through UI Automation in its folder
        // box: the dialog's own format for several items of the open folder
        var folderBox = FindFolderBox(picker).AsTextBox();
        var echoed = Retry.WhileFalse(
            () =>
            {
                var text = folderBox.Text ?? string.Empty;
                return text.Contains("alpha-repo", StringComparison.Ordinal) &&
                       text.Contains("beta-repo", StringComparison.Ordinal) &&
                       text.Contains("plain-folder", StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(200)).Result;
        if (!echoed)
        {
            folderBox.Text = "\"alpha-repo\" \"beta-repo\" \"plain-folder\"";
        }

        FindSelectFolderButton(picker).AsButton().Invoke();

        // Assert: one message with the count and the folder that was not added, and why
        var messageBox = context.WaitForDialog("MessageText", TimeSpan.FromSeconds(15));
        Assert.NotNull(messageBox);
        var message = Find(messageBox, "MessageText").Name;
        Assert.StartsWith("Added 2 of 3 folders.", message, StringComparison.Ordinal);
        Assert.Contains(
            "Not the top folder of a Git repository (no .git folder or file): plain-folder.",
            message,
            StringComparison.Ordinal);

        // Act: dismiss the message
        Find(messageBox, "MessageBoxOkButton").AsButton().Invoke();

        // Assert: the message box is gone and the main window can be used again
        Assert.True(Retry.WhileFalse(
            () => context.WaitForDialog("MessageText", TimeSpan.FromMilliseconds(200)) is null,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.True(DialogInputProbe.WaitForEnabled(mainWindow, expected: true));

        // Assert: both repositories are listed and stored, the plain folder is neither, and it is untouched
        Assert.True(Retry.WhileFalse(
            () => RepoNames(mainWindow).Order(StringComparer.Ordinal).SequenceEqual(["alpha-repo", "beta-repo", "repo"]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        var stored = StoredRepoNames(context);
        Assert.Contains("alpha-repo", stored);
        Assert.Contains("beta-repo", stored);
        Assert.DoesNotContain("plain-folder", stored);
        Assert.Empty(Directory.EnumerateFileSystemEntries(plain));
    }

    /// <summary>
    ///     Choosing several Git repositories at once in the folder picker adds them all - checked in the
    ///     background with the progress strip under the app bar while they are - without a message, and
    ///     the progress strip is gone once they are listed.
    /// </summary>
    [Fact]
    public void AddRepoButton_FolderPickerWithSeveralRepos_AddsThemAndRemovesTheProgressStrip()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: a folder holding three Git repositories
        using var context = new AgentControlTestContext();
        var picked = Path.Combine(context.RootDirectory, "picked");
        CreateGitRepo(Path.Combine(picked, "alpha-repo"));
        CreateGitRepo(Path.Combine(picked, "beta-repo"));
        CreateGitRepo(Path.Combine(picked, "gamma-repo"));

        var mainWindow = context.Launch();
        mainWindow.SetForeground();
        mainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Maximized);
        Assert.Equal(["repo"], RepoNames(mainWindow));

        // Act: Add opens the folder picker; select the three repositories, as a user does
        var picker = NativeFolderPicker.Open(context, mainWindow, Find(mainWindow, "AddRepoButton"), FolderPickerTitle);
        NavigatePicker(context, picker, picked);
        FindListItem(picker, "alpha-repo").Patterns.SelectionItem.Pattern.Select();
        FindListItem(picker, "beta-repo").Patterns.SelectionItem.Pattern.AddToSelection();
        FindListItem(picker, "gamma-repo").Patterns.SelectionItem.Pattern.AddToSelection();
        var folderBox = FindFolderBox(picker).AsTextBox();
        var echoed = Retry.WhileFalse(
            () =>
            {
                var text = folderBox.Text ?? string.Empty;
                return text.Contains("alpha-repo", StringComparison.Ordinal) &&
                       text.Contains("beta-repo", StringComparison.Ordinal) &&
                       text.Contains("gamma-repo", StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(200)).Result;
        if (!echoed)
        {
            folderBox.Text = "\"alpha-repo\" \"beta-repo\" \"gamma-repo\"";
        }

        FindSelectFolderButton(picker).AsButton().Invoke();

        // Assert: all three are listed, the progress strip has gone, no message was needed, and the
        // main window can be used
        Assert.True(Retry.WhileFalse(
            () => RepoNames(mainWindow).Order(StringComparer.Ordinal)
                .SequenceEqual(["alpha-repo", "beta-repo", "gamma-repo", "repo"]),
            TimeSpan.FromSeconds(15),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.True(Retry.WhileFalse(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("GlobalProgressBar")) is not { IsOffscreen: false },
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.Null(context.WaitForDialog("MessageText", TimeSpan.FromMilliseconds(500)));
        Assert.True(DialogInputProbe.WaitForEnabled(mainWindow, expected: true));
        var stored = StoredRepoNames(context);
        Assert.Contains("alpha-repo", stored);
        Assert.Contains("beta-repo", stored);
        Assert.Contains("gamma-repo", stored);
    }

    /// <summary>
    ///     A subfolder of a Git repository chosen in the folder picker is not added: a message says it
    ///     is not the top folder of a Git repository, and the repo already listed (whose folder has no
    ///     <c>.git</c>) is still listed and stored.
    /// </summary>
    [Fact]
    public void AddRepoButton_FolderPickerWithASubfolderOfARepo_RefusesItAndKeepsTheListedFolderWithoutGit()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: a Git repository with a subfolder; the seeded, listed repo has no .git
        using var context = new AgentControlTestContext();
        var project = Path.Combine(context.RootDirectory, "git-project");
        CreateGitRepo(project);
        var docs = Path.Combine(project, "docs");
        Directory.CreateDirectory(docs);
        var seededGit = Path.Combine(context.RepoPath, ".git");
        Assert.False(Directory.Exists(seededGit) || File.Exists(seededGit));

        var mainWindow = context.Launch();
        mainWindow.SetForeground();

        // Assert: the listed folder without .git is listed when the application starts
        Assert.Equal(["repo"], RepoNames(mainWindow));

        // Act: Add opens the folder picker; type the subfolder's path in its folder box and select it
        var picker = NativeFolderPicker.Open(context, mainWindow, Find(mainWindow, "AddRepoButton"), FolderPickerTitle);
        FindFolderBox(picker).AsTextBox().Text = docs;
        FindSelectFolderButton(picker).AsButton().Invoke();

        // Assert: refused with a message (the folder's name only: the picker returns the long path)
        var messageBox = context.WaitForDialog("MessageText", TimeSpan.FromSeconds(15));
        Assert.NotNull(messageBox);
        var message = Find(messageBox, "MessageText").Name;
        Assert.Contains(
            "could not be added: it is not the top folder of a Git repository (it has no .git folder or file).",
            message,
            StringComparison.Ordinal);
        Assert.Contains("docs'", message, StringComparison.Ordinal);

        // Act: dismiss the message
        Find(messageBox, "MessageBoxOkButton").AsButton().Invoke();

        // Assert: nothing added, the listed folder without .git is still listed and stored, docs untouched
        Assert.Equal(["repo"], RepoNames(mainWindow));
        var stored = StoredRepoNames(context);
        Assert.Contains("repo", stored);
        Assert.DoesNotContain("git-project", stored);
        Assert.DoesNotContain("docs", stored);
        Assert.Empty(Directory.EnumerateFileSystemEntries(docs));
    }

    /// <summary>
    ///     Creates a folder that is the top folder of a Git repository: it has a <c>.git</c> folder
    ///     with a <c>HEAD</c> file.
    /// </summary>
    /// <param name="path">The folder's path.</param>
    private static void CreateGitRepo(string path)
    {
        Directory.CreateDirectory(Path.Combine(path, ".git"));
        File.WriteAllText(Path.Combine(path, ".git", "HEAD"), "ref: refs/heads/main\n");
    }

    /// <summary>
    ///     Finds the folder picker's folder box, where the chosen folder's name (or path) is shown and
    ///     can be typed.
    /// </summary>
    /// <param name="picker">The folder picker.</param>
    /// <returns>The folder box.</returns>
    private static AutomationElement FindFolderBox(Window picker)
    {
        var box = Retry.WhileNull(
            () =>
            {
                var edits = picker.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit));
                return edits.FirstOrDefault(edit => edit.Properties.AutomationId.ValueOrDefault == "1152")
                       ?? edits.FirstOrDefault(edit => edit.Properties.AutomationId.ValueOrDefault == "1148")
                       ?? edits.FirstOrDefault(edit =>
                           (edit.Properties.Name.ValueOrDefault ?? string.Empty).StartsWith("Folder", StringComparison.Ordinal));
            },
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200),
            ignoreException: true).Result;
        Assert.True(
            box is not null,
            "The folder picker's folder box was not found (tried an Edit with AutomationId 1152, then 1148, " +
            "then an Edit whose name starts with \"Folder\").");
        return box;
    }

    /// <summary>
    ///     Finds the folder picker's Select Folder button by its control ID (1, the dialog's OK button),
    ///     which does not depend on the user interface language.
    /// </summary>
    /// <param name="picker">The folder picker.</param>
    /// <returns>The button.</returns>
    private static AutomationElement FindSelectFolderButton(Window picker)
    {
        var button = Retry.WhileNull(
            () => picker.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByAutomationId("1"))),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200),
            ignoreException: true).Result;
        Assert.True(button is not null, "The folder picker's Select Folder button (AutomationId 1) was not found.");
        return button;
    }

    /// <summary>
    ///     Opens a folder in the folder picker as a user does: Alt+D moves to the address bar (the
    ///     standard shortcut of the file dialogs and the file manager), the path is typed and Enter
    ///     opens it.
    /// </summary>
    /// <param name="context">The running test context.</param>
    /// <param name="picker">The folder picker.</param>
    /// <param name="folder">The folder to open.</param>
    private static void NavigatePicker(AgentControlTestContext context, Window picker, string folder)
    {
        Assert.True(DialogInputProbe.BringToForeground(picker), "The folder picker could not be brought to the foreground.");
        Keyboard.TypeSimultaneously(VirtualKeyShort.ALT, VirtualKeyShort.KEY_D);
        var folderBox = FindFolderBox(picker);
        var addressBar = Retry.WhileNull(
            () =>
            {
                var focused = context.Automation!.FocusedElement();
                return focused is not null &&
                       focused.Properties.ControlType.ValueOrDefault == ControlType.Edit &&
                       !Equals(focused, folderBox)
                    ? focused
                    : null;
            },
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(200),
            ignoreException: true).Result;
        Assert.True(addressBar is not null, "Alt+D did not move the keyboard focus to the folder picker's address bar.");

        DialogInputProbe.TypeText(folder);
        DialogInputProbe.Press(VirtualKeyShort.RETURN);
    }

    /// <summary>
    ///     Finds an item of the folder picker's items view (not of its navigation pane, whose entries
    ///     are tree items), waiting for the opened folder's items to be listed.
    /// </summary>
    /// <param name="picker">The folder picker.</param>
    /// <param name="name">The item's name.</param>
    /// <returns>The item.</returns>
    private static AutomationElement FindListItem(Window picker, string name)
    {
        var item = Retry.WhileNull(
            () => picker.FindFirstDescendant(cf => cf.ByControlType(ControlType.ListItem).And(cf.ByName(name))),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200),
            ignoreException: true).Result;
        Assert.True(item is not null, $"The folder picker does not list \"{name}\".");
        return item;
    }

    /// <summary>
    ///     Lists the names of the repo cards currently shown, in order.
    /// </summary>
    /// <param name="mainWindow">The main window.</param>
    /// <returns>The text of each <c>RepoNameText</c>.</returns>
    private static List<string> RepoNames(Window mainWindow) =>
        [.. mainWindow.FindAllDescendants(cf => cf.ByAutomationId("RepoNameText"))
            .OrderBy(element => element.BoundingRectangle.Top).Select(element => element.Name)];

    /// <summary>
    ///     Reads the persisted settings file, or an empty string when it does not exist yet.
    /// </summary>
    /// <param name="context">The running test context.</param>
    /// <returns>The file's text.</returns>
    private static string ReadSettings(AgentControlTestContext context)
    {
        var path = Path.Combine(context.ConfigDirectory, "settings.json");
        return File.Exists(path) ? SharedFile.ReadAllText(path) : string.Empty;
    }

    /// <summary>
    ///     Lists the folder names of the repos stored in the settings file.
    /// </summary>
    /// <param name="context">The running test context.</param>
    /// <returns>The last path segment of each stored repo's <c>Path</c>.</returns>
    /// <remarks>
    ///     Read without polling: an add is applied (and its cards shown) only once its save was written, so the
    ///     file holds it by the time the tests see the cards.
    /// </remarks>
    private static List<string> StoredRepoNames(AgentControlTestContext context)
    {
        using var document = JsonDocument.Parse(ReadSettings(context));
        return document.RootElement.TryGetProperty("RecentRepos", out var repos) && repos.ValueKind == JsonValueKind.Array
            ? [.. repos.EnumerateArray()
                .Select(repo => repo.TryGetProperty("Path", out var path) ? path.GetString() ?? string.Empty : string.Empty)
                .Select(path => Path.GetFileName(Path.TrimEndingDirectorySeparator(path)))]
            : [];
    }

    /// <summary>
    ///     Finds an element by AutomationId (optionally one that also passes a filter, and optionally the
    ///     n-th such element, as repeated card controls exist once per card), waiting for it to appear.
    /// </summary>
    /// <param name="parent">The window or element to search under.</param>
    /// <param name="automationId">The AutomationId to find.</param>
    /// <param name="filter">An extra condition the element must meet, or <see langword="null"/>.</param>
    /// <param name="index">Which of the matching elements to return, counting from zero.</param>
    /// <returns>The element.</returns>
    private static AutomationElement Find(
        AutomationElement parent, string automationId, Func<AutomationElement, bool>? filter = null, int index = 0)
    {
        var element = Retry.WhileNull(
            () => parent.FindAllDescendants(cf => cf.ByAutomationId(automationId))
                .Where(candidate => filter is null || filter(candidate))
                .ElementAtOrDefault(index),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result;
        Assert.NotNull(element);
        return element;
    }
}
