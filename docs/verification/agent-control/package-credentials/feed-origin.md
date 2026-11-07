### FeedOrigin

#### Verification Approach

`FeedOrigin` is a pure value type verified with unit tests in `FeedOriginTests.cs`. Nothing is mocked: the
tests parse real address strings (including IPv6 literals, explicit default ports, user information and
non-web schemes) and compare the results.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- An origin keeps only scheme, host and port; two spellings of one server (letter case, explicit default
  port) are equal.
- An address with a user name or password, or that is not an absolute http or https address, is refused, and
  the refusal never repeats the address.
- An address matches the origin only when scheme, host and port are all equal.

#### Test Scenarios

**FeedOrigin_NormalizedOrigin**: A full feed address reduces to scheme, host and port with path, query and
fragment dropped; an explicit default port equals none and is omitted from the text form; an IPv6 host is
bracketed and loopback addresses on different ports are different origins; user information, blank,
relative, non-web and malformed text are refused, and `Create` throws without repeating the secret. This
scenario is tested by `FeedOrigin_TryParse_FullFeedUrl_KeepsSchemeHostAndPortOnly`,
`FeedOrigin_Equals_ExplicitDefaultPort_EqualsOriginWithoutPort`,
`FeedOrigin_TryParse_UserInfo_IsRefused`, `FeedOrigin_TryParse_NotAWebAddress_IsRefused`,
`FeedOrigin_Create_UserInfo_ThrowsWithoutRepeatingTheSecret` and
`FeedOrigin_ToString_Ipv6AndPorts_BracketsHostAndKeepsPorts`, covering
`AgentControl-FeedOrigin-NormalizedOrigin`.

**FeedOrigin_MatchesSameOriginOnly**: Other addresses of the same server match; another host, port, scheme or
sub-domain does not; a null or relative address never matches. This scenario is tested by
`FeedOrigin_Matches_VariousUris_OnlyTheSameOriginMatches` and
`FeedOrigin_Matches_NullOrRelativeUri_ReturnsFalse`, covering `AgentControl-FeedOrigin-MatchesSameOriginOnly`.
