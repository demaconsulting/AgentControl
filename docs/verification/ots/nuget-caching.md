## NuGetCaching Verification

This document provides the verification evidence for the `NuGetCaching` OTS software item.
Requirements for this OTS item are defined in the NuGetCaching OTS Software Requirements
document.

### Required Functionality

DemaConsulting.NuGet.Caching backs the `NuGetSourceProvider` unit of AgentControl's
`AgentPackageManagement` subsystem. AgentControl relies on the library to:

- list the versions of a package held in a NuGet folder feed and unpack a chosen version into a package cache whose
  location is governed by the configuration found at the starting folder AgentControl supplies;
- list the versions of a package on a NuGet V3 server without downloading it, and download a chosen version;
- obtain the credentials for a challenged address from a callback supplied for each call, or from a `nuget.config`
  source of the same address, bound each credential exchange by a supplied time, report a refused access (HTTP 401
  or 403) with its authentication exception, fail a call whose callback credential was rejected instead of retrying,
  and - called with interactive plug-ins disabled - never open a user interface of its own;
- when asked to require signed packages, verify a package's signature on a fresh download and for a package already
  in the cache: accept a valid signature, and refuse an unsigned package, a package whose content no longer matches
  its signature and a package whose signer is not trusted, with an error that identifies a signature failure.

The library's credential service is process-wide and is registered by the library itself.

The trusted signers the library applies are the `trustedSigners` it finds in the `nuget.config` discovered from the
starting folder. The OTS requirements cover only the library honoring what it finds: a section beginning with
`<clear />` makes signers configured elsewhere not apply, and a listed `author` entry restricts trust to that signer.
Generating that configuration (the section beginning with `<clear />` and the optional pinned `author` entry) is
AgentControl's own behavior, verified against the `NuGetSourceProvider` unit requirements
`AgentControl-NuGetSourceProvider-GeneratedSignerConfiguration` and
`AgentControl-NuGetSourceProvider-NoSignerConfigurationWithoutRequirement`, not here.

### Verification Approach

#### Evidence relied on

The evidence is AgentControl's own integration tests through the `NuGetSourceProvider` unit. They exercise the real
library against a real NuGet folder feed that the tests create on the fly (a minimal package archive holding a package
manifest and the content folder) and, for server access, against an in-process NuGet V3 mock feed (`MockNuGetFeed`,
Kestrel over plain HTTP, with a Basic-authentication mode and a mode that answers every request with 403) in
`NuGetSourceProviderServerTests.cs`. For signature verification the evidence is of two kinds:
`NuGetSourceProviderSignatureTests.cs` uses unsigned packages and needs no network, and
`NuGetSourceProviderSignedPackageTests.cs` uses a **real signed package** (Newtonsoft.Json, signed by its author and
countersigned by the nuget.org repository) that `SignedNuGetFixture` downloads once per test run and checks against a
pinned SHA-256, so the library's real signature machinery is exercised with a genuine certificate chain on Windows,
Linux and macOS; an altered copy of that package (one byte of its content flipped, which the unchanged signature no
longer matches) supplies the invalid-signature case. Those tests are skipped - and only then - when nuget.org cannot be
reached or the machine's trust store lacks the roots the package needs, and run with certificate revocation checking
set to offline in a non-parallel collection. Each test uses its own temporary feed (or its own mock feed on a fresh
port) and its own AgentControl-owned starting folder, so the tests are parallel-safe and never touch the developer's
global NuGet cache; the NuGet HTTP cache is isolated per test process (`HttpCacheIsolation`). This evidence is
indirect: it proves the behavior AgentControl depends on, not the library's complete behavior.

#### Vendor evidence

The library is maintained in the `demaconsulting/NuGetCaching` repository, which holds its own test project
(`test/DemaConsulting.NuGet.Caching.Tests`) and runs it, with its own requirements trace, in its continuous
integration. The referenced package version (`2.0.0-beta.2`, tag `2.0.0-beta.2`) comes from that repository.
AgentControl does not re-execute those tests, has not reviewed their results for this item and does not rely on
them for any requirement: they are not accepted as evidence here. The library does not provide dotnet self-validation,
and this repository holds no separate OTS test project for it. Every requirement of this item is therefore supported
only by the AgentControl integration tests described above.

#### Limitations

- Credential-provider plug-ins. There is no practical, deterministic way to run a real NuGet credential-provider
  plug-in (for example the Azure Artifacts provider) in AgentControl's automated tests. The
  `AgentControl-OTS-NuGetCaching-NonInteractive` requirement is therefore limited to what AgentControl relies on: the
  library, called with interactive plug-ins disabled, obtains credentials from the supplied callback or the
  `nuget.config` and otherwise fails the call instead of waiting for a person. How a real plug-in behaves is not
  exercised by this evidence.
- Callback frequency. The library invokes the credential callback once for each challenged URL and request type, so
  distinct resource URLs of one operation may invoke it separately. AgentControl makes that safe by memoizing the
  answer for the operation (`PackageCredentialCallback`) and scoping it to the feed's origin; the memoization is
  verified under the `PackageCredentials` subsystem, not as a property of the library.
