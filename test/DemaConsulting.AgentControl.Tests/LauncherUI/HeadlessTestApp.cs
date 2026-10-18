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

using Avalonia;
using Avalonia.Headless;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Bootstraps the real AgentControl <c>App</c> (theme and styles included) on Avalonia's
///     headless platform so window code-behind can be exercised by ordinary <c>[Fact]</c> tests
///     through <see cref="Run(Action)"/> / <see cref="RunAsync(Func{Task})"/>.
/// </summary>
/// <remarks>
///     Avalonia allows only one application per process, so a single shared session (and UI
///     thread) is created lazily and reused by every headless test.
/// </remarks>
public static class HeadlessTestApp
{
    private static readonly Lazy<HeadlessUnitTestSession> Session =
        new(() => HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApp)));

    /// <summary>
    ///     Builds the headless Avalonia application; discovered by name by the test session.
    /// </summary>
    /// <returns>The configured <see cref="AppBuilder"/>.</returns>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());

    /// <summary>
    ///     Runs <paramref name="action"/> on the headless UI thread and waits for it to finish.
    /// </summary>
    /// <param name="action">The UI test body.</param>
    public static void Run(Action action) => Session.Value.Dispatch(action, CancellationToken.None);

    /// <summary>
    ///     Runs <paramref name="action"/> on the headless UI thread and waits for it to finish.
    /// </summary>
    /// <param name="action">The asynchronous UI test body.</param>
    public static void RunAsync(Func<Task> action) =>
        Session.Value.Dispatch(async () =>
        {
            await action();
            return 0;
        }, CancellationToken.None);
}
