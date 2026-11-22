### PackageContentIdentity

#### Verification Approach

`PackageContentIdentity` is verified with unit tests defined in `PackageContentIdentityTests.cs`. The files identity is
computed from an in-memory `IPackageContentReader` defined in the test file, so no file system or external dependency
is involved, and the same reader can deliver the same files in another order, as non-seekable streams, or with
another file added, which is how the "independent of how the files were packaged" and "detects a change" rules are
checked. The encoding is checked against a digest the test computes by building the canonical byte sequence itself
(sorted normalized paths, a zero byte, the length as eight little-endian bytes, the bytes) with `SHA256.HashData`, and
against one pinned expected digest of a fixed set of files, so the test does not merely re-run the code under test.
That the *same* files give the *same* identity when delivered as a zip, a NuGet package folder and a Git layout is
verified by the subsystem tests of `AgentPackageManagement` (see that document), because it needs the real readers.
The identity of the files installed in a repo (`ComputeInstalledFilesIdentity`) is verified in the same test file with
real temporary repos: a package is extracted with `PackageContentExtractor.Extract` (from a real zip, a real unpacked
package folder and a real layout folder), the recorded identity is that of the package's own reader, and the tree is
then left untouched or damaged; junction tests (Windows) and symbolic-link tests (Linux, skipped where links cannot be
created) cover the link cases.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- The files identity of known files equals `files-sha256:` plus the digest of the canonical encoding and equals a
  pinned expected value; a package with no files in a managed folder has the identity of the empty byte sequence.
- The identity is the same whatever the order the reader lists the files in, whether the streams are seekable, and how
  a path is spelled (separators, leading `./`), and only files that are extracted into a managed folder take part -
  `release-notes.md`, `AGENTS.md` and other files do not.
- One changed byte, one renamed or added or removed file, and the same bytes moved between two files all give a
  different identity.
- Two files with the same canonical path, a path that escapes the package root, a file whose length changes while it
  is read and a null reader are refused; the `Try` form reports no identity instead of throwing.
- Only `files-sha256:` plus 64 hexadecimal digits is recognized as a files identity.
- Comparing identities reports nothing to compare when either side is missing or is not a files identity (a legacy
  container identity, a commit), `Same` for equal files identities (ignoring letter case) and `Changed` for different
  ones.
- Two files that differ only in letter case are refused (a message naming both, on every platform) with the repo's
  managed folders untouched, and the content identity reports none for such a package.
- The short description cuts a long value (also one of an unrecognized shape, after its first colon), keeps a short
  one, and reports `none` for a missing identity.
- The identity of the files installed in a repo equals the recorded identity of the package after an untouched
  extraction of a zip, an unpacked package folder or a layout folder, whatever the line endings of the files, and
  ignores empty folders, the repo's root files and anything outside the four managed folders; a missing, changed,
  renamed or extra file gives a different identity; a linked managed folder, a missing repo, a null repo root and a
  canceled token are refused rather than identified; and a repo root reached through a link is accepted.
- Hashing a package's files or a repo's installed agent files on a thread guarded against blocking calls is refused
  and recorded, never reported as content that cannot be identified.

#### Test Scenarios

**PackageContentIdentity_FilesIdentity_CanonicalEncoding**: Known files produce `files-sha256:` plus the digest of the
canonical encoding, which the test builds independently and which also equals a pinned value; a reader without any
managed file produces the identity of an empty sequence. This scenario is tested by
`PackageContentIdentity_ComputeFilesIdentity_KnownFiles_MatchesCanonicalEncoding` and
`PackageContentIdentity_ComputeFilesIdentity_NoManagedFiles_IsIdentityOfEmptyStream`, covering
`AgentControl-PackageContentIdentity-FilesIdentity`.

**PackageContentIdentity_FilesIdentity_IndependentOfPackaging**: The same files listed in another order, delivered as
non-seekable streams or with an equivalent spelling of a path give the same identity. This scenario is tested by
`PackageContentIdentity_ComputeFilesIdentity_EntryOrderDiffers_IsEqual`,
`PackageContentIdentity_ComputeFilesIdentity_NonSeekableStreams_IsEqualToSeekable` and
`PackageContentIdentity_ComputeFilesIdentity_EquivalentSpellingOfPath_IsEqual`, covering
`AgentControl-PackageContentIdentity-FilesIdentityIndependentOfPackaging`.

**PackageContentIdentity_FilesIdentity_ExtractedFilesOnly**: Adding `release-notes.md`, a root `AGENTS.md` or another
file outside the managed folders does not change the identity. This scenario is tested by
`PackageContentIdentity_ComputeFilesIdentity_NonExtractedFiles_AreExcluded`, covering
`AgentControl-PackageContentIdentity-FilesIdentityCoversExtractedFilesOnly`.

