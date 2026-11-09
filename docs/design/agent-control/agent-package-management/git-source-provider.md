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
card passes a `git-cache` folder inside the isolated configuration directory when there is one), `Interactive`,
`Logger`,
the test seams `ExtraConfiguration`/`ExtraEnvironment`, `ListTimeout`/`FetchTimeout`, `MaxCachedCommits` (5) and
`EnforceMinimumGitVersion` (false; the application sets it to true). A further internal init-only seam,
`RemoveGitFolder`, replaces the removal of the `.git` folder so tests can simulate a removal that fails or silently does
nothing.

**GitTagPreview** (record) and **IGitTagPreview**: what the tags look like under the pattern (`TotalTags`, `Matching`,
`NonMatchingCount`, `NonMatchingSamples`), used by the editor to preview a pattern
(`AgentControl-GitSourceProvider-TagPreview`).

**DiscoveredPackage** fields as this provider fills them: `PackageName` = the configured package ID, `Location` = the
tag
name, `Revision` = the commit, `SourceAlias` = the source alias.

**The tag is derived, not stored.** A pin to this source records the commit (`git-commit:` identity) and the version,
never the tag: the tag of a version is whatever the source's tag pattern, as configured now, maps that version to. If
the pattern of a source is edited, a pinned version maps to a different tag, whose commit may differ from the one the
pin recorded; the listing then carries that other commit as the package's `Revision`, the comparison with the pin
reports changed content, and the application shows it as a moved tag and applies nothing until the user accepts it
with Change package (see the `RepoCardViewModel` design). No schema or provider change is needed for this.

**Cache layout** (`-CacheByAddressAndCommit`, `-CacheSeparatesRepositories`, `-CacheTrustsOnlyCompleteFolders`,
`-CacheSweepsTemporaryFolders`): `{CacheRoot}/{addressKey}/{commit}/` holds the written layout (below `{subfolder}` when
one is configured) plus the marker file `.agentcontrol-commit` containing exactly the commit; `{addressKey}` is the
`CacheKey` of the `GitRepositoryAddress` (the first 16 hexadecimal digits of a SHA-256 of the normalized address), so
two repositories never share a folder and a commit is never fetched twice; the marker is written last, so a folder
without a marker that names its commit is incomplete and is replaced, not trusted; temporary folders are
`{CacheRoot}/{addressKey}/.tmp-{guid}`; the client's work folders are `{CacheRoot}/.work`.

**Threat model of the cache folders**: AgentControl's own cache folders under the user's profile are trusted local
state. A process that can plant links or files there already runs with the user's privileges, so link redirection
inside the owned cache is out of scope and is not mitigated.

**Reader leases** (`-CachePruningSparesOpenReaders`, `-CacheReleasesCommitWhenReaderDisposed`): a process-wide,
thread-safe registry (the private static class `CacheLeases`) counts, by full normalized path of the commit folder
(compared ignoring case on Windows and macOS), the readers of this process that hold each folder. `AcquireAsync` takes
the lease before it looks at the folder - so no acquisition in the process can prune the folder between the marker check
and the reader being returned - and hands it to the reader it returns: the returned reader is a private wrapper
(`LeasedPackageContentReader`) that delegates to the layout reader and, when it is disposed, disposes the layout reader
and releases the lease, exactly once however often `Dispose` is called. An acquisition that fails releases its lease
itself, because no reader exists to do so. The registry is shared by every provider instance, so two sources over the
same cache protect each other's readers. Taking a lease, releasing it and the pruning decision happen under one lock, and
a prune deletes a folder under that lock, so a reader cannot take a folder between the check and the deletion (a lease
taken while a deletion runs waits, then finds the folder gone and fetches it again). **Residual risk**: only readers of
this process are known. Another process that prunes the same cache (a second AgentControl instance with the same cache
root) does not know of them and can delete a folder an open reader is using; the only protection there is that pruning
orders folders by last-write time, which every acquisition of a commit refreshes, so a recently acquired folder is among
the newest (a reader held for a long time is not protected). The leases also do not outlive the process.

#### Key Methods

**Constructor**: Validates the definition - Git type, alias, `GetGitSettingsProblem` - and throws
`InvalidOperationException` (message without the address, `-ValidateDefinitionMessageNeverRepeatsAddress`) before any
git
runs (`AgentControl-GitSourceProvider-ValidateDefinition`).

**ListPackageIdsAsync**: The single configured ID, no network (`AgentControl-GitSourceProvider-ListPackageId`).

