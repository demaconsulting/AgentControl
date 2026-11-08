### SecretRedactingLogger

#### Verification Approach

`SecretRedactingLogger` and its redaction engine are verified with unit tests in `SecretRedactorTests.cs` and
`SecretRedactingLoggerTests.cs`. The static `SecretRedactor.Redact` method is exercised directly with
realistic message text (every authorization scheme, signed and user-information addresses of IPv6 and
unusual forms, addresses in parentheses and Markdown links, secret-bearing keys, plain text, null), registered
secrets are exercised with unique values, and the `ILogger` behavior is exercised over `ListLogger`, a logger
that records the formatted text, the structured property values, the scopes and the exception text of every
message, so the tests assert exactly what would reach the application log. Sentinel secrets make any leak
visible. Evidence that spans units - the redaction working together with the callback and the session store - is
verified at the `PackageCredentials` subsystem level (`PackageCredentialsSubsystemTests.cs`), and the
boundary behavior of an exception carrying a password, an `Authorization` value and a query-bearing address in
`PackageSourceAccessException`, the Test connection and Select Package view models and the repo card is verified
in `PackageSourceErrorRedactionTests.cs` for the subsystems that own those items; neither is evidence for the
unit requirements below.

#### Test Environment

N/A - standard test environment. The tests that register secrets use unique values.

#### Acceptance Criteria

- The query string, the fragment and the user information (user name and password) are each removed from every
  web address - host name or IPv6 literal, addresses inside larger messages, inside quoted or JSON strings
  with escaped delimiters, inside parentheses and Markdown links - while scheme, host, port, path and the
  surrounding text stay and text without secrets, and null, pass through unchanged.
- The complete value of an authorization, proxy-authorization, API-key or cookie header is replaced by a
  placeholder whatever the scheme (basic, bearer, digest, NTLM, negotiate, custom, none) with the header name
  and the other lines kept; bare credentials and secret-bearing keys are replaced.
- A quoted header or key value is replaced up to its real closing quote whatever the form of its quote
  delimiters (plain, backslash-escaped, `\uXXXX`-escaped, entity, the default escaping of `System.Text.Json`)
  and at every level of JSON nesting, with the text after it kept.
- A registered secret, its URL-encoded form and its Base64 basic-credential form are removed from any text
  while registered and not after withdrawal; registrations are counted per holder and the registry is
  thread-safe.
- A message, every structured property value and the scope state reach the wrapped logger redacted; the
  exception is forwarded only as a sanitized stand-in; level checks are delegated.
- A sanitized exception holds only a redacted type name, message chain and stack trace and no original,
  inner exception or data, and its constructor redacts whatever it is given.

#### Test Scenarios

**SecretRedactingLogger_RemovesUrlSecrets**: Addresses with a signed query string are reduced to scheme, host,
port and path, in plain text, inside quoted and JSON strings (serialized with the default and the relaxed
escaping, also nested) with backslash-escaped, `\u0022`-escaped and doubled delimiters, inside parentheses
and inside a Markdown link. This scenario is tested by
`SecretRedactor_Redact_UrlForms_KeepOnlySchemeHostPortAndPath`,
`SecretRedactor_Redact_UrlWithEscapedQuotes_NoSuffixSurvives`,
`SecretRedactor_Redact_JsonSerializedUrl_NeverLeaksQueryOrFragment`,
`SecretRedactor_Redact_UrlInParentheses_RedactsQueryAndKeepsClosingParenthesis`,
`SecretRedactor_Redact_UrlWithBalancedParenthesesInPathAndQuery_RedactsWholeQueryAndFragment` and
`SecretRedactor_Redact_MarkdownLinkWithUserInfoAndQuery_RedactsUserInfoQueryAndFragment`, covering
`AgentControl-SecretRedactingLogger-RemovesUrlSecrets`.

**SecretRedactingLogger_RemovesUrlUserInfo**: User names and passwords before the host are removed, also when
the password contains `/`, `?` or `#`, and inside a Markdown link. This scenario is tested by
`SecretRedactor_Redact_UrlForms_KeepOnlySchemeHostPortAndPath` and
`SecretRedactor_Redact_MarkdownLinkWithUserInfoAndQuery_RedactsUserInfoQueryAndFragment`, covering
`AgentControl-SecretRedactingLogger-RemovesUrlUserInfo`.

**SecretRedactingLogger_RemovesUrlFragment**: Fragments are removed from plain, JSON-serialized, balanced
parenthesis and Markdown-link addresses. This scenario is tested by
`SecretRedactor_Redact_UrlForms_KeepOnlySchemeHostPortAndPath`,
`SecretRedactor_Redact_JsonSerializedUrl_NeverLeaksQueryOrFragment`,
`SecretRedactor_Redact_UrlWithBalancedParenthesesInPathAndQuery_RedactsWholeQueryAndFragment` and
`SecretRedactor_Redact_MarkdownLinkWithUserInfoAndQuery_RedactsUserInfoQueryAndFragment`, covering
`AgentControl-SecretRedactingLogger-RemovesUrlFragment`.

