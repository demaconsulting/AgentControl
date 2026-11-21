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

using DemaConsulting.AgentControl.LauncherUI;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Unit tests for <see cref="GlobalProgressViewModel"/> and the
///     <see cref="GlobalProgressOperation"/> its owners report through.
/// </summary>
public sealed class GlobalProgressViewModelTests
{
    /// <summary>
    ///     Test that a new strip is idle: not shown, no text, no Cancel.
    /// </summary>
    [Fact]
    public void GlobalProgressViewModel_Idle_IsInactiveWithNoText()
    {
        // Arrange / Act: a new strip
        var progress = new GlobalProgressViewModel();

        // Assert: nothing is shown
        Assert.False(progress.IsActive);
        Assert.Equal(string.Empty, progress.Text);
        Assert.Equal(string.Empty, progress.AccessibleName);
        Assert.False(progress.CanCancel);
        Assert.False(progress.CancelCommand.CanExecute(null));
    }

    /// <summary>
    ///     Test that beginning an operation shows the strip, indeterminate, with the operation's text
    ///     as the screen-reader name too.
    /// </summary>
    [Fact]
    public void GlobalProgressViewModel_Begin_ShowsIndeterminateText()
    {
        // Arrange
        var progress = new GlobalProgressViewModel();

        // Act
        using var operation = progress.Begin("Adding repositories...");

        // Assert
        Assert.True(progress.IsActive);
        Assert.True(progress.IsIndeterminate);
        Assert.Equal("Adding repositories...", progress.Text);
        Assert.Equal("Adding repositories...", progress.AccessibleName);
    }

    /// <summary>
    ///     Test that reporting a count makes the bar determinate with the counts and the new text.
    /// </summary>
    [Fact]
    public void GlobalProgressViewModel_Report_ShowsCountsDeterminate()
    {
        // Arrange
        var progress = new GlobalProgressViewModel();
        using var operation = progress.Begin("Adding repositories...");

        // Act
        operation.Report(7, 20, "Adding 7 of 20 repositories...");

        // Assert
        Assert.False(progress.IsIndeterminate);
        Assert.Equal(7, progress.Value);
        Assert.Equal(20, progress.Maximum);
        Assert.Equal("Adding 7 of 20 repositories...", progress.Text);

        // Act: a step that cannot be counted
        operation.SetIndeterminate("Saving...");

        // Assert
        Assert.True(progress.IsIndeterminate);
        Assert.Equal("Saving...", progress.Text);
    }

    /// <summary>
    ///     Test that a cancelable operation offers Cancel, and that the Cancel command runs the cancel
    ///     action once and then hides Cancel.
    /// </summary>
    [Fact]
    public void GlobalProgressViewModel_CancelableOperation_CancelCommandCancelsOnce()
    {
        // Arrange
        var progress = new GlobalProgressViewModel();
        var cancels = 0;
        using var operation = progress.Begin("Adding repositories...", () => cancels++);
        Assert.True(progress.CanCancel);
        Assert.True(progress.CancelCommand.CanExecute(null));

        // Act: Cancel pressed twice
        progress.CancelCommand.Execute(null);
        progress.CancelCommand.Execute(null);

        // Assert: canceled once, Cancel gone, the operation still shown until its owner ends it
        Assert.Equal(1, cancels);
        Assert.False(progress.CanCancel);
        Assert.True(progress.IsActive);
    }

    /// <summary>
    ///     Test that an operation without a cancel action, or one that disallows cancel for a step,
    ///     offers no Cancel.
    /// </summary>
    [Fact]
    public void GlobalProgressViewModel_NotCancelable_CancelCommandDisabled()
    {
        // Arrange
        var progress = new GlobalProgressViewModel();
        var cancels = 0;

        // Act / Assert: no cancel action
        using (progress.Begin("Refreshing repositories..."))
        {
            Assert.False(progress.CanCancel);
            Assert.False(progress.CancelCommand.CanExecute(null));
        }

        // Act / Assert: a cancel action, but the step disallows it
        using var operation = progress.Begin("Adding repositories...", () => cancels++);
        operation.AllowCancel = false;
        Assert.False(progress.CanCancel);
        progress.CancelCommand.Execute(null);
        Assert.Equal(0, cancels);

        // Act / Assert: allowed again afterwards
        operation.AllowCancel = true;
        Assert.True(progress.CanCancel);
    }

    /// <summary>
    ///     Test that overlapping operations show the most recently begun one, and the previous one again
    ///     when it ends.
    /// </summary>
    [Fact]
    public void GlobalProgressViewModel_OverlappingOperations_ShowsLatestThenPrevious()
    {
        // Arrange
        var progress = new GlobalProgressViewModel();
        using var refresh = progress.Begin("Refreshing repositories...");
        var add = progress.Begin("Adding repositories...", () => { });

        // Act: the older operation reports while the newer one is shown
        refresh.Report(1, 3, "Refreshing repositories...");

        // Assert: the newer one is shown
        Assert.Equal("Adding repositories...", progress.Text);
        Assert.True(progress.CanCancel);

        // Act: the newer one ends
        add.Dispose();

        // Assert: the older one is shown again, with its latest state
        Assert.True(progress.IsActive);
        Assert.Equal("Refreshing repositories...", progress.Text);
        Assert.False(progress.IsIndeterminate);
        Assert.Equal(1, progress.Value);
        Assert.False(progress.CanCancel);
    }

    /// <summary>
    ///     Test that the strip goes idle once every operation has ended, and that ending twice or
    ///     reporting after the end changes nothing.
    /// </summary>
    [Fact]
    public void GlobalProgressViewModel_AllEnded_ReturnsToIdle()
    {
        // Arrange
        var progress = new GlobalProgressViewModel();
        var first = progress.Begin("Adding repositories...");
        var second = progress.Begin("Refreshing repositories...");

        // Act
        first.Dispose();
        second.Dispose();
        second.Dispose();
        second.Report(1, 1, "late");

        // Assert
        Assert.False(progress.IsActive);
        Assert.Equal(string.Empty, progress.Text);
        Assert.False(progress.CanCancel);
    }
}
