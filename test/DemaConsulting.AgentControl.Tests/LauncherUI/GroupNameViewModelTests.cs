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
using DemaConsulting.AgentControl.Settings;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Unit tests for <see cref="GroupNameViewModel"/>: live validation of the typed name and the
///     prompt's accept/cancel lifecycle, for creating and for renaming a group.
/// </summary>
public class GroupNameViewModelTests
{
    /// <summary>
    ///     Test that an empty prompt is neutral (no error, cannot accept yet) and a valid name can be
    ///     accepted, completing with the cleaned-up name.
    /// </summary>
    [Fact]
    public void GroupNameViewModel_NewGroup_ValidName_AcceptsCleanedName()
    {
        // Arrange
        var viewModel = CreateNew("Home");
        string? result = null;
        viewModel.Completed += (_, name) => result = name;

        // Assert: nothing typed yet
        Assert.False(viewModel.HasError);
        Assert.False(viewModel.CanAccept);
        Assert.False(viewModel.AcceptCommand.CanExecute(null));

        // Act
        viewModel.Text = "  Client   Work ";

        // Assert
        Assert.True(viewModel.CanAccept);
        Assert.False(viewModel.HasError);
        Assert.False(viewModel.HasHint);
        viewModel.AcceptCommand.Execute(null);
        Assert.Equal("Client Work", result);
    }

    /// <summary>
    ///     Test that each unusable name shows its message and cannot be accepted.
    /// </summary>
    /// <param name="text">The typed text.</param>
    /// <param name="expectedMessagePart">A part of the expected message.</param>
    [Theory]
    [InlineData("   ", "cannot be blank")]
    [InlineData("favorites", "built-in")]
    [InlineData("Ungrouped", "built-in")]
    public void GroupNameViewModel_InvalidName_ShowsMessageAndCannotAccept(string text, string expectedMessagePart)
    {
        // Arrange
        var viewModel = CreateNew();
        var completed = false;
        viewModel.Completed += (_, _) => completed = true;

        // Act
        viewModel.Text = text;
        viewModel.AcceptCommand.Execute(null);

        // Assert
        Assert.True(viewModel.HasError);
        Assert.Contains(expectedMessagePart, viewModel.ErrorText, StringComparison.Ordinal);
        Assert.False(viewModel.CanAccept);
        Assert.False(completed);
    }

    /// <summary>
    ///     Test that a blank name, a built-in section's name and a name over the limit are refused, with the
    ///     same message and without completing, whether a group is created, renamed or created inside
    ///     another group.
    /// </summary>
    /// <param name="mode">How the prompt is opened: <c>new</c>, <c>rename</c> or <c>subgroup</c>.</param>
    /// <param name="text">The typed text.</param>
    /// <param name="expectedMessagePart">A part of the expected message.</param>
    [Theory]
    [InlineData("new", "   ", "cannot be blank")]
    [InlineData("rename", "   ", "cannot be blank")]
    [InlineData("subgroup", "   ", "cannot be blank")]
    [InlineData("new", "FAVORITES", "built-in")]
    [InlineData("rename", "Ungrouped", "built-in")]
    [InlineData("subgroup", "favorites", "built-in")]
    [InlineData("new", "TooLong", "at most 40")]
    [InlineData("rename", "TooLong", "at most 40")]
    [InlineData("subgroup", "TooLong", "at most 40")]
    public void GroupNameViewModel_BlankBuiltInAndTooLongNames_AreRefusedInEveryMode(
        string mode, string text, string expectedMessagePart)
    {
        // Arrange
        var viewModel = mode switch
        {
            "rename" => new GroupNameViewModel("Rename group", "New name:", "Rename", ["Home", "Work"], renaming: "Work"),
            "subgroup" => new GroupNameViewModel("New subgroup", "Name:", "Create", ["Home", "Work"], requireNewName: true),
            _ => CreateNew("Home", "Work")
        };
        var completed = false;
        viewModel.Completed += (_, _) => completed = true;

        // Act
        viewModel.Text = text == "TooLong" ? new string('x', RepoGroupName.MaxLength + 1) : text;
        viewModel.AcceptCommand.Execute(null);

        // Assert
        Assert.True(viewModel.HasError);
        Assert.Contains(expectedMessagePart, viewModel.ErrorText, StringComparison.Ordinal);
        Assert.False(viewModel.CanAccept);
        Assert.False(viewModel.HasHint);
        Assert.False(completed);
    }

