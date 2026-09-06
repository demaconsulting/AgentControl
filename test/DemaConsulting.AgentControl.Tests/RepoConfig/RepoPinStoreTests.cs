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
