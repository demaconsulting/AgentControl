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
using DemaConsulting.AgentControl.Puppet.Automation;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUiMouse = FlaUI.Core.Input.Mouse;

namespace DemaConsulting.AgentControl.Puppet;

/// <summary>
///     Real operating-system mouse and keyboard input, aimed at physical screen pixels.
/// </summary>
/// <remarks>
///     Every press is guarded: the window under the target pixel must belong to the instance being driven, otherwise
///     nothing is sent (exit code <see cref="ExitCodes.Obscured"/>), so a covered, off-screen or locked-desktop target
///     never lands a click in somebody else's window. Moves go in small steps with short pauses so Avalonia sees real
///     pointer movement rather than a jump.
/// </remarks>
internal static partial class InputDriver
{
    /// <summary>
    ///     The distance in pixels of one interpolated mouse step.
    /// </summary>
    private const double StepPixels = 12;

    /// <summary>
    ///     The pause after each step.
    /// </summary>
    private static readonly TimeSpan StepDelay = TimeSpan.FromMilliseconds(8);

    /// <summary>
    ///     The pause that lets the UI thread process what was just injected.
    /// </summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(60);

    /// <summary>
    ///     How long Alt stays down after its chord's key is released.
    /// </summary>
    private static readonly TimeSpan AltHold = TimeSpan.FromMilliseconds(400);

    /// <summary>
    ///     The pause between the two clicks of a double click (well inside the system double-click time).
    /// </summary>
    private static readonly TimeSpan DoubleClickGap = TimeSpan.FromMilliseconds(50);

    /// <summary>
    ///     Restores and raises the main window as far as Windows allows a background process to.
    /// </summary>
    /// <param name="context">The attached instance.</param>
    internal static void BringToFront(AttachContext context)
    {
        var handle = context.MainWindow.Properties.NativeWindowHandle.ValueOrDefault;
        if (handle != IntPtr.Zero && IsIconic(handle))
        {
            ShowWindow(handle, ShowRestore);
        }

        _ = UiActions.Safe(() => { context.MainWindow.Focus(); return true; }, false);
        _ = UiActions.Safe(() => { context.MainWindow.SetForeground(); return true; }, false);
        Thread.Sleep(150);

        // Windows refuses a background process the foreground now and then: borrow the foreground thread's input
        // queue for a moment, which is what the system allows, and try again until the window is in front
        for (var attempt = 0; attempt < 5 && handle != IntPtr.Zero && !IsForeground(handle); attempt++)
        {
            ForceForeground(handle);
            Thread.Sleep(150);
        }
    }

    /// <summary>
    ///     Tests whether a window (or a window it owns) is the foreground window.
    /// </summary>
    /// <param name="handle">The window.</param>
    /// <returns><see langword="true"/> when it is in front.</returns>
    private static bool IsForeground(IntPtr handle)
    {
        var front = GetForegroundWindow();
        return front != IntPtr.Zero && (front == handle || GetAncestor(front, RootOwnerAncestor) == handle);
    }

    /// <summary>
    ///     Brings a window to the foreground by attaching to the input queue of the current foreground window.
    /// </summary>
    /// <param name="handle">The window.</param>
    private static void ForceForeground(IntPtr handle)
    {
        var front = GetForegroundWindow();
        var frontThread = front == IntPtr.Zero ? 0 : GetWindowThreadProcessId(front, out _);
        var ownThread = GetCurrentThreadId();
        var attached = frontThread != 0 && frontThread != ownThread && AttachThreadInput(ownThread, frontThread, true);
        try
        {
            _ = ShowWindow(handle, ShowRestore);
            _ = SetForegroundWindow(handle);
        }
        finally
        {
            if (attached)
            {
                _ = AttachThreadInput(ownThread, frontThread, false);
            }
        }
    }

    /// <summary>
    ///     Tests whether the topmost window at a pixel belongs to a process.
    /// </summary>
    /// <param name="point">The pixel.</param>
    /// <param name="processId">The process id.</param>
    /// <returns><see langword="true"/> when the process's window is at the pixel.</returns>
    internal static bool IsOwnedBy(ScreenPoint point, int processId)
    {
        var window = WindowFromPoint(new NativePoint { X = point.PixelX, Y = point.PixelY });
        if (window == IntPtr.Zero)
        {
            return false;
        }

        var root = GetAncestor(window, RootAncestor);
        _ = GetWindowThreadProcessId(root == IntPtr.Zero ? window : root, out var owner);
        return owner == (uint)processId;
    }

