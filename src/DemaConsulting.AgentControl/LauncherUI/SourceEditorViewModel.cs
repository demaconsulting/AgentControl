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
using DemaConsulting.AgentControl.PackageCredentials;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     View model for the dialog that adds or edits one package source: a zip-package folder, a
///     NuGet-package folder or a NuGet-package server.
/// </summary>
/// <remarks>
///     <para>
///     The dialog exists so a source can only be saved when it is valid: the alias is unique
///     (case-insensitively) among the configured sources and is fixed once the source exists
///     (repos pin to it by alias, so renaming would silently break their pins), the location is
///     a full folder path - or, for a server, an <c>https</c> feed address ending in
///     <c>index.json</c> (<c>http</c> only when "Allow insecure HTTP" is ticked) - that carries
///     no credentials, and a NuGet source lists at least one package ID. The same
///     <see cref="PackageSourceDefinition"/> rules the application applies when it saves settings
///     are used here, so a source accepted by the dialog is never refused later. Validation
///     messages appear only for fields the user has already touched so an empty new-source dialog
///     is not covered in errors, but <see cref="CanSave"/> is false until every rule is met.
///     </para>
///     <para>
///     A server source can be tried before it is saved: <see cref="TestConnectionCommand"/> lists
///     the versions of the first package ID, showing progress that the user can cancel, and then
///     states the outcome in words (connected, sign-in failed, package not found, server
///     unreachable, timed out). The test may ask the user to sign in, like any other use of the
///     server. It reads nothing but a version list and changes nothing.
///     </para>
///     <para>
///     A NuGet source (folder or server) can require signed packages, optionally pinned to one
///     publisher's certificate fingerprint. The fingerprint is accepted as typed or pasted
///     (colons and spaces allowed) and saved as 64 upper-case hexadecimal characters; the field is
///     only usable while "Require signed packages" is ticked, and where the platform cannot verify
///     signatures the checkbox is disabled with an explanation. A server source can also forget
///     the sign-in saved for it (after a confirmation the view shows).
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
    ///     Creates the provider that the connection test lists versions through.
    /// </summary>
    private readonly Func<PackageSourceDefinition, IPackageSourceProvider> _providerFactory;

    /// <summary>
    ///     Backing field for <see cref="Alias"/>.
    /// </summary>
    private string _alias;

    /// <summary>
    ///     Backing field for <see cref="Location"/>.
    /// </summary>
    private string _location;

    /// <summary>
    ///     Backing field for <see cref="AllowInsecureHttp"/>.
    /// </summary>
    private bool _allowInsecureHttp;

    /// <summary>
    ///     Backing field for <see cref="NewPackageId"/>.
    /// </summary>
    private string _newPackageId = string.Empty;

    /// <summary>
    ///     Backing field for <see cref="SelectedPackageId"/>.
    /// </summary>
    private string? _selectedPackageId;

    /// <summary>
    ///     Backing field for <see cref="TestResultKind"/>.
    /// </summary>
    private ConnectionTestResultKind _testResultKind;

    /// <summary>
    ///     Backing field for <see cref="TestResultTitle"/>.
    /// </summary>
    private string _testResultTitle = string.Empty;

    /// <summary>
    ///     Backing field for <see cref="TestResultDetail"/>.
    /// </summary>
    private string _testResultDetail = string.Empty;

    /// <summary>
    ///     Whether the user has typed in the alias field, so its error is only shown after that.
    /// </summary>
    private bool _aliasTouched;

    /// <summary>
    ///     Whether the user has typed in the location field, so its error is only shown after that.
    /// </summary>
    private bool _locationTouched;

    /// <summary>
    ///     Whether the user has typed in the signer fingerprint field, so its error is only shown
    ///     after that.
    /// </summary>
    private bool _fingerprintTouched;

    /// <summary>
    ///     Backing field for <see cref="RequireSignedPackages"/>.
    /// </summary>
    private bool _requireSigned;

    /// <summary>
    ///     Backing field for <see cref="SignerFingerprint"/>.
    /// </summary>
    private string _signerFingerprint;

    /// <summary>
    ///     Whether the platform can verify package signatures; decides whether the "Require signed
    ///     packages" option can be used.
    /// </summary>
    private readonly bool _signaturesSupported;

    /// <summary>
    ///     The credential facilities, used to forget a saved sign-in, or <see langword="null"/>
    ///     when there are none.
    /// </summary>
    private readonly PackageCredentialContext? _credentials;

    /// <summary>
    ///     Backing field for <see cref="ForgetResultText"/>.
    /// </summary>
    private string _forgetResultText = string.Empty;

    /// <summary>
    ///     Whether a request to forget the saved sign-in is running.
    /// </summary>
    private bool _isForgetting;

    /// <summary>
    ///     Whether the user's Git credential helper is available for the current feed address:
    ///     <see langword="null"/> while that is being checked (or when there is no usable address),
    ///     otherwise the answer.
    /// </summary>
    private bool? _helperAvailable;

    /// <summary>
    ///     The answers already obtained, by feed origin, so typing in the address box does not ask
    ///     git again for an origin it was already asked about.
    /// </summary>
    private readonly Dictionary<string, bool> _helperAnswers = [];

    /// <summary>
    ///     Cancels the check that is running for an address that has since changed.
    /// </summary>
    private CancellationTokenSource? _helperCheckCancellation;

    /// <summary>
    ///     The most recently started helper check.
    /// </summary>
    private Task _helperCheck = Task.CompletedTask;

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
    /// <param name="isServer">Whether a new NuGet source is a server (an address) rather than a
    ///     folder; ignored when <paramref name="existing"/> is supplied (its location decides) and
    ///     for a zip source.</param>
    /// <param name="providerFactory">Creates the provider the connection test uses;
    ///     <see langword="null"/> creates a real <see cref="NuGetSourceProvider"/> without
    ///     credential facilities. Supplied so the application can give it the session
    ///     credentials and so tests can use a fake.</param>
    /// <param name="credentials">The credential facilities, used by "Forget saved sign-in";
    ///     <see langword="null"/> when there are none (the action is then unavailable).</param>
    /// <param name="signatureVerificationSupported">Whether the platform can verify package
    ///     signatures, or <see langword="null"/> to ask <see cref="SignatureVerificationSupport"/>.
    ///     Where it cannot, "Require signed packages" is shown disabled with an explanation.
    ///     Supplied so tests can exercise both answers.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="aliasExists"/> is
    ///     <see langword="null"/>.</exception>
    public SourceEditorViewModel(
        PackageSourceType type,
        PackageSourceDefinition? existing,
        Func<string, bool> aliasExists,
        bool isServer = false,
        Func<PackageSourceDefinition, IPackageSourceProvider>? providerFactory = null,
        PackageCredentialContext? credentials = null,
        bool? signatureVerificationSupported = null)
    {
        ArgumentNullException.ThrowIfNull(aliasExists);

        _existing = existing?.Clone();
        _aliasExists = aliasExists;
        _providerFactory = providerFactory ?? (definition => new NuGetSourceProvider(definition));
        _credentials = credentials;
        _signaturesSupported = signatureVerificationSupported ?? SignatureVerificationSupport.IsSupported;
        Type = existing?.Type ?? type;
        IsServer = Type == PackageSourceType.NuGet &&
                   (existing is not null ? existing.LocationKind == PackageSourceLocationKind.Server : isServer);
        _alias = existing?.Alias ?? string.Empty;
        _location = existing?.Location ?? string.Empty;
        _allowInsecureHttp = existing?.AllowInsecureHttp ?? false;
        _requireSigned = Type == PackageSourceType.NuGet && (existing?.RequireSignedPackages ?? false);
        _signerFingerprint = existing?.SignerFingerprint ?? string.Empty;
        PackageIds = [.. existing?.PackageIds ?? []];

        AddPackageIdCommand = new RelayCommand(AddPackageId, () => GetPackageIdProblem(_newPackageId) is null);
        RemovePackageIdCommand = new RelayCommand(RemovePackageId, () => _selectedPackageId is not null);
        SaveCommand = new RelayCommand(Save, () => CanSave);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, OnTestFailed, () => CanTestConnection);
        TestConnectionCommand.IsRunningChanged += (_, _) => OnTestRunningChanged();
        CancelTestCommand = new RelayCommand(() => TestConnectionCommand.Cancel(), () => IsTesting);
        ForgetSavedSignInCommand = new RelayCommand(RequestForgetSavedSignIn, () => CanForgetSavedSignIn);
        PackageIds.CollectionChanged += (_, _) => OnPackageIdsChanged();
        RefreshHelperAvailability();
    }

    /// <summary>
    ///     Gets the kind of source this dialog edits.
    /// </summary>
    public PackageSourceType Type { get; }

    /// <summary>
    ///     Gets a value indicating whether the source is a NuGet-package server (an address)
    ///     rather than a folder.
    /// </summary>
    public bool IsServer { get; }

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
    ///     Gets a value indicating whether the source is a NuGet-package source (folder or
    ///     server), which lists the package IDs it offers; a zip-package folder offers whatever
    ///     zips it contains.
    /// </summary>
    public bool IsNuGet => Type == PackageSourceType.NuGet;

    /// <summary>
    ///     Gets the dialog's title, naming the action and the kind of source.
    /// </summary>
    public string Title => (IsNew, IsNuGet, IsServer) switch
    {
        (true, true, true) => "Add NuGet-Package Server",
        (true, true, false) => "Add NuGet-Package Folder",
        (true, false, _) => "Add Zip-Package Folder",
        (false, true, true) => "Edit NuGet-Package Server",
        (false, true, false) => "Edit NuGet-Package Folder",
        _ => "Edit Zip-Package Folder"
    };

    /// <summary>
    ///     Gets the label of the location field: a folder, or the address of a feed.
    /// </summary>
    public string LocationLabel => IsServer ? "Feed address" : "Folder";

    /// <summary>
    ///     Gets the placeholder shown in the empty location field.
    /// </summary>
    public string LocationPlaceholder => IsServer
        ? "https://host/path/index.json"
        : "Local, mapped-drive, or UNC folder";

    /// <summary>
    ///     Gets a short explanation of what kind of location to enter, shown beside the location
    ///     field.
    /// </summary>
    public string LocationHelp => (IsNuGet, IsServer) switch
    {
        (true, true) => "The address of the feed's NuGet V3 service index. It must end in index.json.",
        (true, false) => "A folder of NuGet packages (.nupkg files) - a local, mapped-drive or UNC path.",
        _ => "A folder of agent package zips named {name}-{version}.zip - a local, mapped-drive or UNC path."
    };

    /// <summary>
    ///     The example feed addresses for the two most common servers, shown for a server source.
    /// </summary>
    public const string LocationExamplesText =
        "JFrog Artifactory: https://<host>/artifactory/api/nuget/v3/<repo>/index.json\n" +
        "Azure Artifacts: https://pkgs.dev.azure.com/<org>/_packaging/<feed>/nuget/v3/index.json";

    /// <summary>
    ///     Gets the explanation shown above the package ID list.
    /// </summary>
    public string PackageIdsHelp => IsServer
        ? "The agent packages this server offers (the NuGet package IDs), at least one. " +
          "A server cannot be listed cheaply, so name the packages you want."
        : "The agent packages this folder offers (the NuGet package IDs), at least one.";

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
    ///     Gets or sets the folder (or, for a server, the feed address) the source reads packages
    ///     from.
    /// </summary>
    public string Location
    {
        get => _location;
        set
        {
            if (SetField(ref _location, value ?? string.Empty))
            {
                _locationTouched = true;
                RefreshHelperAvailability();
                RaiseValidationChanged();
            }
        }
    }

    /// <summary>
    ///     Gets or sets a value indicating whether a plain <c>http</c> feed address is accepted.
    ///     Off by default: a password sent over plain http crosses the network unprotected.
    /// </summary>
    public bool AllowInsecureHttp
    {
        get => _allowInsecureHttp;
        set
        {
            if (SetField(ref _allowInsecureHttp, value))
            {
                OnPropertyChanged(nameof(ShowInsecureWarning));
                RaiseValidationChanged();
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether the warning about plain http is shown: a server source
    ///     with "Allow insecure HTTP" ticked.
    /// </summary>
    public bool ShowInsecureWarning => IsServer && _allowInsecureHttp;

    /// <summary>
    ///     The explanation shown under the signature options: what requiring signatures does, and
    ///     why pinning the publisher's certificate is recommended.
    /// </summary>
    public const string SignatureHelpText =
        "Only accept packages that carry a valid signature. Without a fingerprint, any validly signed package " +
        "that chains to a trusted root is accepted - including packages signed by unrelated publishers - so " +
        "pinning your publisher's certificate below is recommended.";

    /// <summary>
    ///     The explanation of how to find a certificate fingerprint, shown under the fingerprint
    ///     field.
    /// </summary>
    public const string FingerprintHelpText =
        "The SHA-256 fingerprint of the publisher's (author) signing certificate: 64 hexadecimal characters; " +
        "colons and spaces are fine. Find it with: dotnet nuget verify --all <package>.nupkg";

    /// <summary>
    ///     Gets a value indicating whether the signature options are shown: for NuGet sources (a
    ///     folder or a server). A zip package carries no signature, so a zip source has none.
    /// </summary>
    public bool ShowSignatureOptions => IsNuGet;

    /// <summary>
    ///     Gets or sets a value indicating whether packages from this source must be validly
    ///     signed. Off by default; cannot be turned on where signature verification is not
    ///     supported (see <see cref="IsRequireSignedEnabled"/>).
    /// </summary>
    public bool RequireSignedPackages
    {
        get => _requireSigned;
        set
        {
            if (SetField(ref _requireSigned, value))
            {
                OnPropertyChanged(nameof(IsFingerprintEnabled));
                OnPropertyChanged(nameof(FingerprintError));
                RaiseSaveGateChanged();
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether the "Require signed packages" checkbox can be changed:
    ///     only where the platform can verify package signatures.
    /// </summary>
    public bool IsRequireSignedEnabled => _signaturesSupported;

    /// <summary>
    ///     Gets the tooltip of the "Require signed packages" checkbox: what it does, or - where it
    ///     is disabled - that signature verification is not supported on this platform.
    /// </summary>
    public string RequireSignedTooltip => _signaturesSupported
        ? "Refuse any package from this source that is not validly signed"
        : SignatureVerificationSupport.UnsupportedMessage;

    /// <summary>
    ///     Gets or sets the text of the trusted signer's certificate fingerprint (SHA-256), as the
    ///     user typed or pasted it. Optional; see <see cref="FingerprintError"/> for what is
    ///     accepted.
    /// </summary>
    public string SignerFingerprint
    {
        get => _signerFingerprint;
        set
        {
            if (SetField(ref _signerFingerprint, value ?? string.Empty))
            {
                _fingerprintTouched = true;
                OnPropertyChanged(nameof(FingerprintError));
                RaiseSaveGateChanged();
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether the fingerprint field can be used: only while "Require
    ///     signed packages" is ticked (and can be used).
    /// </summary>
    public bool IsFingerprintEnabled => _requireSigned && _signaturesSupported;

    /// <summary>
    ///     Gets the reason the fingerprint text is not acceptable, or <see langword="null"/> when
    ///     it is acceptable (including empty - the fingerprint is optional), the field is not in
    ///     use, or the user has not yet typed in it.
    /// </summary>
    public string? FingerprintError => _fingerprintTouched ? GetFingerprintProblem() : null;

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
    ///     Gets the reason a NuGet source cannot be saved for want of package IDs, or
    ///     <see langword="null"/> when it has at least one (or is not a NuGet source).
    /// </summary>
    public string? PackageIdsError
    {
        get
        {
            if (!IsNuGet || PackageIds.Count > 0)
            {
                return null;
            }

            var kind = IsServer ? "server" : "folder";
            return $"Add at least one package ID; a NuGet-package {kind} only offers the packages listed here.";
        }
    }

    /// <summary>
    ///     Gets a value indicating whether the source can be saved: every rule is met.
    /// </summary>
    public bool CanSave =>
        GetAliasProblem() is null && GetLocationProblem() is null && (!IsNuGet || PackageIds.Count > 0) &&
        GetFingerprintProblem() is null;

    /// <summary>
    ///     Gets a value indicating whether the connection can be tested now: a server whose
    ///     address is acceptable and that lists at least one package ID to look for. The alias is
    ///     not needed.
    /// </summary>
    public bool CanTestConnection =>
        IsServer && _location.Trim().Length > 0 && GetLocationProblem() is null && PackageIds.Count > 0;

    /// <summary>
    ///     Gets the reason the connection cannot be tested yet, shown as the disabled button's
    ///     tooltip, or the button's normal explanation when it can.
    /// </summary>
    public string TestConnectionTooltip => CanTestConnection || IsTesting
        ? "Check that the server answers and lists the first package ID (you may be asked to sign in)"
        : "Enter a valid feed address and add at least one package ID to test the connection";

    /// <summary>
    ///     Gets a value indicating whether a connection test is running, so the dialog shows
    ///     progress and a Cancel button.
    /// </summary>
    public bool IsTesting => TestConnectionCommand.IsRunning;

    /// <summary>
    ///     Gets the text shown beside the progress indicator while a test runs.
    /// </summary>
    public string TestProgressText => PackageIds.Count > 0
        ? $"Contacting the server and looking for '{PackageIds[0]}'... (you may be asked to sign in)"
        : "Contacting the server...";

    /// <summary>
    ///     Gets the outcome of the last connection test, or <see cref="ConnectionTestResultKind.None"/>
    ///     when none has run since the address or package IDs last changed.
    /// </summary>
    public ConnectionTestResultKind TestResultKind
    {
        get => _testResultKind;
        private set
        {
            if (SetField(ref _testResultKind, value))
            {
                OnPropertyChanged(nameof(HasTestResult));
                OnPropertyChanged(nameof(TestSucceeded));
                OnPropertyChanged(nameof(TestFailed));
                OnPropertyChanged(nameof(TestWasCanceled));
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether there is a test result to show.
    /// </summary>
    public bool HasTestResult => _testResultKind != ConnectionTestResultKind.None;

    /// <summary>
    ///     Gets a value indicating whether the last test connected and found the package.
    /// </summary>
    public bool TestSucceeded => _testResultKind == ConnectionTestResultKind.Succeeded;

    /// <summary>
    ///     Gets a value indicating whether the last test failed.
    /// </summary>
    public bool TestFailed => _testResultKind == ConnectionTestResultKind.Failed;

    /// <summary>
    ///     Gets a value indicating whether the last test was stopped by the user.
    /// </summary>
    public bool TestWasCanceled => _testResultKind == ConnectionTestResultKind.Canceled;

    /// <summary>
    ///     Gets the one-line headline of the last test result, such as "Connection OK" or "Sign-in
    ///     failed".
    /// </summary>
    public string TestResultTitle
    {
        get => _testResultTitle;
        private set => SetField(ref _testResultTitle, value);
    }

    /// <summary>
    ///     Gets the explanation that goes with <see cref="TestResultTitle"/>, in words a user can
    ///     act on and without any secret.
    /// </summary>
    public string TestResultDetail
    {
        get => _testResultDetail;
        private set => SetField(ref _testResultDetail, value);
    }

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
    ///     Gets the command bound to the "Test connection" button; executable while
    ///     <see cref="CanTestConnection"/> is <see langword="true"/> and no test is running.
    /// </summary>
    public AsyncRelayCommand TestConnectionCommand { get; }

    /// <summary>
    ///     Gets the command bound to the "Cancel test" button shown while a test runs; it stops the
    ///     test, including a sign-in dialog the test opened.
    /// </summary>
    public RelayCommand CancelTestCommand { get; }

    /// <summary>
    ///     Raised when the user saves a valid source, carrying the resulting definition, so the
    ///     owner can store it and the window can close.
    /// </summary>
    public event EventHandler<PackageSourceDefinition>? Saved;

    /// <summary>
    ///     The label of the "Forget saved sign-in" button.
    /// </summary>
    public const string ForgetLabel = "Forget saved sign-in";

    /// <summary>
    ///     Gets a value indicating whether the "Forget saved sign-in" action is available: a
    ///     server source with an acceptable address, and a Git credential helper that applies to
    ///     it (without one nothing can have been saved, so there is nothing to forget).
    /// </summary>
    public bool CanForgetSavedSignIn =>
        IsServer && !_isForgetting && _credentials?.Remembered is not null &&
        _helperAvailable == true && FeedOrigin.TryParse(_location, out _);

    /// <summary>
    ///     The tooltip of the "Forget saved sign-in" button when no Git credential helper is
    ///     available for the server.
    /// </summary>
    public const string NoHelperForgetTooltip =
        "Not available: no Git credential helper is configured on this computer for this server, " +
        "so no sign-in has been saved for it";

    /// <summary>
    ///     Gets a task that completes when the check for a Git credential helper for the current
    ///     address has finished (already complete when no check is needed). For the view and for
    ///     tests; the check never throws.
    /// </summary>
    public Task HelperCheckCompleted => _helperCheck;

    /// <summary>
    ///     Gets the tooltip of the "Forget saved sign-in" button: what it does, or why it is
    ///     unavailable.
    /// </summary>
    public string ForgetTooltip
    {
        get
        {
            if (_credentials?.Remembered is null)
            {
                return "Not available: no Git credential helper can be reached from this window";
            }

            if (!FeedOrigin.TryParse(_location, out _))
            {
                return "Enter a valid feed address to forget its saved sign-in";
            }

            return _helperAvailable switch
            {
                null when !_isForgetting => "Checking whether a Git credential helper is available for this server...",
                false => NoHelperForgetTooltip,
                _ => "Remove the sign-in that your Git credential helper saved for this server " +
                     "(and the one AgentControl remembers for this session)"
            };
        }
    }

    /// <summary>
    ///     Gets the outcome of the last "Forget saved sign-in" request, or an empty string when
    ///     none has been made.
    /// </summary>
    public string ForgetResultText
    {
        get => _forgetResultText;
        private set
        {
            if (SetField(ref _forgetResultText, value))
            {
                OnPropertyChanged(nameof(HasForgetResult));
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether <see cref="ForgetResultText"/> has text to show.
    /// </summary>
    public bool HasForgetResult => _forgetResultText.Length > 0;

    /// <summary>
    ///     Gets the command bound to the "Forget saved sign-in" button; it asks the view to
    ///     confirm (see <see cref="ForgetConfirmationRequested"/>) and does nothing more until the
    ///     view calls <see cref="ForgetSavedSignInAsync"/>.
    /// </summary>
    public RelayCommand ForgetSavedSignInCommand { get; }

    /// <summary>
    ///     Raised when the user asks to forget the saved sign-in, carrying the question to put to
    ///     them. The view shows it and calls <see cref="ForgetSavedSignInAsync"/> only if they
    ///     agree.
    /// </summary>
    public event EventHandler<string>? ForgetConfirmationRequested;

    /// <summary>
    ///     Forgets the sign-in for the source's server - the one AgentControl remembers for this
    ///     session and the one the user's Git credential helper saved - and reports the outcome in
    ///     <see cref="ForgetResultText"/>. Call it only after the user confirmed.
    /// </summary>
    /// <returns>A task that completes when the outcome is shown. Never throws for a failure of
    ///     the credential helper; the failure is reported in words.</returns>
    public async Task ForgetSavedSignInAsync()
    {
        if (!CanForgetSavedSignIn || !FeedOrigin.TryParse(_location, out var origin) || origin is null)
        {
            return;
        }

        _isForgetting = true;
        RaiseForgetAvailabilityChanged();

        // A test result that said "connected" was reached with a sign-in that is about to go
        ClearTestResult();
        try
        {
            var result = await _credentials!.ForgetSavedSignInAsync(origin, CancellationToken.None);
            ForgetResultText = result switch
            {
                ForgetSignInResult.Removed => $"The saved sign-in for {origin} was forgotten.",
                ForgetSignInResult.NothingSaved => $"No saved sign-in was found for {origin}.",
                _ => "Your Git credential helper could not remove the saved sign-in. Remove it with the " +
                     "helper's own tools (for example Windows Credential Manager)."
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ForgetResultText = $"The saved sign-in could not be forgotten: {SecretRedactor.SafeMessage(ex)}";
        }
        finally
        {
            _isForgetting = false;
            RaiseForgetAvailabilityChanged();
        }
    }

    /// <summary>
    ///     Asks the view to confirm forgetting the saved sign-in.
    /// </summary>
    private void RequestForgetSavedSignIn()
    {
        if (!CanForgetSavedSignIn || !FeedOrigin.TryParse(_location, out var origin) || origin is null)
        {
            return;
        }

        ForgetResultText = string.Empty;
        ForgetConfirmationRequested?.Invoke(
            this,
            $"Forget the saved sign-in for {origin}?\n\n" +
            "This removes the user name and password that your Git credential helper (for example Git " +
            "Credential Manager) saved for this server, and the sign-in AgentControl remembers for this " +
            "session. You will be asked to sign in again the next time the server needs it.");
    }

    /// <summary>
    ///     Starts finding out whether a Git credential helper applies to the current feed address,
    ///     reusing an earlier answer for the same origin. Until the answer is known the "Forget
    ///     saved sign-in" action is unavailable; a failed check counts as "no helper".
    /// </summary>
    private void RefreshHelperAvailability()
    {
        _helperCheckCancellation?.Cancel();
        _helperCheckCancellation = null;
        _helperAvailable = null;
        _helperCheck = Task.CompletedTask;

        if (!IsServer || _credentials?.Remembered is null || !FeedOrigin.TryParse(_location, out var origin) || origin is null)
        {
            return;
        }

        var key = origin.ToString();
        if (_helperAnswers.TryGetValue(key, out var known))
        {
            _helperAvailable = known;
            return;
        }

        var cancellation = new CancellationTokenSource();
        _helperCheckCancellation = cancellation;
        _helperCheck = CheckHelperAsync(origin, key, cancellation);
    }

    /// <summary>
    ///     Asks the credential store whether a helper applies to the origin and records the
    ///     answer, unless the address changed meanwhile.
    /// </summary>
    /// <param name="origin">The feed origin.</param>
    /// <param name="key">The cache key of the origin.</param>
    /// <param name="cancellation">The cancellation of this check.</param>
    /// <returns>A task that completes when the check is over; it never faults.</returns>
    private async Task CheckHelperAsync(FeedOrigin origin, string key, CancellationTokenSource cancellation)
    {
        bool available;
        try
        {
            available = await _credentials!.Remembered!.IsAvailableAsync(origin, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
#pragma warning disable CA1031 // Whatever goes wrong, nothing can be forgotten: treat the helper as unavailable
        catch (Exception)
#pragma warning restore CA1031
        {
            available = false;
        }

        if (cancellation.IsCancellationRequested)
        {
            return;
        }

        _helperAnswers[key] = available;
        _helperAvailable = available;
        RaiseForgetAvailabilityChanged();
    }

    /// <summary>
    ///     Raises the change notifications for the "Forget saved sign-in" button.
    /// </summary>
    private void RaiseForgetAvailabilityChanged()
    {
        OnPropertyChanged(nameof(CanForgetSavedSignIn));
        OnPropertyChanged(nameof(ForgetTooltip));
        ForgetSavedSignInCommand.RaiseCanExecuteChanged();
    }

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
    ///     location and, for NuGet, the package IDs and (for a server) the insecure-HTTP choice.
    /// </summary>
    /// <param name="forTest">Whether the definition is for a connection test, which does not need
    ///     an alias (a placeholder is used when none was typed yet).</param>
    /// <returns>The definition. Call only when <see cref="CanSave"/> is <see langword="true"/>
    ///     (or <see cref="CanTestConnection"/> when <paramref name="forTest"/>).</returns>
    private PackageSourceDefinition BuildDefinition(bool forTest = false)
    {
        var alias = _alias.Trim().Length > 0 || !forTest ? _alias.Trim() : "Connection test";
        var definition = _existing?.Clone() ?? new PackageSourceDefinition { Alias = alias, Type = Type };
        definition.Location = _location.Trim();
        definition.PackageIds = IsNuGet ? [.. PackageIds] : [];
        if (IsServer)
        {
            definition.AllowInsecureHttp = _allowInsecureHttp;
        }

        if (IsNuGet)
        {
            // The pinned signer only means something while signatures are required; it is stored in
            // its normalized form so every reader sees the same text
            definition.RequireSignedPackages = _requireSigned;
            definition.SignerFingerprint =
                _requireSigned && PackageSourceDefinition.TryNormalizeFingerprint(_signerFingerprint, out var fingerprint)
                    ? fingerprint
                    : null;
        }

        return definition;
    }

    /// <summary>
    ///     Lists the versions of the first package ID through the source's provider and reports
    ///     the outcome in <see cref="TestResultKind"/>, <see cref="TestResultTitle"/> and
    ///     <see cref="TestResultDetail"/>.
    /// </summary>
    /// <remarks>
    ///     Only the version list is read, never a package, so the test is cheap and changes
    ///     nothing. Every failure is turned into a sentence that says what to do; a cancelled
    ///     test is a neutral outcome, not an error.
    /// </remarks>
    /// <param name="cancellationToken">Cancelled by <see cref="CancelTestCommand"/>.</param>
    /// <returns>A task that completes when the outcome is shown.</returns>
    /// <exception cref="OperationCanceledException">Thrown, after the outcome is recorded, when
    ///     the test was cancelled.</exception>
    private async Task TestConnectionAsync(CancellationToken cancellationToken)
    {
        ClearTestResult();
        var packageId = PackageIds[0];
        try
        {
            var provider = _providerFactory(BuildDefinition(forTest: true));
            var versions = await provider.ListVersionsAsync(packageId, cancellationToken);
            if (versions.Count == 0)
            {
                SetTestResult(
                    ConnectionTestResultKind.Failed,
                    "Package not found",
                    $"The server answered, but it has no package '{packageId}'. Check the package ID (and that " +
                    "your account may see it).");
                return;
            }

            var latest = versions.Max(package => package.Version)!;
            var count = versions.Count == 1 ? "1 version" : $"{versions.Count} versions";
            SetTestResult(
                ConnectionTestResultKind.Succeeded,
                "Connection OK",
                $"Found {count} of '{packageId}' (latest {latest}).");
        }
        catch (OperationCanceledException)
        {
            SetTestResult(ConnectionTestResultKind.Canceled, "Test canceled", "The connection test was stopped.");
            throw;
        }
        catch (PackageSourceAccessException ex)
        {
            SetTestResult(ConnectionTestResultKind.Failed, GetFailureTitle(ex.Kind), SecretRedactor.SafeMessage(ex));
        }
        catch (InvalidOperationException ex)
        {
            SetTestResult(ConnectionTestResultKind.Failed, "Connection failed", SecretRedactor.SafeMessage(ex));
        }
    }

    /// <summary>
    ///     Reports an unexpected failure of the test (anything the test did not anticipate) so the
    ///     dialog never stays silent.
    /// </summary>
    /// <param name="exception">The failure.</param>
    private void OnTestFailed(Exception exception) =>
        SetTestResult(
            ConnectionTestResultKind.Failed,
            "Connection failed",
            $"The test could not be completed: {SecretRedactor.SafeMessage(exception)}");

    /// <summary>
    ///     Chooses the headline for a failure.
    /// </summary>
    /// <param name="kind">The cause.</param>
    /// <returns>The headline.</returns>
    private static string GetFailureTitle(PackageSourceFailureKind kind) => kind switch
    {
        PackageSourceFailureKind.AuthenticationFailed => "Sign-in failed",
        PackageSourceFailureKind.SignInCanceled => "Sign-in canceled",
        PackageSourceFailureKind.NotFound => "Package not found",
        PackageSourceFailureKind.TimedOut => "Timed out",
        PackageSourceFailureKind.SignatureRejected => "Signature requirement not met",
        _ => "Server unreachable"
    };

    /// <summary>
    ///     Records the outcome of a test.
    /// </summary>
    /// <param name="kind">The outcome.</param>
    /// <param name="title">The headline.</param>
    /// <param name="detail">The explanation.</param>
    private void SetTestResult(ConnectionTestResultKind kind, string title, string detail)
    {
        TestResultTitle = title;
        TestResultDetail = detail;
        TestResultKind = kind;
    }

    /// <summary>
    ///     Forgets the last test result, for when the thing it described changed.
    /// </summary>
    private void ClearTestResult()
    {
        TestResultKind = ConnectionTestResultKind.None;
        TestResultTitle = string.Empty;
        TestResultDetail = string.Empty;
    }

    /// <summary>
    ///     Reacts to a test starting or finishing: the progress area and both buttons follow.
    /// </summary>
    private void OnTestRunningChanged()
    {
        OnPropertyChanged(nameof(IsTesting));
        OnPropertyChanged(nameof(TestProgressText));
        OnPropertyChanged(nameof(TestConnectionTooltip));
        CancelTestCommand.RaiseCanExecuteChanged();
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
    ///     Raises the change notifications for the save gate alone, for edits (the signature
    ///     options) that do not affect the connection test.
    /// </summary>
    private void RaiseSaveGateChanged()
    {
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    ///     Raises the change notifications that follow any edit of the field values or package
    ///     IDs. A connection test result describes the address and package IDs it was run with,
    ///     so it is dropped once either changes.
    /// </summary>
    private void RaiseValidationChanged()
    {
        OnPropertyChanged(nameof(AliasError));
        OnPropertyChanged(nameof(LocationError));
        OnPropertyChanged(nameof(PackageIdsError));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanTestConnection));
        OnPropertyChanged(nameof(TestConnectionTooltip));
        SaveCommand.RaiseCanExecuteChanged();
        TestConnectionCommand.RaiseCanExecuteChanged();
        RaiseForgetAvailabilityChanged();
        if (!IsTesting)
        {
            ClearTestResult();
        }
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
    ///     Finds the first rule the signer fingerprint breaks.
    /// </summary>
    /// <returns>A message for the user, or <see langword="null"/> when the fingerprint is
    ///     acceptable: empty (it is optional), or the field is not in use.</returns>
    private string? GetFingerprintProblem()
    {
        if (!IsFingerprintEnabled || string.IsNullOrWhiteSpace(_signerFingerprint))
        {
            return null;
        }

        return PackageSourceDefinition.TryNormalizeFingerprint(_signerFingerprint, out _)
            ? null
            : "Enter the 64 hexadecimal characters of a SHA-256 certificate fingerprint " +
              "(colons and spaces are fine), or leave it empty.";
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

        // A server address has its own rules (https, index.json, no credentials)
        if (IsServer)
        {
            var server = new PackageSourceDefinition
            {
                Alias = _alias,
                Type = Type,
                Location = location,
                AllowInsecureHttp = _allowInsecureHttp
            };
            return server.GetServerLocationProblem();
        }

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
