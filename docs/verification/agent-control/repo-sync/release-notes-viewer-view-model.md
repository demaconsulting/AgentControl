### ReleaseNotesViewerViewModel

#### Verification Approach

`ReleaseNotesViewerViewModel` is verified with unit tests defined in
`ReleaseNotesViewerViewModelTests.cs`. Every test constructs the view model directly with
in-memory string arguments; no Avalonia `Window` is constructed and no external dependency is
involved.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Non-empty release notes are exposed unchanged alongside the repo name and package title.
- Empty release notes are replaced with a placeholder message rather than shown blank.
- A null repo name or release-notes value is rejected at construction.
- Heading lines (`#`/`##`/`###`) parse into blocks with the correct heading level and the
  marker stripped from the text.
- Bullet-list lines (`-`/`*`) parse into blocks flagged as bullets, with the marker stripped.
- `**bold**` and `*italic*` inline spans parse into separate runs with the correct emphasis
  flags, and an unterminated marker is treated as literal text.
- Blank lines between content lines are skipped rather than rendered as empty blocks.

#### Test Scenarios

**ReleaseNotesViewerViewModel_Display_ExposesContentOrPlaceholderAndRejectsNulls**:
Constructing with non-empty release notes exposes the repo name, package title, and content
unchanged; constructing with empty release notes substitutes a placeholder message; and
constructing with a null repo name or null release notes throws `ArgumentNullException`. This
scenario is tested by
`ReleaseNotesViewerViewModel_Constructor_NonEmptyReleaseNotes_ExposesTitleAndContent`,
`ReleaseNotesViewerViewModel_Constructor_EmptyReleaseNotes_UsesPlaceholderMessage`,
`ReleaseNotesViewerViewModel_Constructor_NullRepoName_ThrowsArgumentNullException`, and
`ReleaseNotesViewerViewModel_Constructor_NullReleaseNotes_ThrowsArgumentNullException`,
covering `AgentControl-ReleaseNotesViewerViewModel-Display`.

**ReleaseNotesViewerViewModel_Blocks_ParsesHeadingsBulletsAndEmphasis**: Parsing release notes
text into `Blocks` recognizes heading levels, bullet-list markers, bold/italic inline spans
(treating an unterminated marker as literal text), and skips blank lines. This scenario is
tested by `ReleaseNotesViewerViewModel_Blocks_HeadingLine_ParsesHeadingLevelAndText`,
`ReleaseNotesViewerViewModel_Blocks_BulletLine_ParsesAsBulletWithoutMarker`,
`ReleaseNotesViewerViewModel_Blocks_BoldAndItalicSpans_ParseIntoSeparateRuns`,
`ReleaseNotesViewerViewModel_Blocks_BlankLines_AreSkipped`, and
`ReleaseNotesViewerViewModel_Blocks_UnterminatedAsterisk_TreatedAsLiteralText`, covering
`AgentControl-ReleaseNotesViewerViewModel-MarkdownParsing`.
