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
using DemaConsulting.AgentControl.LauncherUI;
using Material.Icons;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Tests of how a Git source is presented: the icon and wording from
///     <see cref="PackageSourceIcons"/>, and whether a Settings row for it is remote (an online source)
///     or has a folder status (<see cref="PackageSourceItemViewModel"/>).
/// </summary>
public class PackageSourceGitPresentationTests
{
    /// <summary>
    ///     Test that a Git source has the Git icon and its own kind names, different from the other
    ///     source kinds.
    /// </summary>
    [Fact]
    public void PackageSourceIcons_GitSource_HasGitIconAndNames()
    {
        // Arrange
        var git = new PackageSourceDefinition { Alias = "Repo", Type = PackageSourceType.Git, Location = "https://git.example.com/org/r.git" };
        var folder = new PackageSourceDefinition { Alias = "Folder", Type = PackageSourceType.Zip, Location = @"C:\zips" };

        // Act / Assert
        Assert.Equal(MaterialIconKind.Git, PackageSourceIcons.GetIcon(git));
        Assert.Equal("Git repository", PackageSourceIcons.GetTypeName(git));
        Assert.Equal("git repository", PackageSourceIcons.GetShortName(git));
        Assert.NotEqual(PackageSourceIcons.GetIcon(folder), PackageSourceIcons.GetIcon(git));
    }

    /// <summary>
    ///     Test that a row for a Git source reached over https or ssh is remote and an "Online source"
    ///     (a neutral cloud status, not a warning), and that its folder check is a no-op.
    /// </summary>
    /// <param name="location">The repository address.</param>
    [Theory]
    [InlineData("https://git.example.com/org/agents.git")]
    [InlineData("ssh://git@git.example.com/org/agents.git")]
    [InlineData("git@git.example.com:org/agents.git")]
    public async Task PackageSourceItemViewModel_RemoteGitSource_IsOnlineSourceAndNeverAsksTheFolder(string location)
    {
        // Arrange
        var row = new PackageSourceItemViewModel(
            new PackageSourceDefinition { Alias = "Repo", Type = PackageSourceType.Git, Location = location }, 0);
        var asked = false;

        // Act
        await row.CheckStatusAsync((_, _) => { asked = true; return Task.FromResult(true); }, CancellationToken.None);

        // Assert
        Assert.True(row.IsRemote);
        Assert.False(row.IsServer);
        Assert.Equal("Online source", row.StatusText);
        Assert.Equal(MaterialIconKind.CloudOutline, row.StatusIcon);
        Assert.False(row.IsAvailable);
        Assert.False(row.IsMissing);
        Assert.False(row.IsChecking);
        Assert.False(asked);
    }

    /// <summary>
    ///     Test that a row for a Git source at a local path starts in the "checking" state and ends
    ///     with the folder status, with the folder icons.
    /// </summary>
    [Fact]
    public async Task PackageSourceItemViewModel_LocalGitSource_ChecksTheFolder()
    {
        // Arrange
        var path = Path.Combine(Path.GetTempPath(), "agents.git");
        var row = new PackageSourceItemViewModel(
            new PackageSourceDefinition { Alias = "Repo", Type = PackageSourceType.Git, Location = path }, 0);
        Assert.False(row.IsRemote);
        Assert.True(row.IsChecking);
        Assert.Equal("Checking...", row.StatusText);

        // Act
        await row.CheckStatusAsync((_, _) => Task.FromResult(true), CancellationToken.None);

        // Assert
        Assert.Equal("Folder found", row.StatusText);
        Assert.Equal(MaterialIconKind.CheckCircleOutline, row.StatusIcon);
        Assert.True(row.IsAvailable);
    }

    /// <summary>
    ///     Test that a NuGet server is still remote and a zip folder is still not, so Git did not change
    ///     the other kinds.
    /// </summary>
    [Fact]
    public void PackageSourceItemViewModel_OtherKinds_RemoteClassificationUnchanged()
    {
        // Arrange
        var server = new PackageSourceItemViewModel(
            new PackageSourceDefinition { Alias = "Feed", Type = PackageSourceType.NuGet, Location = "https://feeds.example.com/v3/index.json" }, 0);
        var zip = new PackageSourceItemViewModel(
            new PackageSourceDefinition { Alias = "Zips", Type = PackageSourceType.Zip, Location = @"C:\zips" }, 0);

        // Assert
        Assert.True(server.IsRemote);
        Assert.False(zip.IsRemote);
    }
}
