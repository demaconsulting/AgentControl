# Agent Control

[![GitHub forks][badge-forks]][link-forks]
[![GitHub stars][badge-stars]][link-stars]
[![GitHub contributors][badge-contributors]][link-contributors]
[![License][badge-license]][link-license]
[![Build][badge-build]][link-build]
[![Quality Gate][badge-quality]][link-quality]
[![Security][badge-security]][link-security]

AgentControl is a Windows desktop launcher that lets a company distribute proprietary
AI-agent configuration (Copilot/agentic-CLI instruction files, standards, templates, and
skills) alongside public source repositories, without ever committing that proprietary
content to source control. A developer picks a repo from a recent-repos list, AgentControl
makes sure the repo's `.github/agents`, `.github/standards`, `.github/templates`, and
`.github/skills` folders match the version pinned in that repo's `.agentcontrol.json` file,
then launches the developer's preferred agentic CLI tool (for example, GitHub Copilot CLI)
in that repo's working directory.

## Screenshots

![AgentControl main window, dark theme: app bar, then repo cards in sections with status bars][shot-main-dark]

![AgentControl main window, light theme: the same repo cards][shot-main-light]

The cards, the card menu, Settings and the package-source editor are shown in the
[User Guide][link-guide].

## Features

- **Compact app bar and themes**: one slim bar at the top of the main window holds the logo, a filter box and the
  **Add repository**, **Settings** and **About** buttons; the whole application follows the **System default**,
  **Light** or **Dark** theme, with legible text in both
- **Recent-repos list**: add repos via a folder-browse button; each repo is shown as a card
  with a colored **status bar** (green up to date, amber upgrade available, red a problem, grey no
  agent package - always backed by words and tooltips), a favorite star, its display name, a tile with its
  initials (or the repo's own root `icon.png`/`logo.png`), its path, and labeled chips for the pinned agent
  package (name, version and source, with a small check mark when it is the newest), an available upgrade, and
  the current git branch; **Launch** sits on the card and everything else (Git Pull, Refresh - **Check again**
  for a repo whose folder is missing - Select Package/Upgrade, Move to group, Card color, Remove) in its grouped
  "..." menu; a long path is shortened to fit - the folder part first, then the middle of a very long folder
  name - so the start and the end of the repo's folder name stay visible
- **Repo groups and card colors (just for you)**: put repos into groups of your own (**Move to group** in a
  card's "..." menu, then an existing group or **New group...**) and the list becomes collapsible sections -
  **Favorites** first, your groups alphabetically, **Ungrouped** last - with the number of repos in each; the
  collapsed state is remembered, rename or remove a group from its header's "..." menu, and the filter box still
  finds a repo inside a collapsed group. **Card color...** gives a repo's tile a color of your choice from a
  palette (or **Automatic**); the status bar keeps its fixed colors. Both are stored only in your own AgentControl
  settings - never in the repository or its `.agentcontrol.json` - so they are never shared through git
- **Version-pinned agent packages**: every repo names an exact agent package name and
  semantic version, and the source it came from, in its `.agentcontrol.json` pin file — there
  is no floating "latest" mode, so upgrades are always a deliberate, visible action
- **Status chips**: each repo card shows an amber upgrade chip ("Upgrade to" and the version an upgrade
  would move to; click it to upgrade), an uncommitted-changes warning, a committed-agent-files
  warning (advisory only — flags when the managed folders were accidentally committed to
  git), a missing-folder indicator when the path no longer exists on disk, a
  package-source-problem indicator when the pinned source is not configured, and a sign-in-needed indicator
  when the pinned server needs a sign-in. Separately,
  AgentControl proactively ensures the repo's `.gitignore` covers the four managed
  folders immediately after every "Select Package..."/Upgrade, making an accidental commit
  much less likely in the first place
- **Ensure-synced-before-launch**: clicking Launch attempts a best-effort sync of the four
  managed agent folders before starting the agent tool; if they are missing (for example, a
  fresh clone) it silently re-extracts the currently pinned version first — it never
  auto-upgrades to a newer version on your behalf. This sync attempt is purely informational
  and never blocks the launch: repos with committed agent files skip it entirely, repos with
  no pin launch anyway, and a failed re-extraction only surfaces a warning
- **Git integration**: pull the latest commits for a repo with **Git Pull** in its card's "..." menu (enabled
  only when the working tree is clean), with a configurable git executable override
- **Configurable agent tool**: choose GitHub Copilot CLI, Cursor, Claude Code, or a custom
  command line, plus a shell/terminal preference
- **Multiple package sources**: configure any number of agent package sources, each with its
  own alias — a folder of zip packages, a folder of NuGet packages (local drive, mapped
  drive, or UNC share), or a **NuGet-package server** such as JFrog Artifactory or Azure Artifacts.
  A repo's pin records the alias of the source it came from and is only
  ever resolved against that source: if the source is removed or renamed, AgentControl shows a
  package source problem for the repo instead of quietly using another source
- **Protected package servers**: a server source is added with its feed address (https; plain http only
  after an explicit "Allow insecure HTTP" tick), can be checked with **Test connection** before it is saved,
  and asks you to sign in only when the server needs it. Credentials are used for the current session
  only and are never saved by AgentControl; you can opt in, per sign-in, to **Remember on this computer**,
  which has your own Git credential helper keep it (AgentControl itself never stores a secret)
- **Signed-package enforcement**: a NuGet source (folder or server) can **Require signed packages**,
  optionally pinned to your publisher's certificate fingerprint. A package that is not validly signed (or
  not signed by that publisher) is never installed — even one already downloaded earlier — and a platform
  that cannot verify signatures refuses such a source instead of skipping the check
