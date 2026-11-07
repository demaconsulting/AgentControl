### FeedOrigin

![PackageCredentials Structure](PackageCredentialsView.svg)

#### Purpose

`FeedOrigin` is the identity of a package server - its scheme, host and port - and the unit to which a
credential is scoped. Scoping by origin rather than by full address lets one sign-in cover every endpoint
of a server (service index, version list, package download) while guaranteeing that a redirect, a
different port or another sub-domain never matches. It satisfies `AgentControl-FeedOrigin-NormalizedOrigin`,
`AgentControl-FeedOrigin-EquivalentSpellings`, `AgentControl-FeedOrigin-RefusesNonWebAddress`,
`AgentControl-FeedOrigin-RefusesUserInfo`, `AgentControl-FeedOrigin-RefusalNeverRepeatsAddress`,
`AgentControl-FeedOrigin-TextFormShowsOnlyOrigin`, `AgentControl-FeedOrigin-MatchesSameOriginOnly` and
`AgentControl-FeedOrigin-NeverMatchesRelativeOrMissingAddress`.

#### Data Model

**Scheme**: `string` - lower-case, `http` or `https`.

**Host**: `string` - lower-case host name or address (an IPv6 address without brackets; internationalized
names in their ASCII form).

**Port**: `int` - the effective port, the scheme's default when the address named none.

**IsSecure**: `bool` - whether the scheme is `https`.

The type is an immutable class implementing value equality over scheme, host and port (so two spellings of
one server compare equal and it is safe as a dictionary key); it never holds a path, query string, fragment
or user information, so its text form can be shown to a user and written to a log.

#### Key Methods

**TryCreate(Uri)** / **TryParse(string)**: Derive the origin of an absolute address. They fail (return
`false`) for a null, blank or relative address, for any scheme other than `http` and `https`, and for an
address that carries user information - a feed must not be addressed with a credential in the address.

**Create(Uri)**: As `TryCreate` but throws `ArgumentException`; the message never repeats the address,
which could contain a secret.

**Matches(Uri)**: `true` only when the address is an absolute web address without user information whose
scheme, host and port all equal this origin's. A null, relative or non-web address never matches.

**ToString**: `scheme://host` with the port appended only when it is not the scheme's default; an IPv6
host is bracketed.

#### Error Handling

Failure to derive an origin is a return value (`TryCreate`, `TryParse`) or an `ArgumentException` that
never contains the address (`Create`); `Matches` never throws and answers `false` for anything it cannot
interpret, so a malformed address can never be mistaken for the feed.

#### Dependencies

- **.NET BCL** - `System.Uri` for parsing and normalization.

#### Callers

- **PackageCredentialCallback** - decides whether a challenged address belongs to the feed.
- **SessionCredentialStore** - uses it as the key.
- **NuGetSourceProvider** - derives the origin of a server source and shows it in messages.
- **CredentialPromptViewModel** (`LauncherUI`) - shows its text form as the server that receives the
  credential.
