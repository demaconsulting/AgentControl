# Introduction

## Purpose

Agent Control is a Windows desktop launcher application that lets a company distribute
proprietary AI-agent configuration (Copilot/agentic-CLI instruction files, standards,
templates, and skills) alongside public source repositories, without ever committing that
proprietary content to source control. A developer opens a repo from a recent-repos list,
has AgentControl ensure the repo's `.github/agents`, `.github/standards`,
`.github/templates`, and `.github/skills` folders match the version pinned in that repo's
`.agentcontrol.json` file, optionally pulls the latest commits, and then launches their
preferred agentic CLI tool (for example, GitHub Copilot CLI) in that repo's working
directory.

## Scope

This user guide covers:

- Installing AgentControl
- Adding a repository to the recent-repos list
- Reading a repo card's badges, branch, and pinned-version display
- Selecting an initial agent package for a never-pinned repo, and upgrading an
  already-pinned repo
- Launch behavior, including ensure-synced-before-launch semantics
- Pulling the latest commits for a repo
- Configuring per-user settings (package source, git executable, agent tool, shell)
- The About dialog
- Authoring agent packages so AgentControl can discover and apply them

# Installation

AgentControl is distributed as a Windows MSI installer, built from
`src/DemaConsulting.AgentControl.Msi/`.

1. Download the latest `AgentControl-{version}.msi` release.
2. Run the installer and follow the prompts.
3. Launch **AgentControl** from the Start menu.

