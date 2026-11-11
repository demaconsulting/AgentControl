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
///     Subsystem tests for RepoConfig: a repo's pin used the way the rest of the application uses it -
///     saved by one session and loaded by a later one through the subsystem's public surface
///     (<see cref="RepoPinStore"/> with <see cref="RepoPin"/> and <see cref="UnsupportedPinSchemaException"/>)
///     against a real temporary repo folder - with attention to the content identity a pin records.
/// </summary>
public class RepoConfigSubsystemTests
{
    /// <summary>
    ///     Test that the content identity of the applied package (computed from its files) survives from one
    ///     session to the next and is never invented: it round-trips, a pin that has none writes none, a pin
    ///     written before identities existed loads with none, a schema 3 pin's identity of a container or of a
    ///     Git commit is dropped, and each such pin adopts the identity of the next package applied.
    /// </summary>
    [Fact]
    public void RepoConfig_ContentIdentityPin_SurvivesSessionsMigratesOlderPinsAndIsAdoptedOnTheNextApply()
    {
        // Arrange: one repo per kind of pin, and pin files as earlier versions wrote them
        var filesRepo = CreateTempDirectory();
        var noneRepo = CreateTempDirectory();
        var oldRepo = CreateTempDirectory();
        var containerRepo = CreateTempDirectory();
        var gitV3Repo = CreateTempDirectory();
        try
        {
            var filesIdentity = "files-sha256:" + new string('a', 64);
            var gitCommit = "git-commit:" + new string('b', 40);
            var adopted = "files-sha256:" + new string('c', 64);
            var oldPinPath = Path.Combine(oldRepo, ".agentcontrol.json");
            File.WriteAllText(
                oldPinPath,
                """{ "SchemaVersion": 2, "Source": "Feed", "PackageName": "p", "Version": "1.0.0", "AgentsMdTemplateDeclined": true }""");
            File.WriteAllText(
                Path.Combine(containerRepo, ".agentcontrol.json"),
                $$"""{ "SchemaVersion": 3, "Source": "Zips", "PackageName": "p", "Version": "1.0.0", "ContentIdentity": "sha256:{{new string('e', 64)}}" }""");
            File.WriteAllText(
                Path.Combine(gitV3Repo, ".agentcontrol.json"),
                $$"""{ "SchemaVersion": 3, "Source": "Repo", "PackageName": "p", "Version": "1.0.0", "ContentIdentity": "{{gitCommit}}" }""");

            // Act: one session saves the pins, a later session loads them
            RepoPinStore.Save(
                filesRepo,
                new RepoPin { PackageName = "p", Version = "1.0.0", ContentIdentity = filesIdentity });
            RepoPinStore.Save(noneRepo, new RepoPin { PackageName = "p", Version = "1.0.0" });
            var filesPin = RepoPinStore.Load(filesRepo);
            var nonePin = RepoPinStore.Load(noneRepo);
            var oldPin = RepoPinStore.Load(oldRepo);
            var containerPin = RepoPinStore.Load(containerRepo);
            var gitV3Pin = RepoPinStore.Load(gitV3Repo);

            // Assert: the recorded value came back, nothing was invented, and older identities are not
            // carried over as files identities
            Assert.Multiple(
                () => Assert.Equal(filesIdentity, filesPin?.ContentIdentity),
                () => Assert.Null(nonePin?.ContentIdentity),
                () => Assert.DoesNotContain(
                    "ContentIdentity",
                    File.ReadAllText(Path.Combine(noneRepo, ".agentcontrol.json")),
                    StringComparison.OrdinalIgnoreCase),
                () => Assert.Null(oldPin?.ContentIdentity),
                () => Assert.Null(containerPin?.ContentIdentity),
                () => Assert.Null(gitV3Pin?.ContentIdentity));

            // Act: the next package applied to each older pin's repo records its identity
            oldPin!.ContentIdentity = adopted;
            RepoPinStore.Save(oldRepo, oldPin);
            var adoptedPin = RepoPinStore.Load(oldRepo);
            var rewritten = File.ReadAllText(oldPinPath);
            containerPin!.ContentIdentity = adopted;
            RepoPinStore.Save(containerRepo, containerPin);

            // Assert: the identity was adopted, the file is now the current schema and the rest of the pin is kept
            Assert.Multiple(
                () => Assert.Equal(adopted, adoptedPin?.ContentIdentity),
                () => Assert.Contains($"\"SchemaVersion\": {RepoPin.CurrentSchemaVersion}", rewritten, StringComparison.Ordinal),
                () => Assert.Equal("1.0.0", adoptedPin?.Version),
                () => Assert.True(adoptedPin?.AgentsMdTemplateDeclined),
                () => Assert.Equal(adopted, RepoPinStore.Load(containerRepo)?.ContentIdentity));
        }
        finally
        {
            foreach (var repo in new[] { filesRepo, noneRepo, oldRepo, containerRepo, gitV3Repo })
            {
                Directory.Delete(repo, recursive: true);
            }
        }
    }

