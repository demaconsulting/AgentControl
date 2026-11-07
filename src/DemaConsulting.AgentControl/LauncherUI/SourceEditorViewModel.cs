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
using System.Text.RegularExpressions;
using DemaConsulting.AgentControl.AgentPackageManagement;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     View model for the dialog that adds or edits one package source: a zip-package folder or a
///     NuGet-package folder (a server source is added by a later piece of work).
/// </summary>
/// <remarks>
///     <para>
///     The dialog exists so a source can only be saved when it is valid: the alias is unique
///     (case-insensitively) among the configured sources and is fixed once the source exists
///     (repos pin to it by alias, so renaming would silently break their pins), the location is
///     a full folder path that carries no credentials, and a NuGet folder lists at least one
///     package ID. The same <see cref="PackageSourceDefinition"/> rules the application applies
///     when it saves settings are used here, so a source accepted by the dialog is never refused
///     later. Validation messages appear only for fields the user has already touched so an
///     empty new-source dialog is not covered in errors, but <see cref="CanSave"/> is false
///     until every rule is met.
///     </para>
///     <para>
///     When editing, a copy of the source is changed; everything the dialog does not show is
///     carried over unchanged. Not thread-safe; every member is expected to be called from the UI
///     thread.
///     </para>
/// </remarks>
internal sealed partial class SourceEditorViewModel : ViewModelBase
{
    /// <summary>
    ///     The longest accepted package ID, matching the limit NuGet itself applies.
    /// </summary>
    private const int MaxPackageIdLength = 100;

    /// <summary>
    ///     Tells whether an alias is already used by a configured source; supplied by the owner
    ///     (typically <see cref="SettingsWindowViewModel"/>) so the dialog never needs the whole
    ///     source list.
    /// </summary>
    private readonly Func<string, bool> _aliasExists;

    /// <summary>
    ///     The source being edited, or <see langword="null"/> when a new source is being added.
    /// </summary>
    private readonly PackageSourceDefinition? _existing;

    /// <summary>
    ///     Backing field for <see cref="Alias"/>.
    /// </summary>
    private string _alias;

    /// <summary>
    ///     Backing field for <see cref="Location"/>.
    /// </summary>
    private string _location;

    /// <summary>
    ///     Backing field for <see cref="NewPackageId"/>.
    /// </summary>
    private string _newPackageId = string.Empty;

    /// <summary>
    ///     Backing field for <see cref="SelectedPackageId"/>.
    /// </summary>
    private string? _selectedPackageId;

    /// <summary>
    ///     Whether the user has typed in the alias field, so its error is only shown after that.
    /// </summary>
    private bool _aliasTouched;

    /// <summary>
    ///     Whether the user has typed in the location field, so its error is only shown after that.
    /// </summary>
    private bool _locationTouched;

    /// <summary>
    ///     Initializes a new <see cref="SourceEditorViewModel"/>.
    /// </summary>
    /// <param name="type">The kind of source being added; ignored when
    ///     <paramref name="existing"/> is supplied (its type is kept).</param>
    /// <param name="existing">The source to edit, or <see langword="null"/> to add a new one. Not
    ///     modified; the dialog works on a copy.</param>
    /// <param name="aliasExists">Tells whether an alias is already used by a configured source
    ///     (compared case-insensitively by the caller). When editing, the edited source's own
    ///     alias is never reported as a duplicate.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="aliasExists"/> is
    ///     <see langword="null"/>.</exception>
    public SourceEditorViewModel(
        PackageSourceType type, PackageSourceDefinition? existing, Func<string, bool> aliasExists)
    {
        ArgumentNullException.ThrowIfNull(aliasExists);

        _existing = existing?.Clone();
        _aliasExists = aliasExists;
        Type = existing?.Type ?? type;
        _alias = existing?.Alias ?? string.Empty;
        _location = existing?.Location ?? string.Empty;
        PackageIds = [.. existing?.PackageIds ?? []];

        AddPackageIdCommand = new RelayCommand(AddPackageId, () => GetPackageIdProblem(_newPackageId) is null);
        RemovePackageIdCommand = new RelayCommand(RemovePackageId, () => _selectedPackageId is not null);
        SaveCommand = new RelayCommand(Save, () => CanSave);
        PackageIds.CollectionChanged += (_, _) => OnPackageIdsChanged();
    }

    /// <summary>
    ///     Gets the kind of source this dialog edits.
    /// </summary>
    public PackageSourceType Type { get; }

    /// <summary>
    ///     Gets a value indicating whether a new source is being added (as opposed to an existing
    ///     one being edited).
    /// </summary>
    public bool IsNew => _existing is null;

    /// <summary>
    ///     Gets a value indicating whether the alias can be typed: only for a new source, because
    ///     repos pin to a source by alias and a rename would break those pins.
    /// </summary>
    public bool IsAliasEditable => IsNew;

