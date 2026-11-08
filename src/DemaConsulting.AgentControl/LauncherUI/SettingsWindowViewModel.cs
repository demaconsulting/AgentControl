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

using System.Collections.ObjectModel;
using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.PackageCredentials;
using DemaConsulting.AgentControl.Settings;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     View model for the settings window: edits a working copy of <see cref="AppSettings"/>'s
///     user-configurable fields, including the list of package sources, and persists them on
///     <see cref="Save"/>.
/// </summary>
/// <remarks>
///     <para>
///     Edits a copy rather than the live <see cref="AppSettings"/> instance so that closing the
///     window without saving never partially mutates the caller's settings. The recent-repos list
///     is intentionally not exposed here - it is owned exclusively by
///     <see cref="MainWindowViewModel"/> and is preserved by <see cref="MainWindowViewModel.ApplySettings"/>
///     when the built <see cref="AppSettings"/> from <see cref="Save"/> is applied; it is only
///     read, to tell the user how many repos are pinned to a source they are about to remove.
///     </para>
///     <para>
///     The package sources are shown as a list with add, edit and remove actions. The dialogs
///     that add or edit a source are view-layer concerns: this class raises
///     <see cref="EditorRequested"/> and <see cref="RemoveRequested"/> and applies the outcome,
///     so the logic stays testable without a window. Whether each source's folder can be found
///     is checked on worker threads so a slow network share never blocks the window. Not
///     thread-safe; every member is expected to be called from the UI thread.
///     </para>
/// </remarks>
internal sealed class SettingsWindowViewModel : ViewModelBase, IDisposable
{
    /// <summary>
    ///     Invoked by <see cref="Save"/> with the newly-built <see cref="AppSettings"/> to
    ///     persist, typically <see cref="MainWindowViewModel.ApplySettings"/>.
    /// </summary>
    private readonly Action<AppSettings> _onSave;

    /// <summary>
    ///     Tells whether a folder exists; run on a worker thread for each source's status check.
    /// </summary>
    private readonly Func<string, CancellationToken, Task<bool>> _folderExists;

    /// <summary>
    ///     Creates the provider the editor's "Test connection" uses for a server source;
    ///     <see langword="null"/> lets the editor create a plain one.
    /// </summary>
    private readonly Func<PackageSourceDefinition, IPackageSourceProvider>? _providerFactory;

    /// <summary>
    ///     The credential facilities passed to the server editor for "Forget saved sign-in", or
    ///     <see langword="null"/> when there are none.
    /// </summary>
    private readonly PackageCredentialContext? _credentials;

    /// <summary>
    ///     How many recent repos are pinned to each source alias (compared case-insensitively),
    ///     captured when the window opened.
    /// </summary>
    private readonly Dictionary<string, int> _pinnedRepoCounts;

    /// <summary>
    ///     Cancels the folder checks still running when the window closes.
    /// </summary>
    private readonly CancellationTokenSource _statusCancellation = new();

    /// <summary>
    ///     The folder checks started so far, so a caller can wait for them to settle.
    /// </summary>
    private readonly List<Task> _statusChecks = [];

    /// <summary>
    ///     Backing field for <see cref="SelectedSource"/>.
    /// </summary>
    private PackageSourceItemViewModel? _selectedSource;

    /// <summary>
    ///     Backing field for <see cref="GitExecutablePath"/>.
    /// </summary>
    private string? _gitExecutablePath;

    /// <summary>
    ///     Backing field for <see cref="AgentTool"/>.
    /// </summary>
    private AgentToolKind _agentTool;

    /// <summary>
    ///     Backing field for <see cref="CustomAgentCommand"/>.
    /// </summary>
    private string? _customAgentCommand;

    /// <summary>
    ///     Backing field for <see cref="ShellPreference"/>.
    /// </summary>
    private string? _shellPreference;

    /// <summary>
    ///     Backing field for <see cref="Theme"/>.
    /// </summary>
    private ThemePreference _theme;

    /// <summary>
    ///     Backing field for <see cref="ErrorMessage"/>.
    /// </summary>
    private string? _errorMessage;

