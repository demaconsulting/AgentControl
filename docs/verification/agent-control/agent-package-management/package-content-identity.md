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
  open, and a null stream is rejected.
- A well-formed 40- or 64-digit commit hash is formatted as a lower-case `git-commit:` identity and a malformed
  one is refused.
- Comparing identities reports nothing to compare when either side is missing, `Same` for equal identities
  (ignoring letter case) and `Changed` for different ones, including identities of different kinds.
- The short description cuts a long value, keeps a short one, and reports `none` for a missing identity.

#### Test Scenarios

**PackageContentIdentity_Sha256_KnownVectorAndNullStream**: The identity of the bytes of `abc` equals
`sha256:` plus the published digest, the stream remains readable, and a null stream throws
`ArgumentNullException`. This scenario is tested by
`PackageContentIdentity_ComputeSha256_KnownContent_ReturnsPrefixedLowerCaseHash` and
`PackageContentIdentity_ComputeSha256_NullStream_ThrowsArgumentNullException`, covering
`AgentControl-PackageContentIdentity-Sha256`.

**PackageContentIdentity_Format_GitCommitAndShortDescription**: Commit hashes of both object-format widths are
lower-cased and prefixed, malformed hashes throw `ArgumentException`, and the short description cuts, keeps or
reports `none` as specified. This scenario is tested by
`PackageContentIdentity_FromGitCommit_ValidHash_ReturnsLowerCasePrefixedIdentity`,
`PackageContentIdentity_FromGitCommit_MalformedHash_ThrowsArgumentException` and
`PackageContentIdentity_Describe_LongAndMissingIdentities_ReturnsShortForm`, covering
`AgentControl-PackageContentIdentity-Format`.

**PackageContentIdentity_Compare_AllOutcomes**: Missing, empty or white-space identities on either side give
`NotComparable`; equal identities (also differing only in hash letter case) give `Same`; different hashes and
identities of different kinds give `Changed`. This scenario is tested by
`PackageContentIdentity_Compare_PinnedAndActual_ReturnsExpectedOutcome`, covering
`AgentControl-PackageContentIdentity-Compare`.
