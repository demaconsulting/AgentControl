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

using System.Text;
using DemaConsulting.AgentControl.GitIntegration;
using DemaConsulting.AgentControl.Logging;
using DemaConsulting.AgentControl.RepoSync;
using Microsoft.Extensions.Logging;

// cspell:ignore CONIN CONOUT ufeff
namespace DemaConsulting.AgentControl.AgentPackageManagement;

/// <summary>
///     Settings of the Git package source that are not part of a source definition: which git to
///     run, where to keep fetched packages, and whether the work was started by the user.
/// </summary>
/// <param name="ResolveGitExecutable">Returns the git executable path or command name, evaluated
///     for every invocation.</param>
/// <param name="CacheRoot">The folder under which fetched packages are kept, or <see langword="null"/>
///     for the per-user default (see <see cref="GitSourceOptions.GetDefaultCacheRoot"/>); must be an
///     absolute path.</param>
/// <param name="Interactive"><see langword="true"/> when the work was started by the user (git may
///     show its credential UI); <see langword="false"/> for background work, which never
///     prompts.</param>
/// <param name="Logger">Receives diagnostics, or <see langword="null"/> for the application
///     logger.</param>
/// <param name="ExtraConfiguration">Extra <c>name=value</c> git settings (<c>-c</c>) for tests that
///     must isolate the user's configuration.</param>
/// <param name="ExtraEnvironment">Extra environment variables for git (a <see langword="null"/>
///     value removes one), for tests.</param>
/// <param name="ListTimeout">The limit for listing tags, or <see langword="null"/> for the
///     default.</param>
/// <param name="FetchTimeout">The limit for fetching a tag, or <see langword="null"/> for the
///     default.</param>
/// <param name="MaxCachedCommits">How many fetched commits are kept per repository.</param>
/// <param name="EnforceMinimumGitVersion"><see langword="true"/> to refuse a git older than
///     <see cref="GitRemoteClient.MinimumGitVersion"/> before the first listing or fetch (with a
///     <see cref="GitRemoteException"/> of kind <see cref="GitFailureKind.GitTooOld"/>); the
///     application sets it, tests that assert exact git invocations leave it off.</param>
internal sealed record GitSourceOptions(
    Func<string> ResolveGitExecutable,
    string? CacheRoot = null,
    bool Interactive = false,
    ILogger? Logger = null,
    IReadOnlyList<string>? ExtraConfiguration = null,
    IReadOnlyDictionary<string, string?>? ExtraEnvironment = null,
    TimeSpan? ListTimeout = null,
    TimeSpan? FetchTimeout = null,
    int MaxCachedCommits = GitSourceOptions.DefaultMaxCachedCommits,
    bool EnforceMinimumGitVersion = false)
{
    /// <summary>
    ///     The default number of fetched commits kept per repository.
    /// </summary>
    public const int DefaultMaxCachedCommits = 5;

    /// <summary>
    ///     Gets a test seam that replaces the removal of the <c>.git</c> folder before a fetched
    ///     package is published, so a removal that fails (or silently does nothing) can be
    ///     simulated; <see langword="null"/> for the real removal.
    /// </summary>
    internal Action<string>? RemoveGitFolder { get; init; }

    /// <summary>
    ///     Gets the default settings: <c>git</c> from the path, the per-user cache, background
    ///     (non-interactive) work.
    /// </summary>
    public static GitSourceOptions Default { get; } = new(() => "git");

    /// <summary>
    ///     Gets the folder under which fetched packages are kept by default: the
    ///     <c>git-cache</c> folder of AgentControl's local (non-roaming) application data.
    /// </summary>
    /// <returns>The folder; it is not created here.</returns>
    public static string GetDefaultCacheRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentControl", "git-cache");
}

/// <summary>
///     What a Git repository's tags look like under a source's tag pattern, for showing a user
///     whether the pattern finds their versions.
/// </summary>
/// <param name="TotalTags">How many tags the repository has.</param>
/// <param name="Matching">The tags that are package versions, highest version first.</param>
/// <param name="NonMatchingCount">How many tags are not package versions.</param>
/// <param name="NonMatchingSamples">Up to five names of tags that are not package versions.</param>
internal sealed record GitTagPreview(
    int TotalTags,
    IReadOnlyList<DiscoveredPackage> Matching,
    int NonMatchingCount,
    IReadOnlyList<string> NonMatchingSamples);

/// <summary>
///     Implemented by a provider that can show how the tags of its repository match its pattern
///     (the Git source), so an editor can preview a pattern after a connection test.
/// </summary>
internal interface IGitTagPreview
{
    /// <summary>
    ///     Lists every tag of the repository and sorts them into package versions and other tags.
    /// </summary>
    /// <param name="cancellationToken">Cancels the listing.</param>
    /// <returns>The preview.</returns>
    /// <exception cref="PackageSourceAccessException">Thrown when the repository cannot be used for
    ///     a reason the user can act on (sign-in, not found, unreachable, timed out).</exception>
    Task<GitTagPreview> PreviewAsync(CancellationToken cancellationToken);
}

