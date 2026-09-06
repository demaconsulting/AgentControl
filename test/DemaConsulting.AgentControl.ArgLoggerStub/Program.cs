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

namespace DemaConsulting.AgentControl.ArgLoggerStub;

/// <summary>
///     A minimal cross-platform "arg logger" stub executable used only by
///     <c>DemaConsulting.AgentControl.UiTests</c>: records its own invocation (working directory
///     plus full argument list) as a single JSON line appended to a log file, then exits
///     successfully without doing any real work.
/// </summary>
/// <remarks>
///     Per architecture.md's "Testability via config-dir override and stub executables"
///     decision, this executable is substituted for the real <c>git</c> executable and the
///     configured agentic CLI tool command in FlaUI end-to-end tests, so those tests can assert
///     on what AgentControl actually invoked (and in which working directory) without depending
///     on git or a real agent CLI tool being installed on the test/CI machine.
///     <para>
///     The output file path is taken from the <c>ARGLOGGER_OUTPUT_FILE</c> environment variable
///     rather than a command-line argument convention, because AgentControl always launches this
///     stub with test-controlled arguments (e.g. <c>status --porcelain</c>, <c>pull</c>, or none
///     at all for the agent-tool case) that must be preserved verbatim in the log for assertions
///     - inventing a "first argument is the output path" convention would require the test
///     harness to inject an extra argument that real git/agent-tool invocations never receive,
///     complicating the git-path-override and agent-tool-command-override wiring in
///     <c>GitClient</c>/<c>RepoCardViewModel</c>. The environment variable is instead set once on
///     the AgentControl process the test launches; every child process (git subprocess calls, and
///     the shell process that runs the configured agent-tool command) inherits it automatically,
///     so a single shared log file captures every invocation across the whole test.
///     </para>
/// </remarks>
internal static class Program
{
    /// <summary>
    ///     Name of the environment variable naming the file this stub appends its invocation
    ///     record to.
    /// </summary>
    private const string OutputFileEnvironmentVariable = "ARGLOGGER_OUTPUT_FILE";

    /// <summary>
    ///     Serialization options used for each appended invocation record.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    /// <summary>
    ///     Entry point: records this invocation and exits successfully.
    /// </summary>
    /// <param name="args">The command-line arguments this stub was invoked with.</param>
    /// <returns>Always <c>0</c> - per this stub's contract, it never fails, since a spurious
    ///     non-zero exit code would make real production code paths (e.g. <c>GitClient</c>
    ///     treating a non-zero exit as a git failure) behave differently under test than in
    ///     production for reasons unrelated to what is being tested.</returns>
    public static int Main(string[] args)
    {
        var outputFile = Environment.GetEnvironmentVariable(OutputFileEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(outputFile))
        {
            // No output file configured: nothing to record, but still succeed - a misconfigured
            // test harness should fail its own assertions (missing log entries), not this stub.
            return 0;
        }

        var record = new InvocationRecord(
            Environment.CurrentDirectory,
            args,
            DateTimeOffset.UtcNow);

        try
        {
            var line = JsonSerializer.Serialize(record, JsonOptions);

            // Use a named mutex to serialize appends across concurrent stub invocations (e.g. a
            // git status check racing a git pull), since File.AppendAllText alone is not
            // guaranteed atomic across separate processes.
            using var mutex = new Mutex(false, "Global\\DemaConsulting.AgentControl.ArgLoggerStub");
            mutex.WaitOne();
            try
            {
                File.AppendAllText(outputFile, line + Environment.NewLine);
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }
        catch (IOException)
        {
            // Best-effort logging only; a transient file-write failure must not cause this stub
            // to report a non-zero exit code (see the return-value remarks above).
        }
        catch (UnauthorizedAccessException)
        {
            // See the IOException remarks above.
        }

        return 0;
    }
}

/// <summary>
///     A single recorded invocation of the arg-logger stub.
/// </summary>
/// <param name="WorkingDirectory">The stub's current working directory at invocation time.</param>
/// <param name="Arguments">The full command-line argument list the stub was invoked with.</param>
/// <param name="TimestampUtc">The UTC time the invocation was recorded.</param>
internal sealed record InvocationRecord(string WorkingDirectory, IReadOnlyList<string> Arguments, DateTimeOffset TimestampUtc);
