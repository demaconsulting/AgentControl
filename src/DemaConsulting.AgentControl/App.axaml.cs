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
using Avalonia.Styling;
using DemaConsulting.AgentControl.GitIntegration;
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.PackageCredentials;
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

            // Apply the saved theme before any window exists, so the first frame is already in
            // the user's theme (the system default follows the operating system, live)
            ApplyTheme(settings.Theme);

            // The session credential store holds what the user types into the sign-in dialog, in
            // memory only; it is emptied when the application exits. A sign-in can only outlive
            // the session in the user's own Git credential helper, reached through the git
            // executable the user configured (the setting, else the startup override, else "git")
            var credentialStore = new SessionCredentialStore();
            MainWindowViewModel? viewModel = null;
            var rememberedCredentials = new GitCredentialClient(
                () => ResolveGitExecutablePath(viewModel?.Settings ?? settings, Options));
            var credentials = new PackageCredentialContext(
                credentialStore, new AvaloniaCredentialPrompt(), rememberedCredentials);
            viewModel = new MainWindowViewModel(
                settings, Options, Options?.ConfigDirectory, credentials, applyTheme: ApplyTheme);
            desktop.Exit += (_, _) => credentialStore.Clear();

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    ///     Maps the user's theme choice to the Avalonia theme variant that implements it.
    /// </summary>
    /// <param name="preference">The user's choice.</param>
    /// <returns><see cref="ThemeVariant.Default"/> for <see cref="ThemePreference.System"/> (Avalonia
    ///     then follows the operating system's setting, including while the application runs),
    ///     <see cref="ThemeVariant.Light"/> or <see cref="ThemeVariant.Dark"/> otherwise; the system
    ///     default for a value this method does not know.</returns>
    internal static ThemeVariant ToThemeVariant(ThemePreference preference) => preference switch
    {
        ThemePreference.Light => ThemeVariant.Light,
        ThemePreference.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default
    };

    /// <summary>
    ///     Applies the user's theme choice to the running application, so every open and future
    ///     window changes at once without a restart.
    /// </summary>
    /// <param name="preference">The user's choice.</param>
    /// <remarks>
    ///     Does nothing when there is no current application (for example in a unit test that
    ///     never created one). Must be called on the UI thread.
    /// </remarks>
    internal static void ApplyTheme(ThemePreference preference)
    {
        if (Current is { } application)
        {
            application.RequestedThemeVariant = ToThemeVariant(preference);
        }
    }

    /// <summary>
    ///     Resolves the git executable to run: the per-user setting if configured, otherwise the
    ///     startup-time override, otherwise <c>"git"</c> (found through <c>PATH</c>) - the same
    ///     order every other git call in the application uses.
    /// </summary>
    /// <param name="settings">The current application settings.</param>
    /// <param name="options">The startup options, or <see langword="null"/> for none.</param>
    /// <returns>The git executable path or command name.</returns>
    private static string ResolveGitExecutablePath(AppSettings settings, StartupOptions? options) =>
        !string.IsNullOrWhiteSpace(settings.GitExecutablePath)
            ? settings.GitExecutablePath
            : options?.GitExecutableOverride ?? "git";
}
