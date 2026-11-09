## AgentPackageManagement

### Verification Approach

The `AgentPackageManagement` subsystem is verified indirectly through its constituent units'
test suites (`PackageSourceTests.cs`, `PackageVersionTests.cs`, `PackageVersionCacheTests.cs`,
`PackageSourceRegistryTests.cs`, `ZipFolderSourceProviderTests.cs`,
`NuGetSourceProviderTests.cs`, `NuGetSourceProviderServerTests.cs`, `NuGetSourceProviderSignatureTests.cs`,
`NuGetSourceProviderSignedPackageTests.cs`, `SignatureVerificationSupportTests.cs` and
`PackageContentIdentityTests.cs`), each of which is documented
in its own unit-level verification
design, plus `PackageSourceDefinitionTests.cs` and `PackageSourceServerLocationTests.cs`, which verify the
supporting source model types and `PackageSourceAccessException` that have no unit of their own, and
`AgentPackageManagementSubsystemTests.cs`, the subsystem tests that resolve configured sources through the registry
into providers and exercise the NuGet server source (against the in-process mock feed), the signature requirement and
the platform check together. Zip discovery,
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
  `PackageSourceRegistry`, `ZipFolderSourceProvider`, `NuGetSourceProvider`, `SignatureVerificationSupport`,
  `PackageContentIdentity` and the
  source model types pass with zero failures.
- A package a zip or NuGet source delivers reports the SHA-256 identity of its package file, and a zip republished
  under the same version reports a different one.
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

**AgentPackageManagement_SourceDefinitions_CarryAliasTypeLocationAndTrustOptions**: The source
description carries alias, type, location, package IDs and trust options with safe defaults and
serializes its type by name without the derived kind. This scenario is tested by the source model tests, with
`PackageSourceDefinition_Defaults_AreSafeAndEmpty` and
`PackageSourceDefinition_Serialize_TypeAsNameAndNoDerivedLocationKind` cited directly at the subsystem level,
covering `AgentControl-AgentPackageManagement-SourceDefinitions`.

**AgentPackageManagement_SourceLocationKind_FollowsTheLocation**: A location that is an absolute `http` or `https`
address is a server and any other location is a folder, decided from the location alone. This scenario is tested
by `PackageSourceDefinition_LocationKind_DerivedFromLocation`, covering
`AgentControl-AgentPackageManagement-SourceLocationKind`.

**AgentPackageManagement_SourceNoSecrets_NoMemberCanHoldASecret**: No property of the source description is named
like a password, token, key, credential or secret. This scenario is tested by
`PackageSourceDefinition_Members_HaveNoSecretLikeProperty`, covering
`AgentControl-AgentPackageManagement-SourceNoSecrets`.

**AgentPackageManagement_SourceCredentialBearingLocation_IsFlaggedWithoutRepeatingIt**: A location that embeds a
credential (URL user information, or a server URL with a query string or fragment) is flagged without repeating it
in the message, and an ordinary location is not. This scenario is tested by
`PackageSourceDefinition_GetCredentialProblem_CredentialBearingLocation_ReturnsMessageWithoutSecret` and
`PackageSourceDefinition_GetCredentialProblem_OrdinaryLocation_ReturnsNull`, covering
`AgentControl-AgentPackageManagement-SourceCredentialBearingLocation`.

**AgentPackageManagement_SourceServerAddress_IsJudgedWithAMessageThatSaysWhatToDo**: A server address is judged
(`https`, or `http` only when insecure HTTP is allowed; ends in `index.json`; no credential) with a message that
says what to do. This scenario is tested by
`PackageSourceDefinition_GetServerLocationProblem_AcceptableAddress_ReturnsNull` and
`PackageSourceDefinition_GetServerLocationProblem_UnacceptableAddress_ReturnsClearMessage` (in
`PackageSourceServerLocationTests.cs`), covering `AgentControl-AgentPackageManagement-SourceServerAddress`.

**AgentPackageManagement_SourceFingerprintNormalization_SpellingsDenoteOnePublisher**: A fingerprint written with or
without colons, dashes or spaces and in either letter case is reduced to 64 upper-case hexadecimal characters. This
scenario is tested by `PackageSourceDefinition_TryNormalizeFingerprint_ValidSpellings_NormalizeToUpperCaseHex`,
covering `AgentControl-AgentPackageManagement-SourceFingerprintNormalization`.

