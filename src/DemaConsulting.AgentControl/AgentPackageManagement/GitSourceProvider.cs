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

using DemaConsulting.AgentControl.GitIntegration;
using DemaConsulting.AgentControl.Logging;
using DemaConsulting.AgentControl.RepoSync;
using Microsoft.Extensions.Logging;

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
internal sealed record GitSourceOptions(
    Func<string> ResolveGitExecutable,
    string? CacheRoot = null,
    bool Interactive = false,
    ILogger? Logger = null,
    IReadOnlyList<string>? ExtraConfiguration = null,
    IReadOnlyDictionary<string, string?>? ExtraEnvironment = null,
    TimeSpan? ListTimeout = null,
    TimeSpan? FetchTimeout = null,
    int MaxCachedCommits = GitSourceOptions.DefaultMaxCachedCommits)
{
    /// <summary>
    ///     The default number of fetched commits kept per repository.
    /// </summary>
    public const int DefaultMaxCachedCommits = 5;

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
///     <b>Acquiring</b> fetches exactly one tag, shallow, into a temporary repository that
///     AgentControl creates from an empty template (so it has no hooks); checks that the fetched
///     tag is the commit that was listed; reads the tree of the tag and refuses the package when
///     any part of the package layout is a symbolic link, a submodule or an entry named
///     <c>.git</c>, or when it is too large; checks out only the layout paths; deletes the
///     <c>.git</c> folder; and keeps the result in a cache folder named by the repository and the
///     commit, so the same commit is never fetched twice and different repositories never share a
///     folder. Anything in the repository outside the layout (including links) is never checked
///     out. The reader returned lists and reads only the layout and reports the identity
///     <c>git-commit:{sha}</c>.
///     </para>
///     <para>
///     Credentials come only from the user's own git setup (credential helper, ssh keys); see
///     <see cref="GitRemoteClient"/>. Work that the user did not start must be created with
///     <see cref="GitSourceOptions.Interactive"/> false: it can then never prompt, and a missing
///     sign-in is reported as <see cref="PackageSourceFailureKind.AuthenticationFailed"/>. Thread-safe;
///     two simultaneous acquisitions of the same commit are safe (the one that loses the final
///     rename uses the winner's folder).
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
        try
        {
            if (!IsCompleteCacheFolder(final, commit))
            {
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
        var reader = DirectoryPackageContentReader.OpenLayoutRoot(layoutRoot, PackageContentIdentity.FromGitCommit(commit));
        await Task.Run(() => Prune(commit), CancellationToken.None);
        return reader;
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
            return await _client.ListTagsAsync(
                new GitRemoteTarget(_address.Value, ToTransport(_address.Kind)), refGlob, cancellationToken);
        }
        catch (GitRemoteException ex)
        {
            throw Translate(ex);
        }
    }

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
    ///     Fetches the tag into a temporary folder, validates and checks out the layout, and
    ///     publishes the folder as the cache folder of the commit.
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

            var checkoutPaths = await ValidateLayoutAsync(temporary, commit, cancellationToken);
            await _client.CheckoutPathsAsync(temporary, commit, checkoutPaths, cancellationToken);

            await Task.Run(
                () =>
                {
                    DeleteDirectory(Path.Combine(temporary, ".git"));
                    File.WriteAllText(Path.Combine(temporary, MarkerFileName), commit);
                    Publish(temporary, final, commit);
                },
                CancellationToken.None);
        }
        finally
        {
            // After a successful publish the temporary folder no longer exists
            await Task.Run(() => DeleteDirectory(temporary), CancellationToken.None);
        }
    }

    /// <summary>
    ///     Reads the tree of the fetched commit and refuses it when the layout is not acceptable.
    /// </summary>
    /// <param name="repository">The temporary repository.</param>
    /// <param name="commit">The commit.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The layout paths that exist in the commit, as git paths from the repository root:
    ///     the only paths that are checked out.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the sub-folder is not a folder, a
    ///     layout path is a symbolic link, a submodule or contains a <c>.git</c> name, the layout
    ///     is too large, or there is no layout.</exception>
    private async Task<List<string>> ValidateLayoutAsync(string repository, string commit, CancellationToken cancellationToken)
    {
        var prefix = _subfolder is null ? string.Empty : _subfolder + "/";

        // The named entries themselves first: a sub-folder, .github or a root file that is a link or
        // a submodule is refused outright (git cannot list below a link, so the recursive listing
        // below would silently find nothing there). They are listed in separate calls because
        // git lists everything below a named folder when another pathspec reaches into it.
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

        var layoutPaths = PackageContentExtractor.LayoutPaths.Select(path => prefix + path).ToList();
        var files = await _client.ListTreeAsync(repository, commit, layoutPaths, recursive: true, cancellationToken);
        long bytes = 0;
        foreach (var entry in files)
        {
            RequireOrdinaryEntry(entry, isFolderExpected: false);
            bytes += entry.Size ?? 0;
        }

        if (files.Count > MaxLayoutFiles || bytes > MaxLayoutBytes)
        {
            throw new InvalidOperationException(
                $"Source '{Alias}': this tag's agent files are too large (more than {MaxLayoutFiles} files or " +
                $"{MaxLayoutBytes / (1024 * 1024)} MB).");
        }

        // Only the layout paths that really exist are named for the checkout
        var existing = layoutPaths
            .Where(path => files.Any(entry => entry.Path == path || entry.Path.StartsWith(path + "/", StringComparison.Ordinal)))
            .ToList();
        if (existing.Count == 0)
        {
            throw new InvalidOperationException(
                $"Source '{Alias}': this tag has no agent package layout" +
                (_subfolder is null ? "." : $" under '{_subfolder}'.") +
                " It must contain .github/agents, .github/standards, .github/templates, .github/skills or release-notes.md.");
        }

        return existing;
    }

    /// <summary>
    ///     Refuses a tree entry that is a symbolic link, a submodule, has a <c>.git</c> path
    ///     component, or is not the expected kind of entry.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="isFolderExpected">Whether the entry must be a folder (otherwise a regular file).</param>
    /// <exception cref="InvalidOperationException">Thrown when the entry is not acceptable.</exception>
    private void RequireOrdinaryEntry(GitTreeEntry entry, bool isFolderExpected)
    {
        if (entry.Path.Split('/').Any(part => part.Equals(".git", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Source '{Alias}': this tag's agent package contains a '.git' entry, which is not allowed.");
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
                _ => "not an ordinary file or folder"
            };
            throw new InvalidOperationException(
                $"Source '{Alias}': '{entry.Path}' in this tag is {kind}, which is not allowed in an agent package.");
        }
    }

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
    ///     never the one just used. Failures are logged and ignored.
    /// </summary>
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
            foreach (var folder in folders.Skip(keep).Where(f => f.Name != keepCommit))
            {
                DeleteDirectory(folder.FullName);
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder that cannot be removed now is swept later (temporary folders) or ignored (cache)
        }
    }

    /// <summary>
    ///     Converts a failure of git into the exception the provider contract promises.
    /// </summary>
    /// <param name="ex">The failure.</param>
    /// <returns>A <see cref="PackageSourceAccessException"/> for a cause the user can act on,
    ///     otherwise an <see cref="InvalidOperationException"/> with git's redacted summary.</returns>
    private Exception Translate(GitRemoteException ex) => ex.Kind switch
    {
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
}
