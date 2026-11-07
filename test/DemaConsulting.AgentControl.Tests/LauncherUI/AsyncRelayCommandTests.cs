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
///     Unit tests for <see cref="AsyncRelayCommand"/>, the command that runs the view models'
///     asynchronous, cancellable operations without blocking the UI thread.
/// </summary>
public class AsyncRelayCommandTests
{
    /// <summary>
    ///     Test that a run reports itself as running, disables the command meanwhile and raises the
    ///     change notifications, then returns to idle.
    /// </summary>
    [Fact]
    public async Task AsyncRelayCommand_ExecuteAsync_Running_ReportsRunningAndDisablesCommand()
    {
        // Arrange: an operation held open
        var release = new TaskCompletionSource();
        var command = new AsyncRelayCommand(_ => release.Task, _ => { });
        var canExecuteChanged = 0;
        var runningChanged = 0;
        command.CanExecuteChanged += (_, _) => canExecuteChanged++;
        command.IsRunningChanged += (_, _) => runningChanged++;
        Assert.True(command.CanExecute(null));

        // Act: start without waiting
        var running = command.ExecuteAsync();

        // Assert: the call returned while the operation is pending
        Assert.False(running.IsCompleted);
        Assert.True(command.IsRunning);
        Assert.False(command.CanExecute(null));

        // Act: the operation finishes
        release.SetResult();
        await running;

        // Assert
        Assert.False(command.IsRunning);
        Assert.True(command.CanExecute(null));
        Assert.Equal(2, runningChanged);
        Assert.Equal(2, canExecuteChanged);
    }

    /// <summary>
    ///     Test that activating the command again while it runs does not start the operation a
    ///     second time.
    /// </summary>
    [Fact]
    public async Task AsyncRelayCommand_ExecuteAsync_WhileRunning_DoesNotRunOperationTwice()
    {
        // Arrange
        var release = new TaskCompletionSource();
        var runs = 0;
        var command = new AsyncRelayCommand(_ =>
        {
            runs++;
            return release.Task;
        }, _ => { });

        // Act: two activations while the first is still running
        var first = command.ExecuteAsync();
        await command.ExecuteAsync();
        command.Execute(null);
        release.SetResult();
        await first;

        // Assert
        Assert.Equal(1, runs);
    }

    /// <summary>
    ///     Test that cancelling signals the token the operation received, and that the resulting
    ///     cancellation is not reported as an error.
    /// </summary>
    [Fact]
    public async Task AsyncRelayCommand_Cancel_Running_SignalsTokenAndIsNotAnError()
    {
        // Arrange: an operation that waits for cancellation
        var errors = new List<Exception>();
        var command = new AsyncRelayCommand(token => Task.Delay(Timeout.Infinite, token), errors.Add);

        // Act
        var running = command.ExecuteAsync();
        command.Cancel();
        await running;

        // Assert
        Assert.Empty(errors);
        Assert.False(command.IsRunning);
    }

    /// <summary>
    ///     Test that cancelling when nothing runs does nothing.
    /// </summary>
    [Fact]
    public void AsyncRelayCommand_Cancel_NotRunning_DoesNothing()
    {
        // Arrange
        var command = new AsyncRelayCommand(_ => Task.CompletedTask, _ => { });

        // Act
        var exception = Record.Exception(command.Cancel);

        // Assert
        Assert.Null(exception);
        Assert.False(command.IsRunning);
    }

    /// <summary>
    ///     Test that an exception thrown by the operation is passed to the error callback instead
    ///     of escaping, and that the command can run again afterwards.
    /// </summary>
    [Fact]
    public async Task AsyncRelayCommand_ExecuteAsync_OperationThrows_ReportsErrorAndCanRunAgain()
    {
        // Arrange
        var errors = new List<Exception>();
        var runs = 0;
        var command = new AsyncRelayCommand(_ =>
        {
            runs++;
            throw new InvalidOperationException("boom");
        }, errors.Add);

        // Act
        await command.ExecuteAsync();
        await command.ExecuteAsync();

        // Assert
        Assert.Equal(2, runs);
        Assert.Equal(2, errors.Count);
        Assert.All(errors, error => Assert.Equal("boom", error.Message));
        Assert.True(command.CanExecute(null));
    }

    /// <summary>
    ///     Test that the eligibility predicate decides CanExecute and that raising the change
    ///     notification lets bound controls re-evaluate it.
    /// </summary>
    [Fact]
    public void AsyncRelayCommand_CanExecute_Predicate_DecidesEligibility()
    {
        // Arrange
        var eligible = false;
        var command = new AsyncRelayCommand(_ => Task.CompletedTask, _ => { }, () => eligible);
        var raised = 0;
        command.CanExecuteChanged += (_, _) => raised++;
        Assert.False(command.CanExecute(null));

        // Act
        eligible = true;
        command.RaiseCanExecuteChanged();

        // Assert
        Assert.True(command.CanExecute(null));
        Assert.Equal(1, raised);
    }

    /// <summary>
    ///     Test that activating the command the way a bound control does (ICommand.Execute) starts
    ///     the operation without waiting for it.
    /// </summary>
    [Fact]
    public async Task AsyncRelayCommand_Execute_FromControl_StartsOperationWithoutWaiting()
    {
        // Arrange
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var command = new AsyncRelayCommand(async _ =>
        {
            started.SetResult();
            await release.Task;
        }, _ => { });

        // Act
        command.Execute(null);
        await started.Task;

        // Assert: running, and Execute already returned
        Assert.True(command.IsRunning);

        // Cleanup: let the operation finish
        release.SetResult();
        await Task.Run(
            async () =>
            {
                while (command.IsRunning)
                {
                    await Task.Delay(10, TestContext.Current.CancellationToken);
                }
            },
            TestContext.Current.CancellationToken);
        Assert.False(command.IsRunning);
    }

    /// <summary>
    ///     Test that the constructor rejects a missing operation or error callback.
    /// </summary>
    [Fact]
    public void AsyncRelayCommand_Constructor_NullArguments_ThrowsArgumentNullException()
    {
        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => new AsyncRelayCommand(null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => new AsyncRelayCommand(_ => Task.CompletedTask, null!));
    }
}
