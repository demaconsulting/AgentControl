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
- Only `files-sha256:` plus 64 hexadecimal digits is recognized as a files identity; only a well-formed
  `git-commit:` text is recognized as a source revision.
- A well-formed 40- or 64-digit commit hash is formatted as a lower-case `git-commit:` text, and a malformed or
  null one is refused.
- Comparing identities reports nothing to compare when either side is missing or is not a files identity (a legacy
  container identity, a commit), `Same` for equal files identities (ignoring letter case) and `Changed` for different
  ones.
- The short description cuts a long value (also one of an unrecognized shape, after its first colon), keeps a short
  one, and reports `none` for a missing identity.

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

**PackageContentIdentity_RecognizeFilesIdentity**: A files identity is recognized (also in upper case or with
surrounding white space), and a legacy `sha256:` value, a `git-commit:` value, a short or non-hexadecimal digest, empty
and null text are not. This scenario is tested by
`PackageContentIdentity_IsFilesIdentity_VariousTexts_RecognizesOnlyFilesIdentity`, covering
`AgentControl-PackageContentIdentity-RecognizeFilesIdentity`.

**PackageContentIdentity_Format_GitCommit**: Commit hashes of both object-format widths are lower-cased and
prefixed with `git-commit:`, and only such text is recognized as a source revision. This scenario is tested by
`PackageContentIdentity_FromGitCommit_ValidHash_ReturnsLowerCasePrefixedIdentity` and
`PackageContentIdentity_IsGitCommitRevision_VariousTexts_RecognizesOnlyCommitRevision`, covering
`AgentControl-PackageContentIdentity-Format`.

**PackageContentIdentity_Format_MalformedOrNullCommit**: Hashes that are empty, too short, not hexadecimal or of
the wrong length throw `ArgumentException`, and a null hash throws `ArgumentNullException`. This scenario is tested
by `PackageContentIdentity_FromGitCommit_MalformedHash_ThrowsArgumentException` and
`PackageContentIdentity_FromGitCommit_NullCommit_ThrowsArgumentNullException`, covering
`AgentControl-PackageContentIdentity-FormatRejectMalformed`.

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
