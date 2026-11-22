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

using System.Text.Json;
using DemaConsulting.AgentControl.Logging;
using DemaConsulting.AgentControl.Utilities;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.AgentControl.Settings;

/// <summary>
///     Reads and writes <see cref="AppSettings"/> as JSON under a configurable directory,
///     defaulting to <c>%APPDATA%\AgentControl\</c>.
/// </summary>
/// <remarks>
///     The configuration directory is always caller-supplied rather than read from process-wide
///     state, so that <c>StartupOptions.ConfigDirectory</c> (the FlaUI test-isolation override
///     described in architecture.md) can redirect settings I/O to a temporary folder without any
///     global/static mutable state. Stateless and thread-safe: every member is a pure function of
///     its parameters plus the filesystem.
/// </remarks>
internal static class SettingsStore
{
    /// <summary>
    ///     File name used for the persisted settings JSON within the configuration directory.
    /// </summary>
    private const string SettingsFileName = "settings.json";

    /// <summary>
    ///     Category of the logger used to report a repo order that could not be saved.
    /// </summary>
    private const string LoggerCategoryName = "DemaConsulting.AgentControl.Settings.SettingsStore";

    /// <summary>
    ///     Total number of attempts (the first try plus the retries) made to move the temporary file
    ///     over the settings file before a lock is treated as permanent.
    /// </summary>
    /// <remarks>
    ///     Another process (an antivirus scanner, the Windows Search indexer, a cloud-sync client or a
    ///     backup tool) can hold the settings file open without delete sharing for a moment, and that
    ///     makes the replace fail. Such a hold normally lasts well under a second, so a handful of
    ///     attempts covers it while a permanent failure still surfaces quickly.
    /// </remarks>
    internal const int ReplaceMaxAttempts = 8;

    /// <summary>
    ///     Wait before the first retry of the replace; each further wait doubles, up to
    ///     <see cref="ReplaceMaxDelay"/>.
    /// </summary>
    internal static readonly TimeSpan ReplaceInitialDelay = TimeSpan.FromMilliseconds(20);

    /// <summary>
    ///     Longest single wait between two replace attempts.
    /// </summary>
    /// <remarks>
    ///     With <see cref="ReplaceMaxAttempts"/> and <see cref="ReplaceInitialDelay"/> the waits are
    ///     20, 40, 80, 160, 200, 200 and 200 ms: at most 900 ms in total, so a lock that never clears
    ///     still surfaces as a failure within about a second. The window's saves run in the background
    ///     (see <see cref="SettingsWriter"/>), so the waits never freeze it.
    /// </remarks>
    internal static readonly TimeSpan ReplaceMaxDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>
    ///     Win32 error code for a sharing violation (the file is open in another process without
    ///     the sharing the operation needs).
    /// </summary>
    private const int ErrorSharingViolation = 32;

    /// <summary>
    ///     Win32 error code for a lock violation (part of the file is locked by another process).
    /// </summary>
    private const int ErrorLockViolation = 33;

