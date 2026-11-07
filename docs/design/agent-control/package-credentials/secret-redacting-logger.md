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
by `PackageSourceAccessException`. It satisfies `AgentControl-SecretRedactingLogger-RemovesUrlSecrets`,
`AgentControl-SecretRedactingLogger-RemovesAuthorizationValues`,
`AgentControl-SecretRedactingLogger-RemovesKnownSecrets`,
`AgentControl-SecretRedactingLogger-AppliesToEveryMessage` and
`AgentControl-SecretRedactingLogger-SanitizesExceptions`.

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
redacted stack trace, and nothing else - no inner exception, no data, no reference to the original.

#### Key Methods

**SecretRedactor.Register / Unregister(credential)**: Add or withdraw one registration of the credential's
secret together with the forms in which it appears on the wire - its URL-encoded form and the Base64 form of
`user:secret` used by basic authentication. An empty secret registers nothing. A secret of any length is
registered; a very short one is also replaced where it occurs inside unrelated text, which is the safe
direction.

**SecretRedactor.Redact(text)**: Works in layers so a secret missed by one is caught by another.
(1) Every registered secret is replaced verbatim. (2) The *complete* value of an `Authorization`,
`Proxy-Authorization`, API-key or cookie header is replaced up to the end of the line (or the closing quote of
a quoted value) whatever its scheme - basic, bearer, digest, NTLM, negotiate, a custom scheme or none.
(3) A small scanner (not a pattern) finds each `scheme://` address - understanding the brackets of an IPv6
literal - and reduces it to scheme, host, port and path: user information, query string and fragment are
removed, and an `@` anywhere before the query is treated as the end of user information so a password
containing `/`, `?` or `#` cannot survive. Trailing sentence punctuation stays. (4) Secret-bearing keys and
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
