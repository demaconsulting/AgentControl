## AgentPackageManagement

### Verification Approach

The `AgentPackageManagement` subsystem is verified indirectly through its constituent units'
test suites (`PackageSourceTests.cs`, `PackageVersionTests.cs`, `PackageVersionCacheTests.cs`,
`PackageSourceRegistryTests.cs`, `ZipFolderSourceProviderTests.cs`,
`NuGetSourceProviderTests.cs`, `NuGetSourceProviderServerTests.cs`, `NuGetSourceProviderSignatureTests.cs`,
`NuGetSourceProviderSignedPackageTests.cs` and `SignatureVerificationSupportTests.cs`), each of which is documented
in its own unit-level verification
design, plus `PackageSourceDefinitionTests.cs` and `PackageSourceServerLocationTests.cs`, which verify the
supporting source model types and `PackageSourceAccessException` that have no unit of their own, and
`AgentPackageManagementSubsystemTests.cs`, the subsystem tests that resolve configured sources through the registry
into providers and exercise the signature requirement and platform check together. Zip discovery,
version parsing, and caching use real temporary
directories containing real zip files, and the NuGet provider runs the real NuGet caching library
against a real folder feed built on the fly and, for servers, against an in-process mock NuGet V3 feed, so
they are exercised against genuine filesystem and network state; only the registry and cache
multi-source tests use an in-memory fake provider, to count
which sources were contacted. Package signatures are verified in two ways: the refusal of unsigned packages, the
re-check of cached packages, the fail-closed behavior and the generated trust configuration need no network, while
acceptance, the publisher pin and independence from the user's own NuGet trust settings use a real signed package
downloaded once per run from nuget.org (its SHA-256 verified), so the real signature machinery is exercised on every
supported platform rather than a fake.

### Test Environment

The server tests need loopback networking (an in-process Kestrel server on a fresh port per test) and the
`Microsoft.AspNetCore.App` shared framework (part of the .NET runtime, referenced by the test project). The
real-signed-package tests need access to nuget.org on first use and an operating system whose trust store holds the
roots the package's certificates chain to; they are skipped - and only then - when nuget.org cannot be reached or
those roots are missing, and run in a non-parallel collection with certificate revocation checking set to offline
so they never depend on revocation services. The unsigned-package, fail-closed and generated-configuration tests need
no network. Otherwise standard test environment, with no external services or hardware.

### Acceptance Criteria

- All unit tests for `PackageSource`, `PackageVersion`, `PackageVersionCache`,
  `PackageSourceRegistry`, `ZipFolderSourceProvider`, `NuGetSourceProvider`, `SignatureVerificationSupport` and the
  source model types pass with zero failures.
- Package discovery reflects only what is physically present in a source folder.
- Upgrade detection produces a definite true/false answer for every input, including an
  unparsable pinned version or an empty source folder.
- A NuGet server source lists versions without downloading, signs in in the documented order (NuGet's own
  credentials, the session or saved credential, then one prompt), offers a credential only to the server's own origin,
  reports each failure distinctly without revealing a secret, and refuses a plain-`http` address unless the
  source allows it.
- A NuGet source that requires signed packages supplies no unsigned, invalidly signed or wrongly signed package -
  freshly downloaded or already cached - accepts a validly signed one, honors the pinned author fingerprint, is not
  influenced by the user's own trusted-signer settings, reports a refusal as a signature failure and, where the
  platform cannot verify signatures, refuses to list or supply rather than skip the check.

### Test Scenarios

**AgentPackageManagement_DiscoverPackages_UnitTestsCoverNameVersionAndEnumeration**: Package
name/version discovery and enumeration are verified by the `PackageSource` unit tests
(see the `PackageSource` unit verification design), with
`PackageSource_EnumeratePackageNames_MixedDirectory_ReturnsDistinctSortedNames` cited directly
at the subsystem level, covering `AgentControl-AgentPackageManagement-DiscoverPackages`
(children: `AgentControl-PackageSource-EnumeratePackages`,
`AgentControl-PackageSource-DiscoverNames`, `AgentControl-PackageSource-FindLatest`,
`AgentControl-PackageVersion-Parse`, `AgentControl-PackageVersionCache-CachePackageNames`,
`AgentControl-PackageVersionCache-CacheVersions`,
`AgentControl-PackageVersionCache-Invalidate`).

**AgentPackageManagement_UpgradeDetection_UnitTestsCoverNewerVersionAndUnparsablePin**:
Upgrade detection is verified by the `PackageSource`, `PackageVersion`, and
`PackageVersionCache` unit tests (see their respective unit verification designs), with
`PackageSource_IsNewerVersionAvailable_SourceHasNewerVersion_ReturnsTrue` and
`PackageSource_IsNewerVersionAvailable_UnparsablePin_ReturnsTrue` cited directly at the
subsystem level, covering `AgentControl-AgentPackageManagement-UpgradeDetection` (children:
`AgentControl-PackageSource-DetectNewerVersion`, `AgentControl-PackageVersion-Compare`,
`AgentControl-PackageVersionCache-CacheNewerVersion`).