**SecretRedactingLogger_KeepsUrlLocation**: The scheme, host, port and path of the address are unchanged, and an
address without secrets passes through as it is. This scenario is tested by
`SecretRedactor_Redact_UrlForms_KeepOnlySchemeHostPortAndPath`,
`SecretRedactor_Redact_PlainText_IsUnchanged` and
`SecretRedactor_Redact_UrlInParentheses_RedactsQueryAndKeepsClosingParenthesis`, covering
`AgentControl-SecretRedactingLogger-KeepsUrlLocation`.

**SecretRedactingLogger_KeepsSurroundingText**: Text before and after an address, trailing sentence
punctuation, a closing parenthesis that surrounds the address and text without secrets, null and empty text
are unchanged. This scenario is tested by `SecretRedactor_Redact_UrlForms_KeepOnlySchemeHostPortAndPath`,
`SecretRedactor_Redact_PlainText_IsUnchanged` and
`SecretRedactor_Redact_UrlInParentheses_RedactsQueryAndKeepsClosingParenthesis`, covering
`AgentControl-SecretRedactingLogger-KeepsSurroundingText`.

**SecretRedactingLogger_RecognizesIpv6Literal**: Addresses with an IPv6 literal host, with and without a port,
lose their query string and fragment. This scenario is tested by
`SecretRedactor_Redact_UrlForms_KeepOnlySchemeHostPortAndPath`, covering
`AgentControl-SecretRedactingLogger-RecognizesIpv6Literal`.

**SecretRedactingLogger_RedactsUrlInsideEscapedQuotes**: Addresses inside quoted and JSON strings lose
everything after the path although a backslash-escaped, `\u0022`-escaped, doubled or entity-style delimiter
follows the query string; the text after the real terminator is kept. This scenario is tested by
`SecretRedactor_Redact_UrlWithEscapedQuotes_NoSuffixSurvives` and
`SecretRedactor_Redact_JsonSerializedUrl_NeverLeaksQueryOrFragment`, covering
`AgentControl-SecretRedactingLogger-RedactsUrlInsideEscapedQuotes`.

**SecretRedactingLogger_RedactsUrlInsideParentheses**: An address wrapped in parentheses loses its query string
and keeps the closing parenthesis; an address with balanced parentheses in its path and query string loses the
whole query string and fragment; a Markdown link loses its user information, query string and fragment. This
scenario is tested by `SecretRedactor_Redact_UrlForms_KeepOnlySchemeHostPortAndPath`,
`SecretRedactor_Redact_UrlInParentheses_RedactsQueryAndKeepsClosingParenthesis`,
`SecretRedactor_Redact_UrlWithBalancedParenthesesInPathAndQuery_RedactsWholeQueryAndFragment` and
`SecretRedactor_Redact_MarkdownLinkWithUserInfoAndQuery_RedactsUserInfoQueryAndFragment`, covering
`AgentControl-SecretRedactingLogger-RedactsUrlInsideParentheses`.

**SecretRedactingLogger_RemovesAuthorizationValues**: Authorization, proxy-authorization, API-key and cookie
header values, also in quoted JSON and in multi-line text, are replaced by a placeholder. This scenario is tested
by `SecretRedactor_Redact_AuthorizationHeaders_ValueIsReplacedWhateverTheScheme` and
`SecretRedactor_Redact_HeaderInsideMultiLineText_KeepsOtherLines`, covering
`AgentControl-SecretRedactingLogger-RemovesAuthorizationValues`.

**SecretRedactingLogger_ReplacesValueWhateverScheme**: Basic, bearer, digest, NTLM, negotiate, AWS-style,
unknown, custom and absent schemes all lose their complete value. This scenario is tested by
`SecretRedactor_Redact_AuthorizationHeaders_ValueIsReplacedWhateverTheScheme`, covering
`AgentControl-SecretRedactingLogger-ReplacesValueWhateverScheme`.

**SecretRedactingLogger_KeepsHeaderNameAndOtherLines**: Only the header value goes; the header name and the
lines around it are untouched. This scenario is tested by
`SecretRedactor_Redact_HeaderInsideMultiLineText_KeepsOtherLines`, covering
`AgentControl-SecretRedactingLogger-KeepsHeaderNameAndOtherLines`.

**SecretRedactingLogger_RemovesBareCredentials**: A basic or bearer credential and a JSON web token without a
header around them are replaced. This scenario is tested by
`SecretRedactor_Redact_BareCredentialsAndSecretKeys_AreReplaced`, covering
`AgentControl-SecretRedactingLogger-RemovesBareCredentials`.