    /// <summary>
    ///     Describes who owns the topmost window at a pixel, for the refusal message.
    /// </summary>
    /// <param name="point">The pixel.</param>
    /// <returns>A short description such as <c>pid 1234</c> or <c>no window</c>.</returns>
    internal static string DescribeOwner(ScreenPoint point)
    {
        var window = WindowFromPoint(new NativePoint { X = point.PixelX, Y = point.PixelY });
        if (window == IntPtr.Zero)
        {
            return "no window";
        }

        _ = GetWindowThreadProcessId(window, out var owner);
        return $"a window of pid {owner}";
    }

    /// <summary>
    ///     Clicks a mouse button at a pixel.
    /// </summary>
    /// <param name="at">The pixel.</param>
    /// <param name="button">The button.</param>
    /// <param name="modifiers">The modifier keys to hold during the click.</param>
    /// <param name="clicks">1 for a click, 2 for a double click.</param>
    internal static void Click(ScreenPoint at, MouseButton button, KeyChordModifiers modifiers, int clicks = 1)
    {
        using var held = new HeldModifiers(modifiers);
        MoveTo(at);
        for (var i = 0; i < clicks; i++)
        {
            FlaUiMouse.Down(button);
            Thread.Sleep(30);
            FlaUiMouse.Up(button);
            if (i + 1 < clicks)
            {
                Thread.Sleep(DoubleClickGap);
            }
        }

        Thread.Sleep(Settle);
    }

    /// <summary>
    ///     Presses at one pixel, drags in small steps to another and releases.
    /// </summary>
    /// <param name="from">Where to press.</param>
    /// <param name="to">Where to release.</param>
    /// <param name="modifiers">The modifier keys to hold during the drag.</param>
    /// <param name="whileHeld">An action to run at the destination while the button is still down (a screenshot of a drag ghost), or <see langword="null"/>.</param>
    internal static void Drag(ScreenPoint from, ScreenPoint to, KeyChordModifiers modifiers, Action? whileHeld = null)
    {
        using var held = new HeldModifiers(modifiers);
        MoveTo(from);
        FlaUiMouse.Down(MouseButton.Left);
        Thread.Sleep(Settle);
        try
        {
            // A first small nudge crosses the editor's and OLE's drag threshold before the long travel
            FlaUiMouse.Position = new System.Drawing.Point(from.PixelX + Math.Sign(to.X - from.X) * 6, from.PixelY + Math.Sign(to.Y - from.Y) * 6);
            Thread.Sleep(StepDelay);
            Travel(new ScreenPoint(FlaUiMouse.Position.X, FlaUiMouse.Position.Y), to);

            // Hover a moment at the destination: drop targets update their feedback on the next pointer event
            Thread.Sleep(Settle);
            FlaUiMouse.Position = new System.Drawing.Point(to.PixelX, to.PixelY);
            Thread.Sleep(Settle);
            whileHeld?.Invoke();
        }
        finally
        {
            FlaUiMouse.Up(MouseButton.Left);
        }

        Thread.Sleep(Settle);
    }

    /// <summary>
    ///     Turns the mouse wheel at a pixel.
    /// </summary>
    /// <param name="at">The pixel.</param>
    /// <param name="notches">The notches; positive scrolls up (away from the user), negative down.</param>
    /// <param name="modifiers">The modifier keys to hold while scrolling.</param>
    internal static void Wheel(ScreenPoint at, int notches, KeyChordModifiers modifiers)
    {
        using var held = new HeldModifiers(modifiers);
        MoveTo(at);
        Thread.Sleep(Settle);
        for (var i = 0; i < Math.Abs(notches); i++)
        {
            FlaUiMouse.Scroll(Math.Sign(notches));
            Thread.Sleep(StepDelay * 4);
        }

        Thread.Sleep(Settle);
    }

