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

using DemaConsulting.AgentControl.PackageCredentials;
using DemaConsulting.NuGet.Caching;

namespace DemaConsulting.AgentControl.Tests.PackageCredentials;

/// <summary>
///     A scripted <see cref="ICredentialPrompt"/> for tests: answers each question from a queue
///     (a credential, or <see langword="null"/> for "the user cancelled"), records every question
///     and can instead wait forever so timeout and cancellation behavior can be tested.
/// </summary>
/// <remarks>
///     Thread-safe, because the code under test asks from worker threads.
/// </remarks>
internal sealed class FakeCredentialPrompt : ICredentialPrompt
{
    /// <summary>
    ///     The answers still to give, in order; the last answer repeats when the queue is empty.
    /// </summary>
    private readonly Queue<CredentialPromptAnswer?> _answers = new();

    /// <summary>
    ///     The questions asked so far.
    /// </summary>
    private readonly List<CredentialPromptRequest> _requests = [];

    /// <summary>
    ///     The answer given when the queue has run out.
    /// </summary>
    private CredentialPromptAnswer? _lastAnswer;

    /// <summary>
    ///     Gets or sets a value indicating whether a question is never answered (it completes only
    ///     when its cancellation token is cancelled), simulating a user who walked away.
    /// </summary>
    public bool NeverAnswer { get; set; }

    /// <summary>
    ///     Gets the questions asked so far.
    /// </summary>
    public IReadOnlyList<CredentialPromptRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>
    ///     Gets how many questions were asked.
    /// </summary>
    public int AskCount => Requests.Count;

    /// <summary>
    ///     Queues an answer: a credential the "user" types, or <see langword="null"/> for a
    ///     cancelled dialog.
    /// </summary>
    /// <param name="credential">The answer.</param>
    /// <param name="remember">Whether the "user" ticks "Remember on this computer".</param>
    public void EnqueueAnswer(NuGetCredential? credential, bool remember = false)
    {
        lock (_answers)
        {
            _answers.Enqueue(credential is null ? null : new CredentialPromptAnswer(credential, remember));
        }
    }

    /// <inheritdoc />
    public async Task<CredentialPromptAnswer?> PromptAsync(CredentialPromptRequest request, CancellationToken cancellationToken)
    {
        lock (_requests)
        {
            _requests.Add(request);
        }

        if (NeverAnswer)
        {
            // Completes only by cancellation, as an abandoned dialog would
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        lock (_answers)
        {
            if (_answers.Count > 0)
            {
                _lastAnswer = _answers.Dequeue();
            }

            return _lastAnswer;
        }
    }
}
