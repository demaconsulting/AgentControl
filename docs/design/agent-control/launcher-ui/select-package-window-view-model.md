### SelectPackageWindowViewModel

![LauncherUI Structure](LauncherUIView.svg)

#### Purpose

`SelectPackageWindowViewModel` backs the "Select Package..." dialog: lists distinct package
names discoverable at a configured source, then that name's discoverable versions (descending,
defaulting to the latest), per architecture.md's "Initial package selection" decision. It
lets a repo with no existing pin acquire its first pinned package/version.

#### Data Model

**PackageNames**: `IReadOnlyList<string>` — The distinct package base names discoverable at
the configured source, populated once at construction from the shared cache.

**SelectedPackageName**: `string?` — The package name currently selected by the user; setting
it recomputes `AvailableVersions` and resets `SelectedVersion` to the first (highest/latest)
entry, or `null` if the chosen name has no discoverable versions.

**AvailableVersions**: `IReadOnlyList<string>` — The currently-selected package name's
discoverable versions, descending (highest first).

**SelectedVersion**: `string?` — The version currently selected by the user.

**CanConfirm**: `bool` (derived) — `true` only when both `SelectedPackageName` and
`SelectedVersion` are non-null.

#### Key Methods

**Constructor**: Populates `PackageNames` from the configured source via the shared cache.

- *Parameters*: `string sourceDirectory`, `PackageVersionCache cache`.
- *Preconditions*: Neither parameter is null.
- *Postconditions*: `PackageNames` reflects `cache.GetPackageNames(sourceDirectory)`; an empty
  source produces an empty list and `CanConfirm` starts `false`
  (`AgentControl-SelectPackageWindowViewModel-ListPackages`).

All filesystem access routes through the shared, session-scoped `PackageVersionCache` rather
than `PackageSource` directly, so opening this dialog a second time in the same app session
does not re-scan the filesystem.

**SelectedPackageName (setter)**: Recomputes `AvailableVersions` for the newly-selected name.

- *Postconditions*: `AvailableVersions` reflects `cache.GetVersionsDescending(sourceDirectory,
  value)`'s version strings, descending; `SelectedVersion` is reset to the first entry, or
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

The constructor throws `ArgumentNullException` for a null `sourceDirectory` or `cache`.
`Confirm` performs a defensive null check and silently returns without raising `Confirmed` if
either selection is somehow null, even though `ConfirmCommand`'s `CanExecute` should already
prevent that call. No other method throws; all filesystem-failure exceptions originate from
the underlying `PackageVersionCache`/`PackageSource` calls, not this view model itself.

#### Dependencies

- **PackageVersionCache** (`AgentPackageManagement` subsystem) — sole source of package names
  and versions; shared with `MainWindowViewModel` and every `RepoCardViewModel`.
- **RelayCommand** (`LauncherUI` subsystem, shared MVVM support) — backs `ConfirmCommand`.

#### Callers

- **RepoCardViewModel** — constructs this view model (via `SelectPackageRequested`) when the
  user chooses "Select Package..." for a repo with no existing pin, and subscribes to
  `Confirmed` to apply the chosen package/version (`ApplySelectedPackage`).
