### SecretRedactingLogger

![PackageCredentials Structure](PackageCredentialsView.svg)

#### Purpose

`SecretRedactingLogger` wraps the logger given to the NuGet library so that anything that could be a secret
is removed from every message before it reaches the application log. The library reports its network
activity - which addresses it requested and why a request failed - which is valuable for diagnosing an
unreachable server, but a redirect to a download service can carry a signed token in a query string and an
exception message can quote one. Rather than trust every message source, all text passes through the
redaction, so the rule *secrets, Authorization headers and full URLs with query strings never reach the log*
holds whatever the library writes. It satisfies `AgentControl-SecretRedactingLogger-RemovesUrlSecrets`,
`AgentControl-SecretRedactingLogger-RemovesAuthorizationValues` and
`AgentControl-SecretRedactingLogger-AppliesToEveryMessage`.

#### Data Model

**_inner**: `ILogger` - the wrapped logger that receives the redacted messages. The type is otherwise
stateless and thread-safe when the wrapped logger is.

Two compiled regular expressions: one that finds `http`/`https` addresses inside free text, and one that finds
an `Authorization`/`Proxy-Authorization` header or a `Basic`/`Bearer` credential followed by its value.

#### Key Methods

**Redact(text)** (static): Replaces the value of each authorization header or credential with `[redacted]`
(done first, so an address inside a header value is not half-processed), then reduces each address to its
scheme, host, port and path - dropping user information, query string and fragment. Text without secrets is
returned unchanged. An address that cannot be parsed is cut at its first `?`, `#` or `@` and marked redacted.

**Log(...)**: When the wrapped logger has the level enabled, formats the message first, redacts the final
text, appends the exception's type name and redacted message when there is an exception, and forwards only
that text - no structured state, stack trace or inner exception is passed on, which keeps the guarantee
simple (`AgentControl-SecretRedactingLogger-AppliesToEveryMessage`).

**IsEnabled / BeginScope**: Delegate to the wrapped logger, so its level filtering and scopes stay in effect.

#### Error Handling

Redaction never throws for null or empty input (null is treated as empty). Exceptions raised by the wrapped
logger propagate unchanged. The user name and password a person types are never passed to this logger at
all; the wrapper is a second line of defense for secrets carried by addresses and headers.

#### Dependencies

- **Microsoft.Extensions.Logging** - `ILogger`.
- **.NET BCL** - generated regular expressions and `System.Uri`.

#### Callers

- **NuGetSourceProvider** - wraps the application's logger with it before handing a logger to the NuGet
  library, and uses the wrapped logger for its own diagnostics.
