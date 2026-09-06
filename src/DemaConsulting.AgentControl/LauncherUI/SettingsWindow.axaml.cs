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
///     Modal settings window: edits the package-source path, git executable override, agent-tool
///     selection, and shell preference via a <see cref="SettingsWindowViewModel"/>.
/// </summary>
/// <remarks>
///     All editable state and the save/round-trip logic live in
///     <see cref="SettingsWindowViewModel"/>; this code-behind only wires the folder-browse
///     dialog (which requires a live <see cref="IStorageProvider"/>) and closes the window once
///     <see cref="SettingsWindowViewModel.Saved"/> fires.
/// </remarks>
internal sealed partial class SettingsWindow : Window
{
    /// <summary>
    ///     Initializes a new <see cref="SettingsWindow"/> with no bound view model; used by the
    ///     Avalonia XAML previewer/loader only. Production code should use
    ///     <see cref="SettingsWindow(SettingsWindowViewModel)"/>.
    /// </summary>
    public SettingsWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    ///     Initializes a new <see cref="SettingsWindow"/> bound to the given view model, closing
    ///     itself once the view model reports a successful save.
    /// </summary>
    /// <param name="viewModel">The view model to bind and observe for the "Saved" event.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="viewModel"/> is
    ///     <see langword="null"/>.</exception>
    public SettingsWindow(SettingsWindowViewModel viewModel) : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
        viewModel.Saved += (_, _) => Close();
    }

    /// <summary>
    ///     Opens a native folder-browse dialog and applies the chosen folder to
    ///     <see cref="SettingsWindowViewModel.PackageSourcePath"/>.
    /// </summary>
    /// <param name="sender">The clicked "Browse..." button.</param>
    /// <param name="e">Routed event arguments (unused).</param>
    private async void BrowsePackageSourceButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsWindowViewModel viewModel)
        {
            return;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select the agent package source folder",
            AllowMultiple = false
        });

        var folder = folders.Count > 0 ? folders[0] : null;
        var localPath = folder?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(localPath))
        {
            viewModel.PackageSourcePath = localPath;
        }
    }
}
