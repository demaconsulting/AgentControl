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
using DemaConsulting.AgentControl.GitIntegration;
using DemaConsulting.AgentControl.PackageCredentials;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     The Git-repository part of <see cref="SourceEditorViewModel"/>: the tag pattern, the optional
///     folder inside the repository, the package name, the sign-in note and the preview of the tags
///     that match the pattern after a connection test.
/// </summary>
/// <remarks>
///     A Git source is one repository that delivers one package whose versions are the repository's
///     tags that match the tag pattern. Each field is validated with the same rules the application
///     applies when it saves settings (<see cref="PackageSourceDefinition.GetGitSettingsProblem"/>),
///     so a source the dialog accepts is never refused later. The connection test lists the
///     repository's tags (no clone) and shows which ones match; it may show Git's own sign-in
///     dialog, because the user started it. The only tag names ever shown are ones the provider
///     found safe to show.
/// </remarks>
internal sealed partial class SourceEditorViewModel
{
    /// <summary>
    ///     The tag pattern a new Git source starts with.
    /// </summary>
    public const string DefaultTagPattern = "v{version}";

    /// <summary>
    ///     How many matching tags the preview lists before it says how many more there are.
    /// </summary>
    private const int MaxPreviewItems = 10;

    /// <summary>
    ///     The examples of repository addresses shown under the address box of a Git source.
    /// </summary>
    public const string GitLocationExamplesText =
        "Examples: https://host/org/repo.git\n" +
        "git@host:org/repo.git or ssh://git@host/org/repo.git (SSH)\n" +
        "C:\\Demo\\agent-packages.git or \\\\server\\share\\agent-packages.git (a local path)";

    /// <summary>
    ///     The explanation shown under the tag pattern: what the pattern does, with examples, and that
    ///     the literal parts are case-sensitive.
    /// </summary>
    public const string TagPatternHelpText =
        "The version of a package is the part of a tag name where {version} stands (three numbers such as " +
        "1.2.3, optionally with a prerelease such as 1.2.3-beta.1). Everything else in the pattern must match the " +
        "tag exactly - including upper and lower case, so V{version} does not find v1.2.3.\n" +
        "Examples: v{version} finds v1.2.3. release/V{version} finds release/V1.2.3. ver{version} finds ver1.2.3.";

    /// <summary>
    ///     The explanation shown under the folder-in-repository box.
    /// </summary>
    public const string SubfolderHelpText =
        "Optional. The folder inside the repository that holds .github/, release-notes.md and AGENTS.md. " +
        "Leave it empty if they are at the top of the repository.";

    /// <summary>
    ///     The explanation shown under the package name.
    /// </summary>
    public const string GitPackageIdHelpText =
        "The name this package has in Select Package and on repo cards. Leave it empty to use the alias " +
        "(if the alias is a valid package name).";

    /// <summary>
    ///     The sign-in note of a Git source: where the credentials come from, that AgentControl never
    ///     stores them, and what background checks do.
    /// </summary>
    public const string GitSignInHelpText =
        "AgentControl uses your normal Git sign-in - Git Credential Manager (or another credential helper) for " +
        "https, your SSH keys or ssh-agent for SSH - and never stores credentials itself. Test connection and " +
        "Select Package may show Git's sign-in; checks AgentControl runs in the background never prompt, and the " +
        "card says \"Sign-in needed\" instead. SSH needs a key that works without a passphrase prompt (ssh-agent) " +
        "and a host you have connected to once, so its key is trusted.";

    /// <summary>
    ///     Backing field for <see cref="TagPattern"/>.
    /// </summary>
    private string _tagPattern = DefaultTagPattern;

    /// <summary>
    ///     Backing field for <see cref="Subfolder"/>.
    /// </summary>
    private string _subfolder = string.Empty;

    /// <summary>
    ///     Backing field for <see cref="GitPackageId"/>.
    /// </summary>
    private string _gitPackageId = string.Empty;

    /// <summary>
    ///     Whether the user has typed in the tag pattern box, so its error is only shown after that.
    /// </summary>
    private bool _tagPatternTouched;

    /// <summary>
    ///     Whether the user has typed in the folder-in-repository box.
    /// </summary>
    private bool _subfolderTouched;

    /// <summary>
    ///     Whether the user has typed in the package name box.
    /// </summary>
    private bool _gitPackageIdTouched;

    /// <summary>
    ///     Backing field for <see cref="TagPreviewSummary"/>.
    /// </summary>
    private string _tagPreviewSummary = string.Empty;

    /// <summary>
    ///     Gets a value indicating whether the source is a Git repository.
    /// </summary>
    public bool IsGit => Type == PackageSourceType.Git;

