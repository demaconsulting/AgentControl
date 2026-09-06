# AgentControl Architecture

## Purpose

AgentControl is a desktop launcher application that lets a company distribute
proprietary AI-agent configuration (Copilot/agentic-CLI instruction files,
standards, templates, and skills) alongside public source repositories without
committing that proprietary content to source control. A developer uses
AgentControl to open a repo from a recent-repos list, have the tool ensure the
repo's `.github/agents`, `.github/standards`, `.github/templates`, and
`.github/skills` folders match the version pinned in the repo, optionally pull
the latest commits, and then launch their preferred agentic CLI tool (e.g.
GitHub Copilot CLI) in that repo's folder.

Primary users are individual developers at a company that maintains its own
proprietary agent packages on an internal file share, used once or more per
day as a quick-launch utility for AI-assisted development sessions.

## Scope

**Included:**

- Recent-repos list with per-repo launch, git-pull, and agent-package
  sync/upgrade actions
- Reading/writing a per-repo `.agentcontrol.json` pin file (package name +
  pinned semantic version)
- Fetching agent package zip files from a configurable filesystem path (local
  drive, mapped drive, or UNC path)
- Detecting when a newer agent package version is available and prompting the
  user to upgrade
- Blind-delete-and-replace sync of the four known agent folders
- Displaying package release notes in a non-modal window after upgrade
- Launching a configurable agentic CLI tool in a configurable/auto-detected
  shell, in the repo's working directory
- Per-user settings (package source path, git executable override, agent tool
  selection, shell preference)
- Repo-card status badges: upgrade-available, committed-agent-files warning,
  missing-repo, dirty-working-tree; per-repo current branch name; recent-repos
  search/filter and most-recently-launched sort order (with pinning/favorites);
  per-repo manual refresh; confirmation prompt before removing a repo from the
  recent-repos list
- Diagnostic file logging (Serilog-based) for troubleshooting process-launch
  and git-integration failures
- **Initial package selection** for a repo with no `.agentcontrol.json` pin
  yet: a "Select Package..." action that lists the distinct package base
  names discoverable at the configured source, lets the user pick one (and,
  within it, a version — defaulting to latest), then performs the same
  extract-and-pin sequence as an upgrade
- **Ensure-synced-before-launch:** clicking Launch first verifies the four
  known agent folders are actually present on disk for a repo that already
  has a pin; if any are missing (e.g. a fresh clone, or a `.gitignore`'d
  folder that was never unpacked on this machine), it re-extracts the
  *currently pinned* version (never silently jumps to latest) before
  proceeding to launch the agent tool. If no pin exists yet at Launch time,
  the user is prompted to use "Select Package..." first rather than the
  launch silently proceeding with no agent files
- A simple About dialog (app version, copyright, license) reachable from the
  main window, so the running build can be identified without inspecting
  file properties

**Excluded:**

- Merging/combining multiple agent packages into one repo (always exactly one
  package per repo)
- Zip signing/checksum verification or authenticated package sources
  (trust boundary is the maintainer of the shared package source path)
- Rollback/backup of agent files on upgrade failure
- Application-level logging (error message boxes are sufficient)
- Non-Windows installers for v1 (code stays cross-platform-capable via
  Avalonia, but only an MSI is produced initially)
- Version 1 supports only a filesystem-based package source (UNC/local/mapped
  drive); other source types (HTTP, package feeds) are explicitly deferred

## Technology Stack

- **Language/Framework**: C#, .NET 10
- **UI Framework**: Avalonia 12 (cross-platform capable; v1 distributed as a
  Windows MSI installer)
- **Testing**: xUnit.v3 4.0.0, headless unit tests where possible;
  FlaUI-based UI integration tests for end-to-end automation