**AgentPackageManagement_SourceUnreadableFingerprint_IsRefusedAndReportedAsAPolicyProblem**: Text that is not a
SHA-256 fingerprint is refused, and only an unreadable fingerprint of a NuGet source that requires signatures is
reported as a policy problem (not an empty one, one that is not in use, or a zip source). This scenario is tested
by `PackageSourceDefinition_TryNormalizeFingerprint_InvalidText_IsRefused` and
`PackageSourceDefinition_GetSignaturePolicyProblem_OnlyAnUnreadableFingerprintOfARequiringNuGetSource`, covering
`AgentControl-AgentPackageManagement-SourceUnreadableFingerprint`.

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
`AgentControl-NuGetSourceProvider-RejectLinkedCacheFolder`,
`AgentControl-NuGetSourceProvider-RejectPackageWithoutContent`,
`AgentControl-NuGetSourceProvider-ExplicitCacheRoot`,
`AgentControl-NuGetSourceProvider-CacheRootMustBeAbsolute`,
`AgentControl-NuGetSourceProvider-NoRepoNuGetConfig`,
`AgentControl-NuGetSourceProvider-NoCredentialServiceRegistration`,
`AgentControl-NuGetSourceProvider-ValidateLocation`,
`AgentControl-NuGetSourceProvider-RejectNonNuGetDefinition`,
`AgentControl-NuGetSourceProvider-AcquireOnlyFromOwnSource`).

**AgentPackageManagement_NuGetServerSource_ListsAcquiresSignsInAndReportsFailuresWithoutLeakingSecrets**: A NuGet
server source is resolved through the registry and run against an in-process mock NuGet V3 feed in the subsystem
tests (`AgentPackageManagementSubsystemTests.cs`); the finer evidence for each child requirement is in the
`NuGetSourceProvider` unit tests (see its unit verification design). Each requirement is covered as follows.

- Listing: versions are listed, labeled with the alias, without downloading a package, and none for an unknown
  package, by
  `AgentPackageManagementSubsystem_ServerSource_ListVersions_ReturnsVersionsWithoutDownloadingAndNoneForUnknownPackage`,
  covering `AgentControl-AgentPackageManagement-NuGetServerSource` (children:
  `AgentControl-NuGetSourceProvider-ServerListAndAcquire`,
  `AgentControl-NuGetSourceProvider-ServerListDoesNotDownload`,
  `AgentControl-NuGetSourceProvider-ServerUnknownPackageIsEmpty`).
- Acquisition: a chosen version's content is downloaded and supplied, by
  `AgentPackageManagementSubsystem_ServerSource_Acquire_SuppliesContentOfChosenVersion`, covering
  `AgentControl-AgentPackageManagement-NuGetServerAcquire` (child: `AgentControl-NuGetSourceProvider-ServerAcquire`).
- Transport policy: a source is only usable with an `https` address of an `index.json` (plain `http` only with the
  explicit opt-in) that embeds no credential, and a refusal does not repeat the address, by
  `AgentPackageManagementSubsystem_ServerSource_Address_RefusedUnlessHttpsServiceIndexOrOptedInHttp`, covering
  `AgentControl-AgentPackageManagement-NuGetServerTransportPolicy` (children:
  `AgentControl-NuGetSourceProvider-PlainHttpRefusedUnlessAllowed`,
  `AgentControl-NuGetSourceProvider-ServerAddressMustBeServiceIndex`,
  `AgentControl-NuGetSourceProvider-CredentialInAddressRefused`,
  `AgentControl-NuGetSourceProvider-HttpsAddressAccepted`,
  `AgentControl-PackageSourceRegistry-PlainHttpServerSourceRefused`).
- Sign-in order: a credential from the user's NuGet configuration or a saved sign-in is used without asking,
  otherwise the user is asked once and the answer is reused by the next operation without asking, by
  `AgentPackageManagementSubsystem_ServerSource_NeedsSignIn_UsesSavedSignInOtherwiseAsksOnceAndReusesTheAnswer`,
  covering `AgentControl-AgentPackageManagement-NuGetServerSignIn` (children:
  `AgentControl-NuGetSourceProvider-SignInOrder`, `AgentControl-NuGetSourceProvider-AskUserOnce`,
  `AgentControl-NuGetSourceProvider-NonInteractiveLibrary`, `AgentControl-NuGetSourceProvider-SavedSignIn`,
  `AgentControl-NuGetSourceProvider-SavedSignInLookedUpOnce`). The failure when no prompt is available is verified
  by the failures scenario below.
