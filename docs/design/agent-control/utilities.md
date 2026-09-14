## Utilities

![Utilities Structure](UtilitiesView.svg)

### Overview

The `Utilities` subsystem provides shared utility functions for the Agent Control. It
supplies reusable, independently testable helpers consumed by other subsystems. Its primary
responsibility is safe file-path manipulation, protecting callers from path-traversal
vulnerabilities when constructing paths from caller-supplied inputs, and detecting filesystem
reparse points (symlinks/junctions) that could otherwise redirect a file operation outside an
intended directory tree. The `Utilities` subsystem contains one unit: `PathHelpers`.

### Interfaces

**PathHelpers.SafePathCombine**: Combines a base path and a relative path, rejecting any result
that escapes the base directory.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Accepts `string basePath` and `string relativePath`. Returns the combined path
  produced by `Path.Combine(basePath, relativePath)` after verifying that the resolved result
  remains within `basePath`. Preserves the caller's relative/absolute style in the return value.
- *Constraints*: Throws `ArgumentNullException` for null inputs; throws `ArgumentException`
  when the combined path escapes the base directory; may propagate `NotSupportedException` or
  `PathTooLongException` from underlying BCL path operations.

**PathHelpers.FindReparsePointInAncestry**: Searches upward from a path to a root (inclusive of
both ends) for the first directory entry that is a reparse point (symlink or junction).

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Accepts `string root` and `string path`, where `path` is expected to be at or
  below `root`. Returns the first reparse-point path found while walking upward from `path` to
  `root`, or `null` if none is found. Uses `File.GetAttributes` rather than `Directory.Exists`
  so a dangling link (whose target no longer exists) is still detected.
- *Constraints*: Throws `ArgumentNullException` for null inputs; may propagate `IOException` or
  `UnauthorizedAccessException` from underlying filesystem access.

**PathHelpers.FindReparsePointInDescendants**: Recursively searches a directory tree
(inclusive of the directory itself) for the first reparse point.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Accepts `string directory`. Returns the first reparse-point path found in the
  directory itself or anywhere beneath it, or `null` if none is found.
- *Constraints*: Throws `ArgumentNullException` for null input; may propagate `IOException` or
  `UnauthorizedAccessException` from underlying filesystem access.

### Design

The `Utilities` subsystem contains only the `PathHelpers` unit. It has no dependencies on other
tool units or subsystems; it uses only .NET BCL types (`Path`, `File`, `Directory`,
`ArgumentNullException`).

`PathHelpers.SafePathCombine` is a pure, lexical utility method: it performs no file-system I/O,
holds no state, and throws immediately on invalid input. All calls to `SafePathCombine` in the
codebase originate from the `RepoConfig` subsystem (`RepoPinStore`, resolving the per-repo
`.agentcontrol.json` pin file path) and the `RepoSync` subsystem (`PackageZipExtractor`,
resolving managed-folder and zip-entry destination paths), each using it to keep a
caller-supplied repo root from being escaped by a malformed relative path.

`FindReparsePointInAncestry` and `FindReparsePointInDescendants` are the filesystem-aware
counterpart to `SafePathCombine`: because a reparse point cannot be detected lexically, these
methods perform real filesystem queries (`File.GetAttributes`) to find one. Both are pure query
functions - they report what they find via a nullable return value and never throw for the
"reparse point found" case, deliberately separating *detection* from the *policy* of what to do
about a finding. The sole caller of both methods is the `RepoSync` subsystem
(`PackageZipExtractor`), which applies that policy by throwing
`UnsafeRepositoryStateException` when either method returns non-null, refusing to extract into
or delete through a path reachable only via a symlink/junction.
