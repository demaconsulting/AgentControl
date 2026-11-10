### PackageContentIdentity

![AgentPackageManagement Structure](AgentPackageManagementView.svg)

#### Purpose

`PackageContentIdentity` computes, formats and compares the *content identity* of an agent package: a short
text that names exactly which agent files a package version delivered, so that a repo's pin can tell a package
republished under the same version from the one it was made from - and can recognize the same files delivered by a
different source (`AgentControl-AgentPackageManagement-ContentIdentity`). It exists as its own unit because
three parties need the same format and the same comparison rule - the readers that produce identities, the pin
that stores one, and the repo card that decides what a difference means and which other source may stand in for
the pinned one - and none of them should own it.

The identity is computed from the *delivered files themselves* - exactly the files AgentControl writes into the
four managed folders - and not from the zip, the `.nupkg` or the Git repository they came in. Users of one repo
reach the same files through sources of different kinds and names (a zip folder, a NuGet feed, a Git tag, a
re-packed mirror), and a hash of the container would be different for each.

#### Data Model

An identity is the string `files-sha256:` followed by 64 lower-case hexadecimal digits.

**Canonical encoding.** The identity is the SHA-256 of one byte stream that is built like this:

1. The files taken into account are those that `PackageContentExtractor.GetManagedPackagePath` accepts - the one
   rule that also decides what `PackageContentExtractor.Extract` writes into a repo, so the identity can never
   cover a different set of files than the one delivered. Everything else is excluded: `release-notes.md`,
   `AGENTS.md`, the rest of `.github`, any other file of the package
   (`AgentControl-PackageContentIdentity-FilesIdentityCoversExtractedFilesOnly`). A path is taken in its
   canonical form: `.` and `..` segments resolved, `/` separators, relative to the package content root.
2. The files are sorted by *ordinal comparison* (UTF-16 code units, `string.CompareOrdinal`) of that path.
3. For each file, in that order, the stream receives: the UTF-8 bytes of the path, one `0x00` byte, the length of
   the file in bytes as an unsigned 8-byte little-endian integer, and then the raw bytes of the file.
4. A package that delivers no managed file is the empty stream (`AgentControl-PackageContentIdentity-FilesIdentity`).

Nothing else takes part - no timestamps, permissions, directory entries, entry order, compression or container
format - so the identity is the same for the same files whatever delivered them
(`AgentControl-PackageContentIdentity-FilesIdentityIndependentOfPackaging`), and it changes if one byte, a path, or
the membership changes, or if bytes move from one file to another because the length and path are part of the
stream (`AgentControl-PackageContentIdentity-FilesIdentityDetectsChange`).

A *source revision* is a different thing and is never compared as an identity: `git-commit:` followed by a 40- or
64-digit lower-case commit hash, the commit a tag of a Git repository pointed to. `GitSourceProvider` reports it
for every package it delivers, and the pin records it separately so that a moved tag can be noticed.

**ContentIdentityComparison**: `enum` - `NotComparable` (a side is missing or is not a files identity), `Same`,
`Changed`.

The class is `static` and stateless; every member is a pure function of its parameters (plus the content that is
read) and is thread-safe.

#### Key Methods

**ComputeFilesIdentity**: Computes the identity of the agent files an `IPackageContentReader` delivers.

- *Parameters*: `IPackageContentReader reader`.
- *Returns*: `string`.
- *Algorithm*: Lists the reader's files, keeps those `GetManagedPackagePath` accepts, sorts them, and streams each
  file through one `IncrementalHash` as described in the canonical encoding. Memory use is bounded by one 80 KB
  buffer however large the package is. The length of a file has to precede its bytes: a seekable stream states it,
  and any other stream (an entry of a compressed zip) is read once to count it and opened a second time to hash it.
