### NuGetSourceProvider

#### Verification Approach

`NuGetSourceProvider` is verified with unit tests defined in `NuGetSourceProviderTests.cs` (folder sources and
the shared isolation properties), `NuGetSourceProviderServerTests.cs` (server sources, including saved sign-ins and
signature requirements on a server), `NuGetSourceProviderSignatureTests.cs` (signature requirements that need no
network) and `NuGetSourceProviderSignedPackageTests.cs` (signature requirements against a real signed package).
The
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
(no question), session credential (no question), then exactly one question. The user's own credential store (their
Git credential helper) is replaced by `FakeRememberedCredentialStore`, which records every look-up, save and
removal and holds a scripted saved credential, so the saved sign-in is exercised against the real client and server
without any real helper.

**Credential-provider plug-ins are not exercised.** AgentControl runs the NuGet library non-interactively
(`Interactive = false`), so that neither the library nor a credential-provider plug-in it might run can open a
dialog of its own, and supplies credentials from the user's NuGet configuration, the session store, the user's
credential helper and its own prompt. There is no practical, deterministic way to install and run a real NuGet
credential-provider plug-in in the automated tests (it is a separate executable that the NuGet client discovers
on the machine and talks to over a private protocol), so what such a plug-in does when the library runs it is the
behavior of the OTS item and is not verified here; no requirement of this unit depends on it. The tests show the
credentials AgentControl supplies (configuration, session, saved, prompt) and that, when no prompt is available, the
operation fails with an authentication failure and no question. The `Interactive = false` setting itself is a
one-line option verified by review of the design and the code, and is not observed by a test.

The signature tests are of two kinds. `NuGetSourceProviderSignatureTests.cs` needs no network: it builds unsigned
packages with `NuGetFeedBuilder` (a folder feed, or the mock feed for a server) and checks the refusal, the message,
the re-check of a cached package, the fail-closed behavior (the platform answer is injected through the provider's
optional constructor argument) and the content of the generated `nuget.config` read back from the source's private
root. `NuGetSourceProviderSignedPackageTests.cs` uses a **real signed package** - Newtonsoft.Json from nuget.org,
signed by its author and countersigned by the repository - provided by `SignedNuGetFixture`: it is downloaded once per
test run, verified against a pinned SHA-256 (a mismatch fails the tests, since it would mean tampering), kept in a
temporary folder removed at the end of the run, and served to the provider as a folder feed; no binary is checked in.
The tests set `NUGET_CERT_REVOCATION_MODE=offline` for their duration, which is why they share a non-parallel
collection. They show the real signature machinery accepting a valid signature, honoring an author pin typed in a
different spelling, refusing another publisher and the repository countersignature's fingerprint, and - with a
`nuget.config` placed in a folder above the source's cache root - the user's own trusted-signer settings neither
widening a pin nor narrowing an unpinned source (the tests fail without the `<clear />` the provider writes).

#### Test Environment

Requires the `NUGET_PACKAGES` environment variable to be unset (acquisition deliberately fails
closed when it redirects the package cache outside the per-source folder); symbolic links are used only
by the cache-link test, which skips where they cannot be created. The server tests need loopback networking and the
ASP.NET Core shared framework that the
test project references. The real-signed-package tests need access to nuget.org on first use and an operating
system whose trust store holds the roots the package's certificates chain to; they are skipped - and only then -
when nuget.org cannot be reached or those roots are missing (a Linux distribution that dropped the legacy roots, for
example), so they run on the Windows, Ubuntu and macOS builds of the project. The unsigned-package, cached-package,
fail-closed and generated-configuration tests need no network.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Versions of a package (including prereleases and four-part versions) are listed with the source
  alias; an absent package yields an empty list; a missing feed folder is reported as such.
- A token that is already canceled makes listing and acquiring throw `OperationCanceledException` before any
  preflight work: no missing-folder report, no signature-support probe and no cache root or configuration
  created.
- Acquisition yields a reader over the package's `content` folder (managed files, release notes,
  AGENTS.md template), a wrong-layout package is rejected, and the content can be applied to a repo
  through the shared extractor.
