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
- the process must have started exactly when the record says, to the tick (this detects a process id that Windows
  reused for another program; there is no tolerance, since a reused id inside one would be indistinguishable);
- the process command line must carry a `--config-dir` argument that names exactly the recorded folder (the command line
  is split with the Windows rules and the paths are compared after normalization, so a nested folder or one that only
  shares a prefix does not match; checked when the command line can be read).

The validated process handle is opened before the check and kept for the whole command, so `quit` (waiting, `--kill`) and
failed-`launch` cleanup act on that process only and never on an unrelated program that reused the process id.

All UI Automation lookups are scoped to the process id of the instance, never to a window title.

- `launch` starts `DemaConsulting.AgentControl.exe --config-dir <dir>` and waits until the process is alive, UI
  Automation attaches and the main window (titled `AgentControl`) is visible, then prints
  `OK instance=<alias> pid=<pid> exe=... config=... title=...`. It refuses (exit 4) when the alias already has a live
  instance, unless `--allow-existing` (the old instance is then left running and, once the new one is ready, the alias
  points to the new one; a launch that fails leaves the old record untouched). A
  launch that never becomes ready, or whose record cannot be written, is killed (process tree).
- **Isolation**: without `--config-dir` a fresh folder `%TEMP%\AgentControlPuppet\config-<alias>-<id>` is created, so
  the real `%APPDATA%\AgentControl\` settings are never touched. `--git-path` and `--agent-tool-command` are passed
  through to the application's startup options (the stored settings take precedence over them once loaded). Use
  [`fixture`](#fixture) to prepare a config folder with a package source and a repo.
- `instances` lists the live instances (`INSTANCE alias=... pid=... title=... exe=... config=... started=...`) and
  removes the records of the ones that are gone (`PRUNED alias=... reason=NotRunning|PidReused|ConfigMismatch`).
- `quit` closes the open dialogs (innermost first), then the main window (through the UI Automation Window pattern, the
  same request as the close button), and waits up to 10 seconds for the process to exit. `--kill` ends the whole process
  tree if it did
  not; the record is removed only once the process is confirmed gone (a survivor keeps its record and the command
  exits 2). A config folder that `launch` created is left in place so settings and logs
  (`<config>\logs`) can be inspected afterwards. An option a command does not accept (for example a misspelled
  `--instnace`) is refused with exit 5 instead of being ignored.

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
  and the release-notes window are non-modal windows and are listed too. A dialog opened from a dialog is listed as a
  dialog of its own, after the one that opened it (outer to inner), and each dialog lists only its own controls.
- `shot` renders the main window **and every open dialog and pop-up** (a flyout, a combo box drop-down) with
  `PrintWindow` and saves a PNG; see [Screenshots](#screenshots).
- `place` restores the main window from the maximized state and puts its **visible** frame (the invisible resize
  borders are compensated for) at the given physical pixels, without activating it. `--work-area` computes the
  rectangle from the primary screen's work area (the screen minus the taskbar): it keeps `--left-gap` pixels free on
  the left (default 0) and `--margin` pixels (default 8) on the other three sides. Use it before `shot` for screenshots
  with a predictable size and position.

### Scoping repeated ids

A repo card repeats its AutomationIds (`RepoNameText`, `LaunchButton`, `RepoStatusAccent`, ...) once per card, and
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

- **Main** (`AgentControl`): `AppLogo` (the logo in the app bar; its name is `AgentControl`), `FilterTextBox`, `AddRepoButton`,
  `OpenSettingsButton`, `OpenAboutButton`,
  `EmptyStatePanel`, `EmptyStateText`, `RepoListBox`.
- **Group header** (repeated, one per section, in `RepoListBox`; only present once a repo is in a group):
  `GroupHeaderToggleButton` (collapses or expands; its name says the section, count and state, for example
  `Work, 2 repos, collapsed`, and so does its list item), `GroupHeaderNameText`, `GroupHeaderCountText`, and, for
  the user's own groups, `GroupHeaderMenuButton` with the menu items `RenameGroupMenuItem` and `RemoveGroupMenuItem`.
  `--item` matches repo cards only, so a header command acts on the first header (the one at the top of the list);
  to start with a particular group collapsed, list it in `CollapsedGroups` in the settings file before `launch`.
- **Repo card** (repeated, one per list item): `RepoNameText`, `RepoStatusAccent` (the status bar; its name is the
  status in words), `RepoAvatarText`, `RepoAvatarImage`, `UpgradeAvailableBadge` (a button: click it to upgrade),
  `TagMovedChip` (a button, with `TagMovedBadge`: a Git tag that now names another commit; click it to open
  Change package), `PackageSourceIcon` and `PackageSourceAliasText` (the source's icon and alias in the package chip),
  CommittedAgentFilesBadge, DirtyWorkingTreeBadge, MissingRepoBadge, RepoPathText (a text control whose name is the
  full path; the path is shortened to fit, a long folder name in the middle), RepoPinnedVersionText,
  `CurrentBranchText`, `GroupChipText` (a favorite's group), `FavoriteToggleButton` (accessible name `Favorite`; a
  toggle: `read` shows `toggle=On|Off`), `UpToDateCue` (the check mark in the package chip of an up-to-date card),
  `LaunchButton`,
  `MoreActionsButton`.
- **Card menu** (pop-up of `MoreActionsButton`): `PullButton` (Git Pull), `RefreshMenuItem`, `SelectPackageMenuItem`
  (only for a repo that has no package yet), `ChangePackageMenuItem` (Change package..., only for a repo that has a
  package: it opens Select Package on the current choice), `UpgradeMenuItem` (only while an upgrade is available),
  `MoveToGroupMenuItem` (its submenu, built when the menu opens: one `MoveToGroupItem` per group - find it by
  name, for example `menu MoveToGroupMenuItem/Work` - then `NewGroupMenuItem`, and `RemoveFromGroupMenuItem` for a
  grouped repo), `CardColorMenuItem`, `RemoveMenuItem`.
- **Card color palette** (pop-up under `MoreActionsButton`, opened by `CardColorMenuItem`): `CardColorPalette`,
  one `CardColorSwatch_<Name>` per color (`CardColorSwatch_Teal`, ...; the name is also the accessible name) and
  `CardColorAutomaticButton`.
- **New group / Rename group** (`New group`, `Rename group`): `GroupNamePromptText`, `GroupNameTextBox`,
  `GroupNameErrorText`, `GroupNameHintText`, `GroupNameCancelButton`, `GroupNameAcceptButton`.
- **Settings** (`AgentControl Settings`): the package sources `SourcesList` (rows with `SourceAliasText`,
  `SourceLocationText`, `SourceStatusText`), `AddSourceButton` (opens the Add menu), its items
  `AddZipSourceButton`, `AddNuGetFolderSourceButton`, `AddNuGetServerSourceButton`, `AddGitSourceButton`,
  `EditSourceButton`,
  `RemoveSourceButton`, `SourcesEmptyText`; the Appearance section (`AppearanceHeadingText`, `ThemeComboBox`, named
  `Theme`) and the Tools section (`ToolsHeadingText`),
  `GitExecutablePathTextBox`, `AgentToolComboBox`, `CustomAgentCommandTextBox`, `ShellPreferenceComboBox`,
  `SettingsErrorTextBlock`, `SaveSettingsButton`. The **source editor** has `SourceAliasTextBox`,
  `SourceLocationTextBox`, `SourceBrowseButton`, `SourceEditorSaveButton`, `SourceEditorCancelButton` (and, for NuGet
  sources, the package ID, sign-in, signature and test-connection controls; for a Git repository
  `SourceGitPackageIdTextBox`, `SourceTagPatternTextBox` with `SourceTagPatternErrorText`, `SourceSubfolderTextBox`,
  `SourceGitSignInHelpText`, `SourceTestConnectionButton`, `SourceTestResultTitleText`, `SourceTestResultDetailText`
  and the tag preview `SourceTagPreviewSummaryText` and `SourceTagPreviewList`).
- **Select Package** (`Select Package`): `SelectPackageSourceList`, `SelectPackageNameList`,
  `SelectPackageVersionList`, `SelectPackageCancelButton`, `SelectPackageConfirmButton` (and the inline error and retry
  controls).
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

- `key` sends **real** key presses to the focused element of the instance (or to `--to <id>` after focusing it); the
  window that is raised for it is the innermost open **modal** dialog when there is one (the topmost titled dialog
  window by z-order, a dialog opened from a dialog sitting above its owner, whatever order UI Automation lists them
  in; a modal dialog keeps the keyboard, and the disabled main window behind it must not be pushed in front of it;
  a non-modal window such as the release-notes or About window leaves the main window usable, so it does not take
  the keys), else the main window. A
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
- `wheel <notches>` turns the wheel (positive up, negative down; a whole number from -1000 to 1000) at the centre of
  the main window, at the centre of
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
covers the windows UI Automation lists (dialogs, the non-modal About window, and dialogs opened from dialogs at any
depth, such as Settings, then a package-source editor, then a sign-in prompt) and the native pop-up windows of the
process that it does not list (a flyout, a combo box drop-down, a tooltip). The windows are drawn in z-order from the
bottom of the stack to the top, an owner always below the dialogs it owns, so a nested dialog covers the one that opened
it, as on screen. The output reports the size and the number of `windows=` and `popups=` drawn.

`shot --screen` copies the screen pixels of the window's frame instead, which also shows what only the compositor draws
(a drag ghost) but needs the window on screen and uncovered (it is brought to the front first; when two instances
overlap and the other keeps the foreground, place them apart or quit it). `PrintWindow` can lag one frame behind a
change: wait a moment after a command before a plain `shot`, or use `--screen`. Rounded or shadowed window chrome may
differ slightly from the screen.

## Fixture

```text
agentcontrol-puppet fixture <dir> [--packages zip|nupkg|both|git|all] [--repo] [--pin <name>@<version>]
```

Creates a ready-to-use sandbox in a new (or empty) folder for manual exploration, and prints the paths it created:

- `<dir>\packages\` - the package source, with sample packages `contoso-agents` 1.0.0 and 1.1.0 and `fabrikam-agents`
  2.0.0. `--packages zip` (the default) writes `{name}-{version}.zip` with `.github\agents`, `.github\standards`,
  `.github\templates`, `.github\skills\...`, `release-notes.md` and `AGENTS.md`; `nupkg` writes
  `{name}.{version}.nupkg` with the same files under `content\` and a minimal `{name}.nuspec`; `both` writes both.
  Zip packages are offered by a `Default` zip source and `.nupkg` packages by a NuGet folder source named `NuGet`
  (both pointing at this folder).
- `<dir>\git-source.git\` (`--packages git`, or `all` for zip, nupkg and git) - a **bare** git repository that is a
  `Git` package source named `Git` (tag pattern `release/{version}`, package `contoso-agents`). The package layout (the
  same files as the zip, at the repository root) is committed once per version of `contoso-agents` and tagged
  `release/1.0.0` (lightweight) and `release/1.1.0` (**annotated**), so both tag kinds are exercised. It is built
  locally with `git` from the PATH (a scratch work repository that is deleted afterwards, then a push into the bare
  repository) with a fixed author identity, signing off, no user or system git configuration and no prompts. Nothing
  touches the network: the source's address is the local path. `--packages git` writes no package files into
  `<dir>\packages\`. To get a "moved tag" for a screenshot, move the tag after the first sync, for example
  `git --git-dir <dir>\git-source.git tag -f release/1.0.0 "release/1.1.0^{commit}"`.
- `<dir>\repo\` (with `--repo`, implied by `--pin`) - a git repository (`git` from the PATH: `git init -b main` and one
  commit). `--pin contoso-agents@1.0.0` also commits an `.agentcontrol.json` pin (schema 2, naming the `Default`
  source, else `NuGet` with `--packages nupkg`, else `Git` with `--packages git`; no content identity is written), so
  the card shows a pinned version and
  an upgrade badge; without a pin the repo is "never synced" and its card offers `SelectPackageMenuItem`.
- `<dir>\config\settings.json` - the application's settings: `Sources` (the zip, NuGet and/or Git source above, the
  NuGet one naming the sample package IDs it offers, the Git one its `contoso-agents` package) and, with a repo,
  `RecentRepos` with the
  repo (and its pin). Nothing else is written, so every other setting keeps its default and the file loads in any
  version of the application. Start the application on it with `launch --config-dir <dir>\config`.

A Git package source fixture, with the tag moved after the first sync (the safety rules are the same: local paths only):

```powershell
& $puppet fixture "$env:TEMP\acp-git" --packages git --repo
& $puppet launch --config-dir "$env:TEMP\acp-git\config"
# ... select Git / contoso-agents / 1.0.0 on the card, pull, then move the tag to the other commit:
git --git-dir "$env:TEMP\acp-git\git-source.git" tag -f release/1.0.0 "release/1.1.0^{commit}"
# ... relaunch: the card shows TagMovedChip; card menu > ChangePackageMenuItem, same version, Yes accepts the change
```

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
& $puppet read SourceLocationText --raw
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

# Pull (the sample repo has no remote, so git reports a problem in a message box); Git Pull is in the card menu
& $puppet click MoreActionsButton --item repo
& $puppet click PullButton
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
