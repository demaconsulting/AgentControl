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
using System.Text;

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     Builds a minimal agent package as a <c>.nupkg</c> file in a local NuGet folder feed for
///     FlaUI end-to-end tests: a package manifest plus the managed folders and release notes under
///     the package's <c>content</c> folder, which is where AgentControl expects agent files.
/// </summary>
internal static class NuGetFixturePackageBuilder
{
    /// <summary>
    ///     Creates a fixture package under <paramref name="feedFolder"/>.
    /// </summary>
    /// <param name="feedFolder">The feed folder to create the package in; created if missing.</param>
    /// <param name="packageId">The package ID, e.g. <c>"contoso-agents"</c>.</param>
    /// <param name="version">The package version text, e.g. <c>"1.0.0"</c>.</param>
    /// <param name="marker">The content of <c>content/.github/agents/feed.md</c>, which tests read
    ///     back to tell which source a repo's files came from.</param>
    /// <param name="releaseNotes">The content of <c>content/release-notes.md</c>.</param>
    /// <returns>The absolute path of the created <c>.nupkg</c> file.</returns>
    public static string Create(
        string feedFolder,
        string packageId,
        string version,
        string marker,
        string releaseNotes = "# Release Notes\n\nFeed fixture package.\n")
    {
        Directory.CreateDirectory(feedFolder);
        var path = Path.Combine(feedFolder, $"{packageId}.{version}.nupkg");
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddTextEntry(archive, $"{packageId}.nuspec", BuildNuspec(packageId, version));
        AddTextEntry(archive, "content/.github/agents/feed.md", marker);
        AddTextEntry(archive, "content/.github/standards/feed.md", "# Feed Standard\n");
        AddTextEntry(archive, "content/.github/templates/feed.md", "# Feed Template\n");
        AddTextEntry(archive, "content/.github/skills/feed.md", "# Feed Skill\n");
        AddTextEntry(archive, "content/release-notes.md", releaseNotes);
        return path;
    }

    /// <summary>
    ///     Builds the text of a minimal package manifest.
    /// </summary>
    /// <param name="packageId">The package ID.</param>
    /// <param name="version">The package version text.</param>
    /// <returns>The manifest XML.</returns>
    private static string BuildNuspec(string packageId, string version) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
        "<package xmlns=\"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd\">\n" +
        "  <metadata>\n" +
        $"    <id>{packageId}</id>\n" +
        $"    <version>{version}</version>\n" +
        "    <authors>test</authors>\n" +
        "    <description>Test agent package</description>\n" +
        "  </metadata>\n" +
        "</package>\n";

    /// <summary>
    ///     Adds a single UTF-8 text entry to an archive being built.
    /// </summary>
    /// <param name="archive">The archive to add to.</param>
    /// <param name="entryName">The entry name, using forward slashes.</param>
    /// <param name="content">The entry's text content.</param>
    private static void AddTextEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }
}