- Credential retention: a wrong password is neither remembered nor saved, a rejected saved sign-in is removed from
  the user's store and replaced after one question, and a working credential the user chose to remember is saved,
  by `AgentPackageManagementSubsystem_ServerSource_Credentials_KeptOnlyAfterSuccessAndForgottenWhenRejected`,
  covering `AgentControl-AgentPackageManagement-NuGetServerCredentialRetention` (children:
  `AgentControl-NuGetSourceProvider-CredentialRememberedAfterSuccess`,
  `AgentControl-NuGetSourceProvider-RejectedCredentialForgotten`,
  `AgentControl-NuGetSourceProvider-RejectedSavedSignInRemoved`,
  `AgentControl-NuGetSourceProvider-SignInSavedOnlyWhenChosen`).
- Origin scoping: the user is asked for the feed's own origin only, even when the feed's index advertises resources
  on another host, by
  `AgentPackageManagementSubsystem_ServerSource_ResourcesOnAnotherOrigin_PromptsOnlyForTheFeedOrigin`, covering
  `AgentControl-AgentPackageManagement-NuGetServerOriginScoping` (child:
  `AgentControl-NuGetSourceProvider-CredentialOnlyForFeedOrigin`).
- Failures: a sign-in that is needed but cannot be asked for, a cancelled sign-in, a sign-in that takes too long
  and an address that is not a feed are distinct failures without a secret or address path, and a caller's
  cancellation during a sign-in is not a failure, by
  `AgentPackageManagementSubsystem_ServerSource_Failures_AreDistinctAndNameNeitherSecretNorPath` and
  `AgentPackageManagementSubsystem_ServerSource_CallerCancelsWhileSigningIn_IsCancellationNotFailure`, covering
  `AgentControl-AgentPackageManagement-NuGetServerFailures` (children:
  `AgentControl-NuGetSourceProvider-ServerFailures`, `AgentControl-NuGetSourceProvider-SignInTimeLimit`,
  `AgentControl-NuGetSourceProvider-CallerCancellationNotAFailure`).
- Secret safety: no password, encoded credential or secret-bearing inner exception reaches the log or an
  exception, whether a sign-in succeeds or fails, by
  `AgentPackageManagementSubsystem_ServerSource_SignInFlows_NeverLogOrRaiseTheSecret`, covering
  `AgentControl-AgentPackageManagement-NuGetServerSecretSafety` (children:
  `AgentControl-NuGetSourceProvider-SecretsNotLogged`, `AgentControl-NuGetSourceProvider-SecretsNotInExceptions`).

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
`AgentControl-AgentPackageManagement-SignaturePlatformSupport`. The support check's own behavior is verified by the
unit tests and covered by the children
`AgentControl-SignatureVerificationSupport-PlatformSupport` (the decision for exactly Windows, Linux and macOS, a
platform-independent test), `AgentControl-SignatureVerificationSupport-SupportedOnWindows`,
`AgentControl-SignatureVerificationSupport-SupportedOnLinux` and
`AgentControl-SignatureVerificationSupport-SupportedOnMacOS` (each proven only by the test runs of that platform in
the continuous-integration matrix, selected by the `windows@`, `ubuntu@` and `macos@` source filters of the
test-result files, where the check reports "supported" and a real offline certificate-chain probe succeeds),
`AgentControl-SignatureVerificationSupport-FailClosedProbe`,
`AgentControl-SignatureVerificationSupport-RealChainProbe`,
`AgentControl-SignatureVerificationSupport-UnsupportedExplanation`; the provider's refusal to acquire is verified by
the unit tests and covered by
`AgentControl-NuGetSourceProvider-FailClosedWithoutVerification` and
`AgentControl-NuGetSourceProvider-UnsupportedPlatformDoesNotAffectOpenSources`.

**AgentPackageManagement_ContentIdentity_ZipAndNuGetSourcesReportTheBytesTheyDeliver**: Through the registry, a zip
source and a NuGet folder source each acquire a package and report a `sha256:` content identity; the NuGet identity
is the same on a second acquisition, and a zip replaced under the same version (the file deleted and written again
with other content) reports an identity that compares as changed. The identity of each kind is verified against an
independently computed SHA-256 at the unit level. This scenario is tested by
`AgentPackageManagementSubsystem_ZipAndNuGetSources_ReportContentIdentityAndDetectRepublishedZip`, covering
`AgentControl-AgentPackageManagement-ContentIdentity` (children: `AgentControl-PackageContentIdentity-Sha256`,
`AgentControl-PackageContentIdentity-Format`, `AgentControl-PackageContentIdentity-Compare`,
`AgentControl-ZipFolderSourceProvider-ReportContentIdentity` and
`AgentControl-NuGetSourceProvider-ReportContentIdentity`).
