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

using System.Diagnostics;
using DemaConsulting.AgentControl.Puppet.Automation;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using FlaUiApplication = FlaUI.Core.Application;

namespace DemaConsulting.AgentControl.Puppet;

/// <summary>
///     A FlaUI attachment to one running AgentControl process and its main window.
/// </summary>
/// <remarks>
///     Every window title of AgentControl is shared by all its instances ("AgentControl", "AgentControl Settings", ...),
///     so everything here is scoped to the process id of the instance, never to a title.
/// </remarks>
internal sealed class AttachContext : IDisposable
{
    /// <summary>
    ///     The AutomationId of AgentControl's main window; with the title it tells the main window from the non-modal
    ///     windows (About, release notes) that are top-level windows of the same process.
    /// </summary>
    internal const string MainWindowId = "MainWindowRoot";

    /// <summary>
    ///     The title of AgentControl's main window.
    /// </summary>
    internal const string MainWindowTitle = "AgentControl";

    /// <summary>
    ///     The most windows <see cref="Roots"/> collects (a guard against a runaway tree).
    /// </summary>
    private const int MaxWindows = 64;

    /// <summary>
    ///     How long to wait for the main window to exist.
    /// </summary>
    private static readonly TimeSpan WindowAppearTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     The UI Automation session, disposed with the context.
    /// </summary>
    private readonly UIA3Automation _automation;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AttachContext"/> class.
    /// </summary>
    /// <param name="alias">The instance alias.</param>
    /// <param name="automation">The UI Automation session.</param>
    /// <param name="application">The attached application.</param>
    /// <param name="mainWindow">The main window.</param>
    private AttachContext(string alias, UIA3Automation automation, FlaUiApplication application, Window mainWindow)
    {
        Alias = alias;
        _automation = automation;
        Application = application;
        MainWindow = mainWindow;
    }

    /// <summary>
    ///     Gets the instance alias.
    /// </summary>
    internal string Alias { get; }

    /// <summary>
    ///     Gets the attached application.
    /// </summary>
    internal FlaUiApplication Application { get; }

    /// <summary>
    ///     Gets the main window.
    /// </summary>
    internal Window MainWindow { get; }

    /// <summary>
    ///     Gets the process id of the instance.
    /// </summary>
    internal int ProcessId => Application.ProcessId;

    /// <summary>
    ///     Gets the element that holds keyboard focus.
    /// </summary>
    /// <returns>The element, or <see langword="null"/> when none can be read.</returns>
    internal AutomationElement? TryGetFocusedElement()
    {
        try
        {
            return _automation.FocusedElement();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Gets the UI Automation root of the desktop.
    /// </summary>
    /// <returns>The desktop element.</returns>
    internal AutomationElement GetDesktop() => _automation.GetDesktop();

    /// <summary>
    ///     Gets every top-level window of the instance: the main window first, then dialogs and open pop-ups.
    /// </summary>
    /// <returns>The windows.</returns>
    internal IReadOnlyList<AutomationElement> Roots()
    {
        var roots = new List<AutomationElement> { MainWindow };
        var mainHandle = UiActions.Safe(() => MainWindow.Properties.NativeWindowHandle.ValueOrDefault, IntPtr.Zero);
        var others = UiActions.Safe(
            () => GetDesktop().FindAllChildren(cf => cf.ByProcessId(ProcessId)),
            []);
        var seen = new HashSet<IntPtr> { mainHandle };

        // UI Automation nests an owned window (a modal dialog, a tooltip pop-up) under its owner instead of listing it on the
        // desktop, and a dialog opened from a dialog under that dialog, so the owned windows are followed down every level
        var pending = new Queue<AutomationElement>(others.Prepend(MainWindow));
        while (pending.Count > 0)
        {
            var window = pending.Dequeue();
            if (!ReferenceEquals(window, MainWindow) && !AddRoot(roots, seen, window))
            {
                continue;
            }

            if (roots.Count > MaxWindows)
            {
                continue;
            }

            var owned = UiActions.Safe(
                () => window.FindAllChildren(cf => cf.ByControlType(ControlType.Window).Or(cf.ByControlType(ControlType.ToolTip))),
                []);
            foreach (var child in owned)
            {
                pending.Enqueue(child);
            }
        }

        return roots;
    }

    /// <summary>
    ///     Adds a window to the roots unless one with the same handle is there already.
    /// </summary>
    /// <param name="roots">The roots found so far.</param>
    /// <param name="seen">The handles of the roots found so far.</param>
    /// <param name="window">The window.</param>
    /// <returns><see langword="true"/> when the window was added.</returns>
    private static bool AddRoot(List<AutomationElement> roots, HashSet<IntPtr> seen, AutomationElement window)
    {
        var handle = UiActions.Safe(() => window.Properties.NativeWindowHandle.ValueOrDefault, IntPtr.Zero);
        if (handle != IntPtr.Zero && !seen.Add(handle))
        {
            return false;
        }

        roots.Add(window);
        return true;
    }


    /// <summary>
    ///     Attaches to a process.
    /// </summary>
    /// <param name="alias">The instance alias.</param>
    /// <param name="process">The process.</param>
    /// <param name="error">Why attaching failed when the result is <see langword="null"/>.</param>
    /// <returns>The context, or <see langword="null"/> on failure.</returns>
    internal static AttachContext? TryAttach(string alias, Process process, out string error)
    {
        var automation = new UIA3Automation();
        var attached = false;
        try
        {
            var app = FlaUiApplication.Attach(process);

            // A non-modal window (About) is a top-level window of its own and the process's "main window" can be any of
            // them, so the main window is picked by its identity first
            var window = FindMainWindow(app, automation) ?? app.GetMainWindow(automation, WindowAppearTimeout);
            if (window is null)
            {
                error = $"Instance '{alias}' (pid={process.Id}) has no main window yet.";
                return null;
            }

            error = string.Empty;
            var context = new AttachContext(alias, automation, app, window);
            attached = true;
            return context;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            error = ex.Message;
            return null;
        }
        finally
        {
            // The session is owned by the context on success; on every other path (no window, an exception) it is released exactly once here
            if (!attached)
            {
                automation.Dispose();
            }
        }
    }

    /// <summary>
    ///     Finds the main window among the top-level windows of the process.
    /// </summary>
    /// <param name="app">The attached application.</param>
    /// <param name="automation">The UI Automation session.</param>
    /// <returns>The main window, or <see langword="null"/> when none of the windows is recognizable as it.</returns>
    private static Window? FindMainWindow(FlaUiApplication app, UIA3Automation automation)
    {
        var windows = UiActions.Safe(() => app.GetAllTopLevelWindows(automation), []);
        return windows.FirstOrDefault(w => UiActions.Safe(() => w.AutomationId, string.Empty) == MainWindowId)
            ?? windows.FirstOrDefault(w => string.Equals(UiActions.Safe(() => w.Title, string.Empty), MainWindowTitle, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Releases the UI Automation session.
    /// </summary>
    public void Dispose()
    {
        _automation.Dispose();
    }
}
