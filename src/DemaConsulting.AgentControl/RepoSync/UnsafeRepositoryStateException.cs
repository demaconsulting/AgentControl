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

namespace DemaConsulting.AgentControl.RepoSync;

/// <summary>
///     Thrown by <see cref="PackageZipExtractor"/> when a reparse point (symlink/junction) is
///     found somewhere it would let a blind-delete-and-replace operation read from or write to a
///     location outside the intended repository directory.
/// </summary>
/// <remarks>
///     Deliberately distinct from the plain <see cref="InvalidOperationException"/> thrown for
///     ordinary extraction failures (a corrupt zip, a locked file, an unreachable package
///     source), even though it derives from it for backward-compatible catch-by-base-type
///     behavior. Callers that need to react differently to a genuine security concern - e.g.
///     <c>RepoCardViewModel.EnsureAgentFilesSyncedBeforeLaunch</c>, which must not let its
///     "launch proceeds regardless of sync failure" best-effort policy also apply to a detected
///     symlink/junction attack - can catch this type specifically before the general
///     <see cref="InvalidOperationException"/> case.
/// </remarks>
internal sealed class UnsafeRepositoryStateException : InvalidOperationException
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="UnsafeRepositoryStateException"/> class.
    /// </summary>
    /// <param name="message">A message describing the unsafe reparse point that was detected.</param>
    public UnsafeRepositoryStateException(string message)
        : base(message)
    {
    }
}
