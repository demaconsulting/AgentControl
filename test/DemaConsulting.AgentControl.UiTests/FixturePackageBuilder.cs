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

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     Builds a minimal, realistic agent package fixture zip on disk for FlaUI end-to-end tests,
///     matching the <c>{packageName}-{version}.zip</c> naming convention and the four managed
///     folders (<c>.github/agents</c>, <c>.github/standards</c>, <c>.github/templates</c>,
///     <c>.github/skills</c>) that <c>PackageZipExtractor</c> blind-deletes and replaces, plus a
///     root-level <c>release-notes.md</c>.
/// </summary>
internal static class FixturePackageBuilder
{
    /// <summary>
    ///     Creates a fixture package zip under <paramref name="sourceDirectory"/>.
    /// </summary>
    /// <param name="sourceDirectory">The package-source directory to create the zip in; created
    ///     if it does not already exist.</param>
    /// <param name="packageName">The package base name, e.g. <c>"contoso-agents"</c>.</param>
    /// <param name="version">The package's semantic version, e.g. <c>"1.0.0"</c>.</param>
    /// <param name="releaseNotes">The content of the root-level <c>release-notes.md</c> entry.</param>
    /// <returns>The absolute path to the created zip file.</returns>
    public static string Create(
        string sourceDirectory,
        string packageName,
        string version,
        string releaseNotes = "# Release Notes\n\nInitial fixture package.\n")
    {
        Directory.CreateDirectory(sourceDirectory);

        var zipPath = Path.Combine(sourceDirectory, $"{packageName}-{version}.zip");
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        AddTextEntry(archive, ".github/agents/test.md", "# Test Agent\n");
        AddTextEntry(archive, ".github/standards/test.md", "# Test Standard\n");
        AddTextEntry(archive, ".github/templates/test.md", "# Test Template\n");
        AddTextEntry(archive, ".github/skills/test.md", "# Test Skill\n");
        AddTextEntry(archive, "release-notes.md", releaseNotes);

        return zipPath;
    }

    /// <summary>
    ///     Adds a single UTF-8 text entry to a zip archive being built.
    /// </summary>
    /// <param name="archive">The archive to add to.</param>
    /// <param name="entryName">The zip-relative entry name, using forward slashes.</param>
    /// <param name="content">The entry's text content.</param>
    private static void AddTextEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }
}