    /// <summary>
    ///     Test that a pin written by a newer version of the application is never interpreted or
    ///     overwritten by this one, even when it holds the properties this version knows in another form:
    ///     loading says which versions are supported and what to do, saving changes nothing, and a pin of
    ///     the schema this version writes is read normally afterwards.
    /// </summary>
    [Fact]
    public void RepoConfig_NewerSchemaPin_IsRefusedWithAdviceAndNeverOverwritten()
    {
        // Arrange: a pin file of a newer schema whose identity is an object, not text
        var repoRoot = CreateTempDirectory();
        try
        {
            var path = Path.Combine(repoRoot, ".agentcontrol.json");
            var newerPin = $$"""{ "SchemaVersion": {{RepoPin.CurrentSchemaVersion + 1}}, "Source": "Feed", "PackageName": "p", "Version": "1.0.0", "ContentIdentity": { "algorithm": "sha512", "value": "x" } }""";
            File.WriteAllText(path, newerPin);

            // Act: this version tries to read it, and then to save a pin over it
            var loadFailure = Assert.Throws<UnsupportedPinSchemaException>(() => RepoPinStore.Load(repoRoot));
            var saveFailure = Assert.Throws<UnsupportedPinSchemaException>(
                () => RepoPinStore.Save(
                    repoRoot,
                    new RepoPin { PackageName = "p", Version = "2.0.0", ContentIdentity = "files-sha256:" + new string('d', 64) }));

            // Assert: the refusals name the versions and say to update, and the file is untouched
            Assert.Multiple(
                () => Assert.Equal(RepoPin.CurrentSchemaVersion + 1, loadFailure.SchemaVersion),
                () => Assert.Contains($"up to {RepoPin.CurrentSchemaVersion}", loadFailure.Message, StringComparison.Ordinal),
                () => Assert.Contains("Update AgentControl", loadFailure.Message, StringComparison.Ordinal),
                () => Assert.Equal(loadFailure.SchemaVersion, saveFailure.SchemaVersion),
                () => Assert.Equal(newerPin, File.ReadAllText(path)),
                () => Assert.Equal([path], Directory.GetFiles(repoRoot)));

            // Act: a pin of the schema this version writes replaces it (the user's deliberate action)
            File.Delete(path);
            RepoPinStore.Save(repoRoot, new RepoPin { PackageName = "p", Version = "2.0.0" });

            // Assert: it is read normally
            Assert.Equal("2.0.0", RepoPinStore.Load(repoRoot)?.Version);
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a pin names no package source: a pin file an earlier version wrote with a source (and
    ///     a Git revision) is read with the source as a hint only, and whatever the application saves - over
    ///     that file or fresh - carries neither, so one user's source settings never reach the shared file.
    /// </summary>
    [Fact]
    public void RepoConfig_PinNamesNoSource_ReadsLegacySourceAsAHintAndNeverWritesIt()
    {
        // Arrange: a repo whose pin an earlier build wrote with a source and a source revision, and a new repo
        var legacyRepo = CreateTempDirectory();
        var freshRepo = CreateTempDirectory();
        try
        {
            var legacyPath = Path.Combine(legacyRepo, ".agentcontrol.json");
            File.WriteAllText(
                legacyPath,
                $$"""{ "SchemaVersion": 4, "Source": "Feed", "PackageName": "p", "Version": "1.0.0", "ContentIdentity": "files-sha256:{{new string('a', 64)}}", "SourceRevision": "git-commit:{{new string('b', 40)}}" }""");

            // Act: load the legacy pin and save it back, and save a brand-new pin elsewhere
            var legacyPin = RepoPinStore.Load(legacyRepo);
            RepoPinStore.Save(legacyRepo, legacyPin!);
            RepoPinStore.Save(freshRepo, new RepoPin { PackageName = "p", Version = "2.0.0" });
            var legacyJson = File.ReadAllText(legacyPath);
            var freshJson = File.ReadAllText(Path.Combine(freshRepo, ".agentcontrol.json"));

            // Assert: the source was a hint, and neither file has a source or a revision
            Assert.Multiple(
                () => Assert.Equal("Feed", legacyPin?.LegacySource),
                () => Assert.DoesNotContain("source", legacyJson, StringComparison.OrdinalIgnoreCase),
                () => Assert.DoesNotContain("source", freshJson, StringComparison.OrdinalIgnoreCase),
                () => Assert.Contains("\"ContentIdentity\"", legacyJson, StringComparison.Ordinal),
                () => Assert.Null(RepoPinStore.Load(freshRepo)?.LegacySource));
        }
        finally
        {
            Directory.Delete(legacyRepo, recursive: true);
            Directory.Delete(freshRepo, recursive: true);
        }
    }

    /// <summary>
    ///     Creates a unique, empty temporary repo folder.
    /// </summary>
    /// <returns>The path of the new folder.</returns>
    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "AgentControlRepoConfigSubsystemTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
