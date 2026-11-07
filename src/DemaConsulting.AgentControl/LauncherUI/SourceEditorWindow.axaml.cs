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
using DemaConsulting.AgentControl.AgentPackageManagement;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     Modal dialog that adds or edits one zip-package folder or NuGet-package folder source via a
///     <see cref="SourceEditorViewModel"/>, mirroring <see cref="ConfirmationWindow"/>'s
///     <see cref="Window.ShowDialog{TResult}(Window)"/> convention.
/// </summary>
/// <remarks>
///     All validation and the resulting definition live in <see cref="SourceEditorViewModel"/>;
///     this code-behind only wires the folder-browse dialog (which requires a live
///     <see cref="IStorageProvider"/>), the "Cancel" button, and closes the window with the saved
///     <see cref="PackageSourceDefinition"/> once <see cref="SourceEditorViewModel.Saved"/> fires
///     (or with <see langword="null"/> when canceled).
/// </remarks>
internal sealed partial class SourceEditorWindow : Window
{
    /// <summary>
    ///     Initializes a new <see cref="SourceEditorWindow"/> with no bound view model; used by
    ///     the Avalonia XAML previewer/loader only. Production code should use
    ///     <see cref="SourceEditorWindow(SourceEditorViewModel)"/>.
    /// </summary>
    public SourceEditorWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    ///     Initializes a new <see cref="SourceEditorWindow"/> bound to the given view model,
    ///     closing itself with the saved definition once the view model reports a save.
    /// </summary>
    /// <param name="viewModel">The view model to bind and observe for the "Saved" event.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="viewModel"/> is
    ///     <see langword="null"/>.</exception>
    public SourceEditorWindow(SourceEditorViewModel viewModel) : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
        viewModel.Saved += (_, definition) => Close(definition);
    }

    /// <summary>
    ///     Opens a native folder-browse dialog and applies the chosen folder to
    ///     <see cref="SourceEditorViewModel.Location"/>.
    /// </summary>
    /// <param name="sender">The clicked "Browse..." button.</param>
    /// <param name="e">Routed event arguments (unused).</param>
    private async void BrowseButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SourceEditorViewModel viewModel)
        {
            return;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = viewModel.IsNuGet
                ? "Select the folder of NuGet packages"
                : "Select the folder of agent package zips",
            AllowMultiple = false
        });

        var folder = folders.Count > 0 ? folders[0] : null;
        var localPath = folder?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(localPath))
        {
            viewModel.Location = localPath;
        }
    }

    /// <summary>
    ///     Closes the window reporting that the user canceled the edit.
    /// </summary>
    /// <param name="sender">The clicked "Cancel" button.</param>
    /// <param name="e">Routed event arguments (unused).</param>
    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
