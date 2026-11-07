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
using Avalonia.Markup.Xaml;
using DemaConsulting.AgentControl.AgentPackageManagement;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     Modal settings window: edits the package sources, git executable override, agent-tool
///     selection, and shell preference via a <see cref="SettingsWindowViewModel"/>.
/// </summary>
/// <remarks>
///     All editable state and the save/round-trip logic live in
///     <see cref="SettingsWindowViewModel"/>; this code-behind only shows the dialogs the view
///     model asks for - the <see cref="SourceEditorWindow"/> that adds or edits a source and the
///     <see cref="ConfirmationWindow"/> that confirms a removal - cancels the view model's
///     folder checks when the window closes, and closes the window once
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
    /// <param name="viewModel">The view model to bind and observe.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="viewModel"/> is
    ///     <see langword="null"/>.</exception>
    public SettingsWindow(SettingsWindowViewModel viewModel) : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
        viewModel.Saved += (_, _) => Close();
        viewModel.EditorRequested += async (_, editor) => await ShowEditorAsync(editor);
        viewModel.RemoveRequested += async (_, source) => await ConfirmAndRemoveAsync(viewModel, source);
        Closed += (_, _) => viewModel.Dispose();
    }

    /// <summary>
    ///     Shows the modal dialog that adds or edits a source. The editor view model applies the
    ///     outcome itself when the user saves, so nothing is done with the dialog's result here.
    /// </summary>
    /// <param name="editor">The editor view model to show.</param>
    /// <returns>A task that completes when the dialog closes.</returns>
    private async Task ShowEditorAsync(SourceEditorViewModel editor)
    {
        await new SourceEditorWindow(editor).ShowDialog<PackageSourceDefinition?>(this);
    }

    /// <summary>
    ///     Asks the user to confirm removing a source (warning when repos are pinned to it) and
    ///     removes it only if they agree.
    /// </summary>
    /// <param name="viewModel">The settings view model that owns the source list.</param>
    /// <param name="source">The row the user asked to remove.</param>
    /// <returns>A task that completes when the user has answered and any removal is done.</returns>
    private async Task ConfirmAndRemoveAsync(SettingsWindowViewModel viewModel, PackageSourceItemViewModel source)
    {
        var confirmed = await new ConfirmationWindow(SettingsWindowViewModel.GetRemovalWarning(source)).ShowDialog<bool>(this);
        if (confirmed)
        {
            viewModel.RemoveSource(source);
        }
    }
}
