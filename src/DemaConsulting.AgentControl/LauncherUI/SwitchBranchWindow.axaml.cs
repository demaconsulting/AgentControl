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
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     Modal "Switch branch" dialog: lists the branches a repo already knows and lets the user choose
///     one, via a <see cref="SwitchBranchViewModel"/>, mirroring <see cref="GroupNameWindow"/>'s
///     <see cref="Window.ShowDialog{TResult}(Window)"/> convention: the awaited result is the chosen
///     <see cref="BranchChoice"/>, or <see langword="null"/> when the user canceled.
/// </summary>
/// <remarks>
///     All state lives in <see cref="SwitchBranchViewModel"/>; this code-behind only starts the
///     (asynchronous, cancelable) reading of the branches when the window opens and stops it when the
///     window closes, closes the window with the result the view model reports, makes closing it by
///     any other means (the title-bar button, Escape) count as "Cancel", and turns a double-click on a
///     branch into a choice.
/// </remarks>
internal sealed partial class SwitchBranchWindow : Window
{
    /// <summary>
    ///     Cancels the reading of the branches when the window closes.
    /// </summary>
    private readonly CancellationTokenSource _loadCancellation = new();

    /// <summary>
    ///     Initializes a new <see cref="SwitchBranchWindow"/> with no bound view model; used by the
    ///     Avalonia XAML previewer/loader only. Production code should use
    ///     <see cref="SwitchBranchWindow(SwitchBranchViewModel)"/>.
    /// </summary>
    public SwitchBranchWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    ///     Initializes a new <see cref="SwitchBranchWindow"/> bound to the given view model.
    /// </summary>
    /// <param name="viewModel">The view model to bind and observe for the answer.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="viewModel"/> is
    ///     <see langword="null"/>.</exception>
    public SwitchBranchWindow(SwitchBranchViewModel viewModel) : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
        viewModel.Completed += (_, choice) => Close(choice);

        // However the window closes, an unanswered dialog counts as "cancelled", and a read that is
        // still running is given up
        Closed += (_, _) =>
        {
            _loadCancellation.Cancel();
            _loadCancellation.Dispose();
            viewModel.Cancel();
        };

        // Tab stays in the dialog, the filter box has the (visible) focus, and Escape cancels
        DialogBehavior.Apply(this, "SwitchBranchFilterTextBox", viewModel.Cancel);
        Opened += async (_, _) => await LoadBranchesAsync(viewModel);
    }

    /// <summary>
    ///     Starts reading the branches once the window is showing; the window stays responsive
    ///     meanwhile and a failure is reported by the view model as text.
    /// </summary>
    /// <param name="viewModel">The view model to load.</param>
    /// <returns>A task that completes when the branches are shown or the window was closed.</returns>
    private async Task LoadBranchesAsync(SwitchBranchViewModel viewModel)
    {
        try
        {
            await viewModel.LoadAsync(_loadCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // The window was closed before the branches were read; nothing is left to show
        }
    }

    /// <summary>
    ///     Chooses the branch that was double-clicked.
    /// </summary>
    /// <param name="sender">The branch list.</param>
    /// <param name="e">The event arguments (unused).</param>
    private void BranchList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        // The row under the pointer is the selected one: the first tap of the double-click selected it
        if (DataContext is SwitchBranchViewModel { SelectedItem: { } item } viewModel)
        {
            viewModel.Confirm(item);
        }
    }
}
