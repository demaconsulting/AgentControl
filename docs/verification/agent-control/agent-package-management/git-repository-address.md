### GitRepositoryAddress

#### Verification Approach

`GitRepositoryAddress` is verified with unit tests in `GitRepositoryAddressTests.cs`. The unit is pure, so every test
passes address text and inspects the result; no git is run and nothing touches the filesystem except that a local path
is
built under the temporary folder and never created. Hostile inputs (option injection, remote helpers, embedded
credentials, control characters) are theory data so each form is a separate, named case.

#### Test Environment

N/A - standard test environment. Two cases are operating-system specific and guard themselves (a UNC path is only
checked on Windows).

#### Acceptance Criteria

- Valid https, ssh (URL and `user@host:path`) and absolute local addresses are accepted with the right kind and
  protocol.
- Every dangerous or unsupported form is refused with a message: other schemes, remote helpers, leading hyphens,
  relative
  or ambiguous paths, malformed hosts, ports and paths, control characters and over-long text.
- Credentials in an address - user information on https, a password in ssh, a query or fragment - are refused and
  detected from the text alone, while an ssh user name is accepted.
- No message ever contains the planted secret or the complete input; the cache key has the documented format, is stable
  and distinguishes repositories.

#### Test Scenarios

**GitRepositoryAddress_Accept_HttpsSshAndLocal**: https with and without a port and scheme case, ssh URLs (with and
without a user name and port, including an IPv6 host), the scp-like `user@host:path` form and an absolute local path are
accepted. This scenario is tested by `GitRepositoryAddress_TryParse_Https_IsAccepted`,
`GitRepositoryAddress_TryParse_SshUrl_IsAccepted`, `GitRepositoryAddress_TryParse_ScpLikeSsh_IsAccepted` and
`GitRepositoryAddress_TryParse_AbsoluteLocalPath_IsAcceptedWithoutCheckingExistence`, covering
`AgentControl-GitRepositoryAddress-AcceptHttps`, `AgentControl-GitRepositoryAddress-AcceptSsh`,
`AgentControl-GitRepositoryAddress-AcceptScpLikeSsh` and `AgentControl-GitRepositoryAddress-AcceptLocalPath`.

**GitRepositoryAddress_Reject_DangerousTransports**: plain http, `git://`, `file://`, `ftp://`, `ext::`, `fd::`,
`foo::bar`, `host:path` and relative paths are refused with a message that does not contain the input. This scenario is
tested by `GitRepositoryAddress_TryParse_DangerousOrUnsupported_IsRefused`, covering
`AgentControl-GitRepositoryAddress-RejectDangerousTransports`.

**GitRepositoryAddress_Reject_OptionLikeValues**: `-oProxyCommand=...`, `--upload-pack=...`,
`ssh://-oProxyCommand=.../x`,
a host, user name or scp path that starts with a hyphen are refused. This scenario is tested by
`GitRepositoryAddress_TryParse_OptionLikeValue_IsRefused`, covering
`AgentControl-GitRepositoryAddress-RejectOptionLikeValues`.

**GitRepositoryAddress_Reject_ControlCharactersAndMalformedParts**: a line feed, a NUL, a tab, a space in the host, the
user name or an scp path, a URL without a path, a bad or missing port, a backslash, an unterminated IPv6 host and an
over-long address are refused. This scenario is tested by
`GitRepositoryAddress_TryParse_ControlCharactersOrWhiteSpace_IsRefused`,
`GitRepositoryAddress_TryParse_MalformedHostPortOrPath_IsRefused` and `GitRepositoryAddress_TryParse_TooLong_IsRefused`,
covering `AgentControl-GitRepositoryAddress-RejectControlCharacters`,
`AgentControl-GitRepositoryAddress-RejectMalformedParts` and `AgentControl-GitRepositoryAddress-RejectTooLong`.

**GitRepositoryAddress_Credentials_RefusedAndDetected**: user information on https, a password in an ssh URL, a query
and a
fragment are refused, and `EmbedsCredential` says so for the text alone (and not for an ssh user name, an scp address or
a local path). This scenario is tested by `GitRepositoryAddress_TryParse_EmbeddedCredential_IsRefused`, covering
`AgentControl-GitRepositoryAddress-RejectEmbeddedCredentials`, and by
`GitRepositoryAddress_EmbedsCredential_DetectsSecretsOnlyInUrls`, covering
`AgentControl-GitRepositoryAddress-RecognizeEmbeddedCredentials`.

**GitRepositoryAddress_NeverEchoesInput**: none of the messages contains the sentinel secret planted in the input, and
no
message contains the complete input (for every refused input of more than four characters, and for a 2100-character
address); the fixed wording uses placeholders such as `https://<host>/<path>` rather than example addresses, so that a
short input cannot appear in a message by coincidence. This scenario is tested by
`GitRepositoryAddress_TryParse_Hostile_NeverEchoesInput`,
`GitRepositoryAddress_TryParse_DangerousOrUnsupported_IsRefused`
and `GitRepositoryAddress_TryParse_TooLong_IsRefused`, covering `AgentControl-GitRepositoryAddress-NeverEchoesInput`.

**GitRepositoryAddress_CacheKey_FormatStabilityAndDistinctness**: the key is 16 lower-case hex digits, equal for
addresses
that differ only in scheme or host case (and for the same address with surrounding white space), and different for
another path, another path case and another kind. This scenario is tested by
`GitRepositoryAddress_CacheKey_IsSixteenLowerCaseHexDigits`, covering
`AgentControl-GitRepositoryAddress-CacheKeyFormat`;
`GitRepositoryAddress_CacheKey_SchemeAndHostCaseDoNotChangeIt`, covering
`AgentControl-GitRepositoryAddress-CacheKeyStable`; and
`GitRepositoryAddress_CacheKey_DifferentRepositoriesHaveDifferentKeys`,
covering `AgentControl-GitRepositoryAddress-CacheKeyDistinguishesRepositories`.