**AgentPackageManagement_SourceDefinitions_HoldNoSecretsAndDeriveLocationKind**: The source
description carries alias, type, location, package IDs and trust options with safe defaults,
derives folder-versus-server from the location, serializes its type by name without the derived
kind, validates aliases, has no property named like a password, token, key, credential or
secret, and flags a location that embeds a credential (URL user information, or a server URL
with a query string or fragment) without repeating it in the message, and judges a server address (`https`,
or `http` only when insecure HTTP is allowed; ends in `index.json`; no credential) with a message that says
what to do. This scenario is tested by
the source model tests, with
`PackageSourceDefinition_Members_HaveNoSecretLikeProperty`,
`PackageSourceDefinition_LocationKind_DerivedFromLocation`,
`PackageSourceDefinition_Serialize_TypeAsNameAndNoDerivedLocationKind`,
`PackageSourceDefinition_GetCredentialProblem_CredentialBearingLocation_ReturnsMessageWithoutSecret`,
`PackageSourceDefinition_GetCredentialProblem_OrdinaryLocation_ReturnsNull`,
`PackageSourceDefinition_GetServerLocationProblem_AcceptableAddress_ReturnsNull`, and
`PackageSourceDefinition_GetServerLocationProblem_UnacceptableAddress_ReturnsClearMessage` (the last two in
`PackageSourceServerLocationTests.cs`) cited directly at the
subsystem level, covering `AgentControl-AgentPackageManagement-SourceDefinitions`. The trust options are verified
too: a fingerprint written with or without colons, dashes or spaces and in either letter case is reduced to 64
upper-case hexadecimal characters, text that is not such a fingerprint is refused, and only an unreadable
fingerprint of a NuGet source that requires signatures is reported as a policy problem (not an empty one, one that is
not in use, or a zip source). This is tested by
`PackageSourceDefinition_TryNormalizeFingerprint_ValidSpellings_NormalizeToUpperCaseHex`,
`PackageSourceDefinition_TryNormalizeFingerprint_InvalidText_IsRefused` and
`PackageSourceDefinition_GetSignaturePolicyProblem_OnlyAnUnreadableFingerprintOfARequiringNuGetSource`.

**AgentPackageManagement_SourceResolution_ResolvesOnlyTheNamedSourceWithNoFallback**: Resolving a
source alias returns that source alone; a missing alias is an error that contacts no other source
even when only one source is configured. This scenario is verified by the `PackageSourceRegistry`
unit tests (see its unit verification design), with
`PackageSourceRegistry_PinToSourceB_SamePackageIdAtBothSources_OnlyBIsContacted` and
`PackageSourceRegistry_Resolve_MissingAliasWithSingleConfiguredSource_DoesNotFallBack` cited
directly at the subsystem level, covering `AgentControl-AgentPackageManagement-SourceResolution`
(children: `AgentControl-PackageSourceRegistry-ResolveAlias`,
`AgentControl-PackageSourceRegistry-MissingAliasIsError`,
`AgentControl-PackageSourceRegistry-BlankAliasNeverConfigured`,
`AgentControl-PackageSourceRegistry-InvalidAliasesRejected`,
`AgentControl-PackageSourceRegistry-NullDefinitionRejected`,
`AgentControl-PackageSourceRegistry-DefinitionsAreASnapshot`,
`AgentControl-PackageSourceRegistry-NoCrossSourceFallback`).

**AgentPackageManagement_ZipFolderSource_ListsAndSuppliesFromItsFolderOnly**: A zip folder source
lists its packages and versions with the same file-name rules as before, supplies a package's
content only from its own folder (not through a link that leads outside it), reports a missing folder
as such and honors cancellation. This scenario is verified by the `ZipFolderSourceProvider` unit
tests, with `ZipFolderSourceProvider_ListVersions_MixedFolder_ReturnsMatchingPackagesStampedWithAlias`
and `ZipFolderSourceProvider_Acquire_DiscoveredPackage_ReturnsReaderOverZipRoot` cited directly at
the subsystem level, covering `AgentControl-AgentPackageManagement-ZipFolderSource` (children:
`AgentControl-ZipFolderSourceProvider-ListAndAcquire`,
`AgentControl-ZipFolderSourceProvider-ListPackageNames`,
`AgentControl-ZipFolderSourceProvider-AcquireContent`,
`AgentControl-ZipFolderSourceProvider-FolderErrorsNormalized`,
`AgentControl-ZipFolderSourceProvider-CancellationObserved`,
`AgentControl-ZipFolderSourceProvider-AcquireOnlyFromOwnSource`,
`AgentControl-ZipFolderSourceProvider-ZipMustBeInOwnFolder`,
`AgentControl-ZipFolderSourceProvider-RefusesLinkedZipOutsideFolder`,
`AgentControl-ZipFolderSourceProvider-AllowsLinkedZipInsideFolder`,
`AgentControl-ZipFolderSourceProvider-ValidateDefinition`).

