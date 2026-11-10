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

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     Carries what the view layer needs to ask the user whether AgentControl may stop git tracking
///     a repo's managed agent folders before it applies a package, and carries the answer back.
/// </summary>
/// <remarks>
///     Raised by <see cref="RepoCardViewModel.CommittedFilesConversionRequested"/>. Unlike
///     <see cref="ContentChangeConfirmationRequest"/>, whose answer re-enters the card through a new
///     apply, this request is answered in place: the card's package operation waits for
///     <see cref="Answer"/> while it holds the card busy, so the card's other actions stay
///     disabled and no second operation can start while the question is open. The message is already
///     written for the user, so the view can show it as is. The first answer wins; later calls to
///     <see cref="Respond"/> are ignored. Thread-safe.
/// </remarks>
internal sealed class CommittedFilesConversionRequest
{
    /// <summary>
    ///     Completes with the user's answer; runs continuations asynchronously so answering from the
    ///     view never runs the card's operation inside the dialog's close handling.
    /// </summary>
    private readonly TaskCompletionSource<bool> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    ///     Initializes a request.
    /// </summary>
    /// <param name="message">The complete question to show: which folders are tracked, what agreeing
    ///     does and what the user must do afterwards. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="message"/> is
    ///     <see langword="null"/>.</exception>
    public CommittedFilesConversionRequest(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Message = message;
    }

    /// <summary>
    ///     Gets the complete question to show the user.
    /// </summary>
    public string Message { get; }

    /// <summary>
    ///     Gets the task that completes with <see langword="true"/> when the user agreed and
    ///     <see langword="false"/> when the user declined (or the question could not be shown).
    /// </summary>
    public Task<bool> Answer => _answer.Task;

    /// <summary>
    ///     Records the user's answer.
    /// </summary>
    /// <param name="confirmed"><see langword="true"/> when the user agreed to stop git tracking the
    ///     agent folders; <see langword="false"/> otherwise.</param>
    public void Respond(bool confirmed) => _answer.TrySetResult(confirmed);
}
