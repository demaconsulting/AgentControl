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

using System.Xml.Linq;
using DemaConsulting.AgentControl.LauncherUI;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Unit tests for <see cref="ThirdPartyDependencies"/>.
/// </summary>
public class ThirdPartyDependenciesTests
{
    /// <summary>
    ///     Item-group labels (the comment text immediately preceding each <c>ItemGroup</c> in
    ///     <c>DemaConsulting.AgentControl.csproj</c>) whose <c>PackageReference</c> entries are
    ///     this application's direct runtime dependencies, as opposed to build-only/analyzer-only
    ///     packages which are deliberately excluded from <see cref="ThirdPartyDependencies.All"/>.
    /// </summary>
    private static readonly string[] RuntimeDependencyItemGroupComments =
    [
        " Logging Dependencies ",
        " Avalonia UI Dependencies "
    ];

    /// <summary>
    ///     Test that the dependency list's names and versions match the csproj's actual
    ///     "Logging Dependencies"/"Avalonia UI Dependencies" <c>PackageReference</c> entries
    ///     exactly, order-independent, so the hand-maintained list cannot silently drift from the
    ///     project file it documents. The license column cannot be derived from the csproj (NuGet
    ///     package metadata, not project-file content), so it is checked only for non-emptiness by
    ///     <see cref="ThirdPartyDependencies_All_EveryEntryHasNonEmptyLicense"/>.
    /// </summary>
    [Fact]
    public void ThirdPartyDependencies_All_MatchesCsprojDirectRuntimeDependencies()
    {
        // Arrange: read the actual direct runtime PackageReference entries straight from the
        // csproj file, rather than hardcoding a second, independently-maintained copy of them
        // here (which could drift from the implementation list without either side noticing).
        var expected = ReadRuntimeDependenciesFromCsproj()
            .Select(d => (d.Name, d.Version))
            .OrderBy(d => d.Name, StringComparer.Ordinal)
            .ToArray();
        var actual = ThirdPartyDependencies.All
            .Select(d => (d.Name, d.Version))
            .OrderBy(d => d.Name, StringComparer.Ordinal)
            .ToArray();

        // Act / Assert: the hand-maintained list matches the csproj's actual package references
        Assert.Equal(expected, actual);
    }

    /// <summary>
    ///     Test that no two entries share the same dependency name.
    /// </summary>
    [Fact]
    public void ThirdPartyDependencies_All_NoDuplicateNames()
    {
        // Arrange
        var names = ThirdPartyDependencies.All.Select(d => d.Name).ToList();

        // Act
        var distinctNames = names.Distinct().ToList();

        // Assert: every name appeared only once
        Assert.Equal(names.Count, distinctNames.Count);
    }

    /// <summary>
    ///     Test that every entry has a non-empty, non-whitespace license string.
    /// </summary>
    [Fact]
    public void ThirdPartyDependencies_All_EveryEntryHasNonEmptyLicense()
    {
        // Act / Assert
        Assert.All(ThirdPartyDependencies.All, d => Assert.False(string.IsNullOrWhiteSpace(d.License)));
    }

    /// <summary>
    ///     Test that every entry has a non-empty, non-whitespace version string.
    /// </summary>
    [Fact]
    public void ThirdPartyDependencies_All_EveryEntryHasNonEmptyVersion()
    {
        // Act / Assert
        Assert.All(ThirdPartyDependencies.All, d => Assert.False(string.IsNullOrWhiteSpace(d.Version)));
    }

    /// <summary>
    ///     Test that DependencyInfo's ToString() override renders the expected display format.
    /// </summary>
    [Fact]
    public void DependencyInfo_ToString_RepresentativeEntry_ReturnsNameVersionLicenseFormat()
    {
        // Arrange
        var dependency = new DependencyInfo("Serilog", "4.4.0", "Apache-2.0");

        // Act
        var text = dependency.ToString();

        // Assert
        Assert.Equal("Serilog 4.4.0 — Apache-2.0", text);
    }

    /// <summary>
    ///     Reads the <c>PackageReference</c> <c>Include</c>/<c>Version</c> pairs from the
    ///     <c>ItemGroup</c>s immediately preceded by one of
    ///     <see cref="RuntimeDependencyItemGroupComments"/> in
    ///     <c>DemaConsulting.AgentControl.csproj</c>.
    /// </summary>
    /// <returns>The direct runtime dependencies' names and versions, as declared in the csproj.</returns>
    private static IEnumerable<(string Name, string Version)> ReadRuntimeDependenciesFromCsproj()
    {
        var csprojPath = FindAgentControlCsproj();
        var document = XDocument.Load(csprojPath);

        foreach (var itemGroup in document.Descendants("ItemGroup"))
        {
            if (!HasPrecedingLabelComment(itemGroup))
            {
                continue;
            }

            foreach (var packageReference in itemGroup.Elements("PackageReference"))
            {
                var name = packageReference.Attribute("Include")?.Value;
                var version = packageReference.Attribute("Version")?.Value;
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(version))
                {
                    yield return (name, version);
                }
            }
        }
    }

    /// <summary>
    ///     Determines whether <paramref name="itemGroup"/> is immediately preceded - skipping any
    ///     number of XML comments and whitespace-only text nodes, but nothing else - by one of
    ///     <see cref="RuntimeDependencyItemGroupComments"/>. Each runtime-dependency
    ///     <c>ItemGroup</c> is preceded by a short label comment and then a longer explanatory
    ///     comment, so this cannot simply check the single nearest preceding comment.
    /// </summary>
    /// <param name="itemGroup">The <c>ItemGroup</c> element to check.</param>
    /// <returns><see langword="true"/> if a matching label comment precedes this item group.</returns>
    private static bool HasPrecedingLabelComment(XElement itemGroup)
    {
        var node = itemGroup.PreviousNode;
        while (node is not null)
        {
            switch (node)
            {
                case XComment comment when RuntimeDependencyItemGroupComments.Contains(comment.Value):
                    return true;

                case XComment:
                    node = node.PreviousNode;
                    continue;

                case XText text when string.IsNullOrWhiteSpace(text.Value):
                    node = node.PreviousNode;
                    continue;

                default:
                    return false;
            }
        }

        return false;
    }

    /// <summary>
    ///     Walks up from this test assembly's own output directory to find
    ///     <c>src/DemaConsulting.AgentControl/DemaConsulting.AgentControl.csproj</c>, identifying
    ///     the repository root by the presence of <c>DemaConsulting.AgentControl.slnx</c>.
    /// </summary>
    /// <returns>The absolute path to <c>DemaConsulting.AgentControl.csproj</c>.</returns>
    /// <exception cref="DirectoryNotFoundException">Thrown when no ancestor directory contains
    ///     the solution file.</exception>
    private static string FindAgentControlCsproj()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DemaConsulting.AgentControl.slnx")))
            {
                return Path.Combine(
                    directory.FullName,
                    "src",
                    "DemaConsulting.AgentControl",
                    "DemaConsulting.AgentControl.csproj");
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate the repository root (DemaConsulting.AgentControl.slnx) above '{AppContext.BaseDirectory}'.");
    }
}
