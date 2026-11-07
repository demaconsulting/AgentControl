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
- Duplicate or blank aliases are rejected when the registry is built.
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
throws `UnknownSourceException` carrying and naming the alias; duplicate and blank aliases are
rejected at construction; blank aliases are never contained and rejected by `Resolve`; a NuGet
server source with an `https` address resolves to a `NuGetSourceProvider` while a plain-`http` one, whose
definition does not allow insecure HTTP, fails when resolved. This scenario is tested by
`PackageSourceRegistry_Resolve_MissingAlias_ThrowsUnknownSourceExceptionNamingAlias`,
`PackageSourceRegistry_Constructor_DuplicateOrBlankAlias_ThrowsInvalidOperationException`,
`PackageSourceRegistry_BlankAlias_ContainsFalseAndResolveThrowsArgumentException`, and
`PackageSourceRegistry_Create_NuGetServerSource_ResolvesHttpsAndRefusesPlainHttpWithoutOptIn`, covering
`AgentControl-PackageSourceRegistry-MissingAliasIsError`.

**PackageSourceRegistry_NoFallback_NeverUsesAnotherSource**: With a single configured source
named `Default`, resolving a different alias fails and that source receives no calls; with the
same package ID at sources A (higher version) and B, resolving and listing through B returns only
B's version and A receives no calls; with the pinned source B removed, resolving it fails and A
receives no calls. This scenario is tested by
`PackageSourceRegistry_Resolve_MissingAliasWithSingleConfiguredSource_DoesNotFallBack`,
`PackageSourceRegistry_PinToSourceB_SamePackageIdAtBothSources_OnlyBIsContacted`, and
`PackageSourceRegistry_PinToRemovedSource_ThrowsAndContactsNoOtherSource`, covering
`AgentControl-PackageSourceRegistry-NoCrossSourceFallback`.