- The root folder passed to the library is AgentControl-owned, absolute and distinct per source (a
  relative cache base is refused); two sources with the same package ID and version each yield
  their own content.
- Listing and acquiring leave the process-wide NuGet credential service unchanged.
- The reader reports the SHA-256 identity of the package file, and a package folder whose package file cannot be
  identified is refused.
- Nothing is read from or written to a repo, including a repo `nuget.config`.
- A server lists versions without downloading content and supplies a downloaded package's content.
- A server that needs a sign-in is signed in to from the user's NuGet configuration without a question;
  otherwise the user is asked exactly once, a working credential is remembered for the session and reused
  without a question, and a rejected one is forgotten and asked for again. With no prompt available the
  operation fails with an authentication failure and no question. (Credential-provider plug-ins are not
  exercised; see the Verification Approach.)
- A wrong password, a cancelled dialog, a missing prompt, a sign-in that takes too long and an address that is
  not a feed each produce a distinct failure after a single question, with no retry loop and a message
  without the secret, the path or the query.
- A caller's cancellation while the dialog is open propagates as a cancellation, not a failure.
- A credential is requested from the user only for the feed's own origin.
- A sign-in the user saved with their Git credential helper is used without a question (also with no prompt
  available), looked up through the helper; a saved sign-in the server rejects is removed from the helper and the user
  is asked once; a sign-in is handed to the helper only when the user ticked "Remember" and the operation succeeded,
  never for a wrong password or an unticked choice.
- A source that requires signed packages supplies no unsigned package (folder or server, fresh or cached), accepts a
  validly signed one, accepts only the pinned author when a publisher is pinned (and not the repository
  countersignature), and is not widened or narrowed by the user's own trusted-signer settings; the generated
  configuration holds only the clear entry plus the pinned author, and none for a source that does not require
  signatures; a refusal is a signature failure whose message names the package and source and only the start and end
  of the fingerprint; an unreadable fingerprint stops the source; and on a platform that cannot verify signatures
  listing and acquiring are refused.
- A plain-`http` address is refused unless insecure HTTP is allowed; addresses that are not a V3 index or that
  carry a credential are refused without repeating them; relative folders and non-NuGet definitions are
  rejected; a package from another source is refused.
- No sign-in flow, successful or not, writes a secret to the log.

#### Test Scenarios

**NuGetSourceProvider_ListVersions_ReportsVersionsEmptyAndMissingFolder**: A feed with
several versions (a release, a prerelease, a four-part version) of one package and another
package lists exactly the requested package's versions stamped with the alias; an absent package
returns an empty list; a missing feed folder throws `DirectoryNotFoundException`; the configured package IDs
are returned trimmed, de-duplicated and sorted, and a definition whose package-ID list is null offers no
packages rather than crashing. This scenario is tested by
`NuGetSourceProvider_ListVersions_FolderFeedWithVersions_ReturnsAllVersionsStampedWithAlias`
(`AgentControl-NuGetSourceProvider-ListVersions`),
`NuGetSourceProvider_ListVersions_PackageNotInFeed_ReturnsEmptyList`
(`AgentControl-NuGetSourceProvider-ListVersionsUnknownPackageIsEmpty`),
`NuGetSourceProvider_ListVersions_FolderDoesNotExist_ThrowsDirectoryNotFoundException`
(`AgentControl-NuGetSourceProvider-ListVersionsMissingFolderReported`),
`NuGetSourceProvider_ListPackageIds_ConfiguredIds_ReturnsDistinctSortedTrimmedIds` and
`NuGetSourceProvider_Constructor_NullPackageIds_OffersNoPackages`
(`AgentControl-NuGetSourceProvider-ListPackageIds`).

**NuGetSourceProvider_Cancellation_IsHonoredBeforeAnySideEffect**: A listing with an already canceled token
throws `OperationCanceledException`; a listing and an acquisition with a pre-canceled token throw
`OperationCanceledException` without preparing anything - the cache root and its configuration are not created
and, for a feed folder that does not exist, no missing-folder error is reported instead. (That the preflight runs
on a worker thread so a slow share cannot block the caller is a design property and is not timed by a test.) This
scenario is tested by `NuGetSourceProvider_ListVersions_AlreadyCanceled_ThrowsOperationCanceledException`,
`NuGetSourceProvider_ListVersions_PreCanceledToken_ThrowsOperationCanceledWithoutPreparingAnything` and
`NuGetSourceProvider_Acquire_PreCanceledToken_ThrowsOperationCanceledWithoutPreparingAnything`, covering
`AgentControl-NuGetSourceProvider-CancellationHonoredBeforeSideEffects`.

