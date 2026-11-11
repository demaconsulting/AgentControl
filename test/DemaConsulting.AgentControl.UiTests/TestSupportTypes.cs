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

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     Computes the content identity of agent files the way the application documents it, independently
///     of the application's own code, so end-to-end tests can state what a pin must record.
/// </summary>
/// <remarks>
///     The identity is <c>files-sha256:</c> followed by the SHA-256 of a byte stream built from the agent
///     files (those in the four managed folders under <c>.github</c>) in ordinal order of their
///     <c>/</c>-separated package-relative path: for each file the UTF-8 path, a zero byte, the file length
///     as eight little-endian bytes, then the file's bytes.
/// </remarks>
internal static class TestFilesIdentity
{
    /// <summary>
    ///     The package-relative folders whose files take part in the identity.
    /// </summary>
    private static readonly string[] ManagedFolders =
        [".github/agents/", ".github/standards/", ".github/templates/", ".github/skills/"];

    /// <summary>
    ///     Computes the identity of the agent files among the given files.
    /// </summary>
    /// <param name="files">Package-relative path and content of files; files outside the managed
    ///     folders are ignored.</param>
    /// <returns>The identity text.</returns>
    public static string Compute(IEnumerable<(string Path, byte[] Content)> files)
    {
        using var stream = new MemoryStream();
        foreach (var (path, content) in files
                     .Where(file => ManagedFolders.Any(folder => file.Path.StartsWith(folder, StringComparison.Ordinal)))
                     .OrderBy(file => file.Path, StringComparer.Ordinal))
        {
            stream.Write(Encoding.UTF8.GetBytes(path));
            stream.WriteByte(0);
            stream.Write(BitConverter.GetBytes((long)content.Length));
            stream.Write(content);
        }

        return "files-sha256:" + Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    /// <summary>
    ///     Computes the identity of agent files given as text.
    /// </summary>
    /// <param name="files">Package-relative path and UTF-8 text (no byte-order mark) of each file.</param>
    /// <returns>The identity text.</returns>
    public static string OfText(params (string Path, string Text)[] files) =>
        Compute(files.Select(file => (file.Path, Encoding.UTF8.GetBytes(file.Text))));

    /// <summary>
    ///     Computes the identity of the agent files inside a zip or <c>.nupkg</c> file.
    /// </summary>
    /// <param name="archivePath">The archive.</param>
    /// <param name="rootPrefix">The folder of the archive that is the package content root (empty for
    ///     a zip package, <c>content/</c> for a NuGet package).</param>
    /// <returns>The identity text.</returns>
    public static string OfArchive(string archivePath, string rootPrefix = "")
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var files = new List<(string Path, byte[] Content)>();
        foreach (var entry in archive.Entries.Where(
                     entry => entry.Name.Length > 0 && entry.FullName.StartsWith(rootPrefix, StringComparison.Ordinal)))
        {
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            files.Add((entry.FullName[rootPrefix.Length..], buffer.ToArray()));
        }

        return Compute(files);
    }
}

/// <summary>
///     Writes a repo's <c>.agentcontrol.json</c> pin file directly, without taking a compile-time
///     dependency on the (internal) <c>RepoPin</c>/<c>RepoPinStore</c> types.
/// </summary>
internal static class TestRepoPinWriter
{
    /// <summary>
    ///     Shared options instance for the JSON serialization performed by this writer, avoiding
    ///     a fresh <see cref="JsonSerializerOptions"/> allocation on every call.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    /// <summary>
    ///     Writes <c>.agentcontrol.json</c> at the root of <paramref name="repoPath"/>. A pin names no
    ///     package source (sources are the user's own configuration): it records the package, the
    ///     version and, when given, the identity of the agent files.
    /// </summary>
    /// <param name="repoPath">Absolute path to the repository root; must already exist.</param>
    /// <param name="packageName">The pinned package name.</param>
    /// <param name="version">The pinned package version.</param>
    /// <param name="contentIdentity">The content identity to record, or <see langword="null"/> for a
    ///     pin that records none (written in the oldest format, which the application treats as a pin
    ///     with nothing to verify a source against); a pin that records one is written as the current
    ///     schema version.</param>
    public static void Write(
        string repoPath,
        string packageName,
        string version,
        string? contentIdentity = null)
    {
        var pin = new Dictionary<string, object?> { ["PackageName"] = packageName, ["Version"] = version };
        if (contentIdentity is not null)
        {
            pin["SchemaVersion"] = 4;
            pin["ContentIdentity"] = contentIdentity;
        }

        var json = JsonSerializer.Serialize(pin, SerializerOptions);
        File.WriteAllText(Path.Combine(repoPath, ".agentcontrol.json"), json);
    }
}

/// <summary>
///     One recorded invocation of the arg-logger stub, mirroring
///     <c>DemaConsulting.AgentControl.ArgLoggerStub.InvocationRecord</c>'s JSON shape without
///     taking a compile-time dependency on that (internal) type.
/// </summary>
internal sealed record ArgLoggerInvocation(string WorkingDirectory, IReadOnlyList<string> Arguments, DateTimeOffset TimestampUtc);

/// <summary>
///     Reads and polls the JSON-lines log file the arg-logger stub appends invocation records to.
/// </summary>
internal static class ArgLoggerLog
{
    /// <summary>
    ///     Reads every invocation currently recorded in <paramref name="logFilePath"/>.
    /// </summary>
    /// <param name="logFilePath">Path to the arg-logger stub's output file.</param>
    /// <returns>The recorded invocations, in the order they were appended; empty if the file does
    ///     not exist yet (no invocation has happened yet).</returns>
    public static IReadOnlyList<ArgLoggerInvocation> ReadAll(string logFilePath)
    {
        if (!File.Exists(logFilePath))
        {
            return [];
        }

        var records = new List<ArgLoggerInvocation>();

        // Retry the read a handful of times: the stub process may still be flushing its write
        // while this test is polling, which can otherwise surface as a transient sharing
        // violation on Windows.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using var stream = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    var record = JsonSerializer.Deserialize<ArgLoggerInvocation>(line);
                    if (record is not null)
                    {
                        records.Add(record);
                    }
                }

                return records;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(100);
                records.Clear();
            }
        }

        return records;
    }

    /// <summary>
    ///     Polls <paramref name="logFilePath"/> until a recorded invocation satisfies
    ///     <paramref name="predicate"/>, or the timeout elapses.
    /// </summary>
    /// <param name="logFilePath">Path to the arg-logger stub's output file.</param>
    /// <param name="predicate">Condition an invocation must satisfy.</param>
    /// <param name="timeout">Maximum time to wait.</param>
    /// <returns>The first matching invocation, or <see langword="null"/> if the timeout elapsed
    ///     without a match.</returns>
    public static ArgLoggerInvocation? WaitForInvocation(
        string logFilePath,
        Func<ArgLoggerInvocation, bool> predicate,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var match = ReadAll(logFilePath).FirstOrDefault(predicate);
            if (match is not null)
            {
                return match;
            }

            Thread.Sleep(100);
        }

        return ReadAll(logFilePath).FirstOrDefault(predicate);
    }
}
