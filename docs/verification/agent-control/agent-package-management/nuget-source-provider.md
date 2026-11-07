### NuGetSourceProvider

#### Verification Approach

`NuGetSourceProvider` is verified with unit tests defined in `NuGetSourceProviderTests.cs` (folder sources and
the shared isolation properties) and `NuGetSourceProviderServerTests.cs` (server sources). The
folder tests run the real NuGet caching library against a real NuGet folder feed built on the fly in a
temporary folder (`NuGetFeedBuilder` writes minimal unsigned `.nupkg` files: a nuspec plus content
files), so listing, unpacking, the private cache and the generated configuration are all exercised
for real without any network access. Each test supplies its own per-source cache base folder and
package IDs unique to the test, so tests are parallel-safe and never touch the developer's NuGet
cache. Two properties are verified by inspection of real outcomes rather than mocks: a hostile
repo-level `nuget.config` is left byte-for-byte unchanged and ignored, and the product assembly's
type references are read with `System.Reflection.Metadata` to prove it never references NuGet's
credential-service types.

The server tests run the real NuGet client against `MockNuGetFeed`, a minimal in-process NuGet V3 feed served
by Kestrel over **plain HTTP** on a fresh loopback port per instance (the NuGet client caches service indexes
per address, and plain HTTP avoids any test-certificate trust problem in CI; the sources opt in with
`AllowInsecureHttp`, and the https rule itself is verified with pure unit tests of the address validation). The
mock serves the service index, the flat-container version lists and generated `.nupkg` files in AgentControl's
package layout; in its Basic-authentication mode it answers 401 with `WWW-Authenticate: Basic` for any
request without the matching header, as an Artifactory or Azure Artifacts feed does; it records every request
and `Authorization` header so a test can assert what the client sent; and it can advertise its resources on a
second feed (another origin) to simulate an index that sends the client elsewhere. `HttpCacheIsolation`
gives the test process a private `NUGET_HTTP_CACHE_PATH`, so a port reused across runs is never answered from
a stale on-disk cache. The user is replaced by `FakeCredentialPrompt` (scripted answers, counted questions,
or never answering to simulate a walked-away user), the log by `ListLogger`, and the user's NuGet
configuration by a `nuget.config` written into an ancestor of the source's cache root. The test project
references the `Microsoft.AspNetCore.App` shared framework (part of the .NET runtime, not an OTS NuGet
package) to host the mock feed. Credential tiers are exercised end to end, in order: configuration credentials
(no question), session credential (no question), then exactly one question.

#### Test Environment

Requires the `NUGET_PACKAGES` environment variable to be unset (acquisition deliberately fails
closed when it redirects the package cache outside the per-source folder); symbolic links are not
used by these tests. The server tests need loopback networking and the ASP.NET Core shared framework that the
test project references.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Versions of a package (including prereleases and four-part versions) are listed with the source
  alias; an absent package yields an empty list; a missing feed folder is reported as such;
  cancellation is honored.
- Acquisition yields a reader over the package's `content` folder (managed files, release notes,
  AGENTS.md template), a wrong-layout package is rejected, and the content can be applied to a repo
  through the shared extractor.
- The root folder passed to the library is AgentControl-owned, absolute and distinct per source (a
  relative cache base is refused); two sources with the same package ID and version each yield
  their own content.
- Listing and acquiring leave the process-wide NuGet credential service unchanged.
- Nothing is read from or written to a repo, including a repo `nuget.config`.
- A server lists versions without downloading content and supplies a downloaded package's content.
- A server that needs a sign-in is signed in to from the user's NuGet configuration without a question;
  otherwise the user is asked exactly once, a working credential is remembered for the session and reused
  without a question, and a rejected one is forgotten and asked for again.
- A wrong password, a cancelled dialog, a missing prompt, a sign-in that takes too long and an address that is
  not a feed each produce a distinct failure after a single question, with no retry loop and a message
  without the secret, the path or the query.
- A caller's cancellation while the dialog is open propagates as a cancellation, not a failure.
- A credential is requested from the user only for the feed's own origin.
- A plain-`http` address is refused unless insecure HTTP is allowed; addresses that are not a V3 index or that
  carry a credential are refused without repeating them; relative folders and non-NuGet definitions are
  rejected; a package from another source is refused.
- No sign-in flow, successful or not, writes a secret to the log.

#### Test Scenarios

**NuGetSourceProvider_ListVersions_ReportsVersionsEmptyMissingAndCancellation**: A feed with
several versions (a release, a prerelease, a four-part version) of one package and another
package lists exactly the requested package's versions stamped with the alias; an absent package
returns an empty list; a missing feed folder throws `DirectoryNotFoundException`; an already
canceled token throws `OperationCanceledException`; the configured package IDs are returned
trimmed, de-duplicated and sorted. This scenario is tested by
`NuGetSourceProvider_ListVersions_FolderFeedWithVersions_ReturnsAllVersionsStampedWithAlias`,
`NuGetSourceProvider_ListVersions_PackageNotInFeed_ReturnsEmptyList`,
`NuGetSourceProvider_ListVersions_FolderDoesNotExist_ThrowsDirectoryNotFoundException`,
`NuGetSourceProvider_ListVersions_AlreadyCanceled_ThrowsOperationCanceledException`, and
`NuGetSourceProvider_ListPackageIds_ConfiguredIds_ReturnsDistinctSortedTrimmedIds`, covering
`AgentControl-NuGetSourceProvider-ListVersions`.

