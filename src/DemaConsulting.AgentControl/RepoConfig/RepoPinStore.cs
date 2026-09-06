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
using DemaConsulting.AgentControl.Utilities;

namespace DemaConsulting.AgentControl.RepoConfig;

/// <summary>
///     Reads and writes the per-repo <c>.agentcontrol.json</c> pin file at a repo's root.
/// </summary>
/// <remarks>
///     Stateless and thread-safe: every member is a pure function of its parameters plus the
///     filesystem. Uses <see cref="PathHelpers.SafePathCombine"/> to construct the pin file path
///     so the repo root cannot be escaped via a malformed root path.
/// </remarks>
internal static class RepoPinStore
{
    /// <summary>
    ///     File name of the per-repo pin file, always located directly at the repo root.
    /// </summary>
    private const string PinFileName = ".agentcontrol.json";

    /// <summary>
    ///     JSON serialization options shared by <see cref="Load"/> and <see cref="Save"/>.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    ///     Loads the <see cref="RepoPin"/> for the repo at <paramref name="repoRoot"/>, or
    ///     <see langword="null"/> if the repo has no pin file yet (never synced).
    /// </summary>
    /// <param name="repoRoot">Absolute path to the repository root.</param>
    /// <returns>The parsed pin, or <see langword="null"/> when no pin file exists.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="repoRoot"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the pin file exists but cannot be
    ///     read or contains invalid JSON.</exception>
    public static RepoPin? Load(string repoRoot)
    {
        ArgumentNullException.ThrowIfNull(repoRoot);

        var filePath = PathHelpers.SafePathCombine(repoRoot, PinFileName);
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<RepoPin>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new InvalidOperationException($"Failed to load pin file '{filePath}': {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Writes the <see cref="RepoPin"/> for the repo at <paramref name="repoRoot"/>, creating
    ///     or overwriting the <c>.agentcontrol.json</c> file.
    /// </summary>
    /// <param name="repoRoot">Absolute path to the repository root. Must already exist.</param>
    /// <param name="pin">The pin to persist.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="repoRoot"/> or
    ///     <paramref name="pin"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the pin file cannot be written.</exception>
    public static void Save(string repoRoot, RepoPin pin)
    {
        ArgumentNullException.ThrowIfNull(repoRoot);
        ArgumentNullException.ThrowIfNull(pin);

        var filePath = PathHelpers.SafePathCombine(repoRoot, PinFileName);

        try
        {
            var json = JsonSerializer.Serialize(pin, JsonOptions);
            File.WriteAllText(filePath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save pin file '{filePath}': {ex.Message}", ex);
        }
    }
}
