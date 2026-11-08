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

namespace DemaConsulting.AgentControl.ArgLoggerStub;

/// <summary>
///     An opt-in emulation of a Git credential helper for end-to-end tests: when the
///     <c>ARGLOGGER_CREDENTIAL_STORE</c> environment variable names a file, the stub answers the
///     <c>git config --get-urlmatch credential.helper</c> and <c>git credential fill|approve|reject</c>
///     invocations the way git with a file-backed helper would, so a test can show that a sign-in is
///     kept - and found, and removed - only by "the user's helper" and never by AgentControl itself.
/// </summary>
/// <remarks>
///     Without the environment variable the stub behaves exactly as before (every invocation succeeds
///     and prints nothing, which AgentControl reads as "no credential helper"). The store is a plain
///     text file with one tab-separated entry per line: the origin (<c>protocol://host</c>), the user
///     name and the password; it exists only inside the test's temporary folder.
/// </remarks>
internal static class CredentialHelperEmulator
{
    /// <summary>
    ///     Name of the environment variable naming the emulated helper's store file.
    /// </summary>
    public const string StoreEnvironmentVariable = "ARGLOGGER_CREDENTIAL_STORE";

    /// <summary>
    ///     Answers a git invocation as an emulated credential helper would.
    /// </summary>
    /// <param name="args">The arguments the stub was invoked with.</param>
    /// <param name="storeFile">The path of the store file.</param>
    /// <returns>The exit code: <c>0</c> for everything except a <c>fill</c> that finds nothing,
    ///     which fails as git does when it may not prompt.</returns>
    public static int Run(IReadOnlyList<string> args, string storeFile)
    {
        // The helper question: any non-empty answer means "a helper applies"
        if (args.Contains("--get-urlmatch"))
        {
            Console.Out.Write("emulated-helper\n");
            return 0;
        }

        var credentialIndex = IndexOf(args, "credential");
        if (credentialIndex < 0 || credentialIndex + 1 >= args.Count)
        {
            return 0;
        }

        var request = ReadRequest(Console.In);
        var origin = $"{request.GetValueOrDefault("protocol")}://{request.GetValueOrDefault("host")}";
        var entries = ReadEntries(storeFile);
        switch (args[credentialIndex + 1])
        {
            case "fill":
                var found = entries.FirstOrDefault(entry => entry[0] == origin);
                if (found is null)
                {
                    return 1;
                }

                Console.Out.Write(
                    $"protocol={request.GetValueOrDefault("protocol")}\nhost={request.GetValueOrDefault("host")}\n" +
                    $"username={found[1]}\npassword={found[2]}\n");
                return 0;

            case "approve":
                entries.RemoveAll(entry => entry[0] == origin);
                entries.Add([origin, request.GetValueOrDefault("username") ?? string.Empty, request.GetValueOrDefault("password") ?? string.Empty]);
                WriteEntries(storeFile, entries);
                return 0;

            case "reject":
                entries.RemoveAll(entry => entry[0] == origin);
                WriteEntries(storeFile, entries);
                return 0;

            default:
                return 0;
        }
    }

    /// <summary>
    ///     Finds an argument.
    /// </summary>
    /// <param name="args">The arguments.</param>
    /// <param name="value">The argument to find.</param>
    /// <returns>The index of the argument, or <c>-1</c>.</returns>
    private static int IndexOf(IReadOnlyList<string> args, string value)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == value)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    ///     Reads the <c>key=value</c> lines of a credential request up to the blank line.
    /// </summary>
    /// <param name="input">The standard input.</param>
    /// <returns>The keys and values.</returns>
    private static Dictionary<string, string> ReadRequest(TextReader input)
    {
        var request = new Dictionary<string, string>(StringComparer.Ordinal);
        while (input.ReadLine() is { Length: > 0 } line)
        {
            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator > 0)
            {
                request[line[..separator]] = line[(separator + 1)..];
            }
        }

        return request;
    }

    /// <summary>
    ///     Reads the store file.
    /// </summary>
    /// <param name="storeFile">The path of the store file.</param>
    /// <returns>The entries, each as origin, user name and password.</returns>
    private static List<string[]> ReadEntries(string storeFile) =>
        File.Exists(storeFile)
            ? [.. File.ReadAllLines(storeFile).Select(line => line.Split('\t')).Where(parts => parts.Length == 3)]
            : [];

    /// <summary>
    ///     Writes the store file.
    /// </summary>
    /// <param name="storeFile">The path of the store file.</param>
    /// <param name="entries">The entries to keep.</param>
    private static void WriteEntries(string storeFile, List<string[]> entries) =>
        File.WriteAllLines(storeFile, entries.Select(entry => string.Join('\t', entry)));
}