**NuGetSourceProvider_Acquire_ReaderOverContentFolderInPrivateCache**: Acquiring a package
returns a reader exposing the managed files, release notes and AGENTS.md template; the same
reader applied through the shared extractor writes only the managed folders to a repo; two feeds
holding the same package ID and version with different content each yield their own content; a
package without a `content` folder is rejected. This scenario is tested by
`NuGetSourceProvider_Acquire_ExistingPackage_ReturnsReaderOverContentFolder`,
`NuGetSourceProvider_Acquire_ThenExtract_WritesManagedFoldersToRepoOnly`,
`NuGetSourceProvider_Acquire_SamePackageInTwoSources_EachSourceGetsItsOwnContent`, and
`NuGetSourceProvider_Acquire_PackageWithoutContentFolder_ThrowsInvalidOperationException`,
covering `AgentControl-NuGetSourceProvider-AcquireToPrivateCache`.

**NuGetSourceProvider_ExplicitRoot_AgentControlOwnedAndGeneratedConfigOnly**: The root folder is
directly under the supplied base, its generated `nuget.config` contains a global-packages-folder
setting and no sources or credentials, and the unpacked package is inside it; the per-source key is
stable for the same alias and location (alias case and trailing separators ignored), differs when
the location or alias differs, and is safe as a folder name; a relative or drive-relative cache base
is refused with `ArgumentException`, and the default base is an absolute path under the per-user
local application-data folder. This scenario is tested by
`NuGetSourceProvider_Acquire_UsesExplicitAgentControlOwnedRootWithGeneratedConfig`,
`NuGetSourceProvider_BuildSourceKey_AliasAndLocation_StableDistinctAndFolderSafe`,
`NuGetSourceProvider_Constructor_RelativeRootBase_ThrowsArgumentException`, and
`NuGetSourceProvider_Constructor_NoRootBase_RootIsAbsoluteUnderLocalApplicationData`, covering
`AgentControl-NuGetSourceProvider-ExplicitCacheRoot`.

**NuGetSourceProvider_NoRepoNuGetConfig_RepoIsNeverReadOrWritten**: With a repo folder holding a
`nuget.config` that tries to redirect the package cache, listing and acquiring leave the repo
holding only that original file, unchanged, create no cache there, and use a root outside the repo.
This scenario is tested by `NuGetSourceProvider_ListAndAcquire_NeverTouchesRepoNuGetConfig`,
covering `AgentControl-NuGetSourceProvider-NoRepoNuGetConfig`.

**NuGetSourceProvider_NoCredentialService_ProcessWideServiceUnchanged**: After listing and
acquiring, the process-wide NuGet credential service is the same instance as before, and the
product assembly's type references contain no credential-service or HTTP-handler-resource type.
This scenario is tested by
`NuGetSourceProvider_ListAndAcquire_LeavesProcessWideCredentialServiceUnchanged` and
`NuGetSourceProvider_ProductAssembly_DoesNotReferenceNuGetCredentialService`, covering `AgentControl-NuGetSourceProvider-NoCredentialServiceRegistration`.

**NuGetSourceProvider_ValidateLocation_RejectsInvalidLocationsAndAcceptsValidHttps**: A plain `http`
server location is rejected with a message naming the source and explaining the opt-in, and accepted when
the source allows insecure HTTP; server locations that the NuGet client would not treat as a V3 index (no
`index.json`, a query string, a fragment) or that carry a credential are refused without repeating the secret;
a valid `https` location is accepted and its cache root is under the supplied base, never the repo or the
current directory; a relative folder is rejected; a zip-type or null definition is rejected. This scenario is
tested by
`NuGetSourceProvider_Constructor_PlainHttpWithoutOptIn_ThrowsInvalidOperationExceptionWithClearMessage`,
`NuGetSourceProvider_Constructor_PlainHttp_RefusedUnlessInsecureHttpIsAllowed`,
`NuGetSourceProvider_Constructor_InvalidServerLocation_ThrowsWithoutRepeatingTheLocation`,
`NuGetSourceProvider_Constructor_HttpsServerLocation_IsAcceptedWithCacheRootUnderBase`,
`NuGetSourceProvider_Constructor_RelativeLocation_ThrowsInvalidOperationException`, and
`NuGetSourceProvider_Constructor_ZipTypeOrNullDefinition_Throws`, covering
`AgentControl-NuGetSourceProvider-ValidateLocation`.

**NuGetSourceProvider_AcquireOnlyFromOwnSource_RefusesForeignPackages**: A package that names a
different source alias is refused with `ArgumentException`. This scenario is tested by
`NuGetSourceProvider_Acquire_PackageFromOtherSource_ThrowsArgumentException`, covering
`AgentControl-NuGetSourceProvider-AcquireOnlyFromOwnSource`.

