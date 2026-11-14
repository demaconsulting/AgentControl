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
///     Tests of how <see cref="SettingsStore"/> persists and reads the user's own repo order: the
///     stored sequence and its marker survive a save and a load, a file without the marker is
///     arranged once the way older versions displayed the list, and a marker of the wrong type reads
///     as "not arranged yet" without failing the load.
/// </summary>
public class SettingsStoreRepoOrderTests
{
    /// <summary>
    ///     Test that the repos come back in the stored sequence (not re-arranged by favorite or launch
    ///     time) and that the marker is written and read.
    /// </summary>
    [Fact]
    public void SettingsStore_SaveThenLoad_RepoOrder_RoundTripsInTheStoredSequence()
    {
        // Arrange: a user's order that differs from favorites-first and from the launch order
        var configDir = CreateTempDirectory();
        try
        {
            var settings = new AppSettings
            {
                HasUserRepoOrder = true,
                RecentRepos =
                [
                    new RecentRepo { Path = @"C:\repos\first", LastLaunchedUtc = DateTimeOffset.UtcNow.AddDays(-9) },
                    new RecentRepo { Path = @"C:\repos\second", IsFavorite = true },
                    new RecentRepo { Path = @"C:\repos\third", LastLaunchedUtc = DateTimeOffset.UtcNow }
                ]
            };

            // Act
            SettingsStore.Save(settings, configDir);
            var loaded = SettingsStore.Load(configDir);
            var json = File.ReadAllText(Path.Combine(configDir, "settings.json"));

            // Assert
            Assert.Multiple(
                () => Assert.True(loaded.HasUserRepoOrder),
                () => Assert.Equal(
                    [@"C:\repos\first", @"C:\repos\second", @"C:\repos\third"],
                    loaded.RecentRepos.Select(repo => repo.Path)),
                () => Assert.Contains("\"HasUserRepoOrder\": true", json, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a file without the marker (written by an older version) is arranged once as
    ///     favorites first, then the most recently launched with never-launched repos last (equal
    ///     keys keeping their order), and is then marked.
    /// </summary>
    [Fact]
    public void SettingsStore_Load_FileWithoutOrderMarker_SeedsFavoritesFirstThenMostRecentlyLaunched()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(configDir, "settings.json"),
                "{ \"RecentRepos\": [" +
                " { \"Path\": \"never1\" }," +
                " { \"Path\": \"old\", \"LastLaunchedUtc\": \"2026-01-01T00:00:00+00:00\" }," +
                " { \"Path\": \"never2\" }," +
                " { \"Path\": \"fav\", \"IsFavorite\": true, \"LastLaunchedUtc\": \"2025-01-01T00:00:00+00:00\" }," +
                " { \"Path\": \"new\", \"LastLaunchedUtc\": \"2026-06-01T00:00:00+00:00\" }" +
                "] }");

            // Act
            var loaded = SettingsStore.Load(configDir);

            // Assert
            Assert.Multiple(
                () => Assert.True(loaded.HasUserRepoOrder),
                () => Assert.Equal(
                    ["fav", "new", "old", "never1", "never2"], loaded.RecentRepos.Select(repo => repo.Path)));
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a file with the marker keeps the stored sequence exactly, whatever the favorites
    ///     and launch times say.
    /// </summary>
    [Fact]
    public void SettingsStore_Load_FileWithOrderMarker_KeepsTheStoredSequence()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(configDir, "settings.json"),
                "{ \"HasUserRepoOrder\": true, \"RecentRepos\": [" +
                " { \"Path\": \"never\" }," +
                " { \"Path\": \"new\", \"LastLaunchedUtc\": \"2026-06-01T00:00:00+00:00\" }," +
                " { \"Path\": \"fav\", \"IsFavorite\": true }" +
                "] }");

            // Act
            var loaded = SettingsStore.Load(configDir);

            // Assert
            Assert.Equal(["never", "new", "fav"], loaded.RecentRepos.Select(repo => repo.Path));
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a marker that is not a boolean (null, a string, a number, an object, an array, or
    ///     <c>false</c>) reads as "not arranged yet": the load does not fail, the file is arranged once,
    ///     and the values around the marker are still read.
    /// </summary>
    /// <param name="markerJson">The JSON text of the damaged marker.</param>
    [Theory]
    [InlineData("null")]
    [InlineData("\"yes\"")]
    [InlineData("1")]
    [InlineData("{ \"nested\": [1, 2] }")]
    [InlineData("[ true ]")]
    [InlineData("false")]
    public void SettingsStore_Load_InvalidOrderMarker_ReadsAsNotSeededAndSeeds(string markerJson)
    {
        // Arrange: the marker is followed by values that must still be read
        var configDir = CreateTempDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(configDir, "settings.json"),
                "{ \"HasUserRepoOrder\": " + markerJson + ", \"GitExecutablePath\": \"git-after\"," +
                " \"RecentRepos\": [ { \"Path\": \"plain\" }, { \"Path\": \"fav\", \"IsFavorite\": true } ] }");

            // Act
            var loaded = SettingsStore.Load(configDir);

            // Assert
            Assert.Multiple(
                () => Assert.Equal("git-after", loaded.GitExecutablePath),
                () => Assert.True(loaded.HasUserRepoOrder),
                () => Assert.Equal(["fav", "plain"], loaded.RecentRepos.Select(repo => repo.Path)));
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a first run (no settings file) returns settings without a stored order and with an
    ///     empty list; the order is seeded later by the main window's view model.
    /// </summary>
    [Fact]
    public void SettingsStore_Load_NoFile_ReturnsEmptyUnseededSettings()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        try
        {
            // Act
            var loaded = SettingsStore.Load(configDir);

            // Assert
            Assert.Empty(loaded.RecentRepos);
            Assert.False(loaded.HasUserRepoOrder);
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
        var path = Path.Combine(Path.GetTempPath(), "AgentControlRepoOrderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
