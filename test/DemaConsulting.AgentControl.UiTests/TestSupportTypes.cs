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

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     Writes a repo's <c>.agentcontrol.json</c> pin file directly, without taking a compile-time
///     dependency on the (internal) <c>RepoPin</c>/<c>RepoPinStore</c> types.
/// </summary>
internal static class TestRepoPinWriter
{
    /// <summary>
    ///     Writes <c>.agentcontrol.json</c> at the root of <paramref name="repoPath"/>.
    /// </summary>
    /// <param name="repoPath">Absolute path to the repository root; must already exist.</param>
    /// <param name="packageName">The pinned package name.</param>
    /// <param name="version">The pinned package version.</param>
    public static void Write(string repoPath, string packageName, string version)
    {
        var pin = new { PackageName = packageName, Version = version };
        var json = JsonSerializer.Serialize(pin, new JsonSerializerOptions { WriteIndented = true });
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
