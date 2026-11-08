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

using System.ComponentModel;
using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     Owns one hermetic, isolated launch of <c>AgentControl.exe</c> under FlaUI for a single
///     test: an isolated temp config directory, an isolated temp repo directory, a pre-seeded
///     <c>settings.json</c> pointing git/agent-tool commands at the arg-logger stub, and the
///     FlaUI <see cref="Application"/>/<see cref="UIA3Automation"/> instances needed to drive it.
/// </summary>
/// <remarks>
///     Implements <see cref="IDisposable"/> so every test can use a <c>using</c> block (or a
///     <c>try</c>/<c>finally</c>) to guarantee the spawned AgentControl process, any shell process
///     it in turn launches for the agent-tool command (see <c>AgentToolLauncher</c>'s
///     <c>-NoExit</c>/<c>/K</c> "leave the window open" behavior, which this context must
///     clean up since the shell is not a child FlaUI tracks), and every temp directory are
///     cleaned up even if the test fails an assertion or throws. Shell cleanup is strictly
///     scoped to this context's own launch (see <see cref="LaunchedShellCleaner"/>): unrelated
///     shells and terminal tabs are never touched.
/// </remarks>
internal sealed class AgentControlTestContext : IDisposable
{
    private readonly LaunchedShellCleaner _shellCleaner;
    private int? _appProcessId;
    private DateTime _appStartTime;
    private bool _disposed;

    /// <summary>
    ///     Initializes a new isolated test context: creates temp directories, writes
    ///     <c>settings.json</c>, and prepares the scoped shell cleaner.
    /// </summary>
    /// <param name="packageSourcePath">The package source directory to configure, or
    ///     <see langword="null"/> to leave it unconfigured.</param>
    /// <param name="pinnedPackageName">The pinned package name to seed for
    ///     <see cref="RepoPath"/>, or <see langword="null"/> for a never-synced repo.</param>
    /// <param name="pinnedPackageVersion">The pinned package version to seed for
    ///     <see cref="RepoPath"/> (also written as the repo's actual <c>.agentcontrol.json</c>
    ///     pin file when <paramref name="pinnedPackageName"/> is supplied, since
    ///     <c>RepoCardViewModel.Refresh()</c> always re-reads the pin from disk), or
    ///     <see langword="null"/>.</param>
    /// <param name="nuGetFeedPath">A NuGet package folder to configure as the additional
    ///     <c>Feed</c> source, or <see langword="null"/> for none.</param>
    /// <param name="nuGetPackageIds">The package IDs offered by the <c>Feed</c> source.</param>
    /// <param name="pinnedSource">The source alias to record in the repo's pin file, or
    ///     <see langword="null"/> to write a pin that names no source.</param>
    /// <param name="nuGetRequireSigned">Whether the <c>Feed</c> source requires signed
    ///     packages.</param>
    /// <param name="nuGetServerIndexUrl">The service-index address of a NuGet package server to
    ///     configure as the additional <c>Server</c> source, or <see langword="null"/> for none.</param>
    /// <param name="customizeSettings">Edits the settings dictionary just before it is written, with
    ///     this context (whose <see cref="RootDirectory"/> and <see cref="RepoPath"/> are set), or
    ///     <see langword="null"/> for none; used to seed groups, collapsed groups and more repos.</param>
    public AgentControlTestContext(
        string? packageSourcePath = null,
        string? pinnedPackageName = null,
        string? pinnedPackageVersion = null,
        string? nuGetFeedPath = null,
        IReadOnlyList<string>? nuGetPackageIds = null,
        string? pinnedSource = null,
        bool nuGetRequireSigned = false,
        string? nuGetServerIndexUrl = null,
        Action<Dictionary<string, object?>, AgentControlTestContext>? customizeSettings = null)
    {
        var root = Directory.CreateTempSubdirectory("AgentControlUiTests-");
        RootDirectory = root.FullName;
        ConfigDirectory = Path.Combine(RootDirectory, "config");
        RepoPath = Path.Combine(RootDirectory, "repo");
        ArgLoggerOutputFile = Path.Combine(RootDirectory, "arglogger.jsonl");

        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(RepoPath);

        var argLoggerStubExe = TestPaths.ResolveArgLoggerStubExe();
        TestSettingsWriter.Write(
            ConfigDirectory,
            argLoggerStubExe,
            packageSourcePath,
            RepoPath,
            pinnedPackageName,
            pinnedPackageVersion,
            nuGetFeedPath,
            nuGetPackageIds,
            nuGetRequireSigned,
            nuGetServerIndexUrl,
            customizeSettings is null ? null : settings => customizeSettings(settings, this));

        if (pinnedPackageName is not null && pinnedPackageVersion is not null)
        {
            TestRepoPinWriter.Write(RepoPath, pinnedPackageName, pinnedPackageVersion, pinnedSource);
        }

        _shellCleaner = new LaunchedShellCleaner(argLoggerStubExe, LogCleanup);
    }

