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
///     Unit tests for <see cref="RepoGroupHeaderViewModel"/>: what a section header says about its
///     section and how its commands raise their requests.
/// </summary>
public class RepoGroupHeaderViewModelTests
{
    /// <summary>
    ///     Test that the kind decides which icon and which actions the header offers.
    /// </summary>
    [Fact]
    public void RepoGroupHeaderViewModel_Kind_SelectsSectionFlags()
    {
        // Act
        var favorites = new RepoGroupHeaderViewModel(RepoGroupKind.Favorites, "Favorites");
        var group = new RepoGroupHeaderViewModel(RepoGroupKind.Group, "Work");
        var ungrouped = new RepoGroupHeaderViewModel(RepoGroupKind.Ungrouped, "Ungrouped");

        // Assert
        Assert.Multiple(
            () => Assert.True(favorites.IsFavoritesSection),
            () => Assert.False(favorites.IsUserGroup),
            () => Assert.True(group.IsUserGroup),
            () => Assert.False(group.IsUngroupedSection),
            () => Assert.True(ungrouped.IsUngroupedSection),
            () => Assert.False(ungrouped.IsUserGroup));
    }

    /// <summary>
    ///     Test that the accessible name and the tooltips describe the section, the count and the state.
    /// </summary>
    [Fact]
    public void RepoGroupHeaderViewModel_Update_DescribesCountAndState()
    {
        // Arrange
        var header = new RepoGroupHeaderViewModel(RepoGroupKind.Group, "Work");
        var changed = new List<string?>();
        header.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        // Act: one repo, expanded
        header.Update(count: 1, isCollapsed: false, isToggleEnabled: true);

        // Assert
        Assert.Equal("Work, 1 repo, expanded", header.AccessibleName);
        Assert.Equal("Collapse Work", header.ToggleTooltip);
        Assert.Equal("1 repo", header.CountTooltip);
        Assert.True(header.IsExpanded);

        // Act: collapsed with several repos
        header.Update(count: 3, isCollapsed: true, isToggleEnabled: true);

        // Assert
        Assert.Equal("Work, 3 repos, collapsed", header.AccessibleName);
        Assert.Equal("Expand Work", header.ToggleTooltip);
        Assert.False(header.IsExpanded);
        Assert.Contains(nameof(RepoGroupHeaderViewModel.AccessibleName), changed);
    }

    /// <summary>
    ///     Test that while toggling is disabled (a filter is active) the toggle command is unavailable
    ///     and the tooltip says why, and that the command raises its request otherwise.
    /// </summary>
    [Fact]
    public void RepoGroupHeaderViewModel_ToggleCommand_FollowsToggleEnabled()
    {
        // Arrange
        var header = new RepoGroupHeaderViewModel(RepoGroupKind.Group, "Work");
        var requests = 0;
        header.ToggleRequested += (_, _) => requests++;
        header.Update(2, isCollapsed: false, isToggleEnabled: true);

        // Act / Assert: enabled
        Assert.True(header.ToggleCommand.CanExecute(null));
        header.ToggleCommand.Execute(null);
        Assert.Equal(1, requests);

        // Act / Assert: disabled while filtering
        header.Update(2, isCollapsed: false, isToggleEnabled: false);
        Assert.False(header.ToggleCommand.CanExecute(null));
        Assert.Contains("filter", header.ToggleTooltip, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the rename and remove commands only raise their requests.
    /// </summary>
    [Fact]
    public void RepoGroupHeaderViewModel_RenameAndRemoveCommands_RaiseRequests()
    {
        // Arrange
        var header = new RepoGroupHeaderViewModel(RepoGroupKind.Group, "Work");
        var renames = 0;
        var removals = 0;
        header.RenameRequested += (_, _) => renames++;
        header.RemoveGroupRequested += (_, _) => removals++;

        // Act
        header.RenameCommand.Execute(null);
        header.RemoveGroupCommand.Execute(null);
        header.RemoveGroupCommand.Execute(null);

        // Assert
        Assert.Equal(1, renames);
        Assert.Equal(2, removals);
    }

    /// <summary>
    ///     Test that the header's text form is its accessible name, which names the hosting list item
    ///     for assistive technology instead of the type name.
    /// </summary>
    [Fact]
    public void RepoGroupHeaderViewModel_ToString_IsTheAccessibleName()
    {
        // Arrange
        var header = new RepoGroupHeaderViewModel(RepoGroupKind.Favorites, "Favorites");
        header.Update(2, isCollapsed: true, isToggleEnabled: true);

        // Act / Assert
        Assert.Equal("Favorites, 2 repos, collapsed", header.ToString());
    }

    /// <summary>
    ///     Test that a header needs a name.
    /// </summary>
    [Fact]
    public void RepoGroupHeaderViewModel_Constructor_BlankName_Throws()
    {
        // Act / Assert
        Assert.Throws<ArgumentException>(() => new RepoGroupHeaderViewModel(RepoGroupKind.Group, "  "));
        Assert.Throws<ArgumentNullException>(() => new RepoGroupHeaderViewModel(RepoGroupKind.Group, null!));
    }
}