    /// <summary>
    ///     Test that changing the typed text announces a change of the message, the hint and whether the name
    ///     can be accepted - once per real change - so the window follows the current text.
    /// </summary>
    [Fact]
    public void GroupNameViewModel_Text_RaisesChangeNotificationsForTheDerivedState()
    {
        // Arrange
        var viewModel = CreateNew("Work");
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        // Act
        viewModel.Text = "work";
        var afterChange = changed.ToList();
        viewModel.Text = "work";

        // Assert: every derived property was announced, and a second identical text announced nothing more
        Assert.Contains(nameof(GroupNameViewModel.Text), afterChange);
        Assert.Contains(nameof(GroupNameViewModel.ErrorText), afterChange);
        Assert.Contains(nameof(GroupNameViewModel.HasError), afterChange);
        Assert.Contains(nameof(GroupNameViewModel.HintText), afterChange);
        Assert.Contains(nameof(GroupNameViewModel.HasHint), afterChange);
        Assert.Contains(nameof(GroupNameViewModel.CanAccept), afterChange);
        Assert.Equal(afterChange.Count, changed.Count);
        Assert.True(viewModel.AcceptCommand.CanExecute(null));
    }

    /// <summary>
    ///     Test that a name over the limit is refused with its length and that exactly the limit is fine.
    /// </summary>
    [Fact]
    public void GroupNameViewModel_TooLongName_ShowsLengthMessage()
    {
        // Arrange
        var viewModel = CreateNew();

        // Act / Assert: one over
        viewModel.Text = new string('x', RepoGroupName.MaxLength + 1);
        Assert.Contains($"at most {RepoGroupName.MaxLength}", viewModel.ErrorText, StringComparison.Ordinal);
        Assert.False(viewModel.CanAccept);

        // Act / Assert: exactly the limit
        viewModel.Text = new string('x', RepoGroupName.MaxLength);
        Assert.Null(viewModel.ErrorText);
        Assert.True(viewModel.CanAccept);
    }

    /// <summary>
    ///     Test that, when creating a group, a name matching an existing group (ignoring case) is not
    ///     an error: it gets a hint and completes with the existing group's own spelling.
    /// </summary>
    [Fact]
    public void GroupNameViewModel_NewGroup_ExistingName_HintsAndAdoptsSpelling()
    {
        // Arrange
        var viewModel = CreateNew("Home", "Work");
        string? result = null;
        viewModel.Completed += (_, name) => result = name;

        // Act
        viewModel.Text = "work";

        // Assert
        Assert.False(viewModel.HasError);
        Assert.True(viewModel.HasHint);
        Assert.Contains("\"Work\"", viewModel.HintText, StringComparison.Ordinal);
        viewModel.AcceptCommand.Execute(null);
        Assert.Equal("Work", result);
    }

    /// <summary>
    ///     Test that a prompt that requires a new name (a group created inside another group) refuses the
    ///     name of every existing group, in any letter case, with no hint of joining it, and accepts a
    ///     fresh name exactly as typed.
    /// </summary>
    [Fact]
    public void GroupNameViewModel_NewSubgroup_RefusesExistingNamesAcceptsFreshName()
    {
        // Arrange
        var viewModel = new GroupNameViewModel(
            "New subgroup", "Name of the new group:", "Create", ["Home", "Work"], requireNewName: true);
        string? result = null;
        viewModel.Completed += (_, name) => result = name;

        // Act / Assert: an existing group's name, in another letter case
        viewModel.Text = "HOME";
        Assert.Contains("already exists", viewModel.ErrorText, StringComparison.Ordinal);
        Assert.False(viewModel.HasHint);
        Assert.False(viewModel.CanAccept);

        // Act / Assert: a fresh name is accepted as typed (cleaned up)
        viewModel.Text = "  new   Name ";
        Assert.False(viewModel.HasError);
        Assert.True(viewModel.CanAccept);
        viewModel.AcceptCommand.Execute(null);
        Assert.Equal("new Name", result);
    }

