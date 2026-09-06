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

namespace DemaConsulting.AgentControl.RepoConfig;

/// <summary>
///     Data model for a repository's <c>.agentcontrol.json</c> pin file.
/// </summary>
/// <remarks>
///     Per architecture.md's "Mandatory version pinning" decision, a repo always names an exact
///     package name and semantic version — there is no "latest"/unpinned mode. Mutable with
///     public getters/setters so <see cref="System.Text.Json.JsonSerializer"/> can round-trip the
///     type without custom converters. Not thread-safe.
/// </remarks>
internal sealed class RepoPin
{
    /// <summary>
    ///     Gets or sets the pinned agent package's base name (e.g. the <c>{name}</c> portion of
    ///     the <c>{name}-{version}.zip</c> file this repo was last synced from).
    /// </summary>
    public string PackageName { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the pinned agent package's exact semantic version (e.g. <c>"1.2.3"</c>).
    /// </summary>
    public string Version { get; set; } = string.Empty;
}
