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
    ///     Test that the content identity of the applied package, in either of the forms the application
    ///     records, survives from one session to the next and is never invented: a zip or NuGet identity
    ///     and a Git commit identity round-trip, a pin that has none writes none, and a pin written before
    ///     identities existed loads with none and adopts the identity of the next package applied.
    /// </summary>
    [Fact]
    public void RepoConfig_ContentIdentityPin_SurvivesSessionsIsNeverInventedAndIsAdoptedOnTheNextApply()
    {
        // Arrange: one repo per kind of identity, and a pin file as an earlier version wrote it
        var zipRepo = CreateTempDirectory();
        var gitRepo = CreateTempDirectory();
        var noneRepo = CreateTempDirectory();
        var oldRepo = CreateTempDirectory();
        try
        {
            var sha256 = "sha256:" + new string('a', 64);
            var gitCommit = "git-commit:" + new string('b', 40);
            var adopted = "sha256:" + new string('c', 64);
            var oldPinPath = Path.Combine(oldRepo, ".agentcontrol.json");
            File.WriteAllText(
                oldPinPath,
                """{ "SchemaVersion": 2, "Source": "Feed", "PackageName": "p", "Version": "1.0.0", "AgentsMdTemplateDeclined": true }""");

            // Act: one session saves each pin, a later session loads them
            RepoPinStore.Save(zipRepo, new RepoPin { Source = "Zips", PackageName = "p", Version = "1.0.0", ContentIdentity = sha256 });
            RepoPinStore.Save(gitRepo, new RepoPin { Source = "Repo", PackageName = "p", Version = "1.0.0", ContentIdentity = gitCommit });
            RepoPinStore.Save(noneRepo, new RepoPin { Source = "Feed", PackageName = "p", Version = "1.0.0" });
            var zipPin = RepoPinStore.Load(zipRepo);
            var gitPin = RepoPinStore.Load(gitRepo);
            var nonePin = RepoPinStore.Load(noneRepo);
            var oldPin = RepoPinStore.Load(oldRepo);

            // Assert: each identity came back as recorded, the pin without one has none and none is in its file,
            // and the old pin has no identity either
            Assert.Multiple(
                () => Assert.Equal(sha256, zipPin?.ContentIdentity),
                () => Assert.Equal(gitCommit, gitPin?.ContentIdentity),
                () => Assert.Null(nonePin?.ContentIdentity),
                () => Assert.DoesNotContain(
                    "ContentIdentity",
                    File.ReadAllText(Path.Combine(noneRepo, ".agentcontrol.json")),
                    StringComparison.OrdinalIgnoreCase),
                () => Assert.Null(oldPin?.ContentIdentity));

            // Act: the next package applied to the old pin's repo records its identity
            oldPin!.ContentIdentity = adopted;
            RepoPinStore.Save(oldRepo, oldPin);
            var adoptedPin = RepoPinStore.Load(oldRepo);
            var rewritten = File.ReadAllText(oldPinPath);

            // Assert: the identity was adopted, the file is now the current schema and the rest of the pin is kept
            Assert.Multiple(
                () => Assert.Equal(adopted, adoptedPin?.ContentIdentity),
                () => Assert.Contains($"\"SchemaVersion\": {RepoPin.CurrentSchemaVersion}", rewritten, StringComparison.Ordinal),
                () => Assert.Equal("Feed", adoptedPin?.Source),
                () => Assert.Equal("1.0.0", adoptedPin?.Version),
                () => Assert.True(adoptedPin?.AgentsMdTemplateDeclined));
        }
        finally
        {
            foreach (var repo in new[] { zipRepo, gitRepo, noneRepo, oldRepo })
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
                    new RepoPin { Source = "Feed", PackageName = "p", Version = "2.0.0", ContentIdentity = "sha256:" + new string('d', 64) }));

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
            RepoPinStore.Save(repoRoot, new RepoPin { Source = "Feed", PackageName = "p", Version = "2.0.0" });

            // Assert: it is read normally
            Assert.Equal("2.0.0", RepoPinStore.Load(repoRoot)?.Version);
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
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
