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

using System.Text.Json.Serialization;

namespace DemaConsulting.AgentControl.AgentPackageManagement;

/// <summary>
///     The persisted kind of an agent package source: how packages are named and fetched.
/// </summary>
/// <remarks>
///     Only the package format is persisted. Whether a <see cref="NuGet"/> source is a local or
///     network folder or a remote server is derived from its location (see
///     <see cref="PackageSourceLocationKind"/>), so the two can never disagree. Serialized by name
///     (<c>"Zip"</c>, <c>"NuGet"</c>) so the settings file stays readable and stable if members
///     are ever reordered. A <see cref="Git"/> source is one repository whose tags are the package
///     versions; its address is validated by <see cref="GitRepositoryAddress"/>, not by
///     <see cref="PackageSourceLocationKind"/>.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<PackageSourceType>))]
internal enum PackageSourceType
{
    /// <summary>
    ///     A folder of <c>{name}-{semver}.zip</c> package files (the original source format).
    /// </summary>
    Zip,

    /// <summary>
    ///     NuGet packages (<c>.nupkg</c>) held in a local/network folder feed or on a NuGet
    ///     server, whose agent content lives under the package's <c>content</c> folder.
    /// </summary>
    NuGet,

    /// <summary>
    ///     One Git repository (an <c>https</c> or <c>ssh</c> address, or a local path) in which
    ///     the tags that match a configured pattern are the versions of a single package, whose
    ///     agent files are at the top of the repository or in a configured sub-folder.
    /// </summary>
    Git
}

/// <summary>
///     Whether a source's location is a filesystem folder or a remote server, derived from the
///     location text rather than persisted.
/// </summary>
internal enum PackageSourceLocationKind
{
    /// <summary>
    ///     A rooted local, mapped-drive, or UNC folder path.
    /// </summary>
    Folder,

    /// <summary>
    ///     An absolute <c>http</c>/<c>https</c> URL.
    /// </summary>
    Server
}
