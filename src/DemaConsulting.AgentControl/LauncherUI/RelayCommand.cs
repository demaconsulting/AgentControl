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

using System.Windows.Input;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     A minimal <see cref="ICommand"/> implementation that delegates execution and
///     eligibility checks to injected delegates, for binding view-model actions
///     (Launch/Pull/Upgrade/Save/etc.) to Avalonia <c>Button</c>/<c>MenuItem</c> controls.
/// </summary>
/// <remarks>
///     Implemented by hand rather than via CommunityToolkit.Mvvm's <c>[RelayCommand]</c>
///     generator to keep this project's dependency surface minimal, matching
///     <see cref="ViewModelBase"/>'s rationale. Not thread-safe: <see cref="RaiseCanExecuteChanged"/>
///     is expected to be called from the UI thread, consistent with every other view-model
///     mutation in this codebase.
/// </remarks>
internal sealed class RelayCommand : ICommand
{
    /// <summary>
    ///     The action to invoke when the command executes.
    /// </summary>
    private readonly Action _execute;

    /// <summary>
    ///     Optional predicate determining whether the command can currently execute; a
    ///     <see langword="null"/> predicate means the command is always executable.
    /// </summary>
    private readonly Func<bool>? _canExecute;

    /// <summary>
    ///     Initializes a new <see cref="RelayCommand"/>.
    /// </summary>
    /// <param name="execute">The action to invoke when the command executes. Must not be
    ///     <see langword="null"/>.</param>
    /// <param name="canExecute">Optional predicate determining whether the command can
    ///     currently execute; pass <see langword="null"/> for an always-executable command.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="execute"/> is
    ///     <see langword="null"/>.</exception>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
    }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    /// <inheritdoc />
    public void Execute(object? parameter) => _execute();

    /// <summary>
    ///     Notifies any bound controls that <see cref="CanExecute"/> should be re-evaluated.
    /// </summary>
    /// <remarks>
    ///     Callers invoke this after mutating state that <see cref="_canExecute"/> depends on
    ///     (e.g. after a git-status refresh changes whether "Pull" is currently eligible).
    /// </remarks>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