**AgentPackageManagement_NuGetFolderSource_ListsAndSuppliesFromPrivateStorage**: A NuGet folder
source lists the versions of its packages, supplies a chosen version's content folder, keeps its
storage in AgentControl-owned folders, never touches the target repo, and honors a cancellation requested
before it starts without preparing anything. This scenario is
verified by the `NuGetSourceProvider` unit tests, with
`NuGetSourceProvider_ListVersions_FolderFeedWithVersions_ReturnsAllVersionsStampedWithAlias`,
`NuGetSourceProvider_Acquire_ExistingPackage_ReturnsReaderOverContentFolder`, and
`NuGetSourceProvider_ListAndAcquire_NeverTouchesRepoNuGetConfig` cited directly at the subsystem
level, covering `AgentControl-AgentPackageManagement-NuGetFolderSource` (children:
`AgentControl-NuGetSourceProvider-ListVersions`,
`AgentControl-NuGetSourceProvider-ListVersionsUnknownPackageIsEmpty`,
`AgentControl-NuGetSourceProvider-ListVersionsMissingFolderReported`,
`AgentControl-NuGetSourceProvider-ListPackageIds`,
`AgentControl-NuGetSourceProvider-CancellationHonoredBeforeSideEffects`,
`AgentControl-NuGetSourceProvider-AcquireToPrivateCache`,
`AgentControl-NuGetSourceProvider-PrivateCachePerSource`,
`AgentControl-NuGetSourceProvider-RejectPackageWithoutContent`,
`AgentControl-NuGetSourceProvider-ExplicitCacheRoot`,
`AgentControl-NuGetSourceProvider-CacheRootMustBeAbsolute`,
`AgentControl-NuGetSourceProvider-NoRepoNuGetConfig`,
`AgentControl-NuGetSourceProvider-NoCredentialServiceRegistration`,
`AgentControl-NuGetSourceProvider-ValidateLocation`,
`AgentControl-NuGetSourceProvider-RejectNonNuGetDefinition`,
`AgentControl-NuGetSourceProvider-AcquireOnlyFromOwnSource`).

**AgentPackageManagement_NuGetServerSource_ListsSignsInAndReportsFailuresWithoutLeakingSecrets**: A NuGet
server source, run against an in-process mock NuGet V3 feed, lists versions without downloading a package and
supplies a chosen version's content; for a server that requires a sign-in it uses a credential from the
user's NuGet configuration without asking, otherwise asks the user once and remembers a working credential for
the session, reuses it without asking, forgets a rejected one and asks again; it never opens the prompt for
another host that the feed's index advertises; it reports a refused access, a wrong credential, a cancelled
sign-in, a sign-in that takes too long and an address that is not a feed as distinct failures with messages
that name only the server's origin; it accepts only `https` addresses (plain `http` only with the explicit
opt-in) of an `index.json` with no credential; and no secret reaches the log. This scenario is verified by the
`NuGetSourceProvider` unit tests (see its unit verification design), with
`NuGetSourceProvider_ListVersions_AnonymousServer_ReturnsVersionsWithoutDownloading`,
`NuGetSourceProvider_ListVersions_ServerNeedsSignIn_PromptsOnceAndRemembersCredentialForTheSession` and
`NuGetSourceProvider_Constructor_PlainHttp_RefusedUnlessInsecureHttpIsAllowed` cited directly at the subsystem
level, covering `AgentControl-AgentPackageManagement-NuGetServerSource` (children:
`AgentControl-NuGetSourceProvider-ServerListAndAcquire`,
`AgentControl-NuGetSourceProvider-ServerListDoesNotDownload`,
`AgentControl-NuGetSourceProvider-ServerUnknownPackageIsEmpty`,
`AgentControl-NuGetSourceProvider-ServerAcquire`,
`AgentControl-NuGetSourceProvider-PlainHttpRefusedUnlessAllowed`,
`AgentControl-NuGetSourceProvider-ServerAddressMustBeServiceIndex`,
`AgentControl-NuGetSourceProvider-CredentialInAddressRefused`,
`AgentControl-NuGetSourceProvider-HttpsAddressAccepted`,
`AgentControl-NuGetSourceProvider-SignInOrder`,
`AgentControl-NuGetSourceProvider-AskUserOnce`,
`AgentControl-NuGetSourceProvider-CredentialRememberedAfterSuccess`,
`AgentControl-NuGetSourceProvider-NonInteractiveLibrary`,
`AgentControl-NuGetSourceProvider-SavedSignIn`,
`AgentControl-NuGetSourceProvider-SavedSignInLookedUpOnce`,
`AgentControl-NuGetSourceProvider-RejectedSavedSignInRemoved`,
`AgentControl-NuGetSourceProvider-SignInSavedOnlyWhenChosen`,
`AgentControl-NuGetSourceProvider-CredentialOnlyForFeedOrigin`,
`AgentControl-NuGetSourceProvider-ServerFailures`,
`AgentControl-NuGetSourceProvider-RejectedCredentialForgotten`,
`AgentControl-NuGetSourceProvider-SignInTimeLimit`,
`AgentControl-NuGetSourceProvider-CallerCancellationNotAFailure`,
`AgentControl-NuGetSourceProvider-SecretsNotLogged`,
`AgentControl-NuGetSourceProvider-SecretsNotInExceptions`,
`AgentControl-PackageSourceRegistry-PlainHttpServerSourceRefused`).

