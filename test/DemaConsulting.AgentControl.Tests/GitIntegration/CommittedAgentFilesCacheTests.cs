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

using DemaConsulting.AgentControl.GitIntegration;

namespace DemaConsulting.AgentControl.Tests.GitIntegration;

/// <summary>
///     Unit tests for <see cref="CommittedAgentFilesCache"/>.
/// </summary>
/// <remarks>
///     This class takes the <c>HEAD</c> hash as a plain input, so no process spawning is involved
///     - these are pure cache-logic unit tests, distinct from <see cref="GitClientTests"/>.
/// </remarks>
public sealed class CommittedAgentFilesCacheTests
{
    /// <summary>
    ///     Test that repeated lookups for the same repo path and HEAD hash return the cached
    ///     value.
    /// </summary>
    [Fact]
    public void CommittedAgentFilesCache_SameRepoAndHash_ReturnsCachedValue()
    {
        // Arrange
        var cache = new CommittedAgentFilesCache();
        cache.Set(@"C:\repos\example", "abc123", true);

        // Act
        var found = cache.TryGetCached(@"C:\repos\example", "abc123", out var value);

        // Assert
        Assert.True(found);
        Assert.True(value);
    }

    /// <summary>
    ///     Test that a lookup for a different HEAD hash on the same repo path is a cache miss.
    /// </summary>
    [Fact]
    public void CommittedAgentFilesCache_DifferentHash_IsTreatedAsCacheMiss()
    {
        // Arrange
        var cache = new CommittedAgentFilesCache();
        cache.Set(@"C:\repos\example", "abc123", true);

        // Act
        var found = cache.TryGetCached(@"C:\repos\example", "def456", out var value);

        // Assert
        Assert.False(found);
        Assert.False(value);
    }

    /// <summary>
    ///     Test that a lookup for a repo path never recorded is a cache miss.
    /// </summary>
    [Fact]
    public void CommittedAgentFilesCache_UnknownRepoPath_IsTreatedAsCacheMiss()
    {
        // Arrange
        var cache = new CommittedAgentFilesCache();

        // Act
        var found = cache.TryGetCached(@"C:\repos\never-seen", "abc123", out _);

        // Assert
        Assert.False(found);
    }

    /// <summary>
    ///     Test that Invalidate clears a specific repo's cached entry, without affecting other
    ///     repos' entries.
    /// </summary>
    [Fact]
    public void CommittedAgentFilesCache_Invalidate_ClearsOnlyThatRepoPath()
    {
        // Arrange
        var cache = new CommittedAgentFilesCache();
        cache.Set(@"C:\repos\example", "abc123", true);
        cache.Set(@"C:\repos\other", "xyz789", false);

        // Act
        cache.Invalidate(@"C:\repos\example");

        // Assert: the invalidated repo is now a miss, the other repo's entry survives
        Assert.False(cache.TryGetCached(@"C:\repos\example", "abc123", out _));
        Assert.True(cache.TryGetCached(@"C:\repos\other", "xyz789", out var otherValue));
        Assert.False(otherValue);
    }

    /// <summary>
    ///     Test that Set overwrites a previously-cached value for the same key.
    /// </summary>
    [Fact]
    public void CommittedAgentFilesCache_SetSameKeyTwice_OverwritesPreviousValue()
    {
        // Arrange
        var cache = new CommittedAgentFilesCache();
        cache.Set(@"C:\repos\example", "abc123", false);

        // Act
        cache.Set(@"C:\repos\example", "abc123", true);

        // Assert
        Assert.True(cache.TryGetCached(@"C:\repos\example", "abc123", out var value));
        Assert.True(value);
    }
}
