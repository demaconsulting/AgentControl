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
///     An <see cref="ICommand"/> that runs an asynchronous, cancelable operation without
///     blocking the UI thread and refuses to start a second run while one is in progress.
/// </summary>
/// <remarks>
///     <para>
///     It exists so view models can bind long-running work (reading a package source, extracting
///     a package) to buttons without a synchronous bridge over asynchronous code: the operation
///     is awaited, the command reports <see cref="IsRunning"/> and disables itself for the
///     duration (so a double click cannot start the work twice), and <see cref="Cancel"/> signals
///     the token the operation received.
///     </para>
///     <para>
///     Failures never escape to the UI thread's unhandled-exception path: any exception other
///     than the cancellation of the operation is passed to the <c>onError</c> callback supplied at
///     construction, which the owning view model uses to tell the user. Not thread-safe; like
///     <see cref="RelayCommand"/>, every member is expected to be called from the UI thread.
///     </para>
/// </remarks>
internal sealed class AsyncRelayCommand : ICommand
{
    /// <summary>
    ///     The operation to run; receives a token that <see cref="Cancel"/> signals.
    /// </summary>
    private readonly Func<CancellationToken, Task> _execute;

    /// <summary>
    ///     Optional predicate deciding whether the command is eligible to run apart from the
    ///     built-in "not already running" rule.
    /// </summary>
    private readonly Func<bool>? _canExecute;

    /// <summary>
    ///     Receives every exception other than cancellation that the operation throws.
    /// </summary>
    private readonly Action<Exception> _onError;

    /// <summary>
    ///     The source of the token handed to the running operation, or <see langword="null"/>
    ///     when no run is in progress.
    /// </summary>
    private CancellationTokenSource? _cancellation;

    /// <summary>
    ///     Backing field for <see cref="IsRunning"/>.
    /// </summary>
    private bool _isRunning;

    /// <summary>
    ///     Initializes a new <see cref="AsyncRelayCommand"/>.
    /// </summary>
    /// <param name="execute">The asynchronous operation to run. Must not be
    ///     <see langword="null"/>.</param>
    /// <param name="onError">Callback that receives any exception other than cancellation thrown
    ///     by <paramref name="execute"/>. Must not be <see langword="null"/>.</param>
    /// <param name="canExecute">Optional additional eligibility predicate; <see langword="null"/>
    ///     means the command is eligible whenever it is not already running.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="execute"/> or
    ///     <paramref name="onError"/> is <see langword="null"/>.</exception>
    public AsyncRelayCommand(
        Func<CancellationToken, Task> execute, Action<Exception> onError, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(onError);

        _execute = execute;
        _onError = onError;
        _canExecute = canExecute;
    }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <summary>
    ///     Raised when <see cref="IsRunning"/> changes, so an owner can show or hide progress
    ///     for the operation.
    /// </summary>
    public event EventHandler? IsRunningChanged;

    /// <summary>
    ///     Gets a value indicating whether the operation is currently running.
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => !_isRunning && (_canExecute?.Invoke() ?? true);

    /// <summary>
    ///     Starts the operation without waiting for it, as bound controls do; see
    ///     <see cref="ExecuteAsync"/> for the awaitable form.
    /// </summary>
    /// <param name="parameter">Command parameter (unused).</param>
    /// <remarks>
    ///     The returned task is intentionally not awaited: <see cref="ExecuteAsync"/> reports
    ///     every failure through the <c>onError</c> callback, so it cannot fault.
    /// </remarks>
    public void Execute(object? parameter) => _ = ExecuteAsync();

    /// <summary>
    ///     Runs the operation to completion unless it is already running, in which case it
    ///     returns immediately without running it.
    /// </summary>
    /// <remarks>
    ///     Like <see cref="RelayCommand"/>, this does not re-check the eligibility predicate:
    ///     deciding whether the command may be offered is the binding layer's job through
    ///     <see cref="CanExecute"/>; the one guard enforced here is that a second run cannot start
    ///     while one is in progress.
    /// </remarks>
    /// <returns>A task that completes when the run finishes (or immediately when no run was
    ///     started). Cancellation is not an error; any other failure is reported through the
    ///     <c>onError</c> callback and the task still completes normally.</returns>
    public async Task ExecuteAsync()
    {
        if (_isRunning)
        {
            return;
        }

        // Mark the command running before the first await so a second activation that arrives
        // while the operation is suspended is refused
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        SetRunning(true);
        try
        {
            await _execute(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // A cancelled run is a normal outcome (the user asked for it), not a failure
        }
        catch (Exception ex)
        {
            _onError(ex);
        }
        finally
        {
            _cancellation = null;
            SetRunning(false);
        }
    }

    /// <summary>
    ///     Signals cancellation to the running operation; does nothing when no run is in
    ///     progress.
    /// </summary>
    public void Cancel()
    {
        try
        {
            _cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The run finished between the check and the cancel; nothing is left to cancel
        }
    }

    /// <summary>
    ///     Notifies bound controls that <see cref="CanExecute"/> should be re-evaluated, for use
    ///     after state the additional eligibility predicate depends on changed.
    /// </summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    ///     Updates <see cref="IsRunning"/> and raises the change notifications.
    /// </summary>
    /// <param name="running">The new running state.</param>
    private void SetRunning(bool running)
    {
        _isRunning = running;
        IsRunningChanged?.Invoke(this, EventArgs.Empty);
        RaiseCanExecuteChanged();
    }
}
