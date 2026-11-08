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

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;

namespace DemaConsulting.AgentControl.Puppet;

/// <summary>
///     Renders windows to bitmaps with <c>PrintWindow</c>, so a shot shows AgentControl even when another window covers it.
/// </summary>
/// <remarks>
///     Besides the main window and the dialogs UI Automation lists (at any nesting depth: a dialog opened from a dialog),
///     a shot includes the visible native windows of the process that UI Automation does not list as windows: Avalonia
///     draws a flyout, a combo box drop-down or a tooltip in a window of its own. All of them are composed in z-order.
/// </remarks>
internal static partial class WindowCapture
{
    /// <summary>
    ///     Asks the window to render its full content, including what DirectComposition draws (Windows 8.1+).
    /// </summary>
    private const uint RenderFullContent = 2;

    /// <summary>
    ///     The <c>GW_OWNER</c> command of <c>GetWindow</c>.
    /// </summary>
    private const uint GetWindowOwner = 4;

    /// <summary>
    ///     Renders the main window with any open dialogs and pop-ups drawn over it at their places, on a canvas covering them all.
    /// </summary>
    /// <param name="mainWindow">The main window.</param>
    /// <param name="dialogs">The open dialogs (a dialog can stick out past the main window's edge).</param>
    /// <param name="popups">The native pop-up windows (flyouts, drop-downs, tooltips) that are open.</param>
    /// <returns>The bitmap; the caller disposes it.</returns>
    internal static Bitmap Render(AutomationElement mainWindow, IReadOnlyList<AutomationElement> dialogs, IReadOnlyList<IntPtr> popups)
    {
        var windows = dialogs
            .Prepend(mainWindow)
            .Select(w => new Target(w.Properties.NativeWindowHandle.ValueOrDefault, FrameOf(w), w.Name))
            .Concat(popups.Select(h => new Target(h, NativeFrameOf(h), "pop-up")))
            .ToList();
        var area = windows.Skip(1).Aggregate(windows[0].Bounds, (a, w) => Rectangle.Union(a, w.Bounds));
        var canvas = new Bitmap(Math.Max(1, area.Width), Math.Max(1, area.Height), PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(canvas);
        graphics.Clear(Color.Black);

        // Every window is drawn from the bottom of the stack to the top (an owner below the windows it owns), so a nested
        // dialog covers the dialog that opened it, as on screen
        var mainHandle = windows[0].Handle;
        _ = GetWindowThreadProcessId(mainHandle, out var processId);
        var entries = windows.Select(w => new StackEntry(w.Handle, w.Handle == IntPtr.Zero ? IntPtr.Zero : OwnerOf(w.Handle))).ToList();
        foreach (var index in WindowStacking.DrawOrder(entries, ZOrderOf((int)processId)))
        {
            var window = windows[index];
            using var image = RenderOne(window);
            graphics.DrawImage(image, window.Bounds.X - area.X, window.Bounds.Y - area.Y);
        }

        return canvas;
    }

    /// <summary>
    ///     Copies the pixels of the main window's visible frame (title bar included) (and any pop-ups and dialogs inside the union) from the
    ///     screen, so what only the compositor draws (a drag ghost, a context menu) is in the picture.
    /// </summary>
    /// <param name="mainWindow">The main window.</param>
    /// <param name="others">The open dialogs.</param>
    /// <param name="popups">The native pop-up windows (flyouts, drop-downs, tooltips) that are open.</param>
    /// <returns>The bitmap; the caller disposes it.</returns>
    /// <remarks>The window must be on screen and not covered: unlike <see cref="Render"/> this is a real screen copy.</remarks>
    internal static Bitmap CaptureScreen(AutomationElement mainWindow, IReadOnlyList<AutomationElement> others, IReadOnlyList<IntPtr> popups)
    {
        var handle = mainWindow.Properties.NativeWindowHandle.ValueOrDefault;
        var frame = (handle != IntPtr.Zero ? WindowPlacement.GetVisibleFrame(handle) : null) ?? mainWindow.BoundingRectangle;
        var area = others.Select(w => w.BoundingRectangle)
            .Concat(popups.Select(NativeFrameOf))
            .Aggregate(frame, Rectangle.Union);
        var canvas = new Bitmap(Math.Max(1, area.Width), Math.Max(1, area.Height), PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(canvas);
        graphics.CopyFromScreen(area.Location, Point.Empty, area.Size);
        return canvas;
    }

    /// <summary>
    ///     Lists the visible top-level windows of a process that are not already known, such as an Avalonia pop-up.
    /// </summary>
    /// <param name="processId">The process id.</param>
    /// <param name="knownHandles">The windows UI Automation already lists.</param>
    /// <returns>The window handles, in z-order from the top.</returns>
    internal static IReadOnlyList<IntPtr> FindPopupHandles(int processId, IReadOnlyCollection<IntPtr> knownHandles)
    {
        var found = new List<IntPtr>();
        var window = FindWindowEx(IntPtr.Zero, IntPtr.Zero, null, null);
        while (window != IntPtr.Zero)
        {
            _ = GetWindowThreadProcessId(window, out var owner);
            if (owner == (uint)processId && IsWindowVisible(window) && !knownHandles.Contains(window) && NativeFrameOf(window) is { Width: > 0, Height: > 0 })
            {
                found.Add(window);
            }

            window = FindWindowEx(IntPtr.Zero, window, null, null);
        }

        return found;
    }

    /// <summary>
    ///     Lists every top-level window of a process (visible or not) in z-order, from the top of the stack down.
    /// </summary>
    /// <param name="processId">The process id.</param>
    /// <returns>The window handles, topmost first.</returns>
    internal static IReadOnlyList<IntPtr> ZOrderOf(int processId)
    {
        var found = new List<IntPtr>();
        var window = FindWindowEx(IntPtr.Zero, IntPtr.Zero, null, null);
        while (window != IntPtr.Zero)
        {
            _ = GetWindowThreadProcessId(window, out var owner);
            if (owner == (uint)processId)
            {
                found.Add(window);
            }

            window = FindWindowEx(IntPtr.Zero, window, null, null);
        }

        return found;
    }

    /// <summary>
    ///     Gets the owner window of a top-level window (the window a dialog was opened from).
    /// </summary>
    /// <param name="handle">The window.</param>
    /// <returns>The owner, or zero when the window has none.</returns>
    internal static IntPtr OwnerOf(IntPtr handle) => GetWindow(handle, GetWindowOwner);

    /// <summary>
    ///     Gets the window's whole frame (title bar and borders included), which <c>PrintWindow</c> draws.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <returns>The frame in screen pixels.</returns>
    private static Rectangle FrameOf(AutomationElement window)
    {
        var handle = window.Properties.NativeWindowHandle.ValueOrDefault;
        return handle != IntPtr.Zero && GetWindowRect(handle, out var r)
            ? Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom)
            : window.BoundingRectangle;
    }

    /// <summary>
    ///     Gets the frame of a native window.
    /// </summary>
    /// <param name="handle">The window.</param>
    /// <returns>The frame in screen pixels (empty when it cannot be read).</returns>
    private static Rectangle NativeFrameOf(IntPtr handle) =>
        GetWindowRect(handle, out var r) ? Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom) : Rectangle.Empty;

