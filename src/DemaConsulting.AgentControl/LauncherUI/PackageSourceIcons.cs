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

using DemaConsulting.AgentControl.AgentPackageManagement;
using Material.Icons;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     Maps a configured package source to the icon and wording the UI uses to show what kind of
///     source it is, so the Settings list, the Select Package window and the repo card chips
///     present a source consistently.
/// </summary>
/// <remarks>
///     Kept as one small static helper (rather than repeated switch expressions in each view
///     model) because a source type icon must mean the same thing everywhere it appears. Pure
///     and thread-safe.
/// </remarks>
internal static class PackageSourceIcons
{
    /// <summary>
    ///     Gets the icon for a source: a zipped folder for a zip-package folder, a package for a
    ///     NuGet-package folder, a server for a NuGet-package server and the Git logo for a Git
    ///     repository.
    /// </summary>
    /// <param name="definition">The source, or <see langword="null"/> when its definition is not
    ///     known (for example a pin naming a source that is not configured).</param>
    /// <returns>The icon kind; a question-mark icon when <paramref name="definition"/> is
    ///     <see langword="null"/>.</returns>
    public static MaterialIconKind GetIcon(PackageSourceDefinition? definition) => definition switch
    {
        null => MaterialIconKind.HelpCircleOutline,
        { Type: PackageSourceType.Zip } => MaterialIconKind.FolderZip,
        { Type: PackageSourceType.Git } => MaterialIconKind.Git,
        { Type: PackageSourceType.NuGet, LocationKind: PackageSourceLocationKind.Server } => MaterialIconKind.ServerNetwork,
        _ => MaterialIconKind.PackageVariant
    };

    /// <summary>
    ///     Gets the full, capitalized name of a source's kind, for list rows and headings.
    /// </summary>
    /// <param name="definition">The source, or <see langword="null"/> when unknown.</param>
    /// <returns>A name such as <c>Zip-package folder</c>; <c>Unknown source</c> when
    ///     <paramref name="definition"/> is <see langword="null"/>.</returns>
    public static string GetTypeName(PackageSourceDefinition? definition) => definition switch
    {
        null => "Unknown source",
        { Type: PackageSourceType.Zip } => "Zip-package folder",
        { Type: PackageSourceType.Git } => "Git repository",
        { Type: PackageSourceType.NuGet, LocationKind: PackageSourceLocationKind.Server } => "NuGet-package server",
        _ => "NuGet-package folder"
    };

    /// <summary>
    ///     Gets a short lower-case description of a source's kind, for use inside a sentence such
    ///     as a tooltip.
    /// </summary>
    /// <param name="definition">The source, or <see langword="null"/> when unknown.</param>
    /// <returns>A phrase such as <c>zip folder</c>; <c>source not configured</c> when
    ///     <paramref name="definition"/> is <see langword="null"/>.</returns>
    public static string GetShortName(PackageSourceDefinition? definition) => definition switch
    {
        null => "source not configured",
        { Type: PackageSourceType.Zip } => "zip folder",
        { Type: PackageSourceType.Git } => "git repository",
        { Type: PackageSourceType.NuGet, LocationKind: PackageSourceLocationKind.Server } => "NuGet server",
        _ => "NuGet folder"
    };
}