    /// <summary>
    ///     Test that renaming starts with the group's own name, refuses another existing group's name,
    ///     accepts a change of letter case only (keeping what was typed) and a fresh name.
    /// </summary>
    [Fact]
    public void GroupNameViewModel_Rename_RefusesOtherGroupsAcceptsCaseChange()
    {
        // Arrange
        var viewModel = new GroupNameViewModel("Rename group", "New name:", "Rename", ["Home", "Work"], renaming: "Work");
        string? result = null;
        viewModel.Completed += (_, name) => result = name;

        // Assert: starts with the current name
        Assert.Equal("Work", viewModel.Text);

        // Act / Assert: another group's name
        viewModel.Text = "HOME";
        Assert.Contains("already exists", viewModel.ErrorText, StringComparison.Ordinal);
        Assert.False(viewModel.CanAccept);

        // Act / Assert: only the case changes
        viewModel.Text = "work";
        Assert.False(viewModel.HasError);
        Assert.False(viewModel.HasHint);
        Assert.True(viewModel.CanAccept);
        viewModel.AcceptCommand.Execute(null);
        Assert.Equal("work", result);
    }

    /// <summary>
    ///     Test that canceling completes with no name, and that the prompt completes only once however
    ///     many times it is canceled or accepted afterwards.
    /// </summary>
    [Fact]
    public void GroupNameViewModel_Cancel_CompletesOnceWithNull()
    {
        // Arrange
        var viewModel = CreateNew();
        var results = new List<string?>();
        viewModel.Completed += (_, name) => results.Add(name);
        viewModel.Text = "Work";

        // Act
        viewModel.CancelCommand.Execute(null);
        viewModel.Cancel();
        viewModel.AcceptCommand.Execute(null);

        // Assert
        Assert.Equal([null], results);
    }

    /// <summary>
    ///     Test that accepting completes with the name exactly once, however many times the prompt is
    ///     accepted or canceled afterwards (the one-shot guarantee holds for the accepted path too).
    /// </summary>
    [Fact]
    public void GroupNameViewModel_Accept_CompletesOnceWithTheName()
    {
        // Arrange
        var viewModel = CreateNew();
        var results = new List<string?>();
        viewModel.Completed += (_, name) => results.Add(name);
        viewModel.Text = "Work";

        // Act: accept, then try to accept, cancel and change the text again
        viewModel.AcceptCommand.Execute(null);
        viewModel.AcceptCommand.Execute(null);
        viewModel.CancelCommand.Execute(null);
        viewModel.Cancel();
        viewModel.Text = "Home";
        viewModel.AcceptCommand.Execute(null);

        // Assert: one completion, with the name that was accepted
        Assert.Equal(["Work"], results);
    }

    /// <summary>
    ///     Test that the constructor rejects missing arguments.
    /// </summary>
    [Fact]
    public void GroupNameViewModel_Constructor_NullArguments_Throw()
    {
        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => new GroupNameViewModel(null!, "p", "a", []));
        Assert.Throws<ArgumentNullException>(() => new GroupNameViewModel("t", null!, "a", []));
        Assert.Throws<ArgumentNullException>(() => new GroupNameViewModel("t", "p", null!, []));
        Assert.Throws<ArgumentNullException>(() => new GroupNameViewModel("t", "p", "a", null!));
    }

    /// <summary>
    ///     Creates a prompt for a new group.
    /// </summary>
    /// <param name="existing">The names of the groups that exist.</param>
    /// <returns>The view model.</returns>
    private static GroupNameViewModel CreateNew(params string[] existing) =>
        new("New group", "Name:", "Create", existing);
}