    /// <summary>
    ///     Initializes a new <see cref="SettingsWindowViewModel"/>, seeding its editable
    ///     properties from <paramref name="initial"/> and starting the status check of every
    ///     configured source.
    /// </summary>
    /// <param name="initial">The current application settings to edit a copy of.</param>
    /// <param name="onSave">Callback invoked by <see cref="Save"/> with the updated settings.</param>
    /// <param name="folderExists">Tells whether a folder exists, for the sources' status check;
    ///     <see langword="null"/> checks the real file system. Supplied so tests need no real
    ///     folders and the check can be made slow on purpose.</param>
    /// <param name="providerFactory">Creates the provider the server editor's "Test connection"
    ///     uses (so the application can give it the session credentials); <see langword="null"/>
    ///     uses a plain one. Supplied so tests can use a fake.</param>
    /// <param name="credentials">The credential facilities the server editor's "Forget saved
    ///     sign-in" uses, or <see langword="null"/> when there are none.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="initial"/> or
    ///     <paramref name="onSave"/> is <see langword="null"/>.</exception>
    public SettingsWindowViewModel(
        AppSettings initial,
        Action<AppSettings> onSave,
        Func<string, CancellationToken, Task<bool>>? folderExists = null,
        Func<PackageSourceDefinition, IPackageSourceProvider>? providerFactory = null,
        PackageCredentialContext? credentials = null)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(onSave);

