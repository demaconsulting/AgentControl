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
- Reading a repo card's chips: pinned package, upgrade, branch, and status warnings
- Configuring package sources (zip-package folders and NuGet-package folders)
- Selecting an initial agent package for a never-pinned repo, and upgrading an
  already-pinned repo
- Launch behavior, including ensure-synced-before-launch semantics
- Pulling the latest commits for a repo
- Configuring per-user settings (package sources, git executable, agent tool, shell)
- The About dialog
- Authoring agent packages (zip files and NuGet packages) so AgentControl can discover and
  apply them

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

- The repo's name (its folder name) and, in smaller muted text below, its full path (hover for the
  full text if it is shortened)
- A row of **chips** — small rounded labels, each with a hover tooltip that explains it in full
  (see [Reading the Chips](#reading-the-chips) below)
- A favorite star toggle, letting you pin specific repos above the default
  most-recently-launched sort order
- A **Launch** button labelled with your agent tool (see [Launching](#launching))
- A **Git Pull** button (see [Pulling Changes](#pulling-changes))
- A "..." menu offering Refresh, **Select Package...** or **Upgrade** (mutually exclusive —
  see below), and Remove
- A manual **Refresh** action that re-checks the repo's git state and committed-agent-files
  chip without waiting for the next app launch

Removing a repo from the list prompts for confirmation first, since it is a destructive
action against AgentControl's own recent-repos state (it does not touch the repository
itself).

## Reading the Chips

- **Agent package chip** (robot icon) — the agent package the repo is pinned to and its
  version, for example `contoso-agents 1.0.0`. A small icon beside the version shows the kind of
  source the package came from (a folder of zips, or a folder of NuGet packages), and the tooltip
  names it: "Agent files: contoso-agents 1.0.0 from source 'Default' (zip folder)". If the source
  is not configured, the tooltip says so.
- **Upgrade chip** (amber, up arrow) — the version an upgrade would move to, for example
  `1.1.0`. It appears only when the source the repo is pinned to has a newer version. Use the
  card's "..." menu > **Upgrade** to apply it. While AgentControl is still checking the source,
  the chip is not shown yet; a slow or unreachable source never blocks the window.
- **No agent package chip** (grey) — the repo has no pin yet. Click it, or use "..." > **Select
  Package...**, to choose one.
- **Branch chip** — the repo's current git branch.
- **Uncommitted changes** (amber) — the repo has uncommitted local changes; this disables the
  **Git Pull** action for that card.
- **Committed agent files** (amber) — one or more of `.github/agents`, `.github/standards`,
  `.github/templates`, `.github/skills` are tracked by git at `HEAD` in this repo. This is
  purely advisory: AgentControl does not modify git tracking state itself, it only warns
  that content meant to stay untracked (via `.gitignore`) appears to have been committed.
  Separately, AgentControl proactively ensures your `.gitignore` covers these four folders
  immediately after every Select-Package/Upgrade (see below), which makes this situation
  much less likely to occur going forward.
- **Folder missing** (red) — the repo path no longer exists on disk (for example, a deleted
  folder or an unmounted network/removable drive). This suppresses the card's other chips and
  actions, and offers a "Remove from list" action instead (the card is never auto-removed, since
  the path may reappear, such as a drive being remounted).
- **Package source problem** (red) — the source the repo is pinned to is not configured (for
  example it was removed or renamed in Settings). The tooltip states the problem. No upgrade is
  offered and no other source is ever used instead; restore the source with the same alias in
  Settings to resolve it.

# Package Sources

AgentControl takes agent packages from one or more **package sources**, which you manage in
Settings. Two kinds of source are supported:

- **Zip-package folder** — a folder (local, mapped drive, or UNC share) holding agent package
  zip files named `{package-name}-{version}.zip`.
- **NuGet-package folder** — a folder (local, mapped drive, or UNC share) holding `.nupkg`
  files, as a NuGet "folder feed" does. A feed folder cannot be listed cheaply, so you also give
  the **package IDs** it offers.

Each source has an **alias**, the short name shown in the app and recorded in a repo's pin.

## Adding a Source

1. Open Settings (the gear icon) and, in the **Package sources** list, click **Add Zip-Package
   Folder** or **Add NuGet-Package Folder**.
2. Type an **alias**. It must be unique (letters are compared ignoring case) and cannot be
   changed later, because repo pins refer to it.
3. Enter the **location** as a full folder path, or use **Browse** to pick the folder. A
   location that contains a user name or password is refused, and so is a web address (servers
   are not supported yet).
4. For a NuGet-package folder, enter each package ID and click **Add**; at least one is required.
5. Messages under each field say what is wrong, and **Save** stays disabled until everything is
   valid. Then save the source, and save the Settings window to keep the change.

Each row of the list shows the alias, an icon for the kind of source, the location (hover for
the full text) and a status: whether the folder can currently be found. The status is checked in
the background, so an offline network share never freezes Settings.

## Editing and Removing a Source

Select a row and click **Edit** to change its location or (for NuGet) its package IDs; the alias
is fixed. **Remove** asks for confirmation. Removing a source is allowed even when repos are
pinned to it, but the confirmation tells you how many are: those repos will show a **Package
source problem** chip until a source with that alias is configured again.

## What Is Stored

Sources are saved in your per-user `settings.json` (in AgentControl's folder under your user
profile) as alias, kind, location and package IDs. Nothing secret is ever stored: AgentControl
has no credentials for these folders and uses your normal file access. Each repo's
`.agentcontrol.json` pin records the package name, exact version and **the alias of its source**.
AgentControl never reads or writes a repo's own `nuget.config`, and keeps its NuGet caches in
its own folder under your user profile.

## Moving From a Single Package Source Folder

Earlier versions had a single "package source folder". If your settings still have one, it
appears automatically as a zip-package folder source named **Default**, and repos pinned before
sources existed are treated as pinned to `Default`, so nothing needs to be redone.

## A Pin Never Falls Back

A repo is only ever served from the source its pin names. If two sources offer a package with the
same name, the one in the pin wins even if the other has a higher version; if the pinned source
is missing, you see an error rather than a quiet substitution. This stops a package of the same
name in another place from being installed under your repo's pin.

# Selecting and Upgrading Agent Packages

A repo's card shows exactly one of **Select Package...** or **Upgrade** in its "..." menu,
never both, depending on whether the repo already has a pin.

## Selecting an Initial Package

A repo with no `.agentcontrol.json` pin yet (for example, a repo just added to the list)
shows **Select Package...**. Choosing it opens a dialog with three columns:

1. **Source** — every configured package source, with its kind icon and alias. Each source loads
   in the background with a progress indicator; if a source cannot be read (for example, an
   offline share), its error appears in the dialog and the other sources still work.
2. **Package** — the packages the selected source offers.
3. **Version** — once you pick a package, its versions in descending order, defaulting the
   selection to the latest.

On confirm, AgentControl runs the same extract-and-pin sequence as an upgrade: validate the
package, delete the four managed folders if present, extract the new files, ensure your
`.gitignore` covers the four managed folders, write the `.agentcontrol.json` pin (naming the
source), show the release notes, then offer the `AGENTS.md` template if applicable (see
[AGENTS.md Template Offer](#agentsmd-template-offer) below). While this runs the card shows
that it is busy and offers **Cancel**.

## Upgrading an Already-Pinned Repo

Once a repo has a pin, its "..." menu shows **Upgrade** instead, appearing whenever a newer
version of the pinned package is available at the source named in the pin (shown by the
upgrade chip). Upgrading:

1. Opens and validates the new package. If it opens without error, its contents are
   assumed good — no checksum or signature verification is performed.
2. Deletes the four managed agent folders under the repo root, if present.
3. Extracts the new files into those same four folders.
4. Ensures your `.gitignore` covers the four managed folders (adding them if not already
   covered), so they are far less likely to be committed by accident.
5. Rewrites the `.agentcontrol.json` pin to the new package name and version.
6. Shows the new package's `release-notes.md` (when the package includes one) in a
   non-modal, resizable window.
7. Offers the package's `AGENTS.md` template if applicable (see
   [AGENTS.md Template Offer](#agentsmd-template-offer) below).

There is no rollback if a step fails partway through — a message box reports the error, and
you are expected to resolve it manually. Any local customizations you may have added inside
the four managed folders are not preserved across an upgrade: AgentControl always performs a
blind delete-and-replace of those exact four folders, never a precise diff against the
previous package's contents.

# Launching

Click **Launch** on a repo card (the button names your configured agent tool, for example
**Launch Copilot**, or just **Launch** when the tool has a long or unknown name) to start your
configured agentic CLI tool in that repo's
working directory. Launching is **never blocked** by agent-package sync state — an agentic
CLI tool remains useful whether or not any agent files are present, and may even help you
migrate away from agent files you've committed to the repo.

Before starting the tool, Launch makes a best-effort attempt to keep the repo synced, purely
as an informational side action:

- If the repo has **committed agent files** (see the warning chip above), the four managed
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
  launched. If you cancel the wait, the tool is launched as well. A successful re-extraction
  here also offers the package's `AGENTS.md` template if
  applicable, consistent with Select Package and Upgrade (see
  [AGENTS.md Template Offer](#agentsmd-template-offer) below).

# Pulling Changes

The **Git Pull** button on a repo card runs `git pull` in that repo's working directory. It stays
visible but is disabled (with a tooltip explaining why) whenever the repo's working tree has
uncommitted changes, the repo is missing, or the repo's Git status could not be determined
(for example, the folder is not a Git repository, or git could not be run). It also starts
disabled, with a "Git status has not been checked yet" tooltip, briefly while a repo card is
first realized, before its initial (deferred) Git status check has completed. The
"Uncommitted changes" chip, if shown, specifically denotes a confirmed dirty working tree - it is not
shown when Git Pull is disabled because of a missing repo, an undetermined status, or a status
check that simply hasn't run yet.

# Settings

Open Settings from the gear icon on the toolbar to configure:

- **Package sources** — the list of configured package sources, with **Add Zip-Package Folder**,
  **Add NuGet-Package Folder**, **Edit** and **Remove** (see [Package Sources](#package-sources)).
  No authentication, checksum, or signature
  verification is performed on these folders by AgentControl — the security and integrity of the
  shared location is the responsibility of whoever maintains it.
- **Git executable path override** — a specific `git` executable to use instead of resolving
  `git` from the process `PATH`.
- **Agent tool selection** — which agentic CLI tool Launch starts: GitHub Copilot CLI,
  Cursor, Claude Code, or a custom command line that you supply yourself. In a custom command
  line, quote the executable path if it contains spaces, for example
  `"C:\Program Files\My Tool\tool.exe" --flag`. Double quotes work in every shell; single quotes
  (`'C:\Program Files\My Tool\tool.exe' --flag`) work in PowerShell and POSIX shells but not in
  `cmd`, which only understands double quotes. When the launch shell
  is PowerShell, AgentControl automatically adds the PowerShell call operator (`&`) in front of
  a command that starts with a quote, so you do not need to type it yourself (a command you
  already start with `&` is left alone). Commands launched through `cmd` or a POSIX shell are
  used exactly as typed.
- **Shell/terminal preference** — the shell AgentControl launches the agent tool in, chosen
  from an editable dropdown of OS-appropriate presets (`pwsh`/`powershell`/`cmd` on Windows,
  `bash`/`zsh`/`sh` on macOS/Linux) or a custom path you type in. Leave this unset (the blank
  "auto-detect" entry) to let AgentControl auto-detect the best available shell (it prefers the
  highest available PowerShell/`pwsh`, falls back to Windows PowerShell 5, then to `cmd`, on
  Windows; the platform default shell is used on macOS/Linux).

# About

The About dialog, reachable from an info icon on the main window's toolbar, is a simple,
non-modal window showing the running build's application version, copyright, and license
text, so you can identify the running build without inspecting file properties, along with a
scrollable list of this application's direct third-party dependencies and their licenses.

# Authoring Agent Packages

This section is the reference for teams that build and publish agent packages for
AgentControl to discover and apply — not for developers who only consume packages through
the app's own Select Package/Upgrade UI. A package can be published as a **zip file** in a
zip-package folder, or as a **NuGet package** in a NuGet-package folder; both carry the same
content (see [Managed Folders](#managed-folders) and the sections after it), differing only in
where the content sits and how the name and version are found.

## Package File Naming

An agent package published as a zip is a single `.zip` file placed in a zip-package folder
source, named:

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

The package version in a **zip file name** must exactly match:

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
  Versioning 2.0.0) are **not** supported in zip file names — they must never carry one.
- A version with a prerelease identifier always sorts as lower precedence than the same
  `MAJOR.MINOR.PATCH` without one (for example, `1.0.0-beta` is considered older than
  `1.0.0`), matching standard semantic-versioning precedence rules.

Valid examples: `1.0.0`, `2.4.13`, `1.2.0-beta`, `1.2.0-beta.1`, `1.2.0-rc-2`.

Invalid examples for zip file names: `1.0` (only two components), `1.0.0.0` (four components),
`1.0.0-` (empty prerelease), `1.0.0+build.5` (build metadata is unsupported).

NuGet packages carry their version in the package, so they may use NuGet's wider version forms:
three or four numeric components (`1.2.0` or `1.2.0.1`; `1.2.0.0` equals `1.2.0`), an optional
prerelease, and optional build metadata after `+` (`1.2.0+build.5`). Build metadata is kept in
the pin as written but is ignored when versions are compared, so `1.2.0+a` and `1.2.0+b` are the
same version. Prerelease labels compare ignoring letter case.

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

## AGENTS.md Template Offer

A package zip may also optionally include a root-level `AGENTS.md` entry, following the same
convention as `release-notes.md`: it is never extracted into any of the four managed folders,
and it is never deleted or overwritten by a sync.

Whenever a package is extracted — Select Package, Upgrade, or the best-effort
sync-before-launch — AgentControl checks whether the repo already has its own root-level
`AGENTS.md` file:

- If the repo **already has** an `AGENTS.md` file, nothing happens: it is never touched,
  overwritten, or even read.
- If the repo **has no** `AGENTS.md` file, and the package being applied **has** an
  `AGENTS.md` template to offer, AgentControl shows a modal Yes/No dialog explaining that the
  package includes a starting `AGENTS.md` template, that the repo doesn't have one yet, and
  that you are responsible for customizing it afterward — it is a starting point, not a
  finished file.
  - Choosing **Yes** writes the package's template verbatim to the repo's root as
    `AGENTS.md`.
  - Choosing **No** records that decision for the repo so you are **not** asked again on a
    later sync; this preference does not reset automatically — for example, deleting the
    repo's `AGENTS.md` later does not revive the prompt.
- If the package has **no** `AGENTS.md` template at all, no prompt is ever shown.

## Worked Example

A package named `contoso-agents` at version `1.2.0` would be published as a single zip file:

```text
contoso-agents-1.2.0.zip
├── release-notes.md
├── AGENTS.md
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
but never written to the repo's working directory, and `AGENTS.md` is only ever written to
the repo root if the repo lacks one and the user accepts the offer.

## Publishing a Package

For a zip package, place the finished zip file directly in a folder configured as a
**zip-package folder** source in AgentControl's Settings (a local drive, mapped network drive, or
UNC share).
AgentControl's package source directory scan discovers every `*.zip` file there on demand —
there is no publish command, index file, or registration step beyond copying the zip into
that directory. Publishing a new version alongside older versions of the same package (for
example, keeping both `contoso-agents-1.1.0.zip` and `contoso-agents-1.2.0.zip` in the same
directory) is expected and normal: existing repos remain pinned to whichever version they
last synced until a developer explicitly upgrades.

For a NuGet package, see the next section.

## Authoring a NuGet Package

A **NuGet-package folder** source reads `.nupkg` files from a folder feed. Package authors need to
know three things:

1. **The package ID and version** come from the package's `.nuspec` (or project properties). The
   consumer adds the package ID to the NuGet source's package ID list in Settings; AgentControl
   offers exactly the listed IDs and finds their versions in the folder.
2. **The content lives under `content/`.** Inside the package, use the zip layout rooted at
   `content/` (there is no mapping from other layouts and no fallback):

   ```text
   contoso-agents.1.2.0.nupkg
   └── content/
       ├── release-notes.md
       ├── AGENTS.md
       └── .github/
           ├── agents/
           ├── standards/
           ├── templates/
           └── skills/
   ```

   `content/release-notes.md` and `content/AGENTS.md` are optional and behave as described
   above; only the four `.github` subtrees are extracted into a repo.
3. **A package without a `content` folder is not an agent package** and is refused with an
   error.

A minimal `.nuspec` listing the files explicitly:

```xml
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>contoso-agents</id>
    <version>1.2.0</version>
    <authors>Contoso</authors>
    <description>Contoso agent files for AgentControl.</description>
  </metadata>
  <files>
    <file src="agents\**" target="content\.github\agents" />
    <file src="standards\**" target="content\.github\standards" />
    <file src="templates\**" target="content\.github\templates" />
    <file src="skills\**" target="content\.github\skills" />
    <file src="release-notes.md" target="content" />
    <file src="AGENTS.md" target="content" />
  </files>
</package>
```

Pack it with `nuget pack` or `dotnet pack`, and copy the resulting `.nupkg` into the folder the
source points to.

When you pack with an **SDK-style project** instead, use an explicit `PackagePath` that names the
`.github` folder, because wildcard globbing may skip dot-folders:

```xml
<ItemGroup>
  <None Include="agents\**" Pack="true" PackagePath="content\.github\agents" />
  <None Include="standards\**" Pack="true" PackagePath="content\.github\standards" />
  <None Include="templates\**" Pack="true" PackagePath="content\.github\templates" />
  <None Include="skills\**" Pack="true" PackagePath="content\.github\skills" />
  <None Include="release-notes.md" Pack="true" PackagePath="content" />
  <None Include="AGENTS.md" Pack="true" PackagePath="content" />
</ItemGroup>
```

Inspect the produced `.nupkg` (it is a zip file) to confirm the `content/.github/...` entries are
present before publishing.

## References

N/A
