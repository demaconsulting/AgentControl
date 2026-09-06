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

using System.Reflection;
using Avalonia;
using DemaConsulting.AgentControl.Logging;
using DemaConsulting.AgentControl.Startup;
using Microsoft.Extensions.Logging;
using Serilog;

namespace DemaConsulting.AgentControl;

/// <summary>
///     Main program entry point for Agent Control.
/// </summary>
/// <remarks>
///     Parses the fixed set of startup arguments (config-dir override, and test-only
///     git/agent-tool command overrides) via <see cref="StartupOptions"/>, attaches them to
///     <see cref="App.Options"/>, and hands off to the Avalonia classic desktop lifetime. The
///     options are attached to a static property rather than passed as a constructor argument
///     because Avalonia's <see cref="BuildAvaloniaApp"/> pattern requires <see cref="App"/> to
///     have a parameterless constructor so the framework's designer/previewer tooling can also
///     instantiate it.
/// </remarks>
internal static class Program
{
    /// <summary>
    ///     Gets the application version string.
    /// </summary>
    /// <remarks>
    ///     The version is read from the <see cref="AssemblyInformationalVersionAttribute"/> via
    ///     reflection on every access. There is no caching; callers that need the value more than
    ///     once should store the result locally.
    /// </remarks>
    public static string Version
    {
        get
        {
            // Get the assembly containing this program
            var assembly = typeof(Program).Assembly;

            // Try to get version from assembly attributes, fallback to AssemblyVersion, or default to 0.0.0
            return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? assembly.GetName().Version?.ToString()
                   ?? "0.0.0";
        }
    }

    /// <summary>
    ///     Main entry point for Agent Control.
    /// </summary>
    /// <param name="args">Command-line arguments; see <see cref="StartupOptions.Parse"/> for the
    ///     supported set.</param>
    /// <returns>Exit code: 0 for success, non-zero for failure.</returns>
    /// <remarks>
    ///     <see cref="ArgumentException"/> raised while parsing startup arguments is treated as an
    ///     expected error: its message is written to stderr and exit code 1 is returned without a
    ///     stack trace. Logging is initialized immediately after argument parsing - before the
    ///     Avalonia app builder runs - specifically so startup-time failures are also captured on
    ///     disk, per the diagnostic-capture goal described in
    ///     <c>.agent-logs/implementation-agentcontrol-v1-final-3e91c7.md</c>. On success,
    ///     <see cref="App.Options"/> is set and the Avalonia classic desktop lifetime runs the UI
    ///     for the remainder of the process's life; the returned exit code reflects the parsing
    ///     step only, since the desktop lifetime itself does not surface a separate exit code. Any
    ///     unhandled exception escaping the desktop lifetime is logged at Critical before being
    ///     rethrown, and the Serilog pipeline is always flushed on the way out so no buffered log
    ///     entries are lost. Marked <see cref="STAThreadAttribute"/> because Windows OLE
    ///     clipboard operations (cut/copy/paste in any <c>TextBox</c>) require the UI thread to
    ///     run in a single-threaded apartment; without this attribute the thread defaults to MTA,
    ///     clipboard <c>Set</c>/<c>Get</c> calls fail silently, and editing controls appear to
    ///     have a broken clipboard (copy does nothing, cut leaves the text in place).
    /// </remarks>
    [STAThread]
    public static int Main(string[] args)
    {
        StartupOptions options;
        try
        {
            // Parse the fixed set of startup arguments so App can load settings from the
            // (possibly overridden) configuration directory before showing the UI.
            options = StartupOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            // Print expected argument exceptions and return error code
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }

        // Initialize logging as early as possible - before the Avalonia app builder runs - using
        // the same configuration-directory override as settings so test runs stay isolated.
        LoggingSetup.Initialize(options.ConfigDirectory);
        var logger = AppLogging.Factory.CreateLogger(typeof(Program));

        try
        {
            logger.LogInformation("Starting AgentControl {Version}", Version);

            // Attach the parsed options for App.OnFrameworkInitializationCompleted to consume,
            // then hand off to Avalonia's classic desktop lifetime for the remainder of the
            // process.
            App.Options = options;
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }
        catch (Exception ex)
        {
            // Log the full detail of any unhandled startup/runtime exception before rethrowing,
            // since this is the last point the app-specific Serilog pipeline is guaranteed to
            // still be flushable. Wrapped (rather than a bare `throw;`) so the rethrown exception
            // carries explicit contextual information about where it was caught, satisfying
            // static-analysis guidance against silently rethrowing an already-logged exception
            // unchanged.
            logger.LogCritical(ex, "Unhandled exception during application startup or execution");
            throw new InvalidOperationException("Unhandled exception during application startup or execution.", ex);
        }
        finally
        {
            // Ensure buffered log entries are written to disk regardless of how Main exits.
            Log.CloseAndFlush();
        }
    }

    /// <summary>
    ///     Configures the Avalonia <see cref="AppBuilder"/> used to run <see cref="App"/>.
    /// </summary>
    /// <returns>A configured, not-yet-started <see cref="AppBuilder"/>.</returns>
    /// <remarks>
    ///     Kept as a separate, parameterless static method (rather than inlined into
    ///     <see cref="Main"/>) following Avalonia's standard template convention: this allows
    ///     the same builder configuration to be reused by design-time tooling and by
    ///     out-of-process previewers, neither of which call <see cref="Main"/> directly.
    /// </remarks>
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
