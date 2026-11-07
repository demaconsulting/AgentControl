# AgentControl Puppet

`agentcontrol-puppet` is a stateless driver for the AgentControl desktop application. It lets an AI coding agent or a
test script drive the **real GUI** one action at a time: UI Automation (FlaUI UIA3) for buttons, text boxes, lists,
menus, dialogs and screenshots, and **real operating-system input** (cursor, mouse wheel, key presses) for the few
things UI Automation patterns cannot do (tooltips, drags, key chords).

It exists so that an agent can explore AgentControl, check what is actually drawn and reproduce a UI problem without a
human clicking through the app. It is agent tooling: it is **not shipped** and is **not a software item** of
AgentControl (it has no requirements, design or verification documents and no review-set; ReviewMark excludes it).

## Warning

Some commands drive the real desktop. `key`, `hover`, `click-at`, `drag-element`, `wheel` and `shot --screen` move the
real mouse cursor, send real key presses or bring the window to the front. Do not use the keyboard or mouse while such
a command runs, and never run them on a locked or remote-disconnected session (see [Safety checks](#safety-checks)).
All other commands (`launch`, `instances`, `quit`, `window`, `tree`, `find`, `click`, `type`, `choose`, `select`,
`read`, `menu`, `dialogs`, `shot`, `place`, `fixture`) use UI Automation patterns or Win32 window calls and do not move
the cursor or send key presses, so they can run while someone else works on the desktop.

Each command is a fresh process: it finds the AgentControl instance by its alias, does one thing, prints plain text and
exits with a documented code. All state lives in the AgentControl process (and in the small instance record that
`launch` writes, see [Instances](#instances)).

## Build

```powershell
dotnet build DemaConsulting.AgentControl.slnx -c Release
```

The Puppet is `tools\DemaConsulting.AgentControl.Puppet\bin\Release\net10.0-windows\agentcontrol-puppet.exe`. `launch`
starts the newest `DemaConsulting.AgentControl.exe` under `src\DemaConsulting.AgentControl\bin\{Release,Debug}\net10.0*`
of the repository the Puppet itself runs from (or beside the Puppet); pass `--exe <path>` for any other build. Build the
application too, or `launch` reports that it cannot find it.

## Instances

Every command takes `--instance <alias>` (default `main`). Aliases use letters, digits, `-` and `_`; any number of
instances can run side by side.

```text
agentcontrol-puppet launch [--instance <alias>] [--exe <path>] [--config-dir <dir>] [--git-path <p>]
                           [--agent-tool-command <cmd>] [--allow-existing]
agentcontrol-puppet instances
agentcontrol-puppet quit [--instance <alias>] [--kill]
```

AgentControl has no automation endpoint, and all instances share the same window titles ("AgentControl",
"AgentControl Settings", "Select Package", "About AgentControl"), so the Puppet never finds an instance by title.
Instead `launch` writes a record per alias to `%TEMP%\AgentControlPuppet\instances\<alias>.json` (process id, process
start time, executable and `--config-dir`). Every command validates the record before it touches anything:

- the process must be alive (a record of an exited instance is stale: exit 3);
- the process must have started when the record says (this detects a process id that Windows reused for another
  program);
- the process command line must carry a `--config-dir` argument that names exactly the recorded folder (the command line
  is split with the Windows rules and the paths are compared after normalization, so a nested folder or one that only
  shares a prefix does not match; checked when the command line can be read).

The validated process handle is opened before the check and kept for the whole command, so `quit` (waiting, `--kill`) and
failed-`launch` cleanup act on that process only and never on an unrelated program that reused the process id.

All UI Automation lookups are scoped to the process id of the instance, never to a window title.

- `launch` starts `DemaConsulting.AgentControl.exe --config-dir <dir>` and waits until the process is alive, UI
  Automation attaches and the main window (titled `AgentControl`) is visible, then prints
  `OK instance=<alias> pid=<pid> exe=... config=... title=...`. It refuses (exit 4) when the alias already has a live
  instance, unless `--allow-existing` (the old instance is then left running and the alias points to the new one). A
  launch that never becomes ready is killed (process tree) and its record removed.
- **Isolation**: without `--config-dir` a fresh folder `%TEMP%\AgentControlPuppet\config-<alias>-<id>` is created, so
  the real `%APPDATA%\AgentControl\` settings are never touched. `--git-path` and `--agent-tool-command` are passed
  through to the application's startup options (the stored settings take precedence over them once loaded). Use
  [`fixture`](#fixture) to prepare a config folder with a package source and a repo.
- `instances` lists the live instances (`INSTANCE alias=... pid=... title=... exe=... config=... started=...`) and
  removes the records of the ones that are gone (`PRUNED alias=... reason=NotRunning|PidReused|ConfigMismatch`).
- `quit` closes the open dialogs, then the main window (through the UI Automation Window pattern, the same request as
  the close button), and waits up to 10 seconds for the process to exit. `--kill` ends the whole process tree if it did
  not. The record is removed. A config folder that `launch` created is left in place so settings and logs
  (`<config>\logs`) can be inspected afterwards.

## UI Automation commands

```text
agentcontrol-puppet window
agentcontrol-puppet tree [--depth <N>] [--under <automationId>]
agentcontrol-puppet find <automationId>
agentcontrol-puppet read <automationId> [--raw]
agentcontrol-puppet click <automationId>
agentcontrol-puppet type <automationId> "<text>"
agentcontrol-puppet choose <comboAutomationId> "<item text>"
agentcontrol-puppet select <listAutomationId> "<item text or index>"
agentcontrol-puppet menu <Menu>/<Item>[/<Sub>]
agentcontrol-puppet dialogs
agentcontrol-puppet shot <path.png> [--screen]
agentcontrol-puppet place (--x <px> --y <px> --width <px> --height <px> | --work-area [--left-gap <px>] [--margin <px>])
```

- Elements are found by AutomationId in the main window, dialogs and pop-ups. A miss prints the closest ids
  ("Did you mean ..."). Nothing is tied to a particular id, so ids that a later version adds work without a change to
  the Puppet.
- `window` prints the main window's title and bounds and the number of windows of the instance.
- `tree` prints one line per element: control type, `id=`, `name=`, `[disabled]` and `help=` (the tooltip, when UI
  Automation exposes it, which AgentControl's controls do). Dialogs and pop-ups are listed after the main window.
- `find` prints control type, name, enabled and offscreen flags, bounds and help text. It also works on **disabled**
  elements, so a disabled control and its tooltip can be inspected.
- `read` prints `READ id=... text="..." enabled=... [toggle=...] [selected=...]`; `--raw` prints only the text (the
  Value pattern's value, else the name, else the element's text children).
- `click` invokes through UI Automation patterns (invoke, select, toggle), so it works on buttons, menu items, list
  items and check boxes without moving the cursor. A disabled element exits with 6.
- `type` sets the value through the Value pattern (falling back to real typing for an element without one; exit 9 for a
  read-only element, 6 for a disabled one). `choose` expands a combo box and selects an item by its text.
- `select` selects an item of a list by its text (exact, case-insensitive; else a unique part of the text) or by its
  zero-based index, for example `select SelectPackageVersionList 1.1.0` or `select RepoListBox my-repo`.
- `menu` opens the menus of a path and invokes the last item; a segment is an item's AutomationId or its text (access
  keys, spaces, case and a trailing `...` are ignored). AgentControl has no menu bar: its menu is the pop-up of a repo
  card's "more actions" button, so open it first (`click MoreActionsButton --item my-repo`), then `menu RefreshMenuItem`
  (or `click RefreshMenuItem`).
- `dialogs` lists the open dialogs and their buttons, edits, check boxes, combo boxes, radio buttons, lists and
  identified texts with id, name, enabled flag and help text; the exit code is 10 while one is open. The About window
  and the release-notes window are non-modal windows and are listed too.
- `shot` renders the main window **and every open dialog and pop-up** (a flyout, a combo box drop-down) with
  `PrintWindow` and saves a PNG; see [Screenshots](#screenshots).
- `place` restores the main window from the maximized state and puts its **visible** frame (the invisible resize
  borders are compensated for) at the given physical pixels, without activating it. `--work-area` computes the
  rectangle from the primary screen's work area (the screen minus the taskbar): it keeps `--left-gap` pixels free on
  the left (default 0) and `--margin` pixels (default 8) on the other three sides. Use it before `shot` for screenshots
  with a predictable size and position.

### Scoping repeated ids

A repo card repeats its AutomationIds (`RepoNameText`, `LaunchButton`, `PullButton`, ...) once per card in the list, and
dialogs can repeat the main window's ids, so every element command accepts two scopes:

- `--item <repo name | index>` limits the lookup to one item of `RepoListBox`: the card whose `RepoNameText` is the
  given name (exact, ignoring case; else a unique part of the name), or the card at that zero-based index. A name that
  fits several cards is refused (use the index). Example: `click LaunchButton --item my-repo`,
  `read RepoPinnedVersionText --item 0 --raw`. The repo list is virtualized, so an index counts the cards that are
  currently realized (those scrolled out of view may not exist); prefer names. `--item-list <id>` names another list whose
  items should be searched (matched by any text the item shows).
- `--window <title>` limits the lookup to the windows of the instance with that title (exact, ignoring case; else a
  part of the title), for example `type GitExecutablePathTextBox "C:\tools\git.exe" --window "AgentControl Settings"`.

The pop-up of the "more actions" button is shared by all cards, so open it with `--item` and then use the menu item's id
without it.

### AutomationIds

- **Main** (`AgentControl`): `FilterTextBox`, `AddRepoButton`, `OpenSettingsButton`, `OpenAboutButton`,
  `EmptyStatePanel`, `EmptyStateText`, `RepoListBox`.
- **Repo card** (repeated, one per list item): `RepoNameText`, `UpgradeAvailableBadge`, `CommittedAgentFilesBadge`,
  `DirtyWorkingTreeBadge`, `MissingRepoBadge`, `RepoPathText`, `RepoPinnedVersionText`, `CurrentBranchText`,
  `FavoriteToggleButton`, `LaunchButton`, `PullButton`, `MoreActionsButton`.
- **Card menu** (pop-up of `MoreActionsButton`): `RefreshMenuItem`, `SelectPackageMenuItem` (only for a repo that has
  no package yet), `UpgradeMenuItem`, `RemoveMenuItem`.
- **Settings** (`AgentControl Settings`): `PackageSourcePathTextBox`, `BrowsePackageSourceButton`,
  `GitExecutablePathTextBox`, `AgentToolComboBox`, `CustomAgentCommandTextBox`, `ShellPreferenceComboBox`,
  `SaveSettingsButton`.
- **Select Package** (`Select Package`): `SelectPackageNameList`, `SelectPackageVersionList`,
  `SelectPackageCancelButton`, `SelectPackageConfirmButton`.
- **About** (`About AgentControl`): `AboutTaglineText`, `AboutVersionText`, `AboutCopyrightText`, `AboutLicenseText`,
  `AboutDependenciesList`, `AboutCloseButton`.
- **Confirmation** (`AgentControl`): `ConfirmationMessageText`, `ConfirmationNoButton`, `ConfirmationYesButton`.
- **Message box**: `MessageText`, `MessageBoxOkButton`.
- **Release notes**: `ReleaseNotesPanel`, `ReleaseNotesHeading`.

The ids come from `src/DemaConsulting.AgentControl/**/*.axaml`; `tree` and `dialogs` print what is actually there.
The window chrome has ids too (`Close`, `Maximize-Restore`, `Minimize-Restore`), which is how to close a dialog that has
no button of its own: `click Close --window "AgentControl Settings"`. Badges and other controls that carry no
AutomationId of their own appear in `tree` by name.

## Real-input commands

```text
agentcontrol-puppet key <chord> [--to <automationId>]
agentcontrol-puppet hover <automationId> [--wait <ms>] [--shot <path.png>]
agentcontrol-puppet click-at <x> <y> [--right] [--double] [--shift|--ctrl]
agentcontrol-puppet drag-element <automationId> <dx> <dy>
agentcontrol-puppet wheel <notches> [--ctrl] [--on <automationId> | --at <x>,<y>]
```

- `key` sends **real** key presses to the focused element of the instance (or to `--to <id>` after focusing it). A
  chord is modifiers joined with `+`: `Ctrl+C`, `Ctrl+Shift+Z`, `Delete`, `F2`, `Escape`, `Enter`, `Ctrl+Enter`,
  letters, digits, `F1`-`F12`, `Tab`, `Space`, `Backspace`, arrows, `Home`/`End`/`PageUp`/`PageDown`, `Plus`, `Minus`.
- `hover <id>` rests the real cursor on the centre of the element so its tooltip opens. It only reads the element's
  bounds, so it works on **disabled** elements (their tooltip explains why they are disabled, for example the "Require
  signed packages" check box on a platform that does not support it). The cursor first **parks** beside the element
  (the nearest point outside it, on the instance's window, with the instance's window on top there), which closes a
  tooltip that is showing and resets the control's tooltip state, then moves onto the element in small steps and rests.
  `--wait <ms>` is the rest (default **700**, comfortably longer than Avalonia's 400 ms tooltip delay). `--shot
  <path.png>` saves a screen copy (as `shot --screen`) while the cursor still rests, so the picture shows the open
  tooltip; the window is not raised again before the copy, so keep it uncovered. Output:
  `OK hover id=... at=(x,y) park=(x,y)|none wait=700 shot="..."`.
- `click-at <x> <y>` clicks a physical screen pixel (read element bounds with `find`); `--right` uses the right button,
  `--double` double-clicks, `--shift` and `--ctrl` hold the modifier.
- `drag-element <id> <dx> <dy>` presses at the centre of the element and drags by a screen offset (a splitter, a
  scrollbar thumb).
- `wheel <notches>` turns the wheel (positive up, negative down) at the centre of the main window, at the centre of
  `--on <id>`, or at the screen pixel `--at x,y`; `--ctrl` holds Ctrl.

Coordinates are **physical screen pixels**; the Puppet is per-monitor DPI aware, so `find` bounds and cursor positions
agree on scaled monitors.

### Safety checks

Before any real input the Puppet checks that the topmost window at the target pixel belongs to the instance. If not
(another window covers it, it is off screen or scrolled out of view, the session is locked) **nothing is sent** and the
command exits with code 11. `key` likewise exits with 7 unless an element of the instance holds keyboard focus. This
protects the user's other windows. Stale modifier keys are released before real input, since a held Ctrl or Shift would
change what the application shows.

## Screenshots

`shot <path.png>` draws the main window **and every open dialog and pop-up** with `PrintWindow`, each window rendered by
the window itself, so a window in front does not end up in the picture and it works on a locked desktop as well. It
covers the windows UI Automation lists (dialogs, the non-modal About window) and the native pop-up windows of the
process that it does not list (a flyout, a combo box drop-down, a tooltip). The output reports the size and the number
of `windows=` and `popups=` drawn.

`shot --screen` copies the screen pixels of the window's frame instead, which also shows what only the compositor draws
(a drag ghost) but needs the window on screen and uncovered (it is brought to the front first; when two instances
overlap and the other keeps the foreground, place them apart or quit it). `PrintWindow` can lag one frame behind a
change: wait a moment after a command before a plain `shot`, or use `--screen`. Rounded or shadowed window chrome may
differ slightly from the screen.

## Fixture

```text
agentcontrol-puppet fixture <dir> [--packages zip|nupkg|both] [--repo] [--pin <name>@<version>]
```

Creates a ready-to-use sandbox in a new (or empty) folder for manual exploration, and prints the paths it created:

- `<dir>\packages\` - the package source, with sample packages `contoso-agents` 1.0.0 and 1.1.0 and `fabrikam-agents`
  2.0.0. `--packages zip` (the default) writes `{name}-{version}.zip` with `.github\agents`, `.github\standards`,
  `.github\templates`, `.github\skills\...`, `release-notes.md` and `AGENTS.md`; `nupkg` writes
  `{name}.{version}.nupkg` with the same files under `content\` and a minimal `{name}.nuspec`; `both` writes both.
  **The current AgentControl (main) cannot consume `.nupkg` packages yet**: NuGet package sources arrive with the
  multi-source packages feature, so with the current application use `zip`. The option and the output note
  (`NOTE .nupkg packages ...`) exist so fixtures for that feature can already be prepared.
- `<dir>\repo\` (with `--repo`, implied by `--pin`) - a git repository (`git` from the PATH: `git init -b main` and one
  commit). `--pin contoso-agents@1.0.0` also commits an `.agentcontrol.json` pin, so the card shows a pinned version and
  an upgrade badge; without a pin the repo is "never synced" and its card offers `SelectPackageMenuItem`.
- `<dir>\config\settings.json` - the application's settings: `PackageSourcePath` and, with a repo, `RecentRepos` with the
  repo (and its pin). Nothing else is written, so every other setting keeps its default and the file loads in any
  version of the application. Start the application on it with `launch --config-dir <dir>\config`.

## Exit codes

| Code | Meaning |
| ---: | --- |
| 0 | Success |
| 1 | Element, list item, menu item or executable not found |
| 2 | Timeout (an instance did not exit after `quit`) |
| 3 | Instance not running (no live instance under that alias) |
| 4 | Launch failed or refused |
| 5 | Usage error |
| 6 | Element disabled |
| 7 | No usable keyboard focus |
| 8 | Input had no effect |
| 9 | Element is read-only |
| 10 | A dialog is open (`dialogs`) |
| 11 | Target not on the instance's window; real input refused |
| 99 | Unexpected error (add `--trace` for the stack) |

Code 12 of other Puppets (an application automation endpoint rejected a command) does not exist here: AgentControl has no
such endpoint.

## Worked example: fixture, settings, select a package, pull

```powershell
$puppet = '.\tools\DemaConsulting.AgentControl.Puppet\bin\Release\net10.0-windows\agentcontrol-puppet.exe'
$box = "$env:TEMP\acp-demo"

# A sandbox: sample packages, a git repo that was never synced, and settings that point at them
& $puppet fixture $box --repo
& $puppet launch --config-dir "$box\config"            # instance 'main'; prints the window title and the config folder

# Look at the settings window
& $puppet click OpenSettingsButton
& $puppet dialogs                                       # exit 10: the dialog and its controls are listed
& $puppet read PackageSourcePathTextBox --raw
& $puppet shot "$box\settings.png"                      # the picture shows the Settings window over the main window
& $puppet click Close --window "AgentControl Settings"

# Pin the repo to a package: open the card menu, pick Select Package, choose a name and a version
& $puppet click MoreActionsButton --item repo
& $puppet shot "$box\menu.png"                          # the open card menu is in the picture
& $puppet menu SelectPackageMenuItem
& $puppet select SelectPackageNameList contoso-agents
& $puppet select SelectPackageVersionList 1.0.0
& $puppet shot "$box\select-package.png"
& $puppet click SelectPackageConfirmButton              # extracts the package; release notes and a question follow
& $puppet click ConfirmationNoButton
& $puppet click Close --window "Release Notes"
& $puppet read RepoPinnedVersionText --item repo --raw  # Pinned version: 1.0.0

# Pull (the sample repo has no remote, so git reports a problem in a message box)
& $puppet click PullButton --item repo
& $puppet dialogs

# Tooltips and disabled controls (real cursor)
& $puppet hover LaunchButton --item repo --shot "$box\tip.png"

& $puppet quit
```

For the upgrade path create the fixture with `--pin contoso-agents@1.0.0`: the card then shows an upgrade badge and its
menu offers `UpgradeMenuItem` (version 1.1.0) instead of `SelectPackageMenuItem`.

## Notes

- The Puppet needs an **unlocked interactive desktop** for mouse and key input. UI Automation (`tree`, `click`,
  `type`, `select`, `menu`, `dialogs`) and `shot` keep working on a locked session; `key`, `hover`, `click-at`,
  `drag-element` and `wheel` then refuse (exit 11 / 7) instead of guessing.
- A running instance is found by the record `launch` wrote, so an AgentControl that was started by hand cannot be
  driven; start it with `launch`.
- AgentControl's non-modal windows (About, release notes) are top-level windows of the process. The Puppet picks the
  main window by its AutomationId (`MainWindowRoot`) or its title, not by what Windows calls the process's main window.
- The Puppet is not part of AgentControl: it is built and unit-tested (argument, chord, placement, hover planning,
  instance-record validation, launch arguments, build lookup, list-item matching and fixture creation) in
  `test/DemaConsulting.AgentControl.Puppet.Tests`, but no UI runs in those tests. The unit tests and the build run in CI;
  the Puppet itself only runs on Windows.
