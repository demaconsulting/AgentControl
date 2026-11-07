### PackageSourceRegistry

![AgentPackageManagement Structure](AgentPackageManagementView.svg)

#### Purpose

`PackageSourceRegistry` maps a package source alias to the provider that serves that source. It
is the single place a source is looked up by alias, and the guarantee behind pin integrity: it
returns the provider of exactly the named source, and an alias that is not configured is an
error — never a reason to use another source. A package ID is not unique across sources, so
resolving by anything weaker than the alias could install another source's package under a pin
that names a different one (`AgentControl-System-SourcePinIntegrity`). It satisfies
`AgentControl-PackageSourceRegistry-ResolveAlias`, `AgentControl-PackageSourceRegistry-MissingAliasIsError`,
`AgentControl-PackageSourceRegistry-BlankAliasNeverConfigured`,
`AgentControl-PackageSourceRegistry-InvalidAliasesRejected`,
`AgentControl-PackageSourceRegistry-NullDefinitionRejected`,
`AgentControl-PackageSourceRegistry-PlainHttpServerSourceRefused`,
`AgentControl-PackageSourceRegistry-DefinitionsAreASnapshot` and
`AgentControl-PackageSourceRegistry-NoCrossSourceFallback`.

#### Data Model

**\_definitions**: `Dictionary<string, PackageSourceDefinition>` (case-insensitive alias) — the
configured sources; **\_ordered**: `IReadOnlyList<PackageSourceDefinition>` — the same definitions in
configuration order. Both hold the registry's own private clones of the definitions it was given, so
no caller holds a reference to anything the registry resolves from.

**\_providers**: `Dictionary<string, IPackageSourceProvider>` — providers created so far, built
lazily on first resolve so a misconfigured source only fails when it is used.

**\_providerFactory**: `Func<PackageSourceDefinition, IPackageSourceProvider>` — creates a
provider for a definition; `Create` supplies the built-in factory (zip folder and NuGet folder or server
providers), tests supply fakes.

**UnknownSourceException** (same file): an `InvalidOperationException` subtype carrying the
missing `Alias`, with a message that names it and says how to fix it.

An instance is a genuine snapshot of the definitions it was built from: changing a definition, or
the list it was built from, afterwards does not change which source an alias resolves to; callers
build a new one when settings change. All members are thread-safe.

#### Key Methods

**Constructor**: Builds a registry from definitions and a provider factory.

- *Postconditions*: The definitions are cloned, then indexed by alias, compared case-insensitively
  (`AgentControl-PackageSourceRegistry-DefinitionsAreASnapshot`). A blank alias, or an alias
  configured twice, is rejected with `InvalidOperationException` rather than resolved ambiguously
  (`AgentControl-PackageSourceRegistry-InvalidAliasesRejected`). A missing (`null`) element in the
  sequence is rejected with `InvalidOperationException` ("A configured package source entry is
  missing.") rather than surfacing as an incidental `NullReferenceException`
  (`AgentControl-PackageSourceRegistry-NullDefinitionRejected`).

**Create**: Builds a registry whose provider factory maps `PackageSourceType.Zip` to
`ZipFolderSourceProvider` and `PackageSourceType.NuGet` to `NuGetSourceProvider`, passing through
the base folder under which NuGet sources keep their private caches and the optional
`PackageCredentialContext` that NuGet server sources use to sign in (a context without a prompt gives work
that must not open a dialog, such as the background upgrade check).

**Resolve**: Returns the provider that serves exactly the named source
(`AgentControl-PackageSourceRegistry-ResolveAlias`).

- *Parameters*: `string alias`.
- *Returns*: `IPackageSourceProvider`.
- *Postconditions*: The provider is created once and reused. An alias that is not configured
  throws `UnknownSourceException` naming the alias
  (`AgentControl-PackageSourceRegistry-MissingAliasIsError`); there is no fallback to a default, a
  first, or the only source, and no other provider is created or contacted
  (`AgentControl-PackageSourceRegistry-NoCrossSourceFallback`). A blank alias is rejected with
  `ArgumentException` (`AgentControl-PackageSourceRegistry-BlankAliasNeverConfigured`). A provider
  factory failure (for example a server address that is plain `http` while the source does not allow
  insecure HTTP) propagates as the factory's `InvalidOperationException`
  (`AgentControl-PackageSourceRegistry-PlainHttpServerSourceRefused`).

**Contains / Definitions**: Report whether an alias is configured (case-insensitively; a blank
alias never is) and list the configured definitions in order. `Definitions` returns a fresh
immutable list of clones on every access, so a caller that changes what it gets back — or the list
itself — cannot affect the registry (`AgentControl-PackageSourceRegistry-DefinitionsAreASnapshot`).

#### Error Handling

`UnknownSourceException` for an unconfigured alias; `ArgumentException` for a blank alias passed
to `Resolve`; `InvalidOperationException` for invalid configuration at construction (a blank or
duplicate alias, or a missing entry). Callers turn these into user-visible messages
(`RepoCardViewModel`'s source problem and error events).

#### Dependencies

- **IPackageSourceProvider** (subsystem contract) — what it hands out.
- **PackageSourceDefinition** / **PackageSourceType** (subsystem data types) — the configuration
  it is built from.
- **PackageCredentialContext** (`PackageCredentials` subsystem) — handed to NuGet server providers by
  `Create`.
- **ZipFolderSourceProvider**, **NuGetSourceProvider** — constructed by `Create`.

#### Callers

- **PackageVersionCache** — resolves an alias to a provider on a cache miss.
- **RepoCardViewModel** — builds a registry from the current settings for every source
  operation, and resolves the pin's alias.
- **SelectPackageWindowViewModel** — receives a registry for the dialog's source.