**ListVersionsAsync**: Another package ID gives an empty list without running git
(`-ListVersionsNoVersionsForOtherPackage`). Otherwise (after the version check, below) one
`GitRemoteClient.ListTagsAsync` query - an `ls-remote`, no clone (`-ListVersionsWithoutClone`) - with the pattern's
`ListingGlob`, which narrows what the remote sends, and the address' only transport
(`-ListVersionsOnlyOverAddressTransport`); each tag is matched with `GitTagPattern.TryMatch` (which vets the name the
server sent and requires a strict Semantic Versioning 2.0 version), so tags that are not versions are skipped, and no
matching tag gives an empty list (`-ListVersionsByPattern`, `-ListVersionsEmptyWhenNoMatch`); two tags naming one
version
keep the smallest tag name (ordinal) and log a warning (`-ListVersionsDuplicateVersionKeepsSmallestTag`); each version
carries the tag's peeled commit as `Revision` (`-ResolveAnnotatedTags`).

**PreviewAsync**: The same listing over `refs/tags/*`, sorted into versions and other tags (`-TagPreview`).

**AcquireAsync**: Refuses a package that was not discovered at this source or is for another package
(`-AcquireOnlyFromOwnSource`), or whose tag and commit do not match the pattern and a full commit id
(`-AcquireRequiresTagAndCommit`), before any git runs. If the cache folder for the commit is complete (marker equals the
commit) it is used without any network call and touched for pruning (`-CacheByAddressAndCommit`): a complete folder
exists only for a commit this source acquired earlier, after the checks below. Otherwise (after the version check,
below) the package must be one the source would list *now* (`-AcquireOnlyListedVersions`): the tags are listed again
through the same listing as `ListVersionsAsync`, and the entry of the package's version must have the package's tag name
and commit - a hand-built package, a stale entry of a moved tag, a tag that lost to a smaller name for its version, or a
removed tag is refused with "the repository does not offer that version at that tag and commit any more ... Refresh the
list" before anything is fetched. Then, in a temporary folder:

1. `init` from an empty template (`GitRemoteClient.InitRepositoryAsync`), then fetch exactly the tag, shallow, with no
   other tags (`-AcquireShallowTag`); the commit that `rev-parse` reports must equal `Revision`, otherwise "the tag
   changed while it was being fetched" (`-TagMovedBetweenListAndFetch`; the tag can still move between the listing just
   made and the fetch). The fetch runs with the hardening of the client, so no hook runs (`-HostileHooksNeverRun`).
2. Validate the tree *before* anything is written, with separate `ls-tree` calls because git lists everything below a
   named folder when another pathspec reaches into it: the sub-folder (if any) must exist and be an ordinary folder
   (`-Subfolder`, `-RejectBadSubfolder`); `.github` must be a folder and `release-notes.md`/`AGENTS.md` regular files
   when present; each of the four layout folders (`.github/agents|standards|templates|skills`) that exists must be a
   folder, so that a file or a link where a managed folder belongs is refused (`-RejectLayoutEntryOfWrongKind`); then a
   recursive listing of the layout paths (the four folders, the two root files) in which every entry must be a regular
   blob (mode 100644/100755) lying below a layout folder or being a root file - a symbolic link (120000) or a submodule
   (160000) is refused with a message saying so (`-RejectLinksAndSubmodulesInLayout`; links elsewhere are never listed
   and so ignored, `-IgnoreLinksOutsideLayout`), more than 2000 files or 50 MB is refused, and at least one layout file
   must exist (`-RejectTagWithoutLayout`). Every path component must be safe on every file system
   (`IsSafePathComponent`): not empty, not `.` or `..`, no `\ : * ? " < > |` and no control character (so no drive, no
   alternate data stream), no trailing dot or space, and not a Windows device name (`CON`, `PRN`, `AUX`, `NUL`, `COM1`
   to `COM9`, `LPT1` to `LPT9`, and the superscript forms `COM¹ COM² COM³ LPT¹ LPT² LPT³`, with or without an
   extension) (`-RejectUnsafeEntryNames`); and no
   component may spell the repository metadata folder in any way a file system would (`IsGitFolderName`: `.git` in any
   letter case, with the zero-width and direction characters HFS+ ignores, with trailing dots or spaces that NTFS
   ignores, or as the short name `GIT~1`) (`-RejectGitEntryInLayout`). These checks matter because AgentControl writes
   the files itself (step 3), so nothing else enforces them. On every operating system the listed paths are also checked
   for names that a case-insensitive or normalizing file system would merge (`FindFoldedPathCollision`): every prefix of
   every path (so a file and a folder, or two folders, count as well as two files) gets a canonical key - Unicode
   normalization form C, then upper-cased with the invariant culture - and two different spellings with one key are a
   collision; the acquisition fails with a message that names the pair (control characters replaced), before anything is
   written, so nothing is cached (`-RejectFoldedPathCollisions`). The check does not depend on the platform because the
   cache may be read on a different file system than the one that wrote it.
