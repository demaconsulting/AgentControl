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

## Features

- **Recent-repos list**: add repos via a folder-browse button; each repo is shown as a card
  with its display name, path, pinned package/version, and current git branch
- **Version-pinned agent packages**: every repo names an exact agent package name and
  semantic version in its `.agentcontrol.json` pin file — there is no floating "latest" mode,
  so upgrades are always a deliberate, visible action
- **Status badges**: each repo card shows an upgrade-available badge, a committed-agent-files
  warning (advisory only — flags when the managed folders were accidentally committed to
  git), and a missing-repo indicator when the path no longer exists on disk
- **Ensure-synced-before-launch**: clicking Launch attempts a best-effort sync of the four
  managed agent folders before starting the agent tool; if they are missing (for example, a
  fresh clone) it silently re-extracts the currently pinned version first — it never
  auto-upgrades to a newer version on your behalf. This sync attempt is purely informational
  and never blocks the launch: repos with committed agent files skip it entirely, repos with
  no pin launch anyway, and a failed re-extraction only surfaces a warning
- **Git integration**: pull the latest commits for a repo directly from its card (offered
  only when the working tree is clean), with a configurable git executable override
- **Configurable agent tool**: choose GitHub Copilot CLI, Cursor, Claude Code, or a custom
  command line, plus a shell/terminal preference
- **Configurable package source**: agent packages are fetched from a configurable filesystem
  path (local drive, mapped drive, or UNC share)
- **Upgrade notifications**: a badge and release-notes viewer let you see when a newer agent
  package version is available and what changed before you apply it
- **About dialog**: reachable from the main window, shows the running app's version,
  copyright, and license

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

Click the folder-plus icon button on the toolbar and browse to a repository's root folder.
The repo is added to the recent-repos list as a card showing its display name, path, pinned
package/version (if any), and current git branch.

### Selecting a package

A repo that has never been synced shows a **Select Package...** action (in place of
**Upgrade**) in its card's "..." menu. This lists the distinct package names discoverable at
the configured package source, lets you pick one, then lists that package's available
versions (defaulting to the latest) before extracting and pinning it into the repo.

### Upgrading

Once a repo is pinned, its card's "..." menu instead shows **Upgrade** whenever a newer
version of the pinned package is discoverable at the package source. Upgrading re-runs the
same validate → delete → extract → pin sequence and then shows the new package's
`release-notes.md` (when present) in a non-modal window.

### Launching

Click **Launch** to start your configured agentic CLI tool in the repo's working directory.
Launch is never blocked by agent-package sync state — before launching, AgentControl makes a
best-effort attempt to sync the four managed agent folders for a pinned repo (silently
re-extracting the *currently pinned* version, never a newer one, if any are missing), but this
is purely an informational side action. A repo with committed agent files skips the sync
entirely (the managed folders are never touched), and a repo with no pin at all still launches
— an agentic CLI tool remains useful even with zero managed agent files present, and can help
with migrating away from committed agent files.

### Pulling changes

The **Pull** button on a repo card runs `git pull` in that repo, and is only offered when the
working tree is clean.

### Settings

The Settings window lets you configure:

- **Package source path** — the filesystem path (local, mapped drive, or UNC) from which
  agent package zip files are discovered
- **Git executable override** — a specific `git` executable to use instead of resolving it
  from `PATH`
- **Agent tool selection** — GitHub Copilot CLI, Cursor, Claude Code, or a custom command
- **Shell/terminal preference** — which shell to launch the agent tool in, or leave unset to
  let AgentControl auto-detect the best available shell

### About

The About dialog (reachable from the main window) shows the running build's version,
copyright, and license, so it can be identified without inspecting file properties.

## Authoring agent packages

Agent packages are plain zip files following a naming and content convention that
AgentControl's package source scanner and extractor understand. See the
[**Authoring Agent Packages**](docs/user_guide/introduction.md#authoring-agent-packages)
section of the [User Guide][link-guide] for the full, source-verified guide to naming,
versioning, and structuring a package zip.

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

<!-- Link References -->
[link-forks]: https://github.com/demaconsulting/AgentControl/network/members
[link-stars]: https://github.com/demaconsulting/AgentControl/stargazers
[link-contributors]: https://github.com/demaconsulting/AgentControl/graphs/contributors
[link-license]: https://github.com/demaconsulting/AgentControl/blob/main/LICENSE
[link-build]: https://github.com/demaconsulting/AgentControl/actions/workflows/build_on_push.yaml
[link-quality]: https://sonarcloud.io/dashboard?id=demaconsulting_AgentControl
[link-security]: https://sonarcloud.io/dashboard?id=demaconsulting_AgentControl
[link-guide]: https://github.com/demaconsulting/AgentControl/blob/main/docs/user_guide/introduction.md
