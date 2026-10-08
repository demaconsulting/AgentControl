### ReleaseNotesViewerViewModel

![RepoSync Structure](RepoSyncView.svg)

#### Purpose

`ReleaseNotesViewerViewModel` backs the release-notes window shown after a successful package
upgrade: it holds the window title, the raw release notes text, and that text parsed into
renderable blocks. `PackageZipExtractor.ReadReleaseNotes` already does the only I/O (reading the
zip entry); this view model's own job is the lightweight line-by-line Markdown parsing that
turns that raw text into headings, bullet items, and bold/italic inline runs for
`ReleaseNotesViewer` to render — not a full CommonMark implementation, just the handful of
constructs release notes actually use (`#`/`##`/`###` headings, `-`/`*` bullets, and
`**bold**`/`*italic*` inline emphasis).

#### Data Model

**Title**: `string` — `"Release Notes - {repoName}"`, identifying which repo's upgrade
produced these release notes.

**ReleaseNotes**: `string` — the raw release notes text (kept for callers/tests that only need
the original content). If the source package had no release notes, this is the placeholder text
`"(This package has no release notes.)"` (`AgentControl-ReleaseNotesViewerViewModel-Display`).

**Blocks**: `IReadOnlyList<MarkdownBlock>` — `ReleaseNotes` parsed into one block per non-blank
source line (`AgentControl-ReleaseNotesViewerViewModel-Display`). Each `MarkdownBlock` carries:

- `Runs`: `IReadOnlyList<MarkdownRun>` — the line's inline text spans, in display order. Each
  `MarkdownRun` carries `Text`, and `Bold`/`Italic` flags parsed from `**double**`/`*single*`
  asterisk markers (an unterminated marker is treated as literal text rather than emphasis).
- `HeadingLevel`: `int` — the Markdown heading level (1 for `#`, 2 for `##`, etc.), or 0 for a
  non-heading line.
- `IsBullet`: `bool` — whether the line was a `-`/`*` bullet-list item.

All properties are immutable once constructed; the class is thread-safe.

#### Key Methods

**Constructor**: Builds the display strings and parses `Blocks` from a repo name and release
notes text.

- *Parameters*: `string repoName`, `string releaseNotes`.
- *Preconditions*: Neither parameter `null`.
- *Postconditions*: `Title`, `ReleaseNotes`, and `Blocks` are set as described in Data Model
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
