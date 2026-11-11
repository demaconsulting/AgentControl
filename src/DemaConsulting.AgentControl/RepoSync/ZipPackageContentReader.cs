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

using System.IO.Compression;
using DemaConsulting.AgentControl.AgentPackageManagement;

namespace DemaConsulting.AgentControl.RepoSync;

/// <summary>
///     <see cref="IPackageContentReader"/> over an agent package zip file, whose content root is
///     the zip's root.
/// </summary>
/// <remarks>
///     Holds the zip open (shared-read) until disposed so a package is read consistently across
///     the several reads a sync performs. Never writes to the zip. The zip is opened once, and
///     the same handle serves every read - including the one that computes
///     <see cref="ContentIdentity"/> from the files the archive holds - so the identity always
///     describes exactly the files that are extracted (the file cannot be swapped between
///     identifying and reading, because the open handle denies writers). Not thread-safe.
/// </remarks>
internal sealed class ZipPackageContentReader : IPackageContentReader
{
    /// <summary>
    ///     The opened archive; owned and disposed by this reader.
    /// </summary>
    private readonly ZipArchive _archive;

    /// <summary>
    ///     The identity once it has been computed (<see langword="null"/> is a valid result:
    ///     "cannot be named").
    /// </summary>
    private string? _contentIdentity;

    /// <summary>
    ///     Whether <see cref="_contentIdentity"/> has been computed.
    /// </summary>
    private bool _identityComputed;

    /// <summary>
    ///     Initializes a reader over an already-opened archive.
    /// </summary>
    /// <param name="archive">The opened archive; ownership transfers to this reader.</param>
    /// <param name="description">The zip path, used in messages.</param>
    private ZipPackageContentReader(ZipArchive archive, string description)
    {
        _archive = archive;
        Description = description;
    }

    /// <inheritdoc />
    public string Description { get; }

    /// <summary>
    ///     Gets the <c>files-sha256:</c> identity of the agent files in the zip - independent of the
    ///     zip's compression, timestamps and entry order - or <see langword="null"/> when the zip's
    ///     content cannot be identified (see <see cref="PackageContentIdentity.TryComputeFilesIdentity"/>).
    /// </summary>
    /// <remarks>
    ///     Computed on first use and remembered: a reader opened only for its release notes never
    ///     pays for reading every file.
    /// </remarks>
    public string? ContentIdentity
    {
        get
        {
            if (!_identityComputed)
            {
                _contentIdentity = PackageContentIdentity.TryComputeFilesIdentity(this);
                _identityComputed = true;
            }

            return _contentIdentity;
        }
    }

    /// <summary>
    ///     Opens a package zip for reading, wrapping any failure in a clear exception.
    /// </summary>
    /// <param name="zipPath">Path to the zip file.</param>
    /// <returns>A reader the caller must dispose.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="zipPath"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the file does not exist, is not a
    ///     valid zip archive, the path is invalid or unsupported, or the file cannot be opened for
    ///     another I/O or permission reason.</exception>
    public static ZipPackageContentReader Open(string zipPath)
    {
        ArgumentNullException.ThrowIfNull(zipPath);

        FileStream? stream = null;
        try
        {
            // One handle for everything the reader does (shared-read, as ZipFile.OpenRead does)
            stream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return new ZipPackageContentReader(new ZipArchive(stream, ZipArchiveMode.Read), zipPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                       or ArgumentException or NotSupportedException
                                       or System.Security.SecurityException)
        {
            // Release the handle when the archive could not be built over it; also the
            // path-related failures (an unsupported or invalid path), so the documented
            // contract - every failure to open is an InvalidOperationException - holds
            stream?.Dispose();
            throw new InvalidOperationException($"Failed to open package zip '{zipPath}': {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ListFiles() =>
        // Directory entries have an empty Name (only FullName ends with '/'); skip them, as the
        // extractor recreates any needed directory structure from the file entries.
        _archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).Select(entry => entry.FullName).ToList();

    /// <inheritdoc />
    public Stream OpenRead(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        var entry = _archive.GetEntry(relativePath)
                    ?? throw new InvalidOperationException(
                        $"Package '{Description}' has no entry '{relativePath}'.");
        try
        {
            return entry.Open();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            throw new InvalidOperationException(
                $"Failed to open '{relativePath}' from '{Description}': {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public string? ReadText(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        var entry = _archive.GetEntry(relativePath);
        if (entry is null)
        {
            return null;
        }

        try
        {
            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            throw new InvalidOperationException(
                $"Failed to read '{relativePath}' from '{Description}': {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _archive.Dispose();
}
