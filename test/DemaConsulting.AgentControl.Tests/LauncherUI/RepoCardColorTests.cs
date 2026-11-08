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
using DemaConsulting.AgentControl.Settings;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Unit tests of the user-chosen card color: the palette, how a stored name is understood, and
///     how <see cref="RepoCardViewModel"/> exposes and persists the choice (the status accent is not
///     affected by it).
/// </summary>
/// <remarks>
///     Cards are created without calling <c>RefreshCheap</c>, so no git subprocess is started.
/// </remarks>
public class RepoCardColorTests
{
    /// <summary>
    ///     Test that the palette is the avatar palette the style resources define: one entry per
    ///     index, with distinct names.
    /// </summary>
    [Fact]
    public void CardColorPalette_All_MatchesTheAvatarPalette()
    {
        // Act
        var all = CardColorPalette.All;

        // Assert
        Assert.Multiple(
            () => Assert.Equal(RepoCardViewModel.AvatarPaletteSize, CardColorPalette.Count),
            () => Assert.Equal(Enumerable.Range(0, CardColorPalette.Count), all.Select(color => color.Index)),
            () => Assert.Equal(all.Count, all.Select(color => color.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()));
    }

    /// <summary>
    ///     Test that a stored name is found ignoring case and surrounding white space, and that a
    ///     missing, blank or unknown name means the automatic color (null) rather than an error.
    /// </summary>
    /// <param name="stored">The stored name.</param>
    /// <param name="expected">The expected color name, or null for Automatic.</param>
    [Theory]
    [InlineData("Teal", "Teal")]
    [InlineData("  tEAL ", "Teal")]
    [InlineData("LIME", "Lime")]
    [InlineData("Automatic", null)]
    [InlineData("chartreuse", null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void CardColorPalette_Find_ToleratesAnyStoredValue(string? stored, string? expected)
    {
        // Act
        var color = CardColorPalette.Find(stored);

        // Assert
        Assert.Equal(expected, color?.Name);
    }

    /// <summary>
    ///     Test that a card with no chosen color uses the automatic (path hash) color.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_CardColor_Default_IsAutomatic()
    {
        // Arrange
        var card = CreateCard(new RecentRepo { Path = @"C:\repos\sample" });

        // Assert
        Assert.Multiple(
            () => Assert.Null(card.CardColorName),
            () => Assert.False(card.HasCustomCardColor),
            () => Assert.True(card.IsCardColorAutomatic),
            () => Assert.Equal(RepoCardViewModel.GetAvatarColorIndex(card.RepoPath), card.AvatarColorIndex));
    }

    /// <summary>
    ///     Test that choosing a color changes the tile's palette index and style class, stores the
    ///     name on the settings entry, raises the change event once, and that choosing Automatic goes
    ///     back to the path color and clears the stored value.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_SetCardColor_ChoosesAndClearsColor()
    {
        // Arrange
        var recent = new RecentRepo { Path = @"C:\repos\sample" };
        var card = CreateCard(recent);
        var automaticIndex = card.AvatarColorIndex;
        var raised = 0;
        var changed = new List<string?>();
        card.CardColorChanged += (_, _) => raised++;
        card.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        // Act: a color (letter case is forgiven)
        var chosen = card.SetCardColor("tEaL");

        // Assert
        Assert.True(chosen);
        Assert.Multiple(
            () => Assert.Equal("Teal", card.CardColorName),
            () => Assert.Equal("Teal", recent.CardColor),
            () => Assert.True(card.HasCustomCardColor),
            () => Assert.False(card.IsCardColorAutomatic),
            () => Assert.Equal(5, card.AvatarColorIndex),
            () => Assert.Equal("avatar5", card.AvatarStyleClass),
            () => Assert.Equal(1, raised),
            () => Assert.Contains(nameof(RepoCardViewModel.AvatarStyleClass), changed));

        // Act: the same color again changes nothing
        Assert.True(card.SetCardColor("Teal"));
        Assert.Equal(1, raised);

        // Act: Automatic
        Assert.True(card.SetCardColor(null));

        // Assert
        Assert.Multiple(
            () => Assert.Null(card.CardColorName),
            () => Assert.Null(recent.CardColor),
            () => Assert.Equal(automaticIndex, card.AvatarColorIndex),
            () => Assert.Equal(2, raised));
    }

    /// <summary>
    ///     Test that a name that is not a palette color is refused and changes nothing.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_SetCardColor_UnknownName_IsRefused()
    {
        // Arrange
        var recent = new RecentRepo { Path = @"C:\repos\sample", CardColor = "Red" };
        var card = CreateCard(recent);

        // Act
        var accepted = card.SetCardColor("chartreuse");

        // Assert
        Assert.False(accepted);
        Assert.Equal("Red", card.CardColorName);
        Assert.Equal("Red", recent.CardColor);
    }

    /// <summary>
    ///     Test that a stored color this version does not know falls back to the automatic color
    ///     without failing.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_CardColor_UnknownStoredValue_FallsBackToAutomatic()
    {
        // Arrange
        var card = CreateCard(new RecentRepo { Path = @"C:\repos\sample", CardColor = "#123456" });

        // Assert
        Assert.Null(card.CardColorName);
        Assert.Equal(RepoCardViewModel.GetAvatarColorIndex(card.RepoPath), card.AvatarColorIndex);
    }

    /// <summary>
    ///     Test that the chosen color never touches the status accent: the card's status, status text
    ///     and tooltip are the same before and after.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_SetCardColor_DoesNotChangeStatus()
    {
        // Arrange
        var card = CreateCard(new RecentRepo { Path = @"C:\repos\sample" });
        var status = card.Status;
        var text = card.StatusText;
        var tooltip = card.StatusTooltip;

        // Act
        card.SetCardColor("Red");

        // Assert
        Assert.Equal(status, card.Status);
        Assert.Equal(text, card.StatusText);
        Assert.Equal(tooltip, card.StatusTooltip);
    }

    /// <summary>
    ///     Test that a card's group is read tolerantly, listed with the other groups of the settings,
    ///     and changed through <see cref="RepoCardViewModel.MoveToGroup"/> with the change event.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_MoveToGroup_UpdatesGroupAndRaisesChange()
    {
        // Arrange
        var other = new RecentRepo { Path = @"C:\repos\other", GroupName = "Home" };
        var recent = new RecentRepo { Path = @"C:\repos\sample", GroupName = "  " };
        var settings = new AppSettings { RecentRepos = [other, recent] };
        var card = new RepoCardViewModel(recent, () => settings, new PackageVersionCache());
        var raised = 0;
        card.GroupChanged += (_, _) => raised++;

        // Assert: a blank stored name reads as no group, and the existing groups are listed
        Assert.False(card.HasGroup);
        Assert.Equal(["Home"], card.GetExistingGroupNames());
        Assert.False(card.RemoveFromGroupCommand.CanExecute(null));

        // Act: join the other repo's group with different case
        Assert.True(card.MoveToGroup("home"));

        // Assert
        Assert.Equal("Home", card.GroupName);
        Assert.Equal("Home", recent.GroupName);
        Assert.True(card.RemoveFromGroupCommand.CanExecute(null));
        Assert.Equal(1, raised);

        // Act: the remove command takes it out again
        card.RemoveFromGroupCommand.Execute(null);

        // Assert
        Assert.Null(card.GroupName);
        Assert.Equal(2, raised);
    }

    /// <summary>
    ///     Test that the new-group command only asks the view for the name prompt, and the group chip
    ///     shows for a favorite that has a group only.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_NewGroupCommand_RaisesRequest_AndChipShowsForGroupedFavorites()
    {
        // Arrange
        var card = CreateCard(new RecentRepo { Path = @"C:\repos\sample", GroupName = "Work" });
        var requests = 0;
        card.NewGroupRequested += (_, _) => requests++;

        // Act
        card.NewGroupCommand.Execute(null);

        // Assert: request only, and no chip until the repo is a favorite
        Assert.Equal(1, requests);
        Assert.Equal("Work", card.GroupName);
        Assert.False(card.ShowGroupChip);

        // Act
        card.IsFavorite = true;

        // Assert
        Assert.True(card.ShowGroupChip);
    }

    /// <summary>
    ///     Creates a card over a settings entry, with settings that contain just that entry.
    /// </summary>
    /// <param name="recent">The settings entry.</param>
    /// <returns>The card, not refreshed.</returns>
    private static RepoCardViewModel CreateCard(RecentRepo recent)
    {
        var settings = new AppSettings { RecentRepos = [recent] };
        return new RepoCardViewModel(recent, () => settings, new PackageVersionCache());
    }
}