**PackageContentIdentity_FilesIdentity_DetectsChange**: One changed byte, a changed, added or removed path, and the same
bytes moved between two files each give a different identity. This scenario is tested by
`PackageContentIdentity_ComputeFilesIdentity_OneByteChanged_Differs`,
`PackageContentIdentity_ComputeFilesIdentity_PathOrMembershipChanged_Differs` and
`PackageContentIdentity_ComputeFilesIdentity_BytesMovedBetweenFiles_Differs`, covering
`AgentControl-PackageContentIdentity-FilesIdentityDetectsChange`.

**PackageContentIdentity_FilesIdentity_Refusals**: Duplicate canonical paths, a path escaping the package root and a
file that changes length while it is read throw `InvalidOperationException` (the `Try` form returns null), and a null
reader throws `ArgumentNullException`. This scenario is tested by
`PackageContentIdentity_ComputeFilesIdentity_DuplicateCanonicalPath_ThrowsInvalidOperationException`,
`PackageContentIdentity_ComputeFilesIdentity_PathEscapesPackageRoot_ThrowsAndTryReportsNone`,
`PackageContentIdentity_ComputeFilesIdentity_FileChangesWhileRead_ThrowsInvalidOperationException` and
`PackageContentIdentity_ComputeFilesIdentity_NullReader_ThrowsArgumentNullException`, covering
`AgentControl-PackageContentIdentity-FilesIdentityRejectUnacceptable`.

**PackageContentIdentity_FilesIdentity_RejectsCaseCollisions**: Two managed files whose paths differ only in letter
case (`.github/agents/a.md` and `.github/agents/A.md`) are refused with a message that names both and says that the
names differ only in letter case, and the `Try` form reports no identity; the same file names in different managed
folders are unaffected. This scenario is tested by
`PackageContentIdentity_ComputeFilesIdentity_PathsDifferingOnlyInCase_ThrowsAndTryReportsNone`, covering
`AgentControl-PackageContentIdentity-FilesIdentityRejectCaseCollision`.

**PackageContentIdentity_RecognizeFilesIdentity**: A files identity is recognized (also in upper case or with
surrounding white space), and a legacy `sha256:` value, a `git-commit:` value, a short or non-hexadecimal digest, empty
and null text are not. This scenario is tested by
`PackageContentIdentity_IsFilesIdentity_VariousTexts_RecognizesOnlyFilesIdentity`, covering
`AgentControl-PackageContentIdentity-RecognizeFilesIdentity`.

**PackageContentIdentity_Describe_ShortForms**: A long value is cut to twelve characters after its prefix, a short
value is kept, and an identity of an unrecognized shape is cut after its first colon rather than echoed. This
scenario is tested by `PackageContentIdentity_Describe_LongIdentity_ReturnsShortForm` and
`PackageContentIdentity_Describe_UnrecognizedShape_CutsAfterFirstColon`, covering
`AgentControl-PackageContentIdentity-Describe`.

**PackageContentIdentity_Describe_Missing**: A null or white-space identity is described as `none`. This scenario is
tested by `PackageContentIdentity_Describe_MissingIdentity_ReturnsNone`, covering
`AgentControl-PackageContentIdentity-DescribeNone`.

**PackageContentIdentity_Compare_Missing**: Missing, empty or white-space identities, and identities that are not files
identities (a legacy `sha256:` container identity, a commit), on either side give `NotComparable`. This scenario is
tested by `PackageContentIdentity_Compare_EitherIdentityMissingOrLegacy_IsNotComparable`, covering
`AgentControl-PackageContentIdentity-Compare`.

**PackageContentIdentity_Compare_Same**: Equal identities, also when they differ only in hash letter case, give
`Same`. This scenario is tested by `PackageContentIdentity_Compare_EqualIdentities_IsSameIgnoringCase`, covering
`AgentControl-PackageContentIdentity-CompareSame`.

**PackageContentIdentity_Compare_Changed**: Two different files identities give `Changed`. This scenario is
tested by `PackageContentIdentity_Compare_DifferentValues_IsChanged`, covering
`AgentControl-PackageContentIdentity-CompareChanged`.

