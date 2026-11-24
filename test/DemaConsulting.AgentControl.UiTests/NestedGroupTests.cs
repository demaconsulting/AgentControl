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
using FlaUI.Core.Tools;

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     End-to-end tests of nested groups in the real application: creating a group inside another from
///     the header's menu, seeing it listed inside its parent, and keeping the tree after a restart. The
///     hierarchy is checked to be stored in AgentControl's settings only, never in the repository.
/// </summary>
public sealed class NestedGroupTests
{
    /// <summary>
    ///     Choosing "New subgroup..." in a group header's menu, typing a name and confirming lists the
    ///     new group, with no repos, inside its parent (its header says so), stores the hierarchy in the
    ///     settings file only, and shows the same tree after a restart.
    /// </summary>
    [Fact]
    public void GroupHeaderMenu_NewSubgroup_ListsGroupInsideParentStoresHierarchyInSettingsOnlyAndKeepsItAfterRestart()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: the repo is in "Work", another repo is in no group
        using var context = new AgentControlTestContext(customizeSettings: SeedWorkGroupAndUngroupedRepo);
        var mainWindow = context.Launch();
        mainWindow.SetForeground();

        // Act: ... on the Work header > New subgroup...
        Find(mainWindow, "GroupHeaderMenuButton").AsButton().Invoke();
        Find(mainWindow, "GroupNewSubgroupMenuItem").AsMenuItem().Invoke();

        // Act: type a name in the prompt and confirm
        var prompt = context.WaitForWindowWithDescendant("GroupNameTextBox", TimeSpan.FromSeconds(15));
        Assert.NotNull(prompt);
        Find(prompt, "GroupNameTextBox").AsTextBox().Text = "Phase 1";
        Find(prompt, "GroupNameAcceptButton").AsButton().Invoke();

        // Assert: the new group is listed inside Work, with no repos
        Assert.True(Retry.WhileFalse(
            () => HeaderNames(mainWindow).SequenceEqual(
                ["Work, 1 repo, expanded", "Phase 1, 0 repos, expanded, inside Work", "Ungrouped, 1 repo, expanded"]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);

        // Assert: the hierarchy is stored in the settings, and nothing was written into the repository
        Assert.True(Retry.WhileFalse(
            () => ReadSettings(context).Contains("\"Phase 1\": \"Work\"", StringComparison.Ordinal),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.Empty(Directory.EnumerateFileSystemEntries(context.RepoPath));

        // Act: restart the application against the same settings
        context.CloseApplication();
        var restarted = context.Launch();
        restarted.SetForeground();

        // Assert: the same tree
        Assert.True(Retry.WhileFalse(
            () => HeaderNames(restarted).SequenceEqual(
                ["Work, 1 repo, expanded", "Phase 1, 0 repos, expanded, inside Work", "Ungrouped, 1 repo, expanded"]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
    }

    /// <summary>
    ///     Seeds the settings with the repo in the group "Work" and a second repo in no group.
    /// </summary>
    /// <param name="settings">The settings dictionary to edit.</param>
    /// <param name="context">The test context, whose repo is the first one.</param>
    private static void SeedWorkGroupAndUngroupedRepo(
        Dictionary<string, object?> settings, AgentControlTestContext context)
    {
        var other = Path.Combine(context.RootDirectory, "elsewhere");
        Directory.CreateDirectory(other);
        settings["RecentRepos"] = new object[]
        {
            new { Path = context.RepoPath, GroupName = "Work" },
            new { Path = other }
        };
    }

    /// <summary>
    ///     Lists the names of the section headers currently shown, in order; each names the section, its
    ///     repo count, whether it is expanded and, for a nested group, the group it is inside.
    /// </summary>
    /// <param name="mainWindow">The main window.</param>
    /// <returns>The name of each <c>GroupHeaderToggleButton</c>.</returns>
    private static List<string> HeaderNames(Window mainWindow) =>
        [.. mainWindow.FindAllDescendants(cf => cf.ByAutomationId("GroupHeaderToggleButton"))
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
