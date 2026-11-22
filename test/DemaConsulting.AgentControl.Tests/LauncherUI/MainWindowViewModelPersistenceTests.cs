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

using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.Settings;
using DemaConsulting.AgentControl.Tests.AgentToolLauncher;
using DemaConsulting.AgentControl.Utilities;
using Microsoft.Extensions.Time.Testing;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Tests how the main window's view model keeps the user's settings: a change made in the window is
///     applied at once and written in the background, a failed write is reported once and the change is
///     kept and written later, and the changes that must be on disk before they are applied (an add, the
///     Settings window's Save) are confirmed saves that change nothing when they fail.
/// </summary>
/// <remarks>
///     The save seam (<see cref="SaveControl"/>) writes the real settings file, counts the writes, can
///     hold them at a gate and can make them fail, so no test depends on timing: a test that reads the
///     file first waits for <see cref="MainWindowViewModel.FlushSettingsAsync"/>. Every view model is
///     created with no synchronization context current, so a failed background write is reported on
///     the writing thread, before the flush that made it completes. Runs in the <c>RealProcess</c>
///     collection because every card's background repo-state read spawns real git subprocesses.
/// </remarks>
[Collection("RealProcess")]
public sealed class MainWindowViewModelPersistenceTests : IDisposable
{
    private readonly List<string> _tempPaths = [];

    private readonly List<MainWindowViewModel> _viewModels = [];

    /// <summary>
    ///     Test that marking a favorite while the settings file is busy changes the card, the settings and
    ///     the list at once, without waiting for the write, and that the favorite reaches the file once
    ///     the write can complete.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_Favorite_SaveHeld_AppliedAtOnceAndWrittenAfterFlush()
    {
        // Arrange: two repos, and writes held at a gate
        var fixture = CreateFixture("alpha", "beta");
        var alpha = fixture.Card("alpha");
        fixture.Saving.Gate.Reset();

        // Act: mark the favorite and make the write start (it waits at the gate)
        alpha.IsFavorite = true;
        var flush = fixture.ViewModel.FlushSettingsAsync();
        await fixture.Saving.WaitForStartAsync();

        // Assert: applied everywhere at once, nothing written yet
        Assert.True(alpha.IsFavorite);
        Assert.True(fixture.Settings.RecentRepos.Single(repo => repo.Path == alpha.RepoPath).IsFavorite);
        Assert.Equal(["alpha", "beta"], Describe(fixture.ViewModel));
        Assert.False(flush.IsCompleted);
        Assert.Equal(0, fixture.Saving.Saves);

        // Act: the write can complete
        fixture.Saving.Gate.Set();
        var written = await flush;

        // Assert: the file holds the favorite
        Assert.True(written);
        Assert.True(SettingsStore.Load(fixture.ConfigDirectory).RecentRepos.Single(repo => repo.Path == alpha.RepoPath).IsFavorite);
        Assert.Empty(fixture.Failures);
    }

    /// <summary>
    ///     Test that a favorite whose background write fails is kept (on the card, in the settings and in
    ///     the list), that the user is told once however many changes fail in a row, and that the next
    ///     change, once writing works again, writes the favorite with it.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_FavoriteSaveFails_KeptReportedOnceAndSavedWithNextChange()
    {
        // Arrange: two repos, and writes that fail
        var fixture = CreateFixture("alpha", "beta");
        var alpha = fixture.Card("alpha");
        var beta = fixture.Card("beta");
        fixture.Saving.Fail = true;

        // Act: mark a favorite and let the write fail
        alpha.IsFavorite = true;
        var written = await fixture.ViewModel.FlushSettingsAsync();

        // Assert: the favorite is kept, the user was told once (why, and that it is kept), nothing written
        Assert.False(written);
        Assert.True(alpha.IsFavorite);
        Assert.True(fixture.Settings.RecentRepos.Single(repo => repo.Path == alpha.RepoPath).IsFavorite);
        Assert.Equal(["alpha", "beta"], Describe(fixture.ViewModel));
        var message = Assert.Single(fixture.Failures);
        Assert.Contains("could not be saved", message, StringComparison.Ordinal);
        Assert.Contains("The disk is full.", message, StringComparison.Ordinal);
        Assert.Contains("stay in effect", message, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.SettingsPath));

        // Act: another change fails as well
        Assert.True(beta.SetCardColor("Teal"));
        await fixture.ViewModel.FlushSettingsAsync();

        // Assert: still told only once
        Assert.Single(fixture.Failures);

        // Act: writing works again and the next change is made
        fixture.Saving.Fail = false;
        Assert.True(beta.MoveToGroup("Work"));
        written = await fixture.ViewModel.FlushSettingsAsync();

