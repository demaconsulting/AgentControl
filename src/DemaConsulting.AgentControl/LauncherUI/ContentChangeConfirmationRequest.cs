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

using DemaConsulting.AgentControl.AgentPackageManagement;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     Carries what the view layer needs to ask the user whether a republished package version
///     (the same version, different content) may replace the copy a repo was set up with.
/// </summary>
/// <remarks>
///     Raised by <see cref="RepoCardViewModel.ContentChangeConfirmationRequested"/>. Immutable and
///     thread-safe. The message is already written for the user and contains no hashes or
///     credentials, so the view can show it as is.
/// </remarks>
/// <param name="Selection">The package version to apply again, with the user's confirmation, if the
///     user agrees.</param>
/// <param name="Message">The complete question to show: what changed and what agreeing does.</param>
internal sealed record ContentChangeConfirmationRequest(PackageReference Selection, string Message);
