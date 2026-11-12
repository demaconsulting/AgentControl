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
- Reading/writing a per-repo `.agentcontrol.json` pin file (package name,
  pinned semantic version, and whether the user has declined the AGENTS.md
  template offer for this repo)
- Fetching agent packages from the sources the user configures: a folder of
  zip files, a folder or server of NuGet packages, or a Git repository (local
  drive, mapped drive, UNC path, https/ssh address)
- Detecting when a newer agent package version is available and prompting the
  user to upgrade
- Blind-delete-and-replace sync of the four known agent folders
- Displaying package release notes in a non-modal window after upgrade
- **AGENTS.md template offer:** an agent package zip may optionally contain a
  root-level `AGENTS.md` entry (same convention as the existing root-level
  `release-notes.md` entry — outside the four managed folders, so it is never
  blind-deleted/replaced on sync). When a sync/extract happens (initial
  package selection, upgrade, or the ensure-synced-before-launch best-effort
  sync) and the repo has no root-level `AGENTS.md` of its own, and the
  package being applied has a template to offer, the user is shown a modal
  Yes/No dialog asking whether to place the template at the repo root,
  clearly noting they are responsible for customizing it afterward. A
  pre-existing `AGENTS.md` is never touched or overwritten, and a user's
  decline is persisted in `.agentcontrol.json` so they are never re-prompted
  for that repo again
- Launching a configurable agentic CLI tool in a configurable/auto-detected
  shell, in the repo's working directory
- Per-user settings (package source path, git executable override, agent tool
  selection, shell preference)
- Personal organization and appearance, stored only in the user's own settings
  (never in a repo): favorites, repo groups shown as collapsible sections,
  card colors, an optional repo picture (`icon.png`/`logo.png`) on the card,
  and a light/dark/system color theme; every change is saved before it is
  applied
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
  extract-and-pin sequence as an upgrade (including the AGENTS.md template
  offer described above)
- **Ensure-synced-before-launch:** clicking Launch first attempts a best-effort
  sync of the four known agent folders for a repo that already has a pin; if
  any are missing (e.g. a fresh clone, or a `.gitignore`'d folder that was
  never unpacked on this machine), it re-extracts the *currently pinned*
  version (never silently jumps to latest). This sync attempt is purely
  informational and never blocks the launch: a repo with committed agent
  files skips the sync entirely (its managed folders are never touched), a
  repo with no pin at all launches anyway with an informational message, and
  a failed re-extraction surfaces a non-blocking warning while the launch
  still proceeds. A re-extraction here also triggers the AGENTS.md template
  offer described above, consistent with the other two sync call sites
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
- Package source kinds other than a folder of zip packages, a folder of NuGet
  packages (local/mapped/UNC), a NuGet-package server and a Git repository (one
  repository = one package; its SemVer tags matching a configurable tag pattern
  are the versions), for example plain HTTP downloads. AgentControl never stores
  credentials: servers and Git use the user's own credential helper, SSH keys or
  ssh-agent

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
- **External services**: only the package sources the user configures — a NuGet
  server or a remote Git repository (reached through the user's own `git`,
  at least the minimum supported Git version, stated in the requirements and the
  user guide); the folder sources need no service

## Software Structure

