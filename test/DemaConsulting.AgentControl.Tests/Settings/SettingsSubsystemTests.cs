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
///     Subsystem tests for Settings: the user's theme choice, repo groups, card colors and collapsed
///     sections used the way the rest of the application uses them - saved by one session and loaded
///     by a later one through the subsystem's public surface (<see cref="SettingsStore"/> with
///     <see cref="AppSettings"/> and <see cref="RecentRepo"/>) against a real temporary configuration
///     folder, including that a damaged settings file never keeps the application from starting.
/// </summary>
public class SettingsSubsystemTests
{
    /// <summary>
    ///     Test that each theme the user can choose is still chosen in a later session, and that a
    ///     theme value the later session cannot read (missing, unknown or of the wrong type) reads
    ///     as the system default without failing the load or disturbing other settings.
    /// </summary>
    [Fact]
    public void Settings_Appearance_ChosenThemeSurvivesSessionsAndDamagedValueReadsAsSystemDefault()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        try
        {
            // Act / Assert: every choice is kept by name for the next session
            foreach (var preference in Enum.GetValues<ThemePreference>())
            {
                SettingsStore.Save(
                    new AppSettings { Theme = preference, GitExecutablePath = "git-kept" }, configDir);
                var nextSession = SettingsStore.Load(configDir);
                Assert.Equal(preference, nextSession.Theme);
                Assert.Equal("git-kept", nextSession.GitExecutablePath);
            }

            // Act / Assert: values no session may fail on read as the system default
            foreach (var themeJson in new[] { "\"Sepia\"", "99", "true", "null", "{ \"a\": 1 }", "[ \"Dark\" ]" })
            {
                File.WriteAllText(
                    Path.Combine(configDir, "settings.json"),
                    "{ \"Theme\": " + themeJson + ", \"GitExecutablePath\": \"git-after\" }");
                var damaged = SettingsStore.Load(configDir);
                Assert.Equal(ThemePreference.System, damaged.Theme);
                Assert.Equal("git-after", damaged.GitExecutablePath);
            }

            // Act / Assert: a file written before themes existed starts on the system default too
            File.WriteAllText(Path.Combine(configDir, "settings.json"), "{ \"GitExecutablePath\": \"git-old\" }");
            Assert.Equal(ThemePreference.System, SettingsStore.Load(configDir).Theme);
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a repo's group and card color and the collapsed sections are kept for the next
    ///     session in the user's own settings file only, change with later saves, and are not lost
    ///     when other settings are saved.
    /// </summary>
    [Fact]
    public void Settings_RepoOrganization_GroupColorAndCollapsedSectionsSurviveSessionsInSettingsFileOnly()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        try
        {
            var firstSession = new AppSettings
            {
                RecentRepos =
                [
                    new RecentRepo
                    {
                        Path = @"C:\repos\a", GroupName = "Client Work", CardColor = "Teal", IsFavorite = true
                    },
                    new RecentRepo { Path = @"C:\repos\b", GroupName = "Home" },
                    new RecentRepo { Path = @"C:\repos\c" }
                ],
                CollapsedGroups = ["Home", "Ungrouped"]
            };

            // Act: the first session saves; a later session loads, changes one repo and saves again
            SettingsStore.Save(firstSession, configDir);
            var secondSession = SettingsStore.Load(configDir);
            secondSession.RecentRepos[1].GroupName = null;
            secondSession.RecentRepos[2].CardColor = "Orange";
            secondSession.CollapsedGroups = ["Client Work"];
            secondSession.GitExecutablePath = "git-later";
            SettingsStore.Save(secondSession, configDir);
            var thirdSession = SettingsStore.Load(configDir);

            // Assert: the later session sees the changes, and the earlier values that were not changed
            var repos = thirdSession.RecentRepos;
            Assert.Multiple(
                () => Assert.Equal(["Client Work", null, null], repos.Select(repo => repo.GroupName)),
                () => Assert.Equal(["Teal", null, "Orange"], repos.Select(repo => repo.CardColor)),
                () => Assert.True(repos[0].IsFavorite),
                () => Assert.Equal(["Client Work"], thirdSession.CollapsedGroups),
                () => Assert.Equal("git-later", thirdSession.GitExecutablePath),
                () => Assert.Equal(
                    ["settings.json"], Directory.GetFileSystemEntries(configDir).Select(Path.GetFileName)));
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a settings file whose organizing values are missing, damaged or written by another
    ///     version loads as "no group", the automatic color and "nothing collapsed" (with group names
    ///     repaired and unified), keeps the values around them, and that saving what was loaded writes
    ///     a clean file that reads back the same.
    /// </summary>
    [Fact]
    public void Settings_RepoOrganization_DamagedValuesFallBackAndNextSaveWritesCleanFile()
    {
        // Arrange: damage of every kind in one file, with valid values between them
        var configDir = CreateTempDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(configDir, "settings.json"),
                "{ \"RecentRepos\": [" +
                " { \"Path\": \"a\", \"GroupName\": 42, \"CardColor\": { \"x\": 1 }, \"IsFavorite\": true }," +
                " { \"Path\": \"b\", \"GroupName\": \"  Client   Work \", \"CardColor\": \"Chartreuse\" }," +
                " { \"Path\": \"c\", \"GroupName\": \"client work\", \"CardColor\": \"  tEAL \" }," +
                " { \"Path\": \"d\", \"GroupName\": \"Favorites\", \"CardColor\": null }," +
                " { \"Path\": \"e\" }" +
                "], \"CollapsedGroups\": [ \"Client Work\", 3, null, \"client work\", \"\" ]," +
                " \"GitExecutablePath\": \"git-after\" }");

            // Act
            var loaded = SettingsStore.Load(configDir);
            SettingsStore.Save(loaded, configDir);
            var reloaded = SettingsStore.Load(configDir);
            var json = File.ReadAllText(Path.Combine(configDir, "settings.json"));

            // Assert: damaged values read as none, group names are repaired and unified, valid values stay
            var groups = loaded.RecentRepos.Select(repo => repo.GroupName).ToList();
            var colors = loaded.RecentRepos.Select(repo => repo.CardColor).ToList();
            Assert.Multiple(
                () => Assert.Equal([null, "Client Work", "Client Work", null, null], groups),
                () => Assert.Equal([null, null, "Teal", null, null], colors),
                () => Assert.True(loaded.RecentRepos[0].IsFavorite),
                () => Assert.Equal(["Client Work"], loaded.CollapsedGroups),
                () => Assert.Equal("git-after", loaded.GitExecutablePath),
                () => Assert.Equal(groups, reloaded.RecentRepos.Select(repo => repo.GroupName)),
                () => Assert.Equal(colors, reloaded.RecentRepos.Select(repo => repo.CardColor)),
                () => Assert.Equal(loaded.CollapsedGroups, reloaded.CollapsedGroups),
                () => Assert.DoesNotContain("Chartreuse", json, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Creates a unique, empty temporary configuration folder.
    /// </summary>
    /// <returns>The path of the new folder.</returns>
    private static string CreateTempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(), "AgentControlSettingsSubsystemTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
