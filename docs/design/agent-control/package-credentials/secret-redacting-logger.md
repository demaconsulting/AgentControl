### SecretRedactingLogger

![PackageCredentials Structure](PackageCredentialsView.svg)

#### Purpose

`SecretRedactingLogger` wraps a logger so that anything that could be a secret is removed from everything
that reaches the application log: the formatted message, every structured property value, scope state and
the exception. It is given to the NuGet library (which reports its network activity - which addresses it
requested and why a request failed - and whose redirects can carry a signed token in a query string) and is
also the logger of the repo card, whose operations report package-source failures. Rather than trust every
message source, all text passes through `SecretRedactor`, so the rule *secrets, Authorization headers and full
URLs with query strings never reach the log* holds whatever the caller writes. The unit also contains the
redaction engine itself (`SecretRedactor`) and `SanitizedException`, the secret-free stand-in for an
exception, because the same engine protects the text shown in the user interface and the inner exception kept
by `PackageSourceAccessException`. It satisfies the following requirements, each of which states one criterion:

- *Web addresses*: `AgentControl-SecretRedactingLogger-RemovesUrlSecrets`,
  `AgentControl-SecretRedactingLogger-RemovesUrlUserInfo`,
  `AgentControl-SecretRedactingLogger-RemovesUrlFragment`,
  `AgentControl-SecretRedactingLogger-KeepsUrlLocation`,
  `AgentControl-SecretRedactingLogger-KeepsSurroundingText`,
  `AgentControl-SecretRedactingLogger-RecognizesIpv6Literal`,
  `AgentControl-SecretRedactingLogger-RedactsUrlInsideEscapedQuotes` and
  `AgentControl-SecretRedactingLogger-RedactsUrlInsideParentheses`.
- *Headers and keys*: `AgentControl-SecretRedactingLogger-RemovesAuthorizationValues`,
  `AgentControl-SecretRedactingLogger-ReplacesValueWhateverScheme`,
  `AgentControl-SecretRedactingLogger-KeepsHeaderNameAndOtherLines`,
  `AgentControl-SecretRedactingLogger-RemovesBareCredentials`,
  `AgentControl-SecretRedactingLogger-RemovesSecretBearingKeys`,
  `AgentControl-SecretRedactingLogger-RedactsEscapedDelimitedValues`,
  `AgentControl-SecretRedactingLogger-RedactsNestedJsonValues` and
  `AgentControl-SecretRedactingLogger-StopsAtTheRealTerminator`.
- *Registered secrets*: `AgentControl-SecretRedactingLogger-RemovesKnownSecrets`,
  `AgentControl-SecretRedactingLogger-RemovesEncodedSecretForms`,
  `AgentControl-SecretRedactingLogger-StopsWhenSecretWithdrawn`,
  `AgentControl-SecretRedactingLogger-RedactsWhileAnyHolderUsesSecret` and
  `AgentControl-SecretRedactingLogger-RegistryIsThreadSafe`.
- *Messages the logger forwards*: `AgentControl-SecretRedactingLogger-AppliesToEveryMessage`,
  `AgentControl-SecretRedactingLogger-RedactsStructuredProperties`,
  `AgentControl-SecretRedactingLogger-RedactsScopes`,
  `AgentControl-SecretRedactingLogger-KeepsLevelFiltering` and
  `AgentControl-SecretRedactingLogger-ForwardsOnlySanitizedException`.
- *Secret-free exception description*: `AgentControl-SecretRedactingLogger-SanitizesExceptions`,
  `AgentControl-SecretRedactingLogger-KeepsExceptionTypeName`,
  `AgentControl-SecretRedactingLogger-KeepsRedactedStackTrace`,
  `AgentControl-SecretRedactingLogger-DropsOriginalExceptionData` and
  `AgentControl-SecretRedactingLogger-RedactsConstructorInputs`.

#### Data Model

**SecretRedactingLogger._inner**: `ILogger` - the wrapped logger that receives the redacted output. The
logger is otherwise stateless and thread-safe when the wrapped logger is.

**SecretRedactor** (static): a process-wide, reference-counted registry of the secret values currently in
use (`Dictionary<string,int>` under a lock, republished as an immutable array, longest secret first, for
lock-free reads), and three compiled regular expressions: one for the value of a secret-bearing *header*, one
for the value of a secret-bearing *key* (`password`, `token`, `sig`, ...) and one for a bare credential (a
scheme word followed by a token, or a JSON web token). The registry is process-wide because the secrets it
protects are (the NuGet credential service is too); it is never enumerated, serialized or logged.

**SanitizedException**: an `Exception` that keeps the original type name, a redacted message chain and a
redacted stack trace, and nothing else - no inner exception, no data, no reference to the original. Its
constructor is `internal` and redacts the type name, message and stack trace it is given, so a secret-free
contract holds for every construction path (`Sanitize` and any other caller inside the assembly); the type
cannot be built with text that has not been redacted from outside the assembly.

#### Key Methods

**SecretRedactor.Register / Unregister / Lease(credential)**: Add or withdraw one registration of the
credential's secret together with the forms in which it appears on the wire - its URL-encoded form and the
Base64 form of `user:secret` used by basic authentication. Registrations are reference-counted. `Lease`
registers on behalf of one holder and returns an idempotent `IDisposable` that withdraws exactly that
registration, so each operation that uses a secret keeps it redacted for as long as it runs, independent of
the session store's own registration and of every other operation. An empty secret registers nothing. A
secret of any length is registered; a very short one is also replaced where it occurs inside unrelated text,
which is the safe direction.

