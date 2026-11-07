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

namespace DemaConsulting.AgentControl.PackageCredentials;

/// <summary>
///     The credential facilities the application shares across every package operation: the
///     session store and (when a user is available to ask) the prompt.
/// </summary>
/// <remarks>
///     Bundled into one small value so it can be handed from the application root down to the
///     places that create source providers without widening every constructor. A context with a
///     <see langword="null"/> <see cref="Prompt"/> is "silent": it may use remembered credentials
///     but never asks the user, which is what background work (such as the upgrade check at
///     start-up) must use so that no dialog appears unprompted. Immutable and thread-safe.
/// </remarks>
/// <param name="Store">The session credential store shared by the whole application.</param>
/// <param name="Prompt">Asks the user for a credential, or <see langword="null"/> when no user
///     is available to ask.</param>
internal sealed record PackageCredentialContext(SessionCredentialStore Store, ICredentialPrompt? Prompt)
{
    /// <summary>
    ///     Gets a context that shares this context's store but never asks the user, for work that
    ///     must not interrupt the user with a dialog.
    /// </summary>
    /// <returns>A silent context over the same store.</returns>
    public PackageCredentialContext WithoutPrompt() => this with { Prompt = null };
}
