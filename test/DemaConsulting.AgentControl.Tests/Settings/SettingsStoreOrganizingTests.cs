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
///     Tests of how <see cref="SettingsStore"/> persists and reads the user-specific organizing
///     choices - a repo's group, a repo's card color and the collapsed sections - including that
///     damaged or unexpected values never fail the load.
/// </summary>
public class SettingsStoreOrganizingTests
{
    /// <summary>
    ///     Test that the group, the card color and the collapsed sections survive a save and a load,
    ///     and that a repo without them stores nothing for them.
    /// </summary>
    [Fact]
    public void SettingsStore_SaveThenLoad_GroupColorAndCollapsedGroups_RoundTrip()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        try
        {
            var settings = new AppSettings
            {
                RecentRepos =
                [
                    new RecentRepo { Path = @"C:\repos\a", GroupName = "Work", CardColor = "Teal" },
                    new RecentRepo { Path = @"C:\repos\b" }
                ],
                CollapsedGroups = ["Work", "Ungrouped"]
            };

            // Act
            SettingsStore.Save(settings, configDir);
            var loaded = SettingsStore.Load(configDir);
            var json = File.ReadAllText(Path.Combine(configDir, "settings.json"));

            // Assert
            Assert.Multiple(
                () => Assert.Equal("Work", loaded.RecentRepos[0].GroupName),
                () => Assert.Equal("Teal", loaded.RecentRepos[0].CardColor),
                () => Assert.Null(loaded.RecentRepos[1].GroupName),
                () => Assert.Null(loaded.RecentRepos[1].CardColor),
                () => Assert.Equal(["Work", "Ungrouped"], loaded.CollapsedGroups),
                () => Assert.Equal(1, CountOccurrences(json, "\"GroupName\"")),
                () => Assert.Equal(1, CountOccurrences(json, "\"CardColor\"")));
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a settings file written before groups and colors existed loads with no groups,
    ///     the automatic color and nothing collapsed.
    /// </summary>
    [Fact]
    public void SettingsStore_Load_FileWithoutOrganizingValues_UsesDefaults()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(configDir, "settings.json"),
                "{ \"RecentRepos\": [ { \"Path\": \"C:\\\\repos\\\\a\", \"IsFavorite\": true } ] }");

            // Act
            var loaded = SettingsStore.Load(configDir);

            // Assert
            var repo = Assert.Single(loaded.RecentRepos);
            Assert.Multiple(
                () => Assert.True(repo.IsFavorite),
                () => Assert.Null(repo.GroupName),
                () => Assert.Null(repo.CardColor),
                () => Assert.Empty(loaded.CollapsedGroups));
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a group name or a card color of the wrong JSON type (a number, a boolean, an
    ///     object, an array, null) reads as "none" and does not disturb the values after it.
    /// </summary>
    /// <param name="valueJson">The JSON text of the damaged value.</param>
    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("{ \"nested\": [1, 2] }")]
    [InlineData("[ \"a\", \"b\" ]")]
    public void SettingsStore_Load_InvalidGroupOrColorType_ReadsAsNone(string valueJson)
    {
        // Arrange: damaged values followed by a value that must still be read
        var configDir = CreateTempDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(configDir, "settings.json"),
                "{ \"RecentRepos\": [ { \"Path\": \"C:\\\\repos\\\\a\", \"GroupName\": " + valueJson +
                ", \"CardColor\": " + valueJson + ", \"IsFavorite\": true } ] }");

            // Act
            var loaded = SettingsStore.Load(configDir);