```text
AgentControl
├── LauncherUI (Avalonia) - recent repos list, launch button, settings page,
│   │                       upgrade notification badges/tooltips
│   ├── MainWindowViewModel - owns the recent-repos list; creates a
│   │                         RepoCardViewModel per repo and a
│   │                         RepoGroupHeaderViewModel per list section;
│   │                         saves every change (candidate copy -> save ->
│   │                         commit -> refresh) through Settings
│   ├── RepoCardViewModel - one repo's status and actions; asks
│   │                       RepoImageLocator for the repo's picture, validates
│   │                       group names with RepoGroupName and card colors
│   │                       with CardColorPalette
│   ├── RepoImageLocator - finds and safely decodes a repo's icon.png/logo.png
│   │                      (called by RepoCardViewModel only)
│   ├── RepoGroupHeaderViewModel - a section's collapsible header row; raises
│   │                              toggle/rename/remove requests that
│   │                              MainWindowViewModel handles or forwards
│   ├── GroupNameViewModel - validates the group-name prompt with
│   │                        RepoGroupName; shown by the main window when a
│   │                        card asks for a new group or a header for a rename
│   ├── DialogBehavior - Tab cycle, initial focus and Escape, applied by every
│   │                    dialog window
│   ├── SettingsWindowViewModel, SourceEditorViewModel,
│   │   SelectPackageWindowViewModel, CredentialPromptViewModel - the dialogs
│   └── supporting types (documented in the LauncherUI design, not units):
│       CardColorPalette (built from the Settings card-color names),
│       PathEllipsisText (card path control),
│       ThemePreferenceDisplayNameConverter (theme picker text)
├── GitIntegration - git status check (gates pull offer on clean working tree),
│                     git pull, configurable git executable path override;
│                     the user's own Git credential helper (saved package-feed
│                     sign-ins); hardened git against remote repositories for
│                     the Git package source (tag listing, one-tag shallow
│                     fetch, tree listing, raw blob reading - never a checkout)
├── AgentPackageManagement - resolves the available package versions of each
│                             configured source and hands the chosen package's
│                             content to RepoSync: a folder of zip packages, a
│                             NuGet package folder or server, or a Git
│                             repository (its SemVer tags matching a tag
│                             pattern are the versions; one tag is fetched
│                             shallowly and read from its commit; the content
│                             identity is the identity of the files)
├── RepoSync
│   ├── PackageZipExtractor - opens/validates the new zip, updates the pin
│   │                         file, deletes the 4 known folders, extracts the
│   │                         new files (excluding root-level files such as
│   │                         release-notes.md and AGENTS.md), and can read
│   │                         either root-level file's content without
│   │                         extracting it
│   └── ReleaseNotesViewer - non-modal, resizable dialog displaying the new
│                            package's release-notes.md after a successful
│                            upgrade
├── RepoConfig - reads/writes the per-repo .agentcontrol.json pin file
│                (package name + pinned version + content identity of the
│                files (files-sha256) + whether the user declined
│                the AGENTS.md template offer; no package source, no Git
│                commit and no "latest"/unpinned mode)
├── AgentToolLauncher - detects installed shells (prefers highest available
│                       PowerShell/pwsh, falls back to Windows PowerShell 5,
│                       falls back to cmd; uses the default shell on
│                       macOS/Linux) and launches the configured agentic CLI
│                       tool in the repo's working directory
└── Settings - per-user settings: package source path (editable textbox +
    │          folder-browse dialog), git executable path override, agent
    │          tool selection (Copilot CLI / Cursor / Claude Code / Custom
    │          command), shell/terminal preference, color theme, and the
    │          user's own repo groups, card colors and collapsed sections
    ├── SettingsStore - loads (repairing unusable values, such as an unknown
    │                   card color) and saves the settings file
    └── RepoGroupName - the rules for group names, used by SettingsStore,
                        RepoCardViewModel, MainWindowViewModel and
                        GroupNameViewModel
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
  changes without the developer's awareness. The pin also records a *content
  identity* of what was applied — a SHA-256 over the agent files themselves
  (`files-sha256:`), the same whatever zip, NuGet package or Git repository
  delivered them: pin = package + version + content identity. It records
  *no package source and no Git commit*: sources and their aliases are each
  user's own configuration, and a committed file is shared by a team. The
  user's own configured sources are asked which offer the pinned version, and
  one whose files have the recorded identity supplies them, whatever its alias
  or kind; the last supplier is remembered in the user's own settings, never in
  the pin. The identity is also the only thing needed to prove the delivered
  files were not tampered with: a package republished under the same version,
  or a Git tag moved to other files, has another identity and is refused at
  launch and Upgrade, and accepted only by an explicit **Change package...**
  of the same version, which asks for confirmation and records the new
  identity (identical files are irrelevant, so nothing is flagged for them).
  The launch-time restore only checks the identity and never writes the pin
  (that would dirty the working tree), and pins without an identity adopt one
  at the next Select Package, Change package or Upgrade.

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
    configured package sources.
  - ⚠️ **Committed agent files** — one or more of `.github/agents`,
    `.github/standards`, `.github/templates`, `.github/skills` are tracked by
    git (i.e. committed into a repo that's meant to gitignore them). For
    Launch this is purely advisory — Launch never modifies git tracking
    state. "Select Package...", "Change package..." and "Upgrade" offer to
    convert such a repo instead (see below).

  Separately from this badge, immediately after every successful
  "Select Package..."/"Upgrade" extraction, AgentControl now also proactively
  ensures the repo's root `.gitignore` covers the four managed folders (via a
  fixed marker-comment idempotency check only — never `git check-ignore`, and
  never a gitignore-pattern/glob analysis). This is purely additive to any
  existing `.gitignore` content, never blocks the extraction on failure, and
  never touches git tracking state itself.

  Converting committed agent files: `.gitignore` has no effect on files git
  already tracks, so applying a package over tracked managed folders would
  leave git showing them modified or deleted while the package's new files
  stayed invisible. Select Package, Change package and Upgrade therefore check
  with git, at that moment (never the cached badge), which of the four
  managed folders have tracked files. If any do, AgentControl asks for
  confirmation in a modal dialog before downloading or changing anything,
  naming the repo, the tracked folders and the package. Declining changes
  nothing at all. Agreeing lets the apply proceed and, immediately before the
  package replaces the files, runs `git rm -r --cached` for only the tracked
  folders (index only — the files stay on disk until replaced); a failure
  stops the apply before any file is replaced. Afterwards the committed-files
  badge clears, the "Uncommitted changes" badge shows, and the status message
  tells the user to commit the change (the deletions, `.gitignore` and
  `.agentcontrol.json`). Repos with no tracked managed files, or whose git
  state cannot be read, behave as before, and Launch never converts.
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
    may involve slow network/UNC I/O — cached for at most five minutes
    (the app stays open for days and packages are published meanwhile),
    re-read on every user action that shows them (Select Package, Refresh,
    Upgrade) and by a background re-check every five minutes, rather than
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
  exclusive — a repo is either never-pinned or already-pinned, never both;
  a pinned repo has **Change package...** instead, which opens the same
  dialog preselected on the current choice).
  It opens a small modal dialog that:
  1. Enumerates the distinct package base names discoverable at the
     configured source directory (see the name/version splitting algorithm
     below) and lists them for the user to pick one.
  2. Once a name is chosen, lists that name's discoverable versions
     (descending), defaulting the selection to the latest.
  3. On confirm, runs the same extract-and-pin sequence as `Upgrade()`
     (validate zip → delete four folders if present → extract → write pin →
     show release notes → offer the AGENTS.md template if applicable),
     reusing `PackageZipExtractor`/`RepoPinStore` rather than duplicating
     that logic.
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
  the launch proceeds anyway with an informational status message, rather
  than silently launching the agent tool with no agent files present.
- **About dialog:** a simple, modal "About Agent Control" window
  (app version via the existing `Program.Version`, copyright, license text)
  reachable via a new toolbar/menu entry next to Settings.

## Launch must never be blocked by agent-package sync state

A product-owner report identified that `Launch` blocking entirely when no pin
existed (directing the user to "Select Package..." first, per the section
above) was itself a defect, not a safety feature: a user running with
committed agent files (already warned about via the committed-files badge)
may still want their agentic CLI tool to help with migrating away from those
committed files, and an agentic CLI tool remains useful even with zero
managed agent files present. Agent-package sync is a best-effort,
informational side action only, and must never gate the launch itself. This
amends the "Ensure-synced-before-launch" bullet above:

- `Launch` always calls `EnsureAgentFilesSyncedBeforeLaunch` for its
  best-effort sync side effect, but never gates on its return value — the
  agent tool is always launched afterward (assuming a resolvable agent
  command; the pre-existing "no command configured"/shell-detection/
  process-start failure checks are unaffected and still block as before).
- If the repo has committed agent files, the four managed folders are never
  touched — no delete, no extract — even if a pin exists and the folders are
  missing; a non-blocking status message notes that sync was skipped.
- If no pin exists at all, this is treated as an acceptable state, not an
  error; a non-blocking status message notes that launch is proceeding
  without managed agent files.
- If a pin exists but re-extraction is attempted and fails (source
  unreachable, pinned version missing, extraction I/O failure), this now
  surfaces as a non-blocking warning rather than a launch-blocking error.

## Open Concerns

1. 🟢 **LOW** Package source scaling: sources are read on demand and cached per
   source (or, for Git, per repository and commit); very large catalogs or
   repositories are bounded by size and time limits rather than paged.
2. 🟢 **LOW** Cross-platform distribution: Avalonia code is kept
   platform-portable, but only a Windows MSI is planned for v1 — packaging for
   macOS/Linux is deferred until there is customer demand.
3. 🟢 **LOW** Blind-delete trade-off: any local customizations a developer adds
   inside the four managed folders will be silently removed on the next
   upgrade; this is accepted risk, not a defect, but should be called out in
   user-facing documentation.
