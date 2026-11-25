# ![AgentControl: Versioned agent folders synced into each repo][hero]

[![GitHub forks][badge-forks]][link-forks]
[![GitHub stars][badge-stars]][link-stars]
[![GitHub contributors][badge-contributors]][link-contributors]
[![License][badge-license]][link-license]
[![Build][badge-build]][link-build]
[![Quality Gate][badge-quality]][link-quality]
[![Security][badge-security]][link-security]

AgentControl is a Windows desktop launcher that lets a company distribute proprietary AI-agent configuration
alongside public source repositories, without committing that content to source control. Each repo pins an exact
package version in its `.agentcontrol.json` file. AgentControl syncs the repo's `.github/agents`,
`.github/standards`, `.github/templates`, and `.github/skills` folders to that version, then launches your agentic
CLI tool in the repo.

![Concept: a company publishes versioned agent packages; AgentControl reads the pin in a developer's public repo,
fetches that version from the developer's configured sources, verifies the files against the pinned fingerprint,
places them in the .github folders (normally git-ignored), and launches the agentic CLI tool][workflow]

## Screenshot

![AgentControl main window, dark theme: app bar, then repo cards in sections with status bars][shot-main-dark]

## Features

- 📌 **Version-Pinned Packages**: Pins an exact package version per repo in `.agentcontrol.json`
- 🔒 **Verified Agent Files**: Checks package-managed files against the pin's recorded fingerprint at launch
- 🔁 **Restore or Keep**: Asks whether to restore the pinned files or keep your changes
- 🗂️ **Multiple Package Sources**: Zip folders, NuGet folders and servers, or Git repository tags
- 🔑 **Protected Servers**: Signs in per session, with optional Git credential helper storage
- ✍️ **Signed Packages**: Requires validly signed NuGet packages, optionally from one publisher
- ⬆️ **Upgrade Notifications**: Shows newer versions and their release notes before you upgrade
- 📄 **AGENTS.md Template**: Offers a package's starter AGENTS.md to repos that lack one
- 🚀 **Configurable Agent Tool**: Launches Copilot CLI, Claude Code, Cursor, or a custom command
- 🐚 **Shell Choice**: Picks the launch shell and can keep it open after the tool exits
- 🌿 **Git Integration**: Shows the branch, runs Git Pull, and warns about committed agent files
- 🏷️ **Repo Cards**: Show a status bar and chips for package, upgrade, and branch
- ⭐ **Groups and Favorites**: Organizes repos with favorites, colored groups that can nest inside each other, card
  colors and a sort by name, all in your own order; select several repos to move or remove them together, and delete a
  group with or without its repos
- 🌗 **Light and Dark Themes**: Follows the system theme or uses a fixed choice
- ⚡ **Responsive**: Opens at once; repo, source and settings work runs in the background, with progress and Cancel
- 📦 **Easy Install**: Ships as an MSI installer with a self-contained .NET runtime

## Installation

1. Download the latest `AgentControl-{version}-win-x64.msi` from the [releases][link-releases] page.
2. Run the installer and follow the prompts.
3. Launch **AgentControl** from the Start menu.

The application is self-contained, so the machine needs no .NET runtime. Only a Git repository package source needs
[Git](https://git-scm.com/downloads) 2.29 or newer; the other source kinds need no Git.

## Getting Started

1. Open **Settings** and add a package source: a zip folder, a NuGet folder or server, or a Git repository.
2. Click **Add repository** and choose one or more Git repository folders, or drag them from File Explorer onto the
   list.
3. In the repo card's "..." menu, choose **Select Package...** and pick a package and version.
4. Click **Launch** to start your agentic CLI tool in the repo.

See the [User Guide][link-guide] for everything else: groups, upgrades, sign-in, and the Settings window.

## Security

- AgentControl never stores secrets. A sign-in is kept in memory for the session, unless you choose to hand it to
  your own Git credential helper.
- When a pin records a content fingerprint, agent files are verified against it, so a republished version is not
  applied silently (a cached NuGet copy can hide a server-side republish until the cache is cleared; pins from older
  versions have no fingerprint).
- Git sources run with a hardened command line (no hooks, submodules, or LFS), and NuGet sources can require signed
  packages.

See the User Guide sections on [version immutability and trust][link-guide-trust],
[signed packages][link-guide-signed], and [what is stored][link-guide-stored].

## Documentation

- [User Guide][link-guide]: installation, using AgentControl, and configuring package sources
- [Authoring Agent Packages][link-guide-authoring]: how to build a zip, NuGet, or Git agent package
- [Architecture][link-architecture]: the system design

## Contributing

See [CONTRIBUTING.md][link-contributing] for guidelines on reporting bugs, suggesting features, and submitting pull
requests.

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

<!-- Image References -->
[hero]: https://raw.githubusercontent.com/demaconsulting/AgentControl/main/docs/images/hero.png
[workflow]: https://raw.githubusercontent.com/demaconsulting/AgentControl/main/docs/images/agent-workflow.png
[shot-main-dark]: https://raw.githubusercontent.com/demaconsulting/AgentControl/main/docs/user_guide/images/main-window-dark.png

<!-- Link References -->
[link-forks]: https://github.com/demaconsulting/AgentControl/network/members
[link-stars]: https://github.com/demaconsulting/AgentControl/stargazers
[link-contributors]: https://github.com/demaconsulting/AgentControl/graphs/contributors
[link-license]: https://github.com/demaconsulting/AgentControl/blob/main/LICENSE
[link-build]: https://github.com/demaconsulting/AgentControl/actions/workflows/build_on_push.yaml
[link-quality]: https://sonarcloud.io/dashboard?id=demaconsulting_AgentControl
[link-security]: https://sonarcloud.io/dashboard?id=demaconsulting_AgentControl
[link-releases]: https://github.com/demaconsulting/AgentControl/releases
[link-contributing]: https://github.com/demaconsulting/AgentControl/blob/main/CONTRIBUTING.md
[link-architecture]: https://github.com/demaconsulting/AgentControl/blob/main/architecture.md
[link-guide]: https://github.com/demaconsulting/AgentControl/blob/main/docs/user_guide/introduction.md
[link-guide-authoring]: https://github.com/demaconsulting/AgentControl/blob/main/docs/user_guide/introduction.md#authoring-agent-packages
[link-guide-trust]: https://github.com/demaconsulting/AgentControl/blob/main/docs/user_guide/introduction.md#version-immutability-and-trust
[link-guide-signed]: https://github.com/demaconsulting/AgentControl/blob/main/docs/user_guide/introduction.md#requiring-signed-packages
[link-guide-stored]: https://github.com/demaconsulting/AgentControl/blob/main/docs/user_guide/introduction.md#what-is-stored