/// <summary>
///     <see cref="IPackageSourceProvider"/> over one Git repository whose tags that match a
///     pattern are the versions of a single package.
/// </summary>
/// <remarks>
///     <para>
///     <b>Listing</b> asks the remote for its tags (<c>git ls-remote</c>, no clone), keeps those
///     that match the source's tag pattern and are strict semantic versions, and resolves an
///     annotated tag to the commit it points to; that commit is the version's
///     <see cref="DiscoveredPackage.Revision"/>. Two tags that name the same version keep the
///     lexicographically smallest tag and a warning is logged.
///     </para>
///     <para>
///     <b>Acquiring</b> first confirms that the repository still lists the package's version at the
///     tag and commit it was given (a package this source would not list is refused before anything
///     is fetched), then fetches exactly one tag, shallow, into a temporary repository that
///     AgentControl creates from an empty template (so it has no hooks); checks that the fetched
///     tag is the commit that was listed; reads the tree of the tag and refuses the package when
///     any part of the package layout is a symbolic link, a submodule, an entry named
///     <c>.git</c> or a name that is not safe on every file system, when a layout folder is not a
///     folder or a root file is not a file, when two entries differ only in letter case or Unicode
///     form (they would be one file on some file systems), or when it is too large; writes the
///     validated files itself, byte for byte as they are in the commit, with no checkout (so no
///     filter, attribute or line-ending conversion applies); deletes the <c>.git</c> folder (and
///     publishes nothing when it cannot be deleted); and keeps the result in a cache folder named
///     by the repository and the commit, so the same commit is never fetched twice and different
///     repositories never share a folder. Anything in the repository outside the layout (including
///     links) is never written. The reader returned lists and reads only the layout, reports the
///     files identity of what it delivers like every other source, and carries the commit as its
///     source revision <c>git-commit:{sha}</c>.
///     </para>
///     <para>
///     Credentials come only from the user's own git setup (credential helper, ssh keys); see
///     <see cref="GitRemoteClient"/>. Work that the user did not start must be created with
///     <see cref="GitSourceOptions.Interactive"/> false: it can then never prompt, and a missing
///     sign-in is reported as <see cref="PackageSourceFailureKind.AuthenticationFailed"/>. When
///     <see cref="GitSourceOptions.EnforceMinimumGitVersion"/> is set, an old git is refused before
///     the first listing or fetch with the <see cref="GitRemoteException"/> of kind
///     <see cref="GitFailureKind.GitTooOld"/> itself. Thread-safe;
///     two simultaneous acquisitions of the same commit are safe (the one that loses the final
///     rename uses the winner's folder).
///     </para>
///     <para>
///     <b>Readers hold their folder</b>: a reader returned by <see cref="AcquireAsync"/> holds a lease
///     on its commit folder until it is disposed, so no acquisition of this process prunes the folder
///     while the reader is in use; another process pruning the same cache is not stopped.
///     </para>
/// </remarks>
internal sealed class GitSourceProvider : IPackageSourceProvider, IGitTagPreview
{
    /// <summary>
    ///     The name of the file that marks a cache folder as complete and names its commit.
    /// </summary>
    internal const string MarkerFileName = ".agentcontrol-commit";

    /// <summary>
    ///     The most files a package layout may hold.
    /// </summary>
    internal const int MaxLayoutFiles = 2000;

    /// <summary>
    ///     The most bytes the files of a package layout may hold: 50 MB.
    /// </summary>
    internal const long MaxLayoutBytes = 50L * 1024 * 1024;

    /// <summary>
    ///     How many non-matching tag names a preview shows.
    /// </summary>
    private const int MaxSamples = 5;

    /// <summary>
    ///     The names Windows reserves for devices; a file or folder cannot have one of them (with or
    ///     without an extension). Windows also reserves the superscript-digit forms <c>COM¹ COM² COM³
    ///     LPT¹ LPT² LPT³</c> (U+00B9, U+00B2, U+00B3), which are written as escapes here.
    /// </summary>
    private static readonly string[] ReservedDeviceNames =
    [
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        "COM\u00b9", "COM\u00b2", "COM\u00b3", "LPT\u00b9", "LPT\u00b2", "LPT\u00b3"
    ];

    /// <summary>
    ///     How old a temporary folder must be before another acquisition removes it.
    /// </summary>
    private static readonly TimeSpan StaleTemporaryAge = TimeSpan.FromHours(1);

    /// <summary>
    ///     The package ID this source offers.
    /// </summary>
    private readonly string _packageId;

    /// <summary>
    ///     The validated repository address.
    /// </summary>
    private readonly GitRepositoryAddress _address;

    /// <summary>
    ///     The validated tag pattern.
    /// </summary>
    private readonly GitTagPattern _pattern;

    /// <summary>
    ///     The sub-folder of the repository that holds the layout, or <see langword="null"/>.
    /// </summary>
    private readonly string? _subfolder;

    /// <summary>
    ///     The folder under which this repository's commits are kept.
    /// </summary>
    private readonly string _repositoryFolder;

    /// <summary>
    ///     The settings.
    /// </summary>
    private readonly GitSourceOptions _options;

    /// <summary>
    ///     The hardened git runner.
    /// </summary>
    private readonly GitRemoteClient _client;

    /// <summary>
    ///     Receives diagnostics.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    ///     Initializes a provider for one Git source.
    /// </summary>
    /// <param name="definition">The source definition; must be a valid Git source.</param>
    /// <param name="options">The settings.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when the definition is not a Git source, has a blank
    ///     alias, or the cache root is not absolute.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the settings of the source are not
    ///     valid (an invalid address, tag pattern, sub-folder or package ID, for example in a
    ///     hand-edited settings file): the source then fails closed, before git is ever run.</exception>
    public GitSourceProvider(PackageSourceDefinition definition, GitSourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.ResolveGitExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Alias);
        if (definition.Type != PackageSourceType.Git)
        {
            throw new ArgumentException($"Source '{definition.Alias}' is not a Git source.", nameof(definition));
        }

        var problem = definition.GetGitSettingsProblem();
        if (problem is not null ||
            !GitRepositoryAddress.TryParse(definition.Location, out var address, out _) ||
            !GitTagPattern.TryCreate(definition.TagPattern, out var pattern, out _))
        {
            throw new InvalidOperationException(
                $"Git source '{definition.Alias}' is not configured correctly. {problem}".TrimEnd());
        }

        var cacheRoot = options.CacheRoot ?? GitSourceOptions.GetDefaultCacheRoot();
        if (!Path.IsPathFullyQualified(cacheRoot))
        {
            throw new ArgumentException("The Git cache root must be an absolute path.", nameof(options));
        }