    /// <summary>
    ///     Rests the cursor on a target the way a user hovers it: away from everything first, then onto the target in small
    ///     steps, then still for a while so a tooltip can open.
    /// </summary>
    /// <remarks>
    ///     Stale modifier keys are released first (a held Ctrl or Shift would change what the editor shows). The moves are
    ///     real pointer moves, so the editor sees the pointer leave whatever it was on and arrive at the target.
    /// </remarks>
    /// <param name="park">Where to park the cursor first (empty canvas), or <see langword="null"/> to go straight to the target.</param>
    /// <param name="target">The pixel to rest on.</param>
    /// <param name="rest">How long to rest on the target before returning.</param>
    internal static void Hover(ScreenPoint? park, ScreenPoint target, TimeSpan rest)
    {
        using var held = new HeldModifiers(KeyChordModifiers.None);

        // Parking on empty canvas closes any open tooltip and lets the editor forget its last tooltip target
        if (park is { } parked)
        {
            MoveTo(parked);
            Thread.Sleep(Settle * 3);
        }

        // Arrive at the target in steps, then keep still so the tooltip delay can run out
        MoveTo(target);
        Thread.Sleep(rest);
    }

    /// <summary>
    ///     Presses a key chord on the keyboard.
    /// </summary>
    /// <param name="chord">The chord.</param>
    internal static void PressChord(KeyChordSpec chord)
    {
        using var held = new HeldModifiers(chord.Modifiers);
        Keyboard.Press(chord.Key);
        Keyboard.Release(chord.Key);

        // A person lets go of Alt a moment after the key: releasing it at once reaches the window before a dialog that
        // the chord opens is up, and an Alt press and release on its own moves the focus to the menu bar.
        if (chord.Modifiers.HasFlag(KeyChordModifiers.Alt))
        {
            Thread.Sleep(AltHold);
        }

        Thread.Sleep(Settle);
    }

    /// <summary>
    ///     Moves the cursor to a pixel in small steps.
    /// </summary>
    /// <param name="to">The destination.</param>
    internal static void MoveTo(ScreenPoint to)
    {
        var here = FlaUiMouse.Position;
        Travel(new ScreenPoint(here.X, here.Y), to);
        FlaUiMouse.Position = new System.Drawing.Point(to.PixelX, to.PixelY);
        Thread.Sleep(StepDelay);
    }