**SecretRedactor.Redact(text)**: Works in layers so a secret missed by one is caught by another.
(1) Every registered secret is replaced verbatim. (2) The *complete* value of an `Authorization`,
`Proxy-Authorization`, API-key or cookie header is replaced whatever its scheme - basic, bearer, digest, NTLM,
negotiate, a custom scheme or none. The header name is found by a pattern (which accepts the quote after it in
every written form - plain `"` and `'`, backslash-escaped `\"`, `\\"` and so on, `\u0022`-style escapes
(`\uXXXX`, the default escaping of `System.Text.Json`) and the entity forms `&quot;` and `&#34;` - at any level
of backslash escaping) and the value by an
**escape-aware scanner**: a value in plain quotes ends at the first quote that is neither backslash-escaped
(`\"`, `\\`, `\u0022` - an odd run of backslashes escapes the quote, an even run does not) nor doubled; a value
that starts with an escaped or entity-encoded quote (nested JSON, HTML), an unterminated quoted value and any
other header value run to the end of the line - when in doubt it redacts rather than leaks.
(3) A small scanner (not a pattern) finds each `scheme://` address - understanding the brackets of an IPv6
literal and the same backslash and doubled-quote escapes, so an address inside a JSON or quoted string is
consumed through escaped delimiters up to its real terminator - and reduces it to scheme, host, port and path:
user information, query string and fragment are removed, and an `@` anywhere before the query is treated as
the end of user information so a password containing `/`, `?` or `#` cannot survive. Parentheses are treated in
balance: a `)` that closes a `(` opened inside the address is part of the address, and an unmatched `)` ends the
address only when it is followed by the end of the text, whitespace or closing or sentence punctuation;
otherwise it is treated as part of the address, so that the whole query string and fragment of an address such
as `https://h/p(1)?sig=x` or a Markdown link `[text](https://u:p@h/p?q=1#f)` are removed rather than the part
after a parenthesis being left behind. Trailing sentence punctuation stays. (4) Secret-bearing keys (found the
same way as headers, so with the same quote forms, values scanned escape-aware) and
bare credentials are replaced. Redaction is conservative: it may remove more than strictly necessary but never
less. Text without secrets, and null, pass through unchanged (null becomes empty).

**SecretRedactor.RedactValue(value)**: Redacts a property or scope value: strings and arbitrary objects (by
their text) are redacted, numbers, booleans, enums, dates and similar value types pass through so structured
sinks keep their types.

**SecretRedactor.Sanitize(exception) / SafeMessage(exception)**: `Sanitize` builds a `SanitizedException`
whose message is the redacted `Type: message` of the exception and of every inner and aggregated exception
(joined with `--->`, nesting limited to ten levels) and whose stack trace is redacted; `SafeMessage` is the
redacted message alone, for status text.

**SecretRedactingLogger.Log(...)**: When the wrapped logger has the level enabled, formats the message,
redacts the final text, copies the structured state with every value redacted (the template and the property
names are kept, so sinks still see structure), replaces the exception by its sanitized description, and
forwards all three (`AgentControl-SecretRedactingLogger-AppliesToEveryMessage`).

**SecretRedactingLogger.BeginScope(state)**: Structured scope state is forwarded with redacted values; any
other state is forwarded as redacted text.

**IsEnabled**: Delegates to the wrapped logger, so its level filtering stays in effect.

#### Error Handling

Redaction never throws for null or empty input. Exceptions raised by the wrapped logger propagate unchanged.
Redaction errs on the side of removing too much: an unterminated quoted value, an unmatched parenthesis that is
not followed by a sentence boundary and an escape form it cannot classify are all redacted to the end of the
value, address or line rather than left in the output. `SanitizedException` can only be built inside the assembly,
and its constructor redacts its inputs, so even an internal caller that passes raw text cannot produce a stand-in
that holds a secret.
The user name and password a person types are never passed to a logger by AgentControl; they are
additionally registered with `SecretRedactor` for as long as they are in use (by `SessionCredentialStore`
while stored and by `PackageCredentialCallback` while an operation tries them), so even an unforeseen path
cannot log them.

#### Dependencies

- **Microsoft.Extensions.Logging** - `ILogger`.
- **NuGetCaching** (OTS) - `NuGetCredential`, whose secret is registered.
- **.NET BCL** - generated regular expressions.

#### Callers

- **NuGetSourceProvider** - wraps the application's logger with it before handing a logger to the NuGet
  library, uses the wrapped logger for its own diagnostics, and (through `PackageSourceAccessException`)
  replaces the library's exceptions by sanitized descriptions.
- **SessionCredentialStore / PackageCredentialCallback** - register and withdraw the secrets in use.
- **RepoCardViewModel** - logs through it; with **SourceEditorViewModel**, **SelectPackageWindowViewModel**
  and **MainWindow** it shows only `SafeMessage` text for package-source failures.
- **LoggingSetup** (`Logging` subsystem) and **Program** - the process-wide unhandled and unobserved
  exception handlers and the startup-failure path log every escaping exception through it, so no raw
  exception reaches the diagnostic log.