**NuGetSourceProvider_Acquire_ReaderOverContentFolderInPrivateCache**: Acquiring a package
returns a reader exposing the managed files, release notes and AGENTS.md template; the same
reader applied through the shared extractor writes only the managed folders to a repo; two feeds
holding the same package ID and version with different content each yield their own content, and the
per-source key that names each source's storage is stable for the same alias and location (alias case and
trailing separators ignored), differs when the location or alias differs, and is safe as a folder name; a
package without a `content` folder is rejected. This scenario is tested by
`NuGetSourceProvider_Acquire_ExistingPackage_ReturnsReaderOverContentFolder` and
`NuGetSourceProvider_Acquire_ThenExtract_WritesManagedFoldersToRepoOnly`
(`AgentControl-NuGetSourceProvider-AcquireToPrivateCache`),
`NuGetSourceProvider_Acquire_SamePackageInTwoSources_EachSourceGetsItsOwnContent` and
`NuGetSourceProvider_BuildSourceKey_AliasAndLocation_StableDistinctAndFolderSafe` and
`NuGetSourceProvider_BuildSourceKey_CaseOfLocation_FoldedOnlyWhereComparisonIsCaseInsensitive`
(`AgentControl-NuGetSourceProvider-PrivateCachePerSource`), and
`NuGetSourceProvider_Acquire_PackageWithoutContentFolder_ThrowsInvalidOperationException`
(`AgentControl-NuGetSourceProvider-RejectPackageWithoutContent`). When a package-ID folder inside the private cache
has been replaced by a symbolic link to a copy outside it, acquiring the package is refused with
`InvalidOperationException` naming a link, tested by
`NuGetSourceProvider_Acquire_CacheFolderReplacedByLink_ThrowsInvalidOperationException`
(`AgentControl-NuGetSourceProvider-RejectLinkedCacheFolder`; skipped where symbolic links cannot be created). A
private cache folder that is itself a link is refused before the library is called, leaving the link's target
empty (`NuGetSourceProvider_Acquire_PackagesFolderIsLink_RefusedBeforeAnythingIsWritten`, same requirement).

**NuGetSourceProvider_ExplicitRoot_AgentControlOwnedAndGeneratedConfigOnly**: The root folder is
directly under the supplied base, its generated `nuget.config` contains a global-packages-folder
setting and no sources or credentials, and the unpacked package is inside it; a relative or drive-relative
cache base is refused with `ArgumentException`, and the default base is an absolute path under the per-user
local application-data folder. This scenario is tested by
`NuGetSourceProvider_Acquire_UsesExplicitAgentControlOwnedRootWithGeneratedConfig`
(`AgentControl-NuGetSourceProvider-ExplicitCacheRoot`),
`NuGetSourceProvider_Constructor_RelativeRootBase_ThrowsArgumentException` and
`NuGetSourceProvider_Constructor_NoRootBase_RootIsAbsoluteUnderLocalApplicationData`
(`AgentControl-NuGetSourceProvider-CacheRootMustBeAbsolute`).

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
`NuGetSourceProvider_Constructor_PlainHttpWithoutOptIn_ThrowsInvalidOperationExceptionWithClearMessage` and
`NuGetSourceProvider_Constructor_PlainHttp_RefusedUnlessInsecureHttpIsAllowed`
(`AgentControl-NuGetSourceProvider-PlainHttpRefusedUnlessAllowed`),
`NuGetSourceProvider_Constructor_InvalidServerLocation_ThrowsWithoutRepeatingTheLocation`
(`AgentControl-NuGetSourceProvider-ServerAddressMustBeServiceIndex` and
`AgentControl-NuGetSourceProvider-CredentialInAddressRefused`),
`NuGetSourceProvider_Constructor_HttpsServerLocation_IsAcceptedWithCacheRootUnderBase`
(`AgentControl-NuGetSourceProvider-HttpsAddressAccepted`),
`NuGetSourceProvider_Constructor_RelativeLocation_ThrowsInvalidOperationException`
(`AgentControl-NuGetSourceProvider-ValidateLocation`), and
`NuGetSourceProvider_Constructor_ZipTypeOrNullDefinition_Throws`
(`AgentControl-NuGetSourceProvider-RejectNonNuGetDefinition`).

