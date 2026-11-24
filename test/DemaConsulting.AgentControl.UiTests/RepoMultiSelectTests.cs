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
using FlaUI.Core.WindowsAPI;

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     End-to-end tests of selecting several repo cards in the real application: the list reports that it
///     allows several selected items, the selection is made through UI Automation (as a screen reader or a
///     test tool does) and the real Delete key removes the selected cards after one confirmation, leaving
///     the repos' folders on disk.
/// </summary>
public sealed class RepoMultiSelectTests
{
    /// <summary>
    ///     Selecting two of three repo cards through UI Automation marks exactly those two as selected,
    ///     pressing Delete asks one question that names the count, answering No changes nothing, and answering
    ///     Yes removes the two selected cards from the list and from the settings file while their folders
    ///     stay on disk.
    /// </summary>
    [Fact]
    public void RepoList_SelectSeveralCardsAndPressDelete_ConfirmsOnceAndRemovesOnlyTheSelectedRepos()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: three repos, none in a group
        using var context = new AgentControlTestContext(customizeSettings: SeedThreeRepos);
        var mainWindow = context.Launch();
        mainWindow.SetForeground();
        var list = Find(mainWindow, "RepoListBox");

        // Assert: the list allows several selected items
        Assert.True(list.Patterns.Selection.Pattern.CanSelectMultiple.Value);
        Assert.True(Retry.WhileFalse(
            () => RepoNames(mainWindow).SequenceEqual(["Alpha", "Bravo", "Charlie"]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);

        // Act: select Alpha, then add Charlie to the selection
        Item(list, "Alpha").Patterns.SelectionItem.Pattern.Select();
        Item(list, "Charlie").Patterns.SelectionItem.Pattern.AddToSelection();

        // Assert: exactly those two are selected
        Assert.True(Item(list, "Alpha").Patterns.SelectionItem.Pattern.IsSelected.Value);
        Assert.False(Item(list, "Bravo").Patterns.SelectionItem.Pattern.IsSelected.Value);
        Assert.True(Item(list, "Charlie").Patterns.SelectionItem.Pattern.IsSelected.Value);

        // Act: Delete, answered No
        Assert.True(DialogInputProbe.BringToForeground(mainWindow));
        DialogInputProbe.Press(VirtualKeyShort.DELETE);
        var question = context.WaitForWindowWithDescendant("ConfirmationYesButton", TimeSpan.FromSeconds(15));
        Assert.NotNull(question);
        Assert.Contains("2 repos", Find(question, "ConfirmationMessageText").Name, StringComparison.Ordinal);
        Find(question, "ConfirmationNoButton").AsButton().Invoke();

        // Assert: nothing changed
        Assert.Equal(3, RepoNames(mainWindow).Count);

        // Act: Delete, answered Yes
        Assert.True(DialogInputProbe.BringToForeground(mainWindow));
        DialogInputProbe.Press(VirtualKeyShort.DELETE);
        question = context.WaitForWindowWithDescendant("ConfirmationYesButton", TimeSpan.FromSeconds(15));
        Assert.NotNull(question);
        Find(question, "ConfirmationYesButton").AsButton().Invoke();

        // Assert: only Bravo is left in the list and in the settings, and every folder is still on disk
        Assert.True(Retry.WhileFalse(
            () => RepoNames(mainWindow).SequenceEqual(["Bravo"]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.True(Retry.WhileFalse(
            () => !ReadSettings(context).Contains("Alpha", StringComparison.Ordinal),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.True(Directory.Exists(Path.Combine(context.RootDirectory, "Alpha")));
        Assert.True(Directory.Exists(Path.Combine(context.RootDirectory, "Charlie")));
    }

    /// <summary>
    ///     Seeds the settings with three repos in no group.
    /// </summary>
    /// <param name="settings">The settings dictionary to edit.</param>
    /// <param name="context">The test context.</param>
    private static void SeedThreeRepos(Dictionary<string, object?> settings, AgentControlTestContext context)
    {
        var paths = new[] { "Alpha", "Bravo", "Charlie" }
            .Select(name => Path.Combine(context.RootDirectory, name))
            .ToList();
        foreach (var path in paths)
        {
            Directory.CreateDirectory(path);
        }

        settings["HasUserRepoOrder"] = true;
        settings["RecentRepos"] = paths.Select(path => (object)new { Path = path }).ToArray();
    }

    /// <summary>
    ///     Finds the list item of a repo card, by the repo's name.
    /// </summary>
    /// <param name="list">The repo list.</param>
    /// <param name="name">The repo's folder name.</param>
    /// <returns>The list item.</returns>
    private static AutomationElement Item(AutomationElement list, string name)
    {
        var item = Retry.WhileNull(
            () => list.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
                .FirstOrDefault(candidate => candidate.FindFirstDescendant(cf => cf.ByAutomationId("RepoNameText"))?.Name == name),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result;
        Assert.NotNull(item);
        return item;
    }

    /// <summary>
    ///     Lists the names of the repo cards currently shown, from the top of the list down.
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
    ///     Finds an element by AutomationId, waiting for it to appear.
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
}
