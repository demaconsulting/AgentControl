### PathHelpers

![Utilities Structure](UtilitiesView.svg)

#### Purpose

`PathHelpers` is a static utility class providing path-safety primitives with two
complementary halves: `SafePathCombine` performs purely lexical (string-level) containment
validation, while `FindReparsePointInAncestry` and `FindReparsePointInDescendants` perform
filesystem-aware detection of reparse points (symlinks/junctions) that a lexical check alone
cannot see. Callers that assemble a path and then act on the filesystem at it should normally
use both: `SafePathCombine` to reject a textually-escaping path, then one of the reparse-point
finders to reject a link that would otherwise redirect an apparently-safe path outside its
intended root.

#### Data Model

`PathHelpers` holds no instance state. The class is `internal static` with no fields or
properties.

#### Key Methods

**SafePathCombine**: Safely combines a base path and a relative path.

- *Parameters*: `string basePath` — the base directory path; `string relativePath` — the
  relative path to append.
- *Returns*: `string` — the pre-resolved combined path (preserves the caller's
  relative/absolute style).
- *Preconditions*: Both `basePath` and `relativePath` are non-null.
- *Postconditions*: The returned path, when resolved to absolute form, is contained within
  `basePath`.

Validation steps: (1) reject null inputs via `ArgumentNullException.ThrowIfNull`; (2) call
`Path.Combine(basePath, relativePath)` to produce the candidate path; (3) resolve both
`basePath` and the candidate to absolute form with `Path.GetFullPath`; (4) compute
`Path.GetRelativePath(absoluteBase, absoluteCombined)` and reject if the result equals `".."`,
starts with `".."` followed by a directory separator character, or is itself rooted (absolute);
(5) return the pre-resolved `combinedPath` from step 2.

The containment check uses `Path.GetRelativePath` rather than string inspection to handle root
paths, platform case-sensitivity, and directory-separator normalization natively. The `".."`
check treats a double-dot segment as escaping only when it is the entire relative result or is
followed by a directory separator, avoiding false positives for valid names such as `"..data"`.

This method performs no file-system I/O; it is purely lexical and therefore cannot detect a
symlinked/junctioned directory silently redirecting a nominally-contained path outside its
intended root — see `FindReparsePointInAncestry` for that case.

**FindReparsePointInAncestry**: Searches upward from a path to (and including) a root boundary
for a directory that is itself a reparse point (symlink/junction).

- *Parameters*: `string root` — the already fully-resolved (`Path.GetFullPath`) boundary at
  which the upward walk stops (inclusive); `string path` — the already fully-resolved path
  whose ancestry is inspected.
- *Returns*: `string?` — the closest-to-`path` segment that is a reparse point, or `null` if no
  segment between `path` and `root` (inclusive of both ends) is one.
- *Preconditions*: Both `root` and `path` are non-null.

Reparse-point status is queried via `File.GetAttributes` rather than `Directory.Exists`: the
latter resolves (follows) a link to test whether its target exists, and so returns `false`
(silently missing the reparse point) for a *dangling* symlink/junction, whereas
`File.GetAttributes` reports the reparse point's own attributes without requiring its target to
exist. A path segment that does not exist yet (e.g. a destination directory a caller is about
to create) is not itself a finding; the walk simply continues upward past it. This method only
ever *reports* what it found — callers decide what policy to apply (e.g. throwing a
domain-specific exception, or treating a read-only existence check as "not present"). This only
reflects the state of the filesystem at the moment of the call; it does not eliminate a race
where a segment is replaced with a reparse point between this check and a caller's subsequent
file operation.

**FindReparsePointInDescendants**: Recursively searches a directory (inclusive) for the first
nested directory that is a reparse point (symlink/junction).

- *Parameters*: `string directory` — the already-existing directory (and its descendants) to
  search.
- *Returns*: `string?` — the path of the first reparse point found (`directory` itself, or a
  descendant), or `null` if none exists anywhere in the tree.
- *Preconditions*: `directory` is non-null.

Exists for callers that need to recursively delete, copy, or otherwise walk a directory tree
without following filesystem links nested inside it — unlike `Directory.Delete(path, true)`'s
recursive mode, which follows such links and can affect content outside the tree being
processed. The whole tree is searched up front, rather than interleaving this check with
file-by-file processing, so a caller can preflight an entire operation and fail closed before
acting on any part of the tree if a reparse point exists anywhere within it.

#### Error Handling

`SafePathCombine` throws `ArgumentNullException` for null inputs. It throws `ArgumentException`
(`"Invalid path component: {relativePath}"`) when the combined path escapes the base directory.
`NotSupportedException` and `PathTooLongException` may propagate from underlying BCL path
operations (`Path.Combine`, `Path.GetFullPath`). No logging or error accumulation is performed;
callers receive exceptions directly.

`FindReparsePointInAncestry` and `FindReparsePointInDescendants` throw `ArgumentNullException`
for null inputs, and may propagate `IOException`/`UnauthorizedAccessException` from
`File.GetAttributes`/`Directory.GetDirectories` when a path segment cannot be inspected for a
reason other than not existing (e.g. an ACL-restricted directory). Neither method throws a
domain-specific exception itself when a reparse point is found — that is a query result
(`string?`), not a failure; policy for what to do about a found reparse point belongs to the
caller.

#### Dependencies

- **.NET BCL** — `Path`, `File`, `Directory`, `ArgumentNullException`, and related types are the
  only dependencies. No other tool units or subsystems are used.

#### Callers

- **RepoPinStore** — calls `SafePathCombine` to construct the per-repo `.agentcontrol.json`
  pin file path in both `Load` and `Save`, so a malformed repo root path cannot escape the
  repo directory.
- **PackageZipExtractor** — calls `SafePathCombine` to construct managed-folder paths for
  `AllManagedFoldersExist` and zip-entry destination paths during `Extract`, so a malicious or
  malformed zip-entry name cannot write outside the target repo. Calls
  `FindReparsePointInAncestry` (via its own `EnsureNoSymlinkAncestors` wrapper, and directly from
  `ManagedFolderGenuinelyExists`) to reject a repo root/ancestor/managed-folder that is a
  reparse point, and `FindReparsePointInDescendants` (via `DeleteDirectoryRejectingReparsePoints`)
  to reject a managed folder whose contents include a nested reparse point before blind-deleting
  it.
