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

using System.IO.Compression;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.PackageCredentials;
using DemaConsulting.AgentControl.RepoConfig;
using DemaConsulting.AgentControl.Settings;
using Material.Icons.Avalonia;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Headless tests of the usability fixes as the user meets them in the main window and the dialogs:
///     the favorite star (real pointer and keyboard input), Refresh for a missing folder, the up-to-date
///     cue, the card's name and path at a narrow width, the Settings sections, the keyboard behavior
///     every dialog shares (initial focus, Tab staying inside, Escape), the About version row and the
///     order of the AGENTS.md question and the release notes after an upgrade.
/// </summary>
/// <remarks>
///     Runs in the <c>RealProcess</c> collection because showing cards spawns real git subprocesses as
///     part of each card's eager refresh.
/// </remarks>
[Collection("RealProcess")]
public sealed class UsabilityWindowTests : IDisposable
{
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     Test that a real mouse click on an unfavorited repo's star favorites it (the star is on, the
    ///     repo moves under Favorites and the choice is stored), and that a second real click
    ///     unfavorites it again (back in its own section). A command that also toggled used to undo each
    ///     click.
    /// </summary>
    [Fact]
    public void MainWindow_FavoriteStar_MouseClick_FavoritesThenUnfavoritesMovingBetweenSections() => HeadlessTestApp.Run(() =>
    {
        // Arrange: a group and two ungrouped repos
        var (window, viewModel) = CreateWindow([new RecentRepo { GroupName = "Work" }, new RecentRepo(), new RecentRepo()]);

        try
        {
            var repoName = viewModel.RepoCards[1].RepoName;
            Assert.False(FindStar(window, repoName).IsChecked);

            // The star's checked state (two-way bound) is the only owner of the favorite: a command that
            // also toggled would flip it a second time on every click
            Assert.Null(FindStar(window, repoName).Command);

            // Act: click the star
            ClickWithMouse(window, FindStar(window, repoName));

            // Assert: favorite, listed under Favorites, stored
            Assert.True(viewModel.RepoCards[1].IsFavorite);
            Assert.True(FindStar(window, repoName).IsChecked);
            Assert.Equal(
                ["Favorites, 1 repo, expanded", "Work, 1 repo, expanded", "Ungrouped, 1 repo, expanded"],
                HeaderNames(window));
            Assert.Equal(repoName, SectionOf(window, "Favorites").Single());
            Assert.True(viewModel.Settings.RecentRepos[1].IsFavorite);
            Assert.Same(FindStar(window, repoName), window.FocusManager.GetFocusedElement());

            // Act: click the star again
            ClickWithMouse(window, FindStar(window, repoName));

            // Assert: not a favorite any more, back under Ungrouped, stored
            Assert.False(viewModel.RepoCards[1].IsFavorite);
            Assert.False(FindStar(window, repoName).IsChecked);
            Assert.Equal(
                ["Work, 1 repo, expanded", "Ungrouped, 2 repos, expanded"],
                HeaderNames(window));
            Assert.Contains(repoName, SectionOf(window, "Ungrouped"));
            Assert.False(viewModel.Settings.RecentRepos[1].IsFavorite);
            Assert.Same(FindStar(window, repoName), window.FocusManager.GetFocusedElement());
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that the star also toggles exactly once from the keyboard (Space), off to on and on to off,
    ///     that the keyboard focus stays on that repo's star after each press even though the card moves
    ///     between sections (and its controls are re-created), and that its accessible name says what it is.
    /// </summary>
    [Fact]
    public void MainWindow_FavoriteStar_SpaceKey_TogglesOncePerPress() => HeadlessTestApp.Run(() =>
    {
        // Arrange: a group, so favoriting moves the card from Ungrouped into a new Favorites section
        var (window, viewModel) = CreateWindow([new RecentRepo { GroupName = "Work" }, new RecentRepo(), new RecentRepo()]);

        try
        {
            var card = viewModel.RepoCards[1];
            var repoName = card.RepoName;
            var changes = 0;
            card.FavoriteChanged += (_, _) => changes++;
            Assert.Equal("Favorite", AutomationProperties.GetName(FindStar(window, repoName)));

            // Act: Space on the focused star
            FindStar(window, repoName).Focus(NavigationMethod.Tab);
            PressKey(window, PhysicalKey.Space);

            // Assert: favorite once, the card moved to Favorites, and the focus is on its (new) star
            Assert.True(card.IsFavorite);
            Assert.Equal(1, changes);
            Assert.Equal(repoName, SectionOf(window, "Favorites").Single());
            Assert.Same(FindStar(window, repoName), window.FocusManager.GetFocusedElement());

            // Act: Space again, without touching the focus
            PressKey(window, PhysicalKey.Space);

            // Assert: unfavorite once, back in its own section, the focus still on its star
            Assert.False(card.IsFavorite);
            Assert.Equal(2, changes);
            Assert.Contains(repoName, SectionOf(window, "Ungrouped"));
            Assert.Same(FindStar(window, repoName), window.FocusManager.GetFocusedElement());
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that a repo whose folder is missing still has an enabled menu item to re-check, labeled
    ///     "Check again" with a tooltip that says what it does, and that a repo that is there says "Refresh".
    /// </summary>
    [Fact]
    public void MainWindow_MissingRepoMenu_OffersEnabledCheckAgain_PresentRepoOffersRefresh() => HeadlessTestApp.Run(() =>
    {
        // Arrange: one repo present, one whose folder does not exist
        var missingPath = Path.Combine(CreateTempDirectory(), "gone");
        var (window, viewModel) = CreateWindow([new RecentRepo(), new RecentRepo { Path = missingPath }]);

        try
        {
            var present = viewModel.RepoCards.Single(card => !card.IsMissing);
            var missing = viewModel.RepoCards.Single(card => card.IsMissing);

            // Act / Assert: the present repo's menu
            var menu = OpenCardMenu(window, present.RepoName);
            var refresh = FindMenuItem(menu, "RefreshMenuItem");
            Assert.Equal("Refresh", refresh.Header);
            Assert.True(refresh.IsEffectivelyEnabled);
            menu.Hide();

            // Act / Assert: the missing repo's menu offers Check again, enabled, explained
            menu = OpenCardMenu(window, missing.RepoName);
            refresh = FindMenuItem(menu, "RefreshMenuItem");
            Assert.Equal("Check again", refresh.Header);
            Assert.True(refresh.IsEffectivelyEnabled);
            Assert.Equal("Check whether the folder is available again", ToolTip.GetTip(refresh));
            Assert.False(FindMenuItem(menu, "PullButton").IsEffectivelyEnabled);
            menu.Hide();
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that the "up to date" check mark is drawn inside the agent package chip - with its own
    ///     accessible name and tooltip - only on a card whose pinned package is the newest one, and not on
    ///     a card with an upgrade available or without a package.
    /// </summary>
    [Fact]
    public void MainWindow_UpToDateCue_ShownOnlyForPinnedAndUpToDateCard() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: a source with 1.0.0 and 2.0.0; one repo on 2.0.0, one on 1.0.0, one without a pin
        var sourceDir = CreateTempDirectory();
        CreateZip(sourceDir, "contoso-agents", "1.0.0");
        CreateZip(sourceDir, "contoso-agents", "2.0.0");
        var upToDate = CreatePinnedRepo("a-current", "2.0.0");
        var older = CreatePinnedRepo("b-older", "1.0.0");
        var unpinned = CreateRepoDirectory("c-none");
        var (window, viewModel) = CreateWindow(
            [
                new RecentRepo { Path = upToDate, PinnedPackageName = "contoso-agents", PinnedPackageVersion = "2.0.0" },
                new RecentRepo { Path = older, PinnedPackageName = "contoso-agents", PinnedPackageVersion = "1.0.0" },
                new RecentRepo { Path = unpinned }
            ],
            new AppSettings { PackageSourcePath = sourceDir });

        try
        {
            foreach (var card in viewModel.RepoCards)
            {
                await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);
            }

            Dispatcher.UIThread.RunJobs();

            // Act
            var cues = window.GetVisualDescendantsOf<MaterialIcon>("UpToDateCue").ToList();

            // Assert: one cue per card in the tree, visible only on the up-to-date one
            Assert.Equal(3, cues.Count);
            var visible = cues.Where(cue => cue.IsVisible).ToList();
            var cue = Assert.Single(visible);
            Assert.Equal("a-current", Assert.IsType<RepoCardViewModel>(cue.DataContext).RepoName);
            Assert.Equal("Agent files are up to date", AutomationProperties.GetName(cue));
            Assert.Equal("Agent files are up to date", ToolTip.GetTip(cue));
            Assert.Equal(AccessibilityView.Content, AutomationProperties.GetAccessibilityView(cue));
            Assert.Contains(window.GetVisualDescendantsOf<Border>("PackageChip"), chip => chip.IsVisible && chip.GetVisualDescendants().Contains(cue));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that the card of a repo pinned to a Git source draws the Git icon and the source's alias
    ///     in its package chip, and that the chip's tooltip names the tag and the first seven
    ///     characters of the commit the files came from: the chip as the user sees it in the main window.
    /// </summary>
    [Fact]
    public void MainWindow_PinnedGitSourceCard_ChipShowsGitIconAliasTagAndShortCommit() => HeadlessTestApp.Run(() =>
    {
        // Arrange: a Git source (a local folder, so nothing is fetched) and a repo pinned to its tag
        var source = new PackageSourceDefinition
        {
            Alias = "Repo",
            Type = PackageSourceType.Git,
            Location = CreateTempDirectory(),
            TagPattern = "release/{version}",
            PackageIds = ["contoso-agents"]
        };
        var repo = CreateRepoDirectory("git-pinned");
        RepoPinStore.Save(repo, new RepoPin
        {
            Source = "Repo",
            PackageName = "contoso-agents",
            Version = "1.2.3",
            SourceRevision = "git-commit:abcdef0123456789abcdef0123456789abcdef01"
        });

        // Act: the main window shows the repo's card
        var (window, viewModel) = CreateWindow(
            [new RecentRepo { Path = repo }], new AppSettings { Sources = [source] });

        try
        {
            Dispatcher.UIThread.RunJobs();

            // Assert: the chip in the window carries the Git icon, the alias, the package and the tooltip
            var chip = Assert.Single(window.GetVisualDescendantsOf<Border>("PackageChip"));
            Assert.True(chip.IsVisible);
            var icon = Assert.Single(chip.GetVisualDescendants().OfType<MaterialIcon>(), icon => icon.Name == "PackageSourceIcon");
            Assert.Equal(Material.Icons.MaterialIconKind.Git, icon.Kind);
            Assert.Equal("Repo", Assert.Single(chip.GetVisualDescendants().OfType<TextBlock>(), text => text.Name == "PackageSourceAliasText").Text);
            Assert.Equal("contoso-agents 1.2.3", Assert.Single(chip.GetVisualDescendants().OfType<TextBlock>(), text => text.Name == "RepoPinnedVersionText").Text);
            Assert.Equal(
                "Agent files: contoso-agents 1.2.3 from source 'Repo' (git repository), tag release/1.2.3 @ abcdef0.",
                ToolTip.GetTip(chip));
            Assert.Equal(viewModel.RepoCards[0].PackageChipTooltip, ToolTip.GetTip(chip));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that the card of a repo whose pinned source alias is not configured on this machine, while
    ///     another configured source delivers the identical files, draws the alias of the source that supplies
    ///     them in its package chip (with a tooltip that says why) and no red "Package source problem" chip:
    ///     the card as a teammate with other source names sees it in the main window.
    /// </summary>
    [Fact]
    public void MainWindow_PinnedAliasNotConfiguredButIdenticalFilesElsewhere_ChipNamesSupplierAndShowsNoProblem() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: a pin to "Contoso" that recorded the identity of the files of 1.0.0, and a source
        // named "Artifactory" that delivers the very same files
        var contoso = CreateTempDirectory();
        CreateZip(contoso, "contoso-agents", "1.0.0", releaseNotes: "# Original");
        var artifactory = CreateTempDirectory();
        CreateZip(artifactory, "contoso-agents", "1.0.0", releaseNotes: "# Other release notes");
        string? identity;
        using (var reader = DemaConsulting.AgentControl.RepoSync.ZipPackageContentReader.Open(
                   Path.Combine(contoso, "contoso-agents-1.0.0.zip")))
        {
            identity = reader.ContentIdentity;
        }

        var repo = CreateRepoDirectory("portable-pin");
        RepoPinStore.Save(
            repo,
            new RepoPin { Source = "Contoso", PackageName = "contoso-agents", Version = "1.0.0", ContentIdentity = identity });
        var source = new PackageSourceDefinition { Alias = "Artifactory", Type = PackageSourceType.Zip, Location = artifactory };

        // Act: the main window shows the card and its upgrade check runs
        var (window, viewModel) = CreateWindow([new RecentRepo { Path = repo }], new AppSettings { Sources = [source] });

        try
        {
            await viewModel.RepoCards[0].RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();

            // Assert: the chip names the supplier and explains it; the red chip is not shown
            var chip = Assert.Single(window.GetVisualDescendantsOf<Border>("PackageChip"));
            Assert.Equal("Artifactory", Assert.Single(chip.GetVisualDescendants().OfType<TextBlock>(), text => text.Name == "PackageSourceAliasText").Text);
            Assert.Contains(
                "Pinned source 'Contoso' is not configured; the identical files come from 'Artifactory'.",
                ToolTip.GetTip(chip)?.ToString(),
                StringComparison.Ordinal);
            Assert.All(window.GetVisualDescendantsOf<Border>("SourceProblemChip"), problem => Assert.False(problem.IsVisible));
            Assert.Equal(RepoCardStatus.UpToDate, viewModel.RepoCards[0].Status);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that the card of a repo whose pinned source alias is not configured, while another configured
    ///     source delivers the identical files and also offers a newer version, shows the upgrade chip with
    ///     that newer version: the upgrade is offered from the configured sources that offer the package.
    /// </summary>
    [Fact]
    public void MainWindow_PinnedAliasNotConfigured_UpgradeChipOffersNewestVersionFromAnotherSource() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: a pin to "Contoso" 1.0.0 that recorded the identity of its files, and a source named
        // "Artifactory" with the same 1.0.0 files and a 2.0.0
        var contoso = CreateTempDirectory();
        CreateZip(contoso, "contoso-agents", "1.0.0");
        var artifactory = CreateTempDirectory();
        CreateZip(artifactory, "contoso-agents", "1.0.0");
        CreateZip(artifactory, "contoso-agents", "2.0.0", agentFileContent: "newer agent file");
        string? identity;
        using (var reader = DemaConsulting.AgentControl.RepoSync.ZipPackageContentReader.Open(
                   Path.Combine(contoso, "contoso-agents-1.0.0.zip")))
        {
            identity = reader.ContentIdentity;
        }

        var repo = CreateRepoDirectory("portable-pin-upgrade");
        RepoPinStore.Save(
            repo,
            new RepoPin { Source = "Contoso", PackageName = "contoso-agents", Version = "1.0.0", ContentIdentity = identity });
        var source = new PackageSourceDefinition { Alias = "Artifactory", Type = PackageSourceType.Zip, Location = artifactory };

        // Act
        var (window, viewModel) = CreateWindow([new RecentRepo { Path = repo }], new AppSettings { Sources = [source] });

        try
        {
            await viewModel.RepoCards[0].RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();

            // Assert: the upgrade chip offers 2.0.0, and there is no source problem
            var badge = Assert.Single(window.GetVisualDescendantsOf<Button>("UpgradeAvailableBadge"));
            Assert.True(badge.IsVisible);
            Assert.Equal("2.0.0", Assert.Single(window.GetVisualDescendantsOf<TextBlock>("UpgradeChipText")).Text);
            Assert.All(window.GetVisualDescendantsOf<Border>("SourceProblemChip"), problem => Assert.False(problem.IsVisible));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that a card's name ends in an ellipsis with a full-name tooltip, that its path keeps the
    ///     folder name readable (whole, or with a middle ellipsis that keeps its start and its end) and
    ///     gives way in the folder part first, with the full path as its tooltip and accessible name, and
    ///     that at the narrowest width the name never runs under the Launch button.
    /// </summary>
    [Fact]
    public void MainWindow_NarrowWidth_NameEllipsisPathKeepsFolderNameAndNothingCollides() => HeadlessTestApp.Run(() =>
    {
        // Arrange: a long repo name in a long path, in a window at the narrowest size
        var name = "05-very-long-repository-name-for-narrow-window-checks-alpha";
        var folder = Path.Combine(CreateTempDirectory(), "clients", "contoso", "platform-with-a-long-name", name);
        Directory.CreateDirectory(folder);
        var (window, viewModel) = CreateWindow([new RecentRepo { Path = folder }], width: 560);

        try
        {
            var nameText = window.GetVisualDescendantsOf<TextBlock>("RepoNameText").Single();
            var pathText = window.GetVisualDescendantsOf<PathEllipsisText>("RepoPathText").Single();
            var launch = window.GetVisualDescendantsOf<Button>("LaunchButton").Single();

            // Assert: the name trims at its end and its tooltip has all of it
            Assert.Equal(TextTrimming.CharacterEllipsis, nameText.TextTrimming);
            Assert.Equal(name, ToolTip.GetTip(nameText));

            // Assert: the whole path is the tooltip and the accessible name; what is drawn is shortened
            // (the folder part first, then the folder name in the middle) and is hidden from automation
            Assert.Equal(folder, ToolTip.GetTip(pathText));
            Assert.Equal(folder, AutomationProperties.GetName(pathText));
            Assert.Equal(viewModel.RepoCards[0].RepoPathParent, pathText.ParentText);
            Assert.Equal(viewModel.RepoCards[0].RepoPathLeaf, pathText.LeafText);
            Assert.Contains(PathEllipsisText.Ellipsis, pathText.DisplayText);
            Assert.EndsWith("checks-alpha", pathText.DisplayText);
            Assert.True(pathText.DisplayText.Length < folder.Length, "the path should have been shortened");
            Assert.All(
                pathText.GetVisualDescendants().OfType<TextBlock>(),
                text => Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(text)));
            Assert.True(pathText.Bounds.Width > 0);

            // Assert: the name ends before the Launch button starts
            var nameRight = nameText.TranslatePoint(new Point(nameText.Bounds.Width, 0), window)!.Value.X;
            var launchLeft = launch.TranslatePoint(new Point(0, 0), window)!.Value.X;
            Assert.True(nameRight <= launchLeft, $"the name ends at {nameRight}, the Launch button starts at {launchLeft}");
            Assert.Equal(name, viewModel.RepoCards[0].RepoName);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that the main window has one compact app bar (the logo with an accessible name and a tooltip
    ///     but no name text or tagline, the filter box, then the Add, Settings and About buttons), that the
    ///     filter is the first stop of the keyboard focus order, followed by the three buttons in order, and
    ///     that at the narrowest width the filter has shrunk while the buttons stay inside the window.
    /// </summary>
    [Fact]
    public void MainWindow_AppBar_LogoFilterAndButtonsInOrderAndNothingClipsAtMinimumWidth() => HeadlessTestApp.Run(() =>
    {
        // Arrange: the narrowest window
        var (window, _) = CreateWindow([new RecentRepo()], width: 560);

        try
        {
            var appBar = window.GetVisualDescendantsOf<Border>("AppBar").Single();
            var logo = window.GetVisualDescendantsOf<Border>("AppLogo").Single();
            var filter = window.GetVisualDescendantsOf<TextBox>("FilterTextBox").Single();
            var buttons = new[] { "AddRepoButton", "OpenSettingsButton", "OpenAboutButton" }
                .Select(id => window.GetVisualDescendantsOf<Button>(id).Single())
                .ToList();

            // Assert: the logo is named and has a tooltip, and the bar carries no name text or tagline
            Assert.Equal("AgentControl", AutomationProperties.GetName(logo));
            Assert.Equal("AgentControl", ToolTip.GetTip(logo));
            // (the filter box's own placeholder text does not count)
            Assert.DoesNotContain(
                appBar.GetVisualDescendants().OfType<TextBlock>().Where(text => text.FindAncestorOfType<TextBox>() is null),
                text => !string.IsNullOrEmpty(text.Text));

            // Assert: the keyboard stops (no tab index is set, so the visual order is the focus order) start with
            // the filter, then Add, Settings and About
            var stops = window.GetVisualDescendants()
                .OfType<InputElement>()
                .Where(element => element.Focusable && element.IsTabStop && element.IsEffectivelyVisible)
                .Take(4)
                .ToList();
            Assert.Equal<InputElement>([filter, buttons[0], buttons[1], buttons[2]], stops);

            // Assert: left to right, logo, filter, buttons; all inside the window, the filter taking what is left
            var lefts = new Control[] { logo, filter, buttons[0], buttons[1], buttons[2] }
                .Select(element => element.TranslatePoint(new Point(0, 0), window)!.Value.X)
                .ToList();
            Assert.Equal(lefts.Order(), lefts);
            var aboutRight = lefts[4] + buttons[2].Bounds.Width;
            Assert.True(aboutRight <= window.Bounds.Width, $"About ends at {aboutRight} in a {window.Bounds.Width} wide window");
            Assert.All(
                buttons,
                button => Assert.True(
                    button.Bounds.Width >= button.DesiredSize.Width - button.Margin.Left - button.Margin.Right - 1,
                    "a button was clipped"));
            Assert.True(filter.Bounds.Width > 100, $"the filter is {filter.Bounds.Width} wide");
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that the card color palette names the chosen color in words in its heading, in addition to
    ///     the swatch tooltips, accessible names and check mark.
    /// </summary>
    [Fact]
    public void MainWindow_CardColorPalette_HeadingNamesTheChosenColor() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var (window, viewModel) = CreateWindow([new RecentRepo()]);

        try
        {
            var moreButton = window.GetVisualDescendantsOf<Button>("MoreActionsButton").Single();
            var flyout = Assert.IsType<Flyout>(FlyoutBase.GetAttachedFlyout(moreButton));
            var menu = OpenMenu(moreButton);
            FindMenuItem(menu, "CardColorMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.Hide();
            Dispatcher.UIThread.RunJobs();
            var palette = Assert.IsAssignableFrom<Control>(flyout.Content);

            // Act / Assert: automatic first, then the chosen color
            var heading = palette.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "CardColorHeadingText");
            Assert.Equal("Card color: Automatic", heading.Text);
            viewModel.RepoCards[0].SetCardColor("Violet");
            Assert.Equal("Card color: Violet", heading.Text);
            flyout.Hide();
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that Settings has its own Appearance section, before Tools, holding the Theme picker, and
    ///     that Tools holds the git, agent tool and shell settings.
    /// </summary>
    [Fact]
    public void SettingsWindow_Layout_AppearanceIsItsOwnSectionBeforeTools() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var window = new SettingsWindow(new SettingsWindowViewModel(new AppSettings(), _ => { }, (_, _) => Task.FromResult(true)));
        window.Show();

        try
        {
            var appearance = window.FindControl<TextBlock>("AppearanceHeadingText")!;
            var tools = window.FindControl<TextBlock>("ToolsHeadingText")!;
            var theme = window.FindControl<ComboBox>("ThemeComboBox")!;
            var git = window.FindControl<TextBox>("GitExecutablePathTextBox")!;
            var agentTool = window.FindControl<ComboBox>("AgentToolComboBox")!;

            // Assert: headings and their order on the page
            Assert.Equal("Appearance", appearance.Text);
            Assert.Equal("Tools", tools.Text);
            double Top(Control control) => control.TranslatePoint(default, window)!.Value.Y;
            Assert.True(Top(appearance) < Top(theme));
            Assert.True(Top(theme) < Top(tools), "the theme picker belongs to Appearance, above the Tools heading");
            Assert.True(Top(tools) < Top(git));
            Assert.True(Top(tools) < Top(agentTool));
            Assert.Equal("Theme", AutomationProperties.GetName(theme));
            Assert.DoesNotContain(
                window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text?.StartsWith("Appearance (", StringComparison.Ordinal) == true);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that Settings is resizable with a minimum size, keeps its default opening width, and that
    ///     widening it widens the text boxes and the source list with it, while a short window scrolls the
    ///     settings and keeps the Save button in view.
    /// </summary>
    [Fact]
    public void SettingsWindow_Resizable_HasMinimumSizeAndWidensFieldsAndSourceList() => HeadlessTestApp.Run(() =>
    {
        // Arrange: one source with a long location
        var longLocation = @"\\server\share\" + string.Join('\\', Enumerable.Repeat("a-rather-long-folder-name", 8));
        var settings = new AppSettings
        {
            Sources = [new PackageSourceDefinition { Alias = "Long", Type = PackageSourceType.Zip, Location = longLocation }]
        };
        var window = new SettingsWindow(new SettingsWindowViewModel(settings, _ => { }, (_, _) => Task.FromResult(true)));
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var git = window.FindControl<TextBox>("GitExecutablePathTextBox")!;
            var list = window.FindControl<ListBox>("SourcesList")!;
            var location = window.GetVisualDescendants().OfType<TextBlock>().First(text => text.Name == "SourceLocationText");

            // Assert: resizable, with a minimum, no maximum width, and the default opening width
            Assert.True(window.CanResize);
            Assert.True(window.MinWidth >= 500);
            Assert.True(window.MinHeight >= 360);
            Assert.True(double.IsPositiveInfinity(window.MaxWidth));
            Assert.Equal(800, window.Width);
            var gitWidth = git.Bounds.Width;
            var listWidth = list.Bounds.Width;
            var locationWidth = location.Bounds.Width;

            // Act: widen the window by 300 (as the user dragging its edge does)
            window.Width += 300;
            Dispatcher.UIThread.RunJobs();

            // Assert: the fields and the list took the extra width, and the row shows more of the location
            Assert.True(git.Bounds.Width >= gitWidth + 250, $"git box {gitWidth} -> {git.Bounds.Width}");
            Assert.True(list.Bounds.Width >= listWidth + 250, $"list {listWidth} -> {list.Bounds.Width}");
            Assert.True(location.Bounds.Width >= locationWidth + 250, $"location {locationWidth} -> {location.Bounds.Width}");

            // Act: the user makes it short (resizing by hand ends automatic sizing to the content)
            window.SizeToContent = SizeToContent.Manual;
            window.Height = window.MinHeight;
            Dispatcher.UIThread.RunJobs();

            // Assert: the settings scroll, and the Save button stays inside the window
            var scroll = window.FindControl<ScrollViewer>("SettingsScrollViewer")!;
            var save = window.FindControl<Button>("SaveSettingsButton")!;
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            Assert.True(save.TranslatePoint(new Point(0, save.Bounds.Height), window)!.Value.Y <= window.ClientSize.Height);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that the package-source editor is resizable for every kind of source - zip folder, NuGet
    ///     folder, NuGet server and Git repository - with a minimum size, keeping its default opening width,
    ///     and that widening it widens its text boxes, while a short window scrolls the form and keeps the
    ///     Save button in view.
    /// </summary>
    /// <param name="kind">The kind of source: <c>zip</c>, <c>nuget-folder</c>, <c>nuget-server</c> or <c>git</c>.</param>
    [Theory]
    [InlineData("zip")]
    [InlineData("nuget-folder")]
    [InlineData("nuget-server")]
    [InlineData("git")]
    public void SourceEditorWindow_Resizable_HasMinimumSizeAndWidensFields(string kind) => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var editor = kind switch
        {
            "zip" => new SourceEditorViewModel(PackageSourceType.Zip, null, _ => false),
            "nuget-folder" => new SourceEditorViewModel(PackageSourceType.NuGet, null, _ => false),
            "nuget-server" => new SourceEditorViewModel(PackageSourceType.NuGet, null, _ => false, isServer: true),
            _ => new SourceEditorViewModel(PackageSourceType.Git, null, _ => false)
        };
        editor.Location = "https://feeds.example.com/" + string.Join('/', Enumerable.Repeat("a-rather-long-path-segment", 8));
        var window = new SourceEditorWindow(editor);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var alias = window.FindControl<TextBox>("SourceAliasTextBox")!;
            var location = window.FindControl<TextBox>("SourceLocationTextBox")!;

            // Assert: resizable, with a minimum, no maximum width, and the default opening width
            Assert.True(window.CanResize);
            Assert.True(window.MinWidth >= 500);
            Assert.True(window.MinHeight >= 360);
            Assert.True(double.IsPositiveInfinity(window.MaxWidth));
            Assert.Equal(640, window.Width);
            var aliasWidth = alias.Bounds.Width;
            var locationWidth = location.Bounds.Width;

            // Act: widen the window by 300
            window.Width += 300;
            Dispatcher.UIThread.RunJobs();

            // Assert: the fields took the extra width
            Assert.True(alias.Bounds.Width >= aliasWidth + 250, $"alias {aliasWidth} -> {alias.Bounds.Width}");
            Assert.True(location.Bounds.Width >= locationWidth + 250, $"location {locationWidth} -> {location.Bounds.Width}");

            // Act: the user makes it short
            window.SizeToContent = SizeToContent.Manual;
            window.Height = window.MinHeight;
            Dispatcher.UIThread.RunJobs();

            // Assert: the form scrolls (a zip-folder form is short enough to fit even at the minimum
            // height) and Save stays inside the window
            var scroll = window.FindControl<ScrollViewer>("SourceEditorScrollViewer")!;
            var save = window.FindControl<Button>("SourceEditorSaveButton")!;
            Assert.True(kind == "zip" || scroll.Extent.Height > scroll.Viewport.Height);
            Assert.True(save.TranslatePoint(new Point(0, save.Bounds.Height), window)!.Value.Y <= window.ClientSize.Height);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that Settings opened as a modal dialog puts the visible keyboard focus on its first control,
    ///     that Tab and Shift+Tab cycle among the dialog's own controls only (the window behind never gets the
    ///     focus), and that the focus returns to where it was in the main window when Settings closes.
    /// </summary>
    [Fact]
    public void SettingsWindow_ShownModally_FocusStartsInsideTabCyclesInsideAndReturnsToOwner() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: an owner window with a button that has the focus
        var ownerButton = new Button { Name = "OwnerButton", Content = "Owner" };
        var otherButton = new Button { Name = "OtherOwnerButton", Content = "Other" };
        var owner = new Window { Content = new StackPanel { Children = { ownerButton, otherButton } } };
        owner.Show();
        ownerButton.Focus();

        // The user opened Settings from the keyboard (Enter on the Settings button), so the focus ring shows
        PressKey(owner, PhysicalKey.Enter);
        var viewModel = new SettingsWindowViewModel(new AppSettings(), _ => { }, (_, _) => Task.FromResult(true));
        var settings = new SettingsWindow(viewModel);

        try
        {
            // Act: open Settings the way the main window does
            var shown = settings.ShowDialog(owner);
            Dispatcher.UIThread.RunJobs();

            // Assert: the first control has the focus, with the keyboard focus ring
            var first = Assert.IsAssignableFrom<Control>(settings.FocusManager.GetFocusedElement());
            Assert.Equal("AddSourceButton", first.Name);
            Assert.Contains(":focus-visible", first.Classes);

            // Act: Tab through more stops than the dialog has
            var visited = new List<IInputElement?>();
            for (var step = 0; step < 24; step++)
            {
                PressKey(settings, PhysicalKey.Tab);
                var focused = settings.FocusManager.GetFocusedElement();
                visited.Add(focused);

                // Assert: the focus is always in the dialog, never in the window behind it
                Assert.Same(settings, TopLevel.GetTopLevel(focused as Visual));
            }

            // Assert: the dialog's last control (Save) was reached and the focus came round to the first again
            Assert.Contains(visited, element => element is Control { Name: "SaveSettingsButton" });
            Assert.Contains(first, visited);
            Assert.DoesNotContain(visited, element => element is Control { Name: "OwnerButton" or "OtherOwnerButton" });

            // Act: Shift+Tab from the first control goes to the last one, not out of the dialog
            first.Focus(NavigationMethod.Tab);
            PressKey(settings, PhysicalKey.Tab, RawInputModifiers.Shift);
            var previous = Assert.IsAssignableFrom<Control>(settings.FocusManager.GetFocusedElement());
            Assert.Same(settings, TopLevel.GetTopLevel(previous));
            Assert.Equal("SaveSettingsButton", previous.Name);

            // Act: close it; the owner has its focus again
            settings.Close();
            await shown;
            Dispatcher.UIThread.RunJobs();
            Assert.Same(ownerButton, owner.FocusManager.GetFocusedElement());
        }
        finally
        {
            owner.Close();
        }
    });

    /// <summary>
    ///     Test that every small dialog opens with the focus on its safe or first control, shows the keyboard
    ///     focus ring there, keeps Tab inside (wrapping round) and closes on Escape: the confirmation answers
    ///     No, the message box closes, About closes.
    /// </summary>
    [Fact]
    public void Dialogs_ConfirmationMessageBoxAbout_InitialFocusTabCycleAndEscape() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var owner = new Window();
        owner.Show();

        try
        {
            // Confirmation: focus on No, Tab goes to Yes then wraps to No, Shift+Tab wraps back to Yes
            var confirmation = new ConfirmationWindow("Remove it?");
            var answer = confirmation.ShowDialog<bool>(owner);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("ConfirmationNoButton", FocusedName(confirmation));
            PressKey(confirmation, PhysicalKey.Tab);
            Assert.Equal("ConfirmationYesButton", FocusedName(confirmation));
            PressKey(confirmation, PhysicalKey.Tab);
            Assert.Equal("ConfirmationNoButton", FocusedName(confirmation));
            PressKey(confirmation, PhysicalKey.Tab, RawInputModifiers.Shift);
            Assert.Equal("ConfirmationYesButton", FocusedName(confirmation));

            // Escape answers No
            PressKey(confirmation, PhysicalKey.Escape);
            Assert.False(await answer);

            // Message box: focus on OK, Escape closes it
            var message = new MessageBoxWindow("Something failed");
            var closedMessage = message.ShowDialog(owner);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("MessageBoxOkButton", FocusedName(message));
            PressKey(message, PhysicalKey.Tab);
            Assert.Equal("MessageBoxOkButton", FocusedName(message));
            PressKey(message, PhysicalKey.Escape);
            await closedMessage;

            // About: focus on Close, Escape closes it
            var about = new AboutWindow();
            var closedAbout = about.ShowDialog(owner);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("AboutCloseButton", FocusedName(about));
            PressKey(about, PhysicalKey.Escape);
            await closedAbout;
        }
        finally
        {
            owner.Close();
        }
    });

    /// <summary>
    ///     Test that the prompts and editors open with the focus in their first field and cancel on Escape:
    ///     the group-name prompt (name selected), the source editor (alias) and the sign-in prompt (user name).
    /// </summary>
    [Fact]
    public void Dialogs_PromptsAndEditors_InitialFocusInFirstFieldAndEscapeCancels() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var owner = new Window();
        owner.Show();

        try
        {
            // Group-name prompt
            var groupViewModel = new GroupNameViewModel("New group", "Name:", "Create", ["Work"]);
            var group = new GroupNameWindow(groupViewModel);
            var groupResult = group.ShowDialog<string?>(owner);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("GroupNameTextBox", FocusedName(group));
            PressKey(group, PhysicalKey.Escape);
            Assert.Null(await groupResult);

            // Source editor
            var editor = new SourceEditorWindow(new SourceEditorViewModel(PackageSourceType.Zip, null, _ => false));
            var editorResult = editor.ShowDialog<PackageSourceDefinition?>(owner);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("SourceAliasTextBox", FocusedName(editor));
            PressKey(editor, PhysicalKey.Escape);
            Assert.Null(await editorResult);

            // Sign-in prompt
            var signInViewModel = new CredentialPromptViewModel(new CredentialPromptRequest(
                FeedOrigin.Create(new Uri("https://feeds.example.com/v3/index.json")), "Feed", false, false));
            var signIn = new CredentialPromptWindow(signInViewModel);
            var signInResult = signIn.ShowDialog<object?>(owner);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("CredentialPromptUserNameTextBox", FocusedName(signIn));
            PressKey(signIn, PhysicalKey.Escape);
            await signInResult;
        }
        finally
        {
            owner.Close();
        }
    });

    /// <summary>
    ///     Test that the About window shows a labeled Version row with the version without its build
    ///     metadata, the full build version in the tooltip, and that DisplayVersion drops only the metadata.
    /// </summary>
    [Fact]
    public void AboutWindow_VersionRow_IsLabeledAndShortWithFullVersionInTooltip() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var window = new AboutWindow();
        window.Show();

        try
        {
            // Act
            var versionText = window.FindControl<TextBlock>("AboutVersionText")!;
            var rowLabel = ((StackPanel)versionText.Parent!).Children.OfType<TextBlock>().First();

            // Assert: a label, then the short version; not the product name again
            Assert.Equal("Version", rowLabel.Text);
            Assert.Equal(AboutWindow.DisplayVersion(Program.Version), versionText.Text);
            Assert.DoesNotContain("+", versionText.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("AgentControl", versionText.Text, StringComparison.Ordinal);
            if (Program.Version.Contains('+', StringComparison.Ordinal))
            {
                Assert.Equal($"Full build version: {Program.Version}", ToolTip.GetTip(versionText));
            }
        }
        finally
        {
            window.Close();
        }

        // Assert: only build metadata is dropped
        Assert.Equal("1.2.3", AboutWindow.DisplayVersion("1.2.3+0123456789abcdef"));
        Assert.Equal("1.2.3-beta.1", AboutWindow.DisplayVersion("1.2.3-beta.1+abc"));
        Assert.Equal("1.2.3", AboutWindow.DisplayVersion("1.2.3"));
        Assert.Equal("+abc", AboutWindow.DisplayVersion("+abc"));
        Assert.Throws<ArgumentNullException>(() => AboutWindow.DisplayVersion(null!));
    });

    /// <summary>
    ///     Test that, when an upgrade offers the AGENTS.md template, the question comes first and the release
    ///     notes window opens only after it is answered (so it is in front, not covered by the question).
    /// </summary>
    [Fact]
    public void MainWindow_UpgradeWithAgentsMdOffer_ShowsQuestionFirstThenReleaseNotes() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: a repo with no AGENTS.md and a package that has release notes and an AGENTS.md template
        var sourceDir = CreateTempDirectory();
        CreateZip(sourceDir, "contoso-agents", "1.0.0", releaseNotes: "# Notes", agentsMd: "# AGENTS");
        var repo = CreateRepoDirectory("upgrade-me");
        var (window, viewModel) = CreateWindow([new RecentRepo { Path = repo }], new AppSettings { PackageSourcePath = sourceDir });

        try
        {
            var card = viewModel.RepoCards.Single();

            // Act: apply the package
            await card.ApplySelectedPackageAsync(new PackageReference("Default", "contoso-agents", "1.0.0"));
            Dispatcher.UIThread.RunJobs();

            // Assert: the question is open, the release notes are not shown yet
            var question = Assert.Single(window.OwnedWindows.OfType<ConfirmationWindow>());
            Assert.Empty(window.OpenChildWindows);

            // Act: answer it
            DialogWindowTests.Click(question, "ConfirmationYesButton");
            await Task.Delay(50, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();

            // Assert: now the release notes window is open, and the question is gone
            Assert.Empty(window.OwnedWindows);
            Assert.IsType<DemaConsulting.AgentControl.RepoSync.ReleaseNotesViewer>(Assert.Single(window.OpenChildWindows));
            Assert.True(File.Exists(Path.Combine(repo, "AGENTS.md")));
        }
        finally
        {
            foreach (var child in window.OpenChildWindows.ToList())
            {
                child.Close();
            }

            window.Close();
        }
    });

    /// <summary>
    ///     Test that selecting the version a repo is pinned to, when its package was republished with other content,
    ///     asks before changing anything: No leaves the repo and the pin as they were, Yes applies the package and
    ///     records its new identity.
    /// </summary>
    [Fact]
    public void MainWindow_SelectingRepublishedPinnedVersion_AsksFirstAndAppliesOnlyWhenConfirmed() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: a repo applied from 1.0.0 (identity recorded), whose zip is then republished with other content
        var sourceDir = CreateTempDirectory();
        CreateZip(sourceDir, "contoso-agents", "1.0.0", releaseNotes: "# Original");
        var repo = CreateRepoDirectory("republished");
        var (window, viewModel) = CreateWindow([new RecentRepo { Path = repo }], new AppSettings { PackageSourcePath = sourceDir });

        try
        {
            var card = viewModel.RepoCards.Single();
            var selection = new PackageReference("Default", "contoso-agents", "1.0.0");
            await card.ApplySelectedPackageAsync(selection);
            Dispatcher.UIThread.RunJobs();
            CloseChildWindows(window);
            var recorded = RepoPinStore.Load(repo)?.ContentIdentity;
            Assert.NotNull(recorded);
            File.Delete(Path.Combine(sourceDir, "contoso-agents-1.0.0.zip"));
            CreateZip(
                sourceDir, "contoso-agents", "1.0.0", releaseNotes: "# Republished with other content",
                agentFileContent: "republished agent file");

            // Act 1: select the same version again and answer No
            await card.ApplySelectedPackageAsync(selection);
            Dispatcher.UIThread.RunJobs();
            var question = Assert.Single(window.OwnedWindows.OfType<ConfirmationWindow>());
            DialogWindowTests.Click(question, "ConfirmationNoButton");
            await Task.Delay(50, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();

            // Assert 1: the question was asked before anything changed, and No changed nothing
            Assert.Empty(window.OwnedWindows);
            Assert.Empty(window.OpenChildWindows);
            Assert.Equal(recorded, RepoPinStore.Load(repo)?.ContentIdentity);

            // Act 2: select it again and answer Yes
            await card.ApplySelectedPackageAsync(selection);
            Dispatcher.UIThread.RunJobs();
            question = Assert.Single(window.OwnedWindows.OfType<ConfirmationWindow>());
            DialogWindowTests.Click(question, "ConfirmationYesButton");
            for (var attempt = 0; attempt < 100 && RepoPinStore.Load(repo)?.ContentIdentity == recorded; attempt++)
            {
                await Task.Delay(50, TestContext.Current.CancellationToken);
                Dispatcher.UIThread.RunJobs();
            }

            // Assert 2: the changed package was applied and its identity recorded
            var applied = RepoPinStore.Load(repo)?.ContentIdentity;
            Assert.NotNull(applied);
            Assert.NotEqual(recorded, applied);
        }
        finally
        {
            CloseChildWindows(window);
            window.Close();
        }
    });

    /// <summary>
    ///     Test that applying a package to a repo whose agent folders are tracked by git asks in a modal
    ///     question before anything is downloaded or changed: No leaves git, the files and the pin as
    ///     they were, Yes stops git tracking the folders and applies the package.
    /// </summary>
    [Fact]
    public void MainWindow_ApplyingToRepoWithTrackedAgentFolders_AsksFirstAndConvertsOnlyWhenConfirmed() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: a real repo that tracks an agents folder, and a source with one package
        using var fixture = AgentPackageManagement.GitRepositoryFixture.CreateOrSkip();
        fixture.Commit(
            new Dictionary<string, string> { [".github/agents/old.md"] = "old", ["README.md"] = "readme" },
            "Track agent files");
        var sourceDir = CreateTempDirectory();
        CreateZip(sourceDir, "contoso-agents", "1.0.0", releaseNotes: "# Notes");
        var (window, viewModel) = CreateWindow([new RecentRepo { Path = fixture.WorkPath }], new AppSettings { PackageSourcePath = sourceDir });

        try
        {
            var card = viewModel.RepoCards.Single();
            var selection = new PackageReference("Default", "contoso-agents", "1.0.0");

            // Act 1: apply; the question appears while the card is busy
            var applying = card.ApplySelectedPackageAsync(selection);
            await PumpUntilAsync(() => window.OwnedWindows.OfType<ConfirmationWindow>().Any());
            var question = Assert.Single(window.OwnedWindows.OfType<ConfirmationWindow>());

            // Assert 1: the card is still busy, and nothing has changed yet
            Assert.True(card.IsBusy);
            Assert.False(applying.IsCompleted);
            Assert.False(File.Exists(Path.Combine(fixture.WorkPath, ".agentcontrol.json")));

            // Act 2: answer No
            DialogWindowTests.Click(question, "ConfirmationNoButton");
            await PumpUntilAsync(() => applying.IsCompleted);

            // Assert 2: No changed nothing in git, on disk or in the pin
            Assert.Empty(window.OwnedWindows);
            Assert.Contains(".github/agents/old.md", fixture.Git(fixture.WorkPath, "ls-files"), StringComparison.Ordinal);
            Assert.Equal("old", File.ReadAllText(Path.Combine(fixture.WorkPath, ".github", "agents", "old.md")));
            Assert.False(File.Exists(Path.Combine(fixture.WorkPath, ".agentcontrol.json")));
            Assert.False(card.IsBusy);

            // Act 3: apply again and answer Yes
            applying = card.ApplySelectedPackageAsync(selection);
            await PumpUntilAsync(() => window.OwnedWindows.OfType<ConfirmationWindow>().Any());
            DialogWindowTests.Click(window.OwnedWindows.OfType<ConfirmationWindow>().Single(), "ConfirmationYesButton");
            await PumpUntilAsync(() => applying.IsCompleted);

            // Assert 3: git no longer tracks the folder, the package is applied and the user is told to commit
            Assert.DoesNotContain(".github/agents", fixture.Git(fixture.WorkPath, "ls-files"), StringComparison.Ordinal);
            Assert.Equal("1.0.0", RepoPinStore.Load(fixture.WorkPath)?.Version);
            Assert.False(File.Exists(Path.Combine(fixture.WorkPath, ".github", "agents", "old.md")));
            Assert.Contains("commit the change", card.StatusMessage, StringComparison.Ordinal);
            Assert.False(card.HasCommittedAgentFiles);
        }
        finally
        {
            CloseChildWindows(window);
            window.Close();
        }
    });

    /// <summary>
    ///     Lets the dispatcher and background work run until a condition holds, giving up after about
    ///     five seconds so a test that would hang fails instead.
    /// </summary>
    /// <param name="condition">What to wait for.</param>
    private static async Task PumpUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition(), "The condition did not become true in time.");
    }

    /// <summary>
    ///     Closes the release-notes windows a main window still has open.
    /// </summary>
    /// <param name="window">The main window.</param>
    private static void CloseChildWindows(MainWindow window)
    {
        foreach (var child in window.OpenChildWindows.ToList())
        {
            child.Close();
        }
    }

    /// <summary>
    ///     Test that an upgrade without an AGENTS.md question shows the release notes at once.
    /// </summary>
    [Fact]
    public void MainWindow_UpgradeWithoutAgentsMdOffer_ShowsReleaseNotesAtOnce() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: a package with release notes but no template
        var sourceDir = CreateTempDirectory();
        CreateZip(sourceDir, "contoso-agents", "1.0.0", releaseNotes: "# Notes");
        var repo = CreateRepoDirectory("upgrade-me");
        var (window, viewModel) = CreateWindow([new RecentRepo { Path = repo }], new AppSettings { PackageSourcePath = sourceDir });

        try
        {
            // Act
            await viewModel.RepoCards.Single().ApplySelectedPackageAsync(new PackageReference("Default", "contoso-agents", "1.0.0"));
            Dispatcher.UIThread.RunJobs();

            // Assert: no question, the notes are open
            Assert.Empty(window.OwnedWindows);
            Assert.IsType<DemaConsulting.AgentControl.RepoSync.ReleaseNotesViewer>(Assert.Single(window.OpenChildWindows));
        }
        finally
        {
            foreach (var child in window.OpenChildWindows.ToList())
            {
                child.Close();
            }

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
            Directory.Delete(path, recursive: true);
        }
    }

    /// <summary>
    ///     Creates a shown main window over repos, tall enough that every card is realized and visible.
    /// </summary>
    /// <param name="repos">The repos; a repo without a path gets a new folder.</param>
    /// <param name="settings">The settings to start from, or <see langword="null"/> for defaults.</param>
    /// <param name="width">The window's width.</param>
    /// <returns>The window and its view model.</returns>
    private (MainWindow Window, MainWindowViewModel ViewModel) CreateWindow(
        RecentRepo[] repos, AppSettings? settings = null, double width = 900)
    {
        settings ??= new AppSettings();
        foreach (var repo in repos.Where(repo => string.IsNullOrEmpty(repo.Path)))
        {
            repo.Path = CreateRepoDirectory("repo-" + Guid.NewGuid().ToString("N")[..6]);
        }

        settings.RecentRepos = [.. repos];
        var viewModel = new MainWindowViewModel(settings, configDirectory: CreateTempDirectory());
        var window = new MainWindow { DataContext = viewModel, Width = width, Height = 1600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, viewModel);
    }

    /// <summary>
    ///     Lists the accessible names of the section headers that are shown, top to bottom as the
    ///     user sees them (the order of the controls in the visual tree is not the order on screen
    ///     once the list has moved a card between sections).
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <returns>The headers' accessible names.</returns>
    private static List<string?> HeaderNames(MainWindow window) =>
        [.. window.GetVisualDescendantsOf<Button>("GroupHeaderToggleButton")
            .OrderBy(button => button.TranslatePoint(new Point(0, 0), window)!.Value.Y)
            .Select(AutomationProperties.GetName)];

    /// <summary>
    ///     Finds the favorite star of the named repo's card.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="repoName">The repo's folder name.</param>
    /// <returns>The star.</returns>
    private static ToggleButton FindStar(MainWindow window, string repoName) =>
        window.GetVisualDescendantsOf<ToggleButton>("FavoriteToggleButton")
            .Single(star => star.DataContext is RepoCardViewModel card && card.RepoName == repoName);

    /// <summary>
    ///     Lists the names of the repos listed under a section's header.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="section">The section's name.</param>
    /// <returns>The repo names, in list order.</returns>
    private static List<string> SectionOf(MainWindow window, string section)
    {
        var items = ((MainWindowViewModel)window.DataContext!).DisplayedItems.ToList();
        var header = items.FindIndex(item => item is RepoGroupHeaderViewModel { Name: var name } && name == section);
        return [.. items.Skip(header + 1).TakeWhile(item => item is RepoCardViewModel).Cast<RepoCardViewModel>().Select(card => card.RepoName)];
    }

    /// <summary>
    ///     Clicks a control with the real pointer: a mouse button press and release at its center, as a
    ///     user's click arrives (not a raised click event, which skips the toggling and command logic).
    /// </summary>
    /// <param name="window">The window containing the control.</param>
    /// <param name="control">The control to click.</param>
    private static void ClickWithMouse(Window window, Control control)
    {
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
                     ?? throw new InvalidOperationException("The control is not in the window.");
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    ///     Presses and releases a key as the keyboard sends it to the window.
    /// </summary>
    /// <param name="window">The window that receives the key.</param>
    /// <param name="key">The physical key.</param>
    /// <param name="modifiers">The modifier keys held.</param>
    private static void PressKey(TopLevel window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers);
        try
        {
            window.KeyReleaseQwerty(key, modifiers);
        }
        catch (ObjectDisposedException)
        {
            // The press closed the window (Escape on a dialog), so there is nothing to release the key on
        }

        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    ///     Gets the XAML name of the window's focused control.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <returns>The name, or <see langword="null"/> when nothing, or an unnamed control, is focused.</returns>
    private static string? FocusedName(Window window) => (window.FocusManager?.GetFocusedElement() as Control)?.Name;

    /// <summary>
    ///     Opens the "..." menu of the named repo's card.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="repoName">The repo's folder name.</param>
    /// <returns>The open menu.</returns>
    private static MenuFlyout OpenCardMenu(MainWindow window, string repoName) =>
        OpenMenu(window.GetVisualDescendantsOf<Button>("MoreActionsButton")
            .Single(candidate => candidate.DataContext is RepoCardViewModel card && card.RepoName == repoName));

    /// <summary>
    ///     Opens the menu of a button the way a click does.
    /// </summary>
    /// <param name="button">The button whose flyout to open.</param>
    /// <returns>The open menu.</returns>
    private static MenuFlyout OpenMenu(Button button)
    {
        var flyout = Assert.IsType<MenuFlyout>(button.Flyout);
        flyout.ShowAt(button);
        Dispatcher.UIThread.RunJobs();
        return flyout;
    }

    /// <summary>
    ///     Finds an item of an open menu by its XAML name.
    /// </summary>
    /// <param name="menu">The open menu.</param>
    /// <param name="name">The item's XAML name.</param>
    /// <returns>The item.</returns>
    private static MenuItem FindMenuItem(MenuFlyout menu, string name) =>
        menu.Items.OfType<MenuItem>().Single(item => item.Name == name);

    /// <summary>
    ///     Creates a package zip named <c>{name}-{version}.zip</c> in a source folder.
    /// </summary>
    /// <param name="sourceDir">The source folder.</param>
    /// <param name="name">The package ID.</param>
    /// <param name="version">The version.</param>
    /// <param name="releaseNotes">Release notes text to include, or <see langword="null"/>.</param>
    /// <param name="agentsMd">An AGENTS.md template to include, or <see langword="null"/>.</param>
    /// <param name="agentFileContent">The text of each of the four managed agent files.</param>
    private static void CreateZip(
        string sourceDir, string name, string version, string? releaseNotes = null, string? agentsMd = null,
        string agentFileContent = "content")
    {
        using var archive = ZipFile.Open(Path.Combine(sourceDir, $"{name}-{version}.zip"), ZipArchiveMode.Create);
        foreach (var folder in new[] { "agents", "standards", "templates", "skills" })
        {
            AddEntry(archive, $".github/{folder}/readme.md", agentFileContent);
        }

        if (releaseNotes is not null)
        {
            AddEntry(archive, "release-notes.md", releaseNotes);
        }

        if (agentsMd is not null)
        {
            AddEntry(archive, "AGENTS.md", agentsMd);
        }
    }

    /// <summary>
    ///     Adds a text entry to a zip archive.
    /// </summary>
    /// <param name="archive">The archive.</param>
    /// <param name="path">The entry's path.</param>
    /// <param name="content">The entry's text.</param>
    private static void AddEntry(ZipArchive archive, string path, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open());
        writer.Write(content);
    }

    /// <summary>
    ///     Creates a repo folder whose pin file records a package of the <c>Default</c> source.
    /// </summary>
    /// <param name="name">The folder name.</param>
    /// <param name="version">The pinned version.</param>
    /// <returns>The folder's path.</returns>
    private string CreatePinnedRepo(string name, string version)
    {
        var path = CreateRepoDirectory(name);
        RepoPinStore.Save(path, new RepoPin { Source = "Default", PackageName = "contoso-agents", Version = version });
        return path;
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
        return path;
    }

    /// <summary>
    ///     Creates a unique temporary directory that is removed on disposal.
    /// </summary>
    /// <returns>The directory's path.</returns>
    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "AgentControlUsability-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        return path;
    }
}