        _folderExists = folderExists ?? ((path, token) => Task.Run(() => Directory.Exists(path), token));
        _providerFactory = providerFactory;
        _credentials = credentials;
        _pinnedRepoCounts = initial.RecentRepos
            .Where(repo => !string.IsNullOrEmpty(repo.PinnedSource))
            .GroupBy(repo => repo.PinnedSource!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        _gitExecutablePath = initial.GitExecutablePath;
        _agentTool = initial.AgentTool;
        _customAgentCommand = initial.CustomAgentCommand;
        _shellPreference = initial.ShellPreference;
        _theme = initial.Theme;
        _onSave = onSave;

        Sources = [];
        foreach (var source in initial.GetEffectiveSources())
        {
            AddItem(source.Clone());
        }

        SaveCommand = new RelayCommand(Save);
        AddZipSourceCommand = new RelayCommand(() => RequestEditor(PackageSourceType.Zip, null));
        AddNuGetFolderSourceCommand = new RelayCommand(() => RequestEditor(PackageSourceType.NuGet, null));
        AddNuGetServerSourceCommand = new RelayCommand(() => RequestEditor(PackageSourceType.NuGet, null, server: true));
        EditSourceCommand = new RelayCommand(
            () => RequestEditor(_selectedSource!.Definition.Type, _selectedSource), () => _selectedSource is not null);
        RemoveSourceCommand = new RelayCommand(
            () => RemoveRequested?.Invoke(this, _selectedSource!), () => _selectedSource is not null);
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
    ///     Gets the appearance choices offered by the picker: follow the system first (the
    ///     default), then light and dark.
    /// </summary>
    public static IReadOnlyList<ThemePreference> AvailableThemes { get; } =
    [
        ThemePreference.System,
        ThemePreference.Light,
        ThemePreference.Dark
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
    ///     Gets the configured package sources, in the order they are saved and listed.
    /// </summary>
    public ObservableCollection<PackageSourceItemViewModel> Sources { get; }

    /// <summary>
    ///     Gets a value indicating whether no package source is configured, which the window
    ///     answers with an explanation instead of an empty list.
    /// </summary>
    public bool HasNoSources => Sources.Count == 0;

    /// <summary>
    ///     Gets or sets the source selected in the list, the one the Edit and Remove buttons act
    ///     on.
    /// </summary>
    public PackageSourceItemViewModel? SelectedSource
    {
        get => _selectedSource;
        set
        {
            if (SetField(ref _selectedSource, value))
            {
                EditSourceCommand.RaiseCanExecuteChanged();
                RemoveSourceCommand.RaiseCanExecuteChanged();
            }
        }
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
    ///     Gets or sets the color theme: follow the system (the default), light or dark. Takes
    ///     effect when the settings are saved.
    /// </summary>
    public ThemePreference Theme
    {
        get => _theme;
        set => SetField(ref _theme, value);
    }

    /// <summary>
    ///     Gets the command bound to the window's "Save" button.
    /// </summary>
    public RelayCommand SaveCommand { get; }

    /// <summary>
    ///     Gets the command bound to the "Add Zip-Package Folder" button.
    /// </summary>
    public RelayCommand AddZipSourceCommand { get; }

    /// <summary>
    ///     Gets the command bound to the "Add NuGet-Package Folder" button.
    /// </summary>
    public RelayCommand AddNuGetFolderSourceCommand { get; }

    /// <summary>
    ///     Gets the command bound to the "Add NuGet-Package Server" button.
    /// </summary>
    public RelayCommand AddNuGetServerSourceCommand { get; }

    /// <summary>
    ///     Gets the command bound to the "Edit" button; executable while a source is selected.
    /// </summary>
    public RelayCommand EditSourceCommand { get; }

    /// <summary>
    ///     Gets the command bound to the "Remove" button; executable while a source is selected.
    /// </summary>
    public RelayCommand RemoveSourceCommand { get; }

    /// <summary>
    ///     Raised after <see cref="Save"/> has invoked the <c>onSave</c> callback, so
    ///     <see cref="SettingsWindow"/>'s code-behind can close the window.
    /// </summary>
    public event EventHandler? Saved;

    /// <summary>
    ///     Raised when the user asks to add or edit a source, carrying the editor view model the
    ///     view should show in a dialog. The outcome is applied by this view model when the
    ///     editor reports a save; the view only shows the dialog.
    /// </summary>
    public event EventHandler<SourceEditorViewModel>? EditorRequested;

    /// <summary>
    ///     Raised when the user asks to remove a source, carrying the row. The view confirms with
    ///     the user (see <see cref="GetRemovalWarning"/>) and calls <see cref="RemoveSource"/>
    ///     only if they agree.
    /// </summary>
    public event EventHandler<PackageSourceItemViewModel>? RemoveRequested;

    /// <summary>
    ///     Creates the editor for adding a new source of a kind, or for editing an existing one.
    /// </summary>
    /// <param name="type">The kind of source to add; ignored when <paramref name="editing"/> is
    ///     supplied.</param>
    /// <param name="editing">The row to edit, or <see langword="null"/> to add a new source.</param>
    /// <param name="server">Whether a new NuGet source is a server rather than a folder; ignored
    ///     when <paramref name="editing"/> is supplied.</param>
    /// <returns>An editor whose save adds the new source (selecting it) or replaces the edited one
    ///     in place; its alias check covers every source in the list.</returns>
    public SourceEditorViewModel CreateEditor(
        PackageSourceType type, PackageSourceItemViewModel? editing, bool server = false)
    {
        var editor = new SourceEditorViewModel(
            type,
            editing?.Definition,
            alias => Sources.Any(item => string.Equals(item.Alias, alias, StringComparison.OrdinalIgnoreCase)),
            server,
            _providerFactory,
            _credentials);
        editor.Saved += (_, definition) =>
        {
            if (editing is null)
            {
                AddSource(definition);
            }
            else
            {
                ReplaceSource(editing, definition);
            }
        };
        return editor;
    }

    /// <summary>
    ///     Describes what removing a source will cause, for the confirmation the user sees before
    ///     it is removed.
    /// </summary>
    /// <param name="source">The row about to be removed.</param>
    /// <returns>A question naming the source, with a warning when repos are pinned to it: they
    ///     will show a package source problem until a source with that alias is configured
    ///     again.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is
    ///     <see langword="null"/>.</exception>
    public static string GetRemovalWarning(PackageSourceItemViewModel source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var message = $"Remove the package source '{source.Alias}'?\n\n" +
                      "Nothing is deleted from the source itself; AgentControl only forgets it.";
        if (source.PinnedRepoCount > 0)
        {
            var repos = source.PinnedRepoCount == 1 ? "1 repo is" : $"{source.PinnedRepoCount} repos are";
            message += $"\n\nWarning: {repos} pinned to '{source.Alias}'. Until a source with that " +
                       "alias is configured again, those repos will show a \"Package source problem\" " +
                       "and cannot be upgraded.";
        }
        else
        {
            message += "\n\nRepos pinned to this alias will show a \"Package source problem\" until a " +
                       "source with that alias is configured again.";
        }

        return message;
    }

    /// <summary>
    ///     Removes a source from the working list; nothing is persisted until <see cref="Save"/>.
    /// </summary>
    /// <param name="source">The row to remove.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is
    ///     <see langword="null"/>.</exception>
    public void RemoveSource(PackageSourceItemViewModel source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!Sources.Remove(source))
        {
            return;
        }

        if (ReferenceEquals(_selectedSource, source))
        {
            SelectedSource = null;
        }

        OnPropertyChanged(nameof(HasNoSources));
    }

    /// <summary>
    ///     Cancels the folder checks that are still running and releases the resources they use,
    ///     for when the window closes.
    /// </summary>
    public void Dispose()
    {
        _statusCancellation.Cancel();
        _statusCancellation.Dispose();
    }

    /// <summary>
    ///     Waits for every folder check started so far to finish.
    /// </summary>
    /// <returns>A task that completes when the checks have finished or were cancelled.</returns>
    internal Task WaitForStatusChecksAsync() => Task.WhenAll(_statusChecks.ToArray());

    /// <summary>
    ///     Builds an <see cref="AppSettings"/> from the current editable properties and invokes
    ///     the <c>onSave</c> callback supplied at construction.
    /// </summary>
    /// <remarks>
    ///     The returned settings' <see cref="AppSettings.RecentRepos"/> is left as a fresh empty
    ///     list; callers (specifically <see cref="MainWindowViewModel.ApplySettings"/>) are
    ///     responsible for preserving the authoritative recent-repos list, since this view model
    ///     never owns or edits it. The configured sources are written to
    ///     <see cref="AppSettings.Sources"/> (never the legacy single path) in the order listed.
    ///     A source whose location embeds a credential is refused here, keeping the window open
    ///     with the reason shown, rather than being allowed to reach the settings file. When the
    ///     callback rejects the settings (<see cref="InvalidOperationException"/>, for example a
    ///     settings file that cannot be written) the window stays open with the redacted reason and
    ///     <see cref="Saved"/> is not raised.
    /// </remarks>
    private void Save()
    {
        var sources = Sources.Select(item => item.Definition.Clone()).ToList();
        var updated = new AppSettings
        {
            Sources = sources,
            GitExecutablePath = GitExecutablePath,
            AgentTool = AgentTool,
            CustomAgentCommand = CustomAgentCommand,
            ShellPreference = ShellPreference,
            Theme = Theme
        };

        var problem = sources.Select(source => source.GetCredentialProblem()).FirstOrDefault(p => p is not null);
        if (problem is not null)
        {
            ErrorMessage = problem;
            return;
        }

        ErrorMessage = null;
        try
        {
            _onSave(updated);
        }
        catch (InvalidOperationException ex)
        {
            // A rejected source or a settings file that cannot be written keeps the window open
            // with the reason shown, and nothing is reported as saved
            ErrorMessage = SecretRedactor.SafeMessage(ex);
            return;
        }

        Saved?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    ///     Raises <see cref="EditorRequested"/> with an editor for a new or an existing source.
    /// </summary>
    /// <param name="type">The kind of source to add.</param>
    /// <param name="editing">The row to edit, or <see langword="null"/> to add.</param>
    /// <param name="server">Whether a new NuGet source is a server.</param>
    private void RequestEditor(PackageSourceType type, PackageSourceItemViewModel? editing, bool server = false) =>
        EditorRequested?.Invoke(this, CreateEditor(type, editing, server));

    /// <summary>
    ///     Appends a source to the working list, selects it and starts checking its folder.
    /// </summary>
    /// <param name="definition">The new source.</param>
    private void AddSource(PackageSourceDefinition definition)
    {
        var item = AddItem(definition);
        SelectedSource = item;
    }

    /// <summary>
    ///     Replaces an edited source in place (keeping its position and selection) and restarts
    ///     the check of its folder.
    /// </summary>
    /// <param name="editing">The row being replaced.</param>
    /// <param name="definition">The edited source.</param>
    private void ReplaceSource(PackageSourceItemViewModel editing, PackageSourceDefinition definition)
    {
        var index = Sources.IndexOf(editing);
        if (index < 0)
        {
            return;
        }

        var wasSelected = ReferenceEquals(_selectedSource, editing);
        var replacement = CreateItem(definition);
        Sources[index] = replacement;
        if (wasSelected)
        {
            SelectedSource = replacement;
        }

        StartStatusCheck(replacement);
    }

    /// <summary>
    ///     Adds a source row at the end of the list and starts its folder check.
    /// </summary>
    /// <param name="definition">The source.</param>
    /// <returns>The new row.</returns>
    private PackageSourceItemViewModel AddItem(PackageSourceDefinition definition)
    {
        var item = CreateItem(definition);
        Sources.Add(item);
        OnPropertyChanged(nameof(HasNoSources));
        StartStatusCheck(item);
        return item;
    }

    /// <summary>
    ///     Creates a row for a source, including how many repos are pinned to it.
    /// </summary>
    /// <param name="definition">The source.</param>
    /// <returns>The row, in its "checking" state.</returns>
    private PackageSourceItemViewModel CreateItem(PackageSourceDefinition definition) =>
        new(definition, _pinnedRepoCounts.GetValueOrDefault(definition.Alias));

    /// <summary>
    ///     Starts a row's folder check on a worker thread.
    /// </summary>
    /// <param name="item">The row to check.</param>
    private void StartStatusCheck(PackageSourceItemViewModel item) =>
        _statusChecks.Add(item.CheckStatusAsync(_folderExists, _statusCancellation.Token));
}
