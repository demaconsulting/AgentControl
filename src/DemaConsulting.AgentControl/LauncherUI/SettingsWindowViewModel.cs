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

using DemaConsulting.AgentControl.Settings;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     View model for the settings window: edits a working copy of <see cref="AppSettings"/>'s
///     user-configurable fields and persists them on <see cref="Save"/>.
/// </summary>
/// <remarks>
///     Edits a copy rather than the live <see cref="AppSettings"/> instance so that closing the
///     window without saving (not currently exposed in the UI, but a natural future "Cancel"
///     button) never partially mutates the caller's settings. The recent-repos list is
///     intentionally not exposed here - it is owned exclusively by
///     <see cref="MainWindowViewModel"/> and is preserved by <see cref="MainWindowViewModel.ApplySettings"/>
///     when the built <see cref="AppSettings"/> from <see cref="Save"/> is applied. Not
///     thread-safe; every member is expected to be called from the UI thread.
/// </remarks>
internal sealed class SettingsWindowViewModel : ViewModelBase
{
    /// <summary>
    ///     Invoked by <see cref="Save"/> with the newly-built <see cref="AppSettings"/> to
    ///     persist, typically <see cref="MainWindowViewModel.ApplySettings"/>.
    /// </summary>
    private readonly Action<AppSettings> _onSave;

    private string? _packageSourcePath;
    private string? _gitExecutablePath;
    private AgentToolKind _agentTool;
    private string? _customAgentCommand;
    private string? _shellPreference;

    /// <summary>
    ///     Initializes a new <see cref="SettingsWindowViewModel"/>, seeding its editable
    ///     properties from <paramref name="initial"/>.
    /// </summary>
    /// <param name="initial">The current application settings to edit a copy of.</param>
    /// <param name="onSave">Callback invoked by <see cref="Save"/> with the updated settings.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="initial"/> or
    ///     <paramref name="onSave"/> is <see langword="null"/>.</exception>
    public SettingsWindowViewModel(AppSettings initial, Action<AppSettings> onSave)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(onSave);

        _packageSourcePath = initial.PackageSourcePath;
        _gitExecutablePath = initial.GitExecutablePath;
        _agentTool = initial.AgentTool;
        _customAgentCommand = initial.CustomAgentCommand;
        _shellPreference = initial.ShellPreference;
        _onSave = onSave;

        SaveCommand = new RelayCommand(Save);
    }

    /// <summary>
    ///     Gets the agent tool choices offered by the picker, in the order defined by
    ///     architecture.md's "Configurable agent tool and shell" decision.
    /// </summary>
    public static IReadOnlyList<AgentToolKind> AvailableAgentTools { get; } =
    [
        AgentToolKind.CopilotCli,
        AgentToolKind.Cursor,
        AgentToolKind.ClaudeCode,
        AgentToolKind.Custom
    ];

    /// <summary>
    ///     Gets or sets the filesystem path agent package zips are enumerated from.
    /// </summary>
    public string? PackageSourcePath
    {
        get => _packageSourcePath;
        set => SetField(ref _packageSourcePath, value);
    }

    /// <summary>
    ///     Gets or sets the git executable path override; blank/<see langword="null"/> means
    ///     resolve <c>git</c> from <c>PATH</c>.
    /// </summary>
    public string? GitExecutablePath
    {
        get => _gitExecutablePath;
        set => SetField(ref _gitExecutablePath, value);
    }

    /// <summary>
    ///     Gets or sets which agentic CLI tool to launch.
    /// </summary>
    public AgentToolKind AgentTool
    {
        get => _agentTool;
        set
        {
            if (SetField(ref _agentTool, value))
            {
                OnPropertyChanged(nameof(IsCustomAgentToolSelected));
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether the custom-command textbox should be shown, i.e.
    ///     whether <see cref="AgentTool"/> is currently <see cref="AgentToolKind.Custom"/>.
    /// </summary>
    public bool IsCustomAgentToolSelected => AgentTool == AgentToolKind.Custom;

    /// <summary>
    ///     Gets or sets the custom command line used when <see cref="AgentTool"/> is
    ///     <see cref="AgentToolKind.Custom"/>.
    /// </summary>
    public string? CustomAgentCommand
    {
        get => _customAgentCommand;
        set => SetField(ref _customAgentCommand, value);
    }

    /// <summary>
    ///     Gets or sets the user's shell/terminal preference; blank/<see langword="null"/> means
    ///     let <c>ShellDetector</c> auto-detect the best available shell.
    /// </summary>
    public string? ShellPreference
    {
        get => _shellPreference;
        set => SetField(ref _shellPreference, value);
    }

    /// <summary>
    ///     Gets the command bound to the window's "Save" button.
    /// </summary>
    public RelayCommand SaveCommand { get; }

    /// <summary>
    ///     Raised after <see cref="Save"/> has invoked the <c>onSave</c> callback, so
    ///     <see cref="SettingsWindow"/>'s code-behind can close the window.
    /// </summary>
    public event EventHandler? Saved;

    /// <summary>
    ///     Builds an <see cref="AppSettings"/> from the current editable properties and invokes
    ///     the <c>onSave</c> callback supplied at construction.
    /// </summary>
    /// <remarks>
    ///     The returned settings' <see cref="AppSettings.RecentRepos"/> is left as a fresh empty
    ///     list; callers (specifically <see cref="MainWindowViewModel.ApplySettings"/>) are
    ///     responsible for preserving the authoritative recent-repos list, since this view model
    ///     never owns or edits it.
    /// </remarks>
    private void Save()
    {
        var updated = new AppSettings
        {
            PackageSourcePath = PackageSourcePath,
            GitExecutablePath = GitExecutablePath,
            AgentTool = AgentTool,
            CustomAgentCommand = CustomAgentCommand,
            ShellPreference = ShellPreference
        };

        _onSave(updated);
        Saved?.Invoke(this, EventArgs.Empty);
    }
}