    /// <summary>
    ///     Gets the temp root directory containing this context's config/repo/log paths.
    /// </summary>
    public string RootDirectory { get; }

    /// <summary>
    ///     Gets the isolated <c>--config-dir</c> this context launches AgentControl against.
    /// </summary>
    public string ConfigDirectory { get; }

    /// <summary>
    ///     Gets the isolated repo directory pre-seeded into the recent-repos list.
    /// </summary>
    public string RepoPath { get; }

    /// <summary>
    ///     Gets the file the arg-logger stub appends its invocation records to.
    /// </summary>
    public string ArgLoggerOutputFile { get; }

    /// <summary>
    ///     Gets or sets the file backing the arg-logger stub's emulated Git credential helper, or
    ///     <see langword="null"/> (the default) for a stub that reports no helper. Set before
    ///     <see cref="Launch"/>; a pre-existing file acts as sign-ins saved in an earlier session.
    /// </summary>
    public string? CredentialStoreFile { get; set; }

    /// <summary>
    ///     Gets the launched FlaUI <see cref="Application"/>, or <see langword="null"/> before
    ///     <see cref="Launch"/> is called.
    /// </summary>
    public Application? App { get; private set; }

    /// <summary>
    ///     Gets the <see cref="UIA3Automation"/> instance used to find elements, or
    ///     <see langword="null"/> before <see cref="Launch"/> is called.
    /// </summary>
    public UIA3Automation? Automation { get; private set; }

    /// <summary>
    ///     Launches AgentControl.exe against this context's isolated config directory and waits
    ///     for its main window to appear.
    /// </summary>
    /// <returns>The main window, ready to be driven by FlaUI.</returns>
    /// <exception cref="TimeoutException">Thrown when the main window does not appear within a
    ///     reasonable time.</exception>
    public Window Launch()
    {
        var exePath = TestPaths.ResolveAgentControlExe();

        var startInfo = new ProcessStartInfo(exePath)
        {
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("--config-dir");
        startInfo.ArgumentList.Add(ConfigDirectory);
        startInfo.EnvironmentVariables["ARGLOGGER_OUTPUT_FILE"] = ArgLoggerOutputFile;
        if (CredentialStoreFile is not null)
        {
            // Opt in to the arg-logger stub's emulation of the user's Git credential helper
            startInfo.EnvironmentVariables["ARGLOGGER_CREDENTIAL_STORE"] = CredentialStoreFile;
        }

        // NuGet caches service indexes on disk by URL; a private cache per launch keeps a feed that
        // a test starts on a reused port from being answered with a previous run's responses
        startInfo.EnvironmentVariables["NUGET_HTTP_CACHE_PATH"] = Path.Combine(RootDirectory, "nuget-http-cache");

        // Record which terminal windows already exist so only windows created by this launch
        // can ever be considered for cleanup.
        Automation = new UIA3Automation();
        _shellCleaner.CaptureBaseline(Automation);

        App = Application.Launch(startInfo);
        _appProcessId = App.ProcessId;
        using (var appProcess = Process.GetProcessById(App.ProcessId))
        {
            _appStartTime = appProcess.StartTime;
        }

        var mainWindow = Retry.WhileNull(
            () => App.GetMainWindow(Automation),
            timeout: TimeSpan.FromSeconds(20),
            interval: TimeSpan.FromMilliseconds(200),
            ignoreException: true).Result;

        return mainWindow ?? throw new TimeoutException(
            "AgentControl's main window did not appear within the timeout.");
    }

    /// <summary>
    ///     Finds a currently-open top-level window (other than the main window) whose title
    ///     matches <paramref name="titlePredicate"/>, polling until found or the timeout elapses.
    /// </summary>
    /// <param name="titlePredicate">Predicate matched against each top-level window's title.</param>
    /// <param name="timeout">Maximum time to wait.</param>
    /// <returns>The matching window, or <see langword="null"/> if none appeared in time.</returns>
    public Window? WaitForWindow(Func<string, bool> titlePredicate, TimeSpan timeout)
    {
        if (App is null || Automation is null)
        {
            return null;
        }

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var match = App.GetAllTopLevelWindows(Automation)
                .FirstOrDefault(window => titlePredicate(window.Title ?? string.Empty));
            if (match is not null)
            {
                return match;
            }

            Thread.Sleep(200);
        }

        return null;
    }

