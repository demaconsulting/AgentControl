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
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.Settings;
using DemaConsulting.AgentControl.Startup;

namespace DemaConsulting.AgentControl;

/// <summary>
///     Avalonia application bootstrap: loads <see cref="AppSettings"/> and shows the launcher's
///     <see cref="MainWindow"/> once the desktop lifetime has finished initializing.
/// </summary>
/// <remarks>
///     <see cref="StartupOptions"/> are attached via the static <see cref="Options"/> property
///     rather than a constructor parameter because Avalonia's <c>AppBuilder.Configure&lt;App&gt;()</c>
///     requires a parameterless constructor to instantiate this type; <see cref="Program"/> sets
///     <see cref="Options"/> immediately after parsing arguments and before calling
///     <c>AppBuilder.StartWithClassicDesktopLifetime</c>. Not thread-safe: Avalonia's
///     lifetime callbacks all run on the UI thread, and <see cref="Options"/> is only ever
///     written once, before the framework starts.
/// </remarks>
internal sealed class App : Application
{
    /// <summary>
    ///     Gets or sets the parsed startup options used to locate the configuration directory
    ///     and (as a first-run fallback) the git/agent-tool test overrides.
    /// </summary>
    /// <remarks>
    ///     <see langword="null"/> means "use all defaults" - the real <c>%APPDATA%\AgentControl\</c>
    ///     configuration directory and no test-only command overrides.
    /// </remarks>
    public static StartupOptions? Options { get; set; }

    /// <inheritdoc />
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        // Only the classic desktop lifetime is supported for v1 (per architecture.md's Windows
        // MSI-first distribution decision); other lifetimes (e.g. browser) are not wired up.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Load settings from the (possibly overridden) configuration directory so the
            // launcher window opens with the user's recent repos and preferences already
            // populated.
            var settings = SettingsStore.Load(Options?.ConfigDirectory);
            var viewModel = new MainWindowViewModel(settings, Options);

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
