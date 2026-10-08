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

using DemaConsulting.AgentControl.LauncherUI;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Unit tests for <see cref="ThirdPartyDependencies"/>.
/// </summary>
public class ThirdPartyDependenciesTests
{
    /// <summary>
    ///     The expected direct runtime dependencies, matching
    ///     DemaConsulting.AgentControl.csproj's "Logging Dependencies" and "Avalonia UI
    ///     Dependencies" item groups.
    /// </summary>
    private static readonly (string Name, string Version, string License)[] Expected =
    [
        ("Microsoft.Extensions.Logging.Abstractions", "10.0.12", "MIT"),
        ("Serilog", "4.4.0", "Apache-2.0"),
        ("Serilog.Extensions.Logging", "10.0.0", "Apache-2.0"),
        ("Serilog.Sinks.File", "7.0.0", "Apache-2.0"),
        ("Avalonia", "12.1.3", "MIT"),
        ("Avalonia.Desktop", "12.1.3", "MIT"),
        ("Avalonia.Themes.Fluent", "12.1.3", "MIT"),
        ("Material.Icons.Avalonia", "3.0.2", "MIT")
    ];

    /// <summary>
    ///     Test that the dependency list contains exactly the expected set of direct runtime
    ///     dependencies, matching the csproj's current package references.
    /// </summary>
    [Fact]
    public void ThirdPartyDependencies_All_MatchesCsprojDirectRuntimeDependencies()
    {
        // Arrange: project the actual list into comparable tuples
        var actual = ThirdPartyDependencies.All
            .Select(d => (d.Name, d.Version, d.License))
            .ToArray();

        // Act / Assert: the actual list matches the expected set exactly (order-independent)
        Assert.Equal(Expected.Length, actual.Length);
        Assert.Equal(Expected.OrderBy(e => e.Name), actual.OrderBy(a => a.Name));
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
}