    /// <summary>
    ///     JSON serialization options shared by <see cref="Load"/> and <see cref="Save"/>.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    ///     Gets the default configuration directory, <c>%APPDATA%\AgentControl\</c> on Windows
    ///     (the platform-appropriate equivalent elsewhere), per architecture.md's data-storage
    ///     decision.
    /// </summary>
    /// <returns>The default configuration directory path.</returns>
    public static string GetDefaultConfigDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "AgentControl");
    }

    /// <summary>
    ///     Loads <see cref="AppSettings"/> from the given configuration directory.
    /// </summary>
    /// <param name="configDirectory">Configuration directory to read from. Pass
    ///     <see langword="null"/> to use <see cref="GetDefaultConfigDirectory"/>.</param>
    /// <returns>
    ///     The persisted settings, or a fresh default <see cref="AppSettings"/> instance if no
    ///     settings file exists yet (first run) — this is expected, not an error. A legacy
    ///     <c>PackageSourcePath</c> in the file is migrated into <c>Sources</c> as the
    ///     <c>Default</c> zip source when no sources are configured. A file without the repo-order
    ///     marker has its repos arranged once as favorites first, then the most recently launched,
    ///     and is marked as holding the user's order; that arrangement and the marker are written
    ///     back to the file at once, so it happens only once (a failed write is logged, never fails
    ///     the load, and is retried on the next load). Explicit JSON <c>null</c>
    ///     values for the source list, the recent-repos list or a source's package IDs are
    ///     normalized to empty collections.
    /// </returns>
    /// <param name="replaceFile">Test seam: moves a temporary file over the settings file when the
    ///     load writes the seeded repo order back; <see langword="null"/> for the real move.</param>
    /// <param name="delay">Test seam: waits between replace attempts; <see langword="null"/> to
    ///     really sleep.</param>
    /// <exception cref="InvalidOperationException">Thrown when the settings file exists but
    ///     cannot be read or contains invalid JSON.</exception>
    public static AppSettings Load(
        string? configDirectory = null,
        Action<string, string>? replaceFile = null,
        Action<TimeSpan>? delay = null)
    {
        var directory = configDirectory ?? GetDefaultConfigDirectory();
        var filePath = Path.Combine(directory, SettingsFileName);

        // A missing settings file means first run; return defaults rather than treating it as
        // an error condition.
        if (!File.Exists(filePath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();

            // An explicit JSON null bypasses the collection initializers; restore empty values
            // before anything (including migration) touches them
            settings.NormalizeNulls();

            // Carry a legacy single package-folder setting forward as the "Default" source
            settings.MigrateLegacySource();

            // A file written before the repo order was stored holds the repos in the order they
            // were added; arrange them once the way older versions displayed them
            var seedsRepoOrder = !settings.HasUserRepoOrder;
            settings.EnsureRepoOrder();

            // Persist the one-time arrangement and its marker now, so the next load keeps this order
            // instead of seeding again from launch times that have changed since
            if (seedsRepoOrder)
            {
                PersistSeededRepoOrder(settings, directory, replaceFile, delay);
            }

            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new InvalidOperationException($"Failed to load settings from '{filePath}': {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Writes the freshly seeded repo order and its marker back to the settings file.
    /// </summary>
    /// <param name="settings">The settings that were just loaded and seeded.</param>
    /// <param name="directory">The configuration directory the settings were loaded from.</param>
    /// <param name="replaceFile">Test seam for the file replace, passed on to <see cref="Save"/>.</param>
    /// <param name="delay">Test seam for the wait between replace attempts, passed on to
    ///     <see cref="Save"/>.</param>
    /// <remarks>
    ///     A failed write never fails the load: it is logged and the in-memory order is still used. The
    ///     file then stays unmarked, so the arrangement is attempted and persisted again on the next load.
    ///     The write tolerates a briefly locked file exactly like every other save.
    /// </remarks>
    private static void PersistSeededRepoOrder(
        AppSettings settings, string directory, Action<string, string>? replaceFile, Action<TimeSpan>? delay)
    {
        try
        {
            // The load itself runs before the window exists (see Load); its one-time write is part of it
            SaveCore(settings, directory, replaceFile, delay);
        }
        catch (InvalidOperationException ex)
        {
            AppLogging.Factory.CreateLogger(LoggerCategoryName).LogWarning(
                ex, "The seeded repo order could not be saved; it will be saved again on the next load");
        }
    }

    /// <summary>
    ///     Saves <see cref="AppSettings"/> to the given configuration directory, creating the
    ///     directory if it does not already exist.
    /// </summary>
    /// <param name="settings">The settings to persist. Must not be <see langword="null"/>.</param>
    /// <param name="configDirectory">Configuration directory to write to. Pass
    ///     <see langword="null"/> to use <see cref="GetDefaultConfigDirectory"/>.</param>
    /// <remarks>
    ///     Writes the effective package sources in <c>Sources</c> and never the legacy
    ///     <c>PackageSourcePath</c>. The file is replaced atomically (written to a temporary file in
    ///     the same directory, then moved over the settings file) so a crash or full disk never
    ///     leaves a truncated settings file. The move is retried a bounded number of times
    ///     (<see cref="ReplaceMaxAttempts"/>, backing off from <see cref="ReplaceInitialDelay"/> to
    ///     <see cref="ReplaceMaxDelay"/>) when another process briefly holds the file open, so a
    ///     virus scanner or indexer does not cause a spurious failure. The persisted model has no
    ///     field that can hold a password, token or other secret, so none can be written. It writes
    ///     the disk and can wait for a locked file, so it must not run on the UI thread: it first asks
    ///     <see cref="BlockingCallGuard.ThrowIfGuarded"/>, which refuses the call on a thread a test has
    ///     guarded. The window saves through <see cref="SettingsWriter"/>, on a worker thread.
    /// </remarks>
    /// <param name="replaceFile">Test seam: moves the temporary file (first argument) over the
    ///     settings file (second argument), replacing it; <see langword="null"/> for the real move.</param>
    /// <param name="delay">Test seam: waits between replace attempts; <see langword="null"/> to
    ///     really sleep.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a source location embeds a
    ///     credential (nothing is written), the configuration directory cannot be created, or the
    ///     settings file cannot be written.</exception>
    /// <exception cref="BlockingCallOnGuardedThreadException">Thrown when called on a thread a test
    ///     has guarded (see <see cref="BlockingCallGuard"/>); nothing is written.</exception>
    public static void Save(
        AppSettings settings,
        string? configDirectory = null,
        Action<string, string>? replaceFile = null,
        Action<TimeSpan>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        BlockingCallGuard.ThrowIfGuarded("settings save");
        SaveCore(settings, configDirectory, replaceFile, delay);
    }

    /// <summary>
    ///     The body of <see cref="Save"/>, without the blocking-call guard: also used by the one-time
    ///     write of <see cref="Load"/>, which runs before the window exists.
    /// </summary>
    /// <param name="settings">The settings to persist.</param>
    /// <param name="configDirectory">Configuration directory to write to, or <see langword="null"/>
    ///     for the default.</param>
    /// <param name="replaceFile">Test seam for the file replace, or <see langword="null"/>.</param>
    /// <param name="delay">Test seam for the wait between replace attempts, or
    ///     <see langword="null"/>.</param>
    /// <exception cref="InvalidOperationException">Thrown as documented on <see cref="Save"/>.</exception>
    private static void SaveCore(
        AppSettings settings,
        string? configDirectory,
        Action<string, string>? replaceFile,
        Action<TimeSpan>? delay)
    {

        // Refuse before anything is written: a source location that embeds a credential must
        // never reach the settings file
        foreach (var source in settings.GetEffectiveSources())
        {
            var problem = source.GetCredentialProblem();
            if (problem is not null)
            {
                throw new InvalidOperationException($"Settings were not saved. {problem}");
            }
        }

        var directory = configDirectory ?? GetDefaultConfigDirectory();
        string? temporaryPath = null;

        try
        {
            Directory.CreateDirectory(directory);
            var filePath = Path.Combine(directory, SettingsFileName);
            var json = JsonSerializer.Serialize(settings.CreatePersistedCopy(), JsonOptions);

            // Write beside the target, then move over it, so readers never see a partial file
            temporaryPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporaryPath, json);
            ReplaceWithRetry(
                temporaryPath,
                filePath,
                replaceFile ?? ((source, destination) => File.Move(source, destination, overwrite: true)),
                delay ?? Thread.Sleep);
            temporaryPath = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                   or PathTooLongException or NotSupportedException or DirectoryNotFoundException)
        {
            // Directory.CreateDirectory/Path.Combine surface path-validation failures (invalid
            // characters, an empty/whitespace path, an unreachable drive, etc.) as several
            // distinct exception types - normalize every one of them to InvalidOperationException
            // so callers only ever need to catch the single documented exception type.
            throw new InvalidOperationException($"Failed to save settings to '{directory}': {ex.Message}", ex);
        }
        finally
        {
            // Only reached with a value after a failure between the write and the move
            if (temporaryPath is not null && File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    /// <summary>
    ///     Moves the temporary file over the settings file, retrying while the failure looks like a
    ///     transient lock held by another process.
    /// </summary>
    /// <param name="temporaryPath">The fully written temporary file.</param>
    /// <param name="filePath">The settings file to replace.</param>
    /// <param name="replaceFile">Moves the first path over the second, replacing it.</param>
    /// <param name="delay">Waits between attempts.</param>
    /// <remarks>
    ///     Only <see cref="IsTransientReplaceFailure"/> failures are retried, up to
    ///     <see cref="ReplaceMaxAttempts"/> attempts in all; any other failure, and the failure of the
    ///     last attempt, propagates unchanged to the caller's normal error handling.
    /// </remarks>
    private static void ReplaceWithRetry(
        string temporaryPath, string filePath, Action<string, string> replaceFile, Action<TimeSpan> delay)
    {
        var wait = ReplaceInitialDelay;
        var attempt = 1;
        while (true)
        {
            try
            {
                replaceFile(temporaryPath, filePath);
                return;
            }
            catch (Exception ex) when (attempt < ReplaceMaxAttempts && IsTransientReplaceFailure(ex))
            {
                delay(wait);
                wait = TimeSpan.FromTicks(Math.Min(wait.Ticks * 2, ReplaceMaxDelay.Ticks));
                attempt++;
            }
        }
    }

    /// <summary>
    ///     Determines whether a failed replace is the kind another process's brief hold on the file causes.
    /// </summary>
    /// <param name="ex">The exception the replace threw.</param>
    /// <returns>
    ///     <see langword="true"/> for an <see cref="UnauthorizedAccessException"/> (what Windows reports
    ///     when the destination is open elsewhere without delete sharing or is pending deletion) or an
    ///     <see cref="IOException"/> carrying the sharing-violation or lock-violation error;
    ///     <see langword="false"/> for everything else, such as a missing directory or a full disk.
    /// </returns>
    private static bool IsTransientReplaceFailure(Exception ex)
    {
        if (ex is UnauthorizedAccessException)
        {
            return true;
        }

        // The low 16 bits of an IOException HResult carry the Win32 error code
        return ex is IOException io
               && (io.HResult & 0xFFFF) is ErrorSharingViolation or ErrorLockViolation;
    }
}
