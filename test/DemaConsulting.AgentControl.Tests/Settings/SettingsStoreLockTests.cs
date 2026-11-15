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
///     Unit tests for how <see cref="SettingsStore"/> tolerates the settings file being briefly
///     locked by another process (a virus scanner, an indexer, a sync client) while it is replaced.
///     The retry is driven through the store's replace and delay seams so no test depends on real
///     timing, plus one test with a genuinely held file.
/// </summary>
public class SettingsStoreLockTests
{
    /// <summary>
    ///     The error code (as an exception HResult value) of a Win32 sharing violation (ERROR_SHARING_VIOLATION, 32).
    /// </summary>
    private const int SharingViolationHResult = unchecked((int)0x80070020);

    /// <summary>
    ///     The error code (as an exception HResult value) of a Win32 lock violation (ERROR_LOCK_VIOLATION, 33).
    /// </summary>
    private const int LockViolationHResult = unchecked((int)0x80070021);

    /// <summary>
    ///     The error code (as an exception HResult value) of a Win32 disk-full error (ERROR_DISK_FULL, 112).
    /// </summary>
    private const int DiskFullHResult = unchecked((int)0x80070070);

    /// <summary>
    ///     Test that a replace failing with a sharing or lock violation, or with access denied, a few
    ///     times and then succeeding saves the settings with no error, waiting with a growing back-off
    ///     between the attempts.
    /// </summary>
    /// <param name="kind">The kind of transient failure to simulate.</param>
    [Theory]
    [InlineData("sharing")]
    [InlineData("lock")]
    [InlineData("denied")]
    public void SettingsStore_Save_ReplaceBrieflyLocked_RetriesAndSaves(string kind)
    {
        // Arrange: the first three replace attempts fail
        var configDir = CreateTempDirectory();
        try
        {
            var attempts = 0;
            var delays = new List<TimeSpan>();
            void Replace(string source, string destination)
            {
                if (++attempts <= 3)
                {
                    throw CreateTransientFailure(kind);
                }

                File.Move(source, destination, overwrite: true);
            }

            // Act
            SettingsStore.Save(new AppSettings { GitExecutablePath = "retried" }, configDir, Replace, delays.Add);

            // Assert: saved on the fourth attempt, backing off 20, 40, 80 ms, nothing left behind
            Assert.Multiple(
                () => Assert.Equal(4, attempts),
                () => Assert.Equal(
                    [TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(80)],
                    delays),
                () => Assert.Equal("retried", SettingsStore.Load(configDir).GitExecutablePath),
                () => Assert.Equal(["settings.json"], Directory.GetFileSystemEntries(configDir).Select(Path.GetFileName)));
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a lock that never clears is given up on after a bounded number of attempts and a
    ///     bounded total wait, surfaces as the existing InvalidOperationException, leaves the earlier
    ///     settings file untouched and removes the temporary file.
    /// </summary>
    [Fact]
    public void SettingsStore_Save_ReplaceLockedBeyondRetries_ThrowsInvalidOperationAndCleansUp()
    {
        // Arrange: an existing file and a replace that always fails
        var configDir = CreateTempDirectory();
        try
        {
            SettingsStore.Save(new AppSettings { GitExecutablePath = "before" }, configDir);
            var attempts = 0;
            var delays = new List<TimeSpan>();
            void Replace(string source, string destination)
            {
                attempts++;
                throw CreateTransientFailure("sharing");
            }

            // Act
            var ex = Assert.Throws<InvalidOperationException>(
                () => SettingsStore.Save(new AppSettings { GitExecutablePath = "after" }, configDir, Replace, delays.Add));

            // Assert: bounded attempts and waits, the old content survives, no temporary file remains
            Assert.Multiple(
                () => Assert.Equal(SettingsStore.ReplaceMaxAttempts, attempts),
                () => Assert.Equal(SettingsStore.ReplaceMaxAttempts - 1, delays.Count),
                () => Assert.True(delays.All(delay => delay <= SettingsStore.ReplaceMaxDelay)),
                () => Assert.True(delays.Sum(delay => delay.TotalMilliseconds) <= 1000),
                () => Assert.IsType<IOException>(ex.InnerException),
                () => Assert.Equal("before", SettingsStore.Load(configDir).GitExecutablePath),
                () => Assert.Equal(["settings.json"], Directory.GetFileSystemEntries(configDir).Select(Path.GetFileName)));
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a failure that is not a lock (a full disk, a missing directory) is not retried: it
    ///     surfaces at once as InvalidOperationException with no waiting.
    /// </summary>
    /// <param name="kind">The kind of non-transient failure to simulate.</param>
    [Theory]
    [InlineData("disk-full")]
    [InlineData("directory-missing")]
    [InlineData("plain-io")]
    public void SettingsStore_Save_NonTransientReplaceFailure_IsNotRetried(string kind)
    {
        // Arrange
        var configDir = CreateTempDirectory();
        try
        {
            var attempts = 0;
            var delays = new List<TimeSpan>();
            void Replace(string source, string destination)
            {
                attempts++;
                throw kind switch
                {
                    "disk-full" => new IOException("There is not enough space on the disk.", DiskFullHResult),
                    "directory-missing" => new DirectoryNotFoundException("Could not find a part of the path."),
                    _ => new IOException("Some other failure."),
                };
            }

            // Act / Assert
            Assert.Throws<InvalidOperationException>(
                () => SettingsStore.Save(new AppSettings(), configDir, Replace, delays.Add));
            Assert.Multiple(
                () => Assert.Equal(1, attempts),
                () => Assert.Empty(delays),
                () => Assert.Empty(Directory.GetFileSystemEntries(configDir)));
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test with a really held file: another holder has the settings file open without delete
    ///     sharing (as a scanner or indexer can), releases it during the back-off, and the save still
    ///     succeeds with no error. The release happens inside the delay seam, so the test is
    ///     deterministic.
    /// </summary>
    [Fact]
    public void SettingsStore_Save_SettingsFileHeldOpenThenReleased_Saves()
    {
        // Arrange: an existing settings file held open for reading, without delete sharing
        var configDir = CreateTempDirectory();
        try
        {
            SettingsStore.Save(new AppSettings { GitExecutablePath = "before" }, configDir);
            var filePath = Path.Combine(configDir, "settings.json");
            FileStream? holder = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var delays = 0;
            void Delay(TimeSpan wait)
            {
                // Release the lock on the second wait: the third attempt then succeeds
                if (++delays == 2)
                {
                    holder?.Dispose();
                    holder = null;
                }
            }

            try
            {
                // Act
                SettingsStore.Save(new AppSettings { GitExecutablePath = "after" }, configDir, delay: Delay);
            }
            finally
            {
                holder?.Dispose();
            }

            // Assert: saved; on Windows the held file really forced two waits
            Assert.Equal("after", SettingsStore.Load(configDir).GitExecutablePath);
            if (OperatingSystem.IsWindows())
            {
                Assert.Equal(2, delays);
            }

            Assert.Equal(["settings.json"], Directory.GetFileSystemEntries(configDir).Select(Path.GetFileName));
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that the write-back of the seeded repo order made while loading tolerates a briefly
    ///     locked file too: after the lock clears, the order and its marker are on disk.
    /// </summary>
    [Fact]
    public void SettingsStore_Load_SeedWriteBrieflyLocked_RetriesAndPersistsSeededOrder()
    {
        // Arrange: an unmarked file, and a replace that fails twice before it works
        var configDir = CreateTempDirectory();
        var filePath = Path.Combine(configDir, "settings.json");
        try
        {
            File.WriteAllText(filePath, "{ \"RecentRepos\": [ { \"Path\": \"plain\" }, { \"Path\": \"fav\", \"IsFavorite\": true } ] }");
            var attempts = 0;
            var delays = new List<TimeSpan>();
            void Replace(string source, string destination)
            {
                if (++attempts <= 2)
                {
                    throw CreateTransientFailure("sharing");
                }

                File.Move(source, destination, overwrite: true);
            }

            // Act
            var loaded = SettingsStore.Load(configDir, Replace, delays.Add);

            // Assert: the marker reached the file despite the locks
            Assert.Multiple(
                () => Assert.Equal(3, attempts),
                () => Assert.Equal(2, delays.Count),
                () => Assert.Equal(["fav", "plain"], loaded.RecentRepos.Select(repo => repo.Path)),
                () => Assert.Contains("\"HasUserRepoOrder\": true", File.ReadAllText(filePath), StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    /// <summary>
    ///     Creates the exception a transient lock on the settings file produces.
    /// </summary>
    /// <param name="kind"><c>sharing</c>, <c>lock</c> or <c>denied</c>.</param>
    /// <returns>The exception to throw from a simulated replace.</returns>
    private static Exception CreateTransientFailure(string kind) => kind switch
    {
        "sharing" => new IOException("The process cannot access the file because it is being used by another process.", SharingViolationHResult),
        "lock" => new IOException("The process cannot access the file because another process has locked a portion of the file.", LockViolationHResult),
        _ => new UnauthorizedAccessException("Access to the path is denied."),
    };

    /// <summary>
    ///     Creates a unique temporary directory for test isolation.
    /// </summary>
    /// <returns>The created directory's path.</returns>
    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_settings_lock_test_" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        return path;
    }
}
