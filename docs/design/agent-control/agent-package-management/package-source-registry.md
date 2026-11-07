### PackageSourceRegistry

![AgentPackageManagement Structure](AgentPackageManagementView.svg)

#### Purpose

`PackageSourceRegistry` maps a package source alias to the provider that serves that source. It
is the single place a source is looked up by alias, and the guarantee behind pin integrity: it
returns the provider of exactly the named source, and an alias that is not configured is an
error — never a reason to use another source. A package ID is not unique across sources, so
resolving by anything weaker than the alias could install another source's package under a pin
that names a different one (`AgentControl-System-SourcePinIntegrity`).

#### Data Model

**\_definitions**: `Dictionary<string, PackageSourceDefinition>` (case-insensitive alias) — the
configured sources; **\_ordered**: `List<PackageSourceDefinition>` — the same definitions in
configuration order, exposed as `Definitions`.

**\_providers**: `Dictionary<string, IPackageSourceProvider>` — providers created so far, built
lazily on first resolve so a misconfigured source only fails when it is used.

**\_providerFactory**: `Func<PackageSourceDefinition, IPackageSourceProvider>` — creates a
provider for a definition; `Create` supplies the built-in factory (zip folder and NuGet folder
providers), tests supply fakes.

**UnknownSourceException** (same file): an `InvalidOperationException` subtype carrying the
missing `Alias`, with a message that names it and says how to fix it.

An instance is a snapshot of the definitions it was built from; callers build a new one when
settings change. All members are thread-safe.

#### Key Methods

**Constructor**: Builds a registry from definitions and a provider factory.

- *Postconditions*: Definitions are indexed by alias, compared case-insensitively. A blank alias,
  or an alias configured twice, is rejected with `InvalidOperationException` rather than
  resolved ambiguously (`AgentControl-PackageSourceRegistry-MissingAliasIsError`).

**Create**: Builds a registry whose provider factory maps `PackageSourceType.Zip` to
`ZipFolderSourceProvider` and `PackageSourceType.NuGet` to `NuGetSourceProvider`, passing through
the base folder under which NuGet sources keep their private caches.

**Resolve**: Returns the provider that serves exactly the named source
(`AgentControl-PackageSourceRegistry-ResolveAlias`).

- *Parameters*: `string alias`.
- *Returns*: `IPackageSourceProvider`.
- *Postconditions*: The provider is created once and reused. An alias that is not configured
  throws `UnknownSourceException`; there is no fallback to a default, a first, or the only
  source, and no other provider is created or contacted
  (`AgentControl-PackageSourceRegistry-NoCrossSourceFallback`). A provider factory failure (for
  example an unsupported NuGet server location) propagates as the factory's
  `InvalidOperationException`.

**Contains / Definitions**: Report whether an alias is configured (case-insensitively; a blank
alias never is) and list the configured definitions in order.

#### Error Handling

`UnknownSourceException` for an unconfigured alias; `ArgumentException` for a blank alias passed
to `Resolve`; `InvalidOperationException` for invalid configuration at construction. Callers
turn these into user-visible messages (`RepoCardViewModel`'s source problem and error events).

#### Dependencies

- **IPackageSourceProvider** (subsystem contract) — what it hands out.
- **PackageSourceDefinition** / **PackageSourceType** (subsystem data types) — the configuration
  it is built from.
- **ZipFolderSourceProvider**, **NuGetSourceProvider** — constructed by `Create`.

#### Callers

- **PackageVersionCache** — resolves an alias to a provider on a cache miss.
- **RepoCardViewModel** — builds a registry from the current settings for every source
  operation, and resolves the pin's alias.
- **SelectPackageWindowViewModel** — receives a registry for the dialog's source.