            // Assert
            var repo = Assert.Single(loaded.RecentRepos);
            Assert.Multiple(
                () => Assert.Null(repo.GroupName),
                () => Assert.Null(repo.CardColor),
                () => Assert.True(repo.IsFavorite));
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a collapsed-sections value that is not a list of strings reads as an empty list,
    ///     or keeps just its string entries, and does not disturb the values after it.
    /// </summary>
    /// <param name="valueJson">The JSON text of the damaged value.</param>
    /// <param name="expected">The entries expected to survive, separated by <c>|</c>.</param>
    [Theory]
    [InlineData("null", "")]
    [InlineData("7", "")]
    [InlineData("\"Work\"", "")]
    [InlineData("{ \"a\": 1 }", "")]
    [InlineData("[]", "")]
    [InlineData("[ \"Work\", 3, null, { \"x\": 1 }, [ \"inner\" ], \"  Home  \", \"work\", \"\" ]", "Work|Home")]
    public void SettingsStore_Load_InvalidCollapsedGroups_KeepsOnlyCleanStrings(string valueJson, string expected)
    {
        // Arrange
        var configDir = CreateTempDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(configDir, "settings.json"),
                "{ \"CollapsedGroups\": " + valueJson + ", \"GitExecutablePath\": \"git-after\" }");

            // Act
            var loaded = SettingsStore.Load(configDir);

            // Assert
            Assert.Equal(expected.Length == 0 ? [] : expected.Split('|'), loaded.CollapsedGroups);
            Assert.Equal("git-after", loaded.GitExecutablePath);
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that stored group names are repaired on load: blanks and the built-in section names
    ///     mean no group, an overlong name is cut to the limit, and repos whose names differ only in
    ///     letter case share the first one's spelling.
    /// </summary>
    [Fact]
    public void SettingsStore_Load_GroupNames_AreRepairedAndUnified()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        try
        {
            var tooLong = new string('x', RepoGroupName.MaxLength + 10);
            File.WriteAllText(
                Path.Combine(configDir, "settings.json"),
                "{ \"RecentRepos\": [" +
                " { \"Path\": \"a\", \"GroupName\": \"  Client   Work \" }," +
                " { \"Path\": \"b\", \"GroupName\": \"client work\" }," +
                " { \"Path\": \"c\", \"GroupName\": \"   \" }," +
                " { \"Path\": \"d\", \"GroupName\": \"favorites\" }," +
                " { \"Path\": \"e\", \"GroupName\": \"UNGROUPED\" }," +
                " { \"Path\": \"f\", \"GroupName\": \"" + tooLong + "\" }" +
                "] }");

            // Act
            var loaded = SettingsStore.Load(configDir);

            // Assert
            var names = loaded.RecentRepos.Select(repo => repo.GroupName).ToList();
            Assert.Equal(
                ["Client Work", "Client Work", null, null, null, new string('x', RepoGroupName.MaxLength)], names);
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a card color string this version does not know (a typo, or a color from a newer
    ///     version) reads as the automatic color on load and is not written back, while a known
    ///     name in any letter case is kept under its canonical spelling.
    /// </summary>
    [Fact]
    public void SettingsStore_Load_UnknownCardColorName_ReadsAsAutomaticAndIsNotSavedBack()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(configDir, "settings.json"),
                "{ \"RecentRepos\": [" +
                " { \"Path\": \"a\", \"CardColor\": \"Chartreuse\" }," +
                " { \"Path\": \"b\", \"CardColor\": \"  tEAL \" }," +
                " { \"Path\": \"c\", \"CardColor\": \"   \" }" +
                "] }");

            // Act
            var loaded = SettingsStore.Load(configDir);
            SettingsStore.Save(loaded, configDir);
            var json = File.ReadAllText(Path.Combine(configDir, "settings.json"));

            // Assert: unknown and blank names are gone, the known one is canonical
            Assert.Equal([null, "Teal", null], loaded.RecentRepos.Select(repo => repo.CardColor));
            Assert.DoesNotContain("Chartreuse", json, StringComparison.Ordinal);
            Assert.Equal(1, CountOccurrences(json, "\"CardColor\""));
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Counts how often a text occurs in another.
    /// </summary>
    /// <param name="text">The text to search.</param>
    /// <param name="value">The text to count.</param>
    /// <returns>The number of non-overlapping occurrences.</returns>
    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    /// <summary>
    ///     Creates a unique temporary configuration directory.
    /// </summary>
    /// <returns>The directory's path.</returns>
    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "AgentControlSettingsOrg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