    /// <summary>
    ///     Renders one window into a bitmap.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <returns>The bitmap.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <c>PrintWindow</c> fails.</exception>
    private static Bitmap RenderOne(Target window)
    {
        var bounds = window.Bounds;
        var image = new Bitmap(Math.Max(1, bounds.Width), Math.Max(1, bounds.Height), PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(image);

            // A pop-up without a window handle of its own (a tooltip) cannot be printed: copy its pixels from the screen
            if (window.Handle == IntPtr.Zero)
            {
                graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                return image;
            }

            var dc = graphics.GetHdc();
            try
            {
                if (!PrintWindow(window.Handle, dc, RenderFullContent))
                {
                    throw new InvalidOperationException($"the window \"{window.Label}\" could not be rendered (PrintWindow failed)");
                }
            }
            finally
            {
                graphics.ReleaseHdc(dc);
            }

            return image;
        }
        catch
        {
            // The caller never receives the bitmap when rendering throws, so it is released here
            image.Dispose();
            throw;
        }
    }

    /// <summary>
    ///     Native <c>PrintWindow</c>.
    /// </summary>
    /// <param name="hwnd">The window.</param>
    /// <param name="hdc">The device context to draw into.</param>
    /// <param name="flags">The render flags.</param>
    /// <returns><see langword="true"/> on success.</returns>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    /// <summary>
    ///     Native <c>GetWindowRect</c>.
    /// </summary>
    /// <param name="hwnd">The window.</param>
    /// <param name="rect">Its frame.</param>
    /// <returns><see langword="true"/> on success.</returns>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    /// <summary>
    ///     Native <c>FindWindowExW</c>, used to walk the top-level windows.
    /// </summary>
    /// <param name="parent">The parent window (zero for the top-level windows).</param>
    /// <param name="after">The window to continue after (zero to start).</param>
    /// <param name="className">The class name to match, or <see langword="null"/> for any.</param>
    /// <param name="windowName">The title to match, or <see langword="null"/> for any.</param>
    /// <returns>The next window, or zero.</returns>
    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? windowName);

    /// <summary>
    ///     Native <c>GetWindowThreadProcessId</c>.
    /// </summary>
    /// <param name="hwnd">The window.</param>
    /// <param name="processId">The owning process id.</param>
    /// <returns>The thread id.</returns>
    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    /// <summary>
    ///     Native <c>GetWindow</c>.
    /// </summary>
    /// <param name="hwnd">The window.</param>
    /// <param name="command">The relationship to look up.</param>
    /// <returns>The related window, or zero.</returns>
    [LibraryImport("user32.dll")]
    private static partial IntPtr GetWindow(IntPtr hwnd, uint command);

    /// <summary>
    ///     Native <c>IsWindowVisible</c>.
    /// </summary>
    /// <param name="hwnd">The window.</param>
    /// <returns><see langword="true"/> when the window is visible.</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hwnd);

    /// <summary>
    ///     One window to draw into a shot.
    /// </summary>
    /// <param name="Handle">The window handle, or zero for one that has none (copied from the screen).</param>
    /// <param name="Bounds">The window's frame in screen pixels.</param>
    /// <param name="Label">A name for messages.</param>
    private sealed record Target(IntPtr Handle, Rectangle Bounds, string Label);

    /// <summary>
    ///     The native <c>RECT</c> structure.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        /// <summary>
        ///     The left edge.
        /// </summary>
        public int Left;

        /// <summary>
        ///     The top edge.
        /// </summary>
        public int Top;

        /// <summary>
        ///     The right edge.
        /// </summary>
        public int Right;

        /// <summary>
        ///     The bottom edge.
        /// </summary>
        public int Bottom;
    }
}