    /// <summary>
    ///     Gets a value indicating whether the source is a NuGet-package folder, which lists the
    ///     package IDs it offers; a zip-package folder offers whatever zips it contains.
    /// </summary>
    public bool IsNuGet => Type == PackageSourceType.NuGet;

    /// <summary>
    ///     Gets the dialog's title, naming the action and the kind of source.
    /// </summary>
    public string Title => (IsNew, IsNuGet) switch
    {
        (true, true) => "Add NuGet-Package Folder",
        (true, false) => "Add Zip-Package Folder",
        (false, true) => "Edit NuGet-Package Folder",
        _ => "Edit Zip-Package Folder"
    };

    /// <summary>
    ///     Gets a short explanation of what kind of folder to enter, shown beside the location
    ///     field.
    /// </summary>
    public string LocationHelp => IsNuGet
        ? "A folder of NuGet packages (.nupkg files) - a local, mapped-drive or UNC path."
        : "A folder of agent package zips named {name}-{version}.zip - a local, mapped-drive or UNC path.";

    /// <summary>
    ///     Gets or sets the source's alias, the name repos use to refer to it.
    /// </summary>
    public string Alias
    {
        get => _alias;
        set
        {
            if (SetField(ref _alias, value ?? string.Empty))
            {
                _aliasTouched = true;
                RaiseValidationChanged();
            }
        }
    }

    /// <summary>
    ///     Gets or sets the folder the source reads packages from.
    /// </summary>
    public string Location
    {
        get => _location;
        set
        {
            if (SetField(ref _location, value ?? string.Empty))
            {
                _locationTouched = true;
                RaiseValidationChanged();
            }
        }
    }

    /// <summary>
    ///     Gets the package IDs a NuGet-package folder offers.
    /// </summary>
    public ObservableCollection<string> PackageIds { get; }

