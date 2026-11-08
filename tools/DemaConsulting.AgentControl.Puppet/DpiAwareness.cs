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

using System.Runtime.InteropServices;

namespace DemaConsulting.AgentControl.Puppet;

/// <summary>
///     Makes the Puppet process DPI aware, so the pixel coordinates it reads and injects are physical screen pixels.
/// </summary>
/// <remarks>
///     UI Automation reports rectangles in physical pixels and Windows scales the coordinates of a DPI-unaware process
///     (UI Automation rectangles, the cursor position), so without this the mouse would land in the wrong place on a
///     scaled monitor.
/// </remarks>
internal static partial class DpiAwareness
{
    /// <summary>
    ///     The per-monitor-aware-v2 context handle (<c>DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2</c>).
    /// </summary>
    private static readonly IntPtr PerMonitorAwareV2 = new(-4);

    /// <summary>
    ///     Opts the process in to per-monitor DPI awareness, falling back to system awareness on old Windows.
    /// </summary>
    internal static void Enable()
    {
        try
        {
            if (!SetProcessDpiAwarenessContext(PerMonitorAwareV2))
            {
                SetProcessDPIAware();
            }
        }
        catch (EntryPointNotFoundException)
        {
            // Old Windows without the per-monitor API: the system-wide awareness fallback is not available either
        }
        catch (DllNotFoundException)
        {
            // Not Windows: nothing to configure
        }
    }

    /// <summary>
    ///     Native <c>SetProcessDpiAwarenessContext</c>.
    /// </summary>
    /// <param name="value">The awareness context.</param>
    /// <returns><see langword="true"/> on success.</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessDpiAwarenessContext(IntPtr value);

    /// <summary>
    ///     Native <c>SetProcessDPIAware</c>.
    /// </summary>
    /// <returns><see langword="true"/> on success.</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessDPIAware();
}
