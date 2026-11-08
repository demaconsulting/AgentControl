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

using DemaConsulting.AgentControl.Puppet.Automation;

namespace DemaConsulting.AgentControl.Puppet.Tests;

/// <summary>
///     Tests for resolving an item of a list by its text or its index (no UI is touched).
/// </summary>
public class ItemMatcherTests
{
    /// <summary>
    ///     The names of three repo cards.
    /// </summary>
    private static readonly IReadOnlyList<IReadOnlyList<string>> Cards =
    [
        ["alpha-repo"],
        ["beta-repo"],
        ["alpha-tools"],
    ];

    /// <summary>
    ///     Validates that an exact name wins, ignoring case.
    /// </summary>
    [Fact]
    public void ItemMatcher_TryResolve_ExactName_IgnoresCase()
    {
        // Arrange / Act
        var ok = ItemMatcher.TryResolve(Cards, "BETA-REPO", out var index, out var error);

        // Assert
        Assert.Multiple(
            () => Assert.True(ok, error),
            () => Assert.Equal(1, index));
    }

    /// <summary>
    ///     Validates that a number that is not a name is a zero-based index.
    /// </summary>
    [Fact]
    public void ItemMatcher_TryResolve_Number_IsZeroBasedIndex()
    {
        // Arrange / Act
        var ok = ItemMatcher.TryResolve(Cards, "2", out var index, out var error);

        // Assert
        Assert.Multiple(
            () => Assert.True(ok, error),
            () => Assert.Equal(2, index));
    }

    /// <summary>
    ///     Validates that a number that is the exact name of an item is that item, not an index.
    /// </summary>
    [Fact]
    public void ItemMatcher_TryResolve_NumberThatIsAName_PrefersTheName()
    {
        // Arrange
        IReadOnlyList<IReadOnlyList<string>> cards = [["first"], ["0"]];

        // Act
        var ok = ItemMatcher.TryResolve(cards, "0", out var index, out var error);

        // Assert
        Assert.Multiple(
            () => Assert.True(ok, error),
            () => Assert.Equal(1, index));
    }

    /// <summary>
    ///     Validates that an index past the end is an error that lists the items.
    /// </summary>
    [Fact]
    public void ItemMatcher_TryResolve_IndexOutOfRange_ListsItems()
    {
        // Arrange / Act
        var ok = ItemMatcher.TryResolve(Cards, "3", out _, out var error);

        // Assert
        Assert.Multiple(
            () => Assert.False(ok),
            () => Assert.Contains("out of range", error, StringComparison.Ordinal),
            () => Assert.Contains("beta-repo", error, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Validates that a part of a name that fits exactly one item selects it.
    /// </summary>
    [Fact]
    public void ItemMatcher_TryResolve_UniquePartOfName_Matches()
    {
        // Arrange / Act
        var ok = ItemMatcher.TryResolve(Cards, "beta", out var index, out var error);

        // Assert
        Assert.Multiple(
            () => Assert.True(ok, error),
            () => Assert.Equal(1, index));
    }

    /// <summary>
    ///     Validates that a part of a name that fits several items is an error that names their indexes.
    /// </summary>
    [Fact]
    public void ItemMatcher_TryResolve_AmbiguousPartOfName_IsErrorWithIndexes()
    {
        // Arrange / Act
        var ok = ItemMatcher.TryResolve(Cards, "alpha", out _, out var error);

        // Assert
        Assert.Multiple(
            () => Assert.False(ok),
            () => Assert.Contains("fits 2 items", error, StringComparison.Ordinal),
            () => Assert.Contains("0, 2", error, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Validates that a duplicate exact name is ambiguous rather than the first one.
    /// </summary>
    [Fact]
    public void ItemMatcher_TryResolve_DuplicateExactName_IsAmbiguous()
    {
        // Arrange
        IReadOnlyList<IReadOnlyList<string>> cards = [["same"], ["same"]];

        // Act
        var ok = ItemMatcher.TryResolve(cards, "same", out _, out var error);

        // Assert
        Assert.Multiple(
            () => Assert.False(ok),
            () => Assert.Contains("fits 2 items", error, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Validates that an item that shows several texts is matched by any of them.
    /// </summary>
    [Fact]
    public void ItemMatcher_TryResolve_AnyTextOfAnItem_Matches()
    {
        // Arrange
        IReadOnlyList<IReadOnlyList<string>> versions = [["Item 1.0.0", "1.0.0"], ["Item 1.1.0", "1.1.0"]];

        // Act
        var ok = ItemMatcher.TryResolve(versions, "1.1.0", out var index, out var error);

        // Assert
        Assert.Multiple(
            () => Assert.True(ok, error),
            () => Assert.Equal(1, index));
    }

    /// <summary>
    ///     Validates that no match, and an empty list, are errors.
    /// </summary>
    [Fact]
    public void ItemMatcher_TryResolve_NoMatch_IsError()
    {
        // Arrange / Act
        var noMatch = ItemMatcher.TryResolve(Cards, "gamma", out _, out var error);
        var empty = ItemMatcher.TryResolve([], "x", out _, out var emptyError);

        // Assert
        Assert.Multiple(
            () => Assert.False(noMatch),
            () => Assert.Contains("no item matches", error, StringComparison.Ordinal),
            () => Assert.False(empty),
            () => Assert.Contains("no items", emptyError, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Validates that the scope options are read from the command line and an empty value is a usage error.
    /// </summary>
    [Fact]
    public void ElementScope_FromCommand_ReadsOptionsAndRejectsEmptyValue()
    {
        // Arrange
        var parsed = CommandLineParser.Parse(["click", "LaunchButton", "--item", "my-repo", "--window", "Settings"]);
        var missing = CommandLineParser.Parse(["click", "LaunchButton", "--item"]);

        // Act
        var scope = ElementScope.FromCommand(parsed);
        var exception = Assert.Throws<PuppetException>(() => ElementScope.FromCommand(missing));

        // Assert
        Assert.Multiple(
            () => Assert.Equal("my-repo", scope.Item),
            () => Assert.Equal("Settings", scope.Window),
            () => Assert.Equal("RepoListBox", scope.ItemList),
            () => Assert.Equal(ExitCodes.UsageError, exception.ExitCode));
    }

    /// <summary>
    ///     Validates that the closest ids are suggested for a misspelled AutomationId.
    /// </summary>
    [Fact]
    public void ElementFinder_Suggest_MisspelledId_ReturnsClosestFirst()
    {
        // Arrange
        string[] ids = ["LaunchButton", "PullButton", "FilterTextBox", "AddRepoButton"];

        // Act
        var suggestions = ElementFinder.Suggest("LunchButton", ids);

        // Assert
        Assert.Equal("LaunchButton", suggestions[0]);
    }

    /// <summary>
    ///     Validates that menu item names match ignoring access keys, white space, case and a trailing ellipsis.
    /// </summary>
    /// <param name="name">The item's name.</param>
    /// <param name="segment">The path segment.</param>
    [Theory]
    [InlineData("_Refresh", "refresh")]
    [InlineData("Select Package...", "SelectPackage")]
    [InlineData("&Remove", "Remove")]
    public void MenuNavigator_NameMatches_IgnoresDecoration(string name, string segment)
    {
        // Arrange / Act
        var matches = MenuNavigator.NameMatches(name, segment);

        // Assert
        Assert.True(matches);
    }
}