- The server evidence uses plain HTTP on loopback with Basic authentication. Other schemes (for example bearer
  tokens issued by a plug-in) are not exercised.

### Test Scenarios

#### NuGetSourceProvider_ListVersions_FolderFeedWithVersions_ReturnsAllVersionsStampedWithAlias

**Scenario**: A folder feed holds several versions of one package; the provider lists the versions through the
library.

**Expected**: Every version in the feed is returned, proving the library lists a folder feed's versions.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-FolderListing`.

#### NuGetSourceProvider_Acquire_ExistingPackage_ReturnsReaderOverContentFolder

**Scenario**: A listed package version is acquired through the library, which unpacks it into the package cache.

**Expected**: The unpacked package folder exists and its content folder holds the files placed in the package,
proving the library caches the chosen version.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-ListAndCache`.

#### NuGetSourceProvider_Acquire_UsesExplicitAgentControlOwnedRootWithGeneratedConfig

**Scenario**: A package is acquired with a generated configuration in the starting folder that names a private package
cache.

**Expected**: The package is unpacked inside the private package cache named by that configuration, proving the
library honors the cache location of the starting folder's configuration.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-CacheLocation`.

#### NuGetSourceProvider_ListVersions_AnonymousServer_ReturnsVersionsWithoutDownloading

**Scenario**: A package's versions are listed from an anonymous mock NuGet V3 server through the library.

**Expected**: Every version is returned and the mock records no package download, proving the library lists a server's
versions without downloading a package.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-ServerListing`.

#### NuGetSourceProvider_Acquire_AnonymousServer_DownloadsPackageAndExposesContent

**Scenario**: A listed version is acquired from the mock server through the library.

**Expected**: The package is downloaded and unpacked into the private cache and its content folder is readable,
proving the library downloads a chosen version from a server.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-ServerDownload`.

#### NuGetSourceProvider_ListVersions_ServerNeedsSignIn_PromptsOnceAndRemembersCredentialForTheSession

**Scenario**: The mock server answers 401 with a Basic challenge; the per-call credential callback supplies a
credential for the challenged address.

**Expected**: The library invokes the callback, applies its credential to the retried request, and the call succeeds
after a single question, proving the callback mechanism that AgentControl scopes a credential with. The library
invokes the callback once for each challenged URL and request type; the single question here is the outcome of
AgentControl's memoization of the answer, not a guarantee of the library.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-ServerAccess`.

#### NuGetSourceProvider_Acquire_AfterSignInInAnEarlierOperation_ReusesSessionCredentialWithoutPrompting

**Scenario**: After a sign-in through a version listing, the package is downloaded by a second operation on the same
source; its callback supplies the remembered credential.

**Expected**: The download succeeds with the credential from the callback and without a new question, proving the
callback's credential is applied to the download requests as well as to the listing.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-ServerAccess`.

#### NuGetSourceProvider_ListVersions_NuGetConfigHasCredentialsForTheFeed_SignsInWithoutPrompting

**Scenario**: A `nuget.config` found by the library's discovery from the starting folder lists the server's address as
a source with stored credentials, and no credential callback answer is available.

**Expected**: The library signs in with the configured credentials and the call succeeds without any question, proving
the NuGet configuration is honored for a source with the same address and that the call needs no interaction.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-ServerConfiguredCredentials`,
`AgentControl-OTS-NuGetCaching-NonInteractive`.

#### NuGetSourceProvider_ListVersions_SignInTakesTooLong_ThrowsTimedOut

**Scenario**: The callback never answers while a short authentication timeout is set for the call.

**Expected**: The library stops the credential exchange when the timeout elapses and the call ends with a cancellation
that is not the caller's own, proving the per-call time limit is applied.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-AuthenticationTimeout`.

#### NuGetSourceProvider_ListVersions_ServerNeedsSignInAndNoPrompt_ThrowsAuthenticationFailedWithoutAsking

**Scenario**: The mock server answers 401 and no credential is available: no prompt can be shown and neither the
callback nor the configuration holds one.

**Expected**: The library fails the call with its authentication exception and does not wait for anyone, proving a
401 is reported as a refused access and that the library, called with interactive plug-ins disabled, does not wait
for a person.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-UnauthorizedReported`,
`AgentControl-OTS-NuGetCaching-NonInteractive`.

#### NuGetSourceProvider_ListVersions_UserCancelsSignIn_ThrowsSignInCanceledAfterOnePrompt

**Scenario**: The mock server answers 401 and the callback declines to supply a credential.

**Expected**: The library fails the call with its authentication exception after a single request for a credential,
proving a declined challenge is reported as a refused access without a retry loop.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-UnauthorizedReported`.

#### NuGetSourceProvider_ListVersions_ServerAnswersForbiddenAndNoPrompt_ThrowsAuthenticationFailedWithoutAsking

**Scenario**: The mock server answers every request with HTTP 403 and no credential is available.