**NuGetSourceProvider_ServerListAndAcquire_ListsWithoutDownloadingAndSuppliesContent**: Against an anonymous
mock feed the provider lists the versions of a package stamped with the alias and the mock records no package
download; a package the server does not have yields an empty list; acquiring a version downloads it into the
private cache and its content (managed files) is readable. This scenario is tested by
`NuGetSourceProvider_ListVersions_AnonymousServer_ReturnsVersionsWithoutDownloading`,
`NuGetSourceProvider_ListVersions_PackageNotOnServer_ReturnsEmptyList` and
`NuGetSourceProvider_Acquire_AnonymousServer_DownloadsPackageAndExposesContent`, covering
`AgentControl-NuGetSourceProvider-ServerListAndAcquire`.

**NuGetSourceProvider_SignInOrder_ConfigurationThenSessionThenOnePrompt**: Against a feed that requires Basic
sign-in: a `nuget.config` source with the feed's address and stored credentials signs in with no question;
with nothing known the user is asked exactly once, the sign-in succeeds and the credential is in the session
store afterwards; a later operation (the package download, which needs the credential again) uses the session
credential without a question; and an operation with no prompt available (background work) fails with
"needs a sign-in" without asking. This scenario is tested by
`NuGetSourceProvider_ListVersions_NuGetConfigHasCredentialsForTheFeed_SignsInWithoutPrompting`,
`NuGetSourceProvider_ListVersions_ServerNeedsSignIn_PromptsOnceAndRemembersCredentialForTheSession`,
`NuGetSourceProvider_Acquire_AfterSignInInAnEarlierOperation_ReusesSessionCredentialWithoutPrompting` and
`NuGetSourceProvider_ListVersions_ServerNeedsSignInAndNoPrompt_ThrowsAuthenticationFailedWithoutAsking`,
covering `AgentControl-NuGetSourceProvider-SignInOrder`.

**NuGetSourceProvider_CredentialOnlyForFeedOrigin_AnotherOriginNeverPrompts**: The mock feed's service
index advertises its resources on a second mock server (another origin) while the first requires a sign-in;
the user is asked once, for the feed's origin, and never for the other server's origin, whose own different
credentials are never requested from the user. (What the NuGet client itself sends to the second server once the
feed has authenticated is observed behavior of the client and is documented as a limit in the design, not
asserted as a guarantee.) This scenario is tested by
`NuGetSourceProvider_ListVersions_ResourcesOnAnotherOrigin_PromptsOnlyForTheFeedOrigin`, covering
`AgentControl-NuGetSourceProvider-CredentialOnlyForFeedOrigin`.

**NuGetSourceProvider_ServerFailures_DistinctCausesWithSecretFreeMessages**: A wrong password produces
`AuthenticationFailed` after one question with nothing remembered and a message without the password; a
cancelled dialog produces `SignInCanceled` after one question; a remembered credential that the server now
rejects is forgotten and the user is asked again, being told the last one failed; a sign-in that never
completes is stopped after the configured authentication timeout and reported as `TimedOut` (not as the
caller's cancellation); cancelling the caller's token while the dialog is open propagates
`OperationCanceledException`; an address on a live server that has no service index is `Unreachable` with a
message naming the origin but not the path; and the exception type carries its kind, message and inner
exception. This scenario is tested by
`NuGetSourceProvider_ListVersions_WrongCredentials_ThrowsAuthenticationFailedAfterOnePrompt`,
`NuGetSourceProvider_ListVersions_UserCancelsSignIn_ThrowsSignInCanceledAfterOnePrompt`,
`NuGetSourceProvider_ListVersions_SessionCredentialRejected_ForgetsItAndPromptsAgain`,
`NuGetSourceProvider_ListVersions_SignInTakesTooLong_ThrowsTimedOut`,
`NuGetSourceProvider_ListVersions_CallerCancelsWhileSigningIn_ThrowsOperationCanceled`,
`NuGetSourceProvider_ListVersions_AddressIsNotANuGetFeed_ThrowsUnreachable` and
`PackageSourceAccessException_Constructor_CarriesKindMessageAndInnerException`, covering
`AgentControl-NuGetSourceProvider-ServerFailures`. The tests use a short authentication timeout; the product
default is two minutes. An observation recorded while building these tests: a 401 on a non-index resource
after a credential was supplied makes the NuGet client retry with its own back-off (several seconds), which is
why the user interface shows progress and offers cancel.

**NuGetSourceProvider_SecretsNotLogged_SignInFlowsNeverLogTheSecret**: A successful sign-in and a failed one
with a wrong password are run with a recording logger; no recorded line contains the password or its Base64
form, the exception raised for the wrong password does not contain it either, and its inner exception is a
sanitized description rather than the library's own exception. This scenario is tested by
`NuGetSourceProvider_SignInFlows_NeverLogTheSecret` and
`NuGetSourceProvider_ListVersions_WrongCredentials_ThrowsAuthenticationFailedAfterOnePrompt`, covering
`AgentControl-NuGetSourceProvider-SecretsNotLogged`.
