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

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     Modal prompt for a repo group's name (a new group, or a rename), via a
///     <see cref="GroupNameViewModel"/>, mirroring <see cref="CredentialPromptWindow"/>'s
///     <see cref="Window.ShowDialog{TResult}(Window)"/> convention: the awaited result is the chosen
///     name, or <see langword="null"/> when the user canceled.
/// </summary>
/// <remarks>
///     All state and validation live in <see cref="GroupNameViewModel"/>; this code-behind only
///     closes the window with the result the view model reports, makes closing it by any other
///     means (the title-bar button, Escape) count as "Cancel", and puts the cursor in the name box,
///     with the current name selected, when the window opens.
/// </remarks>
internal sealed partial class GroupNameWindow : Window
{
    /// <summary>
    ///     Initializes a new <see cref="GroupNameWindow"/> with no bound view model; used by the
    ///     Avalonia XAML previewer/loader only. Production code should use
    ///     <see cref="GroupNameWindow(GroupNameViewModel)"/>.
    /// </summary>
    public GroupNameWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    ///     Initializes a new <see cref="GroupNameWindow"/> bound to the given view model.
    /// </summary>
    /// <param name="viewModel">The view model to bind and observe for the answer.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="viewModel"/> is
    ///     <see langword="null"/>.</exception>
    public GroupNameWindow(GroupNameViewModel viewModel) : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
        viewModel.Completed += (_, name) => Close(name);

        // However the window closes, an unanswered prompt counts as "cancelled"
        Closed += (_, _) => viewModel.Cancel();

        // Tab stays in the prompt, the name box has the (visible) focus with its text selected, and
        // Escape cancels
        DialogBehavior.Apply(this, "GroupNameTextBox", viewModel.Cancel);
        Opened += (_, _) => this.FindControl<TextBox>("GroupNameTextBox")?.SelectAll();
    }
}
