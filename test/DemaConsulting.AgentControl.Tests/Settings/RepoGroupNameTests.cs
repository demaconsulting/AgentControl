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

using DemaConsulting.AgentControl.Settings;

namespace DemaConsulting.AgentControl.Tests.Settings;

/// <summary>
///     Unit tests for <see cref="RepoGroupName"/>: how typed or stored group names are cleaned,
///     validated, repaired and unified.
/// </summary>
public class RepoGroupNameTests
{
    /// <summary>
    ///     Test that normalizing trims the ends, folds runs of white space and control characters into
    ///     one space, and returns null for nothing.
    /// </summary>
    /// <param name="input">The raw text.</param>
    /// <param name="expected">The expected normalized name, or null.</param>
    [Theory]
    [InlineData("Work", "Work")]
    [InlineData("  Work  ", "Work")]
    [InlineData("Client \t  Alpha", "Client Alpha")]
    [InlineData("A\r\nB", "A B")]
    [InlineData("A\u0007B", "A B")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("\u0001\u0002", null)]
    [InlineData(null, null)]
    public void RepoGroupName_Normalize_CleansWhiteSpaceAndControlCharacters(string? input, string? expected)
    {
        // Act
        var result = RepoGroupName.Normalize(input);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    ///     Test that validation accepts a usable name and names the reason for each refusal.
    /// </summary>
    [Fact]
    public void RepoGroupName_Validate_ReportsEachProblem()
    {
        // Arrange
        var longest = new string('x', RepoGroupName.MaxLength);
        var tooLong = new string('x', RepoGroupName.MaxLength + 1);

        // Act / Assert
        Assert.Multiple(
            () => Assert.Equal(RepoGroupNameProblem.None, RepoGroupName.Validate("Work")),
            () => Assert.Equal(RepoGroupNameProblem.None, RepoGroupName.Validate(longest)),
            () => Assert.Equal(RepoGroupNameProblem.TooLong, RepoGroupName.Validate(tooLong)),
            () => Assert.Equal(RepoGroupNameProblem.Empty, RepoGroupName.Validate("   ")),
            () => Assert.Equal(RepoGroupNameProblem.Empty, RepoGroupName.Validate(null)),
            () => Assert.Equal(RepoGroupNameProblem.Reserved, RepoGroupName.Validate("Favorites")),
            () => Assert.Equal(RepoGroupNameProblem.Reserved, RepoGroupName.Validate("  ungrouped ")));
    }

    /// <summary>
    ///     Test that a stored name is repaired rather than refused: cut to the limit, reserved names
    ///     and blanks mean no group.
    /// </summary>
    [Fact]
    public void RepoGroupName_Sanitize_RepairsStoredNames()
    {
        // Arrange
        var tooLong = new string('x', RepoGroupName.MaxLength + 15);

        // Act / Assert
        Assert.Multiple(
            () => Assert.Equal("Work", RepoGroupName.Sanitize(" Work ")),
            () => Assert.Equal(new string('x', RepoGroupName.MaxLength), RepoGroupName.Sanitize(tooLong)),
            () => Assert.Null(RepoGroupName.Sanitize("FAVORITES")),
            () => Assert.Null(RepoGroupName.Sanitize("Ungrouped")),
            () => Assert.Null(RepoGroupName.Sanitize("")),
            () => Assert.Null(RepoGroupName.Sanitize(null)));
    }

    /// <summary>
    ///     Test that names differing only in letter case are one group, spelled like the first
    ///     occurrence, and the distinct names come back sorted.
    /// </summary>
    [Fact]
    public void RepoGroupName_DistinctNames_UnifiesCaseAndSorts()
    {
        // Act
        var names = RepoGroupName.DistinctNames(["work", null, "Alpha", "Work", " ", "beta", "ALPHA"]);

        // Assert: first spelling wins, ordinal ignore-case order
        Assert.Equal(["Alpha", "beta", "work"], names);
    }

    /// <summary>
    ///     Test that a name typed with other letter case adopts an existing group's spelling, and a new
    ///     name is left as typed.
    /// </summary>
    [Fact]
    public void RepoGroupName_Canonicalize_AdoptsExistingSpelling()
    {
        // Act / Assert
        Assert.Equal("Work", RepoGroupName.Canonicalize("wORK", ["Home", "Work", null]));
        Assert.Equal("Fresh", RepoGroupName.Canonicalize("Fresh", ["Home", "Work"]));
    }
}