- **Responsive**: package sources are read in the background with progress and a Cancel button, so a
  slow or offline network share or server never freezes the window, and one unreachable source does not
  hide the packages or upgrades of the others
- **Upgrade notifications**: an upgrade chip and release-notes viewer let you see when a newer agent
  package version is available and what changed before you apply it
- **About dialog**: reachable from the main window, shows the application logo, a short
  mission tagline, the running app's version, copyright, license, and a scrollable list of
  this application's direct third-party dependencies and their licenses

### Security notes

- A package source location must never contain credentials (a user name or password in a URL or
  path is refused), and AgentControl never stores secrets: `settings.json` holds only aliases,
  locations, NuGet package IDs, the insecure-HTTP flag of a server source and, per NuGet source, the
  signature settings — whether signed packages are required (`RequireSignedPackages`) and the optional signer
  fingerprint (`SignerFingerprint`), which is the SHA-256 fingerprint of a publisher's public certificate and
  not a secret.
- A password or token you type into the sign-in dialog is kept in memory for the current session
  only — AgentControl does not write it to the settings, a repo's pin file, the log or anywhere else — and
  offers it only to the server it was typed for, and only when that server asks for it. The dialog shows
  the server that will receive it. A server on plain `http` is flagged and needs an explicit opt-in.
- **Remember on this computer** (off by default, and disabled with an explanation when no Git credential
  helper is configured) hands the sign-in to your own Git credential helper — Git Credential Manager,
  the macOS keychain, libsecret or the credential-store file — after it has been shown to work. AgentControl
  itself never stores a secret. A saved sign-in is read back once per server per session, without a dialog and
  only for that server's exact scheme, host and port; if the server rejects it, it is removed from the helper
  and you are asked once. **Forget saved sign-in** in the server source's editor removes it too.
- Credentials AgentControl does not need to ask for are reused: a `nuget.config` source with the same
  address (and its stored credentials) and NuGet credential-provider plug-ins such as the Azure Artifacts
  Credential Provider are tried before the dialog. Background checks never open the dialog.
- **Signed packages.** Per NuGet source you can require validly signed packages and, recommended, pin the
  publisher by the SHA-256 fingerprint of the package **author** certificate. Without a pin, any validly
  signed package from any publisher your machine trusts is accepted. The check is made every time a package is
  used, including one already in AgentControl's cache; your own NuGet trusted-signer settings neither widen nor
  narrow it; verification is supported on Windows, Linux and macOS, and elsewhere a source that requires
  signatures is refused (fails closed) rather than skipped. Zip packages carry no signatures.
- Exceptions that reach AgentControl's last-resort handlers are logged only after secrets (passwords,
  authorization values, URL user information and query strings) have been removed from them.
- **Only add servers you trust.** A server's own index tells the NuGet client where its package resources
  are, and once a server has authenticated, the NuGet client can answer sign-in challenges from other hosts
  that the index names with the same credential. AgentControl itself only ever offers a credential to the
  server's own address, but cannot stop the NuGet client from doing that.
- A repo's pin names its source by alias, and a package is only ever taken from that source; a
  missing source is an error, never a fallback to another source with a package of the same name.
- A version number is only a label: depending on the source, a published version can be overwritten with different
  content (a shared folder by anyone who can write to it, some servers when redeploy is allowed). See the User
  Guide's section "Version Immutability and Trust" for how to guard against it.
- AgentControl never reads or writes the target repo's `nuget.config`, and keeps its NuGet
  caches in its own folder under the user profile.

See [architecture.md](architecture.md) for the full system design.

## Installation

AgentControl is distributed as a Windows MSI installer, built from
[`src/DemaConsulting.AgentControl.Msi/`](src/DemaConsulting.AgentControl.Msi/README.md).