**Expected**: The library fails the call with its authentication exception, proving a 403 is reported as a refused
access in the same way as a 401.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-ForbiddenReported`.

#### NuGetSourceProvider_ListVersions_WrongCredentials_ThrowsAuthenticationFailedAfterOnePrompt

**Scenario**: The mock server answers 401 and the callback supplies a wrong password.

**Expected**: The library reports a refused access (its authentication exception) after a single request for the
credential instead of retrying in a loop, proving a rejected credential fails the call promptly.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-RejectedCredentialNotRetried`.

#### NuGetSourceProvider_ListVersions_ServerAnswersForbiddenToTheCredential_ThrowsAuthenticationFailedAfterOnePrompt

**Scenario**: The mock server answers every request with HTTP 403, including the one that carries the credential the
callback supplied.

**Expected**: The library reports a refused access after a single request for the credential instead of retrying in a
loop, proving a credential rejected with 403 fails the call promptly too.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-RejectedCredentialNotRetried`.

#### NuGetSourceProvider_Acquire_SignedPackageAndNoPinnedSigner_SignatureIsAccepted

**Scenario**: The real signed package is acquired with signed packages required and a generated configuration whose
trusted-signers list holds only the clear entry.

**Expected**: The package is accepted and unpacked, proving the library accepts a valid signature that chains to a
trusted root.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-SignatureVerification`.

#### NuGetSourceProvider_Acquire_RequireSignedAndPackageUnsigned_IsRejectedAndNothingIsInstalled

**Scenario**: A folder feed holds an unsigned package and the call requires signed packages.

**Expected**: The library refuses the package with an error that identifies a signature failure (which the provider
reports as a signature rejection, not as an unreachable source) and nothing is unpacked into the private cache,
proving an unsigned package is refused on a fresh download and that the refusal identifies a signature failure.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-UnsignedRejected`,
`AgentControl-OTS-NuGetCaching-SignatureFailureIdentified`.

#### NuGetSourceProvider_Acquire_ServerRequiresSignaturesAndPackageUnsigned_IsRejectedAsASignatureFailure

**Scenario**: The mock server serves an unsigned package and the call requires signed packages.

**Expected**: The library refuses the downloaded package and the refusal is reported as a signature failure, proving an
unsigned package from a server is refused.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-UnsignedRejected`.

#### NuGetSourceProvider_Acquire_RequirementEnabledAfterPackageWasCached_RejectsTheCachedPackage

**Scenario**: An unsigned package is acquired without the requirement, so it lands in the cache; the same package is
then acquired again with signed packages required.

**Expected**: The library refuses the cached package, proving the signature is checked for a package already in the
cache and not only on download.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-CachedUnsignedRejected`.

#### NuGetSourceProvider_Acquire_SignedPackageWithAlteredContent_IsRejectedAndNotInstalled

**Scenario**: The real signed package, with one byte of its content flipped after it was signed, is acquired with
signed packages required and no pinned signer.

**Expected**: The library refuses the package as a signature failure and nothing is unpacked, proving a package whose
content no longer matches its signature is refused.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-InvalidSignatureRejected`.

#### NuGetSourceProvider_Acquire_SignedPackageAndOtherSignerPinned_IsRejectedAndNotInstalled

**Scenario**: The real signed package is acquired with a trusted-signers list that holds an author entry for a
different certificate.

**Expected**: The package is refused as a signature failure and not unpacked, proving the library accepts only the
listed signers and identifies the refusal as a signature failure.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-UntrustedSignerRejected`,
`AgentControl-OTS-NuGetCaching-SignatureFailureIdentified`.

#### NuGetSourceProvider_Acquire_SignedPackageAndMatchingAuthorPinned_SignatureIsAccepted

**Scenario**: The real signed package is acquired with a trusted-signers list that holds one author entry, the
fingerprint of the package's author certificate.

**Expected**: The package is accepted, proving the library honors an author entry of the trusted-signers list.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-TrustedSignerAccepted`.

#### NuGetSourceProvider_Acquire_PinnedSignerChangedAfterPackageWasCached_RejectsTheCachedPackage

**Scenario**: The real signed package is acquired with the author trusted, and then acquired again, from the same
cache, after the trusted-signers list was changed to a different signer.

**Expected**: The cached package is refused under the new list, proving the library re-evaluates the trusted signers
for a package already in the cache.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-CachedSignerRechecked`.

#### NuGetSourceProvider_Acquire_UsersConfigurationTrustsTheAuthorButOtherSignerPinned_IsStillRejected

**Scenario**: A `nuget.config` in a folder above the starting folder trusts the package's author, while the
configuration in the starting folder starts its trusted-signers list with a clear entry and lists a different signer.

**Expected**: The package is refused, proving the clear entry stops signers configured elsewhere from applying (the
test fails without the clear entry).

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-OtherConfigurationCannotWidenTrust`.

#### NuGetSourceProvider_Acquire_UsersConfigurationTrustsOnlyAnotherSignerAndNoneIsPinned_SignatureIsAccepted

**Scenario**: A `nuget.config` in a folder above the starting folder trusts only an unrelated certificate, while the
configuration in the starting folder holds only the clear entry.

**Expected**: The validly signed package is accepted, proving a signer list configured elsewhere cannot narrow what the
clear entry leaves open (the test fails without the clear entry).

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-OtherConfigurationCannotNarrowTrust`.
