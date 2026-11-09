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
- Heading lines (`#` through `######`) parse into blocks with the correct heading level and
  the marker stripped from the text; a line with more than six leading `#` stays plain text.
- Bullet-list lines (`-`/`*`) parse into blocks flagged as bullets, with the marker stripped.
- `**bold**` and `*italic*` inline spans parse into separate runs with the correct emphasis
  flags, and an unterminated marker is treated as literal text.
- Blank lines between content lines are skipped rather than rendered as empty blocks.
- Only absolute http/https addresses become link runs (in paragraphs, bullets and bold text); every other scheme and
  relative target is plain text with no address, and malformed link syntax stays literal text.

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
`ReleaseNotesViewerViewModel_Blocks_HeadingDepth_RecognizesOnlyLevelsOneToSix`,
`ReleaseNotesViewerViewModel_Blocks_BulletLine_ParsesAsBulletWithoutMarker`,
`ReleaseNotesViewerViewModel_Blocks_BoldAndItalicSpans_ParseIntoSeparateRuns`,
`ReleaseNotesViewerViewModel_Blocks_BlankLines_AreSkipped`, and
`ReleaseNotesViewerViewModel_Blocks_UnterminatedAsterisk_TreatedAsLiteralText`, covering
`AgentControl-ReleaseNotesViewerViewModel-MarkdownParsing`.

**ReleaseNotesViewerViewModel_Links_OnlyHttpAndHttpsBecomeLinks**: `[text](url)` links with an https or http
address (including one with balanced parentheses, a query, and a quoted title) parse into link runs carrying the
text and the exact address - in paragraphs, bullet items and bold text; `<https://...>` and bare addresses
become links without trailing punctuation; a `[text](url)` with a `file:`, `javascript:`, `mailto:`, `ftp:`,
protocol-relative, relative or host-less target parses into plain text `text (url)` with no address anywhere; malformed
bracket syntax keeps its text unchanged; and the address gate itself accepts only absolute http/https URLs without
whitespace or control characters. This scenario is tested by
`ReleaseNotesViewerViewModel_Blocks_InlineLink_ParsesIntoLinkRun`,
`ReleaseNotesViewerViewModel_Blocks_LinkInBulletBoldAndWithTitle_AreLinks`,
`ReleaseNotesViewerViewModel_Blocks_LinkWithOtherSchemeOrRelativeTarget_IsPlainText`,
`ReleaseNotesViewerViewModel_Blocks_MalformedLink_StaysLiteralText`,
`ReleaseNotesViewerViewModel_Blocks_BareAndAngleAddresses_BecomeLinksWithoutTrailingPunctuation` and
`ReleaseNotesViewerViewModel_IsOpenableUrl_AllowsOnlyAbsoluteHttpAndHttps`, covering
`AgentControl-ReleaseNotesViewerViewModel-Links`.
