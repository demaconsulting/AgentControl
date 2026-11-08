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
using System.Globalization;
using System.Runtime.InteropServices;

namespace DemaConsulting.AgentControl.Puppet;

/// <summary>
///     Where the <c>place</c> command puts the main window: either explicit pixels or the work area minus margins.
/// </summary>
/// <param name="Explicit">The explicit rectangle, or <see langword="null"/> when the work area is used.</param>
/// <param name="LeftGap">With the work area: the gap kept free on its left edge, in pixels.</param>
/// <param name="Margin">With the work area: the gap kept on the top, right and bottom edges, in pixels.</param>
internal sealed record PlacementRequest(Rectangle? Explicit, int LeftGap, int Margin)
{
    /// <summary>
    ///     The default margin of the work-area form, in pixels.
    /// </summary>
    internal const int DefaultMargin = 8;

    /// <summary>
    ///     Reads a placement from a parsed command: <c>--x --y --width --height</c> (all four) or <c>--work-area</c>
    ///     with optional <c>--left-gap</c> and <c>--margin</c>.
    /// </summary>
    /// <param name="parsed">The parsed command.</param>
    /// <returns>The request.</returns>
    /// <exception cref="PuppetException">Thrown when the options are incomplete, not integers or not positive.</exception>
    internal static PlacementRequest Parse(ParsedCommand parsed)
    {
        const string usage = "agentcontrol-puppet place [--instance <alias>] (--x <px> --y <px> --width <px> --height <px> | --work-area [--left-gap <px>] [--margin <px>])";
        if (parsed.HasOption("work-area"))
        {
            var leftGap = Pixels(parsed, "left-gap", 0, allowNegative: false);
            var margin = Pixels(parsed, "margin", DefaultMargin, allowNegative: false);
            return new PlacementRequest(null, leftGap, margin);
        }

        var names = new[] { "x", "y", "width", "height" };
        if (names.Any(name => parsed.GetOption(name) is null))
        {
            throw PuppetException.Usage(usage);
        }

        var x = Pixels(parsed, "x", 0, allowNegative: true);
        var y = Pixels(parsed, "y", 0, allowNegative: true);
        var width = Pixels(parsed, "width", 0, allowNegative: false);
        var height = Pixels(parsed, "height", 0, allowNegative: false);
        return width <= 0 || height <= 0
            ? throw PuppetException.Usage("place: --width and --height must be greater than 0.")
            : new PlacementRequest(new Rectangle(x, y, width, height), 0, 0);
    }

    /// <summary>
    ///     Computes the rectangle to put the window in.
    /// </summary>
    /// <param name="workArea">The work area of the primary screen (the screen minus the taskbar).</param>
    /// <returns>The rectangle in physical pixels.</returns>
    /// <exception cref="PuppetException">Thrown when the gaps leave no room.</exception>
    internal Rectangle Resolve(Rectangle workArea)
    {
        if (Explicit is { } rectangle)
        {
            return rectangle;
        }

        var x = workArea.Left + LeftGap;
        var y = workArea.Top + Margin;
        var width = workArea.Right - Margin - x;
        var height = workArea.Bottom - Margin - y;
        return width <= 0 || height <= 0
            ? throw PuppetException.Usage($"place: the gaps leave no room in the work area {workArea.Width}x{workArea.Height}.")
            : new Rectangle(x, y, width, height);
    }

    /// <summary>
    ///     Reads an integer option.
    /// </summary>
    /// <param name="parsed">The parsed command.</param>
    /// <param name="name">The option name.</param>
    /// <param name="fallback">The value when absent.</param>
    /// <param name="allowNegative">Whether a negative value is accepted.</param>
    /// <returns>The value.</returns>
    private static int Pixels(ParsedCommand parsed, string name, int fallback, bool allowNegative)
    {
        if (parsed.GetOption(name) is not { } text)
        {
            return fallback;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || (!allowNegative && value < 0))
        {
            throw PuppetException.Usage($"--{name} must be a {(allowNegative ? "whole number" : "non-negative whole number")} of pixels, not '{text}'.");
        }

        return value;
    }
}

/// <summary>
///     Moves and resizes top-level windows with Win32.
/// </summary>
internal static partial class WindowPlacement
{
    /// <summary>
    ///     <c>SystemParametersInfo</c> action that reads the work area of the primary screen.
    /// </summary>
    private const uint GetWorkAreaAction = 0x0030;

    /// <summary>
    ///     <c>ShowWindow</c> command that restores a minimized or maximized window.
    /// </summary>
    private const int Restore = 9;

    /// <summary>
    ///     <c>SetWindowPos</c> flags: keep the z-order (<c>0x4</c>) and do not activate (<c>0x10</c>).
    /// </summary>
    private const uint NoZOrderNoActivate = 0x0004 | 0x0010;

