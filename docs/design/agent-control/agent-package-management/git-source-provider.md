### GitSourceProvider

![AgentPackageManagement Structure](AgentPackageManagementView.svg)

#### Purpose

`GitSourceProvider` is the `IPackageSourceProvider` for one Git repository whose tags that match a pattern are the
versions of a single package (`AgentControl-AgentPackageManagement-GitSource`, `AgentControl-System-GitSources`,
`AgentControl-System-GitSourceSafety`). It lists the versions without a clone, acquires one version by fetching exactly
its tag, and delivers the package layout of that commit through a layout-root reader. Everything git-specific stays
here and in `GitRemoteClient`; the rest of the application sees an ordinary provider.

#### Data Model

**GitSourceOptions** (record): `ResolveGitExecutable`, `CacheRoot` (default `%LOCALAPPDATA%/AgentControl/git-cache`; the
card passes a `git-cache` folder inside the isolated configuration directory when there is one), `Interactive`, `Logger`,
the test seams `ExtraConfiguration`/`ExtraEnvironment`, `ListTimeout`/`FetchTimeout` and `MaxCachedCommits` (5).

**GitTagPreview** (record) and **IGitTagPreview**: what the tags look like under the pattern (`TotalTags`, `Matching`,
`NonMatchingCount`, `NonMatchingSamples`), used by the editor to preview a pattern
(`AgentControl-GitSourceProvider-TagPreview`).

**DiscoveredPackage** fields as this provider fills them: `PackageName` = the configured package ID, `Location` = the tag
name, `Revision` = the commit, `SourceAlias` = the source alias.

**Cache layout**: `{CacheRoot}/{addressKey}/{commit}/` holds the checked-out layout (below `{subfolder}` when one is
configured) plus the marker file `.agentcontrol-commit` containing exactly the commit; temporary folders are
`{CacheRoot}/{addressKey}/.tmp-{guid}`; the client's work folders are `{CacheRoot}/.work`.

#### Key Methods

**Constructor**: Validates the definition - Git type, alias, `GetGitSettingsProblem` - and throws
`InvalidOperationException` (message without the address) before any git runs
(`AgentControl-GitSourceProvider-ValidateDefinition`).

**ListPackageIdsAsync**: The single configured ID, no network (`AgentControl-GitSourceProvider-ListPackageId`).

**ListVersionsAsync**: Another package ID gives an empty list. Otherwise `GitRemoteClient.ListTagsAsync` with the pattern's
`ListingGlob` and the address' only transport; each tag is matched with `GitTagPattern.TryMatch` (which vets the name the
server sent); two tags naming one version keep the smallest tag name and log a warning; each version carries the tag's
peeled commit as `Revision` (`AgentControl-GitSourceProvider-ListVersionsByPattern`, `-ResolveAnnotatedTags`).

**PreviewAsync**: The same listing over `refs/tags/*`, sorted into versions and other tags.

**AcquireAsync**: Refuses a package that was not discovered at this source, is for another package or whose tag and
commit do not match the pattern and a full commit id (`-AcquireOnlyFromOwnSource`). If the cache folder for the commit
is complete (marker equals the commit) it is used without any network call and touched for pruning
(`-CacheByAddressAndCommit`). Otherwise, in a temporary folder:

1. `init` from an empty template (`GitRemoteClient.InitRepositoryAsync`), then fetch exactly the tag; the commit that
   `rev-parse` reports must equal `Revision`, otherwise "the tag changed while it was being fetched"
   (`-TagMovedBetweenListAndFetch`).
2. Validate the tree *before* checkout, with separate `ls-tree` calls because git lists everything below a named folder
   when another pathspec reaches into it: the sub-folder (if any) must exist and be an ordinary folder; `.github` must be
   a folder and `release-notes.md`/`AGENTS.md` regular files when present; then a recursive listing of the layout paths
   (`.github/agents|standards|templates|skills`, the two root files) in which every entry must be a regular blob
   (mode 100644/100755) - a symbolic link (120000), a submodule (160000) or a path component named `.git` is refused with
   a message saying so, more than 2000 files or 50 MB is refused, and at least one layout path must exist
   (`-RejectLinksAndSubmodulesInLayout`, `-Subfolder`).
3. Check out only the layout paths that exist; delete `.git` (clearing read-only attributes first - git writes read-only
   object files, which Windows refuses to delete) (`-AcquireShallowTag`, `-ExcludeGitFolder`); write the marker; publish
   by renaming the folder to `{commit}`. If the rename fails because another acquisition published first, that folder is
   used; a folder without a valid marker is deleted, not trusted.
4. On any failure or cancellation the temporary folder is removed; temporary folders older than an hour (a killed
   acquisition) are swept at the next acquisition.

The reader is `DirectoryPackageContentReader.OpenLayoutRoot(root[/subfolder], "git-commit:{commit}")`
(`-ReportContentIdentity`). After a successful acquisition the oldest cached commits beyond `MaxCachedCommits` are
deleted, never the one just used; failures are logged and ignored (`-CachePruning`).

**Failure translation**: `GitRemoteException` kinds become `PackageSourceAccessException` - `AuthenticationRequired` ->
`AuthenticationFailed` ("Sign-in needed ..."), `HostKeyUnverified` -> `Unreachable` (with the hint to connect once with
ssh), `NotFound`, `Unreachable`, `TimedOut`; everything else (git missing, unsafe repository owner, output too large)
becomes an `InvalidOperationException` with the client's redacted summary. Messages name the source alias, never the
address and never git's raw output (`-FailureClassification`).

#### Error Handling

`ArgumentException` for a package this source did not report; `InvalidOperationException` for a tag that moved while
fetching or a layout that is refused; `PackageSourceAccessException` as above; `OperationCanceledException` when the
token is cancelled, at every step (`-CancellationObserved`). `Interactive` only changes how git is run
(`-InteractiveFlagHonored`); hooks never run (`-HostileHooksNeverRun`, enforced in `GitRemoteClient`).

#### Dependencies

- **GitRemoteClient** (GitIntegration) - every git command.
- **GitRepositoryAddress**, **GitTagPattern**, **PackageSourceDefinition**, **PackageVersion**,
  **PackageContentIdentity** (same subsystem), **PackageSourceAccessException**.
- **DirectoryPackageContentReader**, **PackageContentExtractor.LayoutPaths** (RepoSync) - the layout reader and the
  single list of layout paths.

#### Callers

- **PackageSourceRegistry** - creates it for a Git source.
- **PackageVersionCache**, **RepoCardViewModel** and the Select Package window - through `IPackageSourceProvider`.
- The source editor (a later change) - through `IGitTagPreview`.