- **Installer**: MSI (Windows) for v1
- **Data storage**: Per-user JSON files under the .NET special folder
  `Environment.SpecialFolder.ApplicationData` (`%APPDATA%\AgentControl\`) for
  AgentControl's own settings/recent-repos state; per-repo `.agentcontrol.json`
  at each repo's root for the version pin
- **External services**: none — filesystem-based package source only

## Software Structure

```text
AgentControl
├── LauncherUI (Avalonia) - recent repos list, launch button, settings page,
│                           upgrade notification badges/tooltips
├── GitIntegration - git status check (gates pull offer on clean working tree),
│                     git pull, configurable git executable path override
├── AgentPackageManagement - resolves available package versions and fetches
│                             agent package zips from the configured
│                             filesystem path (local/mapped/UNC)
├── RepoSync
│   ├── PackageZipExtractor - opens/validates the new zip, updates the pin
│   │                         file, deletes the 4 known folders, extracts the
│   │                         new files (excluding root-level files such as
│   │                         release-notes.md)
│   └── ReleaseNotesViewer - non-modal, resizable dialog displaying the new
│                            package's release-notes.md after a successful
│                            upgrade
├── RepoConfig - reads/writes the per-repo .agentcontrol.json pin file
│                (package name + pinned version; no "latest"/unpinned mode)
├── AgentToolLauncher - detects installed shells (prefers highest available
│                       PowerShell/pwsh, falls back to Windows PowerShell 5,
│                       falls back to cmd; uses the default shell on
│                       macOS/Linux) and launches the configured agentic CLI
│                       tool in the repo's working directory
└── Settings - per-user settings: package source path (editable textbox +
               folder-browse dialog), git executable path override, agent
               tool selection (Copilot CLI / Cursor / Claude Code / Custom
               command), shell/terminal preference
```

## Architectural Decisions

- **Single agent package per repo, permanently.** AgentControl never merges
  or stitches multiple agent packages together. If a team wants a combined
  set of agents/standards/templates/skills, they combine them externally into
  one package before publishing it. *Reason:* keeps sync logic simple and
  unambiguous — one pin, one source of truth, one delete-and-replace pass.

- **Mandatory version pinning — no "latest" tracking.** The `.agentcontrol.json`
  file always names an exact version. *Reason:* upgrades between agent package
  versions can require manual migration steps (documented in release notes),
  so silently floating to the newest version would risk applying breaking
  changes without the developer's awareness.

- **Blind delete of the four known folders** (`.github/agents`,
  `.github/standards`, `.github/templates`, `.github/skills`) rather than a
  precise diff against the previous package contents. *Reason:* simplicity was
  preferred over precision. *Accepted risk:* this can delete local
  customizations a developer may have manually added inside those folders
  outside the package's control — documented here as a known trade-off rather
  than solved with a manifest or old-zip diff.

- **Upgrade sequence and failure handling:** (1) open/validate the new zip —
  if it opens without error, its contents are assumed good; (2) delete the
  four old folders; (3) extract the new files (excluding root-level files
  like release-notes.md); (4) rewrite the `.agentcontrol.json` pin to the new
  version; (5) show the release notes in a non-modal dialog. The pin file is
  updated only after a successful extraction — deliberately later than the
  originally planned order (validate → pin → delete → extract) — so that a
  failure partway through extraction never leaves the repo pinned to a
  version whose files were not actually applied. There is no rollback on
  failure at any step — a message box reports the error and the developer is
  expected to resolve it manually, consistent with the tool's target audience
  of professional developers.

- **Package source is a plain filesystem path, not specifically UNC.** It may
  be a local drive letter, a mapped network drive, or a UNC path. No
  authentication, checksum, or signature verification is performed by
  AgentControl — the security and integrity of the shared path is the
  responsibility of whoever maintains it.

- **No elevation required.** AgentControl runs as a normal user-level
  application at all times (the MSI installer itself may prompt for
  elevation, but the running app never does).

- **Diagnostic file logging via Serilog.** Message boxes remain the primary
  in-the-moment error surface for users, but AgentControl also writes a
  rolling diagnostic log (daily files, 14-day retention via Serilog's
  built-in `retainedFileCountLimit` — no hand-rolled cleanup) under
  `<config-dir>\logs\`, to capture process-launch and git-integration detail
  (exit codes, timing, stderr, Win32 error codes) that a message box alone
  cannot show. This supersedes the original v1 "no application logging"
  decision after real-world flaky-process-launch troubleshooting showed a
  message box alone was insufficient to root-cause intermittent failures.

- **Configurable agent tool and shell, not hardcoded to Copilot CLI.** Settings
  expose a picker (Copilot CLI / Cursor / Claude Code / Custom command) plus a
  shell/terminal preference, because the correct terminal materially affects
  how well some CLI tools behave on Windows (e.g., Copilot CLI works best in a
  modern PowerShell).

- **Testability via config-dir override and stub executables.** A command-line
  option lets FlaUI-driven integration tests point AgentControl at an isolated
  `%APPDATA%`-equivalent folder containing a fully-controlled
  `.agentcontrol`-style settings JSON (recent repos, package source path, git
  path, agent tool command). Git and the agentic CLI tool are substituted with
  a small "arg-logger" helper executable that records its invocation
  arguments to a file, which tests assert against — giving fully
  hermetic, repeatable end-to-end automation without depending on git or a
  real agentic CLI tool being installed on the test machine. Avalonia 12's
  built-in AutomationId/UI Automation support is used for reliable FlaUI
  element lookup.

- **Repo-card information design.** Each recent-repo card shows: display name
  (user-editable alias) + full path, pinned package/version, current git
  branch name, and a row of status badges/icons, each with its own hover
  tooltip:
  - ⬆️ **Upgrade available** — pin is behind the newest version found in the
    package source.
  - ⚠️ **Committed agent files** — one or more of `.github/agents`,
    `.github/standards`, `.github/templates`, `.github/skills` are tracked by
    git at `HEAD` (i.e. accidentally committed into a repo that's meant to
    gitignore them). This is purely advisory — AgentControl does not modify
    git tracking state itself.
  - ❌ **Missing** — the repo path no longer exists on disk (deleted or an
    unmounted network/removable drive). Suppresses all other badges/actions
    for that card; offers a "Remove from list" action (never auto-removed,
    since the path may reappear).
  - 🔒 **Dirty working tree** — suppresses/disables the Pull action.

  The recent-repos list supports a search/filter box and sorts by
  most-recently-launched by default, with the ability to pin/favorite
  specific repos above that ordering. Each card has a manual refresh action
  (re-checks git state and the committed-agent-files badge without waiting
  for the next full app launch) and a confirmation prompt before "Remove
  from list" (a destructive action against AgentControl's own state, not the
  repository itself).

- **Repo-fact caching strategy.** Per-repo facts shown on a card differ in
  how safely they can be cached:
  - *Committed-agent-files badge* is cached keyed by the repo's current
    `HEAD` commit hash (cheap to obtain via `git rev-parse HEAD`) — it is a
    property of the git tree, so it is safe to skip re-running
    `git ls-files` against the four known folders when `HEAD` hasn't
    changed since the last check.
  - *Working-tree dirty/clean state* is **not** cacheable this way, since it
    reflects uncommitted local edits independent of any commit. Rather than
    caching, this check is deferred/lazy — computed when a card becomes
    visible or via manual refresh, not eagerly for every recent repo at app
    launch, to keep startup fast.
  - *Available package versions* in the package source are not repo-scoped
    at all (shared across every repo pinned to the same package name) and
    may involve slow network/UNC I/O — cached once per app session (or a
    short TTL), refreshed on app start or via manual refresh, rather than
    rescanned per repo-card.
  - *Repo-exists-on-disk* and the pin-file contents are cheap enough
    (`Directory.Exists`, one small JSON read) that they are always read
    fresh rather than cached.

- **UI icon convention.** Toolbar and repo-card actions use
  [Material.Icons.Avalonia](https://github.com/AvaloniaUI/Material.Icons) (MIT
  license, vector-based `MaterialIcon`/`MaterialIconKind`, Avalonia-12
  compatible) rather than raster images or Unicode glyphs. *Reason:* using
  icons more consistently reduces toolbar clutter (e.g. the previous
  editable-path-textbox + "Browse..." + "Add Repo" trio is now a single
  folder-plus icon button that browses and adds in one step; "Settings" is a
  gear icon; "About" is an info icon), while the favorite toggle's Unicode
  `★` literal is replaced by a proper vector `Star`/`StarOutline` icon bound
  to `IsFavorite` via `FavoriteIconConverter`. Primary, consequential actions
  (Launch, Pull) keep an icon *and* a text label to avoid ambiguity; purely
  secondary/utility actions with well-established glyphs (Settings, About,
  Add Repo, More actions, Favorite, and the "More actions" flyout's menu
  items) go icon-only. Every icon-only control carries a `ToolTip.Tip`
  describing its action, and no existing
  `AutomationProperties.AutomationId` is ever renamed or removed when a
  control is converted to icon-only or has its content restructured, since
  FlaUI integration tests locate elements by AutomationId, not by their
  visible content.

## Initial package selection and ensure-synced-before-launch

A gap was found in the initial implementation: a repo added with no
pre-existing `.agentcontrol.json` pin had no UI path to ever acquire one —
"Upgrade" only appears once a pin already exists, and `Launch` never checked
or synced agent files at all before starting the agentic CLI tool, contrary
to this document's own Purpose statement. The following closes that gap:

- **"Select Package..." action** replaces "Upgrade" in the repo card's "..."
  menu whenever `PinnedPackageName` is unset (the two are mutually
  exclusive — a repo is either never-pinned or already-pinned, never both).
  It opens a small modal dialog that:
  1. Enumerates the distinct package base names discoverable at the
     configured source directory (see the name/version splitting algorithm
     below) and lists them for the user to pick one.
  2. Once a name is chosen, lists that name's discoverable versions
     (descending), defaulting the selection to the latest.
  3. On confirm, runs the same extract-and-pin sequence as `Upgrade()`
     (validate zip → delete four folders if present → extract → write pin →
     show release notes), reusing `PackageZipExtractor`/`RepoPinStore`
     rather than duplicating that logic.
- **Package name/version splitting algorithm** (needed because
  `PackageSource` today only matches versions for an *already-known* name):
  for each `*.zip` file's base name, scan its hyphens left-to-right; for each
  hyphen position, test whether the substring *after* that hyphen parses via
  `PackageVersion.TryParse`. The first hyphen position (leftmost) whose
  suffix parses successfully splits the file into
  `(name = prefix, version = suffix)`; a file with no such split point is
  skipped (not a recognized package file). This mirrors `PackageVersion`'s
  own "first hyphen starts the prerelease" rule, so it correctly handles
  package names that themselves contain hyphens (e.g.
  `contoso-agents-1.2.0.zip` → name `contoso-agents`, version `1.2.0`) as
  long as no numeric-looking prefix segment could itself be mistaken for a
  version (an accepted edge-case limitation, consistent with this codebase's
  existing simplicity-over-precision stance).
- **Ensure-synced-before-launch:** `Launch()` now checks, for a repo that
  already has a pin, whether all four known agent folders exist on disk.
  If any are missing, it re-extracts the *currently pinned* version (never
  auto-upgrades to latest — that remains a deliberate, separate user action)
  before proceeding to launch the agent tool. This covers the common case of
  a freshly cloned repo whose `.gitignore`'d agent folders were never
  unpacked on this machine. If no pin exists at all when Launch is clicked,
  the launch is stopped with a message directing the user to
  "Select Package..." first, rather than silently launching the agent tool
  with no agent files present.
- **About dialog:** a simple, non-modal "About AgentControl" window
  (app version via the existing `Program.Version`, copyright, license text)
  reachable via a new toolbar/menu entry next to Settings.

## Open Concerns

1. 🟢 **LOW** Package source scaling: only a filesystem path is supported for
   v1; HTTP(S)/authenticated/package-feed sources are explicitly deferred to a
   future release and will need their own design pass when prioritized.
2. 🟢 **LOW** Cross-platform distribution: Avalonia code is kept
   platform-portable, but only a Windows MSI is planned for v1 — packaging for
   macOS/Linux is deferred until there is customer demand.
3. 🟢 **LOW** Blind-delete trade-off: any local customizations a developer adds
   inside the four managed folders will be silently removed on the next
   upgrade; this is accepted risk, not a defect, but should be called out in
   user-facing documentation.
