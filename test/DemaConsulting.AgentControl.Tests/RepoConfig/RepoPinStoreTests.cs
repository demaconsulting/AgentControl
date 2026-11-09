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

// cspell:ignore SCHEMAVERSION
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
            var pin = new RepoPin { Source = "Default", PackageName = "contoso-agents", Version = "2.1.0" };

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
            RepoPinStore.Save(repoRoot, new RepoPin { Source = "Default", PackageName = "contoso-agents", Version = "1.0.0" });

            // Act: overwrite with a newer version
            RepoPinStore.Save(repoRoot, new RepoPin { Source = "Default", PackageName = "contoso-agents", Version = "2.0.0" });
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
            var pin = new RepoPin { Source = "Default", PackageName = "contoso-agents", Version = "1.0.0", AgentsMdTemplateDeclined = true };

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
    ///     Test that saving writes schema version 3 and the source alias, and that a pin
    ///     round-trips with its source.
    /// </summary>
    [Fact]
    public void RepoPinStore_SaveThenLoad_SourceAlias_WritesSchemaVersion3AndRoundTripsSource()
    {
        // Arrange
        var repoRoot = CreateTempDirectory();
        try
        {
            // Act
            RepoPinStore.Save(repoRoot, new RepoPin { Source = "Team Feed", PackageName = "contoso-agents", Version = "1.2.3.4+b5" });
            var loaded = RepoPinStore.Load(repoRoot);
            var json = File.ReadAllText(Path.Combine(repoRoot, ".agentcontrol.json"));

            // Assert: the file declares schema 3 and the source, and the pin round-trips
            Assert.Contains("\"SchemaVersion\": 3", json, StringComparison.Ordinal);
            Assert.Contains("\"Source\": \"Team Feed\"", json, StringComparison.Ordinal);
            Assert.NotNull(loaded);
            Assert.Equal(3, loaded.SchemaVersion);
            Assert.Equal("Team Feed", loaded.Source);
            Assert.Equal("1.2.3.4+b5", loaded.Version);
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a recorded content identity round-trips through save and load, and is not
    ///     written at all when the pin has none.
    /// </summary>
    [Fact]
    public void RepoPinStore_SaveThenLoad_ContentIdentity_RoundTripsAndIsOmittedWhenAbsent()
    {
        // Arrange
        var repoRoot = CreateTempDirectory();
        try
        {
            var path = Path.Combine(repoRoot, ".agentcontrol.json");
            var identity = "sha256:" + new string('a', 64);

            // Act 1: save a pin that records an identity, then reload it
            RepoPinStore.Save(
                repoRoot,
                new RepoPin { Source = "Feed", PackageName = "p", Version = "1.0.0", ContentIdentity = identity });
            var withIdentity = RepoPinStore.Load(repoRoot);
            var jsonWithIdentity = File.ReadAllText(path);

            // Act 2: save a pin that records none
            RepoPinStore.Save(repoRoot, new RepoPin { Source = "Feed", PackageName = "p", Version = "1.0.0" });
            var withoutIdentity = RepoPinStore.Load(repoRoot);
            var jsonWithoutIdentity = File.ReadAllText(path);

            // Assert: the identity is written and read back, and is absent when not recorded
            Assert.Contains($"\"ContentIdentity\": \"{identity}\"", jsonWithIdentity, StringComparison.Ordinal);
            Assert.Equal(identity, withIdentity?.ContentIdentity);
            Assert.DoesNotContain("ContentIdentity", jsonWithoutIdentity, StringComparison.OrdinalIgnoreCase);
            Assert.Null(withoutIdentity?.ContentIdentity);
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    /// <summary>
    ///     Test that pin files written before identities existed (version 1 and version 2) still
    ///     load, with no content identity, and are written back as schema version 3.
    /// </summary>
    [Theory]
    [InlineData("""{ "PackageName": "contoso-agents", "Version": "1.0.0" }""")]
    [InlineData("""{ "SchemaVersion": 2, "Source": "Feed", "PackageName": "contoso-agents", "Version": "1.0.0" }""")]
    public void RepoPinStore_Load_PinWithoutContentIdentity_LoadsWithNullIdentityAndUpgradesToSchema3OnSave(string json)
    {
        // Arrange: a version 1 or version 2 pin file
        var repoRoot = CreateTempDirectory();
        try
        {
            var path = Path.Combine(repoRoot, ".agentcontrol.json");
            File.WriteAllText(path, json);

            // Act: load it, then save it back
            var loaded = RepoPinStore.Load(repoRoot);
            Assert.NotNull(loaded);
            RepoPinStore.Save(repoRoot, loaded);

            // Assert: no identity was invented, and the rewritten file is schema 3
            Assert.Null(loaded.ContentIdentity);
            Assert.Equal("contoso-agents", loaded.PackageName);
            Assert.Contains("\"SchemaVersion\": 3", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a hand-written camelCase <c>contentIdentity</c> is read.
    /// </summary>
    [Fact]
    public void RepoPinStore_Load_CamelCaseContentIdentity_IsRead()
    {
        // Arrange
        var repoRoot = CreateTempDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(repoRoot, ".agentcontrol.json"),
                """{ "schemaVersion": 3, "source": "Feed", "packageName": "p", "version": "1.0.0", "contentIdentity": "sha256:abc" }""");

            // Act
            var loaded = RepoPinStore.Load(repoRoot);

            // Assert
            Assert.Equal("sha256:abc", loaded?.ContentIdentity);
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
    ///     Test that a pin file of the current schema that names no source is rejected instead of
    ///     being read as a pin to the legacy Default source.
    /// </summary>
    [Theory]
    [InlineData("""{ "SchemaVersion": 2, "PackageName": "contoso-agents", "Version": "1.0.0" }""")]
    [InlineData("""{ "SchemaVersion": 2, "Source": "  ", "PackageName": "contoso-agents", "Version": "1.0.0" }""")]
    public void RepoPinStore_Load_CurrentSchemaPinWithoutSource_IsRejectedInsteadOfMappedToDefault(string json)
    {
        // Arrange
        var repoRoot = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(repoRoot, ".agentcontrol.json"), json);

            // Act
            var ex = Assert.Throws<InvalidOperationException>(() => RepoPinStore.Load(repoRoot));

            // Assert
            Assert.IsNotType<UnsupportedPinSchemaException>(ex);
            Assert.Contains(".agentcontrol.json", ex.Message, StringComparison.Ordinal);
            Assert.Contains("source", ex.Message, StringComparison.OrdinalIgnoreCase);
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
                """{ "SchemaVersion": 4, "Source": "Feed", "PackageName": "p", "Version": "1.0.0" }""");

            // Act
            var ex = Assert.Throws<UnsupportedPinSchemaException>(() => RepoPinStore.Load(repoRoot));

            // Assert: the message names the file's version and tells the user what to do
            Assert.Equal(4, ex.SchemaVersion);
            Assert.Contains("4", ex.Message, StringComparison.Ordinal);
            Assert.Contains("up to 3", ex.Message, StringComparison.Ordinal);
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
    ///     Test that a pin file from a newer schema is recognized as newer - not treated as corrupt -
    ///     even when a property this build knows has another type there (an identity that is an
    ///     object or a number, a name that is a list): the declared schema is read before the typed
    ///     pin is.
    /// </summary>
    [Theory]
    [InlineData("""{ "SchemaVersion": 4, "Source": "Feed", "PackageName": "p", "ContentIdentity": { "algorithm": "sha512", "value": "ab" } }""")]
    [InlineData("""{ "SchemaVersion": 4, "Source": "Feed", "PackageName": "p", "ContentIdentity": 7 }""")]
    [InlineData("""{ "SchemaVersion": 4, "Source": "Feed", "PackageName": ["p", "q"], "Version": "1.0.0" }""")]
    [InlineData("""{ "SchemaVersion": 4, "Source": "Feed", "PackageName": "p", "AgentsMdTemplateDeclined": "maybe" }""")]
    [InlineData("""{ "PackageName": ["p"], "schemaVersion": 4 }""")]
    public void RepoPinStore_Load_NewerSchemaWithIncompatibleKnownPropertyType_ThrowsUnsupportedPinSchemaException(string json)
    {
        // Arrange: a newer file whose known properties have other types than this build's pin
        var repoRoot = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(repoRoot, ".agentcontrol.json"), json);

            // Act
            var ex = Assert.Throws<UnsupportedPinSchemaException>(() => RepoPinStore.Load(repoRoot));

            // Assert: reported as a newer schema, not as an unreadable file
            Assert.Equal(4, ex.SchemaVersion);
            Assert.Contains("Update AgentControl", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    /// <summary>
    ///     Test that saving over a newer-schema pin file with an incompatible known property type is
    ///     refused and leaves the file byte-for-byte unchanged: it is a valid file from a newer tool,
    ///     not a corrupt one to repair by overwriting.
    /// </summary>
    [Theory]
    [InlineData("""{ "SchemaVersion": 4, "Source": "Feed", "PackageName": "p", "ContentIdentity": { "algorithm": "sha512", "value": "ab" } }""")]
    [InlineData("""{ "SchemaVersion": 4, "Source": "Feed", "PackageName": "p", "ContentIdentity": 7 }""")]
    [InlineData("""{ "SchemaVersion": 4, "PackageName": ["p"] }""")]
    public void RepoPinStore_Save_NewerSchemaWithIncompatibleKnownPropertyType_ThrowsAndLeavesFileUnchanged(string json)
    {
        // Arrange
        var repoRoot = CreateTempDirectory();
        try
        {
            var path = Path.Combine(repoRoot, ".agentcontrol.json");
            File.WriteAllText(path, json);
            var before = File.ReadAllBytes(path);

            // Act
            var ex = Assert.Throws<UnsupportedPinSchemaException>(
                () => RepoPinStore.Save(repoRoot, new RepoPin { Source = "Feed", PackageName = "p", Version = "1.0.0" }));

            // Assert: refused for the schema, and the file and its folder are untouched
            Assert.Equal(4, ex.SchemaVersion);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Equal([path], Directory.GetFiles(repoRoot));
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    /// <summary>
    ///     Test that saving over a pin file whose declared schema is a number beyond any numeric range
    ///     (a positive integer of forty-odd digits) is refused as a newer schema and leaves the file
    ///     byte-for-byte unchanged: it is never mistaken for a corrupt file and overwritten.
    /// </summary>
    [Fact]
    public void RepoPinStore_Save_SchemaVersionBeyondAnyNumericRange_ThrowsAndLeavesFileUnchanged()
    {
        // Arrange: a pin file declaring a schema that fits no decimal or integer
        var repoRoot = CreateTempDirectory();
        try
        {
            var path = Path.Combine(repoRoot, ".agentcontrol.json");
            File.WriteAllText(path, """{ "SchemaVersion": 999999999999999999999999999999999999999999, "PackageName": "p" }""");
            var before = File.ReadAllBytes(path);

            // Act
            var ex = Assert.Throws<UnsupportedPinSchemaException>(
                () => RepoPinStore.Save(repoRoot, new RepoPin { Source = "Feed", PackageName = "p", Version = "1.0.0" }));
            var loadFailure = Assert.Throws<UnsupportedPinSchemaException>(() => RepoPinStore.Load(repoRoot));

            // Assert: refused as newer in both directions, and the file and its folder are untouched
            Assert.Equal(int.MaxValue, ex.SchemaVersion);
            Assert.Equal(int.MaxValue, loadFailure.SchemaVersion);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Equal([path], Directory.GetFiles(repoRoot));
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a declared schema that is text or a whole number too large for an integer is
    ///     still recognized as newer, and that a letter-case variant of the property name is read.
    /// </summary>
    [Theory]
    [InlineData("""{ "SchemaVersion": "5", "PackageName": ["p"] }""", 5)]
    [InlineData("""{ "SchemaVersion": 99999999999, "PackageName": ["p"] }""", int.MaxValue)]
    [InlineData("""{ "SchemaVersion": 999999999999999999999999999999999999999999 }""", int.MaxValue)]
    [InlineData("""{ "SchemaVersion": 1e999 }""", int.MaxValue)]
    [InlineData("""{ "SchemaVersion": "999999999999999999999999999999999999999999" }""", int.MaxValue)]
    [InlineData("""{ "SCHEMAVERSION": 7 }""", 7)]
    public void RepoPinStore_ProbeSchemaVersion_TextOrOversizedOrDifferentlyCasedVersion_IsReadAsNewer(string json, int expected)
    {
        // Arrange: a raw document whose declared schema is not a plain integer property

        // Act
        var declared = RepoPinStore.ProbeSchemaVersion(json);

        // Assert
        Assert.Equal(expected, declared);
    }

    /// <summary>
    ///     Test that the schema probe reports no version for a document without a usable one (a
    ///     schema 1 file, a version of another kind, a document that is not an object) and rejects
    ///     text that is not JSON.
    /// </summary>
    [Fact]
    public void RepoPinStore_ProbeSchemaVersion_NoUsableVersion_ReturnsNullAndInvalidJsonThrows()
    {
        // Arrange: documents without a declared integer schema

        // Act / Assert
        Assert.Null(RepoPinStore.ProbeSchemaVersion("""{ "PackageName": "p" }"""));
        Assert.Null(RepoPinStore.ProbeSchemaVersion("""{ "SchemaVersion": true }"""));
        Assert.Null(RepoPinStore.ProbeSchemaVersion("""{ "SchemaVersion": "next" }"""));
        Assert.Null(RepoPinStore.ProbeSchemaVersion("[1, 2]"));
        Assert.Null(RepoPinStore.ProbeSchemaVersion("null"));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => RepoPinStore.ProbeSchemaVersion("{ not json"));
    }

    /// <summary>
    ///     Test that a pin file of the current schema whose known property has an incompatible type
    ///     is corrupt: loading reports it as unreadable (not as a newer schema), and saving replaces
    ///     it, which is the repair.
    /// </summary>
    [Fact]
    public void RepoPinStore_CurrentSchemaWithIncompatibleKnownPropertyType_IsCorruptAndReplacedOnSave()
    {
        // Arrange: a schema 3 file whose identity is an object
        var repoRoot = CreateTempDirectory();
        try
        {
            var path = Path.Combine(repoRoot, ".agentcontrol.json");
            File.WriteAllText(path, """{ "SchemaVersion": 3, "Source": "Feed", "PackageName": "p", "ContentIdentity": { "x": 1 } }""");

            // Act
            var loadFailure = Assert.Throws<InvalidOperationException>(() => RepoPinStore.Load(repoRoot));
            RepoPinStore.Save(repoRoot, new RepoPin { Source = "Feed", PackageName = "p", Version = "1.0.0" });

            // Assert: unreadable, not newer; and replaced by a valid current pin
            Assert.IsNotType<UnsupportedPinSchemaException>(loadFailure);
            Assert.Equal("p", RepoPinStore.Load(repoRoot)?.PackageName);
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