**SecretRedactingLogger_RemovesSecretBearingKeys**: The values of keys such as `password`, `token` and `sig`
are replaced, in plain text and in JSON with escaped quotes, with the serializer's default escaping, with
`\uXXXX`-escaped delimiters and in nested JSON text. This scenario is tested by
`SecretRedactor_Redact_BareCredentialsAndSecretKeys_AreReplaced`,
`SecretRedactor_Redact_QuotedValueWithEscapedQuotes_NoSuffixSurvives`,
`SecretRedactor_Redact_JsonSerializerDefaultEscaping_RedactsHeaderAndKeyValues`,
`SecretRedactor_Redact_UnicodeEscapedQuoteDelimiters_RedactsHeaderAndKeyValues` and
`SecretRedactor_Redact_NestedJsonText_RedactsHeaderAndKeyValues`, covering
`AgentControl-SecretRedactingLogger-RemovesSecretBearingKeys`.

**SecretRedactingLogger_RedactsEscapedDelimitedValues**: Quoted values containing escaped quotes (`\"`,
`\u0022`, `\\"`, doubled quotes, the other quote character) and values whose delimiters are written in
`\uXXXX` form, as `JsonSerializer` writes them by default, are replaced whole, with no suffix of the secret
surviving. This scenario is tested by `SecretRedactor_Redact_QuotedValueWithEscapedQuotes_NoSuffixSurvives`,
`SecretRedactor_Redact_JsonSerializerDefaultEscaping_RedactsHeaderAndKeyValues` and
`SecretRedactor_Redact_UnicodeEscapedQuoteDelimiters_RedactsHeaderAndKeyValues`, covering
`AgentControl-SecretRedactingLogger-RedactsEscapedDelimitedValues`.

**SecretRedactingLogger_RedactsNestedJsonValues**: A header value in JSON serialized into JSON, once and twice,
with the default and the relaxed escaping, and in nested JSON text written by hand, is replaced whole. This
scenario is tested by `SecretRedactor_Redact_JsonSerializedAuthorizationValue_IsRedactedWholeAtEveryNestingLevel`
and `SecretRedactor_Redact_NestedJsonText_RedactsHeaderAndKeyValues`, covering
`AgentControl-SecretRedactingLogger-RedactsNestedJsonValues`.

**SecretRedactingLogger_StopsAtTheRealTerminator**: The text after the real closing quote is kept, and an even
run of backslashes before a quote leaves the quote as the terminator. This scenario is tested by
`SecretRedactor_Redact_QuotedValue_StopsAtTheRealTerminator`, covering
`AgentControl-SecretRedactingLogger-StopsAtTheRealTerminator`.

**SecretRedactingLogger_RemovesKnownSecrets**: A registered password in arbitrary text, a property value, a
scope and an exception message (also in an inner exception) is removed. This scenario is tested by
`SecretRedactor_RegisterAndUnregister_ReplacesEveryOccurrenceOfTheSecretWhileRegistered` and
`SecretRedactingLogger_Log_RegisteredPasswordInArbitraryTextPropertiesAndExceptions_IsRemoved`, covering
`AgentControl-SecretRedactingLogger-RemovesKnownSecrets`.

**SecretRedactingLogger_RemovesEncodedSecretForms**: The URL-encoded form of a registered secret and the Base64
form of a registered basic credential are removed. This scenario is tested by
`SecretRedactor_RegisterAndUnregister_ReplacesEveryOccurrenceOfTheSecretWhileRegistered`, covering
`AgentControl-SecretRedactingLogger-RemovesEncodedSecretForms`.

**SecretRedactingLogger_StopsWhenSecretWithdrawn**: Text that carries a secret is unchanged before it is
registered and after its last registration is withdrawn, and an empty secret registers nothing. This scenario is
tested by `SecretRedactor_RegisterAndUnregister_ReplacesEveryOccurrenceOfTheSecretWhileRegistered` and
`SecretRedactor_Unregister_SecretRegisteredTwice_StaysRedactedUntilTheLastWithdrawal`, covering
`AgentControl-SecretRedactingLogger-StopsWhenSecretWithdrawn`.

**SecretRedactingLogger_RedactsWhileAnyHolderUsesSecret**: A secret registered twice stays redacted after the
first withdrawal and is released by the last; a lease taken for one holder is idempotent, withdraws only its own
registration and leaves another holder's registration in force; a lease alone redacts the secret only while it is
held, and a lease cannot be taken without a credential. This scenario is tested by
`SecretRedactor_Unregister_SecretRegisteredTwice_StaysRedactedUntilTheLastWithdrawal`,
`SecretRedactor_Lease_DisposedTwiceOrAlongsideOtherHolders_WithdrawsOnlyItsOwnRegistration`,
`SecretRedactor_Lease_HeldAndDisposed_RedactsTheSecretOnlyWhileLeased` and
`SecretRedactor_Lease_NullCredential_Throws`, covering
`AgentControl-SecretRedactingLogger-RedactsWhileAnyHolderUsesSecret`.

