### SelectPackageWindowViewModel

![LauncherUI Structure](LauncherUIView.svg)

#### Purpose

`SelectPackageWindowViewModel` backs the "Select Package..." dialog. The dialog has three
columns: the configured package sources, the packages of the selected source, and the versions
of the selected package (descending, defaulting to the latest), per architecture.md's "Initial
package selection" decision. It lets a repo with no existing pin acquire its first pinned
package, and confirming reports the full package reference - source alias, package name and
version - so the pin records which source the package came from
(`AgentControl-SelectPackageWindowViewModel-SourceScoped`). Sources are shown side by side rather
than merged into one list because the same package name can exist in several sources and the
user must see which one they are choosing from.

#### Data Model

**Sources**: `IReadOnlyList<SelectPackageSourceViewModel>` — One row per configured source, in
configured order, each with its alias, kind icon and label, and its own state: loading, loaded
with a package list, failed with a message, or empty.

**SelectedSource**: `SelectPackageSourceViewModel?` — The source whose packages the second column
shows. Setting it clears the package and version selection.

**PackageNames**: `IReadOnlyList<string>` — The package names of the selected source once it is
loaded.

**SelectedPackageName**: `string?` — The package name currently selected by the user; setting
it starts a background read of its versions, clears the previous versions and selected version,
and supersedes a read still running for an earlier selection.

**AvailableVersions**: `IReadOnlyList<string>` — The selected package's versions, descending
(highest first).

**SelectedVersion**: `string?` — The version currently selected; defaults to the first (highest)
entry when the versions arrive.

**IsLoading** / **IsLoadingVersions**: `bool` — Drive the progress indicators for the source
list and the version list.

**SourceErrorMessage** / **HasSourceError** / **HasNoPackages** / **VersionsError**: Inline
messages for the selected source or package.

**CanRetry** / **RetryCommand**: `bool` / command — `CanRetry` is `true` while there is a failed read to
retry: the selected source could not be read, or the selected package's versions could not be read. The
**Retry** button beside the inline error is bound to `RetryCommand`.

**CanConfirm**: `bool` (derived) — `true` only when a source, a package name and a version are
all selected.

#### Key Methods

**Constructor**: Lists every source of the registry without reading any of them.

- *Parameters*: `PackageSourceRegistry registry`, `PackageVersionCache cache`, and an optional
  alias of the source to start on.
- *Preconditions*: `registry` and `cache` are not null.
- *Postconditions*: `Sources` holds one row per configured source; the preferred source (or the
  first when none or an unknown alias is given) is selected; nothing has been read yet, so
  constructing the view model never blocks.

**LoadAsync**: Reads the package list of every source concurrently without blocking the caller.

- *Parameters*: A cancellation token, cancelled when the dialog closes.
- *Postconditions*: Each row shows its package list, an empty-source notice, or - when that
  source cannot be read - its own error message; one unreachable source never prevents the
  others from loading, and `IsLoading` is cleared when all have finished
  (`AgentControl-SelectPackageWindowViewModel-ListPackages`,
  `AgentControl-SelectPackageWindowViewModel-AsyncLoading`,
  `AgentControl-SelectPackageWindowViewModel-SourceFailureIsolation`). A cancelled token
  surfaces as `OperationCanceledException`.

**SelectedPackageName (setter)**: Starts the background read of the versions of the chosen package
at the selected source.

- *Postconditions*: `AvailableVersions` eventually reflects the cache's descending versions for
  (`SelectedSource`, value) and `SelectedVersion` is set to the first entry; while the read runs
  `IsLoadingVersions` is true; a result that arrives after the selection moved on is discarded,
  and a failed read shows `VersionsError`
  (`AgentControl-SelectPackageWindowViewModel-VersionSelection`).

**CancelLoading**: Cancels the version read still running, for when the dialog closes.

**Retry** (private, bound to `RetryCommand`): Reads again only what failed
(`AgentControl-SelectPackageWindowViewModel-Retry`). When the selected source failed, that source alone
returns to its loading state and its package list is read again; the other sources and every successful
result are left alone. When only the selected package's versions failed, the version read starts afresh
exactly as a new selection would, superseding the failed one. With nothing failed it does nothing. A server
that was briefly unreachable is simply tried again, and one that needs a sign-in asks again - the user
started this read, so the sign-in dialog may open.

**Confirm** (private, bound to `ConfirmCommand`): Raises `Confirmed` with the current selection.

- *Parameters*: None.
- *Returns*: `void`.
- *Preconditions*: A source, a package name and a version are selected (enforced by
  `ConfirmCommand`'s `CanExecute` predicate referencing `CanConfirm`)
  (`AgentControl-SelectPackageWindowViewModel-ConfirmGating`).
- *Postconditions*: `Confirmed` is raised with a `PackageReference` of (source alias, package
  name, version) (`AgentControl-SelectPackageWindowViewModel-Confirm`).

All source access routes through the shared, session-scoped `PackageVersionCache` rather than a
provider directly, so opening this dialog a second time in the same app session does not query
a source again. Reading a NuGet server source can take several seconds and may open the sign-in
dialog (a credential typed for the server is remembered for the session, so the dialog does not appear
again for the versions of a package or a later dialog); a 401 on a package resource after a credential was
supplied makes the NuGet client retry with its own back-off, which is why each source shows progress and the
read can be cancelled.

#### Error Handling

The constructor throws `ArgumentNullException` for a null `registry` or `cache`. Failures
reading a source (`IOException`, `UnauthorizedAccessException`, `InvalidOperationException`) are
never thrown to the caller: they are recorded as an inline message on the affected source or on
the version list. A server that needs a sign-in reports `PackageSourceAccessException` (see the
`NuGetSourceProvider` design), whose secret-free message is the inline text; `Retry` offers another attempt.
`Confirm` performs a defensive null check and silently returns without raising
`Confirmed` if a selection is somehow null, even though `ConfirmCommand`'s `CanExecute` should
already prevent that call.

#### Dependencies

- **PackageVersionCache**, **PackageSourceRegistry**, **PackageReference** (`AgentPackageManagement`
  subsystem) — the source of package names and versions and the result type; the cache is shared
  with `MainWindowViewModel` and every `RepoCardViewModel`.
- **SelectPackageSourceViewModel**, **PackageSourceIcons** (`LauncherUI` subsystem) — the row of
  the source column.
- **RelayCommand** (`LauncherUI` subsystem, shared MVVM support) — backs `ConfirmCommand`.

#### Callers

- **RepoCardViewModel** — constructs this view model (via `SelectPackageRequested`) when the
  user chooses "Select Package..." for a repo with no existing pin, and subscribes to
  `Confirmed` to apply the chosen package (`ApplySelectedPackageAsync`).
- **SelectPackageWindow** (code-behind) — starts `LoadAsync` when the window opens and
  cancels it when the window closes.
