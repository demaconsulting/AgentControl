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

namespace DemaConsulting.AgentControl.AgentPackageManagement;

/// <summary>
///     Identifies one exact package version at one configured source - the identity a repo pin
///     records.
/// </summary>
/// <remarks>
///     A package ID is deliberately not unique across sources: two sources may both offer
///     <c>contoso-agents 1.0.0</c> with different content, so a package is only ever identified
///     (and resolved) together with the alias of the source it came from. The version is kept as
///     the original text so the exact spelling the user selected survives a pin round trip.
///     Immutable and thread-safe.
/// </remarks>
/// <param name="SourceAlias">Alias of the configured source the package comes from.</param>
/// <param name="PackageId">The package name or NuGet package ID.</param>
/// <param name="Version">The exact version text.</param>
internal sealed record PackageReference(string SourceAlias, string PackageId, string Version)
{
    /// <summary>
    ///     Renders the reference for messages and logs, e.g.
    ///     <c>contoso-agents 1.2.3 (source 'Default')</c>.
    /// </summary>
    /// <returns>A human-readable description containing no location or credential data.</returns>
    public override string ToString() => $"{PackageId} {Version} (source '{SourceAlias}')";
}
