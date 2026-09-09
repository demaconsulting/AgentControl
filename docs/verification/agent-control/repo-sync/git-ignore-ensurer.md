### GitIgnoreEnsurer

#### Verification Approach

`GitIgnoreEnsurer` is verified with unit tests defined in `GitIgnoreEnsurerTests.cs`. Every
test creates a real temporary repo directory and, where relevant, a real temporary
`.gitignore` file, so the marker-comment scan, file creation, additive appending, and I/O
failure handling are all exercised against genuine filesystem state — no mocking.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- A `.gitignore` that already contains the marker comment is left byte-for-byte unchanged.
- A missing `.gitignore` is created with the marker comment and the four managed-folder lines.
- Existing `.gitignore` content is never edited, reordered, or removed — only appended to.
- A blank-line separator is added only when the existing content needs one, never a redundant
  extra blank line.
- A write failure surfaces as `InvalidOperationException` naming the `.gitignore` path.

#### Test Scenarios

**GitIgnoreEnsurer_Ensure_MarkerScanIdempotencyAndCreation**: When the marker comment is
already present, `Ensure` performs a true no-op leaving the file unchanged; when no
`.gitignore` exists at all, `Ensure` creates one containing the marker and the four
managed-folder lines. This scenario is tested by
`GitIgnoreEnsurer_Ensure_MarkerAlreadyPresent_DoesNotModifyFile` and
`GitIgnoreEnsurer_Ensure_NoGitIgnoreFile_CreatesFileWithManagedFoldersBlock`, covering
`AgentControl-GitIgnoreEnsurer-Ensure`.

**GitIgnoreEnsurer_Ensure_AdditiveAppendWithSensibleSeparator**: Appending the block to
existing content preserves every original line unchanged and unreordered, adding a blank-line
separator only when the existing content does not already end in one. This scenario is tested
by `GitIgnoreEnsurer_Ensure_ExistingContentWithoutMarker_AppendsBlockPreservingExistingLines`
and `GitIgnoreEnsurer_Ensure_ExistingContentEndsWithBlankLine_AppendsBlockWithoutExtraBlankLine`,
covering `AgentControl-GitIgnoreEnsurer-Ensure`.

**GitIgnoreEnsurer_Ensure_PathIsDirectory_ThrowsInvalidOperationException**: A `.gitignore`
target path that is actually a directory forces a real I/O failure, wrapped in
`InvalidOperationException` naming the path, with no partial content left behind. This
scenario is tested by `GitIgnoreEnsurer_Ensure_PathIsDirectory_ThrowsInvalidOperationException`,
covering `AgentControl-GitIgnoreEnsurer-Ensure`.
