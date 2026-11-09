### GitRepositoryAddress

#### Verification Approach

`GitRepositoryAddress` is verified with unit tests in `GitRepositoryAddressTests.cs`. The unit is pure, so every test
passes address text and inspects the result; no git is run and nothing touches the filesystem except that a local path is
built under the temporary folder and never created. Hostile inputs (option injection, remote helpers, embedded
credentials, control characters) are theory data so each form is a separate, named case.

#### Test Environment

N/A - standard test environment. Two cases are operating-system specific and guard themselves (a UNC path is only
checked on Windows).

#### Acceptance Criteria

- Valid https, ssh (URL and `user@host:path`) and absolute local addresses are accepted with the right kind and protocol.
- Every dangerous or unsupported form is refused with a message: other schemes, remote helpers, leading hyphens, relative
  or ambiguous paths, malformed hosts, ports and paths, control characters and over-long text.
- Credentials in an address - user information on https, a password in ssh, a query or fragment - are refused and
  detected from the text alone, while an ssh user name is accepted.
- No message ever contains the planted secret; the cache key is stable and distinguishes repositories.

#### Test Scenarios

**GitRepositoryAddress_Accept_HttpsSshAndLocal**: https with and without a port and scheme case, ssh in both forms
(including an IPv6 host) and an absolute local path are accepted. This scenario is tested by
`GitRepositoryAddress_TryParse_Https_IsAccepted`, `GitRepositoryAddress_TryParse_Ssh_IsAccepted` and
`GitRepositoryAddress_TryParse_AbsoluteLocalPath_IsAcceptedWithoutCheckingExistence`, covering
`AgentControl-GitRepositoryAddress-AcceptHttps`, `AgentControl-GitRepositoryAddress-AcceptSsh` and
`AgentControl-GitRepositoryAddress-AcceptLocalPath`.

**GitRepositoryAddress_Reject_DangerousUnsupportedAndOptionLikeAddresses**: plain http, `git://`, `file://`, `ftp://`,
`ext::`, `fd::`, `foo::bar`, `-oProxyCommand=...`, `--upload-pack=...`, `ssh://-oProxyCommand=.../x`, a host or user that
starts with a hyphen, `host:path`, relative paths, a missing path, spaces, backslashes, bad ports and control characters
are refused with a message, and an over-long address is refused. This scenario is tested by
`GitRepositoryAddress_TryParse_DangerousOrUnsupported_IsRefused` and `GitRepositoryAddress_TryParse_TooLong_IsRefused`,
covering `AgentControl-GitRepositoryAddress-RejectDangerousTransports` and
`AgentControl-GitRepositoryAddress-RejectOptionLikeValues`.

**GitRepositoryAddress_Credentials_RefusedDetectedAndNeverEchoed**: user information on https, a password in an ssh URL,
a query and a fragment are refused, `EmbedsCredential` says so for the text alone (and not for an ssh user name, an scp
address or a local path), and none of the messages contains the sentinel secret planted in the input. This scenario is
tested by `GitRepositoryAddress_EmbedsCredential_DetectsSecretsOnlyInUrls` and
`GitRepositoryAddress_TryParse_Hostile_NeverEchoesInput`, covering
`AgentControl-GitRepositoryAddress-RejectEmbeddedCredentials` and `AgentControl-GitRepositoryAddress-NeverEchoesInput`.

**GitRepositoryAddress_CacheKey_StableAndDistinct**: the key is 16 lower-case hex digits, equal for addresses that differ
only in scheme or host case, different for another path, another path case and another kind. This scenario is tested by
`GitRepositoryAddress_CacheKey_IsStableAndDistinguishesRepositories`, covering
`AgentControl-GitRepositoryAddress-CacheKeyStable`.
