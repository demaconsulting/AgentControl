### SecretRedactingLogger

#### Verification Approach

`SecretRedactingLogger` is verified with unit tests in `SecretRedactingLoggerTests.cs`. The static `Redact`
method is exercised directly with realistic message text (signed addresses, user-information addresses,
`Authorization` headers, plain text, null), and the `ILogger` behavior is exercised over `ListLogger`, a
logger that records the formatted text of every message so the tests can assert exactly what would reach the
application log. Sentinel secrets make any leak visible.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- User information, query string and fragment are removed from every web address while scheme, host, port,
  path and the surrounding text stay.
- Authorization header and basic or bearer credential values are replaced by a placeholder.
- Text without secrets, and null, pass through unchanged.
- A message and its exception reach the wrapped logger redacted, as plain text; level checks and scopes are
  delegated to the wrapped logger.

#### Test Scenarios

**SecretRedactingLogger_RemovesUrlSecrets**: Addresses with user information, a query string and a fragment
are reduced to scheme, host, port and path, and text without secrets is unchanged. This scenario is tested by
`SecretRedactingLogger_Redact_UrlsWithSecrets_KeepsOnlySchemeHostPortAndPath` and
`SecretRedactingLogger_Redact_PlainText_IsUnchanged`, covering
`AgentControl-SecretRedactingLogger-RemovesUrlSecrets`.

**SecretRedactingLogger_RemovesAuthorizationValues**: Header values and basic or bearer credentials are
replaced by a placeholder. This scenario is tested by
`SecretRedactingLogger_Redact_AuthorizationValues_AreReplaced`, covering
`AgentControl-SecretRedactingLogger-RemovesAuthorizationValues`.

**SecretRedactingLogger_AppliesToEveryMessage**: A message with secrets and an exception whose message holds
secrets reach the recording logger redacted; scope and level checks are delegated, and a null wrapped logger
is refused. This scenario is tested by
`SecretRedactingLogger_Log_MessageAndExceptionWithSecrets_ForwardsRedactedText` and
`SecretRedactingLogger_IsEnabledAndScope_DelegatesToTheInnerLogger`, covering
`AgentControl-SecretRedactingLogger-AppliesToEveryMessage`.
