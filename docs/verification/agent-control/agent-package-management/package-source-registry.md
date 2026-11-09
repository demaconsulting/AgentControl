### PackageSourceRegistry

#### Verification Approach

`PackageSourceRegistry` is verified with unit tests defined in `PackageSourceRegistryTests.cs`.
Source providers are replaced by an in-memory fake (`FakePackageSourceProvider`) that serves a
fixed list of packages and counts every call made to it, so the tests can prove not only which
provider was returned but also which sources were never contacted. The default provider factory
is verified with real definitions pointing at temporary paths (no provider operation is invoked).

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Resolving a configured alias returns exactly that source's provider, case-insensitively, and
  creates each provider once and only when first used.
- Resolving an alias that is not configured is an error naming the alias - including when exactly
  one source (or a source named `Default`) is configured - and never contacts any source.
- A blank alias is never configured and is rejected by `Resolve`.
- Duplicate or blank aliases, and a missing (`null`) source entry, are rejected when the registry is
  built, the latter with an error saying a configured source entry is missing.
- A plain-`http` server source without the insecure-HTTP opt-in is refused when resolved.
- Changing a definition, or the list it was built from, after construction - or changing a definition
  obtained from `Definitions` - does not change which source an alias resolves to.
- With the same package ID at two sources, resolving the pinned source contacts only that source.

#### Test Scenarios

**PackageSourceRegistry_ResolveAlias_ReturnsExactlyThatSource**: Resolving each of two aliases
returns that source's provider (the alias matched case-insensitively); the provider is created
once and lazily on first use; the default factory yields the zip and NuGet provider kinds for
their source types. This scenario is tested by
`PackageSourceRegistry_Resolve_ConfiguredAlias_ReturnsThatSourcesProvider`,
`PackageSourceRegistry_Resolve_RepeatedCalls_CreatesProviderOnceAndLazily`, and
`PackageSourceRegistry_Create_ZipAndNuGetDefinitions_ResolvesMatchingProviderKinds`, covering
`AgentControl-PackageSourceRegistry-ResolveAlias`.

**PackageSourceRegistry_MissingAlias_IsAnErrorNamingTheAlias**: An alias that is not configured
throws `UnknownSourceException` carrying and naming the alias. This scenario is tested by
`PackageSourceRegistry_Resolve_MissingAlias_ThrowsUnknownSourceExceptionNamingAlias`, covering
`AgentControl-PackageSourceRegistry-MissingAliasIsError`.

**PackageSourceRegistry_BlankAlias_IsNeverConfigured**: A blank alias is not contained and is rejected
by `Resolve` with `ArgumentException`. This scenario is tested by
`PackageSourceRegistry_BlankAlias_ContainsFalseAndResolveThrowsArgumentException`, covering
`AgentControl-PackageSourceRegistry-BlankAliasNeverConfigured`.

**PackageSourceRegistry_InvalidAliases_AreRejectedWhenBuilt**: A configuration with a duplicate alias or
a blank alias is rejected at construction with `InvalidOperationException`. This scenario is tested by
`PackageSourceRegistry_Constructor_DuplicateOrBlankAlias_ThrowsInvalidOperationException`, covering
`AgentControl-PackageSourceRegistry-InvalidAliasesRejected`.

**PackageSourceRegistry_NullDefinitionEntry_IsRejectedWhenBuilt**: A definitions sequence that contains a
`null` element is rejected at construction with `InvalidOperationException` saying that a configured
package source entry is missing, not with a `NullReferenceException`. This scenario is tested by
`PackageSourceRegistry_Constructor_NullDefinitionEntry_ThrowsInvalidOperationException`, covering
`AgentControl-PackageSourceRegistry-NullDefinitionRejected`.

**PackageSourceRegistry_PlainHttpServerSource_IsRefusedWithoutOptIn**: A NuGet server source with an
`https` address resolves to a `NuGetSourceProvider` while a plain-`http` one, whose definition does not
allow insecure HTTP, fails when resolved. This scenario is tested by
`PackageSourceRegistry_Create_NuGetServerSource_ResolvesHttpsAndRefusesPlainHttpWithoutOptIn`, covering
`AgentControl-PackageSourceRegistry-PlainHttpServerSourceRefused`.

**PackageSourceRegistry_Definitions_AreASnapshot**: After a registry is built, mutating a definition (or
the list it was built from) does not change which source an alias resolves to; mutating what `Definitions`
returns leaves the registry unaffected, because every access returns a fresh immutable list of clones.
This scenario is tested by
`PackageSourceRegistry_Constructor_DefinitionsMutatedAfterwards_RegistryIsASnapshot` and
`PackageSourceRegistry_Definitions_CallerMutatesReturnedDefinitions_RegistryIsUnaffected`, covering
`AgentControl-PackageSourceRegistry-DefinitionsAreASnapshot`.

**PackageSourceRegistry_NoFallback_NeverUsesAnotherSource**: With a single configured source
named `Default`, resolving a different alias fails and that source receives no calls; with the
same package ID at sources A (higher version) and B, resolving and listing through B returns only
B's version and A receives no calls; with the pinned source B removed, resolving it fails and A
receives no calls. This scenario is tested by
`PackageSourceRegistry_Resolve_MissingAliasWithSingleConfiguredSource_DoesNotFallBack`,
`PackageSourceRegistry_PinToSourceB_SamePackageIdAtBothSources_OnlyBIsContacted`, and
`PackageSourceRegistry_PinToRemovedSource_ThrowsAndContactsNoOtherSource`, covering
`AgentControl-PackageSourceRegistry-NoCrossSourceFallback`.

**PackageSourceRegistry_Create_GitSource_ResolvesGitProviderAndFailsClosedWhenInvalid**: The default factory resolves a
valid Git source to a `GitSourceProvider` built with the given options, a Git source whose address is a remote-helper
form fails with an `InvalidOperationException` when it is resolved (not when the registry is built), and an alias that is
not configured is still an error. This scenario is tested by
`PackageSourceRegistry_Create_GitSource_ResolvesGitProviderAndFailsClosedWhenInvalid`, covering
`AgentControl-PackageSourceRegistry-GitSourceResolved`.
