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
///     Tests of how <see cref="SettingsStore"/> persists, reads and repairs the hierarchy of the user's
///     groups (<see cref="AppSettings.GroupParents"/>): a file written before groups nested reads as a
///     flat list, a damaged or hand-edited hierarchy is repaired instead of failing the load, and a
///     group that takes part in the hierarchy survives with no repo in it.
/// </summary>
public class SettingsStoreGroupHierarchyTests
{
    /// <summary>
    ///     Test that a settings file written before groups nested loads with no hierarchy, so every group
    ///     is top level with its stored order, and that saving it writes an empty hierarchy.
    /// </summary>
    [Fact]
    public void SettingsStore_Load_FileWithoutGroupParents_ReadsEveryGroupAsTopLevel()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(configDir, "settings.json"),
                "{ \"RecentRepos\": [ { \"Path\": \"a\", \"GroupName\": \"Work\" }," +
                " { \"Path\": \"b\", \"GroupName\": \"Home\" } ], \"GroupOrder\": [ \"Home\", \"Work\" ] }");

            // Act
            var loaded = SettingsStore.Load(configDir);
            SettingsStore.Save(loaded, configDir);
            var json = File.ReadAllText(Path.Combine(configDir, "settings.json"));
            var hierarchy = new RepoGroupHierarchy(loaded.GetGroupNames(), loaded.GroupParents, loaded.GroupOrder);

            // Assert
            Assert.Multiple(
                () => Assert.Empty(loaded.GroupParents),
                () => Assert.Equal(["Home", "Work"], hierarchy.GetChildren(null)),
                () => Assert.All(hierarchy.GetTreeOrder(), node => Assert.Equal(0, node.Depth)),
                () => Assert.Contains("\"GroupParents\": {}", json, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that the hierarchy survives a save and a load, found by group name ignoring letter case.
    /// </summary>
    [Fact]
    public void SettingsStore_SaveThenLoad_GroupParents_RoundTrip()
    {
        // Arrange: Client > Project, with Project holding the repo
        var configDir = CreateTempDirectory();
        try
        {
            var settings = new AppSettings
            {
                RecentRepos = [new RecentRepo { Path = @"C:\repos\a", GroupName = "Project" }],
                GroupParents = new() { ["Client"] = string.Empty, ["Project"] = "Client" }
            };

            // Act
            SettingsStore.Save(settings, configDir);
            var loaded = SettingsStore.Load(configDir);

            // Assert
            Assert.Multiple(
                () => Assert.Equal("Client", loaded.GroupParents["project"]),
                () => Assert.Equal(string.Empty, loaded.GroupParents["CLIENT"]),
                () => Assert.Equal(2, loaded.GroupParents.Count),
                () => Assert.Equal(["Client", "Project"], loaded.GetGroupNames()));
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that an unknown parent, a group that is its own parent and a cycle are repaired on load,
    ///     that the repair is what a later save writes back, and that no group is lost.
    /// </summary>
    [Fact]
    public void SettingsStore_Load_UnknownSelfAndCyclicParents_AreRepairedOnLoad()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(configDir, "settings.json"),
                "{ \"RecentRepos\": [ { \"Path\": \"a\", \"GroupName\": \"Alpha\" }," +
                " { \"Path\": \"b\", \"GroupName\": \"Beta\" }, { \"Path\": \"c\", \"GroupName\": \"Gamma\" } ]," +
                " \"GroupParents\": { \"Alpha\": \"Beta\", \"Beta\": \"Gamma\", \"Gamma\": \"alpha\"," +
                " \"Orphan\": \"Missing\", \"Selfish\": \"selfish\" } }");

            // Act
            var loaded = SettingsStore.Load(configDir);
            SettingsStore.Save(loaded, configDir);
            var reloaded = SettingsStore.Load(configDir);

            // Assert: the cycle is cut at Alpha; Orphan and Selfish stay as empty registered groups
            Assert.Multiple(
                () => Assert.Equal(string.Empty, loaded.GroupParents["Alpha"]),
                () => Assert.Equal("Gamma", loaded.GroupParents["Beta"]),
                () => Assert.Equal("Alpha", loaded.GroupParents["Gamma"]),
                () => Assert.Equal(string.Empty, loaded.GroupParents["Orphan"]),
                () => Assert.Equal(string.Empty, loaded.GroupParents["Selfish"]),
                () => Assert.Equal(["Alpha", "Beta", "Gamma", "Orphan", "Selfish"], loaded.GetGroupNames()),
                () => Assert.Equal(
                    loaded.GroupParents.OrderBy(pair => pair.Key, StringComparer.Ordinal),
                    reloaded.GroupParents.OrderBy(pair => pair.Key, StringComparer.Ordinal)));
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that loading a damaged hierarchy does not rewrite the settings file (apart from the one-time
    ///     seeding of the repo order, which a file that already marks its order does not get): the repair
    ///     lives in the loaded settings and is written by the next save, not by the load itself.
    /// </summary>
    [Fact]
    public void SettingsStore_Load_DamagedHierarchy_DoesNotRewriteTheFile()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        try
        {
            var path = Path.Combine(configDir, "settings.json");
            File.WriteAllText(
                path,
                "{ \"HasUserRepoOrder\": true, \"RecentRepos\": [ { \"Path\": \"a\", \"GroupName\": \"Alpha\" }," +
                " { \"Path\": \"b\", \"GroupName\": \"Beta\" } ]," +
                " \"GroupOrder\": [ \"Alpha\", \"Beta\" ], \"GroupParents\": { \"Alpha\": \"Beta\", \"Beta\": \"Alpha\" } }");
            var before = File.ReadAllText(path);
            var writtenBefore = File.GetLastWriteTimeUtc(path);

            // Act
            var loaded = SettingsStore.Load(configDir);

            // Assert: the loaded settings are repaired, the file is as it was
            Assert.Multiple(
                () => Assert.Equal(string.Empty, loaded.GroupParents["Alpha"]),
                () => Assert.Equal(before, File.ReadAllText(path)),
                () => Assert.Equal(writtenBefore, File.GetLastWriteTimeUtc(path)));
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that every damaged shape of the stored hierarchy - a cycle of two, a cycle with a tail, a cycle
    ///     whose names differ in letter case, duplicate keys in different letter case with different parents, a
    ///     parent that names a group no repo is in any longer and one that a repo spells in another letter case
    ///     - is repaired on load and that a further save, load and save changes nothing (a fixed point).
    /// </summary>
    /// <param name="repoGroups">The groups of the repos, separated by commas.</param>
    /// <param name="parentsJson">The stored hierarchy as JSON.</param>
    [Theory]
    [InlineData("A,B", "{ \"A\": \"B\", \"B\": \"A\" }")]
    [InlineData("A,B,C,D", "{ \"A\": \"B\", \"B\": \"C\", \"C\": \"A\", \"D\": \"C\" }")]
    [InlineData("A,B", "{ \"a\": \"B\", \"b\": \"A\" }")]
    [InlineData("Work,Client,Other", "{ \"Work\": \"Client\", \"work\": \"Other\" }")]
    [InlineData("Work", "{ \"Work\": \"Gone\", \"Ghost\": \"Work\" }")]
    [InlineData("Work,Client", "{ \"Work\": \"client\", \"CLIENT\": \"work\" }")]
    public void SettingsStore_Load_DamagedHierarchyShapes_AreRepairedAndAFurtherLoadSaveIsAFixedPoint(
        string repoGroups, string parentsJson)
    {
        // Arrange
        var configDir = CreateTempDirectory();
        try
        {
            var path = Path.Combine(configDir, "settings.json");
            var repos = string.Join(
                ", ",
                repoGroups.Split(',').Select((name, index) => $"{{ \"Path\": \"repo{index}\", \"GroupName\": \"{name}\" }}"));
            File.WriteAllText(path, $"{{ \"RecentRepos\": [ {repos} ], \"GroupParents\": {parentsJson} }}");

            // Act
            var first = SettingsStore.Load(configDir);
            SettingsStore.Save(first, configDir);
            var firstText = File.ReadAllText(path);
            var second = SettingsStore.Load(configDir);
            SettingsStore.Save(second, configDir);
            var secondText = File.ReadAllText(path);

            // Assert: no cycle is left, every group of the repos is still there, and the save is stable
            var hierarchy = new RepoGroupHierarchy(first.GetGroupNames(), first.GroupParents, first.GroupOrder);
            Assert.Multiple(
                () => Assert.Equal(firstText, secondText),
                () => Assert.Equal(
                    first.GroupParents.OrderBy(pair => pair.Key, StringComparer.Ordinal),
                    second.GroupParents.OrderBy(pair => pair.Key, StringComparer.Ordinal)),
                () => Assert.Equal(first.GetGroupNames().Count, hierarchy.GetTreeOrder().Count),
                () => Assert.All(
                    repoGroups.Split(','),
                    name => Assert.Contains(name, first.GetGroupNames(), RepoGroupName.Comparer)),
                () => Assert.All(first.GroupParents.Keys, name => Assert.True(hierarchy.Contains(name))));
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that an entry whose group name is a built-in section's or is blank is dropped, and that a
    ///     built-in section's name as a parent means top level.
    /// </summary>
    [Fact]
    public void SettingsStore_Load_ReservedAndBlankNamesInGroupParents_AreDropped()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(configDir, "settings.json"),
                "{ \"RecentRepos\": [ { \"Path\": \"a\", \"GroupName\": \"Work\" } ]," +
                " \"GroupParents\": { \"Favorites\": \"Work\", \"  \": \"Work\", \"Ungrouped\": \"\"," +
                " \"Work\": \"Favorites\", \"Kid\": \"Ungrouped\" } }");

            // Act
            var loaded = SettingsStore.Load(configDir);

            // Assert
            Assert.Equal(
                new Dictionary<string, string> { ["Work"] = string.Empty, ["Kid"] = string.Empty },
                loaded.GroupParents);
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a hierarchy of the wrong JSON type, or with members of the wrong type, reads as
    ///     empty or loses only the offending entries, and does not disturb the settings after it.
    /// </summary>
    /// <param name="valueJson">The JSON text of the damaged value.</param>
    /// <param name="expectedCount">The number of entries expected to survive.</param>
    [Theory]
    [InlineData("null", 0)]
    [InlineData("42", 0)]
    [InlineData("\"text\"", 0)]
    [InlineData("[ \"a\", \"b\" ]", 0)]
    [InlineData("{ \"Work\": 3, \"Kid\": [ 1 ], \"Home\": { \"x\": 1 }, \"Child\": \"Work\" }", 1)]
    public void SettingsStore_Load_DamagedGroupParents_KeepsOnlyUsableEntries(string valueJson, int expectedCount)
    {
        // Arrange: a damaged value followed by a value that must still be read
        var configDir = CreateTempDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(configDir, "settings.json"),
                "{ \"RecentRepos\": [ { \"Path\": \"a\", \"GroupName\": \"Work\" } ]," +
                " \"GroupParents\": " + valueJson + ", \"GitExecutablePath\": \"git-after\" }");

            // Act
            var loaded = SettingsStore.Load(configDir);

            // Assert: the surviving entry (if any) is registered under Work, and the later setting was read
            Assert.Multiple(
                () => Assert.Equal(expectedCount, loaded.GroupParents.Count(pair => pair.Key == "Child")),
                () => Assert.Equal(expectedCount == 0 ? 0 : 2, loaded.GroupParents.Count),
                () => Assert.Equal("git-after", loaded.GitExecutablePath));
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a registered group with no repo in it still exists after a load (with its color and
    ///     place in the order), while an unregistered group with no repo is dropped with its color and
    ///     place as before.
    /// </summary>
    [Fact]
    public void SettingsStore_Load_RegisteredGroupWithoutRepos_SurvivesButUnregisteredIsDropped()
    {
        // Arrange: Empty is registered (inside Work); Gone is not registered and has no repo
        var configDir = CreateTempDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(configDir, "settings.json"),
                "{ \"RecentRepos\": [ { \"Path\": \"a\", \"GroupName\": \"Work\" } ]," +
                " \"GroupParents\": { \"Empty\": \"Work\" }," +
                " \"GroupColors\": { \"Empty\": \"Teal\", \"Gone\": \"Lime\" }," +
                " \"GroupOrder\": [ \"Gone\", \"Empty\", \"Work\" ] }");

            // Act
            var loaded = SettingsStore.Load(configDir);

            // Assert: Work was registered as Empty's parent
            Assert.Multiple(
                () => Assert.Equal(["Empty", "Work"], loaded.GetGroupNames()),
                () => Assert.Equal("Work", loaded.GroupParents["Empty"]),
                () => Assert.Equal(string.Empty, loaded.GroupParents["Work"]),
                () => Assert.Equal(["Empty"], loaded.GroupColors.Keys),
                () => Assert.Equal(["Empty", "Work"], loaded.GroupOrder));
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that the hierarchy is spelled the way the repos spell a group, whatever the letter case
    ///     the file used for its entries.
    /// </summary>
    [Fact]
    public void SettingsStore_Load_GroupParentsInOtherLetterCase_AreSpelledLikeTheGroups()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(configDir, "settings.json"),
                "{ \"RecentRepos\": [ { \"Path\": \"a\", \"GroupName\": \"Client Work\" }," +
                " { \"Path\": \"b\", \"GroupName\": \"Project X\" } ]," +
                " \"GroupParents\": { \"project x\": \"CLIENT WORK\" } }");

            // Act
            var loaded = SettingsStore.Load(configDir);

            // Assert
            Assert.Equal(
                new Dictionary<string, string> { ["Project X"] = "Client Work", ["Client Work"] = string.Empty },
                loaded.GroupParents);
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Creates a unique temporary configuration directory.
    /// </summary>
    /// <returns>The directory's path.</returns>
    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "AgentControlSettingsHierarchy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
