## NuGetCaching Verification

This document provides the verification evidence for the `NuGetCaching` OTS software item.
Requirements for this OTS item are defined in the NuGetCaching OTS Software Requirements
document.

### Required Functionality

DemaConsulting.NuGet.Caching backs the `NuGetSourceProvider` unit of AgentControl's
`AgentPackageManagement` subsystem: it lists the versions of a package held in a NuGet folder
feed or on a NuGet V3 server and unpacks a chosen version into a package cache whose location is governed by
the configuration found at the starting folder AgentControl supplies. For a server it also obtains
credentials for an address that asks for them through a callback supplied for each call (and from the user's
NuGet configuration and non-interactive credential providers), bounds each credential exchange by a supplied
time, and reports a refused access distinctly. Its credential service is process-wide and is registered by the
library itself. When asked to require signed packages it verifies a package's signature - on a fresh download and
for a package already in the cache - against the trusted signers of the configuration found at the starting folder,
and refuses a package that is unsigned, invalidly signed or signed by an untrusted signer.

### Verification Approach

The library does not provide dotnet self-validation, and the repository holds no separate OTS
test project for it. The evidence is therefore the `NuGetSourceProvider` integration tests, which
exercise the real library against a real NuGet folder feed that the tests create on the fly (a
minimal package archive holding a package manifest and the content folder) and, for server access, against an
in-process NuGet V3 mock feed (`MockNuGetFeed`, Kestrel over plain HTTP, with a Basic-authentication mode) in
`NuGetSourceProviderServerTests.cs`. For signature verification the evidence is of two kinds:
`NuGetSourceProviderSignatureTests.cs` uses unsigned packages and needs no network, and
`NuGetSourceProviderSignedPackageTests.cs` uses a **real signed package** (Newtonsoft.Json, signed by its author and
countersigned by the nuget.org repository) that `SignedNuGetFixture` downloads once per test run and checks against a
pinned SHA-256, so the library's real signature machinery is exercised with a genuine certificate chain on Windows,
Linux and macOS; those tests are skipped - and only then - when nuget.org cannot be reached or the machine's trust
store lacks the roots the package needs, and run with certificate revocation checking set to offline in a
non-parallel collection. Each test uses its
own temporary feed (or its own mock feed on a fresh port) and its own AgentControl-owned starting folder, so the
tests are parallel-safe and never touch the developer's global NuGet cache; the NuGet HTTP cache is isolated
per test process (`HttpCacheIsolation`). This evidence is indirect: it proves the
behavior AgentControl depends on, not the library's complete behavior.

### Test Scenarios

#### NuGetSourceProvider_ListVersions_FolderFeedWithVersions_ReturnsAllVersionsStampedWithAlias

**Scenario**: A folder feed holds several versions of one package; the provider lists the
versions through the library.

**Expected**: Every version in the feed is returned, proving the library lists a folder feed's
versions.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-ListAndCache`.

#### NuGetSourceProvider_Acquire_ExistingPackage_ReturnsReaderOverContentFolder

**Scenario**: A listed package version is acquired through the library, which unpacks it into the
package cache.

**Expected**: The unpacked package folder exists and its content folder holds the files placed in
the package, proving the library caches the chosen version.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-ListAndCache`.

#### NuGetSourceProvider_Acquire_UsesExplicitAgentControlOwnedRootWithGeneratedConfig

**Scenario**: A package is acquired with a generated configuration in the starting folder that
names a private package cache.

**Expected**: The package is unpacked inside the private package cache named by that
configuration, proving the library honors the cache location of the starting folder's
configuration.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-ListAndCache`.

#### NuGetSourceProvider_ListVersions_AnonymousServer_ReturnsVersionsWithoutDownloading

**Scenario**: A package's versions are listed from an anonymous mock NuGet V3 server through the library.

**Expected**: Every version is returned and the mock records no package download, proving the library lists a
server's versions without downloading a package.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-ServerAccess`.

#### NuGetSourceProvider_Acquire_AnonymousServer_DownloadsPackageAndExposesContent

**Scenario**: A listed version is acquired from the mock server through the library.

**Expected**: The package is downloaded and unpacked into the private cache and its content folder is readable,
proving the library downloads a chosen version from a server.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-ServerAccess`.

#### NuGetSourceProvider_ListVersions_ServerNeedsSignIn_PromptsOnceAndRemembersCredentialForTheSession

**Scenario**: The mock server answers 401 with a Basic challenge; the per-call credential callback supplies a
credential for the first challenged address.

**Expected**: The library invokes the callback, applies its credential to the retried request, and the call
succeeds; the callback is not asked again for the same address within the call, proving the callback mechanism
that AgentControl scopes a credential with.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-ServerAccess`.