    /// <summary>
    ///     Computes the intermediate pixels of a straight move.
    /// </summary>
    /// <param name="from">The start.</param>
    /// <param name="to">The end.</param>
    /// <param name="stepPixels">The distance between points.</param>
    /// <returns>The points after the start up to, and including, the end.</returns>
    internal static IReadOnlyList<ScreenPoint> Interpolate(ScreenPoint from, ScreenPoint to, double stepPixels = StepPixels)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((dx * dx) + (dy * dy)) / stepPixels));
        return [.. Enumerable.Range(1, steps).Select(i => new ScreenPoint(from.X + (dx * i / steps), from.Y + (dy * i / steps)))];
    }

    /// <summary>
    ///     Moves the cursor along a straight line, pausing after each step.
    /// </summary>
    /// <param name="from">The start.</param>
    /// <param name="to">The end.</param>
    private static void Travel(ScreenPoint from, ScreenPoint to)
    {
        foreach (var point in Interpolate(from, to))
        {
            FlaUiMouse.Position = new System.Drawing.Point(point.PixelX, point.PixelY);
            Thread.Sleep(StepDelay);
        }
    }

    /// <summary>
    ///     The <c>SW_RESTORE</c> show command.
    /// </summary>
    private const int ShowRestore = 9;

    /// <summary>
    ///     The <c>GA_ROOT</c> ancestor kind.
    /// </summary>
    private const uint RootAncestor = 2;

    /// <summary>
    ///     The <c>GA_ROOTOWNER</c> ancestor kind.
    /// </summary>
    private const uint RootOwnerAncestor = 3;

    /// <summary>
    ///     Native <c>GetForegroundWindow</c>.
    /// </summary>
    /// <returns>The foreground window, or zero.</returns>
    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    /// <summary>
    ///     Native <c>SetForegroundWindow</c>.
    /// </summary>
    /// <param name="hwnd">The window.</param>
    /// <returns><see langword="true"/> when the window was brought to the foreground.</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hwnd);

    /// <summary>
    ///     Native <c>AttachThreadInput</c>.
    /// </summary>
    /// <param name="attach">The thread that attaches.</param>
    /// <param name="attachTo">The thread to attach to.</param>
    /// <param name="attachNow"><see langword="true"/> to attach, <see langword="false"/> to detach.</param>
    /// <returns><see langword="true"/> on success.</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(uint attach, uint attachTo, [MarshalAs(UnmanagedType.Bool)] bool attachNow);

    /// <summary>
    ///     Native <c>GetCurrentThreadId</c>.
    /// </summary>
    /// <returns>The calling thread's id.</returns>
    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    /// <summary>
    ///     Native <c>IsIconic</c>.
    /// </summary>
    /// <param name="hwnd">The window.</param>
    /// <returns><see langword="true"/> when the window is minimized.</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(IntPtr hwnd);

    /// <summary>
    ///     Native <c>ShowWindow</c>.
    /// </summary>
    /// <param name="hwnd">The window.</param>
    /// <param name="command">The show command.</param>
    /// <returns><see langword="true"/> when the window was visible before.</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hwnd, int command);

    /// <summary>
    ///     Native <c>WindowFromPoint</c>.
    /// </summary>
    /// <param name="point">The pixel.</param>
    /// <returns>The window handle, or zero.</returns>
    [LibraryImport("user32.dll")]
    private static partial IntPtr WindowFromPoint(NativePoint point);

    /// <summary>
    ///     Native <c>GetAncestor</c>.
    /// </summary>
    /// <param name="hwnd">The window.</param>
    /// <param name="flags">The ancestor kind.</param>
    /// <returns>The ancestor handle.</returns>
    [LibraryImport("user32.dll")]
    private static partial IntPtr GetAncestor(IntPtr hwnd, uint flags);

    /// <summary>
    ///     Native <c>GetWindowThreadProcessId</c>.
    /// </summary>
    /// <param name="hwnd">The window.</param>
    /// <param name="processId">The owning process id.</param>
    /// <returns>The thread id.</returns>
    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    /// <summary>
    ///     The native <c>POINT</c> structure.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        /// <summary>
        ///     The horizontal pixel.
        /// </summary>
        public int X;

        /// <summary>
        ///     The vertical pixel.
        /// </summary>
        public int Y;
    }

    /// <summary>
    ///     Holds modifier keys down for the lifetime of the instance and releases them (in reverse) on disposal.
    /// </summary>
    private sealed partial class HeldModifiers : IDisposable
    {
        /// <summary>
        ///     The bit of a <c>GetAsyncKeyState</c> result that is set while the key is down.
        /// </summary>
        private const int KeyDownBit = 0x8000;

        /// <summary>
        ///     The keys that are down.
        /// </summary>
        private readonly List<VirtualKeyShort> _keys = [];

        /// <summary>
        ///     Initializes a new instance of the <see cref="HeldModifiers"/> class and presses the modifiers.
        /// </summary>
        /// <param name="modifiers">The modifiers to hold.</param>
        public HeldModifiers(KeyChordModifiers modifiers)
        {
            // Stale modifier state from an earlier aborted run would change the meaning of the input. Only a modifier that
            // really is down is released: a key-up of a key that is not down is not something a keyboard sends, and an
            // Alt key-up on its own opens the menu bar of an Avalonia window
            foreach (var key in new[] { VirtualKeyShort.SHIFT, VirtualKeyShort.CONTROL, VirtualKeyShort.ALT }.Where(key => (GetAsyncKeyState((int)key) & KeyDownBit) != 0))
            {
                _ = UiActions.Safe(() => { Keyboard.Release(key); return true; }, false);
            }

            if (modifiers.HasFlag(KeyChordModifiers.Control))
            {
                Press(VirtualKeyShort.CONTROL);
            }

            if (modifiers.HasFlag(KeyChordModifiers.Shift))
            {
                Press(VirtualKeyShort.SHIFT);
            }

            if (modifiers.HasFlag(KeyChordModifiers.Alt))
            {
                Press(VirtualKeyShort.ALT);
            }
        }

        /// <summary>
        ///     Releases the held modifiers.
        /// </summary>
        public void Dispose()
        {
            for (var i = _keys.Count - 1; i >= 0; i--)
            {
                Keyboard.Release(_keys[i]);
            }

            _keys.Clear();
        }

        /// <summary>
        ///     Native <c>GetAsyncKeyState</c>.
        /// </summary>
        /// <param name="virtualKey">The virtual key code.</param>
        /// <returns>A value whose high bit is set while the key is down.</returns>
        [LibraryImport("user32.dll")]
        private static partial short GetAsyncKeyState(int virtualKey);

        /// <summary>
        ///     Presses a modifier and remembers it.
        /// </summary>
        /// <param name="key">The key.</param>
        private void Press(VirtualKeyShort key)
        {
            Keyboard.Press(key);
            _keys.Add(key);
        }
    }
}
