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
///     Modal dialog that asks for the user name and password or token of a package feed, via a
///     <see cref="CredentialPromptViewModel"/>, mirroring <see cref="ConfirmationWindow"/>'s
///     <see cref="Window.ShowDialog{TResult}(Window)"/> convention.
/// </summary>
/// <remarks>
///     All state and the decision logic live in <see cref="CredentialPromptViewModel"/>; this
///     code-behind only closes the window with the credential the view model reports (or
///     <see langword="null"/> when cancelled), makes closing the window by any other means - the
///     title-bar button, Escape - count as "Cancel", and puts the cursor in the user name box when
///     the window opens.
/// </remarks>
internal sealed partial class CredentialPromptWindow : Window
{
    /// <summary>
    ///     Initializes a new <see cref="CredentialPromptWindow"/> with no bound view model; used by
    ///     the Avalonia XAML previewer/loader only. Production code should use
    ///     <see cref="CredentialPromptWindow(CredentialPromptViewModel)"/>.
    /// </summary>
    public CredentialPromptWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    ///     Initializes a new <see cref="CredentialPromptWindow"/> bound to the given view model.
    /// </summary>
    /// <param name="viewModel">The view model to bind and observe for the answer.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="viewModel"/> is
    ///     <see langword="null"/>.</exception>
    public CredentialPromptWindow(CredentialPromptViewModel viewModel) : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
        viewModel.Completed += (_, answer) => Close(answer);

        // However the window closes, an unanswered question counts as "cancelled"
        Closed += (_, _) => viewModel.Cancel();

        // Tab stays in the prompt, the user-name box has the (visible) focus, and Escape cancels
        DialogBehavior.Apply(this, "CredentialPromptUserNameTextBox", viewModel.Cancel);
    }
}
