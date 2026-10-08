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

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     Identifies a single direct runtime NuGet dependency shown in <see cref="AboutWindow"/>'s
///     third-party dependency list.
/// </summary>
/// <param name="Name">The NuGet package name.</param>
/// <param name="Version">The pinned package version, as it appears in the project file.</param>
/// <param name="License">The package's SPDX license identifier.</param>
internal sealed record DependencyInfo(string Name, string Version, string License)
{
    /// <summary>
    ///     Returns the display text shown for this dependency in <see cref="AboutWindow"/>'s
    ///     dependency list.
    /// </summary>
    /// <returns>A string of the form <c>"{Name} {Version} — {License}"</c>.</returns>
    public override string ToString()
    {
        return $"{Name} {Version} — {License}";
    }
}

/// <summary>
///     Static list of this application's direct runtime NuGet dependencies and their SPDX
///     license identifiers, shown in <see cref="AboutWindow"/>'s third-party dependency list.
/// </summary>
/// <remarks>
///     This list is hand-maintained to mirror the direct (non-build/analyzer-only)
///     <c>PackageReference</c> items in <c>DemaConsulting.AgentControl.csproj</c>'s "Logging
///     Dependencies" and "Avalonia UI Dependencies" item groups, in the same order as they
///     appear there. It is not parsed from the project file at runtime, so it must be reviewed
///     and updated whenever those dependencies or their versions change.
/// </remarks>
internal static class ThirdPartyDependencies
{
    /// <summary>
    ///     This application's direct runtime NuGet dependencies, in the same order as the
    ///     "Logging Dependencies" and "Avalonia UI Dependencies" item groups in
    ///     <c>DemaConsulting.AgentControl.csproj</c>.
    /// </summary>
    public static IReadOnlyList<DependencyInfo> All { get; } =
    [
        new DependencyInfo("Microsoft.Extensions.Logging.Abstractions", "10.0.12", "MIT"),
        new DependencyInfo("Serilog", "4.4.0", "Apache-2.0"),
        new DependencyInfo("Serilog.Extensions.Logging", "10.0.0", "Apache-2.0"),
        new DependencyInfo("Serilog.Sinks.File", "7.0.0", "Apache-2.0"),
        new DependencyInfo("Avalonia", "12.1.3", "MIT"),
        new DependencyInfo("Avalonia.Desktop", "12.1.3", "MIT"),
        new DependencyInfo("Avalonia.Themes.Fluent", "12.1.3", "MIT"),
        new DependencyInfo("Material.Icons.Avalonia", "3.0.2", "MIT")
    ];
}
