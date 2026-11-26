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
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     Modal "Clone repository" dialog, via a <see cref="CloneRepositoryViewModel"/>, mirroring
///     <see cref="GroupNameWindow"/>'s <see cref="Window.ShowDialog{TResult}(Window)"/> convention: the
///     awaited result is the <see cref="CloneRepositoryRequest"/>, or <see langword="null"/> when the
///     user canceled.
/// </summary>
/// <remarks>
///     All state and validation live in <see cref="CloneRepositoryViewModel"/>; this code-behind only
///     closes the window with the result the view model reports, makes closing it by any other means
///     (the title-bar button, Escape) count as "Cancel", and answers the Browse button by asking the
///     user for a folder (through <see cref="FolderPicker"/>, a seam for tests).
/// </remarks>
internal sealed partial class CloneRepositoryWindow : Window
{
    /// <summary>
    ///     Initializes a new <see cref="CloneRepositoryWindow"/> with no bound view model; used by the
    ///     Avalonia XAML previewer/loader only. Production code should use
    ///     <see cref="CloneRepositoryWindow(CloneRepositoryViewModel)"/>.
    /// </summary>
    public CloneRepositoryWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    ///     Initializes a new <see cref="CloneRepositoryWindow"/> bound to the given view model.
    /// </summary>
    /// <param name="viewModel">The view model to bind and observe for the answer.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="viewModel"/> is
    ///     <see langword="null"/>.</exception>
    public CloneRepositoryWindow(CloneRepositoryViewModel viewModel) : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
        viewModel.Completed += (_, request) => Close(request);

        // However the window closes, an unanswered dialog counts as "cancelled"
        Closed += (_, _) => viewModel.Cancel();

        // Tab stays in the dialog, the address box has the (visible) focus, and Escape cancels
        DialogBehavior.Apply(this, "CloneUrlTextBox", viewModel.Cancel);
    }

    /// <summary>
    ///     Gets or sets how the user picks the parent folder when Browse is pressed; <see langword="null"/>
    ///     uses the native folder-browse dialog. The function completes with the chosen folder's path, or
    ///     <see langword="null"/> when the user canceled. A seam so tests can answer without a real
    ///     dialog, like <see cref="MainWindow.RepoFolderPicker"/>.
    /// </summary>
    internal Func<Task<string?>>? FolderPicker { get; set; }

    /// <summary>
    ///     Lets the user choose the parent folder and puts it in the box; a canceled picker changes
    ///     nothing.
    /// </summary>
    /// <param name="sender">The Browse button.</param>
    /// <param name="e">The event arguments (unused).</param>
    private async void CloneBrowseButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CloneRepositoryViewModel viewModel)
        {
            return;
        }

        var folder = await (FolderPicker ?? BrowseForFolderAsync)();
        if (folder is not null)
        {
            viewModel.ParentFolder = folder;
        }
    }

    /// <summary>
    ///     Shows the native folder-browse dialog for a single folder.
    /// </summary>
    /// <returns>The chosen folder's local path, or <see langword="null"/> when the user canceled.</returns>
    private async Task<string?> BrowseForFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select the folder to clone into",
            AllowMultiple = false
        });

        return folders.Select(folder => folder.TryGetLocalPath()).OfType<string>().FirstOrDefault();
    }
}