**SecretRedactingLogger_RegistryIsThreadSafe**: Registering, redacting and withdrawing secrets from many
threads at once neither throws nor misses a registered secret. This scenario is tested by
`SecretRedactor_RegisterAndRedact_FromManyThreads_IsThreadSafe`, covering
`AgentControl-SecretRedactingLogger-RegistryIsThreadSafe`.

**SecretRedactingLogger_AppliesToEveryMessage**: A message with secrets in its template arguments reaches the
recording logger redacted, together with an exception whose message holds secrets, and a registered password
is removed from the formatted text. This scenario is tested by
`SecretRedactingLogger_Log_MessageAndExceptionWithSecrets_ForwardsRedactedText` and
`SecretRedactingLogger_Log_RegisteredPasswordInArbitraryTextPropertiesAndExceptions_IsRemoved`, covering
`AgentControl-SecretRedactingLogger-AppliesToEveryMessage`.

**SecretRedactingLogger_RedactsStructuredProperties**: Structured properties whose values carry secrets are
redacted individually while the template, the names and the plain value types are kept, and property values of
all kinds are redacted by kind. This scenario is tested by
`SecretRedactingLogger_Log_StructuredPropertyValues_AreRedactedAndStructureIsKept` and
`SecretRedactor_RedactValue_RedactsTextAndKeepsPlainValueTypes`, covering
`AgentControl-SecretRedactingLogger-RedactsStructuredProperties`.

**SecretRedactingLogger_RedactsScopes**: Scope state as text and as pairs reaches the recording logger
redacted. This scenario is tested by `SecretRedactingLogger_BeginScope_TextAndStructuredState_AreRedacted`,
covering `AgentControl-SecretRedactingLogger-RedactsScopes`.

**SecretRedactingLogger_KeepsLevelFiltering**: Level checks are delegated, messages below the minimum level are
dropped and a null wrapped logger is refused. This scenario is tested by
`SecretRedactingLogger_IsEnabled_DelegatesToTheInnerLogger`, covering
`AgentControl-SecretRedactingLogger-KeepsLevelFiltering`.

**SecretRedactingLogger_ForwardsOnlySanitizedException**: An exception reaches the wrapped logger as a
`SanitizedException` that is not the original, has no inner exception and holds no secret. This scenario is
tested by `SecretRedactingLogger_Log_Exception_IsForwardedAsASanitizedStandIn` and
`SecretRedactingLogger_Log_MessageAndExceptionWithSecrets_ForwardsRedactedText`, covering
`AgentControl-SecretRedactingLogger-ForwardsOnlySanitizedException`.

**SecretRedactingLogger_SanitizesExceptions**: An exception chain (inner and aggregated exceptions) with
secrets in every message is described without any of them. This scenario is tested by
`SecretRedactor_Sanitize_ExceptionWithSecretsInChain_KeepsOnlyRedactedText`, covering
`AgentControl-SecretRedactingLogger-SanitizesExceptions`.

**SecretRedactingLogger_KeepsExceptionTypeName**: The description carries the type name of the original
exception. This scenario is tested by `SecretRedactor_Sanitize_ExceptionWithSecretsInChain_KeepsOnlyRedactedText`
and `SecretRedactor_Sanitize_ThrownException_KeepsStackTrace`, covering
`AgentControl-SecretRedactingLogger-KeepsExceptionTypeName`.

**SecretRedactingLogger_KeepsRedactedStackTrace**: A thrown exception keeps its stack trace, redacted, and a
null exception is refused. This scenario is tested by `SecretRedactor_Sanitize_ThrownException_KeepsStackTrace`
and `SecretRedactor_Sanitize_ExceptionWithSecretsInChain_KeepsOnlyRedactedText`, covering
`AgentControl-SecretRedactingLogger-KeepsRedactedStackTrace`.

**SecretRedactingLogger_DropsOriginalExceptionData**: The description has no inner exception and no data, and
the secret held in the data of the original exception appears nowhere in it. This scenario is tested by
`SecretRedactor_Sanitize_ExceptionWithSecretsInChain_KeepsOnlyRedactedText`, covering
`AgentControl-SecretRedactingLogger-DropsOriginalExceptionData`.

**SecretRedactingLogger_RedactsConstructorInputs**: A `SanitizedException` built from a type name, a message and
a stack trace that carry an address with a query string and an authorization header holds none of those
secrets, because its constructor redacts them. This scenario is tested by
`SanitizedException_Constructor_UnredactedText_IsRedactedByTheConstructor`, covering
`AgentControl-SecretRedactingLogger-RedactsConstructorInputs`.
