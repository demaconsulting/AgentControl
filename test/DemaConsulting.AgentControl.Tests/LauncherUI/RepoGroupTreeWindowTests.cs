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

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.Settings;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Headless tests of the main window's tree of nested groups as the user meets it: how the groups
///     and their repos are listed.
/// </summary>
/// <remarks>
///     Runs in the <c>RealProcess</c> collection because showing cards spawns real git subprocesses
///     as part of each card's background repo-state read.
/// </remarks>
[Collection("RealProcess")]
public sealed class RepoGroupTreeWindowTests : IDisposable
{
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     Test that nested groups are listed as a tree - each group's header followed by its subgroups
    ///     and then by its own repos, every header saying which group it is inside - and that collapsing a
    ///     group hides its whole subtree.
    /// </summary>
    [Fact]
    public void MainWindow_NestedGroups_ListSubgroupsBeforeOwnReposAndCollapseHidesSubtree() => HeadlessTestApp.Run(() =>
    {
        // Arrange: Client holds c1 and the subgroups Alpha (a1) and Project (p1)
        var (window, viewModel) = CreateWindow(
            new Dictionary<string, string> { ["Client"] = string.Empty, ["Project"] = "Client", ["Alpha"] = "Client" },
            ("c1", "Client"), ("p1", "Project"), ("a1", "Alpha"), ("loose", null));

        try
        {
            // Act
            var headers = window.GetVisualDescendantsOf<Button>("GroupHeaderToggleButton").ToList();

            // Assert: the tree, depth first, with the group's own repos after its subgroups
            Assert.Equal(
                [
                    "Client, 3 repos, expanded",
                    "Alpha, 1 repo, expanded, inside Client",
                    "Project, 1 repo, expanded, inside Client",
                    "Ungrouped, 1 repo, expanded"
                ],
                headers.Select(AutomationProperties.GetName));
            Assert.Equal(
                ["a1", "p1", "c1", "loose"],
                window.GetVisualDescendantsOf<TextBlock>("RepoNameText").Select(text => text.Text));

            // Act: collapse the top group
            headers[0].Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            // Assert: its subgroups and their repos are hidden with it
            Assert.Equal(["loose"], window.GetVisualDescendantsOf<TextBlock>("RepoNameText").Select(text => text.Text));
            Assert.Equal(
                ["Client, 3 repos, collapsed", "Ungrouped, 1 repo, expanded"],
                window.GetVisualDescendantsOf<Button>("GroupHeaderToggleButton").Select(AutomationProperties.GetName));
            Assert.True(viewModel.IsGroupCollapsed("Client"));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Deletes the temporary folders the tests created.
    /// </summary>
    public void Dispose()
    {
        foreach (var path in _tempPaths.Where(Directory.Exists))
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A card's background git check may still have the folder as its working directory
                // when the window closes; leaving a temporary folder behind is harmless
            }
        }
    }

    /// <summary>
    ///     Creates a shown main window over repos with the given names and groups, and a group hierarchy.
    /// </summary>
    /// <param name="parents">The stored hierarchy.</param>
    /// <param name="repos">The repos as (folder name, group or <see langword="null"/>).</param>
    /// <returns>The window and its view model.</returns>
    private (MainWindow Window, MainWindowViewModel ViewModel) CreateWindow(
        Dictionary<string, string> parents, params (string Name, string? Group)[] repos)
    {
        var settings = new AppSettings
        {
            HasUserRepoOrder = true,
            RecentRepos =
            [
                .. repos.Select(repo => new RecentRepo { Path = CreateRepoDirectory(repo.Name), GroupName = repo.Group })
            ],
            GroupParents = new Dictionary<string, string>(parents, RepoGroupName.Comparer)
        };
        var viewModel = new MainWindowViewModel(settings, configDirectory: CreateTempDirectory());
        var window = new MainWindow { DataContext = viewModel, Height = 1600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // The cards' repo state is read in the background; the tests act on settled cards
        HeadlessTestApp.RunJobsUntilRepoStateSettled(viewModel);
        return (window, viewModel);
    }

    /// <summary>
    ///     Creates an empty repo folder with the given name under a unique parent.
    /// </summary>
    /// <param name="name">The folder name.</param>
    /// <returns>The folder's path.</returns>
    private string CreateRepoDirectory(string name)
    {
        var path = Path.Combine(CreateTempDirectory(), name);
        Directory.CreateDirectory(path);
        Directory.CreateDirectory(Path.Combine(path, ".git"));
        File.WriteAllText(Path.Combine(path, ".git", "HEAD"), "ref: refs/heads/main\n");
        return path;
    }

    /// <summary>
    ///     Creates a unique temporary directory that is removed on disposal.
    /// </summary>
    /// <returns>The directory's path.</returns>
    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "AgentControlGroupTreeWindow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        return path;
    }
}