**NuGetSourceProvider_AcquireOnlyFromOwnSource_RefusesForeignPackages**: A package that names a
different source alias is refused with `ArgumentException`. This scenario is tested by
`NuGetSourceProvider_Acquire_PackageFromOtherSource_ThrowsArgumentException`, covering
`AgentControl-NuGetSourceProvider-AcquireOnlyFromOwnSource`.

**NuGetSourceProvider_ServerListAndAcquire_ListsWithoutDownloadingAndSuppliesContent**: Against an anonymous
mock feed the provider lists the versions of a package stamped with the alias and the mock records no package
download; a package the server does not have yields an empty list; acquiring a version downloads it into the
private cache and its content (managed files) is readable. This scenario is tested by
`NuGetSourceProvider_ListVersions_AnonymousServer_ReturnsVersionsWithoutDownloading`
(`AgentControl-NuGetSourceProvider-ServerListAndAcquire` and
`AgentControl-NuGetSourceProvider-ServerListDoesNotDownload`),
`NuGetSourceProvider_ListVersions_PackageNotOnServer_ReturnsEmptyList`
(`AgentControl-NuGetSourceProvider-ServerUnknownPackageIsEmpty`) and
`NuGetSourceProvider_Acquire_AnonymousServer_DownloadsPackageAndExposesContent`
(`AgentControl-NuGetSourceProvider-ServerAcquire`).

**NuGetSourceProvider_SignInOrder_ConfigurationThenSessionThenOnePrompt**: Against a feed that requires Basic
sign-in: a `nuget.config` source with the feed's address and stored credentials signs in with no question;
with nothing known the user is asked exactly once, the sign-in succeeds and the credential is in the session
store afterwards; a later operation (the package download, which needs the credential again) uses the session
credential without a question; and an operation with no prompt available (background work) fails with
"needs a sign-in" without asking. This scenario is tested by
`NuGetSourceProvider_ListVersions_NuGetConfigHasCredentialsForTheFeed_SignsInWithoutPrompting`,
`NuGetSourceProvider_ListVersions_ServerNeedsSignIn_PromptsOnceAndRemembersCredentialForTheSession` and
`NuGetSourceProvider_Acquire_AfterSignInInAnEarlierOperation_ReusesSessionCredentialWithoutPrompting`,
covering `AgentControl-NuGetSourceProvider-SignInOrder`; by
`NuGetSourceProvider_ListVersions_ServerNeedsSignIn_PromptsOnceAndRemembersCredentialForTheSession` and
`NuGetSourceProvider_ListVersions_WrongCredentials_ThrowsAuthenticationFailedAfterOnePrompt`, covering
`AgentControl-NuGetSourceProvider-AskUserOnce` and `AgentControl-NuGetSourceProvider-CredentialRememberedAfterSuccess`
(the wrong password is asked for once and nothing is remembered); and by
`NuGetSourceProvider_ListVersions_ServerNeedsSignInAndNoPrompt_ThrowsAuthenticationFailedWithoutAsking`, covering
`AgentControl-NuGetSourceProvider-NonInteractiveLibrary` (with no prompt the operation fails with an
authentication failure and nothing asks; the `Interactive = false` option and credential-provider plug-ins
are not observed, see the Verification Approach).

**NuGetSourceProvider_CredentialOnlyForFeedOrigin_AnotherOriginNeverPrompts**: The mock feed's service
index advertises its resources on a second mock server (another origin) while the first requires a sign-in;
the user is asked once, for the feed's origin, and never for the other server's origin, whose own different
credentials are never requested from the user. (What the NuGet client itself sends to the second server once the
feed has authenticated is observed behavior of the client and is documented as a limit in the design, not
asserted as a guarantee.) This scenario is tested by
`NuGetSourceProvider_ListVersions_ResourcesOnAnotherOrigin_PromptsOnlyForTheFeedOrigin`, covering
`AgentControl-NuGetSourceProvider-CredentialOnlyForFeedOrigin`.

