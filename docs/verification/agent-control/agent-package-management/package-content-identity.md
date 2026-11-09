### PackageContentIdentity

#### Verification Approach

`PackageContentIdentity` is verified with unit tests defined in `PackageContentIdentityTests.cs`. Every test
operates on in-memory streams and strings; no filesystem or external dependency is involved. The hashing is
checked against a published SHA-256 test vector (the digest of `abc`), so the test does not
re-implement the code under test.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Hashing known content yields `sha256:` followed by the expected lower-case hexadecimal digest, leaves the stream
  open and positioned at its end, hashes only from the stream's current position, and a null stream is rejected.
- A well-formed 40- or 64-digit commit hash is formatted as a lower-case `git-commit:` identity, and a malformed or
  null one is refused.
- Comparing identities reports nothing to compare when either side is missing, `Same` for equal identities
  (ignoring letter case) and `Changed` for different ones, including identities of different kinds.
- The short description cuts a long value (also one of an unrecognized shape, after its first colon), keeps a short
  one, and reports `none` for a missing identity.

#### Test Scenarios

**PackageContentIdentity_Sha256_KnownVector**: The identity of the bytes of `abc` equals `sha256:` plus the published
digest in lower-case hexadecimal. This scenario is tested by
`PackageContentIdentity_ComputeSha256_KnownContent_ReturnsPrefixedLowerCaseHash`, covering
`AgentControl-PackageContentIdentity-Sha256`.

**PackageContentIdentity_Sha256_StreamStaysOpen**: After hashing, the stream is still readable and its position is
at its end; hashing starts at the stream's current position (a stream of the byte `x` followed by `abc`, positioned
after the first byte, yields the digest of `abc`). This scenario is tested by
`PackageContentIdentity_ComputeSha256_Stream_StaysOpenAtEndAndHashesFromCurrentPosition`, covering
`AgentControl-PackageContentIdentity-Sha256StreamOpen`.

**PackageContentIdentity_Sha256_NullStream**: A null stream throws `ArgumentNullException`. This scenario is tested
by `PackageContentIdentity_ComputeSha256_NullStream_ThrowsArgumentNullException`, covering
`AgentControl-PackageContentIdentity-Sha256RejectNull`.

**PackageContentIdentity_Format_GitCommit**: Commit hashes of both object-format widths are lower-cased and
prefixed with `git-commit:`. This scenario is tested by
`PackageContentIdentity_FromGitCommit_ValidHash_ReturnsLowerCasePrefixedIdentity`, covering
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

**PackageContentIdentity_Compare_Missing**: Missing, empty or white-space identities on either side give
`NotComparable`. This scenario is tested by `PackageContentIdentity_Compare_EitherIdentityMissing_IsNotComparable`,
covering `AgentControl-PackageContentIdentity-Compare`.

**PackageContentIdentity_Compare_Same**: Equal identities, also when they differ only in hash letter case, give
`Same`. This scenario is tested by `PackageContentIdentity_Compare_EqualIdentities_IsSameIgnoringCase`, covering
`AgentControl-PackageContentIdentity-CompareSame`.

**PackageContentIdentity_Compare_Changed**: Two different hashes of the same kind give `Changed`. This scenario is
tested by `PackageContentIdentity_Compare_DifferentValues_IsChanged`, covering
`AgentControl-PackageContentIdentity-CompareChanged`.

**PackageContentIdentity_Compare_DifferentKinds**: A hash against a commit identity gives `Changed`. This scenario is
tested by `PackageContentIdentity_Compare_DifferentKinds_IsChanged`, covering
`AgentControl-PackageContentIdentity-CompareDifferentKinds`.