    /// <summary>
    ///     Gets or sets the package ID typed into the "add" box.
    /// </summary>
    public string NewPackageId
    {
        get => _newPackageId;
        set
        {
            if (SetField(ref _newPackageId, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(NewPackageIdError));
                AddPackageIdCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    ///     Gets or sets the package ID selected in the list, the one the remove button removes.
    /// </summary>
    public string? SelectedPackageId
    {
        get => _selectedPackageId;
        set
        {
            if (SetField(ref _selectedPackageId, value))
            {
                RemovePackageIdCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    ///     Gets the reason the alias is not acceptable, or <see langword="null"/> when it is
    ///     acceptable or the user has not yet typed in the field.
    /// </summary>
    public string? AliasError => _aliasTouched ? GetAliasProblem() : null;

    /// <summary>
    ///     Gets the reason the location is not acceptable, or <see langword="null"/> when it is
    ///     acceptable or the user has not yet typed in the field.
    /// </summary>
    public string? LocationError => _locationTouched ? GetLocationProblem() : null;

    /// <summary>
    ///     Gets the reason the package ID typed into the "add" box cannot be added, or
    ///     <see langword="null"/> when the box is empty or its text can be added.
    /// </summary>
    public string? NewPackageIdError => _newPackageId.Length == 0 ? null : GetPackageIdProblem(_newPackageId);

    /// <summary>
    ///     Gets the reason a NuGet-package folder cannot be saved for want of package IDs, or
    ///     <see langword="null"/> when it has at least one (or is not a NuGet source).
    /// </summary>
    public string? PackageIdsError => IsNuGet && PackageIds.Count == 0
        ? "Add at least one package ID; a NuGet-package folder only offers the packages listed here."
        : null;

    /// <summary>
    ///     Gets a value indicating whether the source can be saved: every rule is met.
    /// </summary>
    public bool CanSave =>
        GetAliasProblem() is null && GetLocationProblem() is null && (!IsNuGet || PackageIds.Count > 0);

    /// <summary>
    ///     Gets the command that adds <see cref="NewPackageId"/> to <see cref="PackageIds"/>.
    /// </summary>
    public RelayCommand AddPackageIdCommand { get; }

    /// <summary>
    ///     Gets the command that removes <see cref="SelectedPackageId"/> from
    ///     <see cref="PackageIds"/>.
    /// </summary>
    public RelayCommand RemovePackageIdCommand { get; }

    /// <summary>
    ///     Gets the command bound to the dialog's "Save" button; executable only while
    ///     <see cref="CanSave"/> is <see langword="true"/>.
    /// </summary>
    public RelayCommand SaveCommand { get; }

    /// <summary>
    ///     Raised when the user saves a valid source, carrying the resulting definition, so the
    ///     owner can store it and the window can close.
    /// </summary>
    public event EventHandler<PackageSourceDefinition>? Saved;

    /// <summary>
    ///     Matches an acceptable NuGet package ID: letters, digits, dots, underscores and hyphens,
    ///     starting with a letter or digit.
    /// </summary>
    /// <returns>The compiled expression.</returns>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageIdPattern();

    /// <summary>
    ///     Builds the source definition the dialog describes: a copy of the edited source (so
    ///     settings the dialog does not show are preserved) or a new definition, with the typed
    ///     location and, for NuGet, the package IDs.
    /// </summary>
    /// <returns>The definition. Call only when <see cref="CanSave"/> is <see langword="true"/>.</returns>
    private PackageSourceDefinition BuildDefinition()
    {
        var definition = _existing?.Clone() ?? new PackageSourceDefinition { Alias = _alias.Trim(), Type = Type };
        definition.Location = _location.Trim();
        definition.PackageIds = IsNuGet ? [.. PackageIds] : [];
        return definition;
    }

    /// <summary>
    ///     Raises <see cref="Saved"/> with the described source when it is valid.
    /// </summary>
    private void Save()
    {
        if (!CanSave)
        {
            return;
        }

        Saved?.Invoke(this, BuildDefinition());
    }

    /// <summary>
    ///     Adds the typed package ID to the list when it is acceptable, then clears the box.
    /// </summary>
    private void AddPackageId()
    {
        var id = _newPackageId.Trim();
        if (GetPackageIdProblem(_newPackageId) is not null)
        {
            return;
        }

        PackageIds.Add(id);
        NewPackageId = string.Empty;
    }

    /// <summary>
    ///     Removes the selected package ID from the list.
    /// </summary>
    private void RemovePackageId()
    {
        if (_selectedPackageId is null)
        {
            return;
        }

        PackageIds.Remove(_selectedPackageId);
        SelectedPackageId = null;
    }

    /// <summary>
    ///     Raises the change notifications that follow any edit of the field values or package
    ///     IDs.
    /// </summary>
    private void RaiseValidationChanged()
    {
        OnPropertyChanged(nameof(AliasError));
        OnPropertyChanged(nameof(LocationError));
        OnPropertyChanged(nameof(PackageIdsError));
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    ///     Reacts to the package ID list changing: the "add" box's duplicate check, the "at least
    ///     one" rule and the save gate all depend on it.
    /// </summary>
    private void OnPackageIdsChanged()
    {
        OnPropertyChanged(nameof(NewPackageIdError));
        AddPackageIdCommand.RaiseCanExecuteChanged();
        RaiseValidationChanged();
    }

    /// <summary>
    ///     Finds the first rule the alias breaks.
    /// </summary>
    /// <returns>A message for the user, or <see langword="null"/> when the alias is acceptable.</returns>
    private string? GetAliasProblem()
    {
        // An existing source keeps its alias, which is therefore never a duplicate of itself
        if (!IsNew)
        {
            return null;
        }

        var alias = _alias;
        if (string.IsNullOrWhiteSpace(alias))
        {
            return "Enter an alias, the short name repos use to refer to this source.";
        }

        if (!PackageSourceDefinition.IsValidAlias(alias))
        {
            return "Use letters, digits, spaces, '.', '_' or '-' (starting with a letter or digit, " +
                   "up to 64 characters, not ending in a space or '.').";
        }

        return _aliasExists(alias)
            ? $"A source named '{alias}' already exists; aliases must be unique (ignoring case)."
            : null;
    }

    /// <summary>
    ///     Finds the first rule the location breaks.
    /// </summary>
    /// <returns>A message for the user, or <see langword="null"/> when the location is
    ///     acceptable.</returns>
    private string? GetLocationProblem()
    {
        var location = _location.Trim();
        if (location.Length == 0)
        {
            return "Enter the folder the packages are in.";
        }

        // Credentials embedded in a location are refused first and with their own message: they
        // must never be stored, whatever kind of location they appear in
        var candidate = new PackageSourceDefinition { Alias = _alias, Type = Type, Location = location };
        var credentialProblem = candidate.GetCredentialProblem();
        if (credentialProblem is not null)
        {
            return credentialProblem;
        }

        // A URL (a server) is not a folder
        if (candidate.LocationKind == PackageSourceLocationKind.Server)
        {
            return "Enter a folder path, not a web address.";
        }

        return Path.IsPathFullyQualified(location)
            ? null
            : "Enter a full path, such as C:\\Packages or \\\\server\\share\\packages.";
    }

    /// <summary>
    ///     Finds the first rule a package ID breaks.
    /// </summary>
    /// <param name="text">The package ID text.</param>
    /// <returns>A message for the user, or <see langword="null"/> when the ID can be added.</returns>
    private string? GetPackageIdProblem(string text)
    {
        var id = text.Trim();
        if (id.Length == 0)
        {
            return "Enter a package ID.";
        }

        if (id.Length > MaxPackageIdLength || !PackageIdPattern().IsMatch(id))
        {
            return "A package ID uses letters, digits, '.', '_' or '-' and starts with a letter or digit.";
        }

        return PackageIds.Any(existing => string.Equals(existing, id, StringComparison.OrdinalIgnoreCase))
            ? $"'{id}' is already in the list."
            : null;
    }
}
