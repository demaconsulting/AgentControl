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
///     What the pre-launch check of a repo's agent files concluded; the launch decides from it
///     whether to start the agent tool.
/// </summary>
internal enum AgentFilesSyncOutcome
{
    /// <summary>
    ///     Nothing needed doing and nothing was compared: the repo has committed agent files, has
    ///     no pin, or its pin records no content identity and the managed folders are all present.
    ///     The launch proceeds.
    /// </summary>
    Skipped,

    /// <summary>
    ///     The agent files on disk match the pin's recorded identity exactly; no package was
    ///     touched and nobody was asked. The launch proceeds.
    /// </summary>
    InSync,

    /// <summary>
    ///     No agent files were installed (or a managed folder was missing for a pin without an
    ///     identity), so the pinned package was installed silently. The launch proceeds.
    /// </summary>
    Restored,

    /// <summary>
    ///     The files differed from the pin and the user chose to update them from the package,
    ///     which succeeded. The launch proceeds.
    /// </summary>
    Updated,

    /// <summary>
    ///     The files differed from the pin and the user chose to keep them (or nobody could be
    ///     asked); they were left exactly as they are. The launch proceeds.
    /// </summary>
    KeptCurrentFiles,

    /// <summary>
    ///     Files were needed but could not be restored, and the launch proceeds anyway: the user
    ///     chose "Launch anyway", or nobody could be asked and the failure was reported passively.
    /// </summary>
    FailedLaunchAnyway,

    /// <summary>
    ///     Files were needed but could not be restored, and the user chose to cancel the launch.
    ///     The agent tool is not started.
    /// </summary>
    LaunchCanceled
}

/// <summary>
///     Carries what the view layer needs to ask the user a two-choice question about a repo's agent
///     files before a launch (keep or update the files that differ from the pin; launch anyway or
///     cancel when a needed restore failed), and carries the answer back.
/// </summary>
/// <remarks>
///     Raised by <see cref="RepoCardViewModel.AgentFilesChoiceRequested"/>. Like
///     <see cref="CommittedFilesConversionRequest"/>, the request is answered in place: the card's
///     launch waits for <see cref="Answer"/> while it holds the card busy, so the card's other
///     actions stay disabled and no second launch can start while the question is open. The message
///     and both button labels are already written for the user, so the view shows them as is. The
///     safe choice is always the <em>decline</em> side (<see cref="DeclineText"/>), which is also
///     what closing the dialog or pressing Escape answers; the <em>confirm</em> side
///     (<see cref="ConfirmText"/>) is the action that changes files or proceeds past a failure. The
///     first answer wins; later calls to <see cref="Respond"/> are ignored. Thread-safe.
/// </remarks>
internal sealed class AgentFilesChoiceRequest
{
    /// <summary>
    ///     Completes with the user's answer; runs continuations asynchronously so answering from the
    ///     view never runs the card's operation inside the dialog's close handling.
    /// </summary>
    private readonly TaskCompletionSource<bool> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    ///     Initializes a request.
    /// </summary>
    /// <param name="message">The complete question to show. Must not be <see langword="null"/>.</param>
    /// <param name="confirmText">The label of the button that confirms the non-default action
    ///     (for example "Update from package" or "Launch anyway"). Must not be
    ///     <see langword="null"/>.</param>
    /// <param name="declineText">The label of the safe, default button (for example "Keep current
    ///     files" or "Cancel"). Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is
    ///     <see langword="null"/>.</exception>
    public AgentFilesChoiceRequest(string message, string confirmText, string declineText)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(confirmText);
        ArgumentNullException.ThrowIfNull(declineText);
        Message = message;
        ConfirmText = confirmText;
        DeclineText = declineText;
    }

    /// <summary>
    ///     Gets the complete question to show the user.
    /// </summary>
    public string Message { get; }

    /// <summary>
    ///     Gets the label of the button that confirms the non-default action.
    /// </summary>
    public string ConfirmText { get; }

    /// <summary>
    ///     Gets the label of the safe, default button.
    /// </summary>
    public string DeclineText { get; }

    /// <summary>
    ///     Gets the task that completes with <see langword="true"/> when the user confirmed the
    ///     non-default action and <see langword="false"/> when the user chose the safe side (or the
    ///     question could not be shown).
    /// </summary>
    public Task<bool> Answer => _answer.Task;

    /// <summary>
    ///     Records the user's answer.
    /// </summary>
    /// <param name="confirmed"><see langword="true"/> when the user chose
    ///     <see cref="ConfirmText"/>; <see langword="false"/> otherwise.</param>
    public void Respond(bool confirmed) => _answer.TrySetResult(confirmed);
}