        Alias = definition.Alias;
        _options = options;
        _address = address!;
        _pattern = pattern!;
        _packageId = definition.PackageIds[0];
        _subfolder = string.IsNullOrEmpty(definition.Subfolder) ? null : definition.Subfolder;
        _repositoryFolder = Path.Combine(Path.GetFullPath(cacheRoot), _address.CacheKey);
        _logger = options.Logger ?? AppLogging.Factory.CreateLogger<GitSourceProvider>();
        _client = new GitRemoteClient(new GitRemoteOptions(
            options.ResolveGitExecutable,
            Path.Combine(Path.GetFullPath(cacheRoot), ".work"),
            options.Interactive,
            options.Logger,
            options.ExtraConfiguration,
            options.ExtraEnvironment,
            options.ListTimeout,
            options.FetchTimeout));
    }

    /// <inheritdoc />
    public string Alias { get; }

    /// <summary>
    ///     Gets the folder under which this repository's fetched commits are kept; exposed so tests
    ///     can inspect the cache.
    /// </summary>
    internal string RepositoryCacheFolder => _repositoryFolder;

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListPackageIdsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<string>>([_packageId]);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiscoveredPackage>> ListVersionsAsync(
        string packageId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        if (!string.Equals(packageId, _packageId, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var tags = await ListTagsAsync(_pattern.ListingGlob, cancellationToken);
        return MatchVersions(tags).Packages;
    }

    /// <inheritdoc />
    public async Task<GitTagPreview> PreviewAsync(CancellationToken cancellationToken)
    {
        var tags = await ListTagsAsync("refs/tags/*", cancellationToken);
        var (packages, others) = MatchVersions(tags);
        var samples = others.Where(name => GitTagPattern.IsSafeTagName(name)).Take(MaxSamples).ToList();
        return new GitTagPreview(
            tags.Count,
            [.. packages.OrderByDescending(package => package.Version)],
            others.Count,
            samples);
    }

    /// <inheritdoc />
    public async Task<IPackageContentReader> AcquireAsync(DiscoveredPackage package, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        cancellationToken.ThrowIfCancellationRequested();

        // Refuse anything this source did not report: acquisition must never be redirected to a
        // tag or commit it did not list
        if (!string.Equals(package.SourceAlias, Alias, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(package.PackageName, _packageId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Package '{package.PackageName}' was not discovered at source '{Alias}'.", nameof(package));
        }

        var commit = package.Revision;
        if (commit is null || !GitRemoteClient.ObjectIdPattern().IsMatch(commit) ||
            !_pattern.TryMatch(package.Location, out var tagVersion) || tagVersion != package.Version)
        {
            throw new ArgumentException(
                $"Package '{package.PackageName}' does not name a tag and commit of source '{Alias}'.", nameof(package));
        }

        var target = new GitRemoteTarget(_address.Value, ToTransport(_address.Kind));
        var final = Path.Combine(_repositoryFolder, commit);

        // The lease comes first: from here until the reader is disposed no acquisition in this
        // process prunes this commit's folder, so a reader that was handed out never loses its
        // files to another acquisition. A folder that does not exist yet is harmless to lease.
        var lease = CacheLeases.Acquire(final);
        try
        {
            try
            {
                if (!IsCompleteCacheFolder(final, commit))
                {
                    // Nothing is fetched that this source would not list: the tag and the commit it was
                    // given must be what the repository offers now, under the same pattern
                    await RequireListedAsync(package, cancellationToken);
                    await EnsureSupportedGitAsync(cancellationToken);
                    await FetchIntoCacheAsync(target, package.Location, commit, final, cancellationToken);
                }
                else
                {
                    // Used now: the newest folders are the ones kept when the cache is pruned
                    TryTouch(final);
                }
            }
            catch (GitRemoteException ex)
            {
                throw Translate(ex);
            }

            var layoutRoot = _subfolder is null
                ? final
                : Path.Combine(final, _subfolder.Replace('/', Path.DirectorySeparatorChar));
            // The identity of the files is computed from the files themselves, like any source's
            var reader = DirectoryPackageContentReader.OpenLayoutRoot(layoutRoot);
            await Task.Run(() => Prune(commit), CancellationToken.None);

            // The reader owns the lease from here on: it is released when the reader is disposed
            return new LeasedPackageContentReader(reader, lease);
        }
        catch
        {
            // No reader was handed out, so nothing else would ever release the lease
            lease.Dispose();
            throw;
        }
    }

    /// <summary>
    ///     Tells whether a commit's cache folder is held by a reader of this process that has not
    ///     been disposed yet; exposed so tests can observe the leases.
    /// </summary>
    /// <param name="folder">The cache folder of the commit.</param>
    /// <returns><see langword="true"/> while at least one reader holds the folder.</returns>
    internal static bool IsCacheFolderLeased(string folder) => CacheLeases.IsLeased(folder);

    /// <summary>
    ///     Refuses a package that this source would not list now, so a caller cannot make the source
    ///     fetch a tag or commit it never offered.
    /// </summary>
    /// <param name="package">The package that is about to be fetched.</param>
    /// <param name="cancellationToken">Cancels the listing.</param>
    /// <returns>A task that completes when the package is confirmed to be listed.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the repository does not list the
    ///     package's version at the package's tag and commit (the tag was moved or removed, or it
    ///     never named that commit).</exception>
    /// <exception cref="PackageSourceAccessException">Thrown when the repository cannot be listed
    ///     for a reason the user can act on.</exception>
    private async Task RequireListedAsync(DiscoveredPackage package, CancellationToken cancellationToken)
    {
        var tags = await ListTagsAsync(_pattern.ListingGlob, cancellationToken);
        var listed = MatchVersions(tags).Packages.FirstOrDefault(candidate => candidate.Version == package.Version);
        if (listed is null ||
            !string.Equals(listed.Location, package.Location, StringComparison.Ordinal) ||
            !string.Equals(listed.Revision, package.Revision, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Version {package.Version} of source '{Alias}' is not offered by the repository at that tag and commit " +
                "any more (the tag was moved or removed). Refresh the list and try again.");
        }
    }

    /// <summary>
    ///     Converts the address kind to the transport git may use.
    /// </summary>
    /// <param name="kind">The address kind.</param>
    /// <returns>The transport.</returns>
    private static GitTransport ToTransport(GitAddressKind kind) => kind switch
    {
        GitAddressKind.Https => GitTransport.Https,
        GitAddressKind.Ssh => GitTransport.Ssh,
        _ => GitTransport.File
    };

    /// <summary>
    ///     Lists the tags of the repository, translating git failures.
    /// </summary>
    /// <param name="refGlob">The glob that narrows the listing.</param>
    /// <param name="cancellationToken">Cancels the listing.</param>
    /// <returns>The tags.</returns>
    private async Task<IReadOnlyList<GitRemoteTag>> ListTagsAsync(string refGlob, CancellationToken cancellationToken)
    {
        try
        {
            await EnsureSupportedGitAsync(cancellationToken);
            return await _client.ListTagsAsync(
                new GitRemoteTarget(_address.Value, ToTransport(_address.Kind)), refGlob, cancellationToken);
        }
        catch (GitRemoteException ex)
        {
            throw Translate(ex);
        }
    }

    /// <summary>
    ///     Checks the version of git when the options ask for it.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>A task that completes when git is known to be supported.</returns>
    /// <exception cref="GitRemoteException">Thrown when git is too old or cannot be run.</exception>
    private Task EnsureSupportedGitAsync(CancellationToken cancellationToken) =>
        _options.EnforceMinimumGitVersion ? _client.EnsureSupportedVersionAsync(cancellationToken) : Task.CompletedTask;

    /// <summary>
    ///     Sorts the tags of the repository into package versions and other tags.
    /// </summary>
    /// <param name="tags">The tags the remote reported.</param>
    /// <returns>The package versions (one per version) and the names of the other tags.</returns>
    private (List<DiscoveredPackage> Packages, List<string> Others) MatchVersions(IReadOnlyList<GitRemoteTag> tags)
    {
        var byVersion = new Dictionary<PackageVersion, GitRemoteTag>();
        var others = new List<string>();
        foreach (var tag in tags)
        {
            // A name a server reports is untrusted: it only goes further when it is a safe tag name
            // and a version under the pattern
            if (!_pattern.TryMatch(tag.Name, out var version) || version is null)
            {
                others.Add(tag.Name);
                continue;
            }

            if (byVersion.TryGetValue(version, out var existing))
            {
                var keep = string.CompareOrdinal(tag.Name, existing.Name) < 0 ? tag : existing;
                _logger.LogWarning(
                    "Tags '{First}' and '{Second}' of source '{Source}' name the same version; keeping '{Kept}'.",
                    existing.Name, tag.Name, Alias, keep.Name);
                byVersion[version] = keep;
                continue;
            }

            byVersion[version] = tag;
        }

        var packages = byVersion
            .Select(pair => new DiscoveredPackage(_packageId, pair.Key, pair.Value.Name, Alias, pair.Value.CommitSha))
            .ToList();
        return (packages, others);
    }

    /// <summary>
    ///     Tells whether a cache folder is a complete fetch of a commit.
    /// </summary>
    /// <param name="folder">The cache folder.</param>
    /// <param name="commit">The commit it should hold.</param>
    /// <returns><see langword="true"/> when the folder exists and its marker names exactly the
    ///     commit.</returns>
    private static bool IsCompleteCacheFolder(string folder, string commit)
    {
        try
        {
            var marker = Path.Combine(folder, MarkerFileName);
            return Directory.Exists(folder) && File.Exists(marker) &&
                   string.Equals(File.ReadAllText(marker).Trim(), commit, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Fetches the tag into a temporary folder, validates the layout, writes the validated blobs
    ///     to it as raw bytes, and publishes the folder as the cache folder of the commit.
    /// </summary>
    /// <param name="target">The remote.</param>
    /// <param name="tag">The tag to fetch.</param>
    /// <param name="commit">The commit the tag was listed at.</param>
    /// <param name="final">The cache folder to publish.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the folder is published (or a concurrent acquisition
    ///     published it).</returns>
    /// <exception cref="InvalidOperationException">Thrown when the tag moved, or the layout is not
    ///     acceptable.</exception>
    private async Task FetchIntoCacheAsync(
        GitRemoteTarget target, string tag, string commit, string final, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_repositoryFolder);
        SweepStaleTemporaryFolders();
        var temporary = Path.Combine(_repositoryFolder, ".tmp-" + Guid.NewGuid().ToString("N"));
        try
        {
            await _client.InitRepositoryAsync(temporary, cancellationToken);
            var fetched = await _client.FetchTagAsync(target, temporary, tag, cancellationToken);
            if (!string.Equals(fetched, commit, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Tag '{tag}' of source '{Alias}' changed while it was being fetched. Refresh the list and try again.");
            }

            var layoutFiles = await ValidateLayoutAsync(temporary, commit, cancellationToken);
            await MaterializeLayoutAsync(temporary, layoutFiles, cancellationToken);

            await Task.Run(() => PublishCheckedOutFolder(temporary, final, commit), CancellationToken.None);
        }
        finally
        {
            // After a successful publish the temporary folder no longer exists. A folder that
            // cannot be removed here is never reused (its name is unique, it holds no completeness
            // marker unless it was published) and is swept by a later acquisition.
            await Task.Run(() => DeleteDirectory(temporary), CancellationToken.None);
        }
    }

    /// <summary>
    ///     Writes the validated layout files into the temporary folder as the exact bytes of their
    ///     blobs. No checkout is used, so no filter driver, attribute or line-ending conversion the
    ///     repository or the user's git configuration names can run or change the content.
    /// </summary>
    /// <param name="repository">The temporary repository, which is also the root the files are
    ///     written under (at their repository paths).</param>
    /// <param name="files">The validated layout files.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when every file is written.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a file cannot be written.</exception>
    private async Task MaterializeLayoutAsync(
        string repository, IReadOnlyList<GitTreeEntry> files, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(repository).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        try
        {
            await _client.ReadBlobsAsync(
                repository, files, entry => OpenLayoutFile(root, entry), MaxLayoutBytes, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "A file of the fetched package of source '{Source}' could not be written.", Alias);
            throw new InvalidOperationException(
                $"Source '{Alias}': a file of this tag's agent package could not be written to the cache folder " +
                $"'{_repositoryFolder}'. Check that there is room and that no program blocks the folder, then try again.",
                ex);
        }
    }

    /// <summary>
    ///     Creates the file a layout entry is written to, refusing anything that would land outside
    ///     the root or over an existing file.
    /// </summary>
    /// <param name="root">The full path of the root folder, ending with a separator.</param>
    /// <param name="entry">The validated entry.</param>
    /// <returns>The new, empty file opened for writing.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the path is not below the root.</exception>
    /// <exception cref="IOException">Thrown when the file already exists or cannot be created.</exception>
    private Stream OpenLayoutFile(string root, GitTreeEntry entry)
    {
        var full = Path.GetFullPath(Path.Combine(root, entry.Path.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(root, comparison))
        {
            throw new InvalidOperationException(
                $"Source '{Alias}': '{Printable(entry.Path)}' in this tag would be written outside the package folder.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        // CreateNew: a file that is already there is never overwritten, and no sharing, so nothing
        // else can write to it while it is filled
        return new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
    }

    /// <summary>
    ///     Removes the <c>.git</c> folder from the temporary folder, verifies that it is
    ///     gone, marks the folder complete and publishes it. Fails closed: when the repository
    ///     metadata cannot be removed nothing is marked or published.
    /// </summary>
    /// <param name="temporary">The temporary folder holding the written layout.</param>
    /// <param name="final">The cache folder to publish.</param>
    /// <param name="commit">The commit.</param>
    /// <exception cref="InvalidOperationException">Thrown when <c>.git</c> cannot be removed or the
    ///     folder cannot be published.</exception>
    private void PublishCheckedOutFolder(string temporary, string final, string commit)
    {
        try
        {
            var gitFolder = Path.Combine(temporary, ".git");
            (_options.RemoveGitFolder ?? DeleteDirectoryOrThrow)(gitFolder);
            if (Directory.Exists(gitFolder) || File.Exists(gitFolder))
            {
                throw new IOException("The repository metadata folder is still there.");
            }

            File.WriteAllText(Path.Combine(temporary, MarkerFileName), commit);
            Publish(temporary, final, commit);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "The fetched package of source '{Source}' could not be finished in the cache.", Alias);
            throw new InvalidOperationException(
                $"Source '{Alias}': the fetched package could not be finished in the cache folder (the Git metadata " +
                "could not be removed, or the folder could not be moved into place). Close programs that may be using " +
                $"the folder '{_repositoryFolder}' and try again.",
                ex);
        }
    }

    /// <summary>
    ///     Reads the tree of the fetched commit and refuses it when the layout is not acceptable.
    /// </summary>
    /// <param name="repository">The temporary repository.</param>
    /// <param name="commit">The commit.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The regular files of the layout, as listed (paths from the repository root): the
    ///     only content that is ever written to the cache.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the sub-folder is not a folder, a
    ///     layout folder is not a folder or a root file is not a file, a layout path is a symbolic
    ///     link, a submodule or contains a <c>.git</c> name or another name that is not safe on every
    ///     file system, the layout is too large, or there is no layout.</exception>
    private async Task<List<GitTreeEntry>> ValidateLayoutAsync(string repository, string commit, CancellationToken cancellationToken)
    {
        var prefix = _subfolder is null ? string.Empty : _subfolder + "/";

        // The named entries themselves first: a sub-folder, .github, a layout folder or a root file
        // that is a link or a submodule is refused outright (git cannot list below a link, so the
        // recursive listing below would silently find nothing there). They are listed in separate
        // calls because git lists everything below a named folder when another pathspec reaches
        // into it.
        if (_subfolder is not null)
        {
            var folderEntries = await _client.ListTreeAsync(repository, commit, [_subfolder], recursive: false, cancellationToken);
            if (folderEntries.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Source '{Alias}': the folder '{_subfolder}' does not exist in this tag.");
            }

            foreach (var entry in folderEntries.Where(entry => entry.Path == _subfolder))
            {
                RequireOrdinaryEntry(entry, isFolderExpected: true);
            }
        }

        var top = new List<string> { prefix + ".github" };
        top.AddRange(PackageContentExtractor.LayoutRootFiles.Select(file => prefix + file));
        foreach (var entry in await _client.ListTreeAsync(repository, commit, top, recursive: false, cancellationToken))
        {
            RequireOrdinaryEntry(entry, isFolderExpected: entry.Path == prefix + ".github");
        }

        // Each layout folder that exists must be a folder: a file (or link) where a managed folder
        // belongs would pass a listing of files, and extracting it would delete the managed folder
        // and put nothing back
        var folderRoots = PackageContentExtractor.LayoutFolders.Select(path => prefix + path).ToList();
        foreach (var entry in await _client.ListTreeAsync(repository, commit, folderRoots, recursive: false, cancellationToken))
        {
            RequireOrdinaryEntry(entry, isFolderExpected: true);
        }

        var layoutPaths = PackageContentExtractor.LayoutPaths.Select(path => prefix + path).ToList();
        var files = await _client.ListTreeAsync(repository, commit, layoutPaths, recursive: true, cancellationToken);
        long bytes = 0;
        foreach (var entry in files)
        {
            RequireOrdinaryEntry(entry, isFolderExpected: false);
            bytes += entry.Size ?? 0;
        }

        // Checked on every platform: the cache may be read on a file system that folds case or
        // Unicode forms, where two such entries would be one file or folder
        var collision = FindFoldedPathCollision(files.Select(entry => entry.Path));
        if (collision is not null)
        {
            throw new InvalidOperationException(
                $"Source '{Alias}': this tag's agent package has files or folders whose names differ only in letter case " +
                $"or Unicode form ({collision}), which would collide on some file systems and is not allowed.");
        }

        if (files.Count > MaxLayoutFiles || bytes > MaxLayoutBytes)
        {
            throw new InvalidOperationException(
                $"Source '{Alias}': this tag's agent files are too large (more than {MaxLayoutFiles} files or " +
                $"{MaxLayoutBytes / (1024 * 1024)} MB).");
        }

        // Every listed file must sit where the layout allows a file: below a layout folder, or as a
        // root file itself (a blob at a layout folder's own path was refused above)
        var allowedFolders = folderRoots.Select(path => path + "/").ToList();
        var allowedFiles = PackageContentExtractor.LayoutRootFiles.Select(file => prefix + file).ToHashSet(StringComparer.Ordinal);
        var stray = files.FirstOrDefault(entry =>
            !allowedFiles.Contains(entry.Path) &&
            !allowedFolders.Any(folder => entry.Path.StartsWith(folder, StringComparison.Ordinal)));
        if (stray is not null)
        {
            throw new InvalidOperationException(
                $"Source '{Alias}': '{Printable(stray.Path)}' in this tag is not part of the agent package layout.");
        }

        if (files.Count == 0)
        {
            throw new InvalidOperationException(
                $"Source '{Alias}': this tag has no agent package layout" +
                (_subfolder is null ? "." : $" under '{_subfolder}'.") +
                " It must contain .github/agents, .github/standards, .github/templates, .github/skills or release-notes.md.");
        }

        return [.. files];
    }

    /// <summary>
    ///     Refuses a tree entry that is a symbolic link, a submodule, has a <c>.git</c> path
    ///     component or another name that is not safe on every file system, or is not the expected
    ///     kind of entry.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="isFolderExpected">Whether the entry must be a folder (otherwise a regular file).</param>
    /// <exception cref="InvalidOperationException">Thrown when the entry is not acceptable.</exception>
    private void RequireOrdinaryEntry(GitTreeEntry entry, bool isFolderExpected)
    {
        foreach (var part in entry.Path.Split('/'))
        {
            if (IsGitFolderName(part))
            {
                throw new InvalidOperationException(
                    $"Source '{Alias}': this tag's agent package contains a '.git' entry, which is not allowed.");
            }

            if (!IsSafePathComponent(part))
            {
                throw new InvalidOperationException(
                    $"Source '{Alias}': '{Printable(entry.Path)}' in this tag has a name that is not safe on every file system, " +
                    "which is not allowed in an agent package.");
            }
        }

        var acceptable = isFolderExpected
            ? entry.Type == "tree" && entry.Mode is "040000" or "40000"
            : entry.Type == "blob" && entry.Mode is "100644" or "100755";
        if (!acceptable)
        {
            var kind = entry.Mode switch
            {
                "120000" => "a symbolic link",
                "160000" => "a submodule",
                _ => isFolderExpected ? "not a folder" : "not an ordinary file"
            };
            throw new InvalidOperationException(
                $"Source '{Alias}': '{Printable(entry.Path)}' in this tag is {kind}, which is not allowed in an agent package.");
        }
    }

    /// <summary>
    ///     Determines whether a path component names the repository metadata folder on any file
    ///     system: <c>.git</c> in any letter case, with characters that HFS+ ignores, or as the
    ///     short name NTFS gives it.
    /// </summary>
    /// <param name="component">One component of a repository path.</param>
    /// <returns><see langword="true"/> when the component would be the <c>.git</c> folder.</returns>
    internal static bool IsGitFolderName(string component)
    {
        // Zero-width and directional characters that HFS+ drops when it compares names, and the
        // trailing dots and spaces NTFS ignores
        var visible = string.Concat(component.Where(character => !IsHfsIgnorable(character))).TrimEnd('.', ' ');
        return visible.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
               (visible.StartsWith("git~", StringComparison.OrdinalIgnoreCase) && visible.Length > 4 &&
                visible[4..].All(char.IsAsciiDigit));
    }

    /// <summary>
    ///     Determines whether a character is one that HFS+ ignores in names.
    /// </summary>
    /// <param name="character">The character.</param>
    /// <returns><see langword="true"/> for the zero-width, directional and byte-order characters.</returns>
    private static bool IsHfsIgnorable(char character) =>
        character is (>= '\u200c' and <= '\u200f') or (>= '\u202a' and <= '\u202e') or
            (>= '\u206a' and <= '\u206f') or '\ufeff';

    /// <summary>
    ///     Determines whether one component of a path can be created with the same meaning on every
    ///     file system the cache may be read on: not empty, not <c>.</c> or <c>..</c>, none of the
    ///     characters Windows forbids (including the <c>:</c> that starts an alternate data stream
    ///     and the backslash), no control character, no trailing dot or space, and not the name of
    ///     a Windows device.
    /// </summary>
    /// <param name="component">One component of a repository path.</param>
    /// <returns><see langword="true"/> when the component is safe to create.</returns>
    internal static bool IsSafePathComponent(string component)
    {
        if (component.Length == 0 || component is "." or ".." ||
            component.EndsWith('.') || component.EndsWith(' ') ||
            component.Any(character => char.IsControl(character) || "\\:*?\"<>|".Contains(character, StringComparison.Ordinal)))
        {
            return false;
        }

        // A device name is reserved with or without an extension (CON, con.txt, COM1.md)
        var stem = component.Split('.')[0].TrimEnd(' ');
        return !ReservedDeviceNames.Contains(stem, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Finds two paths that a case-folding or normalizing file system would treat as the same
    ///     file or folder.
    /// </summary>
    /// <param name="paths">The git paths (<c>/</c>-separated) of the validated layout entries.</param>
    /// <returns>A short description of the first colliding pair of names (with control characters
    ///     replaced), or <see langword="null"/> when no two names collide. Every path component
    ///     counts, so a file and a folder that differ only in case collide as well. A name that
    ///     cannot be normalized is reported as colliding with itself.</returns>
    internal static string? FindFoldedPathCollision(IEnumerable<string> paths)
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var current = string.Empty;
            foreach (var part in path.Split('/'))
            {
                current = current.Length == 0 ? part : current + "/" + part;
                string key;
                try
                {
                    // Unicode composition first (NFC and NFD spellings are one name on some file
                    // systems), then case
                    key = current.Normalize(NormalizationForm.FormC).ToUpperInvariant();
                }
                catch (ArgumentException)
                {
                    return $"'{Printable(current)}' is not valid text";
                }

                if (!seen.TryAdd(key, current) && !string.Equals(seen[key], current, StringComparison.Ordinal))
                {
                    return $"'{Printable(seen[key])}' and '{Printable(current)}'";
                }
            }
        }

        return null;
    }

    /// <summary>
    ///     Makes a repository path safe to show in a message.
    /// </summary>
    /// <param name="text">The path.</param>
    /// <returns>The path with control characters replaced.</returns>
    private static string Printable(string text) =>
        string.Concat(text.Select(character => char.IsControl(character) ? '?' : character));

    /// <summary>
    ///     Makes the temporary folder the cache folder of the commit, or accepts that a concurrent
    ///     acquisition already did.
    /// </summary>
    /// <param name="temporary">The finished temporary folder.</param>
    /// <param name="final">The cache folder.</param>
    /// <param name="commit">The commit.</param>
    /// <exception cref="IOException">Thrown when the folder cannot be published and no complete
    ///     folder exists.</exception>
    private static void Publish(string temporary, string final, string commit)
    {
        try
        {
            if (Directory.Exists(final) && !IsCompleteCacheFolder(final, commit))
            {
                // A leftover from a failed or foreign write: never trusted, replaced
                DeleteDirectory(final);
            }

            Directory.Move(temporary, final);
        }
        catch (IOException) when (IsCompleteCacheFolder(final, commit))
        {
            // Another acquisition published the same commit first; its folder is as good as ours
        }
    }

    /// <summary>
    ///     Removes temporary folders that a failed or killed acquisition left behind, when they are
    ///     old enough that no running acquisition can own them.
    /// </summary>
    private void SweepStaleTemporaryFolders()
    {
        try
        {
            foreach (var folder in Directory.EnumerateDirectories(_repositoryFolder, ".tmp-*"))
            {
                if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(folder) > StaleTemporaryAge)
                {
                    DeleteDirectory(folder);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Stale temporary Git folders of source '{Source}' could not be removed.", Alias);
        }
    }

    /// <summary>
    ///     Removes the oldest cached commits of this repository beyond the configured number,
    ///     never the one just used and never one that a reader still holds. Failures are logged and
    ///     ignored.
    /// </summary>
    /// <remarks>
    ///     A folder held by a reader of this process (see <see cref="CacheLeases"/>) is skipped, so
    ///     the cache can hold more than the configured number while readers are open. Only readers
    ///     of this process are known: another process pruning the same cache is not stopped (the
    ///     folder times still keep the recently used commits).
    /// </remarks>
    /// <param name="keepCommit">The commit just used.</param>
    private void Prune(string keepCommit)
    {
        try
        {
            var folders = Directory.EnumerateDirectories(_repositoryFolder)
                .Where(folder => GitRemoteClient.ObjectIdPattern().IsMatch(Path.GetFileName(folder)))
                .Select(folder => new DirectoryInfo(folder))
                .OrderByDescending(folder => folder.LastWriteTimeUtc)
                .ToList();
            var keep = Math.Max(_options.MaxCachedCommits, 1);
            foreach (var path in folders.Skip(keep).Where(f => f.Name != keepCommit).Select(f => f.FullName))
            {
                // Decided and deleted under the registry's lock, so a reader cannot take the folder
                // between the check and the deletion; a folder a reader holds is left alone
                CacheLeases.DeleteUnlessLeased(path, () => DeleteDirectory(path));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Old Git cache folders of source '{Source}' could not be pruned.", Alias);
        }
    }

    /// <summary>
    ///     Marks a cache folder as used now, ignoring a failure.
    /// </summary>
    /// <param name="folder">The folder.</param>
    private static void TryTouch(string folder)
    {
        try
        {
            Directory.SetLastWriteTimeUtc(folder, DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only the order in which old folders are pruned depends on it
        }
    }

    /// <summary>
    ///     Deletes a folder tree, clearing read-only attributes first (git writes read-only object
    ///     files, which Windows refuses to delete), ignoring a folder that is not there.
    /// </summary>
    /// <param name="path">The folder.</param>
    internal static void DeleteDirectory(string path)
    {
        try
        {
            DeleteDirectoryOrThrow(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder that cannot be removed now is swept later (temporary folders) or ignored (cache)
        }
    }

    /// <summary>
    ///     Deletes a folder tree like <see cref="DeleteDirectory"/> but lets a failure through, for
    ///     the places where a folder that stays must stop the work.
    /// </summary>
    /// <param name="path">The folder.</param>
    /// <exception cref="IOException">Thrown when something in the folder cannot be deleted.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when access to the folder is denied.</exception>
    internal static void DeleteDirectoryOrThrow(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(file);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
            }
        }

        Directory.Delete(path, recursive: true);
    }

    /// <summary>
    ///     Converts a failure of git into the exception the provider contract promises.
    /// </summary>
    /// <param name="ex">The failure.</param>
    /// <returns>A <see cref="PackageSourceAccessException"/> for a cause the user can act on,
    ///     otherwise an <see cref="InvalidOperationException"/> with git's redacted summary.</returns>
    private Exception Translate(GitRemoteException ex) => ex.Kind switch
    {
        // Returned as it is (not wrapped, no alias): the application recognizes this kind to
        // explain how to get a newer git
        GitFailureKind.GitTooOld => ex,
        GitFailureKind.AuthenticationRequired => new PackageSourceAccessException(
            PackageSourceFailureKind.AuthenticationFailed,
            $"Sign-in needed for the Git repository of source '{Alias}'. AgentControl uses your own Git sign-in " +
            "(Git Credential Manager or another credential helper, or your SSH keys): sign in there, then try again.",
            ex),
        GitFailureKind.HostKeyUnverified => new PackageSourceAccessException(
            PackageSourceFailureKind.Unreachable,
            $"The SSH host key of the Git server of source '{Alias}' is not trusted yet. Connect once with ssh " +
            "from a terminal and accept the host key, then try again.",
            ex),
        GitFailureKind.NotFound => new PackageSourceAccessException(
            PackageSourceFailureKind.NotFound,
            $"The Git repository (or the requested tag) of source '{Alias}' was not found, or you do not have access to it.",
            ex),
        GitFailureKind.Unreachable => new PackageSourceAccessException(
            PackageSourceFailureKind.Unreachable,
            $"The Git repository of source '{Alias}' could not be reached. {ex.Message}",
            ex),
        GitFailureKind.TimedOut => new PackageSourceAccessException(
            PackageSourceFailureKind.TimedOut,
            $"Git did not answer for source '{Alias}' in time and was stopped. {ex.Message}",
            ex),
        _ => new InvalidOperationException($"Source '{Alias}': {ex.Message}", ex)
    };

    /// <summary>
    ///     The process-wide registry of cache folders that a reader still holds, so that pruning
    ///     never removes the files of a reader that was handed out.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Keyed by the full normalized path of the commit folder (compared without regard to case
    ///     where the file system usually ignores it), with a count of the readers holding it: every
    ///     provider instance of the process shares it, so two sources, or two providers of the same
    ///     source, that share a cache folder protect each other's readers. Thread-safe: one lock
    ///     guards the counts, and a prune decides and deletes under it, so a reader cannot take a
    ///     folder between the check and the deletion (taking a lease waits for a deletion in
    ///     progress and then finds the folder gone, which sends the acquisition to the fetch).
    ///     </para>
    ///     <para>
    ///     The registry only knows the readers of this process. Another process that prunes the same
    ///     cache is not stopped; the folder times, which pruning orders by, are the only protection
    ///     there.
    ///     </para>
    /// </remarks>
    private static class CacheLeases
    {
        /// <summary>
        ///     Guards <see cref="Counts"/> and serializes the pruning decisions.
        /// </summary>
        private static readonly object Gate = new();

        /// <summary>
        ///     The number of readers holding each folder; a folder with no reader has no entry.
        /// </summary>
        private static readonly Dictionary<string, int> Counts = new(
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        /// <summary>
        ///     Takes a lease on a commit folder.
        /// </summary>
        /// <param name="folder">The commit folder (it need not exist yet).</param>
        /// <returns>The lease; disposing it releases it, and only the first disposal counts.</returns>
        public static IDisposable Acquire(string folder)
        {
            var key = ToKey(folder);
            lock (Gate)
            {
                Counts[key] = Counts.GetValueOrDefault(key) + 1;
            }

            return new Lease(key);
        }

        /// <summary>
        ///     Tells whether any reader holds a folder.
        /// </summary>
        /// <param name="folder">The commit folder.</param>
        /// <returns><see langword="true"/> while at least one lease on it is open.</returns>
        public static bool IsLeased(string folder)
        {
            lock (Gate)
            {
                return Counts.ContainsKey(ToKey(folder));
            }
        }

        /// <summary>
        ///     Runs a deletion only when no reader holds the folder, deciding and deleting under the
        ///     registry's lock.
        /// </summary>
        /// <param name="folder">The commit folder about to be deleted.</param>
        /// <param name="delete">Deletes the folder; it must not take a lease (the lock is held).</param>
        public static void DeleteUnlessLeased(string folder, Action delete)
        {
            var key = ToKey(folder);
            lock (Gate)
            {
                if (!Counts.ContainsKey(key))
                {
                    delete();
                }
            }
        }

        /// <summary>
        ///     Gives a folder the key it is registered under.
        /// </summary>
        /// <param name="folder">The folder.</param>
        /// <returns>The full path without a trailing separator.</returns>
        private static string ToKey(string folder) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));

        /// <summary>
        ///     One reader's claim on a folder, released exactly once however often it is disposed.
        /// </summary>
        private sealed class Lease(string key) : IDisposable
        {
            /// <summary>
            ///     Set to 1 by the first disposal.
            /// </summary>
            private int _released;

            /// <inheritdoc />
            public void Dispose()
            {
                // Only the first disposal gives the count back, or one reader could release the
                // lease of another
                if (Interlocked.Exchange(ref _released, 1) != 0)
                {
                    return;
                }

                lock (Gate)
                {
                    var count = Counts.GetValueOrDefault(key) - 1;
                    if (count > 0)
                    {
                        Counts[key] = count;
                    }
                    else
                    {
                        Counts.Remove(key);
                    }
                }
            }
        }
    }

    /// <summary>
    ///     The reader <see cref="AcquireAsync"/> returns: the layout reader of the commit folder,
    ///     plus the lease that keeps the folder from being pruned until the reader is disposed.
    /// </summary>
    /// <remarks>
    ///     Delegates every call to the layout reader, so what is read, listed and identified is
    ///     exactly what that reader does. Disposal releases the reader and then the lease, exactly
    ///     once however often it is called (and even when the inner reader throws). Not thread-safe,
    ///     like the reader it wraps.
    /// </remarks>
    /// <param name="inner">The layout reader of the commit folder.</param>
    /// <param name="lease">The lease on the commit folder; owned by this reader from now on.</param>
    private sealed class LeasedPackageContentReader(IPackageContentReader inner, IDisposable lease) : IPackageContentReader
    {
        /// <summary>
        ///     Set to 1 by the first disposal.
        /// </summary>
        private int _disposed;

        /// <inheritdoc />
        public string Description => inner.Description;

        /// <inheritdoc />
        public string? ContentIdentity => inner.ContentIdentity;

        /// <inheritdoc />
        public IReadOnlyList<string> ListFiles() => inner.ListFiles();

        /// <inheritdoc />
        public Stream OpenRead(string relativePath) => inner.OpenRead(relativePath);

        /// <inheritdoc />
        public string? ReadText(string relativePath) => inner.ReadText(relativePath);

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                inner.Dispose();
            }
            finally
            {
                lease.Dispose();
            }
        }
    }
}
