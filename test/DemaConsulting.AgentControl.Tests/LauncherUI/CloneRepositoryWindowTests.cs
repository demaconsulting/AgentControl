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

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using DemaConsulting.AgentControl.LauncherUI;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Headless tests of the "Clone repository" dialog (<see cref="CloneRepositoryWindow"/>) as the user
///     meets it: opening, typing the address, the folder name that follows it, Browse, the group box, the
///     error text, Clone by button and by Enter, and canceling.
/// </summary>
/// <remarks>
///     The destination check reads a real temporary folder through the thread-pool runner, so the tests
///     run the UI thread's jobs until it has ended. The rules themselves are tested in
///     <see cref="CloneRepositoryViewModelTests"/>.
/// </remarks>
public sealed class CloneRepositoryWindowTests : IDisposable
{
    /// <summary>
    ///     Folders to delete when the test ends.
    /// </summary>
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     Test that the dialog opens with the focus in the address box, the parent folder it was given,
    ///     Ungrouped selected, an empty folder name, no error and Clone disabled; and that typing an address
    ///     fills the folder name and the destination line and enables Clone.
    /// </summary>
    [Fact]
    public void CloneRepositoryWindow_Opened_FocusesAddressAndTypingAnAddressFillsFolderNameAndEnablesClone() => HeadlessTestApp.Run(() =>
    {
        // Arrange / Act
        var parent = CreateTempDirectory();
        var (window, viewModel) = OpenDialog(parent);

        try
        {
            // Assert: the initial state
            Assert.Equal("Clone repository", window.Title);
            Assert.Same(window.FindControl<TextBox>("CloneUrlTextBox"), window.FocusManager.GetFocusedElement());
            Assert.Equal(parent, window.FindControl<TextBox>("CloneParentTextBox")!.Text);
            Assert.Equal(string.Empty, window.FindControl<TextBox>("CloneFolderNameTextBox")!.Text ?? string.Empty);
            var groups = window.FindControl<ComboBox>("CloneGroupComboBox")!;
            Assert.Equal(["Ungrouped", "Work", "Work > Backend"], groups.Items.OfType<CloneGroupChoice>().Select(choice => choice.DisplayName));
            Assert.Equal("Ungrouped", Assert.IsType<CloneGroupChoice>(groups.SelectedItem).DisplayName);
            Assert.False(window.FindControl<TextBlock>("CloneErrorText")!.IsVisible);
            Assert.False(window.FindControl<ProgressBar>("CloneCheckProgress")!.IsVisible);
            Assert.False(window.FindControl<Button>("CloneAcceptButton")!.IsEffectivelyEnabled);
            Assert.True(window.FindControl<Button>("CloneCancelButton")!.IsEffectivelyEnabled);
            Assert.True(window.FindControl<Button>("CloneBrowseButton")!.IsEffectivelyEnabled);

            // Act: type an address
            window.FindControl<TextBox>("CloneUrlTextBox")!.Text = "https://example.com/team/tools.git";
            Dispatcher.UIThread.RunJobs();

            // Assert: the folder name follows, the destination is shown and Clone is available
            Assert.Equal("tools", window.FindControl<TextBox>("CloneFolderNameTextBox")!.Text);
            Assert.Equal($"Clones into: {Path.Combine(parent, "tools")}", window.FindControl<TextBlock>("CloneDestinationText")!.Text);
            Assert.True(window.FindControl<Button>("CloneAcceptButton")!.IsEffectivelyEnabled);
            Assert.False(viewModel.HasError);

            // Act: pick another group
            groups.SelectedIndex = 2;
            Dispatcher.UIThread.RunJobs();

            // Assert
            Assert.Equal("Backend", viewModel.SelectedGroup.GroupName);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that an address that looks like an option is shown as an error under the fields and keeps
    ///     Clone disabled.
    /// </summary>
    [Fact]
    public void CloneRepositoryWindow_UrlLooksLikeAnOption_ShowsErrorAndDisablesClone() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var (window, _) = OpenDialog(CreateTempDirectory());

        try
        {
            // Act
            window.FindControl<TextBox>("CloneUrlTextBox")!.Text = "--upload-pack=calc.exe";
            Dispatcher.UIThread.RunJobs();

            // Assert
            var error = window.FindControl<TextBlock>("CloneErrorText")!;
            Assert.True(error.IsVisible);
            Assert.Contains("dash", error.Text, StringComparison.Ordinal);
            Assert.False(window.FindControl<Button>("CloneAcceptButton")!.IsEffectivelyEnabled);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that Browse puts the folder the picker returns in the parent folder box, and that a canceled
    ///     picker changes nothing.
    /// </summary>
    [Fact]
    public void CloneRepositoryWindow_BrowseClicked_PutsChosenFolderInParentBoxAndCancelChangesNothing() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var original = CreateTempDirectory();
        var chosen = CreateTempDirectory();
        var (window, viewModel) = OpenDialog(original);

        try
        {
            // Act: the user picks a folder
            window.FolderPicker = () => Task.FromResult<string?>(chosen);
            DialogWindowTests.Click(window, "CloneBrowseButton");
            HeadlessTestApp.RunJobsUntil(() => viewModel.ParentFolder == chosen, "The folder was not taken over.");

            // Assert
            Assert.Equal(chosen, window.FindControl<TextBox>("CloneParentTextBox")!.Text);

            // Act: the user cancels the picker
            var asked = false;
            window.FolderPicker = () =>
            {
                asked = true;
                return Task.FromResult<string?>(null);
            };
            DialogWindowTests.Click(window, "CloneBrowseButton");
            HeadlessTestApp.RunJobsUntil(() => asked, "The picker was not asked.");
            Dispatcher.UIThread.RunJobs();

            // Assert
            Assert.Equal(chosen, viewModel.ParentFolder);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that the Clone button closes the dialog with the request: the address, the destination and
    ///     the chosen group.
    /// </summary>
    [Fact]
    public void CloneRepositoryWindow_CloneClicked_ReturnsTheRequest() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var parent = CreateTempDirectory();
        var owner = new Window();
        owner.Show();
        try
        {
            var viewModel = CreateViewModel(parent);
            var dialog = new CloneRepositoryWindow(viewModel);
            var result = dialog.ShowDialog<CloneRepositoryRequest?>(owner);
            Dispatcher.UIThread.RunJobs();
            dialog.FindControl<TextBox>("CloneUrlTextBox")!.Text = "https://example.com/team/tools.git";
            dialog.FindControl<ComboBox>("CloneGroupComboBox")!.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();

            // Act
            DialogWindowTests.Click(dialog, "CloneAcceptButton");
            HeadlessTestApp.RunJobsUntil(() => result.IsCompleted, "The dialog did not close.");

            // Assert
            Assert.Equal(
                new CloneRepositoryRequest("https://example.com/team/tools.git", Path.Combine(parent, "tools"), "Work"),
                await result);
        }
        finally
        {
            owner.Close();
        }
    });

    /// <summary>
    ///     Test that Enter in the address box clones too, and that a destination that already exists and is
    ///     not empty keeps the dialog open with the reason shown.
    /// </summary>
    [Fact]
    public void CloneRepositoryWindow_EnterInAddressBox_ClonesAndExistingFolderShowsReason() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: "tools" exists and is not empty
        var parent = CreateTempDirectory();
        var existing = Path.Combine(parent, "tools");
        Directory.CreateDirectory(existing);
        await File.WriteAllTextAsync(Path.Combine(existing, "keep.txt"), "mine", TestContext.Current.CancellationToken);
        var owner = new Window();
        owner.Show();
        try
        {
            var viewModel = CreateViewModel(parent);
            var dialog = new CloneRepositoryWindow(viewModel);
            var result = dialog.ShowDialog<CloneRepositoryRequest?>(owner);
            Dispatcher.UIThread.RunJobs();
            var url = dialog.FindControl<TextBox>("CloneUrlTextBox")!;
            url.Text = "https://example.com/team/tools.git";
            Dispatcher.UIThread.RunJobs();

            // Act: Enter with the existing folder
            url.Focus(NavigationMethod.Tab);
            dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            HeadlessTestApp.RunJobsUntil(() => viewModel.ErrorText is not null, "The folder was not checked.");

            // Assert: still open, with the reason
            Assert.False(result.IsCompleted);
            var error = dialog.FindControl<TextBlock>("CloneErrorText")!;
            Assert.True(error.IsVisible);
            Assert.Contains("already exists and is not empty", error.Text, StringComparison.Ordinal);

            // Act: another folder name, then Enter
            dialog.FindControl<TextBox>("CloneFolderNameTextBox")!.Text = "tools2";
            Dispatcher.UIThread.RunJobs();
            Assert.False(error.IsVisible);
            url.Focus(NavigationMethod.Tab);
            dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            HeadlessTestApp.RunJobsUntil(() => result.IsCompleted, "The dialog did not close.");

            // Assert
            Assert.Equal(Path.Combine(parent, "tools2"), (await result)!.DestinationPath);
            Assert.True(File.Exists(Path.Combine(existing, "keep.txt")));
        }
        finally
        {
            owner.Close();
        }
    });

    /// <summary>
    ///     Test that Escape and the Cancel button close the dialog with no request.
    /// </summary>
    [Fact]
    public void CloneRepositoryWindow_EscapeAndCancel_ReturnNull() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var owner = new Window();
        owner.Show();
        try
        {
            var first = new CloneRepositoryWindow(CreateViewModel(CreateTempDirectory()));
            var escaped = first.ShowDialog<CloneRepositoryRequest?>(owner);
            Dispatcher.UIThread.RunJobs();

            // Act: Escape
            first.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

            // Assert
            Assert.Null(await escaped);

            // Act: the Cancel button of another dialog
            var second = new CloneRepositoryWindow(CreateViewModel(CreateTempDirectory()));
            var canceled = second.ShowDialog<CloneRepositoryRequest?>(owner);
            Dispatcher.UIThread.RunJobs();
            DialogWindowTests.Click(second, "CloneCancelButton");

            // Assert
            Assert.Null(await canceled);
        }
        finally
        {
            owner.Close();
        }
    });

    /// <summary>
    ///     Test that the dialog's constructors refuse a missing view model and that the parameterless
    ///     previewer constructor works.
    /// </summary>
    [Fact]
    public void CloneRepositoryWindow_Constructors_NullViewModelThrows_ParameterlessWorks() => HeadlessTestApp.Run(() =>
    {
        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => new CloneRepositoryWindow(null!));
        new CloneRepositoryWindow().Close();
    });

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var path in _tempPaths.Where(Directory.Exists))
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; a leftover temporary folder does not fail the test
            }
        }
    }

    /// <summary>
    ///     Creates the dialog's view model with two groups.
    /// </summary>
    /// <param name="parent">The parent folder to start with.</param>
    /// <returns>The view model.</returns>
    private static CloneRepositoryViewModel CreateViewModel(string parent) =>
        new(
            [CloneGroupChoice.Ungrouped, new CloneGroupChoice("Work", "Work"), new CloneGroupChoice("Backend", "Work > Backend")],
            parent);

    /// <summary>
    ///     Opens the dialog non-modally.
    /// </summary>
    /// <param name="parent">The parent folder to start with.</param>
    /// <returns>The window and its view model.</returns>
    private static (CloneRepositoryWindow Window, CloneRepositoryViewModel ViewModel) OpenDialog(string parent)
    {
        var viewModel = CreateViewModel(parent);
        var window = new CloneRepositoryWindow(viewModel);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, viewModel);
    }

    /// <summary>
    ///     Creates a unique temporary folder, deleted when the test ends.
    /// </summary>
    /// <returns>The folder's path.</returns>
    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_clone_window_" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        return path;
    }
}