    /// <summary>
    ///     Gets a value indicating whether the connection test section is shown: for a server and
    ///     for a Git repository.
    /// </summary>
    public bool ShowTestSection => IsServer || IsGit;

    /// <summary>
    ///     Gets the matching tags the last connection test found, newest version first, each as
    ///     <c>version (tag name)</c>; empty when there is no preview.
    /// </summary>
    public ObservableCollection<string> TagPreviewItems { get; } = [];

    /// <summary>
    ///     Gets the one-line summary of the preview (how many of the repository's tags match), or an
    ///     empty string when there is none.
    /// </summary>
    public string TagPreviewSummary
    {
        get => _tagPreviewSummary;
        private set
        {
            if (SetField(ref _tagPreviewSummary, value))
            {
                OnPropertyChanged(nameof(HasTagPreview));
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether there are tags to preview.
    /// </summary>
    public bool HasTagPreview => _tagPreviewSummary.Length > 0 && TagPreviewItems.Count > 0;

    /// <summary>
    ///     Gets or sets the tag pattern, for example <c>v{version}</c>.
    /// </summary>
    public string TagPattern
    {
        get => _tagPattern;
        set
        {
            if (SetField(ref _tagPattern, value ?? string.Empty))
            {
                _tagPatternTouched = true;
                RaiseValidationChanged();
            }
        }
    }

    /// <summary>
    ///     Gets the reason the tag pattern is not acceptable (live, as the user types), or
    ///     <see langword="null"/> when it is acceptable or not yet touched.
    /// </summary>
    public string? TagPatternError => _tagPatternTouched ? GetTagPatternProblem() : null;

    /// <summary>
    ///     Gets or sets the optional folder inside the repository that holds the package layout.
    /// </summary>
    public string Subfolder
    {
        get => _subfolder;
        set
        {
            if (SetField(ref _subfolder, value ?? string.Empty))
            {
                _subfolderTouched = true;
                RaiseValidationChanged();
            }
        }
    }

    /// <summary>
    ///     Gets the reason the folder inside the repository is not acceptable, or
    ///     <see langword="null"/> when it is acceptable or not yet touched.
    /// </summary>
    public string? SubfolderError => _subfolderTouched ? GetSubfolderProblem() : null;

    /// <summary>
    ///     Gets or sets the package name typed by the user; empty means "use the alias".
    /// </summary>
    public string GitPackageId
    {
        get => _gitPackageId;
        set
        {
            if (SetField(ref _gitPackageId, value ?? string.Empty))
            {
                _gitPackageIdTouched = true;
                RaiseValidationChanged();
            }
        }
    }

    /// <summary>
    ///     Gets the placeholder of the package name box: the alias when there is one.
    /// </summary>
    public string GitPackageIdPlaceholder
    {
        get
        {
            // The alias is what an empty box means, so only a usable alias is offered as the default
            if (PackageSourceDefinition.IsValidGitPackageId(_alias.Trim()))
            {
                return _alias.Trim();
            }

            return _alias.Trim().Length == 0 ? "Defaults to the alias" : "For example: contoso-agents";
        }
    }

    /// <summary>
    ///     Gets the reason the package name is not acceptable, or <see langword="null"/> when it is
    ///     acceptable or neither it nor the alias (which it defaults to) has been typed yet.
    /// </summary>
    public string? GitPackageIdError => _gitPackageIdTouched || _aliasTouched ? GetGitPackageIdProblem() : null;

    /// <summary>
    ///     Gets the package name the source will deliver: the typed name, or the alias when the box
    ///     is empty.
    /// </summary>
    private string EffectiveGitPackageId => _gitPackageId.Trim().Length > 0 ? _gitPackageId.Trim() : _alias.Trim();

    /// <summary>
    ///     Sets the Git fields from the edited source (or the defaults for a new one).
    /// </summary>
    /// <param name="existing">The source being edited, or <see langword="null"/>.</param>
    private void InitializeGit(PackageSourceDefinition? existing)
    {
        if (!IsGit)
        {
            return;
        }

        _tagPattern = existing?.TagPattern ?? DefaultTagPattern;
        _subfolder = existing?.Subfolder ?? string.Empty;
        _gitPackageId = existing?.PackageIds.FirstOrDefault() ?? string.Empty;
    }

    /// <summary>
    ///     Finds the first rule the tag pattern breaks.
    /// </summary>
    /// <returns>A message for the user, or <see langword="null"/>.</returns>
    private string? GetTagPatternProblem() => GitTagPattern.GetProblem(_tagPattern.Trim());

    /// <summary>
    ///     Gets the folder inside the repository the way it is saved: backslashes become slashes and
    ///     a leading or trailing slash is dropped, so a path typed the Windows way works.
    /// </summary>
    /// <returns>The normalized folder, or <see langword="null"/> when none was typed.</returns>
    private string? NormalizedSubfolder()
    {
        var text = _subfolder.Trim().Replace('\\', '/').Trim('/');
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    ///     Finds the first rule the folder inside the repository breaks.
    /// </summary>
    /// <returns>A message for the user, or <see langword="null"/>.</returns>
    private string? GetSubfolderProblem() => PackageSourceDefinition.GetSubfolderProblem(NormalizedSubfolder());

    /// <summary>
    ///     Finds the first rule the package name breaks.
    /// </summary>
    /// <returns>A message for the user, or <see langword="null"/>.</returns>
    private string? GetGitPackageIdProblem()
    {
        if (PackageSourceDefinition.IsValidGitPackageId(EffectiveGitPackageId))
        {
            return null;
        }

        return _gitPackageId.Trim().Length == 0
            ? "Enter a package name (letters, digits, dots, underscores and hyphens): the alias cannot be used as one."
            : "A package name uses letters, digits, '.', '_' or '-', starts with a letter or digit and has at most 100 characters.";
    }

    /// <summary>
    ///     Finds the first rule a Git source's own settings break (the address is checked
    ///     separately, with the location).
    /// </summary>
    /// <returns>A message for the user, or <see langword="null"/> when the pattern, the folder and
    ///     the package name are acceptable.</returns>
    private string? GetGitSettingsOtherThanAddressProblem() =>
        GetTagPatternProblem() ?? GetSubfolderProblem() ?? GetGitPackageIdProblem();

    /// <summary>
    ///     Finds the first rule a Git repository address breaks.
    /// </summary>
    /// <param name="location">The trimmed address text.</param>
    /// <returns>A message for the user, or <see langword="null"/>.</returns>
    private string? GetGitLocationProblem(string location)
    {
        if (location.Length == 0)
        {
            return "Enter the address of the Git repository (https, SSH or a local path).";
        }

        // An address with a password or token is refused first, with its own message
        var candidate = new PackageSourceDefinition { Alias = _alias, Type = Type, Location = location };
        return candidate.GetCredentialProblem() ?? GitRepositoryAddress.GetProblem(location);
    }

    /// <summary>
    ///     Applies the Git fields to a definition being built.
    /// </summary>
    /// <param name="definition">The definition to complete.</param>
    private void ApplyGitFields(PackageSourceDefinition definition)
    {
        definition.TagPattern = _tagPattern.Trim();
        definition.Subfolder = NormalizedSubfolder();
        definition.PackageIds = [EffectiveGitPackageId];
        definition.AllowInsecureHttp = false;
        definition.RequireSignedPackages = false;
        definition.SignerFingerprint = null;
    }

    /// <summary>
    ///     Raises the change notifications of the Git fields after an edit.
    /// </summary>
    private void RaiseGitValidationChanged()
    {
        OnPropertyChanged(nameof(TagPatternError));
        OnPropertyChanged(nameof(SubfolderError));
        OnPropertyChanged(nameof(GitPackageIdError));
        OnPropertyChanged(nameof(GitPackageIdPlaceholder));
    }

    /// <summary>
    ///     Forgets the tag preview of the last test, for when what it described changed.
    /// </summary>
    private void ClearTagPreview()
    {
        TagPreviewItems.Clear();
        TagPreviewSummary = string.Empty;
    }

    /// <summary>
    ///     Lists the tags of the repository through the source's provider and reports which of them
    ///     are package versions, or - when none is - what the repository has instead.
    /// </summary>
    /// <param name="cancellationToken">Cancelled by <see cref="CancelTestCommand"/>.</param>
    /// <returns>A task that completes when the outcome is shown.</returns>
    /// <exception cref="OperationCanceledException">Thrown, after the outcome is recorded, when the
    ///     test was cancelled.</exception>
    private async Task TestGitConnectionAsync(CancellationToken cancellationToken)
    {
        ClearTestResult();
        _testInputVersion = _inputVersion;
        try
        {
            var provider = _providerFactory(BuildDefinition(forTest: true));
            var preview = await GetTagPreviewAsync(provider, cancellationToken);
            ShowTagPreview(preview);
        }
        catch (OperationCanceledException)
        {
            SetTestResult(ConnectionTestResultKind.Canceled, "Test canceled", "The connection test was stopped.");
            throw;
        }
        catch (GitRemoteException ex) when (ex.Kind == GitFailureKind.GitTooOld)
        {
            SetTestResult(ConnectionTestResultKind.Failed, "Git is too old", SecretRedactor.SafeMessage(ex));
        }
        catch (PackageSourceAccessException ex)
        {
            SetTestResult(ConnectionTestResultKind.Failed, GetGitFailureTitle(ex.Kind), SecretRedactor.SafeMessage(ex));
        }
        catch (InvalidOperationException ex)
        {
            SetTestResult(ConnectionTestResultKind.Failed, "Connection failed", SecretRedactor.SafeMessage(ex));
        }
    }

    /// <summary>
    ///     Gets the tag preview from a provider: the provider's own when it can preview, otherwise
    ///     one made from its list of versions.
    /// </summary>
    /// <param name="provider">The provider the test runs through.</param>
    /// <param name="cancellationToken">Cancels the listing.</param>
    /// <returns>The preview.</returns>
    private async Task<GitTagPreview> GetTagPreviewAsync(IPackageSourceProvider provider, CancellationToken cancellationToken)
    {
        if (provider is IGitTagPreview previewing)
        {
            return await previewing.PreviewAsync(cancellationToken);
        }

        var versions = await provider.ListVersionsAsync(EffectiveGitPackageId, cancellationToken);
        return new GitTagPreview(versions.Count, [.. versions.OrderByDescending(package => package.Version)], 0, []);
    }

    /// <summary>
    ///     Chooses the headline of a failed Git test.
    /// </summary>
    /// <param name="kind">The cause.</param>
    /// <returns>The headline.</returns>
    private static string GetGitFailureTitle(PackageSourceFailureKind kind) => kind switch
    {
        PackageSourceFailureKind.AuthenticationFailed => "Sign-in needed",
        PackageSourceFailureKind.NotFound => "Repository not found",
        PackageSourceFailureKind.TimedOut => "Timed out",
        _ => "Repository unreachable"
    };

    /// <summary>
    ///     Shows the outcome of a successful listing: the matching tags, or a hint why none match.
    /// </summary>
    /// <param name="preview">What the repository's tags look like under the pattern.</param>
    private void ShowTagPreview(GitTagPreview preview)
    {
        // A result describes the inputs the test ran with: after an edit it is not shown
        if (_testInputVersion != _inputVersion)
        {
            return;
        }

        if (preview.Matching.Count == 0)
        {
            SetTestResult(ConnectionTestResultKind.Failed, "No matching tags", DescribeNoMatch(preview));
            return;
        }

        TagPreviewItems.Clear();
        foreach (var package in preview.Matching.Take(MaxPreviewItems))
        {
            TagPreviewItems.Add($"{package.Version}   (tag {package.Location})");
        }

        var matching = preview.Matching.Count;
        var more = matching > MaxPreviewItems ? $" Showing the newest {MaxPreviewItems}." : string.Empty;
        var others = preview.NonMatchingCount == 0
            ? string.Empty
            : $" {Count(preview.NonMatchingCount, "other tag does", "other tags do")} not match.";
        TagPreviewSummary = $"{matching} of {Count(preview.TotalTags, "tag", "tags")} " +
                            $"match the pattern.{more}{others}";
        SetTestResult(
            ConnectionTestResultKind.Succeeded,
            "Connection OK",
            $"Found {Count(matching, "version", "versions")} of '{EffectiveGitPackageId}' " +
            $"(latest {preview.Matching[0].Version}).");
    }

    /// <summary>
    ///     Formats a count with the singular or plural noun.
    /// </summary>
    /// <param name="count">The count.</param>
    /// <param name="singular">The noun for exactly one.</param>
    /// <param name="plural">The noun otherwise.</param>
    /// <returns>For example <c>1 tag</c> or <c>3 tags</c>.</returns>
    private static string Count(int count, string singular, string plural) =>
        $"{count} {(count == 1 ? singular : plural)}";

    /// <summary>
    ///     Explains why no tag matched: the repository has no tags, or none fits the pattern - with a
    ///     few of the tag names it does have, so a case or prefix mistake is easy to spot.
    /// </summary>
    /// <param name="preview">The preview that found no match.</param>
    /// <returns>The explanation.</returns>
    private static string DescribeNoMatch(GitTagPreview preview)
    {
        if (preview.TotalTags == 0)
        {
            return "The repository answered, but it has no tags yet. Tag a release (for example git tag v1.0.0, " +
                   "then git push origin v1.0.0) and test again.";
        }

        var samples = string.Empty;
        if (preview.NonMatchingSamples.Count > 0)
        {
            var ellipsis = preview.NonMatchingCount > preview.NonMatchingSamples.Count ? ", ..." : string.Empty;
            samples = $" Tags it does have include: {string.Join(", ", preview.NonMatchingSamples)}{ellipsis}.";
        }

        return $"The repository has {Count(preview.TotalTags, "tag", "tags")}, but none " +
               $"is a version under this pattern.{samples} Check the prefix and the capitalization of the pattern " +
               "(the fixed parts are case-sensitive), and that the version part is three numbers such as 1.2.3.";
    }
}