**NuGetSourceProvider_ServerRefusesAccess_AccessDeniedWithoutPrompting**: When the NuGet library reports that the
server answered HTTP 403 (the `NuGetAuthenticationException` has `StatusCode` 403 and, when the server explained
itself, a `ServerReason`; the message is not read), the operation ends as `AccessDenied` at the silent attempt: the
prompt is never asked (the library does not even consult the credential callback for a 403), a
saved or session sign-in that was used is kept, the message reads "Signed in, but 'host' refused access: reason"
(secret-redacted, one line, at most 300 characters) with a hint to ask the feed's administrator for read permission,
and a warning with the reason is logged; without a reason the message gives the status. A 401 is unchanged: the user
is asked once and the second attempt's result is returned; a 403 that follows a sign-in (the 401 was answered by the
prompt) ends as `AccessDenied` after that one question without keeping the typed credential. These are tested at
the unit level, with a scripted library call (`RunAsync`), by
`NuGetSourceProvider_RunAsync_ForbiddenWithSavedSignIn_ReportsAccessDeniedWithoutPromptingOrForgetting`,
`NuGetSourceProvider_RunAsync_ForbiddenWithoutReason_ReportsAccessDeniedWithTheStatus`,
`NuGetSourceProvider_RunAsync_Unauthorized_StillPromptsOnce`,
`NuGetSourceProvider_RunAsync_UnauthorizedThenForbiddenAfterSignIn_ReportsAccessDeniedAfterOnePrompt` and
`NuGetSourceProvider_IsAccessDenied_StatusAndReason_DetectsForbiddenAndSanitizesReason` (which also checks that a
message that merely talks about a 403 is not one), `NuGetSourceProvider_IsAccessDenied_UrlsInReason_ReducedToSchemeHostAndPort`
(each URL in the reason, with path, query, fragment or user information, becomes scheme, host and port, and another or
unreadable URL becomes `[link]`) and `NuGetSourceProvider_RunAsync_ForbiddenReasonHasUrl_MessageAndLogShowOnlyOrigin`
(the message and the warning log show only the origin) and
`NuGetSourceProvider_RunAsync_Forbidden_KeepsNoInnerExceptionAndNoFeedPath` (an access denial, on the first attempt
and after a sign-in, has no inner exception and no feed path anywhere in its `ToString()`; the end-to-end
`...ServerAnswersForbiddenAndNoPrompt...` test asserts the same against the mock feed); and end to end against the in-process
mock server answering 403 (with an `X-TFS-ServiceError` header, and with no prompt or with a prompt that is never
asked) by
`NuGetSourceProvider_ListVersions_ServerExplainsForbidden_ShowsReasonWithoutPromptingOrForgetting`,
`NuGetSourceProvider_ListVersions_ServerAnswersForbiddenAndNoPrompt_ThrowsAccessDeniedWithoutAsking` and
`NuGetSourceProvider_ListVersions_ServerAnswersForbiddenToTheCredential_ThrowsAccessDeniedWithoutPrompting`. Covering
`AgentControl-NuGetSourceProvider-AccessDenied`.

