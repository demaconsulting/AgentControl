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
///     The overall status of a repo card, as the colored accent bar on the card's left edge
///     shows it: green, amber, red or grey.
/// </summary>
/// <remarks>
///     Kept as a plain enumeration so the card's state is a testable fact of the view model and
///     the view only maps it to a color. The color is never the only carrier of the meaning: the
///     card's chips and tooltips state the same facts in words.
/// </remarks>
internal enum RepoCardStatus
{
    /// <summary>
    ///     An agent package is pinned and no newer version is available (green).
    /// </summary>
    UpToDate,

    /// <summary>
    ///     A newer version of the pinned agent package is available (amber).
    /// </summary>
    UpgradeAvailable,

    /// <summary>
    ///     Something needs attention: the repo folder is missing, the package source has a
    ///     problem or needs a sign-in, or the last action failed (red).
    /// </summary>
    Problem,

    /// <summary>
    ///     No agent package is pinned for the repo (grey).
    /// </summary>
    NoPackage,

    /// <summary>
    ///     A package is pinned, but whether an upgrade exists is not known yet because the
    ///     background check is still running (grey), so the card does not claim "up to date" early.
    /// </summary>
    Checking,

    /// <summary>
    ///     A package is pinned, but the last background check failed (the package source could not
    ///     be read) so it is not known whether an upgrade exists (grey); the card does not claim
    ///     "up to date" on the strength of a check that did not succeed.
    /// </summary>
    Unknown,

    /// <summary>
    ///     The pinned version of a Git source now points to a different commit than the one the
    ///     repo was set up from (amber): the card never claims "up to date" for content that is
    ///     no longer what the repo has, and nothing is applied until the user accepts it with
    ///     "Change package...".
    /// </summary>
    TagMoved
}