1. Download the latest `AgentControl-{version}.msi` from the
   [releases][link-build] for this repository.
2. Run the installer and follow the prompts.
3. Launch **AgentControl** from the Start menu.

The installed application is **self-contained and single-file** — it bundles its own copy of
the .NET 10 runtime, so the target machine needs no pre-installed runtime.

## Usage

### Adding a repo

Click the folder-plus icon button in the app bar at the top of the main window and browse to a repository's root
folder.
The repo is added to the recent-repos list as a card showing its display name, path, and chips
for the pinned package (name, version and source), an available upgrade, and the current git
branch.

### Selecting a package

A repo that has never been synced shows a grey **No agent package** chip, and a **Select
Package...** action (in place of **Upgrade**) in its card's "..." menu. The dialog shows three
columns: the configured package sources, the packages of the selected source, and that package's
available versions (defaulting to the latest). Choosing one extracts it into the repo and pins it
together with the source's alias. If a source is a server that needs a sign-in, a sign-in dialog opens
when you choose it; if reading a source fails, a **Retry** button appears beside the message.

### Upgrading

Once a repo is pinned, its card shows an amber upgrade chip (for example **Upgrade to 1.1.0**) whenever a
newer version of the pinned package is available at the source the pin names; click the chip, or use
**Upgrade to 1.1.0** in the card's "..." menu, to upgrade. Upgrading re-runs the
same validate → delete → extract → pin sequence, offers the package's `AGENTS.md` template when it applies
(below) and then shows the new package's `release-notes.md` (when present) in a non-modal window, in front.

### AGENTS.md template offer

An agent package may optionally include a root-level `AGENTS.md` entry — a starting template,
not a finished file (the same convention as the existing root-level `release-notes.md` entry:
it lives outside the four managed folders, so it's never touched by the blind-delete-and-replace
sync). Whenever a package is extracted (Select Package, Upgrade, or the best-effort
sync-before-launch) and the repo has no root-level `AGENTS.md` of its own, and the package
being applied has a template to offer, AgentControl shows a modal Yes/No dialog asking whether
to place the template at the repo root, clearly noting that you are responsible for
customizing it afterward. A pre-existing `AGENTS.md` is never touched or overwritten, and if
you decline, AgentControl remembers that choice for the repo so you're not asked again on
every subsequent sync.

### Launching

Click **Launch** (labelled with your agent tool, for example **Launch Copilot**) to start your
configured agentic CLI tool in the repo's working directory.
Launch is never blocked by agent-package sync state — before launching, AgentControl makes a
best-effort attempt to sync the four managed agent folders for a pinned repo (silently
re-extracting the *currently pinned* version, never a newer one, if any are missing), but this
is purely an informational side action. A repo with committed agent files skips the sync
entirely (the managed folders are never touched), and a repo with no pin at all still launches
— an agentic CLI tool remains useful even with zero managed agent files present, and can help
with migrating away from committed agent files.

### Pulling changes

**Git Pull**, in the "..." menu of a repo card (under *Repository*), runs `git pull` in that repo. The item is
always present, and is enabled only when the working tree is clean (it is disabled, with a tooltip that says
why, while the status is dirty or cannot be determined).

### Card icon

A repo card shows the repository's own picture instead of the generated initials tile when the repo's **root
folder** has an `icon.png` (or else a `logo.png`; the name's case does not matter). Only a plain PNG file of
at most 1 MB is used - never a link, and never a file in a subfolder - and anything else is ignored, so a
repository you do not trust cannot make AgentControl read other files or load huge images. Choose **Refresh** in
the card's "..." menu after adding or changing the file.

### Groups and card colors

Both are personal ways to organize the list and are stored **only in your own AgentControl settings file**
(`%APPDATA%\AgentControl\settings.json`); nothing is written to a repository or its `.agentcontrol.json`.

- **Groups.** In a card's "..." menu choose **Organize > Move to group**, then one of your groups, **New group...**
  (a small prompt: up to 40 characters; *Favorites* and *Ungrouped* are built-in names; letter case does not make a
  new group) or **Remove from group**. As soon as one repo is in a group the list shows sections with a header
  each - **Favorites** (a favorite is listed only there, and its card shows its group as a chip), then your groups
  alphabetically, then **Ungrouped** - with the number of repos; click a header to collapse or expand it (the choice
  is remembered). A group's header has a "..." menu with **Rename group...** and **Remove group** (its repos stay
  in the list, ungrouped). With no groups at all there are no headers and the list looks as it always did.
  Typing in the filter box shows every match, expanding collapsed groups while you filter.
