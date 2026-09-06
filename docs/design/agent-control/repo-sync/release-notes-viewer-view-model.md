### ReleaseNotesViewerViewModel

![RepoSync Structure](RepoSyncView.svg)

#### Purpose

`ReleaseNotesViewerViewModel` backs the release-notes window shown after a successful package
upgrade: it holds the window title and the release notes text to display. It is deliberately
trivial — `PackageZipExtractor.ReadReleaseNotes` already does the only real work (reading the
zip entry) — so this view model exists purely to give the view a bindable, testable surface
rather than reading fields directly off a plain string.

#### Data Model

**Title**: `string` — `"Release Notes - {repoName}"`, identifying which repo's upgrade
produced these release notes.

**ReleaseNotes**: `string` — the release notes text to display, rendered as plain text (a full
Markdown renderer is out of scope). If the source package had no release notes, this is the
placeholder text `"(This package has no release notes.)"`
(`AgentControl-ReleaseNotesViewerViewModel-Display`).

Both properties are immutable once constructed; the class is thread-safe.

#### Key Methods

**Constructor**: Builds the display strings from a repo name and release notes text.

- *Parameters*: `string repoName`, `string releaseNotes`.
- *Preconditions*: Neither parameter `null`.
- *Postconditions*: `Title` and `ReleaseNotes` are set as described in Data Model
  (`AgentControl-ReleaseNotesViewerViewModel-Display`).

#### Error Handling

The constructor throws `ArgumentNullException` for a null `repoName` or `releaseNotes`. An
empty (but non-null) `releaseNotes` is treated as "no release notes" rather than an error.

#### Dependencies

- **ViewModelBase** (`LauncherUI` subsystem) — shared MVVM base class.
- **PackageZipExtractor.ReadReleaseNotes** — supplies the raw release notes text passed to the
  constructor.

#### Callers

- **RepoCardViewModel** — constructs this view model (via the view layer's `ReleaseNotesReady`
  handler in `MainWindow`'s code-behind) after `ApplyPackageAndShowReleaseNotes` completes.
- **ReleaseNotesViewer** (`.axaml` code-behind, folded into this subsystem's design at the
  subsystem level) — the window whose `DataContext` this view model is bound to.
