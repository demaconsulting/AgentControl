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

using DemaConsulting.AgentControl.Utilities;

namespace DemaConsulting.AgentControl.Tests.Utilities;

/// <summary>
///     Tests for the <see cref="BlockingCallGuard"/> class.
/// </summary>
/// <remarks>
///     Every test guards its own thread only inside a <c>using</c> scope around synchronous code (or
///     on a thread it creates), so no guard outlives the test or reaches a thread another test uses.
/// </remarks>
public class BlockingCallGuardTests
{
    /// <summary>
    ///     Test that a blocking call on a thread nobody guarded is let through and not recorded.
    /// </summary>
    [Fact]
    public void BlockingCallGuard_ThrowIfGuarded_UnguardedThread_DoesNothing()
    {
        // Arrange: start from an empty record on an unguarded thread
        BlockingCallGuard.TakeViolations();

        // Act
        BlockingCallGuard.ThrowIfGuarded("git status");

        // Assert: not guarded, nothing recorded
        Assert.False(BlockingCallGuard.IsCurrentThreadGuarded);
        Assert.Empty(BlockingCallGuard.TakeViolations());
    }

    /// <summary>
    ///     Test that a blocking call inside a guard scope is refused with the dedicated exception (not
    ///     an <see cref="InvalidOperationException"/>, which callers catch as an ordinary failure) and is
    ///     recorded.
    /// </summary>
    [Fact]
    public void BlockingCallGuard_ThrowIfGuarded_GuardedScope_ThrowsAndRecords()
    {
        // Arrange
        BlockingCallGuard.TakeViolations();
        Exception? refusal;
        IReadOnlyList<string> recorded;

        // Act: a blocking call inside the scope
        using (BlockingCallGuard.GuardCurrentThread())
        {
            refusal = Record.Exception(() => BlockingCallGuard.ThrowIfGuarded("git status"));
            recorded = BlockingCallGuard.TakeViolations();
        }

        // Assert: refused with the guard's own exception type (exactly; it is not an
        // InvalidOperationException) naming the call, and recorded
        var exception = Assert.IsType<BlockingCallOnGuardedThreadException>(refusal);
        Assert.Contains("git status", exception.Message, StringComparison.Ordinal);
        Assert.Equal(["git status"], recorded);
    }

    /// <summary>
    ///     Test that the thread is no longer guarded once its guard scope is disposed.
    /// </summary>
    [Fact]
    public void BlockingCallGuard_GuardCurrentThread_ScopeDisposed_ThreadNoLongerGuarded()
    {
        // Arrange
        var scope = BlockingCallGuard.GuardCurrentThread();
        Assert.True(BlockingCallGuard.IsCurrentThreadGuarded);

        // Act
        scope.Dispose();

        // Assert: blocking calls are let through again
        Assert.False(BlockingCallGuard.IsCurrentThreadGuarded);
        BlockingCallGuard.ThrowIfGuarded("git status");
    }

    /// <summary>
    ///     Test that nested guard scopes keep the thread guarded until the outermost one is disposed,
    ///     and that disposing a scope twice does not end an outer one.
    /// </summary>
    [Fact]
    public void BlockingCallGuard_GuardCurrentThread_NestedScopes_GuardUntilOutermostDisposed()
    {
        // Arrange: two nested scopes
        var outer = BlockingCallGuard.GuardCurrentThread();
        var inner = BlockingCallGuard.GuardCurrentThread();

        // Act: dispose the inner one twice
        inner.Dispose();
        inner.Dispose();
        var guardedAfterInner = BlockingCallGuard.IsCurrentThreadGuarded;
        outer.Dispose();

        // Assert: still guarded after the inner scope, free after the outer one
        Assert.True(guardedAfterInner);
        Assert.False(BlockingCallGuard.IsCurrentThreadGuarded);
    }

    /// <summary>
    ///     Test that taking the record returns every refusal in order and leaves the record empty.
    /// </summary>
    [Fact]
    public void BlockingCallGuard_TakeViolations_DrainsRecord()
    {
        // Arrange: two refusals
        BlockingCallGuard.TakeViolations();
        using (BlockingCallGuard.GuardCurrentThread())
        {
            Assert.Throws<BlockingCallOnGuardedThreadException>(() => BlockingCallGuard.ThrowIfGuarded("git status"));
            Assert.Throws<BlockingCallOnGuardedThreadException>(() => BlockingCallGuard.ThrowIfGuarded("read pin file"));
        }

        // Act
        var first = BlockingCallGuard.TakeViolations();
        var second = BlockingCallGuard.TakeViolations();

        // Assert
        Assert.Equal(["git status", "read pin file"], first);
        Assert.Empty(second);
    }

    /// <summary>
    ///     Test that guarding one thread does not guard another: a worker thread running while the
    ///     test's thread is guarded may make blocking calls.
    /// </summary>
    [Fact]
    public void BlockingCallGuard_GuardCurrentThread_OtherThread_NotGuarded()
    {
        // Arrange: a worker thread that waits until this thread is guarded, then checks itself
        using var guarded = new ManualResetEventSlim();
        var workerGuarded = true;
        Exception? workerRefusal = null;
        var worker = new Thread(() =>
        {
            guarded.Wait();
            workerGuarded = BlockingCallGuard.IsCurrentThreadGuarded;
            workerRefusal = Record.Exception(() => BlockingCallGuard.ThrowIfGuarded("git status"));
        });
        worker.Start();

        // Act: guard this thread while the worker checks itself
        using (BlockingCallGuard.GuardCurrentThread())
        {
            guarded.Set();
            worker.Join();
        }

        // Assert: the worker was neither guarded nor refused
        Assert.False(workerGuarded);
        Assert.Null(workerRefusal);
    }

    /// <summary>
    ///     Test that a thread guarded for its lifetime stays guarded with no scope open, so a harness can
    ///     guard the single UI thread it owns once.
    /// </summary>
    [Fact]
    public void BlockingCallGuard_MarkCurrentThreadGuarded_DedicatedThread_StaysGuarded()
    {
        // Arrange: a dedicated thread that no other test uses
        Exception? refusal = null;
        var guardedAfterMark = false;
        IReadOnlyList<string> recorded = [];
        var thread = new Thread(() =>
        {
            // Act: guard it for its lifetime, then make a blocking call on it
            BlockingCallGuard.MarkCurrentThreadGuarded();
            guardedAfterMark = BlockingCallGuard.IsCurrentThreadGuarded;
            refusal = Record.Exception(() => BlockingCallGuard.ThrowIfGuarded("git status"));
            recorded = BlockingCallGuard.TakeViolations();
        });
        thread.Start();
        thread.Join();

        // Assert: guarded, refused and recorded; this test's own thread is not guarded
        Assert.True(guardedAfterMark);
        Assert.IsType<BlockingCallOnGuardedThreadException>(refusal);
        Assert.Equal(["git status"], recorded);
        Assert.False(BlockingCallGuard.IsCurrentThreadGuarded);
    }
}
