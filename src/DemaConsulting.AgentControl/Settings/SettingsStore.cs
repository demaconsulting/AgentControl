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
    ///     settings file exists yet (first run) — this is expected, not an error.
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown when the settings file exists but
    ///     cannot be read or contains invalid JSON.</exception>
    public static AppSettings Load(string? configDirectory = null)
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
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new InvalidOperationException($"Failed to load settings from '{filePath}': {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Saves <see cref="AppSettings"/> to the given configuration directory, creating the
    ///     directory if it does not already exist.
    /// </summary>
    /// <param name="settings">The settings to persist. Must not be <see langword="null"/>.</param>
    /// <param name="configDirectory">Configuration directory to write to. Pass
    ///     <see langword="null"/> to use <see cref="GetDefaultConfigDirectory"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the configuration directory
    ///     cannot be created or the settings file cannot be written.</exception>
    public static void Save(AppSettings settings, string? configDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = configDirectory ?? GetDefaultConfigDirectory();

        try
        {
            Directory.CreateDirectory(directory);
            var filePath = Path.Combine(directory, SettingsFileName);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(filePath, json);
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
    }
}