3. Write the validated blobs and nothing else (`-AcquireOnlyLayout`): there is **no checkout**. A checkout would run
   whatever `filter.<name>.smudge`/`.process` driver the repository's `.gitattributes` selects from the user's own git
   configuration (the client switches off only the large-file filter), and would convert line endings and expand
   identifiers; so `GitRemoteClient.ReadBlobsAsync` reads the listed blob ids with `git cat-file --batch`, which applies
   no
   filter, attribute or conversion, and `OpenLayoutFile` creates each file under the temporary folder at its repository
   path with `FileMode.CreateNew` (an existing file is never overwritten) and no sharing, after checking that the full
   path is below the folder. The delivered bytes are exactly those of the commit (`-AcquireExactBlobBytes`,
   `-NoRepositoryFilters`); the budget given to the client is the layout size limit; the executable bit is not
   preserved because the files are read, never run. An `IOException` or `UnauthorizedAccessException` while writing
   becomes an `InvalidOperationException` that names the cache folder. Then, in one step that fails closed
   (`-FailClosedWhenGitFolderRemains`): delete `.git` (clearing read-only attributes first - git writes read-only object
   files, which Windows refuses to delete) and verify that neither a folder nor a file named `.git` is left
   (`-ExcludeGitFolder`); only then write the marker and publish by renaming the folder to `{commit}`. If the deletion
   throws, or the verification still finds `.git`, or the rename fails with no complete folder to fall back on, the
   `IOException`/`UnauthorizedAccessException` becomes an `InvalidOperationException` ("the fetched package could not be
   finished in the cache folder ... close programs that may be using the folder and try again") and nothing is
   published: the marker is written only after `.git` is gone, and the folder only becomes `{commit}` by the final
   rename. If the rename fails because another acquisition published first, that folder is used; a folder without a
   valid
   marker is deleted (best effort; if that fails the rename fails and so does the acquisition), not trusted
   (`-CacheTrustsOnlyCompleteFolders`).
4. On any failure or cancellation the temporary folder is removed, best effort. A temporary folder that cannot be
   removed
   is harmless: its name is unique and not a commit id, so it is never served from the cache and never counted by
   pruning (`-LeftoverTemporaryFolderNeverUsed`), and temporary folders older than an hour (also a killed acquisition)
   are swept at the next acquisition (`-CacheSweepsTemporaryFolders`).

**Version check**: when `EnforceMinimumGitVersion` is set, `GitRemoteClient.EnsureSupportedVersionAsync` runs first in
the tag listing (which `ListVersionsAsync` and `PreviewAsync` share) and in `AcquireAsync` before the fetch of a cache
miss (a cache hit needs no git). A `GitRemoteException` of kind `GitTooOld` is returned by the failure translation as it
is - not wrapped, not prefixed with the alias - so the application can recognize
`GitRemoteException { Kind: GitTooOld }`
and show its actionable message; it derives from `InvalidOperationException`, so code that only catches that type still
works (`-MinimumGitVersion`, `-GitTooOldReportedUnwrapped`; the version is checked once per executable,
`-MinimumGitVersionCheckedOnce`).

The reader is `DirectoryPackageContentReader.OpenLayoutRoot(root[/subfolder], "git-commit:{commit}")`
(`-ReportContentIdentity`), wrapped with the lease described above. After a successful acquisition the oldest cached
commits beyond `MaxCachedCommits` are deleted, never the one just used and never one a reader holds (so the cache can
temporarily hold more while readers are open, `-CachePruningSparesOpenReaders`); failures are logged and ignored
(`-CachePruning`).

**Failure translation**: `GitRemoteException` kinds become `PackageSourceAccessException` - `AuthenticationRequired` ->
`AuthenticationFailed` ("Sign-in needed ..."), `HostKeyUnverified` -> `Unreachable` (with the hint to connect once with
ssh, `-UntrustedHostKeyHint`), `NotFound`, `Unreachable`, `TimedOut`; `GitTooOld` is returned unchanged; everything else
(git missing, unsafe repository owner, output too large, a process that could not be stopped) becomes an
`InvalidOperationException` with the client's message. The client's messages are fixed sentences that never repeat git's
text (see *GitRemoteClient*), so the translated messages name the source alias, never the address and never git's output
(`-FailureClassification`, `-FailureMessagesNeverRepeatGitOutput`).

#### Error Handling

`ArgumentException` for a package this source did not report; `InvalidOperationException` for a version the repository
no
longer lists at that tag and commit, a tag that moved while fetching, a layout that is refused (including colliding or
unsafe names) or a package that could not be finished in the cache folder; `GitRemoteException` (an
`InvalidOperationException`) of kind `GitTooOld`; `PackageSourceAccessException` as above; `OperationCanceledException`
when the token is cancelled, at every step (`-CancellationObserved`). `Interactive` only changes how git is run
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
- **SourceEditorViewModel** and **MainWindowViewModel** (`LauncherUI`) - create one for the source editor's Test
  connection (the main window supplies the provider the settings window's connection test uses; the editor creates a
  default one when its owner supplies none), always with `Interactive` set because the user started the test; the
  editor lists the tags through `IPackageSourceProvider` and shows the tag preview through `IGitTagPreview`
  (`PreviewAsync`).
