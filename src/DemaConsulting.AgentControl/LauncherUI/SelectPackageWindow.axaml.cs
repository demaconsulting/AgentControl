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

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     Modal "Select Package..." dialog: lets the user pick a package name then a version from
///     the configured source, mirroring <see cref="ConfirmationWindow"/>'s
///     <see cref="Window.ShowDialog{TResult}(Window)"/> convention.
/// </summary>
/// <remarks>
///     All list population/selection logic lives in <see cref="SelectPackageWindowViewModel"/>;
///     this code-behind only wires the "Cancel" button (which has no bindable command, matching
///     <see cref="ConfirmationWindow"/>'s "No" button) and closes the window with the selected
///     <c>(Name, Version)</c> tuple once <see cref="SelectPackageWindowViewModel.Confirmed"/>
///     fires.
/// </remarks>
internal sealed partial class SelectPackageWindow : Window
{
    /// <summary>
    ///     Initializes a new <see cref="SelectPackageWindow"/> with no bound view model; used by
    ///     the Avalonia XAML previewer/loader only. Production code should use
    ///     <see cref="SelectPackageWindow(SelectPackageWindowViewModel)"/>.
    /// </summary>
    public SelectPackageWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    ///     Initializes a new <see cref="SelectPackageWindow"/> bound to the given view model,
    ///     closing itself with the selected <c>(Name, Version)</c> tuple once the view model
    ///     reports a confirmed selection.
    /// </summary>
    /// <param name="viewModel">The view model supplying the package/version lists and selection.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="viewModel"/> is
    ///     <see langword="null"/>.</exception>
    public SelectPackageWindow(SelectPackageWindowViewModel viewModel) : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
        viewModel.Confirmed += (_, result) => Close(((string Name, string Version)?)result);
    }

    /// <summary>
    ///     Closes the window reporting that the user canceled the selection.
    /// </summary>
    /// <param name="sender">The clicked "Cancel" button.</param>
    /// <param name="e">Routed event arguments (unused).</param>
    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(((string Name, string Version)?)null);
    }
}
