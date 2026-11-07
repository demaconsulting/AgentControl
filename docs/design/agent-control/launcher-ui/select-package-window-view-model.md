### SelectPackageWindowViewModel

![LauncherUI Structure](LauncherUIView.svg)

#### Purpose

`SelectPackageWindowViewModel` backs the "Select Package..." dialog: lists distinct package
names discoverable at one named package source, then that name's discoverable versions
(descending, defaulting to the latest), per architecture.md's "Initial package selection"
decision. It lets a repo with no existing pin acquire its first pinned package/version, and
reports the source alias with the selection so the pin records it
(`AgentControl-SelectPackageWindowViewModel-SourceScoped`).

#### Data Model

**SourceAlias**: `string` — The alias of the source whose packages are listed; the dialog never
includes another source's packages.

**PackageNames**: `IReadOnlyList<string>` — The distinct package names discoverable at the
source, populated once at construction from the shared cache.

**SelectedPackageName**: `string?` — The package name currently selected by the user; setting
it recomputes `AvailableVersions` and resets `SelectedVersion` to the first (highest/latest)
entry, or `null` if the chosen name has no discoverable versions.

**AvailableVersions**: `IReadOnlyList<string>` — The currently-selected package name's
discoverable versions, descending (highest first).

**SelectedVersion**: `string?` — The version currently selected by the user.

**CanConfirm**: `bool` (derived) — `true` only when both `SelectedPackageName` and
`SelectedVersion` are non-null.

#### Key Methods

**Constructor**: Populates `PackageNames` from the chosen source via the shared cache.

- *Parameters*: `PackageSourceRegistry registry`, `string sourceAlias`,
  `PackageVersionCache cache`.
- *Preconditions*: `registry` and `cache` are not null and `sourceAlias` is not blank.
- *Postconditions*: `PackageNames` reflects the cache's package IDs for `sourceAlias`; an empty
  source produces an empty list and `CanConfirm` starts `false`
  (`AgentControl-SelectPackageWindowViewModel-ListPackages`). An alias that is not configured
  throws `UnknownSourceException`.

All source access routes through the shared, session-scoped `PackageVersionCache` rather than a
provider directly, so opening this dialog a second time in the same app session does not query the
source again. The asynchronous cache is awaited on a worker thread because the lists are
populated before the dialog is shown; a fully asynchronous dialog with progress and cancellation
belongs to the multi-source UI work.

**SelectedPackageName (setter)**: Recomputes `AvailableVersions` for the newly-selected name.

- *Postconditions*: `AvailableVersions` reflects the cache's descending versions for
  (`SourceAlias`, value) as version strings; `SelectedVersion` is reset to the first entry, or
  `null` if none (`AgentControl-SelectPackageWindowViewModel-VersionSelection`).

**Confirm** (private, bound to `ConfirmCommand`): Raises `Confirmed` with the current
selection.

- *Parameters*: None.
- *Returns*: `void`.
- *Preconditions*: Both `SelectedPackageName` and `SelectedVersion` are non-null (enforced by
  `ConfirmCommand`'s `CanExecute` predicate referencing `CanConfirm`)
  (`AgentControl-SelectPackageWindowViewModel-ConfirmGating`).
- *Postconditions*: `Confirmed` is raised with the selected `(Name, Version)` pair
  (`AgentControl-SelectPackageWindowViewModel-Confirm`).

#### Error Handling

The constructor throws `ArgumentNullException` for a null `registry` or `cache`,
`ArgumentException` for a blank `sourceAlias`, and `UnknownSourceException` for an alias that is
not configured.
`Confirm` performs a defensive null check and silently returns without raising `Confirmed` if
either selection is somehow null, even though `ConfirmCommand`'s `CanExecute` should already
prevent that call. No other method throws; all source-failure exceptions (for example
`DirectoryNotFoundException`) originate from the underlying `PackageVersionCache`/provider calls,
not this view model itself.

#### Dependencies

- **PackageVersionCache**, **PackageSourceRegistry** (`AgentPackageManagement` subsystem) —
  sole source of package names and versions; the cache is shared with `MainWindowViewModel` and
  every `RepoCardViewModel`.
- **RelayCommand** (`LauncherUI` subsystem, shared MVVM support) — backs `ConfirmCommand`.

#### Callers

- **RepoCardViewModel** — constructs this view model (via `SelectPackageRequested`) when the
  user chooses "Select Package..." for a repo with no existing pin, and subscribes to
  `Confirmed` to apply the chosen package/version (`ApplySelectedPackage`).
