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

using DemaConsulting.AgentControl.RepoConfig;

namespace DemaConsulting.AgentControl.Tests.RepoConfig;

/// <summary>
///     Unit tests for <see cref="RepoPinStore"/>.
/// </summary>
public class RepoPinStoreTests
{
    /// <summary>
    ///     Test that a pin round-trips through save and load with all fields preserved.
    /// </summary>
    [Fact]
    public void RepoPinStore_SaveThenLoad_RoundTripsAllFields()
    {
        // Arrange: a temp repo root and a pin to persist
        var repoRoot = CreateTempDirectory();
        try
        {
            var pin = new RepoPin { PackageName = "contoso-agents", Version = "2.1.0" };

            // Act: save then reload from the same repo root
            RepoPinStore.Save(repoRoot, pin);
            var loaded = RepoPinStore.Load(repoRoot);

            // Assert: both fields round-trip exactly
            Assert.NotNull(loaded);
            Assert.Equal("contoso-agents", loaded.PackageName);
            Assert.Equal("2.1.0", loaded.Version);
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    /// <summary>
    ///     Test that loading from a repo with no pin file returns null instead of throwing.
    /// </summary>
    [Fact]
    public void RepoPinStore_Load_NoPinFile_ReturnsNull()
    {
        // Arrange: an empty repo root with no .agentcontrol.json
        var repoRoot = CreateTempDirectory();
        try
        {
            // Act: load from the empty repo root
            var loaded = RepoPinStore.Load(repoRoot);

            // Assert: no pin file means null, not an exception
            Assert.Null(loaded);
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    /// <summary>
    ///     Test that saving overwrites a previously saved pin with new values.
    /// </summary>
    [Fact]
    public void RepoPinStore_Save_ExistingPinFile_OverwritesWithNewValues()
    {
        // Arrange: an existing pin file for an older version
        var repoRoot = CreateTempDirectory();
        try
        {
            RepoPinStore.Save(repoRoot, new RepoPin { PackageName = "contoso-agents", Version = "1.0.0" });

            // Act: overwrite with a newer version
            RepoPinStore.Save(repoRoot, new RepoPin { PackageName = "contoso-agents", Version = "2.0.0" });
            var loaded = RepoPinStore.Load(repoRoot);

            // Assert: the loaded pin reflects the overwrite, not the original value
            Assert.NotNull(loaded);
            Assert.Equal("2.0.0", loaded.Version);
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    /// <summary>
    ///     Test that loading with a null repo root throws an ArgumentNullException.
    /// </summary>
    [Fact]
    public void RepoPinStore_Load_NullRepoRoot_ThrowsArgumentNullException()
    {
        // Act / Assert: a null repo root is rejected
        Assert.Throws<ArgumentNullException>(() => RepoPinStore.Load(null!));
    }

    /// <summary>
    ///     Test that saving with a null pin throws an ArgumentNullException.
    /// </summary>
    [Fact]
    public void RepoPinStore_Save_NullPin_ThrowsArgumentNullException()
    {
        // Arrange: a valid repo root
        var repoRoot = CreateTempDirectory();
        try
        {
            // Act / Assert: a null pin is rejected
            Assert.Throws<ArgumentNullException>(() => RepoPinStore.Save(repoRoot, null!));
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    /// <summary>
    ///     Test that AgentsMdTemplateDeclined round-trips through save and load when set true.
    /// </summary>
    [Fact]
    public void RepoPinStore_SaveThenLoad_AgentsMdTemplateDeclinedTrue_RoundTrips()
    {
        // Arrange: a temp repo root and a pin with the decline flag set
        var repoRoot = CreateTempDirectory();
        try
        {
            var pin = new RepoPin { PackageName = "contoso-agents", Version = "1.0.0", AgentsMdTemplateDeclined = true };

            // Act: save then reload from the same repo root
            RepoPinStore.Save(repoRoot, pin);
            var loaded = RepoPinStore.Load(repoRoot);

            // Assert: the decline flag round-trips exactly
            Assert.NotNull(loaded);
            Assert.True(loaded.AgentsMdTemplateDeclined);
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    /// <summary>
    ///     Test that loading a legacy pin file (written before AgentsMdTemplateDeclined existed)
    ///     defaults the new field to false rather than throwing or leaving it unset.
    /// </summary>
    [Fact]
    public void RepoPinStore_Load_LegacyPinFileMissingAgentsMdTemplateDeclinedField_DefaultsToFalse()
    {
        // Arrange: a hand-written legacy .agentcontrol.json with no AgentsMdTemplateDeclined field
        var repoRoot = CreateTempDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(repoRoot, ".agentcontrol.json"),
                """{ "PackageName": "contoso-agents", "Version": "1.0.0" }""");

            // Act: load the legacy pin file
            var loaded = RepoPinStore.Load(repoRoot);

            // Assert: the new field defaults to false (not yet declined), not an exception
            Assert.NotNull(loaded);
            Assert.Equal("contoso-agents", loaded.PackageName);
            Assert.False(loaded.AgentsMdTemplateDeclined);
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a legacy (version 1) pin file with no source and no schema version loads with
    ///     its source mapped to the legacy Default alias, and the original fields intact.
    /// </summary>
    [Fact]
    public void RepoPinStore_Load_LegacyV1PinFile_MapsToDefaultSource()
    {
        // Arrange: a hand-written v1 pin file
        var repoRoot = CreateTempDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(repoRoot, ".agentcontrol.json"),
                """{ "PackageName": "contoso-agents", "Version": "1.0.0", "AgentsMdTemplateDeclined": true }""");

            // Act
            var loaded = RepoPinStore.Load(repoRoot);

            // Assert: Default source, v1 fields preserved
            Assert.NotNull(loaded);
            Assert.Equal("Default", loaded.Source);
            Assert.Equal("contoso-agents", loaded.PackageName);
            Assert.Equal("1.0.0", loaded.Version);
            Assert.True(loaded.AgentsMdTemplateDeclined);
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    /// <summary>
    ///     Test that saving writes schema version 2 and the source alias, and that a pin
    ///     round-trips with its source.
    /// </summary>
    [Fact]
    public void RepoPinStore_SaveThenLoad_SourceAlias_WritesSchemaVersion2AndRoundTripsSource()
    {
        // Arrange
        var repoRoot = CreateTempDirectory();
        try
        {
            // Act
            RepoPinStore.Save(repoRoot, new RepoPin { Source = "Team Feed", PackageName = "contoso-agents", Version = "1.2.3.4+b5" });
            var loaded = RepoPinStore.Load(repoRoot);
            var json = File.ReadAllText(Path.Combine(repoRoot, ".agentcontrol.json"));

            // Assert: the file declares schema 2 and the source, and the pin round-trips
            Assert.Contains("\"SchemaVersion\": 2", json, StringComparison.Ordinal);
            Assert.Contains("\"Source\": \"Team Feed\"", json, StringComparison.Ordinal);
            Assert.NotNull(loaded);
            Assert.Equal(2, loaded.SchemaVersion);
            Assert.Equal("Team Feed", loaded.Source);
            Assert.Equal("1.2.3.4+b5", loaded.Version);
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    /// <summary>
    ///     Test that camelCase property names written by hand are read, since pin properties are
    ///     matched case-insensitively.
    /// </summary>
    [Fact]
    public void RepoPinStore_Load_CamelCasePropertyNames_AreRead()
    {
        // Arrange
        var repoRoot = CreateTempDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(repoRoot, ".agentcontrol.json"),
                """{ "schemaVersion": 2, "source": "Feed", "packageName": "p", "version": "1.0.0" }""");

            // Act
            var loaded = RepoPinStore.Load(repoRoot);

            // Assert
            Assert.NotNull(loaded);
            Assert.Equal("Feed", loaded.Source);
            Assert.Equal("p", loaded.PackageName);
            Assert.Equal(2, loaded.SchemaVersion);
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    /// <summary>
    ///     Test that properties this version does not know survive a load-modify-save cycle, and
    ///     also survive a save of a freshly built pin over an existing file.
    /// </summary>
    [Fact]
    public void RepoPinStore_Save_UnknownProperties_ArePreserved()
    {
        // Arrange: a v2 pin file with a property from some other version, including a nested value
        var repoRoot = CreateTempDirectory();
        try
        {
            var path = Path.Combine(repoRoot, ".agentcontrol.json");
            File.WriteAllText(
                path,
                """{ "SchemaVersion": 2, "Source": "Feed", "PackageName": "p", "Version": "1.0.0", "FutureField": { "a": [1, 2] }, "Note": "keep" }""");

            // Act 1: load, change the version, save
            var loaded = RepoPinStore.Load(repoRoot);
            Assert.NotNull(loaded);
            loaded.Version = "2.0.0";
            RepoPinStore.Save(repoRoot, loaded);
            var afterLoadedSave = File.ReadAllText(path);

            // Act 2: save a brand-new pin object (as a caller that did not load would)
            RepoPinStore.Save(repoRoot, new RepoPin { Source = "Feed", PackageName = "p", Version = "3.0.0" });
            var afterFreshSave = File.ReadAllText(path);

            // Assert: the unknown properties are still there in both cases
            Assert.Contains("\"FutureField\"", afterLoadedSave, StringComparison.Ordinal);
            Assert.Contains("\"Note\": \"keep\"", afterLoadedSave, StringComparison.Ordinal);
            Assert.Contains("\"2.0.0\"", afterLoadedSave, StringComparison.Ordinal);
            Assert.Contains("\"FutureField\"", afterFreshSave, StringComparison.Ordinal);
            Assert.Contains("\"a\": [", afterFreshSave.Replace("\r\n", "\n").Replace("\n", string.Empty), StringComparison.Ordinal);
            Assert.Contains("\"3.0.0\"", afterFreshSave, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a pin file declaring a newer schema than this build supports is rejected with
    ///     an actionable message instead of being interpreted.
    /// </summary>
    [Fact]
    public void RepoPinStore_Load_NewerSchemaVersion_ThrowsUnsupportedPinSchemaExceptionWithActionableMessage()
    {
        // Arrange: a pin file from a newer AgentControl
        var repoRoot = CreateTempDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(repoRoot, ".agentcontrol.json"),
                """{ "SchemaVersion": 3, "Source": "Feed", "PackageName": "p", "Version": "1.0.0" }""");

            // Act
            var ex = Assert.Throws<UnsupportedPinSchemaException>(() => RepoPinStore.Load(repoRoot));

            // Assert: the message names the file's version and tells the user what to do
            Assert.Equal(3, ex.SchemaVersion);
            Assert.Contains("3", ex.Message, StringComparison.Ordinal);
            Assert.Contains("Update AgentControl", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    /// <summary>
    ///     Test that saving over a pin file with a newer schema is refused and leaves the file
    ///     byte-for-byte unchanged (an older AgentControl never clobbers a newer file).
    /// </summary>
    [Fact]
    public void RepoPinStore_Save_ExistingNewerSchemaFile_ThrowsAndLeavesFileUnchanged()
    {
        // Arrange
        var repoRoot = CreateTempDirectory();
        try
        {
            var path = Path.Combine(repoRoot, ".agentcontrol.json");
            const string newerContent = """{ "SchemaVersion": 9, "Whatever": true }""";
            File.WriteAllText(path, newerContent);

            // Act / Assert
            Assert.Throws<UnsupportedPinSchemaException>(
                () => RepoPinStore.Save(repoRoot, new RepoPin { Source = "Feed", PackageName = "p", Version = "1.0.0" }));
            Assert.Equal(newerContent, File.ReadAllText(path));
            Assert.Equal([path], Directory.GetFiles(repoRoot));
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a corrupt existing pin file is replaced by a save (overwriting it is the
    ///     repair), and that a save leaves no temporary file behind.
    /// </summary>
    [Fact]
    public void RepoPinStore_Save_CorruptExistingFile_IsReplacedAndLeavesNoTemporaryFile()
    {
        // Arrange
        var repoRoot = CreateTempDirectory();
        try
        {
            var path = Path.Combine(repoRoot, ".agentcontrol.json");
            File.WriteAllText(path, "{ this is not json");

            // Act
            RepoPinStore.Save(repoRoot, new RepoPin { Source = "Feed", PackageName = "p", Version = "1.0.0" });

            // Assert
            Assert.Equal("p", RepoPinStore.Load(repoRoot)?.PackageName);
            Assert.Equal([path], Directory.GetFiles(repoRoot));
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    /// <summary>
    ///     Creates a unique temporary directory for test isolation.
    /// </summary>
    /// <returns>The created directory's path.</returns>
    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_repo_config_test_" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        return path;
    }
}