- *Postconditions*: The result has the form above. Throws `ArgumentNullException` for a null reader, and
  `InvalidOperationException` for a package it cannot identify - one that cannot be listed or read, that lists a
  path `Extract` would also refuse (one that resolves outside the package), that lists two files with the same
  canonical path or with canonical paths that differ
  only in letter case (`PackageContentExtractor.RejectCaseCollisions`, the rule `Extract` applies too: the extracted
  bytes would depend on the entry order while the identity stayed the same;
  `AgentControl-PackageContentIdentity-FilesIdentityRejectCaseCollision`), or in which a file
  does not have the length it had when it was measured
  (`AgentControl-PackageContentIdentity-FilesIdentityRejectUnacceptable`).

**TryComputeFilesIdentity**: The same, but returns `null` instead of throwing `InvalidOperationException`. What
the readers' `ContentIdentity` property uses: a reader opened only for its release notes never pays for reading
every file, and a reader over an unacceptable package still opens - `Extract` then reports the specific problem,
and "no identity" means "nothing to compare", never "changed".

**IsFilesIdentity**: Tells whether a text is a well-formed files identity: the prefix (ignoring letter case)
followed by exactly 64 hexadecimal digits, ignoring surrounding white space. An identity an older version
recorded (`sha256:`, `git-commit:`) is not
(`AgentControl-PackageContentIdentity-RecognizeFilesIdentity`).

**IsGitCommitRevision** / **FromGitCommit**: Recognize and format a `git-commit:` source revision.
`FromGitCommit` returns `git-commit:` plus the lower-cased hash
(`AgentControl-PackageContentIdentity-Format`) and throws `ArgumentException` unless the hash is 40 or 64
hexadecimal digits and `ArgumentNullException` for a null hash, so a malformed value never reaches a pin
(`AgentControl-PackageContentIdentity-FormatRejectMalformed`).

**Compare**: Compares the identity a pin recorded with the identity of the content now delivered.

- *Parameters*: `string? pinned`, `string? actual`.
- *Returns*: `ContentIdentityComparison`.
- *Postconditions*: `NotComparable` when either is not a files identity - missing, or of an older kind computed
  from a container or a commit, which can neither confirm nor refute that the files are the same, so it is never
  a mismatch (`AgentControl-PackageContentIdentity-Compare`); otherwise `Same` when the texts are equal ignoring
  letter case, so a hand-edited pin is not a false alarm (`AgentControl-PackageContentIdentity-CompareSame`), and
  `Changed` when they differ (`AgentControl-PackageContentIdentity-CompareChanged`).

**Describe**: Produces a short form for messages and logs.

- *Parameters*: `string? identity`.
- *Returns*: `string` - the kind prefix and the first 12 characters of the value
  (`AgentControl-PackageContentIdentity-Describe`),
  or `none` when no identity is known (`AgentControl-PackageContentIdentity-DescribeNone`). The text is split at its
  first colon, so an identity of an unrecognized shape is cut the same way and never echoed in full.

#### Error Handling

`ComputeFilesIdentity` throws the `InvalidOperationException` described above for a package that cannot be
identified; I/O failures while reading a file (`IOException`, `InvalidDataException` of a corrupt zip,
`UnauthorizedAccessException`) are wrapped in it, naming the package. `FromGitCommit` throws
`ArgumentException`/`ArgumentNullException` for a malformed or null hash. `TryComputeFilesIdentity`, `IsFilesIdentity`,
`IsGitCommitRevision`, `Compare` and `Describe` never throw (for valid arguments).

#### Dependencies

- **IPackageContentReader** and **PackageContentExtractor** (`RepoSync` subsystem) - the reader supplies the files;
  `GetManagedPackagePath` is the membership rule.
- **.NET BCL** - `System.Security.Cryptography.IncrementalHash`.

#### Callers

- **ZipPackageContentReader** and **DirectoryPackageContentReader** (`RepoSync` subsystem) - `TryComputeFilesIdentity`
  over their own files, which makes every source's identity come out the same way.
- **GitSourceProvider** - `FromGitCommit` for the source revision of the tag it fetched.
- **RepoCardViewModel** (`LauncherUI` subsystem) - `Compare`, `Describe` and `IsFilesIdentity` when applying,
  upgrading to or restoring a package, and when verifying another source against a pin.
