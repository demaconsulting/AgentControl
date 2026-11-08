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
                window.GetVisualDescendantsOf<Button>("GroupHeaderToggleButton").Select(AutomationProperties.GetName));
            Assert.Equal(repoName, SectionOf(window, "Favorites").Single());
            Assert.True(viewModel.Settings.RecentRepos[1].IsFavorite);

            // Act: click the star again
            ClickWithMouse(window, FindStar(window, repoName));

            // Assert: not a favorite any more, back under Ungrouped, stored
            Assert.False(viewModel.RepoCards[1].IsFavorite);
            Assert.False(FindStar(window, repoName).IsChecked);
            Assert.Equal(
                ["Work, 1 repo, expanded", "Ungrouped, 2 repos, expanded"],
                window.GetVisualDescendantsOf<Button>("GroupHeaderToggleButton").Select(AutomationProperties.GetName));
            Assert.Contains(repoName, SectionOf(window, "Ungrouped"));
            Assert.False(viewModel.Settings.RecentRepos[1].IsFavorite);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that the star also toggles exactly once from the keyboard (Space), off to on and on to off,
    ///     and that its accessible name says what it is.
    /// </summary>
    [Fact]
    public void MainWindow_FavoriteStar_SpaceKey_TogglesOncePerPress() => HeadlessTestApp.Run(() =>
    {
        // Arrange: a group, so favoriting moves the card from Ungrouped into a new Favorites section
        var (window, viewModel) = CreateWindow([new RecentRepo { GroupName = "Work" }, new RecentRepo(), new RecentRepo()]);

        try
        {
            var repoName = viewModel.RepoCards[1].RepoName;
            Assert.Equal("Favorite", AutomationProperties.GetName(FindStar(window, repoName)));

            // Act: Space on the focused star
            FindStar(window, repoName).Focus(NavigationMethod.Tab);
            PressKey(window, PhysicalKey.Space);

            // Assert: favorite, and the keyboard focus is still on that repo's star (the card moved)
            Assert.True(viewModel.RepoCards[1].IsFavorite);
            Assert.Same(FindStar(window, repoName), window.FocusManager.GetFocusedElement());

            // Act: Space again, without touching the focus
            PressKey(window, PhysicalKey.Space);

            // Assert
            Assert.False(viewModel.RepoCards[1].IsFavorite);
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
    ///     Test that a card's name ends in an ellipsis with a full-name tooltip, that its path keeps the
    ///     folder name (the tail) whole and gives way in the folder part, with the full path as its tooltip
    ///     and accessible name, and that at the narrowest width the name never runs under the Launch
    ///     button.
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
            var pathPanel = window.GetVisualDescendantsOf<Grid>("RepoPathText").Single();
            var parentText = window.GetVisualDescendantsOf<TextBlock>("RepoPathParentText").Single();
            var leafText = window.GetVisualDescendantsOf<TextBlock>("RepoPathLeafText").Single();
            var launch = window.GetVisualDescendantsOf<Button>("LaunchButton").Single();

            // Assert: the name trims at its end and its tooltip has all of it
            Assert.Equal(TextTrimming.CharacterEllipsis, nameText.TextTrimming);
            Assert.Equal(name, ToolTip.GetTip(nameText));

            // Assert: the folder part trims, the folder name does not, and the whole path is the name/tooltip
            Assert.Equal(TextTrimming.CharacterEllipsis, parentText.TextTrimming);
            Assert.Equal(Path.DirectorySeparatorChar + name, leafText.Text);
            Assert.Equal(folder, ToolTip.GetTip(pathPanel));
            Assert.Equal(folder, AutomationProperties.GetName(pathPanel));
            Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(parentText));
            leafText.Measure(Size.Infinity);
            Assert.True(
                leafText.Bounds.Width >= leafText.DesiredSize.Width - 1,
                $"the folder name was trimmed: {leafText.Bounds.Width} < {leafText.DesiredSize.Width}");
            parentText.Measure(Size.Infinity);
            Assert.True(parentText.Bounds.Width < parentText.DesiredSize.Width, "the folder part should have given way");

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
    private static void CreateZip(string sourceDir, string name, string version, string? releaseNotes = null, string? agentsMd = null)
    {
        using var archive = ZipFile.Open(Path.Combine(sourceDir, $"{name}-{version}.zip"), ZipArchiveMode.Create);
        foreach (var folder in new[] { "agents", "standards", "templates", "skills" })
        {
            AddEntry(archive, $".github/{folder}/readme.md", "content");
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
