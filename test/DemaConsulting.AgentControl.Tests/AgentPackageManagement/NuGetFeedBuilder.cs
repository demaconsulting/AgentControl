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

namespace DemaConsulting.AgentControl.Tests.AgentPackageManagement;

/// <summary>
///     Builds minimal, unsigned <c>.nupkg</c> files in a local folder so tests can exercise a
///     real NuGet folder feed without any network access.
/// </summary>
/// <remarks>
///     A folder feed is simply a directory of <c>{id}.{version}.nupkg</c> files; each package is a
///     zip holding a <c>{id}.nuspec</c> plus the package files. Agent packages keep their content
///     under <c>content/</c> (for example <c>content/.github/agents/x.md</c>), which is what the
///     product's directory reader expects. Stateless and thread-safe.
/// </remarks>
internal static class NuGetFeedBuilder
{
    /// <summary>
    ///     Creates one package file in a folder feed.
    /// </summary>
    /// <param name="feedFolder">The (existing) feed folder.</param>
    /// <param name="packageId">The package ID.</param>
    /// <param name="version">The package version text.</param>
    /// <param name="files">The package files as (path inside the package, text content) pairs;
    ///     paths use <c>/</c> separators.</param>
    /// <returns>The path of the created <c>.nupkg</c> file.</returns>
    public static string CreatePackage(
        string feedFolder, string packageId, string version, params (string Path, string Content)[] files)
    {
        var nupkgPath = Path.Combine(feedFolder, $"{packageId}.{version}.nupkg");
        using var archive = ZipFile.Open(nupkgPath, ZipArchiveMode.Create);

        // The nuspec is the one entry NuGet requires; it names the package and its version
        AddEntry(archive, $"{packageId}.nuspec", BuildNuspec(packageId, version));
        foreach (var (path, content) in files)
        {
            AddEntry(archive, path, content);
        }

        return nupkgPath;
    }

    /// <summary>
    ///     Creates a typical agent package: the four managed folders under <c>content/.github</c>,
    ///     plus optional release notes and AGENTS.md template under <c>content/</c>.
    /// </summary>
    /// <param name="feedFolder">The (existing) feed folder.</param>
    /// <param name="packageId">The package ID.</param>
    /// <param name="version">The package version text.</param>
    /// <param name="marker">The content of <c>content/.github/agents/copilot.md</c>, so tests can
    ///     tell which package was extracted.</param>
    /// <param name="releaseNotes">Release notes text, or <see langword="null"/> to omit.</param>
    /// <param name="agentsMdTemplate">AGENTS.md template text, or <see langword="null"/> to omit.</param>
    /// <returns>The path of the created <c>.nupkg</c> file.</returns>
    public static string CreateAgentPackage(
        string feedFolder,
        string packageId,
        string version,
        string marker = "agents content",
        string? releaseNotes = null,
        string? agentsMdTemplate = null)
    {
        var files = new List<(string Path, string Content)>
        {
            ("content/.github/agents/copilot.md", marker),
            ("content/.github/standards/style.md", "standards content"),
            ("content/.github/templates/template.md", "templates content"),
            ("content/.github/skills/skill.md", "skills content")
        };

        if (releaseNotes is not null)
        {
            files.Add(("content/release-notes.md", releaseNotes));
        }

        if (agentsMdTemplate is not null)
        {
            files.Add(("content/AGENTS.md", agentsMdTemplate));
        }

        return CreatePackage(feedFolder, packageId, version, [.. files]);
    }

    /// <summary>
    ///     Builds the text of a minimal nuspec.
    /// </summary>
    /// <param name="packageId">The package ID.</param>
    /// <param name="version">The package version text.</param>
    /// <returns>The nuspec XML.</returns>
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
    ///     Adds one UTF-8 text entry to an archive being built.
    /// </summary>
    /// <param name="archive">The archive.</param>
    /// <param name="path">The entry path.</param>
    /// <param name="content">The entry text.</param>
    private static void AddEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }
}