    /// <summary>
    ///     <c>DwmGetWindowAttribute</c> attribute that reads the visible frame, without the invisible resize borders.
    /// </summary>
    private const int ExtendedFrameBounds = 9;

    /// <summary>
    ///     Reads the work area of the primary screen.
    /// </summary>
    /// <returns>The work area in physical pixels (the process is per-monitor DPI aware).</returns>
    /// <exception cref="InvalidOperationException">Thrown when Windows cannot tell.</exception>
    internal static Rectangle GetWorkArea() =>
        SystemParametersInfo(GetWorkAreaAction, 0, out var r, 0)
            ? Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom)
            : throw new InvalidOperationException("the work area of the primary screen could not be read");

    /// <summary>
    ///     Restores the window and puts its <b>visible</b> frame at a rectangle.
    /// </summary>
    /// <param name="handle">The window handle.</param>
    /// <param name="visible">The wanted visible rectangle in physical pixels.</param>
    /// <returns>The visible frame the window ended up with.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the window could not be moved.</exception>
    internal static Rectangle Place(IntPtr handle, Rectangle visible)
    {
        ShowWindow(handle, Restore);
        if (!SetWindowPos(handle, IntPtr.Zero, visible.X, visible.Y, visible.Width, visible.Height, NoZOrderNoActivate))
        {
            throw new InvalidOperationException("the window could not be moved (SetWindowPos failed)");
        }

        // A framed window has invisible resize borders: grow the request by them so the visible frame lands where asked
        if (GetWindowRect(handle, out var outer) && DwmGetWindowAttribute(handle, ExtendedFrameBounds, out var inner, Marshal.SizeOf<NativeRect>()) == 0)
        {
            var left = inner.Left - outer.Left;
            var top = inner.Top - outer.Top;
            var right = outer.Right - inner.Right;
            var bottom = outer.Bottom - inner.Bottom;
            if (left != 0 || top != 0 || right != 0 || bottom != 0)
            {
                SetWindowPos(handle, IntPtr.Zero, visible.X - left, visible.Y - top, visible.Width + left + right, visible.Height + top + bottom, NoZOrderNoActivate);
            }
        }

        return DwmGetWindowAttribute(handle, ExtendedFrameBounds, out var result, Marshal.SizeOf<NativeRect>()) == 0
            ? Rectangle.FromLTRB(result.Left, result.Top, result.Right, result.Bottom)
            : visible;
    }

    /// <summary>
    ///     Reads the visible frame of a window (title bar and borders, without the invisible resize borders).
    /// </summary>
    /// <param name="handle">The window handle.</param>
    /// <returns>The frame in physical pixels, or <see langword="null"/> when it cannot be read.</returns>
    internal static Rectangle? GetVisibleFrame(IntPtr handle) =>
        DwmGetWindowAttribute(handle, ExtendedFrameBounds, out var r, Marshal.SizeOf<NativeRect>()) == 0
            ? Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom)
            : null;

    /// <summary>
    ///     Native <c>SystemParametersInfo</c> (the rectangle form).
    /// </summary>
    /// <param name="action">The action.</param>
    /// <param name="param">Not used.</param>
    /// <param name="rect">The rectangle that receives the work area.</param>
    /// <param name="flags">Not used.</param>
    /// <returns><see langword="true"/> on success.</returns>
    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint action, uint param, out NativeRect rect, uint flags);

    /// <summary>
    ///     Native <c>ShowWindow</c>.
    /// </summary>
    /// <param name="hwnd">The window.</param>
    /// <param name="command">The show command.</param>
    /// <returns>Whether the window was visible before.</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hwnd, int command);

    /// <summary>
    ///     Native <c>SetWindowPos</c>.
    /// </summary>
    /// <param name="hwnd">The window.</param>
    /// <param name="insertAfter">The z-order neighbor (ignored with the no-z-order flag).</param>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="flags">The flags.</param>
    /// <returns><see langword="true"/> on success.</returns>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    /// <summary>
    ///     Native <c>GetWindowRect</c>.
    /// </summary>
    /// <param name="hwnd">The window.</param>
    /// <param name="rect">Its frame including the invisible borders.</param>
    /// <returns><see langword="true"/> on success.</returns>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    /// <summary>
    ///     Native <c>DwmGetWindowAttribute</c> (the rectangle form).
    /// </summary>
    /// <param name="hwnd">The window.</param>
    /// <param name="attribute">The attribute.</param>
    /// <param name="value">The rectangle that receives the value.</param>
    /// <param name="size">The size of the rectangle.</param>
    /// <returns>0 on success.</returns>
    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out NativeRect value, int size);

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
