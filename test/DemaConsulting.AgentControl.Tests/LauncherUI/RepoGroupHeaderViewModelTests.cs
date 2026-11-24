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
    ///     Test that the kind decides the section's classification: for every kind exactly its own
    ///     flag is set and the other two are not (a full truth table).
    /// </summary>
    /// <param name="kindName">The name of the kind of section (a member of
    ///     <see cref="RepoGroupKind"/>; the enum is internal, so the test takes its name).</param>
    /// <param name="favorites">Whether the favorites flag is expected.</param>
    /// <param name="userGroup">Whether the user-group flag is expected.</param>
    /// <param name="ungrouped">Whether the ungrouped flag is expected.</param>
    [Theory]
    [InlineData(nameof(RepoGroupKind.Favorites), true, false, false)]
    [InlineData(nameof(RepoGroupKind.Group), false, true, false)]
    [InlineData(nameof(RepoGroupKind.Ungrouped), false, false, true)]
    public void RepoGroupHeaderViewModel_Kind_SelectsExactlyTheMatchingSectionFlag(
        string kindName, bool favorites, bool userGroup, bool ungrouped)
    {
        // Arrange
        var kind = Enum.Parse<RepoGroupKind>(kindName);

        // Act
        var header = new RepoGroupHeaderViewModel(kind, "Name");

        // Assert: all three flags, so exactly the matching one is set
        Assert.Multiple(
            () => Assert.Equal(kind, header.Kind),
            () => Assert.Equal(favorites, header.IsFavoritesSection),
            () => Assert.Equal(userGroup, header.IsUserGroup),
            () => Assert.Equal(ungrouped, header.IsUngroupedSection));
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
    ///     Test that the add-repository command only raises its request.
    /// </summary>
    [Fact]
    public void RepoGroupHeaderViewModel_AddRepoCommand_RaisesRequest()
    {
        // Arrange
        var header = new RepoGroupHeaderViewModel(RepoGroupKind.Group, "Work");
        var requests = 0;
        header.AddRepoRequested += (_, _) => requests++;

        // Act
        header.AddRepoCommand.Execute(null);
        header.AddRepoCommand.Execute(null);

        // Assert
        Assert.Equal(2, requests);
    }

    /// <summary>
    ///     Test that the new-subgroup command only raises its request.
    /// </summary>
    [Fact]
    public void RepoGroupHeaderViewModel_NewSubgroupCommand_RaisesRequest()
    {
        // Arrange
        var header = new RepoGroupHeaderViewModel(RepoGroupKind.Group, "Work");
        var requests = 0;
        header.NewSubgroupRequested += (_, _) => requests++;

        // Act
        header.NewSubgroupCommand.Execute(null);

        // Assert
        Assert.Equal(1, requests);
    }

    /// <summary>
    ///     Test that a nested group knows its depth, parent and indent (capped so a deep group stays in
    ///     view), says which group it is inside in its accessible name, and that a count that includes
    ///     subgroups says how many repos are directly in the group.
    /// </summary>
    [Fact]
    public void RepoGroupHeaderViewModel_UpdatePlace_DescribesDepthParentIndentAndDirectCount()
    {
        // Arrange
        var header = new RepoGroupHeaderViewModel(RepoGroupKind.Group, "Project");
        header.Update(count: 3, isCollapsed: false, isToggleEnabled: true, directCount: 1);

        // Assert: a top-level group
        Assert.Multiple(
            () => Assert.Equal(0, header.Depth),
            () => Assert.False(header.HasParent),
            () => Assert.Equal(0, header.IndentWidth),
            () => Assert.Equal("Project, 3 repos, expanded", header.AccessibleName),
            () => Assert.Equal("3 repos (1 directly in this group)", header.CountTooltip));

        // Act
        header.UpdatePlace(depth: 2, parentName: "Client");

        // Assert
        Assert.Multiple(
            () => Assert.Equal(2, header.Depth),
            () => Assert.Equal("Client", header.ParentName),
            () => Assert.True(header.HasParent),
            () => Assert.Equal(2 * RepoGroupHeaderViewModel.IndentPerLevel, header.IndentWidth),
            () => Assert.Equal("Project, 3 repos, expanded, inside Client", header.AccessibleName));

        // Act: deeper than the indent goes
        header.UpdatePlace(depth: 40, parentName: "Deep");

        // Assert
        Assert.Equal(
            RepoGroupHeaderViewModel.MaxIndentedDepth * RepoGroupHeaderViewModel.IndentPerLevel, header.IndentWidth);
        Assert.Equal(40, header.Depth);
    }

    /// <summary>
    ///     Test that the built-in sections are never inside a group and so ignore the place they are given.
    /// </summary>
    /// <param name="kindName">The name of the kind of built-in section.</param>
    [Theory]
    [InlineData(nameof(RepoGroupKind.Favorites))]
    [InlineData(nameof(RepoGroupKind.Ungrouped))]
    public void RepoGroupHeaderViewModel_UpdatePlace_BuiltInSection_StaysTopLevel(string kindName)
    {
        // Arrange
        var header = new RepoGroupHeaderViewModel(Enum.Parse<RepoGroupKind>(kindName), "Section");

        // Act
        header.UpdatePlace(depth: 3, parentName: "Client");

        // Assert
        Assert.Multiple(
            () => Assert.Equal(0, header.Depth),
            () => Assert.Null(header.ParentName),
            () => Assert.Equal(0, header.IndentWidth));
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

    /// <summary>
    ///     Test that a user group's color is exposed by name, as the accent's style class, as the
    ///     palette heading and as the accent's description (so it is never conveyed by color alone),
    ///     with change notifications, and that clearing it leaves no color.
    /// </summary>
    [Fact]
    public void RepoGroupHeaderViewModel_UpdateColor_ExposesNameClassHeadingAndDescription()
    {
        // Arrange
        var header = new RepoGroupHeaderViewModel(RepoGroupKind.Group, "Work");
        var changed = new List<string?>();
        header.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        // Assert: no color at first
        Assert.False(header.HasColor);
        Assert.Equal("Group color: None", header.ColorHeading);

        // Act: a color, in any letter case
        header.UpdateColor("teal");

        // Assert
        Assert.Multiple(
            () => Assert.Equal("Teal", header.ColorName),
            () => Assert.True(header.HasColor),
            () => Assert.Equal("groupAccent5", header.ColorStyleClass),
            () => Assert.Equal("Group color: Teal", header.ColorHeading),
            () => Assert.Equal("Group color: Teal", header.ColorDescription),
            () => Assert.Contains(nameof(RepoGroupHeaderViewModel.ColorStyleClass), changed),
            () => Assert.Contains(nameof(RepoGroupHeaderViewModel.HasColor), changed));

        // Act: an unknown color, then none
        header.UpdateColor("Plaid");
        Assert.Null(header.ColorName);
        header.UpdateColor("Red");
        header.UpdateColor(null);

        // Assert
        Assert.False(header.HasColor);
        Assert.Equal(string.Empty, header.ColorStyleClass);
    }

    /// <summary>
    ///     Test that Favorites and Ungrouped never show a color, whatever they are given.
    /// </summary>
    /// <param name="kindName">The name of the built-in kind of section.</param>
    [Theory]
    [InlineData(nameof(RepoGroupKind.Favorites))]
    [InlineData(nameof(RepoGroupKind.Ungrouped))]
    public void RepoGroupHeaderViewModel_UpdateColor_BuiltInSection_StaysUncolored(string kindName)
    {
        // Arrange
        var header = new RepoGroupHeaderViewModel(Enum.Parse<RepoGroupKind>(kindName), kindName);

        // Act
        header.UpdateColor("Teal");

        // Assert
        Assert.False(header.HasColor);
        Assert.Null(header.ColorName);
    }

    /// <summary>
    ///     Test that the Move up and Move down commands raise move requests only while they are
    ///     available, and follow what the owner says about availability.
    /// </summary>
    [Fact]
    public void RepoGroupHeaderViewModel_MoveCommands_RaiseRequestsAndFollowAvailability()
    {
        // Arrange
        var header = new RepoGroupHeaderViewModel(RepoGroupKind.Group, "Work");
        var requests = new List<RepoGroupMove>();
        header.MoveRequested += (_, move) => requests.Add(move);
        var notified = 0;
        header.MoveUpCommand.CanExecuteChanged += (_, _) => notified++;

        // Assert: unavailable until the owner says where the group stands
        Assert.False(header.MoveUpCommand.CanExecute(null));
        Assert.False(header.MoveDownCommand.CanExecute(null));

        // Act: both available
        header.UpdateOrder(null, null);
        header.MoveUpCommand.Execute(null);
        header.MoveDownCommand.Execute(null);

        // Assert
        Assert.Equal([RepoGroupMove.Up, RepoGroupMove.Down], requests);
        Assert.True(notified > 0);

        // Act: only down
        header.UpdateOrder("first", null);

        // Assert
        Assert.False(header.MoveUpCommand.CanExecute(null));
        Assert.True(header.MoveDownCommand.CanExecute(null));
    }

    /// <summary>
    ///     Test that a move that is unavailable explains why in its tooltip, an available one says
    ///     what it does, and that Favorites and Ungrouped never offer a move.
    /// </summary>
    [Fact]
    public void RepoGroupHeaderViewModel_UpdateOrder_DisabledMoveExplainsWhy()
    {
        // Arrange
        var header = new RepoGroupHeaderViewModel(RepoGroupKind.Group, "Work");
        var favorites = new RepoGroupHeaderViewModel(RepoGroupKind.Favorites, "Favorites");
        var changed = new List<string?>();
        header.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        // Act
        header.UpdateOrder("This group is already the first group.", null);
        favorites.UpdateOrder(null, null);

        // Assert
        Assert.Equal("This group is already the first group.", header.MoveUpTooltip);
        Assert.Equal("Move this group one place down in the list", header.MoveDownTooltip);
        Assert.Contains(nameof(RepoGroupHeaderViewModel.MoveUpTooltip), changed);
        Assert.False(favorites.MoveUpCommand.CanExecute(null));
        Assert.False(favorites.MoveDownCommand.CanExecute(null));
        Assert.False(string.IsNullOrWhiteSpace(favorites.MoveUpTooltip));
    }

    /// <summary>
    ///     Test that "Sort by name" is unavailable until the owner says the group can be sorted, raises its
    ///     request when executed, explains why it is unavailable in its tooltip, and is never offered for
    ///     Favorites or Ungrouped.
    /// </summary>
    [Fact]
    public void RepoGroupHeaderViewModel_UpdateSort_AvailabilityTooltipAndRequest()
    {
        // Arrange
        var header = new RepoGroupHeaderViewModel(RepoGroupKind.Group, "Work");
        var ungrouped = new RepoGroupHeaderViewModel(RepoGroupKind.Ungrouped, "Ungrouped");
        var requests = 0;
        header.SortByNameRequested += (_, _) => requests++;
        var changed = new List<string?>();
        header.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        // Assert: unavailable until the owner says where the group stands
        Assert.False(header.SortByNameCommand.CanExecute(null));

        // Act: available
        header.UpdateSort(null);
        header.SortByNameCommand.Execute(null);

        // Assert
        Assert.Equal(1, requests);
        Assert.True(header.SortByNameCommand.CanExecute(null));
        Assert.Contains("alphabetically", header.SortByNameTooltip);

        // Act: unavailable with a reason; the built-in section ignores an "available" answer
        header.UpdateSort("Already sorted.");
        ungrouped.UpdateSort(null);

        // Assert
        Assert.False(header.SortByNameCommand.CanExecute(null));
        Assert.Equal("Already sorted.", header.SortByNameTooltip);
        Assert.Contains(nameof(RepoGroupHeaderViewModel.SortByNameTooltip), changed);
        Assert.False(ungrouped.SortByNameCommand.CanExecute(null));
        Assert.False(string.IsNullOrWhiteSpace(ungrouped.SortByNameTooltip));
    }
}