**NuGetSourceProvider_ServerFailures_DistinctCausesWithSecretFreeMessages**: A wrong password produces
`AuthenticationFailed` after one question with nothing remembered and a message without the password; a
cancelled dialog produces `SignInCanceled` after one question; a remembered credential that the server now
rejects is forgotten and the user is asked again, being told the last one failed; a sign-in that never
completes is stopped after the configured authentication timeout and reported as `TimedOut` (not as the
caller's cancellation); canceling the caller's token while the dialog is open propagates
`OperationCanceledException`; an address on a live server that has no service index is `Unreachable` with a
message naming the origin but not the path; and the exception type carries its kind, message and inner
exception. This scenario is tested by
`NuGetSourceProvider_ListVersions_WrongCredentials_ThrowsAuthenticationFailedAfterOnePrompt`,
`NuGetSourceProvider_ListVersions_UserCancelsSignIn_ThrowsSignInCanceledAfterOnePrompt`,
`NuGetSourceProvider_ListVersions_AddressIsNotANuGetFeed_ThrowsUnreachable` and
`PackageSourceAccessException_Constructor_CarriesKindMessageAndInnerException`, covering
`AgentControl-NuGetSourceProvider-ServerFailures`; by
`NuGetSourceProvider_ListVersions_SessionCredentialRejected_ForgetsItAndPromptsAgain`, covering
`AgentControl-NuGetSourceProvider-RejectedCredentialForgotten`; by
`NuGetSourceProvider_ListVersions_SignInTakesTooLong_ThrowsTimedOut`, covering
`AgentControl-NuGetSourceProvider-SignInTimeLimit`; and by
`NuGetSourceProvider_ListVersions_CallerCancelsWhileSigningIn_ThrowsOperationCanceled`, covering
`AgentControl-NuGetSourceProvider-CallerCancellationNotAFailure`. The tests use a short authentication timeout; the product
default is two minutes. An observation recorded while building these tests: a 401 on a non-index resource
after a credential was supplied makes the NuGet client retry with its own back-off (several seconds), which is
why the user interface shows progress and offers cancel.

**NuGetSourceProvider_SecretsNotLogged_SignInFlowsNeverLogTheSecret**: A successful sign-in and a failed one
with a wrong password are run with a recording logger; no recorded line contains the password or its Base64
form, the exception raised for the wrong password does not contain it either, and its inner exception is a
sanitized description rather than the library's own exception. This scenario is tested by
`NuGetSourceProvider_SignInFlows_NeverLogTheSecret`, covering
`AgentControl-NuGetSourceProvider-SecretsNotLogged`, and by `NuGetSourceProvider_SignInFlows_NeverLogTheSecret` and
`NuGetSourceProvider_ListVersions_WrongCredentials_ThrowsAuthenticationFailedAfterOnePrompt`, covering
`AgentControl-NuGetSourceProvider-SecretsNotInExceptions`.

**NuGetSourceProvider_SavedSignIn_UsedWithoutAskingAndKeptOnlyWhenChosen**: With a sign-in saved in the (fake) user's
credential store, a listing against a feed that needs a sign-in succeeds without any question, the credential store
having been asked once; a background check with no prompt available succeeds the same way; when the server rejects
the saved sign-in it is removed from the credential store and the user is asked exactly once; when the user ticks
"Remember" the credential is handed to the credential store only after the operation succeeded; a wrong password, or
a correct one without the tick, is never saved. This scenario is tested by
`NuGetSourceProvider_ListVersions_SavedSignInExists_UsedWithoutAsking` and
`NuGetSourceProvider_ListVersions_NoPromptButSavedSignIn_BackgroundCheckSucceeds`, covering
`AgentControl-NuGetSourceProvider-SavedSignIn`; by
`NuGetSourceProvider_ListVersions_SavedSignInExists_UsedWithoutAsking` (the credential store is asked once),
covering `AgentControl-NuGetSourceProvider-SavedSignInLookedUpOnce`; by
`NuGetSourceProvider_ListVersions_SavedSignInRejected_RemovedFromTheUsersStoreAndUserAskedOnce`, covering
`AgentControl-NuGetSourceProvider-RejectedSavedSignInRemoved`; and by
`NuGetSourceProvider_ListVersions_RememberTicked_SavedByTheUsersStoreAfterSuccess` and
`NuGetSourceProvider_ListVersions_WrongPasswordOrRememberNotTicked_NothingIsSaved`, covering
`AgentControl-NuGetSourceProvider-SignInSavedOnlyWhenChosen`.

**NuGetSourceProvider_RequireSigned_UnsignedAndCachedPackagesAreRefused**: An unsigned package from a source that
requires signatures is refused as a signature failure with a message that names the package, the version and the
source, with a sanitized inner exception, and nothing is installed; the same package is accepted when signatures are
not required; turning the requirement on after the package was cached refuses the cached package; a server source
that requires signatures refuses an unsigned package as a signature failure (not as an unreachable or unauthorized
server); and the real signed package is accepted when no publisher is pinned. This scenario is tested by
`NuGetSourceProvider_Acquire_RequireSignedAndPackageUnsigned_IsRejectedAndNothingIsInstalled` and
`NuGetSourceProvider_Acquire_ServerRequiresSignaturesAndPackageUnsigned_IsRejectedAsASignatureFailure`
(`AgentControl-NuGetSourceProvider-RequireSigned`),
`NuGetSourceProvider_Acquire_RequirementEnabledAfterPackageWasCached_RejectsTheCachedPackage`
(`AgentControl-NuGetSourceProvider-SignatureCheckedForCachedPackage`),
`NuGetSourceProvider_Acquire_SignedPackageAndNoPinnedSigner_SignatureIsAccepted`
(`AgentControl-NuGetSourceProvider-SignedPackageAccepted`) and
`NuGetSourceProvider_Acquire_SignaturesNotRequired_UnsignedPackageIsAccepted`
(`AgentControl-NuGetSourceProvider-UnsignedAcceptedWhenNotRequired`).

**NuGetSourceProvider_SignerPin_OnlyThePinnedAuthorIsAccepted**: With the real signed package, pinning its author
certificate (however the fingerprint is typed) accepts it; pinning another fingerprint rejects it and installs
nothing, with the "not signed by the trusted publisher" message; pinning the repository countersignature's
certificate, which is not the author, rejects it; and after the author's pin let the package in, changing the pin
rejects the package now in the cache. This scenario is tested by
`NuGetSourceProvider_Acquire_SignedPackageAndMatchingAuthorPinned_SignatureIsAccepted` and
`NuGetSourceProvider_Acquire_SignedPackageAndOtherSignerPinned_IsRejectedAndNotInstalled`
(`AgentControl-NuGetSourceProvider-SignerPin`),
`NuGetSourceProvider_Acquire_RepositoryCertificatePinned_IsNotTheAuthorAndIsRejected`
(`AgentControl-NuGetSourceProvider-RepositoryCountersignatureNotAPin`) and
`NuGetSourceProvider_Acquire_PinnedSignerChangedAfterPackageWasCached_RejectsTheCachedPackage`
(`AgentControl-NuGetSourceProvider-ChangedPinAppliesToCachedPackages`).

**NuGetSourceProvider_GeneratedSignerConfiguration_ClearsInheritedSignersAndPinsOneAuthor**: A pinned source's
generated `nuget.config`, read back from the source's private root, holds a `trustedSigners` section with the
clear entry (`<clear />`) and exactly one author entry with the normalized fingerprint, SHA-256 and no trust for
untrusted roots, next to the private packages folder, and writes no signature mode and no sources; a source that
requires signatures without a pin gets only the clear entry; a source that does not require signatures gets no
section even if a fingerprint remains in its settings; changing the pin rewrites the file in the same root. This
scenario is tested by `NuGetSourceProvider_PinnedSigner_GeneratesTrustedSignersWithOnlyThatAuthor`,
`NuGetSourceProvider_RequireSignedWithoutPin_ClearsInheritedSignersAndNoRequirementWritesNone` and
`NuGetSourceProvider_PinnedSignerChanged_RewritesTheConfigurationInTheSameRoot`, covering
`AgentControl-NuGetSourceProvider-GeneratedSignerConfiguration`, and by
`NuGetSourceProvider_RequireSignedWithoutPin_ClearsInheritedSignersAndNoRequirementWritesNone`, covering
`AgentControl-NuGetSourceProvider-NoSignerConfigurationWithoutRequirement`. That the NuGet library honors the
trusted-signers it finds is a property of the OTS item (see its verification design); this unit's own contribution -
the generated configuration - is what these tests read back.

**NuGetSourceProvider_SignerTrustIsolated_OnlyTheSourcesOwnDescriptionDecides**: With a user-level
configuration in a folder above the cache root, a configuration that trusts the package's author does not rescue a
mismatching pin, and one that trusts only an unrelated certificate does not make an unpinned source reject a validly
signed package (both tests fail without the clear entry). This scenario is tested by
`NuGetSourceProvider_Acquire_UsersConfigurationTrustsTheAuthorButOtherSignerPinned_IsStillRejected` and
`NuGetSourceProvider_Acquire_UsersConfigurationTrustsOnlyAnotherSignerAndNoneIsPinned_SignatureIsAccepted`, covering
`AgentControl-NuGetSourceProvider-SignerTrustIsolated`.

**NuGetSourceProvider_SignatureFailureReported_NamesPackageSourceAndPinnedPublisher**: The refusal of an unsigned
package carries the "signature rejected" kind, a message naming the package, version and source and saying the source
requires signed packages; for a pinned source the message says the package is not signed by the trusted publisher
and names the start and end of the fingerprint, never the whole of it. This scenario is tested by
`NuGetSourceProvider_Acquire_RequireSignedAndPackageUnsigned_IsRejectedAndNothingIsInstalled` and
`NuGetSourceProvider_Acquire_ServerRequiresSignaturesAndPackageUnsigned_IsRejectedAsASignatureFailure`, covering
`AgentControl-NuGetSourceProvider-SignatureFailureReported`, and by
`NuGetSourceProvider_Acquire_PinnedSignerAndPackageUnsigned_MessageNamesTheTrustedPublisher`, covering
`AgentControl-NuGetSourceProvider-SignatureFailureNamesPinnedPublisher`.

**NuGetSourceProvider_FailClosedWithoutVerification_UnsupportedPlatformRefusesRequiringSources**: With the platform
answer injected as "unsupported" - or with a probe that throws - a source that requires signatures refuses to list
and refuses to acquire, with a message that says why, and creates nothing, without the probe's failure escaping; a
source that does not require signatures works as usual. This scenario is tested by
`NuGetSourceProvider_RequireSignedOnUnsupportedPlatform_FailsClosedForListAndAcquire` and
`NuGetSourceProvider_RequireSignedAndProbeFails_FailsClosedWithoutThrowingFromTheProbe`, covering
`AgentControl-NuGetSourceProvider-FailClosedWithoutVerification`, and by
`NuGetSourceProvider_SignaturesNotRequiredOnUnsupportedPlatform_WorksAsUsual`, covering
`AgentControl-NuGetSourceProvider-UnsupportedPlatformDoesNotAffectOpenSources`.

**NuGetSourceProvider_UnreadableFingerprint_RefusedWhenSignaturesAreRequired**: A fingerprint that is not a SHA-256
fingerprint stops a source that requires signatures at construction, with a message that does not repeat the text,
and is irrelevant for a source that does not require them. This scenario is tested by
`NuGetSourceProvider_Constructor_InvalidFingerprint_RefusesWhenSignaturesAreRequired`, covering
`AgentControl-NuGetSourceProvider-UnreadableFingerprintRefused`.

**NuGetSourceProvider_ReportContentIdentity_IsTheFilesIdentityOfTheCachedCopy**: An acquired package's reader reports
the `files-sha256:` identity of the agent files of the copy unpacked in the private cache - not of the `.nupkg` file -
for a folder source and for a server source (the `.nupkg` downloaded into the private cache); two
versions of one package report the identity of their own files and differ; and a second acquisition of the same version
reports the identity of the copy held in the private cache even though the feed's file was replaced in between - the
identity describes the local copy that is unpacked and applied (NuGet versions are immutable by convention, and the
cache is what spares a second download), which the user guide states. The folder-source test also compares two feeds
that hold the same agent files in packages that differ in bytes (another nuspec, other release notes, unrelated
entries) and expects the same identity. This scenario is tested by
`NuGetSourceProvider_Acquire_ExistingPackage_ReportsFilesIdentityIndependentOfNupkg`,
`NuGetSourceProvider_Acquire_TwoVersions_ReportDifferentIdentities`,
`NuGetSourceProvider_Acquire_SameVersionTwice_ReportsIdentityOfTheCachedCopy` and
`NuGetSourceProvider_Acquire_AnonymousServer_ReportsFilesIdentityOfDownloadedPackage`, covering
`AgentControl-NuGetSourceProvider-ReportContentIdentity`.
