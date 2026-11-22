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

using DemaConsulting.AgentControl.GitIntegration;
using DemaConsulting.AgentControl.Utilities;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Tests of the headless test harness itself (<see cref="HeadlessTestApp"/>): its UI thread is
///     guarded, so the headless window tests really do catch a blocking call made there.
/// </summary>
public sealed class HeadlessTestAppTests : IDisposable
{
    /// <summary>
    ///     The temporary folder created by the test, removed on disposal.
    /// </summary>
    private readonly string _tempPath =
        Path.Combine(Path.GetTempPath(), "agentcontrol_headless_guard_" + Guid.NewGuid().ToString("N"));

    /// <summary>
    ///     Test that a git command made on the headless UI thread is refused before git starts and is
    ///     recorded, so the harness fails any headless test that runs git on the UI thread.
    /// </summary>
    [Fact]
    public void HeadlessTestApp_UiThread_GitCallIsRefusedAndRecorded()
    {
        // Arrange: a folder to query, created before the UI thread is entered
        Directory.CreateDirectory(_tempPath);
        var git = new GitClient();
        var token = TestContext.Current.CancellationToken;
        var guarded = false;
        Exception? refusal = null;
        IReadOnlyList<string> recorded = [];

        // Act: query git on the UI thread, then drain the record so the harness does not fail this test
        HeadlessTestApp.Run(() =>
        {
            BlockingCallGuard.TakeViolations();
            guarded = BlockingCallGuard.IsCurrentThreadGuarded;
            refusal = Record.Exception(() => git.IsWorkingTreeClean(_tempPath, token));
            recorded = BlockingCallGuard.TakeViolations();
        });

        // Assert: the UI thread is guarded, and the git call was refused with the guard's exception and
        // recorded once
        Assert.True(guarded);
        Assert.IsType<BlockingCallOnGuardedThreadException>(refusal);
        Assert.Equal(["git status"], recorded);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempPath))
            {
                Directory.Delete(_tempPath, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup of a temporary folder
        }
    }
}
