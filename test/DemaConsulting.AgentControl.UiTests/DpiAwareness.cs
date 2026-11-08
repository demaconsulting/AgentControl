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

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     Makes the test process aware of the display scaling, so the screen positions UI Automation
///     reports, the pixels a screenshot captures and the points the mouse is moved to are all in the
///     same physical pixels.
/// </summary>
/// <remarks>
///     A process that is not DPI aware sees a scaled-down desktop, and on a display scaled to more than
///     100% the positions it reads from the (DPI aware) application no longer match the screen, so a
///     test that looks at pixels near an element would look in the wrong place. The setting has to be
///     made before the process creates a window or reads any screen position, hence a module
///     initializer. It does not change the application under test, which runs in its own process.
/// </remarks>
internal static class DpiAwareness
{
    /// <summary>
    ///     The <c>DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2</c> pseudo-handle.
    /// </summary>
    private static readonly IntPtr PerMonitorAwareV2 = new(-4);

    /// <summary>
    ///     Win32 <c>SetProcessDpiAwarenessContext</c>: sets the DPI awareness of the current process.
    /// </summary>
    /// <param name="value">The awareness context to set.</param>
    /// <returns><see langword="true"/> when the awareness was set; <see langword="false"/> when the
    ///     process already has one.</returns>
    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    /// <summary>
    ///     Makes the process per-monitor DPI aware as soon as the test assembly loads; an awareness the
    ///     host already chose is left as it is.
    /// </summary>
    /// <remarks>
    ///     <c>SetProcessDpiAwarenessContext</c> exists only on Windows 10 version 1703 (build 15063) and
    ///     later. On older Windows the call is skipped (and a missing entry point is tolerated), because
    ///     an exception thrown from a module initializer would abort the whole test host; the tests then
    ///     run DPI-unaware, which only matters on displays scaled above 100%.
    /// </remarks>
    [ModuleInitializer]
    internal static void Initialize()
    {
        if (!System.OperatingSystem.IsWindowsVersionAtLeast(10, 0, 15063))
        {
            return;
        }

        try
        {
            _ = SetProcessDpiAwarenessContext(PerMonitorAwareV2);
        }
        catch (EntryPointNotFoundException)
        {
            // Older user32.dll without the API: keep the default awareness rather than abort the host
        }
    }
}
