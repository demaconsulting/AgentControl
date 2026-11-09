### PackageContentIdentity

![AgentPackageManagement Structure](AgentPackageManagementView.svg)

#### Purpose

`PackageContentIdentity` computes, formats and compares the *content identity* of an agent package: a short
text that names exactly which bytes a package version delivered, so that a repo's pin can tell a package
republished under the same version from the one it was made from
(`AgentControl-AgentPackageManagement-ContentIdentity`). It exists as its own unit because three parties need
the same format and the same comparison rule - the readers and source providers that produce identities, the
pin that stores one, and the repo card that decides what a difference means - and none of them should own it.

#### Data Model

An identity is a string `{kind}:{value}`:

- `sha256:` followed by 64 lower-case hexadecimal digits - the SHA-256 of a package file (a zip, or the `.nupkg`
  a NuGet source unpacked).
- `git-commit:` followed by a 40- or 64-digit lower-case commit hash - the commit a tag of a Git repository pointed
  to; `GitSourceProvider` produces it for every package it delivers (`FromGitCommit`).

**ContentIdentityComparison**: `enum` - `NotComparable` (a side is missing), `Same`, `Changed`.

The class is `static` and stateless; every member is a pure function of its parameters (plus the stream that is
read) and is thread-safe.

#### Key Methods

**ComputeSha256**: Computes the `sha256:` identity of the bytes a stream delivers from its current position to
its end.

- *Parameters*: `Stream content`.
- *Returns*: `string`.
- *Postconditions*: The stream is left open, positioned at its end, so a caller can hash and then read the very
  same handle (the zip reader does, which closes the window in which the file could be swapped between hashing and
  reading). Throws `ArgumentNullException` for a null stream
  (`AgentControl-PackageContentIdentity-Sha256`).

**FromGitCommit**: Formats a commit hash as a `git-commit:` identity.

- *Parameters*: `string commit`.
- *Returns*: `string`, with the hash lower-cased.
- *Postconditions*: Throws `ArgumentException` unless the hash is 40 or 64 hexadecimal digits, so a malformed
  value never reaches a pin (`AgentControl-PackageContentIdentity-Format`).

**Compare**: Compares the identity a pin recorded with the identity of the content now delivered.

- *Parameters*: `string? pinned`, `string? actual`.
- *Returns*: `ContentIdentityComparison`.
- *Postconditions*: `NotComparable` when either is null, empty or white space - a pin made before identities were
  recorded, or a source that reports none, is never a mismatch; otherwise `Same` when the texts are equal ignoring
  letter case (so a hand-edited pin is not a false alarm) and `Changed` when they differ, including when they are
  of different kinds (`AgentControl-PackageContentIdentity-Compare`).

**Describe**: Produces a short form for messages and logs.

- *Parameters*: `string? identity`.
- *Returns*: `string` - the kind prefix and the first 12 characters of the value, or `none` when no identity is
  known (`AgentControl-PackageContentIdentity-Format`). An identity of an unrecognized shape is cut the same way,
  never echoed in full.

#### Error Handling

`ComputeSha256` propagates the `IOException` of an unreadable stream to its caller, which wraps it in the
exception that names the package. `FromGitCommit` throws `ArgumentException`/`ArgumentNullException` for a
malformed or null hash. `Compare` and `Describe` never throw.

#### Dependencies

- **.NET BCL** - `System.Security.Cryptography.SHA256`.

#### Callers

- **ZipPackageContentReader** (`RepoSync` subsystem) - `ComputeSha256` over the zip it reads.
- **NuGetSourceProvider** - `ComputeSha256` over the `.nupkg` it unpacked.
- **GitSourceProvider** - `FromGitCommit` for the commit of the tag it fetched.
- **RepoCardViewModel** (`LauncherUI` subsystem) - `Compare` and `Describe` when applying, upgrading to or
  restoring a package.
