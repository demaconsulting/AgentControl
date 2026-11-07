### SecretRedactingLogger

#### Verification Approach

`SecretRedactingLogger` and its redaction engine are verified with unit tests in `SecretRedactorTests.cs` and
`SecretRedactingLoggerTests.cs`. The static `SecretRedactor.Redact` method is exercised directly with
realistic message text (every authorization scheme, signed and user-information addresses of IPv4, IPv6 and
unusual forms, secret-bearing keys, plain text, null), registered secrets are exercised with unique values,
and the `ILogger` behavior is exercised over `ListLogger`, a logger that records the formatted text, the
structured property values, the scopes and the exception text of every message, so the tests assert exactly
what would reach the application log. Sentinel secrets make any leak visible. The boundary behavior - an
exception carrying a password, an `Authorization` value and a query-bearing address never appears in
`Exception.ToString()`, captured logs or user-visible status text - is verified in
`PackageSourceErrorRedactionTests.cs` against `PackageSourceAccessException`, the Test connection and Select
Package view models, and the repo card.

#### Test Environment

N/A - standard test environment. The tests that register secrets use unique values; the repo card test swaps
the process-wide logger factory and therefore runs in the `RealProcess` collection.

#### Acceptance Criteria

- User information, query string and fragment are removed from every web address - IPv4, IPv6 literal,
  user-information forms with unusual characters, addresses inside larger messages - while scheme, host,
  port, path and the surrounding text stay.
- The complete value of an authorization, proxy-authorization, API-key or cookie header is replaced by a
  placeholder whatever the scheme (basic, bearer, digest, NTLM, negotiate, custom, none); bare credentials and
  secret-bearing keys are replaced.
- A registered secret, its URL-encoded form and its Base64 basic-credential form are removed from any text
  while registered and not after withdrawal; registrations are counted and the registry is thread-safe.
- A message, every structured property value, the scope state and the exception reach the wrapped logger
  redacted; the exception is forwarded only as a sanitized stand-in; level checks are delegated.
- A sanitized exception holds only redacted type, message chain and stack trace; `PackageSourceAccessException`
  keeps only such a stand-in; Test connection, Select Package and the repo card show and log only redacted
  text for an exception that carries secrets.
- Text without secrets, and null, pass through unchanged.

#### Test Scenarios

**SecretRedactingLogger_RemovesUrlSecrets**: Addresses with user information, a query string, a fragment,
IPv6 hosts and unusual user-information characters are reduced to scheme, host, port and path, and text
without secrets is unchanged; addresses inside quoted and JSON strings (serialized with the default and the
relaxed escaping, also nested) with backslash-escaped, `\u0022`-escaped, doubled and entity-style delimiters
lose everything after the path, and the text after the real terminator is kept. This scenario is tested by
`SecretRedactor_Redact_UrlForms_KeepOnlySchemeHostPortAndPath`, `SecretRedactor_Redact_PlainText_IsUnchanged`,
`SecretRedactor_Redact_UrlWithEscapedQuotes_NoSuffixSurvives` and
`SecretRedactor_Redact_JsonSerializedUrl_NeverLeaksQueryOrFragment`, covering
`AgentControl-SecretRedactingLogger-RemovesUrlSecrets`.

**SecretRedactingLogger_RemovesAuthorizationValues**: Header values of every scheme (including unknown and
absent ones, quoted JSON and multi-line text) and bare credentials and secret-bearing keys are replaced by a
placeholder; quoted values containing escaped quotes (`\"`, `\u0022`, `\\"`, doubled quotes, the other quote
character, nested JSON at several levels, produced by hand and by `JsonSerializer`) are replaced up to their
real terminator, with the text after it kept. This scenario is tested by
`SecretRedactor_Redact_AuthorizationHeaders_ValueIsReplacedWhateverTheScheme`,
`SecretRedactor_Redact_HeaderInsideMultiLineText_KeepsOtherLines`,
`SecretRedactor_Redact_BareCredentialsAndSecretKeys_AreReplaced`,
`SecretRedactor_Redact_QuotedValueWithEscapedQuotes_NoSuffixSurvives`,
`SecretRedactor_Redact_QuotedValue_StopsAtTheRealTerminator` and
`SecretRedactor_Redact_JsonSerializedAuthorizationValue_IsRedactedWholeAtEveryNestingLevel`, covering
`AgentControl-SecretRedactingLogger-RemovesAuthorizationValues`.

**SecretRedactingLogger_RemovesKnownSecrets**: A registered password in arbitrary text, a property value and an
exception message (also an inner exception) is removed together with its encoded forms, registrations are
counted, concurrent use is safe, and a lease taken by one holder keeps the secret redacted after the store (or
another operation) withdraws its own registration, until the lease ends. This scenario is tested by
`SecretRedactor_RegisterAndUnregister_ReplacesEveryOccurrenceOfTheSecretWhileRegistered`,
`SecretRedactor_Unregister_SecretRegisteredTwice_StaysRedactedUntilTheLastWithdrawal`,
`SecretRedactor_RegisterAndRedact_FromManyThreads_IsThreadSafe`,
`SecretRedactingLogger_Log_RegisteredPasswordInArbitraryTextPropertiesAndExceptions_IsRemoved`,
`SessionCredentialStore_TryAcquire_LeaseOutlivesTheStoresOwnRegistrationUntilDisposed` and
`PackageCredentialCallback_ConcurrentOperationRejectsCredential_ActiveOperationStaysRedactedUntilItEnds`,
covering `AgentControl-SecretRedactingLogger-RemovesKnownSecrets`.

**SecretRedactingLogger_AppliesToEveryMessage**: A message with secrets, structured properties whose values
carry secrets (with plain value types kept), scope state as text and as pairs, and an exception whose message
holds secrets reach the recording logger redacted; the exception arrives as a sanitized stand-in; level checks
are delegated and a null wrapped logger is refused. This scenario is tested by
`SecretRedactingLogger_Log_MessageAndExceptionWithSecrets_ForwardsRedactedText`,
`SecretRedactingLogger_Log_StructuredPropertyValues_AreRedactedAndStructureIsKept`,
`SecretRedactingLogger_BeginScope_TextAndStructuredState_AreRedacted`,
`SecretRedactingLogger_Log_Exception_IsForwardedAsASanitizedStandIn`,
`SecretRedactingLogger_IsEnabled_DelegatesToTheInnerLogger` and
`SecretRedactor_RedactValue_RedactsTextAndKeepsPlainValueTypes`, covering
`AgentControl-SecretRedactingLogger-AppliesToEveryMessage`.

**SecretRedactingLogger_SanitizesExceptions**: An exception chain (inner and aggregated exceptions, data) with
secrets in every message is described without any of them and without the original object; a thrown
exception keeps its redacted stack trace; a package-source exception keeps only the sanitized inner
exception; the Test connection, Select Package and repo-card text and the card's log hold none of the planted
secrets. This scenario is tested by `SecretRedactor_Sanitize_ExceptionWithSecretsInChain_KeepsOnlyRedactedText`,
`SecretRedactor_Sanitize_ThrownException_KeepsStackTrace`,
`PackageSourceAccessException_InnerExceptionWithSecrets_IsReplacedByASanitizedDescription`,
`SourceEditorViewModel_TestConnection_ExceptionsWithSecrets_ShowOnlyRedactedText`,
`SelectPackageWindowViewModel_SourceAndVersionFailuresWithSecrets_ShowOnlyRedactedText` and
`RepoCardViewModel_PackageSourceFailureWithSecrets_StatusTextAndLogsAreRedacted`, covering
`AgentControl-SecretRedactingLogger-SanitizesExceptions`.
