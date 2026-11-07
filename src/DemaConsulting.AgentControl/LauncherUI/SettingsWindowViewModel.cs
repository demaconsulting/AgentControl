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

using DemaConsulting.AgentControl.AgentPackageManagement;
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
///     when the built <see cref="AppSettings"/> from <see cref="Save"/> is applied. The package
///     source field edits the location of the source named <c>Default</c> (the source a
///     single-folder configuration migrates to) while every other configured source is carried
///     through unchanged; a full multi-source editor is a separate piece of UI work. Not
///     thread-safe; every member is expected to be called from the UI thread.
/// </remarks>
internal sealed class SettingsWindowViewModel : ViewModelBase
{
    /// <summary>
    ///     Invoked by <see cref="Save"/> with the newly-built <see cref="AppSettings"/> to
    ///     persist, typically <see cref="MainWindowViewModel.ApplySettings"/>.
    /// </summary>
    private readonly Action<AppSettings> _onSave;

    /// <summary>
    ///     Working copies of the sources configured when the window opened, saved back (with the
    ///     <c>Default</c> source's location replaced by <see cref="PackageSourcePath"/>) by
    ///     <see cref="Save"/>.
    /// </summary>
    private readonly List<PackageSourceDefinition> _sources;

    private string? _packageSourcePath;
    private string? _gitExecutablePath;
    private AgentToolKind _agentTool;
    private string? _customAgentCommand;
    private string? _shellPreference;

    /// <summary>
    ///     Backing field for <see cref="ErrorMessage"/>.
    /// </summary>
    private string? _errorMessage;

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

        _sources = initial.GetEffectiveSources().Select(source => source.Clone()).ToList();
        _packageSourcePath = FindDefaultSource(_sources)?.Location;
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
    ///     Gets the shell/terminal preference choices offered by the picker, matching the
    ///     executables <c>ShellDetector</c> recognizes for the current OS. The first entry is
    ///     always the blank "auto-detect" option. The combo box remains editable so users can
    ///     still type a custom shell path not listed here.
    /// </summary>
    public static IReadOnlyList<string> AvailableShellPreferences { get; } =
        OperatingSystem.IsWindows()
            ?
            [
                string.Empty,
                "pwsh",
                "powershell",
                "cmd"
            ]
            :
            [
                string.Empty,
                "bash",
                "zsh",
                "sh"
            ];

    /// <summary>
    ///     Gets or sets the location of the <c>Default</c> package source (the filesystem path
    ///     agent package zips are enumerated from); blank/<see langword="null"/> removes the
    ///     <c>Default</c> source when saved.
    /// </summary>
    public string? PackageSourcePath
    {
        get => _packageSourcePath;
        set => SetField(ref _packageSourcePath, value);
    }

    /// <summary>
    ///     Gets the reason the last save attempt was refused, or <see langword="null"/> when there
    ///     is none. Shown in the window so the user can correct the problem.
    /// </summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether <see cref="ErrorMessage"/> has text to show.
    /// </summary>
    public bool HasError => !string.IsNullOrEmpty(_errorMessage);

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
    ///     never owns or edits it. The configured sources are written to
    ///     <see cref="AppSettings.Sources"/> (never the legacy single path): the <c>Default</c>
    ///     source takes <see cref="PackageSourcePath"/> as its location - created as a zip source
    ///     if it did not exist, removed if the path is blank - and all other sources are kept as
    ///     they were.
    /// </remarks>
    private void Save()
    {
        var sources = _sources.Select(source => source.Clone()).ToList();
        ApplyDefaultSourceLocation(sources, PackageSourcePath);

        var updated = new AppSettings
        {
            Sources = sources,
            GitExecutablePath = GitExecutablePath,
            AgentTool = AgentTool,
            CustomAgentCommand = CustomAgentCommand,
            ShellPreference = ShellPreference
        };

        // A credential embedded in a location is refused here, keeping the window open with the
        // reason shown, rather than being allowed to reach the settings file
        var problem = sources.Select(source => source.GetCredentialProblem()).FirstOrDefault(p => p is not null);
        if (problem is not null)
        {
            ErrorMessage = problem;
            return;
        }

        ErrorMessage = null;
        _onSave(updated);
        Saved?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    ///     Finds the source named <c>Default</c>, if any.
    /// </summary>
    /// <param name="sources">The sources to search.</param>
    /// <returns>The <c>Default</c> source (alias compared case-insensitively), or
    ///     <see langword="null"/>.</returns>
    private static PackageSourceDefinition? FindDefaultSource(IEnumerable<PackageSourceDefinition> sources) =>
        sources.FirstOrDefault(source =>
            string.Equals(source.Alias, PackageSourceDefinition.DefaultAlias, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    ///     Applies the single-field package source location to a source list: updates, adds or
    ///     removes the <c>Default</c> source.
    /// </summary>
    /// <param name="sources">The working list to modify.</param>
    /// <param name="location">The new <c>Default</c> location; blank removes the source.</param>
    private static void ApplyDefaultSourceLocation(List<PackageSourceDefinition> sources, string? location)
    {
        var existing = FindDefaultSource(sources);
        if (string.IsNullOrWhiteSpace(location))
        {
            if (existing is not null)
            {
                sources.Remove(existing);
            }

            return;
        }

        if (existing is not null)
        {
            existing.Location = location;
        }
        else
        {
            sources.Insert(0, PackageSourceDefinition.CreateDefaultZip(location));
        }
    }
}
