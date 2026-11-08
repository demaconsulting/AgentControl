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

namespace DemaConsulting.AgentControl.Puppet.Tests;

/// <summary>
///     Tests for the draw-order planning of a shot's windows (no window is touched).
/// </summary>
public class WindowStackingTests
{
    /// <summary>
    ///     Creates a window handle for a test.
    /// </summary>
    /// <param name="value">The handle value.</param>
    /// <returns>The handle.</returns>
    private static IntPtr H(int value) => new(value);

    /// <summary>
    ///     Validates that windows are drawn bottom to top, the reverse of the topmost-first z-order.
    /// </summary>
    [Fact]
    public void WindowStacking_DrawOrder_FollowsZOrderFromBottomToTop()
    {
        // Arrange
        var entries = new[] { new StackEntry(H(1), IntPtr.Zero), new StackEntry(H(2), H(1)), new StackEntry(H(3), H(2)) };

        // Act
        var order = WindowStacking.DrawOrder(entries, [H(3), H(2), H(1)]);

        // Assert
        Assert.Equal([0, 1, 2], order);
    }

    /// <summary>
    ///     Validates that a three-level owner chain given in reverse (innermost first, as pop-up discovery lists it) is
    ///     drawn outer to inner.
    /// </summary>
    [Fact]
    public void WindowStacking_DrawOrder_NestedChainListedInnermostFirst_DrawsOwnerBeforeOwned()
    {
        // Arrange: main(1) <- settings(2) <- editor(3) <- prompt(4), given prompt first
        var entries = new[]
        {
            new StackEntry(H(4), H(3)),
            new StackEntry(H(3), H(2)),
            new StackEntry(H(2), H(1)),
            new StackEntry(H(1), IntPtr.Zero),
        };

        // Act
        var order = WindowStacking.DrawOrder(entries, [H(4), H(3), H(2), H(1)]);

        // Assert
        Assert.Equal([3, 2, 1, 0], order);
    }

    /// <summary>
    ///     Validates that the owner rule wins when the reported z-order contradicts it.
    /// </summary>
    [Fact]
    public void WindowStacking_DrawOrder_ZOrderContradictsOwnership_OwnerStillDrawnFirst()
    {
        // Arrange: the z-order claims the owned window is below its owner
        var entries = new[] { new StackEntry(H(1), IntPtr.Zero), new StackEntry(H(2), H(1)) };

        // Act
        var order = WindowStacking.DrawOrder(entries, [H(1), H(2)]);

        // Assert
        Assert.Equal([0, 1], order);
    }

    /// <summary>
    ///     Validates that unrelated windows keep their z-order and an owned window follows its owner.
    /// </summary>
    [Fact]
    public void WindowStacking_DrawOrder_SiblingDialogs_FollowZOrder()
    {
        // Arrange: main(1) with two non-modal windows, 3 above 2
        var entries = new[] { new StackEntry(H(2), H(1)), new StackEntry(H(3), H(1)), new StackEntry(H(1), IntPtr.Zero) };

        // Act
        var order = WindowStacking.DrawOrder(entries, [H(3), H(2), H(1)]);

        // Assert
        Assert.Equal([2, 0, 1], order);
    }

    /// <summary>
    ///     Validates that windows without a handle (tooltips) are drawn last, in their given order, and a handle the
    ///     z-order does not list goes to the bottom.
    /// </summary>
    [Fact]
    public void WindowStacking_DrawOrder_HandleLessAndUnlistedWindows_PlacedLastAndFirst()
    {
        // Arrange
        var entries = new[]
        {
            new StackEntry(IntPtr.Zero, IntPtr.Zero),
            new StackEntry(H(5), IntPtr.Zero),
            new StackEntry(H(1), IntPtr.Zero),
            new StackEntry(IntPtr.Zero, IntPtr.Zero),
        };

        // Act
        var order = WindowStacking.DrawOrder(entries, [H(1)]);

        // Assert
        Assert.Equal([1, 2, 0, 3], order);
    }

    /// <summary>
    ///     Validates that an owner cycle (which Windows never produces) does not loop and still places every window once.
    /// </summary>
    [Fact]
    public void WindowStacking_DrawOrder_OwnerCycle_TerminatesWithEveryWindowOnce()
    {
        // Arrange
        var entries = new[] { new StackEntry(H(1), H(2)), new StackEntry(H(2), H(1)) };

        // Act
        var order = WindowStacking.DrawOrder(entries, [H(2), H(1)]);

        // Assert
        Assert.Equal(2, order.Count);
        Assert.Equal([0, 1], order.Order());
    }

    /// <summary>
    ///     Validates that an owner that is not part of the shot (hidden) does not affect the order.
    /// </summary>
    [Fact]
    public void WindowStacking_DrawOrder_OwnerNotInShot_IsIgnored()
    {
        // Arrange
        var entries = new[] { new StackEntry(H(2), H(99)), new StackEntry(H(1), IntPtr.Zero) };

        // Act
        var order = WindowStacking.DrawOrder(entries, [H(2), H(1)]);

        // Assert
        Assert.Equal([1, 0], order);
    }
}
