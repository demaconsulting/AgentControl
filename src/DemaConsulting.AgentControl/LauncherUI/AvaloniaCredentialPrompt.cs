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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using DemaConsulting.AgentControl.PackageCredentials;
using DemaConsulting.NuGet.Caching;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     The application's <see cref="ICredentialPrompt"/>: shows the <see cref="CredentialPromptWindow"/>
///     dialog on the UI thread, one dialog at a time, whichever thread asks.
/// </summary>
/// <remarks>
///     <para>
///     The NuGet library asks for credentials on worker threads, while a window can only be created
///     and shown on the UI thread, so each question is marshalled with
///     <see cref="Dispatcher.InvokeAsync{TResult}(Func{Task{TResult}}, DispatcherPriority)"/>. The
///     caller awaits asynchronously and never blocks the UI thread, so an operation started from a
///     UI-thread command cannot deadlock against its own dialog. Questions are serialized with a
///     gate: if two operations need a sign-in at once (two sources loading together), the second
///     dialog appears after the first is answered rather than on top of it.
///     </para>
///     <para>
///     The dialog is modal over the top-most open window (the Settings window, the Select Package
///     window or the main window), so it is always in front of whatever started the operation.
///     Cancelling the question's token (the operation was cancelled or timed out) closes the dialog
///     and surfaces as a cancellation. The typed password is handed to the caller and never
///     logged. Thread-safe.
///     </para>
/// </remarks>
internal sealed class AvaloniaCredentialPrompt : ICredentialPrompt
{
    /// <summary>
    ///     Lets one question at a time use the screen.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    ///     Finds the window the dialog is shown over; evaluated on the UI thread for each question.
    /// </summary>
    private readonly Func<Window?> _getOwner;

    /// <summary>
    ///     Initializes the prompt.
    /// </summary>
    /// <param name="getOwner">Finds the window to show the dialog over, or <see langword="null"/>
    ///     to use the top-most visible window of the running desktop application. Supplied so
    ///     tests can choose the owner.</param>
    public AvaloniaCredentialPrompt(Func<Window?>? getOwner = null)
    {
        _getOwner = getOwner ?? FindTopMostWindow;
    }

    /// <inheritdoc />
    public async Task<NuGetCredential?> PromptAsync(CredentialPromptRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // One dialog at a time, whichever operation asks; waiting honors cancellation
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Dispatcher.UIThread.InvokeAsync(() => ShowAsync(request, cancellationToken));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    ///     Finds the top-most visible window of the desktop application, the one a new dialog
    ///     belongs in front of.
    /// </summary>
    /// <returns>The window, or <see langword="null"/> when no desktop application is running.</returns>
    private static Window? FindTopMostWindow() =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.Windows.LastOrDefault(window => window.IsVisible) ?? desktop.MainWindow
            : null;

    /// <summary>
    ///     Shows the dialog and waits for the answer; runs on the UI thread.
    /// </summary>
    /// <param name="request">Which feed and source the credential is for.</param>
    /// <param name="cancellationToken">Closes the dialog when cancelled.</param>
    /// <returns>The credential the user entered, or <see langword="null"/> when cancelled.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/>
    ///     was cancelled before the user answered.</exception>
    private async Task<NuGetCredential?> ShowAsync(CredentialPromptRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var viewModel = new CredentialPromptViewModel(request);
        var window = new CredentialPromptWindow(viewModel);
        var answer = new TaskCompletionSource<NuGetCredential?>(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.Completed += (_, credential) => answer.TrySetResult(credential);

        // A cancelled operation withdraws the question: cancelling the view model closes the window
        using var registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(viewModel.Cancel));

        var owner = _getOwner();
        if (owner is { IsVisible: true })
        {
            _ = window.ShowDialog(owner);
        }
        else
        {
            window.Show();
        }

        var credential = await answer.Task;
        cancellationToken.ThrowIfCancellationRequested();
        return credential;
    }
}