    /// <summary>
    ///     Finds a currently-open top-level window that contains an element with the given
    ///     AutomationId, polling until found or the timeout elapses. Searching by a descendant
    ///     rather than by title is reliable for modal dialogs, whose title is not always a
    ///     dependable match target through UI Automation.
    /// </summary>
    /// <param name="automationId">The AutomationId of an element the wanted window contains.</param>
    /// <param name="timeout">Maximum time to wait.</param>
    /// <returns>The matching window, or <see langword="null"/> if none appeared in time.</returns>
    public Window? WaitForWindowWithDescendant(string automationId, TimeSpan timeout)
    {
        if (App is null || Automation is null)
        {
            return null;
        }

        return Retry.WhileNull(
            () => App.GetAllTopLevelWindows(Automation)
                .FirstOrDefault(window => window.FindFirstDescendant(cf => cf.ByAutomationId(automationId)) is not null),
            timeout: timeout,
            interval: TimeSpan.FromMilliseconds(200)).Result;
    }

    /// <summary>
    ///     Opens the Settings window's Add menu and invokes one of its items, the way a user picks
    ///     a source type: the menu items only exist while the menu is open, so the Add button is
    ///     invoked first and the item is then looked up in the opened menu.
    /// </summary>
    /// <param name="settingsWindow">The open Settings window.</param>
    /// <param name="menuItemAutomationId">The AutomationId of the menu item to invoke, for example
    ///     <c>AddZipSourceButton</c>.</param>
    /// <exception cref="InvalidOperationException">Thrown when the Add button or the menu item does
    ///     not appear in time.</exception>
    public static void InvokeAddSourceMenuItem(AutomationElement settingsWindow, string menuItemAutomationId)
    {
        var addButton = Retry.WhileNull(
            () => settingsWindow.FindFirstDescendant(cf => cf.ByAutomationId("AddSourceButton")),
            timeout: TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(200)).Result?.AsButton()
            ?? throw new InvalidOperationException("The Add button was not found in the Settings window.");
        addButton.Invoke();

        var menuItem = Retry.WhileNull(
            () => settingsWindow.FindFirstDescendant(cf => cf.ByAutomationId(menuItemAutomationId)),
            timeout: TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(200)).Result?.AsMenuItem()
            ?? throw new InvalidOperationException($"The Add menu item '{menuItemAutomationId}' did not appear.");
        menuItem.Invoke();
    }

    /// <summary>
    ///     Closes/kills the launched AgentControl process, disposes the automation instance,
    ///     terminates only the shell (and stub) spawned by this context's agent-tool launch,
    ///     closes the terminal window that launch left behind, and best-effort deletes this
    ///     context's temp directory tree.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            App?.Close();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Best-effort: the process may have already exited.
        }

        try
        {
            App?.Kill();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Best-effort: the process may have already exited.
        }

        App?.Dispose();
        Automation?.Dispose();

        // Only after the app is gone: terminate exactly the shell (and stub) it launched and
        // close the terminal window left behind - see LaunchedShellCleaner for the rationale.
        if (_appProcessId is { } appProcessId)
        {
            _shellCleaner.Cleanup(appProcessId, _appStartTime);
        }

        try
        {
            Directory.Delete(RootDirectory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup only; a locked temp file must not fail the test itself.
        }
    }

    /// <summary>
    ///     Forwards a cleanup diagnostic to the xUnit test output (visible with diagnostic
    ///     messages enabled), never failing the test if no test context is available.
    /// </summary>
    /// <param name="message">The diagnostic message.</param>
    private static void LogCleanup(string message)
    {
        try
        {
            TestContext.Current.SendDiagnosticMessage(message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException)
        {
            // Diagnostics are a convenience only; swallow if the xUnit context is unavailable.
        }
    }
}
