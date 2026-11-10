### GitRepositoryAddress

![AgentPackageManagement Structure](AgentPackageManagementView.svg)

#### Purpose

`GitRepositoryAddress` validates the address of the Git repository a package source reads and derives from it the
only transport git may use and the key of the repository's cache folder
(`AgentControl-AgentPackageManagement-GitSource`). It is its own unit because three parties need the same rules - the
source definition (settings load and save), the editor that will check what a user types, and the provider that hands
the address to git - and because the rules are a security boundary: an address is untrusted text (a settings file can be
edited by hand) that ends up on a git command line.

#### Data Model

**GitAddressKind**: `enum` - `Https`, `Ssh`, `Local`.

**GitRepositoryAddress**: immutable, created only by `TryParse` - `Kind`, `Value` (the trimmed text given to git),
`CacheKey` and `GitAllowProtocolName` (`https`, `ssh` or `file`).

Accepted forms and their rules:

- `https://host[:port]/path` - host and path present; **no user information at all**; no query or fragment; no spaces
  or backslashes.
- `ssh://[user@]host[:port]/path` - a user name only (a `:` in it implies a password and is refused); host not starting
  with `-`; IPv6 hosts in brackets.
- `user@host:path` - user and host from `[A-Za-z0-9._-]` (not starting with `-`), path not empty, not starting with `//`
  or `-`, no spaces; the user is required because `host:path` cannot be told from a Windows drive.
- An absolute local or UNC path - `Path.IsPathFullyQualified`; existence is not checked.

Everything else is refused: other schemes (`http`, `git`, `file`, `ftp`), remote-helper syntax (`::` outside a URL:
`ext::`, `fd::`), relative paths, a leading `-`, control characters, and addresses over 2048 characters. The cache key
is
the first 16 hexadecimal digits of the SHA-256 of `kind|normalized`, where the normalized form lower-cases the scheme
and
host (and, on Windows, a local path) and leaves the rest as typed so that case-sensitive servers never collide.

#### Key Methods

**TryParse**: Validates an address.

- *Parameters*: `string? text`, out `GitRepositoryAddress? address`, out `string? problem`.
- *Returns*: `bool` - `true` for an accepted address. Never throws. The problem text never repeats `text`
  (`AgentControl-GitRepositoryAddress-NeverEchoesInput`; the fixed wording avoids example addresses that could contain
  a short input by coincidence); the accept/reject rules are `AgentControl-GitRepositoryAddress-AcceptHttps`,
  `-AcceptSsh`, `-AcceptScpLikeSsh`, `-AcceptLocalPath`, `-RejectEmbeddedCredentials`, `-RejectDangerousTransports`,
  `-RejectOptionLikeValues`, `-RejectControlCharacters`, `-RejectMalformedParts` and `-RejectTooLong`.

**GetProblem**: The problem of an address text, or `null`.

**EmbedsCredential**: Reports from the text alone whether it looks like a URL that carries a credential: user
information on a non-ssh URL, a password in an ssh URL, or a query or fragment
(`AgentControl-GitRepositoryAddress-RecognizeEmbeddedCredentials`). `PackageSourceDefinition.GetCredentialProblem` uses
it
for Git sources, because a URI-based check cannot parse `user@host:path` and would refuse `ssh://git@host/...`.

**CacheKey** (property): 16 lower-case hexadecimal digits (`AgentControl-GitRepositoryAddress-CacheKeyFormat`), the same
for addresses that differ only in scheme or host case (`AgentControl-GitRepositoryAddress-CacheKeyStable`) and different
for different repositories (`AgentControl-GitRepositoryAddress-CacheKeyDistinguishesRepositories`).

#### Error Handling

Invalid input is reported through the `problem` out value and `false`; nothing throws. `EmbedsCredential` and
`GetProblem` accept `null`.

#### Dependencies

- **.NET BCL** - `Regex` (generated, anchored with `\z` so a trailing line feed cannot slip through), `SHA256`, `Path`.

#### Callers

- **PackageSourceDefinition** - `GetGitSettingsProblem`, `GetCredentialProblem`.
- **GitSourceProvider** - parses the address once at construction.
- **SourceEditorViewModel** (`LauncherUI`) - `GetProblem` validates the address the user types (after the credential
  check of `PackageSourceDefinition`) before a Git source can be tested or saved; the editor uses the address rules
  directly, not the provider's parse.
- **PackageSourceItemViewModel** (`LauncherUI`) - `TryParse` tells a local path from a remote address, so a remote
  source is listed as an online source and a local one with the status of its folder.
