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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using DemaConsulting.AgentControl.GitIntegration;
using DemaConsulting.AgentControl.LauncherUI;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Headless tests of the "Switch branch" dialog (<see cref="SwitchBranchWindow"/>) as the user meets
///     it: opening, the list, the filter box, choosing by button, Enter and double-click, and canceling.
/// </summary>
/// <remarks>
///     The branches come from a canned function and the dialog loads them through the thread-pool
///     runner, so the tests wait (running the UI thread's jobs) until the load has ended. The rules
///     themselves are tested in <see cref="SwitchBranchViewModelTests"/>.
/// </remarks>
public sealed class SwitchBranchWindowTests
{
    /// <summary>
    ///     A repo on <c>main</c> with another local branch and two remote branches.
    /// </summary>
    private static readonly GitBranchList Branches = new(
        "main",
        null,
        ["feature/login", "main"],
        [new GitRemoteBranch("origin", "feature/billing", "origin/feature/billing"), new GitRemoteBranch("origin", "release", "origin/release")]);

    /// <summary>
    ///     Test that the dialog lists the branches once loaded - local first, the current one marked and
    ///     the remote ones captioned with their remote - shows where the repo is and the hint, hides its
    ///     progress bar, gives the focus to the filter box and keeps Switch disabled until a branch other
    ///     than the current one is chosen.
    /// </summary>
    [Fact]
    public void SwitchBranchWindow_Opened_ListsBranchesAndFocusesFilterBox() => HeadlessTestApp.Run(() =>
    {
        // Arrange / Act
        var (window, viewModel) = OpenDialog(_ => Branches);

        try
        {
            // Assert: the list, the texts and the controls
            var list = window.FindControl<ListBox>("SwitchBranchList")!;
            Assert.Equal(
                ["feature/login", "main", "feature/billing", "release"],
                list.Items.OfType<BranchItemViewModel>().Select(item => item.Name));
            Assert.Equal("Currently on main", window.FindControl<TextBlock>("SwitchBranchCurrentText")!.Text);
            Assert.Contains("Git Pull", window.FindControl<TextBlock>("SwitchBranchHintText")!.Text, StringComparison.Ordinal);
            Assert.False(window.FindControl<ProgressBar>("SwitchBranchProgress")!.IsVisible);
            Assert.False(window.FindControl<TextBlock>("SwitchBranchErrorText")!.IsVisible);
            Assert.Equal("Switch branch - Agents", window.Title);
            Assert.Same(window.FindControl<TextBox>("SwitchBranchFilterTextBox"), window.FocusManager.GetFocusedElement());
            Assert.False(window.FindControl<Button>("SwitchBranchAcceptButton")!.IsEffectivelyEnabled);
            Assert.True(window.FindControl<Button>("SwitchBranchCancelButton")!.IsEffectivelyEnabled);

            // Act: the current branch, then another one
            list.SelectedItem = viewModel.Items.Single(item => item.IsCurrent);
            Dispatcher.UIThread.RunJobs();
            var enabledOnCurrent = window.FindControl<Button>("SwitchBranchAcceptButton")!.IsEffectivelyEnabled;
            list.SelectedItem = viewModel.Items[0];
            Dispatcher.UIThread.RunJobs();

            // Assert
            Assert.False(enabledOnCurrent);
            Assert.True(window.FindControl<Button>("SwitchBranchAcceptButton")!.IsEffectivelyEnabled);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that typing in the filter box narrows the list, and that a filter that leaves one branch
    ///     lets Enter in the box choose it.
    /// </summary>
    [Fact]
    public void SwitchBranchWindow_FilterThenEnter_ChoosesTheOnlyMatchingBranch() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var owner = new Window();
        owner.Show();
        try
        {
            var viewModel = new SwitchBranchViewModel("Agents", _ => Branches);
            var dialog = new SwitchBranchWindow(viewModel);
            var result = dialog.ShowDialog<BranchChoice?>(owner);
            HeadlessTestApp.RunJobsUntil(() => !viewModel.IsLoading, "The branches were not loaded.");

            // Act: type a filter that leaves two branches, then one
            var filter = dialog.FindControl<TextBox>("SwitchBranchFilterTextBox")!;
            filter.Text = "feature";
            Dispatcher.UIThread.RunJobs();
            var afterFeature = dialog.FindControl<ListBox>("SwitchBranchList")!.Items.OfType<BranchItemViewModel>().Select(item => item.Name).ToList();
            filter.Text = "billing";
            Dispatcher.UIThread.RunJobs();
            filter.Focus(NavigationMethod.Tab);
            dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

            // Assert
            Assert.Equal(["feature/login", "feature/billing"], afterFeature);
            Assert.Equal(new BranchChoice("feature/billing", "origin/feature/billing", true), await result);
        }
        finally
        {
            owner.Close();
        }
    });

    /// <summary>
    ///     Test that the Switch button closes the dialog with the selected branch.
    /// </summary>
    [Fact]
    public void SwitchBranchWindow_SwitchClicked_ReturnsTheSelectedBranch() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var owner = new Window();
        owner.Show();
        try
        {
            var viewModel = new SwitchBranchViewModel("Agents", _ => Branches);
            var dialog = new SwitchBranchWindow(viewModel);
            var result = dialog.ShowDialog<BranchChoice?>(owner);
            HeadlessTestApp.RunJobsUntil(() => !viewModel.IsLoading, "The branches were not loaded.");
            dialog.FindControl<ListBox>("SwitchBranchList")!.SelectedItem = viewModel.Items[0];
            Dispatcher.UIThread.RunJobs();

            // Act
            DialogWindowTests.Click(dialog, "SwitchBranchAcceptButton");

            // Assert
            Assert.Equal(new BranchChoice("feature/login", "feature/login", false), await result);
        }
        finally
        {
            owner.Close();
        }
    });

    /// <summary>
    ///     Test that a double-click on a branch chooses it at once.
    /// </summary>
    [Fact]
    public void SwitchBranchWindow_DoubleClickOnBranch_ReturnsIt() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var owner = new Window();
        owner.Show();
        try
        {
            var viewModel = new SwitchBranchViewModel("Agents", _ => Branches);
            var dialog = new SwitchBranchWindow(viewModel);
            var result = dialog.ShowDialog<BranchChoice?>(owner);
            HeadlessTestApp.RunJobsUntil(() => !viewModel.IsLoading, "The branches were not loaded.");
            var list = dialog.FindControl<ListBox>("SwitchBranchList")!;
            list.SelectedItem = viewModel.Items[3];
            Dispatcher.UIThread.RunJobs();

            // Act: two quick clicks on the selected row; the second press is the double-click, which
            // closes the dialog
            var row = list.ContainerFromIndex(3)!;
            var point = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), dialog)!.Value;
            dialog.MouseDown(point, Avalonia.Input.MouseButton.Left);
            dialog.MouseUp(point, Avalonia.Input.MouseButton.Left);
            dialog.MouseDown(point, Avalonia.Input.MouseButton.Left);

            // Assert
            Assert.Equal(new BranchChoice("release", "origin/release", true), await result);
        }
        finally
        {
            owner.Close();
        }
    });

    /// <summary>
    ///     Test that Escape and the Cancel button close the dialog with no branch.
    /// </summary>
    [Fact]
    public void SwitchBranchWindow_EscapeAndCancel_ReturnNull() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var owner = new Window();
        owner.Show();
        try
        {
            var first = new SwitchBranchWindow(new SwitchBranchViewModel("Agents", _ => Branches));
            var escaped = first.ShowDialog<BranchChoice?>(owner);
            Dispatcher.UIThread.RunJobs();

            // Act: Escape
            first.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

            // Assert
            Assert.Null(await escaped);

            // Act: the Cancel button of another dialog
            var second = new SwitchBranchWindow(new SwitchBranchViewModel("Agents", _ => Branches));
            var canceled = second.ShowDialog<BranchChoice?>(owner);
            Dispatcher.UIThread.RunJobs();
            DialogWindowTests.Click(second, "SwitchBranchCancelButton");

            // Assert
            Assert.Null(await canceled);
        }
        finally
        {
            owner.Close();
        }
    });

    /// <summary>
    ///     Test that closing the window while the branches are still being read gives the read up and
    ///     returns no branch.
    /// </summary>
    [Fact]
    public void SwitchBranchWindow_ClosedWhileLoading_ReturnsNullAndStopsTheRead() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: a read that waits for its token
        var owner = new Window();
        owner.Show();
        try
        {
            var readStarted = new TaskCompletionSource();
            var viewModel = new SwitchBranchViewModel(
                "Agents",
                token =>
                {
                    readStarted.TrySetResult();
                    token.WaitHandle.WaitOne(TimeSpan.FromSeconds(30));
                    token.ThrowIfCancellationRequested();
                    return Branches;
                });
            var dialog = new SwitchBranchWindow(viewModel);
            var result = dialog.ShowDialog<BranchChoice?>(owner);
            await readStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

            // Act
            var showedProgress = dialog.FindControl<ProgressBar>("SwitchBranchProgress")!.IsVisible;
            dialog.Close();

            // Assert
            Assert.True(showedProgress);
            Assert.Null(await result);
        }
        finally
        {
            owner.Close();
        }
    });

    /// <summary>
    ///     Test that a failing read is shown as text in place of the list, and that nothing can be
    ///     switched to.
    /// </summary>
    [Fact]
    public void SwitchBranchWindow_ReadFails_ShowsErrorAndDisablesSwitch() => HeadlessTestApp.Run(() =>
    {
        // Arrange / Act
        var (window, _) = OpenDialog(_ => throw new InvalidOperationException("fatal: not a git repository"));

        try
        {
            // Assert
            var error = window.FindControl<TextBlock>("SwitchBranchErrorText")!;
            Assert.True(error.IsVisible);
            Assert.Contains("fatal: not a git repository", error.Text, StringComparison.Ordinal);
            Assert.False(window.FindControl<Button>("SwitchBranchAcceptButton")!.IsEffectivelyEnabled);
            Assert.Empty(window.FindControl<ListBox>("SwitchBranchList")!.Items);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that the dialog's constructors refuse a missing view model and that the parameterless
    ///     previewer constructor works.
    /// </summary>
    [Fact]
    public void SwitchBranchWindow_Constructors_NullViewModelThrows_ParameterlessWorks() => HeadlessTestApp.Run(() =>
    {
        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => new SwitchBranchWindow(null!));
        new SwitchBranchWindow().Close();
    });

    /// <summary>
    ///     Opens the dialog non-modally and runs the UI thread until the branches are loaded.
    /// </summary>
    /// <param name="loadBranches">The function that reads the branches.</param>
    /// <returns>The window and its view model.</returns>
    private static (SwitchBranchWindow Window, SwitchBranchViewModel ViewModel) OpenDialog(
        Func<CancellationToken, GitBranchList> loadBranches)
    {
        var viewModel = new SwitchBranchViewModel("Agents", loadBranches);
        var window = new SwitchBranchWindow(viewModel);
        window.Show();
        HeadlessTestApp.RunJobsUntil(() => !viewModel.IsLoading, "The branches were not loaded.");
        return (window, viewModel);
    }
}