**PackageContentIdentity_InstalledFilesIdentity_UntouchedExtractionEqualsThePackage**: Arrange: the same files as a zip,
an unpacked package folder and a layout-root folder, each extracted with `PackageContentExtractor.Extract` into a real
temporary repo whose recorded identity is the package reader's. Act: compute
`ComputeInstalledFilesIdentity(repo)`. Assert: it equals the recorded identity for all three kinds; an extra empty
folder does not change it; a repo with no managed folders and one with empty managed folders both give the identity of
the empty package; a repo root `AGENTS.md` and `release-notes.md` and files outside the four folders are ignored,
while a package that ships those root files still equals; a package whose files have CRLF line endings equals its
extraction, and converting one file to LF on disk makes the identity differ (no line-ending normalization); and a repo
root reached through a junction (`windows@`) or a symbolic link (`ubuntu@`) is accepted. This scenario is tested by
`PackageContentIdentity_ComputeInstalledFilesIdentity_UntouchedZipExtraction_EqualsRecordedIdentity`,
`PackageContentIdentity_ComputeInstalledFilesIdentity_UntouchedFolderPackageExtraction_EqualsRecordedIdentity`,
`PackageContentIdentity_ComputeInstalledFilesIdentity_UntouchedLayoutRootExtraction_EqualsRecordedIdentity`,
`PackageContentIdentity_ComputeInstalledFilesIdentity_ExtraEmptyFolder_EqualsRecordedIdentity`,
`PackageContentIdentity_ComputeInstalledFilesIdentity_NoManagedFolders_EqualsIdentityOfEmptyPackage`,
`PackageContentIdentity_ComputeInstalledFilesIdentity_EmptyManagedFolders_EqualsIdentityOfEmptyPackage`,
`PackageContentIdentity_ComputeInstalledFilesIdentity_RepoRootReleaseNotesAndAgentsMd_AreIgnored`,
`PackageContentIdentity_ComputeInstalledFilesIdentity_CrLfContent_IsNotNormalized`,
`windows@PackageContentIdentity_ComputeInstalledFilesIdentity_RepoRootUnderJunction_Succeeds` and
`ubuntu@PackageContentIdentity_ComputeInstalledFilesIdentity_RepoRootUnderSymlink_Succeeds`, covering
`AgentControl-PackageContentIdentity-InstalledFilesIdentity`.

**PackageContentIdentity_InstalledFilesIdentity_DetectsDifferences**: Arrange: an untouched extraction of a package into
a repo, with its recorded identity. Act: delete one file; change one byte of a file; add a file; rename a file; compute
`ComputeInstalledFilesIdentity` each time. Assert: each result differs from the recorded identity. This scenario is
tested by
`PackageContentIdentity_ComputeInstalledFilesIdentity_MissingFile_DiffersFromRecordedIdentity`,
`PackageContentIdentity_ComputeInstalledFilesIdentity_ChangedFileByOneByte_DiffersFromRecordedIdentity`,
`PackageContentIdentity_ComputeInstalledFilesIdentity_ExtraFile_DiffersFromRecordedIdentity` and
`PackageContentIdentity_ComputeInstalledFilesIdentity_RenamedFile_DiffersFromRecordedIdentity`, covering
`AgentControl-PackageContentIdentity-InstalledIdentityDetectsDifferences`.

**PackageContentIdentity_InstalledFilesIdentity_FailsSafely**: Arrange: a repo with a managed folder that is a junction
(`windows@`) or a symbolic link (`ubuntu@`); a canceled token; a null repo root; a repo folder that does not exist.
Act: compute `ComputeInstalledFilesIdentity`. Assert: the linked folder throws `InvalidOperationException` (no
identity is reported for files that cannot be read), a canceled token throws `OperationCanceledException`, a null root
throws `ArgumentNullException` and a missing repo throws `InvalidOperationException`. This scenario is tested by
`windows@PackageContentIdentity_ComputeInstalledFilesIdentity_ManagedFolderIsJunction_ThrowsInvalidOperationException`,
`ubuntu@PackageContentIdentity_ComputeInstalledFilesIdentity_ManagedFolderIsSymlink_ThrowsInvalidOperationException`,
`PackageContentIdentity_ComputeInstalledFilesIdentity_CanceledToken_ThrowsOperationCanceledException`,
`PackageContentIdentity_ComputeInstalledFilesIdentity_NullRepoRoot_ThrowsArgumentNullException` and
`PackageContentIdentity_ComputeInstalledFilesIdentity_MissingRepoRoot_ThrowsInvalidOperationException`, covering
`AgentControl-PackageContentIdentity-InstalledIdentityFailsSafely`.

**PackageContentIdentity_Hash_OnGuardedThread_IsRefusedAndRecorded**: Inside a guard scope of `BlockingCallGuard`,
`TryComputeFilesIdentity` and `ComputeInstalledFilesIdentity` both throw `BlockingCallOnGuardedThreadException` (the
former does not turn it into "no identity"), the record holds `package content hash` and `installed files hash`, and
the same package is identified outside the scope. This scenario is tested by
`PackageContentIdentity_Hash_OnGuardedThread_IsRefusedAndRecorded`, covering
`AgentControl-PackageContentIdentity-RefusesGuardedThread`.