        // Assert: the file holds the favorite, the color and the group; no new message
        Assert.True(written);
        var saved = SettingsStore.Load(fixture.ConfigDirectory).RecentRepos;
        Assert.True(saved.Single(repo => repo.Path == alpha.RepoPath).IsFavorite);
        Assert.Equal("Teal", saved.Single(repo => repo.Path == beta.RepoPath).CardColor);
        Assert.Equal("Work", saved.Single(repo => repo.Path == beta.RepoPath).GroupName);
        Assert.Single(fixture.Failures);
    }

    /// <summary>
    ///     Test that moving a repo into a group whose background write fails is applied (the card, the
    ///     settings and the sectioned list) and reported.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_MoveToGroupSaveFails_KeptAndReported()
    {
        // Arrange
        var fixture = CreateFixture("alpha", "beta");
        var alpha = fixture.Card("alpha");
        fixture.Saving.Fail = true;

        // Act
        var moved = alpha.MoveToGroup("Work");
        await fixture.ViewModel.FlushSettingsAsync();

        // Assert
        Assert.True(moved);
        Assert.Equal("Work", alpha.GroupName);
        Assert.Equal("Work", fixture.Settings.RecentRepos.Single(repo => repo.Path == alpha.RepoPath).GroupName);
        Assert.Equal(["[Work]", "alpha", "[Ungrouped]", "beta"], Describe(fixture.ViewModel));
        Assert.Single(fixture.Failures);
    }

    /// <summary>
    ///     Test that a card color whose background write fails is applied and reported.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_CardColorSaveFails_KeptAndReported()
    {
        // Arrange
        var fixture = CreateFixture("alpha");
        var alpha = fixture.Card("alpha");
        fixture.Saving.Fail = true;

        // Act
        var chosen = alpha.SetCardColor("Teal");
        await fixture.ViewModel.FlushSettingsAsync();

        // Assert
        Assert.True(chosen);
        Assert.Equal("Teal", alpha.CardColorName);
        Assert.Equal("Teal", fixture.Settings.RecentRepos.Single().CardColor);
        Assert.Single(fixture.Failures);
    }

    /// <summary>
    ///     Test that a launch whose stamp cannot be written is still recorded (the stamp is kept in
    ///     memory, the list is not re-sorted) and that the failure is reported as a settings problem.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_LaunchRecordSaveFails_StampKeptAndReported()
    {
        // Arrange: a repo with no pin (the launch does not need a package source) and a custom tool
        var fixture = CreateFixture(
            new AppSettings
            {
                AgentTool = AgentToolKind.Custom,
                CustomAgentCommand = "my-agent"
            },
            "alpha");
        var alpha = fixture.Card("alpha");
        var recorded = false;
        alpha.LaunchRecorded += (_, _) => recorded = true;
        fixture.Saving.Fail = true;

        // Act
        await alpha.LaunchCommand.ExecuteAsync();
        await fixture.ViewModel.FlushSettingsAsync();

        // Assert: the tool was requested through the fake starter and the stamp was recorded in memory
        Assert.Single(fixture.Starter.Started);
        Assert.NotNull(alpha.LastLaunchedUtc);
        Assert.NotNull(fixture.Settings.RecentRepos.Single().LastLaunchedUtc);
        Assert.True(recorded);
        Assert.Single(fixture.Failures);
    }

    /// <summary>
    ///     Test that collapsing a section whose background write fails collapses it - from the view model
    ///     API and from the header's own button alike, neither of which throws - and reports the failure
    ///     once.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_CollapseSaveFails_SectionCollapsedAndReported()
    {
        // Arrange: two groups
        var fixture = CreateFixture(("alpha", "Work"), ("beta", "Home"));
        var home = fixture.ViewModel.DisplayedItems.OfType<RepoGroupHeaderViewModel>().Single(h => h.Name == "Home");
        fixture.Saving.Fail = true;

        // Act: collapse one section through the API and the other through its header's button
        fixture.ViewModel.ToggleGroupCollapsed("Work");
        home.ToggleCommand.Execute(null);
        await fixture.ViewModel.FlushSettingsAsync();

        // Assert
        Assert.True(fixture.ViewModel.IsGroupCollapsed("Work"));
        Assert.True(fixture.ViewModel.IsGroupCollapsed("Home"));
        Assert.Equal(["Work", "Home"], fixture.Settings.CollapsedGroups);
        Assert.Single(fixture.Failures);
    }

    /// <summary>
    ///     Test that renaming a group whose background write fails renames it - its repos and its
    ///     collapsed state follow - and reports the failure.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_RenameGroupSaveFails_RenamedAndReported()
    {
        // Arrange: a collapsed group
        var fixture = CreateFixture(("alpha", "Work"), ("beta", "Work"));
        fixture.ViewModel.ToggleGroupCollapsed("Work");
        await fixture.ViewModel.FlushSettingsAsync();
        fixture.Saving.Fail = true;

        // Act
        var renamed = fixture.ViewModel.RenameGroup("Work", "Office");
        await fixture.ViewModel.FlushSettingsAsync();

        // Assert
        Assert.True(renamed);
        Assert.Equal(["Office"], fixture.ViewModel.GroupNames);
        Assert.All(fixture.Settings.RecentRepos, repo => Assert.Equal("Office", repo.GroupName));
        Assert.True(fixture.ViewModel.IsGroupCollapsed("Office"));
        Assert.Single(fixture.Failures);
        Assert.All(SettingsStore.Load(fixture.ConfigDirectory).RecentRepos, repo => Assert.Equal("Work", repo.GroupName));
    }

    /// <summary>
    ///     Test that removing a group whose background write fails removes it (its repos become
    ///     ungrouped, its collapsed state is forgotten) and reports the failure.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_RemoveGroupSaveFails_RemovedAndReported()
    {
        // Arrange
        var fixture = CreateFixture(("alpha", "Work"), ("beta", null));
        fixture.ViewModel.ToggleGroupCollapsed("Work");
        fixture.Saving.Fail = true;

        // Act
        var removed = fixture.ViewModel.RemoveGroup("Work");
        await fixture.ViewModel.FlushSettingsAsync();

        // Assert
        Assert.True(removed);
        Assert.Empty(fixture.ViewModel.GroupNames);
        Assert.Null(fixture.Card("alpha").GroupName);
        Assert.Empty(fixture.Settings.CollapsedGroups);
        Assert.Single(fixture.Failures);
    }

    /// <summary>
    ///     Test that adding a repo whose save fails adds nothing: not to the cards, the displayed list
    ///     or the settings - an add is applied only once it was saved.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_AddRepoSaveFails_NothingAdded()
    {
        // Arrange
        var fixture = CreateFixture("alpha");
        var newRepo = CreateDirectory("gamma");
        fixture.Saving.Fail = true;

        // Act / Assert
        Assert.Throws<InvalidOperationException>(() => fixture.ViewModel.AddRepo(newRepo));
        Assert.Single(fixture.ViewModel.RepoCards);
        Assert.Single(fixture.Settings.RecentRepos);
        Assert.Single(fixture.ViewModel.DisplayedRepoCards);

        // Act: saving works again, so the same repo can be added
        fixture.Saving.Fail = false;

        // Assert
        Assert.True(fixture.ViewModel.AddRepo(newRepo));
        Assert.Equal(2, fixture.ViewModel.RepoCards.Count);
        await fixture.ViewModel.FlushSettingsAsync();
        Assert.Equal(2, SettingsStore.Load(fixture.ConfigDirectory).RecentRepos.Count);
    }

    /// <summary>
    ///     Test that coloring a group whose background write fails colors the header and the tiles of its
    ///     uncolored repos, and reports the failure.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_SetGroupColorSaveFails_KeptAndReported()
    {
        // Arrange
        var fixture = CreateFixture(("alpha", "Work"), ("beta", null));
        var alpha = fixture.Card("alpha");
        var tileBefore = alpha.AvatarStyleClass;
        fixture.Saving.Fail = true;

        // Act
        var colored = fixture.ViewModel.SetGroupColor("Work", "Teal");
        await fixture.ViewModel.FlushSettingsAsync();

        // Assert
        Assert.True(colored);
        Assert.Equal("Teal", fixture.ViewModel.Settings.FindGroupColor("Work"));
        Assert.NotEqual(tileBefore, alpha.AvatarStyleClass);
        Assert.True(fixture.ViewModel.DisplayedItems.OfType<RepoGroupHeaderViewModel>().Single(header => header.Name == "Work").HasColor);
        Assert.Single(fixture.Failures);
    }

    /// <summary>
    ///     Test that when the save of a multi-folder add fails, nothing at all is added (the whole batch
    ///     is saved once, all or nothing) and every folder that would have been added is reported as not
    ///     saved, with the reason.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_AddRepos_SaveFails_NothingAddedAndEveryFolderReportedNotSaved()
    {
        // Arrange: two new folders and writes that fail
        var fixture = CreateFixture("alpha");
        var first = CreateDirectory("gamma");
        var second = CreateDirectory("delta");
        var shownBefore = Describe(fixture.ViewModel);
        fixture.Saving.Fail = true;

        // Act
        var outcomes = fixture.ViewModel.AddRepos([first, second], null);

        // Assert: both reported as not saved (and why), nothing added anywhere
        Assert.Equal([AddRepoResult.SaveFailed, AddRepoResult.SaveFailed], outcomes.Select(outcome => outcome.Result));
        Assert.All(outcomes, outcome => Assert.False(string.IsNullOrWhiteSpace(outcome.Problem)));
        Assert.Equal(["alpha"], fixture.ViewModel.RepoCards.Select(card => card.RepoName));
        Assert.Single(fixture.Settings.RecentRepos);
        Assert.Equal(shownBefore, Describe(fixture.ViewModel));

        // Act: saving works again, so the same folders can be added
        fixture.Saving.Fail = false;
        outcomes = fixture.ViewModel.AddRepos([first, second], null);

        // Assert
        Assert.Equal([AddRepoResult.Added, AddRepoResult.Added], outcomes.Select(outcome => outcome.Result));
        await fixture.ViewModel.FlushSettingsAsync();
        Assert.Equal(3, SettingsStore.Load(fixture.ConfigDirectory).RecentRepos.Count);
    }

    /// <summary>
    ///     Test that when the save at the end of a background add fails, the user is asked, and Retry
    ///     (once saving works again) adds the whole batch with one more save.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_EnqueueAddRepos_SaveFailsThenRetry_AddsTheBatch()
    {
        // Arrange: saving fails until the user answers Retry
        var fixture = CreateFixture("alpha");
        var first = CreateDirectory("gamma");
        var second = CreateDirectory("delta");
        var questions = new List<string>();
        fixture.ViewModel.AddSaveFailed += (_, request) =>
        {
            questions.Add(request.Message);
            fixture.Saving.Fail = false;
            request.Respond(true);
        };
        fixture.Saving.Fail = true;

        // Act
        var result = await fixture.ViewModel.EnqueueAddReposAsync([first, second], null);

        // Assert: asked once (saying why), then both added and saved
        Assert.False(result.Canceled);
        var question = Assert.Single(questions);
        Assert.Contains("could not be saved", question, StringComparison.Ordinal);
        Assert.Contains("The disk is full.", question, StringComparison.Ordinal);
        Assert.Equal([AddRepoResult.Added, AddRepoResult.Added], result.Outcomes.Select(outcome => outcome.Result));
        Assert.Equal(["alpha", "gamma", "delta"], fixture.ViewModel.RepoCards.Select(card => card.RepoName));
        Assert.Equal(3, SettingsStore.Load(fixture.ConfigDirectory).RecentRepos.Count);
        Assert.Equal(1, fixture.Saving.Saves);
        Assert.False(fixture.ViewModel.GlobalProgress.IsActive);
    }

    /// <summary>
    ///     Test that a Retry whose save fails again asks again, and keeps asking until a save succeeds:
    ///     the batch is then added once, with one successful save.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_EnqueueAddRepos_RetryFailsAgain_AsksAgainUntilSaved()
    {
        // Arrange: the save fails for the first two attempts; the user answers Retry each time
        var fixture = CreateFixture("alpha");
        var newRepo = CreateDirectory("gamma");
        var asked = 0;
        fixture.ViewModel.AddSaveFailed += (_, request) =>
        {
            asked++;
            fixture.Saving.Fail = asked < 2;
            request.Respond(true);
        };
        fixture.Saving.Fail = true;

        // Act
        var result = await fixture.ViewModel.EnqueueAddReposAsync([newRepo], null);

        // Assert: asked twice (the first Retry failed too), then added once and saved once
        Assert.Equal(2, asked);
        Assert.Equal(AddRepoResult.Added, Assert.Single(result.Outcomes).Result);
        Assert.Equal(["alpha", "gamma"], fixture.ViewModel.RepoCards.Select(card => card.RepoName));
        Assert.Equal(2, SettingsStore.Load(fixture.ConfigDirectory).RecentRepos.Count);
        Assert.Equal(1, fixture.Saving.Saves);
        Assert.Empty(fixture.Failures);
    }

    /// <summary>
    ///     Test that Discard after a failed save leaves memory, the cards, the displayed list and the
    ///     file as they were before the add - including a listed repo the add would have moved into a
    ///     collapsed group (its group, its place and the collapsed state are unchanged).
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_EnqueueAddRepos_SaveFailsThenDiscard_LeavesEverythingAsBeforeTheAddIncludingMovedRepos()
    {
        // Arrange: a collapsed "Work" group, a listed repo outside it, and a new folder; the collapse is
        // written so the file can be compared afterwards
        var fixture = CreateFixture(("alpha", "Work"), ("beta", null), ("omega", null));
        await fixture.ViewModel.WhenRepoStateSettledAsync();
        fixture.ViewModel.ToggleGroupCollapsed("Work");
        await fixture.ViewModel.FlushSettingsAsync();
        var beta = fixture.Card("beta");
        var newRepo = CreateDirectory("gamma");
        var fileBefore = await File.ReadAllTextAsync(fixture.SettingsPath, TestContext.Current.CancellationToken);
        var orderBefore = fixture.Settings.RecentRepos.Select(repo => repo.Path).ToList();
        var shownBefore = Describe(fixture.ViewModel);
        var asked = 0;
        fixture.ViewModel.AddSaveFailed += (_, request) =>
        {
            asked++;
            request.Respond(false);
        };
        fixture.Saving.Fail = true;

        // Act: add the listed repo and the new folder to the collapsed group, then Discard
        var result = await fixture.ViewModel.EnqueueAddReposAsync([beta.RepoPath, newRepo], "Work");
        fixture.Saving.Fail = false;
        await fixture.ViewModel.FlushSettingsAsync();

        // Assert: reported as not saved, and nothing changed anywhere
        Assert.Equal(1, asked);
        Assert.Equal([AddRepoResult.SaveFailed, AddRepoResult.SaveFailed], result.Outcomes.Select(outcome => outcome.Result));
        Assert.Null(beta.GroupName);
        Assert.Null(fixture.Settings.RecentRepos.Single(repo => repo.Path == beta.RepoPath).GroupName);
        Assert.Equal(orderBefore, fixture.Settings.RecentRepos.Select(repo => repo.Path));
        Assert.True(fixture.ViewModel.IsGroupCollapsed("Work"));
        Assert.Equal(["alpha", "beta", "omega"], fixture.ViewModel.RepoCards.Select(card => card.RepoName));
        Assert.Equal(shownBefore, Describe(fixture.ViewModel));
        Assert.Equal(fileBefore, await File.ReadAllTextAsync(fixture.SettingsPath, TestContext.Current.CancellationToken));
        Assert.False(fixture.ViewModel.GlobalProgress.IsActive);
    }

    /// <summary>
    ///     Test that Discard after a failed add save keeps the changes the user made in the window
    ///     before the add (still waiting to be written when the add saved) and while being asked: both
    ///     are written, without the discarded add.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_EnqueueAddRepos_SaveFailsThenDiscard_KeepsChangesMadeBeforeAndDuringTheAdd()
    {
        // Arrange: a clock that never fires on its own, so a change waits to be written until a flush
        var fixture = CreateFixture(new AppSettings(), [("alpha", null), ("beta", null)], new FakeTimeProvider());
        var alpha = fixture.Card("alpha");
        var beta = fixture.Card("beta");
        var newRepo = CreateDirectory("gamma");
        fixture.ViewModel.AddSaveFailed += (_, request) =>
        {
            // A change made while the question is shown, then Discard
            Assert.True(beta.SetCardColor("Teal"));
            fixture.Saving.Fail = false;
            request.Respond(false);
        };

        // Act: a change that waits to be written, then an add whose save fails and is discarded
        alpha.IsFavorite = true;
        fixture.Saving.Fail = true;
        var result = await fixture.ViewModel.EnqueueAddReposAsync([newRepo], null);
        var written = await fixture.ViewModel.FlushSettingsAsync();

        // Assert: the add was discarded, both changes are kept and written
        Assert.Equal(AddRepoResult.SaveFailed, Assert.Single(result.Outcomes).Result);
        Assert.True(written);
        var saved = SettingsStore.Load(fixture.ConfigDirectory).RecentRepos;
        Assert.Equal([alpha.RepoPath, beta.RepoPath], saved.Select(repo => repo.Path));
        Assert.True(saved.Single(repo => repo.Path == alpha.RepoPath).IsFavorite);
        Assert.Equal("Teal", saved.Single(repo => repo.Path == beta.RepoPath).CardColor);
        Assert.True(alpha.IsFavorite);
        Assert.Equal("Teal", beta.CardColorName);
    }

    /// <summary>
    ///     Test that a change made in the window while an add is being saved is applied at once and then
    ///     written together with the add, so the file never loses the add to a snapshot taken before it.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_EnqueueAddRepos_ChangeWhileSaving_WrittenWithTheAdd()
    {
        // Arrange: writes held at a gate
        var fixture = CreateFixture("alpha");
        var alpha = fixture.Card("alpha");
        var newRepo = CreateDirectory("gamma");
        fixture.Saving.Gate.Reset();

        // Act: start the add, wait until its save is held, make a change, then let the save complete
        var add = fixture.ViewModel.EnqueueAddReposAsync([newRepo], null);
        await fixture.Saving.WaitForStartAsync();
        alpha.IsFavorite = true;
        Assert.True(alpha.IsFavorite);
        fixture.Saving.Gate.Set();
        var result = await add;
        await fixture.ViewModel.FlushSettingsAsync();

        // Assert: the add's save, then one write holding both the add and the change
        Assert.Equal(AddRepoResult.Added, Assert.Single(result.Outcomes).Result);
        var saved = SettingsStore.Load(fixture.ConfigDirectory).RecentRepos;
        Assert.Equal(2, saved.Count);
        Assert.True(saved.Single(repo => repo.Path == alpha.RepoPath).IsFavorite);
        Assert.Equal(2, fixture.Saving.Saves);
    }

    /// <summary>
    ///     Test that closing the window while an add is being saved, after another change was made, writes
    ///     both: the change made meanwhile is written together with the add once the add's write
    ///     succeeded, rather than over it without the add. Runs on the headless UI thread, so - as in the
    ///     application - the add's own completion cannot run while the close blocks that thread.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_Dispose_WhileAddSavingAfterAnotherChange_WritesAddAndChange() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: writes held at a gate
        var fixture = CreateFixture("alpha");
        var alpha = fixture.Card("alpha");
        var gamma = CreateDirectory("gamma");
        fixture.Saving.Gate.Reset();

        // Act: start the add and wait until its save is held, make a change (deferred while the add
        // saves), then let the save go and close in the same UI-thread turn
        var add = fixture.ViewModel.EnqueueAddReposAsync([gamma], null);
        await fixture.Saving.WaitForStartAsync();
        alpha.IsFavorite = true;
        fixture.Saving.Gate.Set();
        fixture.ViewModel.Dispose();

        // Assert: the file holds both the add and the change
        var saved = SettingsStore.Load(fixture.ConfigDirectory).RecentRepos;
        Assert.Equal(2, saved.Count);
        Assert.Contains(saved, repo => repo.Path == gamma);
        Assert.True(saved.Single(repo => repo.Path == alpha.RepoPath).IsFavorite);

        // The add's completion was queued on the UI thread and is dropped with the test's application
        _ = add;
    });

    /// <summary>
    ///     Test that a failed save with nobody to ask (no subscriber to the question) discards the add.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_EnqueueAddRepos_SaveFailsWithNoOneToAsk_Discards()
    {
        // Arrange
        var fixture = CreateFixture("alpha");
        var newRepo = CreateDirectory("gamma");
        fixture.Saving.Fail = true;

        // Act
        var result = await fixture.ViewModel.EnqueueAddReposAsync([newRepo], null);

        // Assert
        var outcome = Assert.Single(result.Outcomes);
        Assert.Equal(AddRepoResult.SaveFailed, outcome.Result);
        Assert.Contains("The disk is full.", outcome.Problem, StringComparison.Ordinal);
        Assert.Single(fixture.ViewModel.RepoCards);
        Assert.Single(fixture.Settings.RecentRepos);
    }

    /// <summary>
    ///     Test that removing a repo whose background write fails removes it from the list at once (and
    ///     retires its card), reports the failure, and that the next change writes the removal.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_RemoveRepoSaveFails_RemovedReportedAndSavedWithNextChange()
    {
        // Arrange: both repos written, then writes that fail
        var fixture = CreateFixture("alpha", "beta");
        var alpha = fixture.Card("alpha");
        var beta = fixture.Card("beta");
        beta.IsFavorite = true;
        await fixture.ViewModel.FlushSettingsAsync();
        fixture.Saving.Fail = true;

        // Act
        fixture.ViewModel.RemoveRepo(alpha);
        await fixture.ViewModel.FlushSettingsAsync();

        // Assert: removed in memory, still in the file, reported
        Assert.DoesNotContain(alpha, fixture.ViewModel.RepoCards);
        Assert.Single(fixture.Settings.RecentRepos);
        Assert.Equal(2, SettingsStore.Load(fixture.ConfigDirectory).RecentRepos.Count);
        Assert.Single(fixture.Failures);

        // Act: writing works again and the next change is made
        fixture.Saving.Fail = false;
        beta.IsFavorite = false;
        await fixture.ViewModel.FlushSettingsAsync();

        // Assert: the file no longer lists the removed repo
        var saved = Assert.Single(SettingsStore.Load(fixture.ConfigDirectory).RecentRepos);
        Assert.Equal(beta.RepoPath, saved.Path);
        Assert.False(saved.IsFavorite);
    }

    /// <summary>
    ///     Test that a removed repo's card is retired: after the removal it can no longer report
    ///     anything to the user (here: an error that the same action raised before the removal).
    /// </summary>
    [Fact]
    public void MainWindowViewModel_RemoveRepo_RetiresTheCard()
    {
        // Arrange: a card whose select-package action reports an error (no package source configured)
        var fixture = CreateFixture("alpha", "beta");
        var alpha = fixture.Card("alpha");
        var errors = new List<string>();
        alpha.ErrorOccurred += (_, message) => errors.Add(message);
        alpha.SelectPackageCommand.Execute(null);
        Assert.Single(errors);

        // Act: the repo is removed, then the same action is tried again
        fixture.ViewModel.RemoveRepo(alpha);
        alpha.SelectPackageCommand.Execute(null);

        // Assert: nothing was reported for the removed card
        Assert.Single(errors);
        Assert.DoesNotContain(alpha, fixture.ViewModel.RepoCards);
    }

    /// <summary>
    ///     Test that every change is written to the file as well as applied (the success side of the
    ///     failure tests above).
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_ChangesThatSave_AreAppliedAndWritten()
    {
        // Arrange
        var fixture = CreateFixture("alpha", "beta");
        var alpha = fixture.Card("alpha");

        // Act
        alpha.IsFavorite = true;
        Assert.True(alpha.MoveToGroup("Work"));
        Assert.True(alpha.SetCardColor("teal"));
        await fixture.ViewModel.FlushSettingsAsync();

        // Assert
        var saved = SettingsStore.Load(fixture.ConfigDirectory).RecentRepos.Single(repo => repo.Path == alpha.RepoPath);
        Assert.True(saved.IsFavorite);
        Assert.Equal("Work", saved.GroupName);
        Assert.Equal("Teal", saved.CardColor);
        Assert.Empty(fixture.Failures);
    }

    /// <summary>
    ///     Test that applying the Settings window's settings does not wait for the settings file: the
    ///     returned task is pending while the write is held, nothing is adopted yet, and the settings are
    ///     adopted once the write completed.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_ApplySettingsAsync_SaveHeld_ReturnsPendingAndAdoptsOnceSaved()
    {
        // Arrange: writes held at a gate
        var fixture = CreateFixture("alpha");
        fixture.Saving.Gate.Reset();

        // Act: apply new settings; the write starts and waits at the gate
        var apply = fixture.ViewModel.ApplySettingsAsync(new AppSettings { GitExecutablePath = "new-git" });
        await fixture.Saving.WaitForStartAsync();

        // Assert: pending, nothing adopted
        Assert.False(apply.IsCompleted);
        Assert.Same(fixture.Settings, fixture.ViewModel.Settings);
        Assert.Null(fixture.ViewModel.Settings.GitExecutablePath);

        // Act: the write can complete
        fixture.Saving.Gate.Set();
        await apply;

        // Assert: adopted (the repos kept) and written
        Assert.Equal("new-git", fixture.ViewModel.Settings.GitExecutablePath);
        Assert.Single(fixture.ViewModel.Settings.RecentRepos);
        Assert.Equal("new-git", SettingsStore.Load(fixture.ConfigDirectory).GitExecutablePath);
    }

    /// <summary>
    ///     Test that a change made in the window while the Settings window's settings are being saved is
    ///     applied at once, kept when the new settings are adopted, and written together with them.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_ApplySettingsAsync_ChangeDuringSave_KeptAndWrittenAfterFlush()
    {
        // Arrange: writes held at a gate
        var fixture = CreateFixture("alpha");
        var alpha = fixture.Card("alpha");
        fixture.Saving.Gate.Reset();

        // Act: apply new settings, make a change while their write is held, then let it complete
        var apply = fixture.ViewModel.ApplySettingsAsync(new AppSettings { GitExecutablePath = "new-git" });
        await fixture.Saving.WaitForStartAsync();
        alpha.IsFavorite = true;
        fixture.Saving.Gate.Set();
        await apply;
        await fixture.ViewModel.FlushSettingsAsync();

        // Assert: the change is kept in memory and the file holds both
        Assert.True(alpha.IsFavorite);
        Assert.True(fixture.ViewModel.Settings.RecentRepos.Single().IsFavorite);
        var saved = SettingsStore.Load(fixture.ConfigDirectory);
        Assert.Equal("new-git", saved.GitExecutablePath);
        Assert.True(saved.RecentRepos.Single().IsFavorite);
    }

    /// <summary>
    ///     Test that when the Settings window's settings cannot be saved nothing is adopted - not the
    ///     values, not the theme - and the failure reaches the caller.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_ApplySettingsAsync_SaveFails_NothingAdoptedAndThrows()
    {
        // Arrange: writes that fail, and a recording theme applier
        var applied = new List<ThemePreference>();
        var fixture = CreateFixture(new AppSettings(), [("alpha", null)], applyTheme: applied.Add);
        fixture.Saving.Fail = true;

        // Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.ViewModel.ApplySettingsAsync(new AppSettings { GitExecutablePath = "new-git", Theme = ThemePreference.Dark }));

        // Assert
        Assert.Contains("The disk is full.", ex.Message, StringComparison.Ordinal);
        Assert.Same(fixture.Settings, fixture.ViewModel.Settings);
        Assert.Null(fixture.ViewModel.Settings.GitExecutablePath);
        Assert.Empty(applied);
        Assert.Empty(fixture.Failures);
    }

    /// <summary>
    ///     Test that closing the window writes a change that was still waiting to be written, and that
    ///     the close does it without a blocking call on the closing thread (the write runs on a worker).
    /// </summary>
    [Fact]
    public void MainWindowViewModel_Dispose_OnGuardedThread_WritesPendingChangeWithoutBlockingCall()
    {
        // Arrange: a clock that never fires on its own, so the change waits to be written
        var fixture = CreateFixture(new AppSettings(), [("alpha", null)], new FakeTimeProvider());
        var alpha = fixture.Card("alpha");
        alpha.IsFavorite = true;
        Assert.False(File.Exists(fixture.SettingsPath));

        // Act: close on a thread that refuses blocking calls
        using (BlockingCallGuard.GuardCurrentThread())
        {
            fixture.ViewModel.Dispose();
        }

        // Assert: no blocking call was made here, and the change is in the file
        Assert.Empty(BlockingCallGuard.TakeViolations());
        Assert.True(SettingsStore.Load(fixture.ConfigDirectory).RecentRepos.Single().IsFavorite);
    }

    /// <summary>
    ///     Describes the displayed items as text, to compare the list before and after a change.
    /// </summary>
    /// <param name="viewModel">The view model.</param>
    /// <returns>One entry per displayed item: a header's name, or a card's repo name.</returns>
    private static List<string> Describe(MainWindowViewModel viewModel) =>
        [.. viewModel.DisplayedItems.Select(item => item switch
        {
            RepoGroupHeaderViewModel header => $"[{header.Name}]",
            RepoCardViewModel card => card.RepoName,
            _ => throw new InvalidOperationException("Unexpected list item.")
        })];

    /// <summary>
    ///     Creates a fixture over repos with the given names and no group.
    /// </summary>
    /// <param name="names">The repo folder names.</param>
    /// <returns>The fixture.</returns>
    private Fixture CreateFixture(params string[] names) =>
        CreateFixture(new AppSettings(), [.. names.Select(name => (name, (string?)null))]);

    /// <summary>
    ///     Creates a fixture over repos in the given groups.
    /// </summary>
    /// <param name="repos">The repo folder names and their groups.</param>
    /// <returns>The fixture.</returns>
    private Fixture CreateFixture(params (string Name, string? Group)[] repos) =>
        CreateFixture(new AppSettings(), repos);

    /// <summary>
    ///     Creates a fixture over repos with the given names and no group, and the given settings.
    /// </summary>
    /// <param name="settings">The settings to start from (without repos).</param>
    /// <param name="names">The repo folder names.</param>
    /// <returns>The fixture.</returns>
    private Fixture CreateFixture(AppSettings settings, params string[] names) =>
        CreateFixture(settings, [.. names.Select(name => (name, (string?)null))]);

    /// <summary>
    ///     Creates a fixture.
    /// </summary>
    /// <remarks>
    ///     The view model is created with no synchronization context current, so a failed background
    ///     write is reported on the writing thread before the flush that made it completes.
    /// </remarks>
    /// <param name="settings">The settings to start from; its repo list is replaced.</param>
    /// <param name="repos">The repo folder names and their groups.</param>
    /// <param name="timeProvider">The clock of the view model (and so of its settings writer), or
    ///     <see langword="null"/> for the system clock.</param>
    /// <param name="applyTheme">The theme applier, or <see langword="null"/> for none.</param>
    /// <returns>The fixture.</returns>
    private Fixture CreateFixture(
        AppSettings settings,
        (string Name, string? Group)[] repos,
        TimeProvider? timeProvider = null,
        Action<ThemePreference>? applyTheme = null)
    {
        var configDirectory = CreateDirectory("config");
        settings.RecentRepos =
        [
            .. repos.Select(repo => new RecentRepo { Path = CreateDirectory(repo.Name), GroupName = repo.Group })
        ];
        var starter = new FakeAgentToolProcessStarter();
        var saving = new SaveControl(TestContext.Current.CancellationToken);
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        MainWindowViewModel viewModel;
        try
        {
            viewModel = new MainWindowViewModel(
                settings, configDirectory: configDirectory,
                applyTheme: applyTheme,
                timeProvider: timeProvider,
                // Launch-safe by construction: a fixture never starts a real process
                processStarter: starter, shellDetector: FakeAgentToolProcessStarter.CreateShellDetector(),
                saveSettings: snapshot => saving.Save(snapshot, configDirectory));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        _viewModels.Add(viewModel);
        var failures = new List<string>();
        viewModel.SettingsSaveFailed += (_, message) =>
        {
            lock (failures)
            {
                failures.Add(message);
            }
        };
        return new Fixture(viewModel, settings, configDirectory, failures, starter, saving);
    }

    /// <summary>
    ///     Creates a folder named <paramref name="name"/> under a unique temporary parent that is
    ///     removed when the test class is disposed.
    /// </summary>
    /// <param name="name">The folder name (also the display name of a repo).</param>
    /// <returns>The folder's path.</returns>
    private string CreateDirectory(string name)
    {
        var parent = Path.Combine(Path.GetTempPath(), "agentcontrol_persistence_" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(parent, name);
        Directory.CreateDirectory(path);
        Directory.CreateDirectory(Path.Combine(path, ".git"));
        File.WriteAllText(Path.Combine(path, ".git", "HEAD"), "ref: refs/heads/main\n");
        _tempPaths.Add(parent);
        return path;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Closed first, so no settings write is still running when the folders go; a write that fails
        // now has nobody to tell
        foreach (var viewModel in _viewModels)
        {
            viewModel.Dispose();
        }

        foreach (var path in _tempPaths.Where(Directory.Exists))
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A terminal started by a launch test may still have the folder as its working
                // directory; leaving a temporary folder behind is harmless
            }
        }
    }

    /// <summary>
    ///     The view model under test with what the tests need to observe and to control saving.
    /// </summary>
    /// <param name="ViewModel">The view model.</param>
    /// <param name="Settings">The live settings it was created over.</param>
    /// <param name="ConfigDirectory">The directory its settings are saved to.</param>
    /// <param name="Failures">The messages of <see cref="MainWindowViewModel.SettingsSaveFailed"/>.</param>
    /// <param name="Starter">The process starter every card launches through; it records launches
    ///     and starts nothing.</param>
    /// <param name="Saving">Counts the view model's writes and can hold them or make them fail without
    ///     touching the file.</param>
    private sealed record Fixture(
        MainWindowViewModel ViewModel, AppSettings Settings, string ConfigDirectory, List<string> Failures,
        FakeAgentToolProcessStarter Starter, SaveControl Saving)
    {
        /// <summary>
        ///     Gets the path of the settings file.
        /// </summary>
        public string SettingsPath => Path.Combine(ConfigDirectory, "settings.json");

        /// <summary>
        ///     Finds a repo's card by the repo's folder name.
        /// </summary>
        /// <param name="name">The folder name.</param>
        /// <returns>The card.</returns>
        public RepoCardViewModel Card(string name) => ViewModel.RepoCards.Single(card => card.RepoName == name);
    }

    /// <summary>
    ///     The save seam of a fixture's view model: writes the settings file like the application does,
    ///     counts the writes that succeeded, can hold every write at a gate, and can be made to fail
    ///     (leaving the file as it is). Called by the settings writer on a worker thread.
    /// </summary>
    /// <param name="cancellationToken">Ends a held write when the test is canceled, so a broken test
    ///     cannot hang the run.</param>
    private sealed class SaveControl(CancellationToken cancellationToken)
    {
        /// <summary>
        ///     Released once at the start of every write.
        /// </summary>
        private readonly SemaphoreSlim _started = new(0);

        /// <summary>
        ///     Backing field for <see cref="Fail"/>.
        /// </summary>
        private volatile bool _fail;

        /// <summary>
        ///     Backing field for <see cref="Saves"/>.
        /// </summary>
        private int _saves;

        /// <summary>
        ///     Gets the gate every write waits at; open unless a test closes it.
        /// </summary>
        public ManualResetEventSlim Gate { get; } = new(initialState: true);

        /// <summary>
        ///     Gets or sets whether every write fails.
        /// </summary>
        public bool Fail
        {
            get => _fail;
            set => _fail = value;
        }

        /// <summary>
        ///     Gets how many writes succeeded.
        /// </summary>
        public int Saves => Volatile.Read(ref _saves);

        /// <summary>
        ///     Waits until one more write has started (counting from the previous wait).
        /// </summary>
        /// <returns>A task that completes when a write started.</returns>
        public Task WaitForStartAsync() => _started.WaitAsync(cancellationToken);

        /// <summary>
        ///     Writes the settings, or fails like a full disk would.
        /// </summary>
        /// <param name="settings">The settings to write.</param>
        /// <param name="configDirectory">The directory to write them to.</param>
        /// <exception cref="InvalidOperationException">Thrown when <see cref="Fail"/> is set.</exception>
        public void Save(AppSettings settings, string configDirectory)
        {
            _started.Release();
            Gate.Wait(cancellationToken);
            if (Fail)
            {
                throw new InvalidOperationException("The disk is full.");
            }

            SettingsStore.Save(settings, configDirectory);
            Interlocked.Increment(ref _saves);
        }
    }
}
