### GitIgnoreEnsurer

![RepoSync Structure](RepoSyncView.svg)

#### Purpose

`GitIgnoreEnsurer` proactively ensures a repo's root `.gitignore` covers the four managed
agent folders (`.github/agents`, `.github/standards`, `.github/templates`, `.github/skills`)
immediately after a successful sync, so an agentic tool (or a developer) is far less likely to
accidentally commit package-managed content into a repo meant to keep it untracked. This runs
proactively, *before* an accidental commit can happen — it is a deliberate, narrow addition to
the otherwise purely advisory "Committed agent files" badge (see
`docs/design/agent-control/launcher-ui.md`), which continues to only warn *after* the fact.

Idempotency is deliberately implemented as a fixed marker-comment scan only — never
`git check-ignore`, and never any kind of gitignore-pattern/glob analysis. This keeps the unit
simple, dependency-free (no git invocation at all, and no git-repo-existence check), and
predictable: if a developer manually removes the marker comment but leaves the four
managed-folder lines behind, a subsequent call adds a second copy under a fresh marker. That
duplicate-block outcome is an explicitly accepted edge case, not a defect — simplicity was
deliberately chosen over precision, per the request that introduced this unit.

#### Data Model

`GitIgnoreEnsurer` holds no instance state; it is a static class.

**MarkerComment**: `const string` — the fixed marker comment (`# Added by AgentControl - agent
package folders`) whose presence anywhere in the file is the single source of truth for the
idempotency check.

**ManagedFolderPatterns**: `static readonly string[]` — the four gitignore-style folder
patterns (`.github/agents/`, `.github/standards/`, `.github/templates/`, `.github/skills/`)
appended alongside the marker comment.

#### Key Methods

**Ensure**: Ensures the repo's root `.gitignore` contains a marker-delimited block covering
the four managed agent folders, creating the file if it does not yet exist.

- *Parameters*: `string repoRoot`.
- *Returns*: `void`.
- *Preconditions*: `repoRoot` is not `null`.
- *Postconditions*: If the marker comment is already present anywhere in the existing file,
  this is a true no-op — no write occurs at all. Otherwise, the marker comment and the four
  managed-folder patterns are appended to the end of the file, preceded by a single blank-line
  separator only when needed (existing content is non-empty and does not already end in a
  blank line), so the appended block never visually runs into existing content. No
  pre-existing line is ever edited, reordered, or removed — the change is purely additive
  (`AgentControl-GitIgnoreEnsurer-Ensure`).

#### Error Handling

`Ensure` throws `ArgumentNullException` for a `null` `repoRoot`, and `InvalidOperationException`
when the `.gitignore` file cannot be read or written for an I/O reason — wrapping the
underlying `IOException`/`UnauthorizedAccessException` with a message naming the `.gitignore`
path. The marker-already-present no-op path never throws, since it performs no I/O beyond the
initial read.

#### Dependencies

- **PathHelpers** (`Utilities` subsystem) — `SafePathCombine` resolves the `.gitignore` file's
  path safely relative to the repo root.
- **.NET BCL** — `System.IO.File`/`Directory`. No git dependency of any kind: this unit never
  invokes git and performs no git-repo-existence check, per the request's explicit
  requirement.

#### Callers

- **RepoCardViewModel** — calls `Ensure` (via its private `EnsureGitIgnoreCoversManagedFolders`
  non-blocking wrapper) immediately after every successful `PackageZipExtractor.Extract` call
  in `ApplyPackageAndShowReleaseNotes`, covering both the "Select Package..." and "Upgrade"
  flows.