The installed application is **self-contained and single-file**: it bundles its own copy of
the .NET 10 runtime (matching the app's `net10.0` target framework and the Avalonia UI
framework's Windows desktop requirements), so the target machine needs no pre-installed
runtime.

# Adding a Repository

Click the folder-plus icon button on the main window's toolbar. A folder-browse dialog opens;
select the root folder of the git repository you want to manage. The repo is added to the
recent-repos list as a card.

Each repo card shows:

- The repo's display name (a user-editable alias) and full path
- Its pinned package name and version (once selected), read from the repo's
  `.agentcontrol.json` file
- Its current git branch name
- A row of status badges, each with its own hover tooltip:
  - **Upgrade available** — the repo's pin is behind the newest version discoverable at the
    configured package source
  - **Committed agent files** — one or more of `.github/agents`, `.github/standards`,
    `.github/templates`, `.github/skills` are tracked by git at `HEAD` in this repo. This is
    purely advisory: AgentControl does not modify git tracking state itself, it only warns
    that content meant to stay untracked (via `.gitignore`) appears to have been committed.
    Separately, AgentControl proactively ensures your `.gitignore` covers these four folders
    immediately after every Select-Package/Upgrade (see below), which makes this situation
    much less likely to occur going forward — but this badge itself remains unchanged and
    still purely advisory.
  - **Missing** — the repo path no longer exists on disk (for example, a deleted folder or an
    unmounted network/removable drive). This suppresses the card's other badges and actions,
    and offers a "Remove from list" action instead (the card is never auto-removed, since the
    path may reappear, such as a drive being remounted)
  - **Dirty working tree** — the repo has uncommitted local changes; this suppresses/disables
    the Pull action for that card
- A favorite star toggle, letting you pin specific repos above the default
  most-recently-launched sort order
- A "..." menu offering Refresh, **Select Package...** or **Upgrade** (mutually exclusive —
  see below), and Remove
- A manual **Refresh** action that re-checks the repo's git state and committed-agent-files
  badge without waiting for the next app launch
- A **Pull** button (see [Pulling Changes](#pulling-changes))
- A **Launch** button (see [Launching](#launching))

Removing a repo from the list prompts for confirmation first, since it is a destructive
action against AgentControl's own recent-repos state (it does not touch the repository
itself).

# Selecting and Upgrading Agent Packages

A repo's card shows exactly one of **Select Package...** or **Upgrade** in its "..." menu,
never both, depending on whether the repo already has a pin.

## Selecting an Initial Package

A repo with no `.agentcontrol.json` pin yet (for example, a repo just added to the list)
shows **Select Package...**. Choosing it opens a dialog that:

1. Lists the distinct package base names discoverable at the configured package source
   directory.
2. Once you pick a name, lists that package's discoverable versions in descending order,
   defaulting the selection to the latest.
3. On confirm, runs the same extract-and-pin sequence as an upgrade: validate the zip, delete
   the four managed folders if present, extract the new files, ensure your `.gitignore`
   covers the four managed folders, write the `.agentcontrol.json` pin, then show the release
   notes.

## Upgrading an Already-Pinned Repo

Once a repo has a pin, its "..." menu shows **Upgrade** instead, appearing whenever a newer
version of the pinned package is discoverable at the package source (shown by the
upgrade-available badge). Upgrading:

1. Opens and validates the new package zip. If it opens without error, its contents are
   assumed good — no checksum or signature verification is performed.
2. Deletes the four managed agent folders under the repo root, if present.
3. Extracts the new files into those same four folders.
4. Ensures your `.gitignore` covers the four managed folders (adding them if not already
   covered), so they are far less likely to be committed by accident.
5. Rewrites the `.agentcontrol.json` pin to the new package name and version.
6. Shows the new package's `release-notes.md` (when the package includes one) in a
   non-modal, resizable window.

There is no rollback if a step fails partway through — a message box reports the error, and
you are expected to resolve it manually. Any local customizations you may have added inside
the four managed folders are not preserved across an upgrade: AgentControl always performs a
blind delete-and-replace of those exact four folders, never a precise diff against the
previous package's contents.

# Launching

Click **Launch** on a repo card to start your configured agentic CLI tool in that repo's
working directory. Launching is **never blocked** by agent-package sync state — an agentic
CLI tool remains useful whether or not any agent files are present, and may even help you
migrate away from agent files you've committed to the repo.

Before starting the tool, Launch makes a best-effort attempt to keep the repo synced, purely
as an informational side action:

- If the repo has **committed agent files** (see the warning badge above), the four managed
  folders are never touched — no delete, no extract — and Launch proceeds straight to
  starting the tool with an informational status message.
- Otherwise, if the repo has **no pin yet**, Launch proceeds anyway with an informational
  status message noting that no managed agent files are present, rather than blocking the
  launch.
- Otherwise, if the repo **has a pin**, Launch verifies all four managed agent folders
  (`.github/agents`, `.github/standards`, `.github/templates`, `.github/skills`) exist on
  disk. If any are missing — the common case for a freshly cloned repo whose `.gitignore`'d
  agent folders were never unpacked on this machine — AgentControl silently re-extracts the
  **currently pinned** version before proceeding. It never auto-upgrades to a newer version
  during this check; upgrading always remains a separate, deliberate action. If this
  re-extraction attempt fails (for example, the package source is unreachable, or the pinned
  version is no longer available), a non-blocking warning is shown, but the tool is still
  launched.

# Pulling Changes

The **Pull** button on a repo card runs `git pull` in that repo's working directory. It is
only offered when the repo's working tree is clean (the "Dirty working tree" badge, if
shown, disables Pull for that card).

# Settings

Open Settings from the gear icon on the toolbar to configure:

- **Package source path** — the filesystem path from which agent package zip files are
  discovered. This may be a local drive, a mapped network drive, or a UNC path; it is edited
  via a textbox with a folder-browse dialog. No authentication, checksum, or signature
  verification is performed on this path by AgentControl — the security and integrity of the
  shared location is the responsibility of whoever maintains it.
- **Git executable path override** — a specific `git` executable to use instead of resolving
  `git` from the process `PATH`.
- **Agent tool selection** — which agentic CLI tool Launch starts: GitHub Copilot CLI,
  Cursor, Claude Code, or a custom command line that you supply yourself.
- **Shell/terminal preference** — the shell AgentControl launches the agent tool in. Leave
  this unset to let AgentControl auto-detect the best available shell (it prefers the
  highest available PowerShell/`pwsh`, falls back to Windows PowerShell 5, then to `cmd`, on
  Windows; the platform default shell is used on macOS/Linux).

# About

The About dialog, reachable from an info icon on the main window's toolbar, is a simple,
non-modal window showing the running build's application version, copyright, and license
text, so you can identify the running build without inspecting file properties.

# Authoring Agent Packages

This section is the reference for teams that build and publish agent packages for
AgentControl to discover and apply — not for developers who only consume packages through
the app's own Select Package/Upgrade UI.

## Package File Naming

An agent package is a single `.zip` file placed in the configured package source directory,
named:

```text
{package-name}-{version}.zip
```

For example: `contoso-agents-1.2.0.zip`.

AgentControl discovers package names and versions purely from the zip's file name — it does
not read any manifest inside the zip to determine its name or version. The split between
`{package-name}` and `{version}` is found with a **leftmost-hyphen-scan** algorithm:

1. Take the file name without its `.zip` extension.
2. Scan its hyphens (`-`) left to right.
3. For each hyphen position, test whether the text **after** that hyphen parses as a valid
   package version (see [Version Format](#version-format) below).
4. The **first** (leftmost) hyphen whose suffix parses successfully is the split point:
   everything before it is the package name, everything after it is the version.
5. If no hyphen's suffix parses as a valid version, the file is not a recognized package and
   is silently skipped — a package source directory is expected to hold arbitrary or
   unrelated files alongside package zips.

Because the scan is leftmost-first, a package name that itself contains hyphens still splits
correctly as long as no earlier hyphen's suffix happens to also parse as a version. For
example, `contoso-agents-extra-1.2.0.zip` splits into name `contoso-agents-extra`, version
`1.2.0`, because the earlier candidate suffixes (`agents-extra-1.2.0`, `extra-1.2.0`) do not
parse as valid versions.

**When choosing a package name, avoid naming segments that could themselves be mistaken for
a version** — for example, a package name ending in something like `-2` or `-1.0` could
cause an earlier hyphen to win the split unexpectedly, since the scan stops at the first
hyphen whose suffix is a valid version, not necessarily the one the author intended. This is
an accepted "simplicity over precision" limitation: choose package names that don't create
this ambiguity, rather than relying on any escaping mechanism (none exists).

## Version Format

Versions must exactly match:

```text
MAJOR.MINOR.PATCH[-PRERELEASE]
```

- `MAJOR`, `MINOR`, and `PATCH` are each required, non-negative integers, separated by dots.
  There must be exactly three dot-separated numeric components — not two, not four.
- An optional prerelease identifier may follow a single `-`. Everything after the **first**
  `-` in the version string is taken as the prerelease identifier verbatim (so a prerelease
  identifier may itself contain further hyphens, e.g. `1.2.0-beta-2` is a valid version with
  prerelease identifier `beta-2`). A trailing `-` with nothing after it is invalid.
- Build-metadata suffixes (a trailing `+...` segment, as permitted by full Semantic
  Versioning 2.0.0) are **not** supported — package file names must never carry one.
- A version with a prerelease identifier always sorts as lower precedence than the same
  `MAJOR.MINOR.PATCH` without one (for example, `1.0.0-beta` is considered older than
  `1.0.0`), matching standard semantic-versioning precedence rules.

Valid examples: `1.0.0`, `2.4.13`, `1.2.0-beta`, `1.2.0-beta.1`, `1.2.0-rc-2`.

Invalid examples: `1.0` (only two components), `1.0.0.0` (four components), `1.0.0-`
(empty prerelease), `1.0.0+build.5` (build metadata is unsupported).

## Managed Folders

Every extract or upgrade performs a **blind delete-and-replace** of exactly four folders,
relative to the repo root:

- `.github/agents`
- `.github/standards`
- `.github/templates`
- `.github/skills`

Each of these four folders is deleted (recursively, if it exists) and then re-created from
the package zip's matching entries. Any other content in the zip — including any root-level
files — is **not** extracted to disk at all; only zip entries that fall inside one of these
four folders are written out. There is no merge or diff against the previous package's
contents, so any local customizations a developer may have added inside these folders will
be lost on the next upgrade — package authors and package consumers should treat these four
folders as fully owned and replaced by the package, not a place for local edits.

## Release Notes

A package zip may optionally include a root-level `release-notes.md` entry. It is never
extracted to disk as part of the managed folders; instead, AgentControl reads its content
directly from the zip and displays it in a non-modal window after a successful extract or
upgrade. A package with no `release-notes.md` entry is still extracted successfully — the
release-notes display is simply skipped.

## Worked Example

A package named `contoso-agents` at version `1.2.0` would be published as a single zip file:

```text
contoso-agents-1.2.0.zip
├── release-notes.md
├── .github/
│   ├── agents/
│   │   └── ...
│   ├── standards/
│   │   └── ...
│   ├── templates/
│   │   └── ...
│   └── skills/
│       └── ...
```

Only the four `.github/agents`, `.github/standards`, `.github/templates`, and
`.github/skills` subtrees are extracted into a repo; `release-notes.md` is shown to the user
but never written to the repo's working directory.

## Publishing a Package

Place the finished zip file directly in the directory configured as the **package source
path** in AgentControl's Settings (a local drive, mapped network drive, or UNC share).
AgentControl's package source directory scan discovers every `*.zip` file there on demand —
there is no publish command, index file, or registration step beyond copying the zip into
that directory. Publishing a new version alongside older versions of the same package (for
example, keeping both `contoso-agents-1.1.0.zip` and `contoso-agents-1.2.0.zip` in the same
directory) is expected and normal: existing repos remain pinned to whichever version they
last synced until a developer explicitly upgrades.

## References

N/A
