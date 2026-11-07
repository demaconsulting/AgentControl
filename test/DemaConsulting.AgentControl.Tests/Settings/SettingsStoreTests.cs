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

using System.Text.Json.Nodes;
using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.Settings;

namespace DemaConsulting.AgentControl.Tests.Settings;

/// <summary>
///     Unit tests for <see cref="SettingsStore"/>.
/// </summary>
public class SettingsStoreTests
{
    /// <summary>
    ///     Test that settings round-trip through save and load with all fields preserved.
    /// </summary>
    [Fact]
    public void SettingsStore_SaveThenLoad_RoundTripsAllFields()
    {
        // Arrange: a temp config directory and a fully populated settings instance
        var configDir = CreateTempDirectory();
        try
        {
            var settings = new AppSettings
            {
                Sources =
                [
                    new PackageSourceDefinition
                    {
                        Alias = "Default",
                        Type = PackageSourceType.Zip,
                        Location = @"\\shared\agent-packages"
                    },
                    new PackageSourceDefinition
                    {
                        Alias = "Feed",
                        Type = PackageSourceType.NuGet,
                        Location = @"\\shared\feed",
                        PackageIds = ["contoso.agents", "contoso.extras"],
                        AllowInsecureHttp = true,
                        RequireSignedPackages = true,
                        SignerFingerprint = "AB12"
                    }
                ],
                GitExecutablePath = @"C:\tools\git.exe",
                AgentTool = AgentToolKind.Custom,
                CustomAgentCommand = "my-custom-tool --flag",
                ShellPreference = "pwsh",
                RecentRepos =
                [
                    new RecentRepo
                    {
                        Path = @"C:\repos\example",
                        PinnedPackageName = "contoso-agents",
                        PinnedPackageVersion = "1.2.3",
                        PinnedSource = "Feed"
                    }
                ]
            };

            // Act: save then reload from the same directory
            SettingsStore.Save(settings, configDir);
            var loaded = SettingsStore.Load(configDir);

            // Assert: every field round-trips exactly
            Assert.Equal(2, loaded.Sources.Count);
            Assert.Equal("Default", loaded.Sources[0].Alias);
            Assert.Equal(PackageSourceType.Zip, loaded.Sources[0].Type);
            Assert.Equal(@"\\shared\agent-packages", loaded.Sources[0].Location);
            Assert.Equal("Feed", loaded.Sources[1].Alias);
            Assert.Equal(PackageSourceType.NuGet, loaded.Sources[1].Type);
            Assert.Equal(@"\\shared\feed", loaded.Sources[1].Location);
            Assert.Equal(["contoso.agents", "contoso.extras"], loaded.Sources[1].PackageIds);
            Assert.True(loaded.Sources[1].AllowInsecureHttp);
            Assert.True(loaded.Sources[1].RequireSignedPackages);
            Assert.Equal("AB12", loaded.Sources[1].SignerFingerprint);
            Assert.Null(loaded.PackageSourcePath);
            Assert.Equal(settings.GitExecutablePath, loaded.GitExecutablePath);
            Assert.Equal(settings.AgentTool, loaded.AgentTool);
            Assert.Equal(settings.CustomAgentCommand, loaded.CustomAgentCommand);
            Assert.Equal(settings.ShellPreference, loaded.ShellPreference);
            var loadedRepo = Assert.Single(loaded.RecentRepos);
            Assert.Equal(@"C:\repos\example", loadedRepo.Path);
            Assert.Equal("contoso-agents", loadedRepo.PinnedPackageName);
            Assert.Equal("1.2.3", loadedRepo.PinnedPackageVersion);
            Assert.Equal("Feed", loadedRepo.PinnedSource);
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that loading from a directory with no settings file returns defaults instead of
    ///     throwing.
    /// </summary>
    [Fact]
    public void SettingsStore_Load_NoSettingsFile_ReturnsDefaults()
    {
        // Arrange: an empty temp directory with no settings.json
        var configDir = CreateTempDirectory();
        try
        {
            // Act: load from the empty directory
            var loaded = SettingsStore.Load(configDir);

            // Assert: a fresh default instance is returned, not an error
            Assert.Null(loaded.PackageSourcePath);
            Assert.Equal(AgentToolKind.CopilotCli, loaded.AgentTool);
            Assert.Empty(loaded.RecentRepos);
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that saving creates the configuration directory when it does not already exist.
    /// </summary>
    [Fact]
    public void SettingsStore_Save_DirectoryDoesNotExist_CreatesDirectory()
    {
        // Arrange: a path to a directory that does not yet exist
        var parent = CreateTempDirectory();
        var configDir = Path.Combine(parent, "nested", "config");
        try
        {
            // Act: save settings to the non-existent nested directory
            SettingsStore.Save(new AppSettings(), configDir);

            // Assert: the directory and settings file were created
            Assert.True(File.Exists(Path.Combine(configDir, "settings.json")));
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    /// <summary>
    ///     Test that saving with an invalid configuration directory path throws
    ///     InvalidOperationException rather than leaking the raw path-validation exception.
    /// </summary>
    [Fact]
    public void SettingsStore_Save_InvalidDirectoryPath_ThrowsInvalidOperationException()
    {
        // Arrange: a configuration directory path containing a NUL character - rejected by
        // Directory.CreateDirectory on every platform since a path can never legally contain one
        var invalidConfigDir = Path.Combine(Path.GetTempPath(), "agentcontrol_invalid_\0_dir");

        // Act / Assert: the documented InvalidOperationException contract is honored instead of
        // the raw ArgumentException escaping uncaught
        Assert.Throws<InvalidOperationException>(() => SettingsStore.Save(new AppSettings(), invalidConfigDir));
    }

    /// <summary>
    ///     Test that saving with a null settings instance throws an ArgumentNullException.
    /// </summary>
    [Fact]
    public void SettingsStore_Save_NullSettings_ThrowsArgumentNullException()
    {
        // Act / Assert: null settings are rejected
        Assert.Throws<ArgumentNullException>(() => SettingsStore.Save(null!, CreateTempDirectory()));
    }

    /// <summary>
    ///     Test that the default configuration directory is rooted under the special
    ///     ApplicationData folder and named "AgentControl".
    /// </summary>
    [Fact]
    public void SettingsStore_GetDefaultConfigDirectory_ReturnsPathUnderApplicationData()
    {
        // Act: read the default configuration directory
        var directory = SettingsStore.GetDefaultConfigDirectory();

        // Assert: it is nested under ApplicationData and named "AgentControl"
        var expectedRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Assert.StartsWith(expectedRoot, directory, StringComparison.Ordinal);
        Assert.EndsWith("AgentControl", directory, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a settings file written before multiple sources existed (only the legacy
    ///     PackageSourcePath) loads with that path migrated to a Default zip source.
    /// </summary>
    [Fact]
    public void SettingsStore_Load_LegacyPackageSourcePath_MigratesToDefaultZipSource()
    {
        // Arrange: a legacy settings file with only the single-path field
        var configDir = CreateTempDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(configDir, "settings.json"),
                """{ "PackageSourcePath": "\\\\shared\\agent-packages", "AgentTool": 0 }""");

            // Act
            var loaded = SettingsStore.Load(configDir);

            // Assert: the path became the Default zip source and the legacy field is cleared
            var source = Assert.Single(loaded.Sources);
            Assert.Equal("Default", source.Alias);
            Assert.Equal(PackageSourceType.Zip, source.Type);
            Assert.Equal(@"\\shared\agent-packages", source.Location);
            Assert.Null(loaded.PackageSourcePath);
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that when a settings file has both a Sources list and a legacy path, the Sources
    ///     list wins and the legacy path is ignored (not added as another source).
    /// </summary>
    [Fact]
    public void SettingsStore_Load_SourcesAndLegacyPath_SourcesWin()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(configDir, "settings.json"),
                """{ "PackageSourcePath": "C:\\legacy", "Sources": [ { "Alias": "Mine", "Type": "Zip", "Location": "C:\\mine" } ] }""");

            // Act
            var loaded = SettingsStore.Load(configDir);

            // Assert
            var source = Assert.Single(loaded.Sources);
            Assert.Equal("Mine", source.Alias);
            Assert.Null(loaded.PackageSourcePath);
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that saving settings that only carry the legacy path writes it as a Sources entry
    ///     and never writes the legacy field, without altering the in-memory settings.
    /// </summary>
    [Fact]
    public void SettingsStore_Save_LegacyPackageSourcePath_WritesSourcesNotLegacyField()
    {
        // Arrange: in-memory settings seeded the legacy way
        var configDir = CreateTempDirectory();
        try
        {
            var settings = new AppSettings { PackageSourcePath = @"D:\packages" };

            // Act
            SettingsStore.Save(settings, configDir);
            var json = JsonNode.Parse(File.ReadAllText(Path.Combine(configDir, "settings.json")))!.AsObject();

            // Assert: Sources holds the Default zip source, the legacy field is absent, and the
            // caller's instance is untouched
            Assert.False(json.ContainsKey("PackageSourcePath"));
            var source = Assert.Single(json["Sources"]!.AsArray())!.AsObject();
            Assert.Equal("Default", (string?)source["Alias"]);
            Assert.Equal("Zip", (string?)source["Type"]);
            Assert.Equal(@"D:\packages", (string?)source["Location"]);
            Assert.Equal(@"D:\packages", settings.PackageSourcePath);
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that the persisted settings JSON never contains a property that could hold a
    ///     secret, even for a fully populated NuGet source.
    /// </summary>
    [Fact]
    public void SettingsStore_Save_FullyPopulatedSources_JsonContainsNoSecretLikeProperties()
    {
        // Arrange: a NuGet server source with every option populated
        var configDir = CreateTempDirectory();
        try
        {
            var settings = new AppSettings
            {
                Sources =
                [
                    new PackageSourceDefinition
                    {
                        Alias = "Server",
                        Type = PackageSourceType.NuGet,
                        Location = "https://feeds.example.com/v3/index.json",
                        PackageIds = ["contoso.agents"],
                        AllowInsecureHttp = false,
                        RequireSignedPackages = true,
                        SignerFingerprint = "ABCDEF"
                    }
                ]
            };

            // Act
            SettingsStore.Save(settings, configDir);
            var json = File.ReadAllText(Path.Combine(configDir, "settings.json"));

            // Assert: no property name suggests a stored credential
            string[] secretMarkers = ["password", "passwd", "secret", "token", "credential", "key", "pat"];
            var propertyNames = new List<string>();
            CollectPropertyNames(JsonNode.Parse(json)!, propertyNames);
            Assert.All(
                propertyNames,
                name => Assert.DoesNotContain(
                    secretMarkers, marker => string.Equals(name, marker, StringComparison.OrdinalIgnoreCase) ||
                                             (marker.Length > 3 && name.Contains(marker, StringComparison.OrdinalIgnoreCase))));
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that settings holding a source location with an embedded credential are refused:
    ///     nothing is written, so the sentinel password can never appear in the settings file.
    /// </summary>
    [Fact]
    public void SettingsStore_Save_SourceLocationWithEmbeddedCredential_ThrowsAndWritesNothing()
    {
        // Arrange: a NuGet server source whose location embeds a sentinel password
        var configDir = CreateTempDirectory();
        try
        {
            var settings = new AppSettings
            {
                Sources =
                [
                    new PackageSourceDefinition
                    {
                        Alias = "Server",
                        Type = PackageSourceType.NuGet,
                        Location = "https://user:SentinelSecretValue@feeds.example.com/v3/index.json"
                    }
                ]
            };

            // Act
            var ex = Assert.Throws<InvalidOperationException>(() => SettingsStore.Save(settings, configDir));

            // Assert: refused without leaking the secret, and no settings file (or temp file) exists
            Assert.DoesNotContain("SentinelSecretValue", ex.Message, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(configDir));
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a legacy single package path embedding a credential is refused on save too,
    ///     so the migration path cannot write a secret either.
    /// </summary>
    [Fact]
    public void SettingsStore_Save_LegacyPackageSourcePathWithEmbeddedCredential_ThrowsAndWritesNothing()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        try
        {
            var settings = new AppSettings { PackageSourcePath = "https://user:SentinelSecretValue@host/feed" };

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => SettingsStore.Save(settings, configDir));
            Assert.Empty(Directory.GetFiles(configDir));
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a save replaces the settings file atomically: afterwards only the settings
    ///     file exists (no temporary file is left behind) and it holds the latest content.
    /// </summary>
    [Fact]
    public void SettingsStore_Save_RepeatedSaves_LeavesOnlySettingsFileWithLatestContent()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        try
        {
            SettingsStore.Save(new AppSettings { GitExecutablePath = "first" }, configDir);

            // Act: overwrite with new content
            SettingsStore.Save(new AppSettings { GitExecutablePath = "second" }, configDir);

            // Assert: exactly one file, with the second value
            var file = Assert.Single(Directory.GetFiles(configDir));
            Assert.Equal("settings.json", Path.GetFileName(file));
            Assert.Equal("second", SettingsStore.Load(configDir).GitExecutablePath);
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Recursively collects every JSON property name in a node.
    /// </summary>
    /// <param name="node">The node to walk.</param>
    /// <param name="names">Receives the property names.</param>
    private static void CollectPropertyNames(JsonNode node, List<string> names)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, child) in obj)
                {
                    names.Add(name);
                    if (child is not null)
                    {
                        CollectPropertyNames(child, names);
                    }
                }

                break;
            case JsonArray array:
                foreach (var child in array)
                {
                    if (child is not null)
                    {
                        CollectPropertyNames(child, names);
                    }
                }

                break;
        }
    }

    /// <summary>
    ///     Creates a unique temporary directory for test isolation.
    /// </summary>
    /// <returns>The created directory's path.</returns>
    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_settings_test_" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        return path;
    }
}