#### NuGetSourceProvider_ListVersions_WrongCredentials_ThrowsAuthenticationFailedAfterOnePrompt

**Scenario**: The callback supplies a wrong password.

**Expected**: The library reports a refused access (`NuGetAuthenticationException`) after a single request for
the credential instead of retrying in a loop, proving access refusal is reported distinctly and promptly.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-ServerAccess`.

#### NuGetSourceProvider_ListVersions_SignInTakesTooLong_ThrowsTimedOut

**Scenario**: The callback never answers while a short authentication timeout is set for the call.

**Expected**: The library stops the credential exchange when the timeout elapses and the call ends with a
cancellation that is not the caller's own, proving the per-call time limit is applied.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-ServerAccess`.

#### NuGetSourceProvider_ListVersions_NuGetConfigHasCredentialsForTheFeed_SignsInWithoutPrompting

**Scenario**: A `nuget.config` found by the library's discovery from the starting folder lists the server's
address as a source with stored credentials, and no credential callback answer is available.

**Expected**: The library signs in with the configured credentials and the call succeeds without any question,
proving the user's NuGet configuration is honored for a source with the same address.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-ServerAccess`.

#### NuGetSourceProvider_Acquire_RequireSignedAndPackageUnsigned_IsRejectedAndNothingIsInstalled

**Scenario**: A folder feed holds an unsigned package and the call requires signed packages.

**Expected**: The library refuses the package with an error that identifies a signature failure and nothing is
unpacked into the private cache, proving an unsigned package is refused on a fresh download.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-SignatureVerification`.

#### NuGetSourceProvider_Acquire_RequirementEnabledAfterPackageWasCached_RejectsTheCachedPackage

**Scenario**: An unsigned package is acquired without the requirement, so it lands in the cache; the same package is
then acquired again with signed packages required.

**Expected**: The library refuses the cached package, proving the signature is checked for a package already in the
cache and not only on download.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-SignatureVerification`.

#### NuGetSourceProvider_Acquire_SignedPackageAndNoPinnedSigner_SignatureIsAccepted

**Scenario**: The real signed package is acquired with signed packages required and a generated configuration whose
trusted-signers list holds only the clear entry.

**Expected**: The package is accepted and unpacked, proving the library accepts a valid signature that chains to a
trusted root.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-SignatureVerification`.

#### NuGetSourceProvider_Acquire_SignedPackageAndMatchingAuthorPinned_SignatureIsAccepted

**Scenario**: The real signed package is acquired with a trusted-signers list that holds one author entry, the
fingerprint of the package's author certificate.

**Expected**: The package is accepted, proving the library honors an author entry of the trusted-signers list.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-SignatureVerification`.

#### NuGetSourceProvider_Acquire_SignedPackageAndOtherSignerPinned_IsRejectedAndNotInstalled

**Scenario**: The real signed package is acquired with a trusted-signers list that holds an author entry for a
different certificate.

**Expected**: The package is refused and not unpacked, proving the library accepts only the listed signers.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-SignatureVerification`.

#### NuGetSourceProvider_Acquire_UsersConfigurationTrustsTheAuthorButOtherSignerPinned_IsStillRejected

**Scenario**: A `nuget.config` in a folder above the starting folder trusts the package's author, while the
generated configuration in the starting folder starts its trusted-signers list with a clear entry and lists a
different signer.

**Expected**: The package is refused, proving the clear entry stops signers configured elsewhere from applying
(the test fails without the clear entry).

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-SignatureVerification`.

#### NuGetSourceProvider_Acquire_UsersConfigurationTrustsOnlyAnotherSignerAndNoneIsPinned_SignatureIsAccepted

**Scenario**: A `nuget.config` in a folder above the starting folder trusts only an unrelated certificate, while the
generated configuration holds only the clear entry.

**Expected**: The validly signed package is accepted, proving a signer list configured elsewhere cannot narrow what
the clear entry leaves open (the test fails without the clear entry).

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-SignatureVerification`.

#### NuGetSourceProvider_Acquire_PinnedSignerChangedAfterPackageWasCached_RejectsTheCachedPackage

**Scenario**: The real signed package is acquired with the author trusted, and then acquired again, from the same
cache, after the trusted-signers list was changed to a different signer.

**Expected**: The cached package is refused under the new list, proving the library re-evaluates the trusted signers
for a package already in the cache.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-SignatureVerification`.