**AgentPackageManagement_SignedPackages_RefusesEveryPackageThatFailsTheRequirement**: Through the registry, which
resolves configured sources into providers, a NuGet source that requires signed packages refuses an unsigned
package as a signature failure while a second source over the same feed that does not require signatures supplies
it; with the real signed package, a source that requires signatures with no pin and one pinned to the package's
author certificate (typed in lower case with spaces) accept it - the package is installed and the provider goes on
to its content check - and a source pinned to another publisher refuses it as "not signed by the trusted
publisher"; a hand-edited unreadable fingerprint stops the source when it is resolved. This scenario is tested by
`AgentPackageManagementSubsystem_RequireSignedSource_UnsignedPackageRefusedWhileOpenSourceServesIt`,
`AgentPackageManagementSubsystem_SignedPackage_AcceptedWhenAnyOrAuthorPinnedAndRejectedWhenOtherPinned` and
`AgentPackageManagementSubsystem_UnreadableSignerFingerprint_SourceIsRefusedWhenResolved`, covering
`AgentControl-AgentPackageManagement-SignedPackages`. The finer evidence - the re-check of a package cached before
the requirement or pin changed, a server source refusing an unsigned package, a repository countersignature pin
not matching, the independence from the user's own trusted-signer settings and the generated trust configuration -
is in the `NuGetSourceProvider` unit tests and is covered by the child requirements
`AgentControl-NuGetSourceProvider-RequireSigned`, `AgentControl-NuGetSourceProvider-SignatureCheckedForCachedPackage`,
`AgentControl-NuGetSourceProvider-SignedPackageAccepted`,
`AgentControl-NuGetSourceProvider-UnsignedAcceptedWhenNotRequired`, `AgentControl-NuGetSourceProvider-SignerPin`,
`AgentControl-NuGetSourceProvider-RepositoryCountersignatureNotAPin`,
`AgentControl-NuGetSourceProvider-ChangedPinAppliesToCachedPackages`,
`AgentControl-NuGetSourceProvider-GeneratedSignerConfiguration`,
`AgentControl-NuGetSourceProvider-NoSignerConfigurationWithoutRequirement`,
`AgentControl-NuGetSourceProvider-SignerTrustIsolated`, `AgentControl-NuGetSourceProvider-SignatureFailureReported`,
`AgentControl-NuGetSourceProvider-SignatureFailureNamesPinnedPublisher`
and `AgentControl-NuGetSourceProvider-UnreadableFingerprintRefused`.

**AgentPackageManagement_SignaturePlatformSupport_FailsClosedWhereVerificationIsUnsupported**: When the platform
support check answers "not supported" - including when its probe fails - a NuGet source that requires signatures
refuses to list with a signature failure that explains why, while a source that does not require signatures over
the same feed works as usual. This scenario is tested by
`AgentPackageManagementSubsystem_RequireSignedOnUnsupportedPlatform_SourceRefusesAndOthersWork`, covering
`AgentControl-AgentPackageManagement-SignaturePlatformSupport`. The support check's own behavior (exactly Windows,
Linux and macOS, the fail-closed probe, the real offline certificate-chain probe, the documented explanation) and
the provider's refusal to acquire are
verified by the unit tests and covered by the children
`AgentControl-SignatureVerificationSupport-PlatformSupport`,
`AgentControl-SignatureVerificationSupport-FailClosedProbe`,
`AgentControl-SignatureVerificationSupport-RealChainProbe`,
`AgentControl-SignatureVerificationSupport-UnsupportedExplanation`,
`AgentControl-NuGetSourceProvider-FailClosedWithoutVerification` and
`AgentControl-NuGetSourceProvider-UnsupportedPlatformDoesNotAffectOpenSources`.
