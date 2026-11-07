## AgentPackageManagement

### Verification Approach

The `AgentPackageManagement` subsystem is verified indirectly through its constituent units'
test suites (`PackageSourceTests.cs`, `PackageVersionTests.cs`, `PackageVersionCacheTests.cs`,
`PackageSourceRegistryTests.cs`, `ZipFolderSourceProviderTests.cs`,
`NuGetSourceProviderTests.cs`, `NuGetSourceProviderServerTests.cs`, `NuGetSourceProviderSignatureTests.cs`,
`NuGetSourceProviderSignedPackageTests.cs` and `SignatureVerificationSupportTests.cs`), each of which is documented
in its
own unit-level verification
design, plus `PackageSourceDefinitionTests.cs` and `PackageSourceServerLocationTests.cs`, which verify the
supporting source model types and `PackageSourceAccessException` that have no unit of their own. Zip discovery,
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
`AgentControl-PackageSourceRegistry-NoCrossSourceFallback`).

**AgentPackageManagement_ZipFolderSource_ListsAndSuppliesFromItsFolderOnly**: A zip folder source
lists its packages and versions with the same file-name rules as before and supplies a package's
content only from its own folder. This scenario is verified by the `ZipFolderSourceProvider` unit
tests, with `ZipFolderSourceProvider_ListVersions_MixedFolder_ReturnsMatchingPackagesStampedWithAlias`
and `ZipFolderSourceProvider_Acquire_DiscoveredPackage_ReturnsReaderOverZipRoot` cited directly at
the subsystem level, covering `AgentControl-AgentPackageManagement-ZipFolderSource` (children:
`AgentControl-ZipFolderSourceProvider-ListAndAcquire`,
`AgentControl-ZipFolderSourceProvider-AcquireOnlyFromOwnSource`).

**AgentPackageManagement_NuGetFolderSource_ListsAndSuppliesFromPrivateStorage**: A NuGet folder
source lists the versions of its packages, supplies a chosen version's content folder, keeps its
storage in AgentControl-owned folders, and never touches the target repo. This scenario is
verified by the `NuGetSourceProvider` unit tests, with
`NuGetSourceProvider_ListVersions_FolderFeedWithVersions_ReturnsAllVersionsStampedWithAlias`,
`NuGetSourceProvider_Acquire_ExistingPackage_ReturnsReaderOverContentFolder`, and
`NuGetSourceProvider_ListAndAcquire_NeverTouchesRepoNuGetConfig` cited directly at the subsystem
level, covering `AgentControl-AgentPackageManagement-NuGetFolderSource` (children:
`AgentControl-NuGetSourceProvider-ListVersions`,
`AgentControl-NuGetSourceProvider-AcquireToPrivateCache`,
`AgentControl-NuGetSourceProvider-ExplicitCacheRoot`,
`AgentControl-NuGetSourceProvider-NoRepoNuGetConfig`,
`AgentControl-NuGetSourceProvider-NoCredentialServiceRegistration`,
`AgentControl-NuGetSourceProvider-ValidateLocation`,
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
`AgentControl-NuGetSourceProvider-ValidateLocation`,
`AgentControl-NuGetSourceProvider-SignInOrder`,
`AgentControl-NuGetSourceProvider-CredentialOnlyForFeedOrigin`,
`AgentControl-NuGetSourceProvider-ServerFailures`,
`AgentControl-NuGetSourceProvider-SecretsNotLogged`, `AgentControl-NuGetSourceProvider-SavedSignIn`).

**AgentPackageManagement_SignedPackages_RefusesEveryPackageThatFailsTheRequirement**: For a NuGet source that
requires signed packages, an unsigned package is refused as a signature failure and nothing is installed for it; the
same package is accepted when the source does not require signatures; a package cached while no signature was
required is refused once the requirement is turned on, and a package accepted for one pinned publisher is refused
once the pin changes; a server source refuses an unsigned package as a signature failure too. With the real signed
package: it is accepted with no pin and with its author certificate pinned (typed in lower case with colons), and
refused - and not installed - when another publisher's fingerprint, or only the repository countersignature's, is
pinned. This scenario is tested by
`NuGetSourceProvider_Acquire_RequireSignedAndPackageUnsigned_IsRejectedAndNothingIsInstalled`,
`NuGetSourceProvider_Acquire_SignedPackageAndMatchingAuthorPinned_SignatureIsAccepted`,
`NuGetSourceProvider_Acquire_SignedPackageAndOtherSignerPinned_IsRejectedAndNotInstalled`,
`NuGetSourceProvider_Acquire_PinnedSignerChangedAfterPackageWasCached_RejectsTheCachedPackage` and
`NuGetSourceProvider_Acquire_ServerRequiresSignaturesAndPackageUnsigned_IsRejectedAsASignatureFailure` cited
directly at the subsystem level, covering `AgentControl-AgentPackageManagement-SignedPackages` (children:
`AgentControl-NuGetSourceProvider-RequireSigned`, `AgentControl-NuGetSourceProvider-SignerPin`,
`AgentControl-NuGetSourceProvider-SignerTrustIsolated`, `AgentControl-NuGetSourceProvider-SignatureFailureReported`,
`AgentControl-NuGetSourceProvider-UnreadableFingerprintRefused`).

**AgentPackageManagement_SignaturePlatformSupport_FailsClosedWhereVerificationIsUnsupported**: The platform answer is
`true` for exactly Windows, Linux and macOS and the explanation is the documented text; a NuGet source that requires
signatures on a platform that cannot verify them refuses both listing and acquiring with a signature failure that
explains why, while a source that does not require signatures works as usual there. This scenario is tested by
`SignatureVerificationSupport_IsSupportedPlatform_WindowsLinuxAndMacOsOnly`,
`SignatureVerificationSupport_UnsupportedMessage_IsTheDocumentedExplanation` and
`NuGetSourceProvider_RequireSignedOnUnsupportedPlatform_FailsClosedForListAndAcquire` cited directly at the
subsystem level, covering `AgentControl-AgentPackageManagement-SignaturePlatformSupport` (children:
`AgentControl-SignatureVerificationSupport-PlatformSupport`,
`AgentControl-SignatureVerificationSupport-UnsupportedExplanation`,
`AgentControl-NuGetSourceProvider-FailClosedWithoutVerification`).
