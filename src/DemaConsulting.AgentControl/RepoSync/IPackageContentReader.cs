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
///     Read-only view of an agent package's content, independent of whether the package is a zip
///     file or an unpacked folder, so one extraction routine can serve every package source.
/// </summary>
/// <remarks>
///     <para>
///     A package's content is a set of files addressed by relative paths that use <c>/</c> as the
///     separator and are rooted at the package content root: the zip's root for a zip package, or
///     the <c>content</c> folder of an unpacked NuGet package. The managed agent folders live
///     under <c>.github/</c> there, with <c>release-notes.md</c> and the optional
///     <c>AGENTS.md</c> template at the root.
///     </para>
///     <para>
///     Implementations are strictly read-only: they must never write to, delete from, or otherwise
///     modify the package they read (a NuGet package folder is a shared cache). Disposing a reader
///     releases any file handle it holds. Instances are not thread-safe.
///     </para>
/// </remarks>
internal interface IPackageContentReader : IDisposable
{
    /// <summary>
    ///     Gets a short description of where the content comes from (a zip path or folder path),
    ///     used in diagnostics and error messages.
    /// </summary>
    string Description { get; }

    /// <summary>
    ///     Gets the content identity of the package this reader delivers: the
    ///     <c>files-sha256:</c> identity of the agent files it would extract (see
    ///     <see cref="DemaConsulting.AgentControl.AgentPackageManagement.PackageContentIdentity"/>),
    ///     or <see langword="null"/> when the implementation cannot name one.
    /// </summary>
    /// <remarks>
    ///     Lets a repo's pin record which files a package version delivered, so a later sync of the
    ///     same version can detect that the package was republished - whichever kind of source
    ///     delivers it, because the identity is computed from the files (through
    ///     <see cref="DemaConsulting.AgentControl.AgentPackageManagement.PackageContentIdentity.ComputeFilesIdentity"/>)
    ///     and not from the zip, <c>.nupkg</c> or repository they came in. It describes the very
    ///     content this reader reads, never a separate look at the source. A default implementation
    ///     returns <see langword="null"/> so readers that predate identities keep working;
    ///     <see langword="null"/> means "nothing to compare", never "changed".
    /// </remarks>
    string? ContentIdentity => null;

    /// <summary>
    ///     Lists every file in the package content.
    /// </summary>
    /// <returns>Relative file paths using <c>/</c> separators; directories are not listed.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the content cannot be enumerated or
    ///     is unacceptable (for example a folder package that contains a link).</exception>
    IReadOnlyList<string> ListFiles();

    /// <summary>
    ///     Opens one file of the package for reading.
    /// </summary>
    /// <param name="relativePath">A path previously returned by <see cref="ListFiles"/>.</param>
    /// <returns>A readable stream the caller must dispose.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the file does not exist, is not
    ///     acceptable to read (a link), or cannot be opened.</exception>
    Stream OpenRead(string relativePath);

    /// <summary>
    ///     Reads a text file at the package content root, if present.
    /// </summary>
    /// <param name="relativePath">The root-level file name, e.g. <c>release-notes.md</c>.</param>
    /// <returns>The file's text, or <see langword="null"/> when the package has no such file.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the file exists but cannot be
    ///     read.</exception>
    string? ReadText(string relativePath);
}