- **Card color.** **Organize > Card color...** opens a palette of ten colors that work in the light and dark
  themes, plus **Automatic** (the color AgentControl derives from the repo's path). The color goes on the card's
  tile (as a ring around a repo's own picture); the status bar on the card's left edge keeps its meaning.

### Settings

The Settings window lets you configure:

- **Package sources** — a list of the configured sources (alias, kind, location and whether the
  folder can be found; a server shows **Not checked** because AgentControl contacts it only when it is
  needed). Use **Add** (a menu of **Zip-package folder**, **NuGet-package folder** and **NuGet-package server**)
  to add a source, and **Edit** or **Remove** to change one; a NuGet source also lists the package
  IDs it offers, a server can be checked with **Test connection** before it is saved, and a NuGet source can
  **Require signed packages** (optionally pinned to a publisher's certificate fingerprint). An alias cannot
  be changed once created, because repo pins refer to it. A single
  package source folder configured in an earlier version appears as a zip source named `Default`.
  See the [User Guide][link-guide] chapter "Package Sources".
- **Appearance** (its own section) — the **Theme**: **System default** (follow the operating system's light/dark
  setting, live), **Light** or **Dark**; applied as soon as you save, without a restart
- **Git executable override** — a specific `git` executable to use instead of resolving it
  from `PATH`
- **Agent tool selection** — GitHub Copilot CLI, Cursor, Claude Code, or a custom command
- **Shell/terminal preference** — which shell to launch the agent tool in, picked from an
  OS-appropriate dropdown (`pwsh`/`powershell`/`cmd` on Windows, `bash`/`zsh`/`sh` elsewhere)
  or typed in directly for a custom shell; leave unset to let AgentControl auto-detect the
  best available shell

### About

The About dialog (the info button in the main window's app bar) shows the application logo, a short mission
tagline, and the running build's version, copyright, and license, so it can be identified
without inspecting file properties, along with a scrollable list of this application's direct
third-party dependencies and their licenses.

## Authoring agent packages

Agent packages are either plain zip files or NuGet packages (served from a folder or a NuGet
server) following a naming and content
convention that AgentControl's package sources and extractor understand. See the
[**Authoring Agent Packages**](docs/user_guide/introduction.md#authoring-agent-packages)
section of the [User Guide][link-guide] for the full, source-verified guide to naming,
versioning, and structuring a package zip or NuGet package.

## Contributing

See [CONTRIBUTING.md](https://github.com/demaconsulting/AgentControl/blob/main/CONTRIBUTING.md) for
guidelines on reporting bugs, suggesting features, and submitting pull requests.

## License

Copyright (c) DEMA Consulting. Licensed under the MIT License. See [LICENSE][link-license] for details.

By contributing to this project, you agree that your contributions will be licensed under the MIT License.

<!-- Badge References -->
[badge-forks]: https://img.shields.io/github/forks/demaconsulting/AgentControl?style=plastic
[badge-stars]: https://img.shields.io/github/stars/demaconsulting/AgentControl?style=plastic
[badge-contributors]: https://img.shields.io/github/contributors/demaconsulting/AgentControl?style=plastic
[badge-license]: https://img.shields.io/github/license/demaconsulting/AgentControl?style=plastic
[badge-build]: https://img.shields.io/github/actions/workflow/status/demaconsulting/AgentControl/build_on_push.yaml?style=plastic
[badge-quality]: https://sonarcloud.io/api/project_badges/measure?project=demaconsulting_AgentControl&metric=alert_status
[badge-security]: https://sonarcloud.io/api/project_badges/measure?project=demaconsulting_AgentControl&metric=security_rating

<!-- Screenshot References -->
[shot-main-dark]: https://raw.githubusercontent.com/demaconsulting/AgentControl/main/docs/user_guide/images/main-window-dark.png
[shot-main-light]: https://raw.githubusercontent.com/demaconsulting/AgentControl/main/docs/user_guide/images/main-window-light.png

<!-- Link References -->
[link-forks]: https://github.com/demaconsulting/AgentControl/network/members
[link-stars]: https://github.com/demaconsulting/AgentControl/stargazers
[link-contributors]: https://github.com/demaconsulting/AgentControl/graphs/contributors
[link-license]: https://github.com/demaconsulting/AgentControl/blob/main/LICENSE
[link-build]: https://github.com/demaconsulting/AgentControl/actions/workflows/build_on_push.yaml
[link-quality]: https://sonarcloud.io/dashboard?id=demaconsulting_AgentControl
[link-security]: https://sonarcloud.io/dashboard?id=demaconsulting_AgentControl
[link-guide]: https://github.com/demaconsulting/AgentControl/blob/main/docs/user_guide/introduction.md
