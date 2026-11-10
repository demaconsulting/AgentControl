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

using Avalonia.Controls;
using Avalonia.Threading;
using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.Settings;
using Material.Icons;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Tests of the status a row of the Settings window's source list shows: the neutral "Online
///     source" status of a remote source, the unchanged folder statuses of a local one, and the
///     "Signed only" badge of a source that requires signed packages
///     (<see cref="PackageSourceItemViewModel"/>).
/// </summary>
public class PackageSourceStatusPresentationTests
{
    /// <summary>
    ///     A 64-digit certificate fingerprint, written the way a user pastes it (with separators).
    /// </summary>
    private const string Fingerprint =
        "ab:12:cd:34:ef:56:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:98:76";

    /// <summary>
    ///     Test that a NuGet server row is an "Online source": a neutral cloud icon, no folder state,
    ///     and a tooltip that says when the source is contacted and where to test it now.
    /// </summary>
    [Fact]
    public void PackageSourceItemViewModel_ServerSource_ShowsNeutralOnlineSourceStatus()
    {
        // Arrange / Act
        var row = new PackageSourceItemViewModel(
            new PackageSourceDefinition
            {
                Alias = "Feed",
                Type = PackageSourceType.NuGet,
                Location = "https://feeds.example.com/v3/index.json"
            }, 0);

        // Assert
        Assert.Equal("Online source", row.StatusText);
        Assert.Equal(MaterialIconKind.CloudOutline, row.StatusIcon);
        Assert.False(row.IsAvailable);
        Assert.False(row.IsMissing);
        Assert.False(row.IsChecking);
        Assert.Contains("only when it needs it", row.StatusTooltip, StringComparison.Ordinal);
        Assert.Contains("syncing at launch", row.StatusTooltip, StringComparison.Ordinal);
        Assert.Contains("Test connection", row.StatusTooltip, StringComparison.Ordinal);
        Assert.DoesNotContain("not checked", row.StatusTooltip, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Feed, NuGet-package server, Online source", row.AutomationName);
    }

    /// <summary>
    ///     Test that a local folder source keeps its folder statuses (checking, found, not found).
    /// </summary>
    [Fact]
    public async Task PackageSourceItemViewModel_LocalFolderSource_KeepsFolderStatuses()
    {
        // Arrange
        var found = new PackageSourceItemViewModel(
            new PackageSourceDefinition { Alias = "Zips", Type = PackageSourceType.Zip, Location = @"C:\zips" }, 0);
        var missing = new PackageSourceItemViewModel(
            new PackageSourceDefinition { Alias = "Gone", Type = PackageSourceType.Zip, Location = @"C:\gone" }, 0);
        Assert.Equal("Checking...", found.StatusText);
        Assert.Equal(MaterialIconKind.ProgressClock, found.StatusIcon);

        // Act
        await found.CheckStatusAsync((_, _) => Task.FromResult(true), CancellationToken.None);
        await missing.CheckStatusAsync((_, _) => Task.FromResult(false), CancellationToken.None);

        // Assert
        Assert.Equal("Folder found", found.StatusText);
        Assert.Equal(MaterialIconKind.CheckCircleOutline, found.StatusIcon);
        Assert.Equal("Folder not found", missing.StatusText);
        Assert.Equal(MaterialIconKind.AlertCircleOutline, missing.StatusIcon);
    }

    /// <summary>
    ///     Test that a NuGet source (folder or server) that requires signed packages shows the
    ///     "Signed only" badge, and that a source that does not, a zip folder and a Git repository
    ///     never do.
    /// </summary>
    [Fact]
    public void PackageSourceItemViewModel_RequiresSignature_OnlyForSignedNuGetSources()
    {
        // Arrange
        PackageSourceItemViewModel Row(PackageSourceType type, string location, bool signed) =>
            new(new PackageSourceDefinition { Alias = "S", Type = type, Location = location, RequireSignedPackages = signed }, 0);

        // Act / Assert
        Assert.True(Row(PackageSourceType.NuGet, @"C:\feed", signed: true).RequiresSignature);
        Assert.True(Row(PackageSourceType.NuGet, "https://feeds.example.com/v3/index.json", signed: true).RequiresSignature);
        Assert.False(Row(PackageSourceType.NuGet, @"C:\feed", signed: false).RequiresSignature);
        Assert.False(Row(PackageSourceType.Zip, @"C:\zips", signed: true).RequiresSignature);
        Assert.False(Row(PackageSourceType.Git, "https://git.example.com/org/r.git", signed: true).RequiresSignature);
    }

    /// <summary>
    ///     Test that the badge's tooltip says signed packages are required and, without a pinned
    ///     publisher, that any trusted signer is accepted.
    /// </summary>
    [Fact]
    public void PackageSourceItemViewModel_SignatureTooltip_NoPinnedPublisher_SaysAnyTrustedSigner()
    {
        // Arrange
        var row = new PackageSourceItemViewModel(
            new PackageSourceDefinition
            {
                Alias = "S",
                Type = PackageSourceType.NuGet,
                Location = @"C:\feed",
                RequireSignedPackages = true
            }, 0);

        // Act
        var tooltip = row.SignatureTooltip;

        // Assert
        Assert.Contains("Signed packages are required", tooltip, StringComparison.Ordinal);
        Assert.Contains("trusted publisher", tooltip, StringComparison.Ordinal);
        Assert.DoesNotContain("pinned", tooltip, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that, with a pinned publisher, the tooltip says only that publisher is accepted and
    ///     shows just the start and end of its fingerprint, never the whole fingerprint.
    /// </summary>
    [Fact]
    public void PackageSourceItemViewModel_SignatureTooltip_PinnedPublisher_ShowsOnlyStartAndEndOfFingerprint()
    {
        // Arrange
        var row = new PackageSourceItemViewModel(
            new PackageSourceDefinition
            {
                Alias = "S",
                Type = PackageSourceType.NuGet,
                Location = @"C:\feed",
                RequireSignedPackages = true,
                SignerFingerprint = Fingerprint
            }, 0);

        // Act
        var tooltip = row.SignatureTooltip;

        // Assert: the first 8 and last 4 digits, and not the rest
        Assert.Contains("Signed packages are required", tooltip, StringComparison.Ordinal);
        Assert.Contains("pinned publisher", tooltip, StringComparison.Ordinal);
        Assert.Contains("AB12CD34…9876", tooltip, StringComparison.Ordinal);
        Assert.DoesNotContain("0000000000", tooltip, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the Settings window shows the "Signed only" badge (with its AutomationId and
    ///     tooltip) on the rows of sources that require signed packages and hides it on the others,
    ///     and shows "Online source" for the server row.
    /// </summary>
    [Fact]
    public void SettingsWindow_SourceRows_ShowSignedOnlyBadgeOnlyWhereRequiredAndOnlineSourceStatus() => HeadlessTestApp.Run(() =>
    {
        // Arrange: a signed folder, a plain zip folder, a signed and pinned server
        var settings = new AppSettings
        {
            Sources =
            [
                new PackageSourceDefinition
                {
                    Alias = "SignedFolder", Type = PackageSourceType.NuGet, Location = @"C:\feed", RequireSignedPackages = true
                },
                new PackageSourceDefinition { Alias = "PlainZips", Type = PackageSourceType.Zip, Location = @"C:\zips" },
                new PackageSourceDefinition
                {
                    Alias = "Server",
                    Type = PackageSourceType.NuGet,
                    Location = "https://feeds.example.com/v3/index.json",
                    RequireSignedPackages = true,
                    SignerFingerprint = Fingerprint
                }
            ]
        };
        var viewModel = new SettingsWindowViewModel(settings, _ => { }, (_, _) => Task.FromResult(true));
        var window = new SettingsWindow(viewModel);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            // Act
            var rows = window.GetVisualDescendantsOf<Border>("SourceSignedOnlyBadge").ToList();

            // Assert: one badge per row, visible only for the two signed sources
            Assert.Equal(3, rows.Count);
            Assert.Equal(
                [true, false, true],
                rows.Select(badge => ((PackageSourceItemViewModel)badge.DataContext!).RequiresSignature));
            Assert.Equal([true, false, true], rows.Select(badge => badge.IsVisible));
            Assert.All(
                rows.Where(badge => badge.IsVisible),
                badge => Assert.False(string.IsNullOrEmpty(ToolTip.GetTip(badge)?.ToString())));
            Assert.Contains(
                "AB12CD34…9876", ToolTip.GetTip(rows[2])!.ToString(), StringComparison.Ordinal);

            // Assert: the server row says "Online source" under the existing AutomationId
            var statuses = window.GetVisualDescendantsOf<TextBlock>("SourceStatusText").Select(text => text.Text).ToList();
            Assert.Contains("Online source", statuses);
            Assert.DoesNotContain("Not checked", statuses);
        }
        finally
        {
            window.Close();
        }
    });
}
