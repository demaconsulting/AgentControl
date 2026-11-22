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
///     End-to-end tests of the responsive start-up in the real application: the window and its cards
///     appear at once, each card says it is still being read and shows its branch as "..." until git
///     has answered, and the global progress strip under the app bar counts the reads, without Cancel,
///     and goes away once every card has been read.
/// </summary>
/// <remarks>
///     The arg-logger stub stands in for git; its hold file (see
///     <see cref="AgentControlTestContext.GitHoldFile"/>) keeps every git command waiting, so what the
///     window shows while git has not answered can be checked without depending on timing, and is
///     then released by deleting the file.
/// </remarks>
public sealed class ResponsiveStartupTests
{
    /// <summary>
    ///     The repos seeded besides the context's own one, in the user's order after it.
    /// </summary>
    private static readonly string[] OtherRepos = ["second", "third"];

    /// <summary>
    ///     While git has not answered, the three cards are already shown, each with "refreshing..."
    ///     and a "..." branch, and the progress strip reads "Refreshing repositories 0 of 3..." with no
    ///     Cancel button; once git answers, the strip and the indicators go and each card shows its
    ///     branch.
    /// </summary>
    [Fact]
    public void MainWindow_StartupWhileGitHasNotAnswered_ShowsCardsRefreshingAndCountsThemInTheStrip()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: three repos, each with a .git/HEAD naming the branch "main" (read from the file, without git);
        // every git command the cards run waits while the hold file exists
        using var context = new AgentControlTestContext(customizeSettings: SeedThreeRepos);
        foreach (var repo in new[] { context.RepoPath }.Concat(OtherRepos.Select(name => Path.Combine(context.RootDirectory, name))))
        {
            var gitFolder = Path.Combine(repo, ".git");
            Directory.CreateDirectory(gitFolder);
            File.WriteAllText(Path.Combine(gitFolder, "HEAD"), "ref: refs/heads/main\n");
        }

        var holdFile = Path.Combine(context.RootDirectory, "git-hold");
        File.WriteAllText(holdFile, string.Empty);
        context.GitHoldFile = holdFile;
        try
        {
            // Act: start the application
            var mainWindow = context.Launch();
            mainWindow.SetForeground();

            // Assert: the cards are there while git has not answered
            Assert.True(Retry.WhileFalse(
                () => RepoNames(mainWindow).SequenceEqual(["repo", "second", "third"]),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromMilliseconds(200)).Result);

            // Assert: the strip counts the reads, none done yet, and offers no Cancel
            Assert.True(Retry.WhileFalse(
                () => CardRefreshWait.FindShown(mainWindow, "GlobalProgressText")?.Name == "Refreshing repositories 0 of 3...",
                TimeSpan.FromSeconds(10),
                TimeSpan.FromMilliseconds(200)).Result);
            Assert.NotNull(CardRefreshWait.FindShown(mainWindow, "GlobalProgressStrip"));
            Assert.NotNull(CardRefreshWait.FindShown(mainWindow, "GlobalProgressBar"));
            Assert.Null(CardRefreshWait.FindShown(mainWindow, "GlobalProgressCancelButton"));

            // Assert: every card says it is being read, and shows its branch as "..." until it is known
            Assert.True(Retry.WhileFalse(
                () => CardRefreshWait.FindAllShown(mainWindow, "RepoRefreshingText").Count == 3,
                TimeSpan.FromSeconds(10),
                TimeSpan.FromMilliseconds(200)).Result);
            var branches = CardRefreshWait.FindAllShown(mainWindow, "CurrentBranchText");
            Assert.Equal(3, branches.Count);
            Assert.All(branches, branch => Assert.Equal("...", branch.Name));

            // Act: let git answer
            File.Delete(holdFile);

            // Assert: the strip and the indicators go, and every card shows its branch
            Assert.True(CardRefreshWait.WaitUntilCardsAreRead(mainWindow, TimeSpan.FromSeconds(60)));
            Assert.Null(CardRefreshWait.FindShown(mainWindow, "GlobalProgressBar"));
            Assert.True(Retry.WhileFalse(
                () => CardRefreshWait.FindAllShown(mainWindow, "CurrentBranchText").Select(branch => branch.Name)
                    .SequenceEqual(["main", "main", "main"]),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromMilliseconds(200)).Result);
        }
        finally
        {
            // Release any git still waiting before the application is closed
            File.Delete(holdFile);
        }
    }

    /// <summary>
    ///     Seeds the settings with three repos in the user's own order (the test repo, "second" and
    ///     "third").
    /// </summary>
    /// <param name="settings">The settings dictionary to edit.</param>
    /// <param name="context">The test context, whose repo is the first one.</param>
    private static void SeedThreeRepos(Dictionary<string, object?> settings, AgentControlTestContext context)
    {
        foreach (var name in OtherRepos)
        {
            Directory.CreateDirectory(Path.Combine(context.RootDirectory, name));
        }

        settings["HasUserRepoOrder"] = true;
        settings["RecentRepos"] = new object[]
        {
            new { Path = context.RepoPath },
            new { Path = Path.Combine(context.RootDirectory, OtherRepos[0]) },
            new { Path = Path.Combine(context.RootDirectory, OtherRepos[1]) }
        };
    }

    /// <summary>
    ///     Lists the names of the repo cards currently shown, top to bottom.
    /// </summary>
    /// <param name="mainWindow">The main window.</param>
    /// <returns>The text of each <c>RepoNameText</c>.</returns>
    private static List<string> RepoNames(AutomationElement mainWindow) =>
        [.. mainWindow.FindAllDescendants(cf => cf.ByAutomationId("RepoNameText"))
            .OrderBy(element => element.BoundingRectangle.Top).Select(element => element.Name)];
}
