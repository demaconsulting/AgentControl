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
                PackageSourcePath = @"\\shared\agent-packages",
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
                        PinnedPackageVersion = "1.2.3"
                    }
                ]
            };

            // Act: save then reload from the same directory
            SettingsStore.Save(settings, configDir);
            var loaded = SettingsStore.Load(configDir);

            // Assert: every field round-trips exactly
            Assert.Equal(settings.PackageSourcePath, loaded.PackageSourcePath);
            Assert.Equal(settings.GitExecutablePath, loaded.GitExecutablePath);
            Assert.Equal(settings.AgentTool, loaded.AgentTool);
            Assert.Equal(settings.CustomAgentCommand, loaded.CustomAgentCommand);
            Assert.Equal(settings.ShellPreference, loaded.ShellPreference);
            var loadedRepo = Assert.Single(loaded.RecentRepos);
            Assert.Equal(@"C:\repos\example", loadedRepo.Path);
            Assert.Equal("contoso-agents", loadedRepo.PinnedPackageName);
            Assert.Equal("1.2.3", loadedRepo.PinnedPackageVersion);
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
