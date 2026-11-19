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

using System.Text.Json;
using DemaConsulting.AgentControl.LauncherUI;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Unit tests for <see cref="ThirdPartyDependencies"/>.
/// </summary>
public class ThirdPartyDependenciesTests
{
    /// <summary>
    ///     The name of the application's dependency manifest, which the build writes next to the
    ///     application (and copies into this test project's output with it).
    /// </summary>
    private const string ApplicationDepsFile = "DemaConsulting.AgentControl.deps.json";

    /// <summary>
    ///     Test that the dependency list's names match the application's direct runtime dependencies
    ///     exactly, order-independent, so the hand-maintained list cannot silently drift from what the
    ///     application ships. The direct runtime dependencies are read from the dependency manifest the
    ///     build produced for the application: the packages the application depends on directly that
    ///     contribute runtime or native files of their own (build-only and analyzer packages contribute
    ///     none). Versions are deliberately not compared so dependency-update tooling can bump them
    ///     freely. The license column cannot be derived from the manifest, so it is checked only for
    ///     non-emptiness by <see cref="ThirdPartyDependencies_All_EveryEntryHasNonEmptyLicense"/>.
    /// </summary>
    [Fact]
    public void ThirdPartyDependencies_All_MatchesTheApplicationsDirectRuntimeDependencies()
    {
        // Arrange: the direct runtime dependencies recorded in the application's build output
        var expected = ReadDirectRuntimeDependencies(Path.Combine(AppContext.BaseDirectory, ApplicationDepsFile))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        // Act
        var actual = ThirdPartyDependencies.All
            .Select(d => d.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        // Assert: the hand-maintained list matches what the application ships
        Assert.NotEmpty(expected);
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
    ///     Test that DependencyInfo's ToString() override renders the expected display format.
    /// </summary>
    [Fact]
    public void DependencyInfo_ToString_RepresentativeEntry_ReturnsNameLicenseFormat()
    {
        // Arrange
        var dependency = new DependencyInfo("Serilog", "Apache-2.0");

        // Act
        var text = dependency.ToString();

        // Assert
        Assert.Equal("Serilog — Apache-2.0", text);
    }

    /// <summary>
    ///     Reads the names of the application's direct runtime dependencies from its dependency
    ///     manifest: the packages listed as dependencies of the application itself whose own entry
    ///     contributes runtime or native files.
    /// </summary>
    /// <param name="depsFilePath">The path of the application's <c>.deps.json</c> file.</param>
    /// <returns>The direct runtime dependencies' names.</returns>
    private static List<string> ReadDirectRuntimeDependencies(string depsFilePath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(depsFilePath));
        var root = document.RootElement;
        var runtimeTarget = root.GetProperty("runtimeTarget").GetProperty("name").GetString()!;
        var target = root.GetProperty("targets").GetProperty(runtimeTarget);
        var libraries = root.GetProperty("libraries");

        var application = target.EnumerateObject()
            .Single(entry => entry.Name.StartsWith("DemaConsulting.AgentControl/", StringComparison.Ordinal));

        var names = new List<string>();
        foreach (var dependency in application.Value.GetProperty("dependencies").EnumerateObject())
        {
            var key = $"{dependency.Name}/{dependency.Value.GetString()}";
            var isPackage = libraries.GetProperty(key).GetProperty("type").GetString() == "package";
            var entry = target.GetProperty(key);
            if (isPackage && (entry.TryGetProperty("runtime", out _) || entry.TryGetProperty("native", out _)))
            {
                names.Add(dependency.Name);
            }
        }

        return names;
    }
}
