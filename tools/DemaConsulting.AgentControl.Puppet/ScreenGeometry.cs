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

namespace DemaConsulting.AgentControl.Puppet;

/// <summary>
///     A point in physical screen pixels.
/// </summary>
/// <param name="X">The horizontal pixel.</param>
/// <param name="Y">The vertical pixel.</param>
internal readonly record struct ScreenPoint(double X, double Y)
{
    /// <summary>
    ///     Gets the pixel column, rounded to the nearest pixel.
    /// </summary>
    internal int PixelX => (int)Math.Round(X, MidpointRounding.AwayFromZero);

    /// <summary>
    ///     Gets the pixel row, rounded to the nearest pixel.
    /// </summary>
    internal int PixelY => (int)Math.Round(Y, MidpointRounding.AwayFromZero);
}

/// <summary>
///     A rectangle in physical screen pixels.
/// </summary>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
internal readonly record struct ScreenRect(double X, double Y, double Width, double Height)
{
    /// <summary>
    ///     Gets the right edge.
    /// </summary>
    internal double Right => X + Width;

    /// <summary>
    ///     Gets the bottom edge.
    /// </summary>
    internal double Bottom => Y + Height;

    /// <summary>
    ///     Gets the middle of the rectangle.
    /// </summary>
    internal ScreenPoint Center => new(X + (Width / 2), Y + (Height / 2));

    /// <summary>
    ///     Gets a value indicating whether the rectangle has no area (an element that is not drawn).
    /// </summary>
    internal bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>
    ///     Tests whether a point lies inside the rectangle.
    /// </summary>
    /// <param name="point">The point.</param>
    /// <returns><see langword="true"/> when the point is inside (edges included).</returns>
    internal bool Contains(ScreenPoint point) =>
        point.X >= X && point.X <= Right && point.Y >= Y && point.Y <= Bottom;

    /// <summary>
    ///     Returns a copy shrunk by the same amount on every side.
    /// </summary>
    /// <param name="by">The inset in pixels.</param>
    /// <returns>The smaller rectangle.</returns>
    internal ScreenRect Inset(double by) => new(X + by, Y + by, Width - (2 * by), Height - (2 * by));
}
