### FeedOrigin

#### Verification Approach

`FeedOrigin` is a pure value type verified with unit tests in `FeedOriginTests.cs`. Nothing is mocked: the
tests parse real address strings (including IPv6 literals, explicit default ports, user information and
non-web schemes) and compare the results. The user-information tests build their address with `UriBuilder`
from a sentinel user name and password, so the address is an absolute `https` address with a non-empty user
information part by construction, and the tests assert that the sentinel appears in neither a result nor a
failure message.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- An origin keeps only scheme, host and port; two spellings of one server (letter case, explicit default
  port) are equal.
- An address that is not an absolute http or https address is refused.
- An absolute address with a user name or password is refused, and the refusal never repeats the address.
- The text form of an origin holds only scheme, host (an IPv6 host in brackets) and a non-default port.
- An address matches the origin only when scheme, host and port are all equal; a null or relative address
  never matches.

#### Test Scenarios

**FeedOrigin_NormalizedOrigin**: A full feed address with path, query string and fragment reduces to its
scheme, host and port. This scenario is tested by `FeedOrigin_TryParse_FullFeedUrl_KeepsSchemeHostAndPortOnly`,
covering `AgentControl-FeedOrigin-NormalizedOrigin`.

**FeedOrigin_EquivalentSpellings**: An explicit default port and different letter case compare equal to the
plain spelling and give the same hash code. This scenario is tested by
`FeedOrigin_Equals_ExplicitDefaultPort_EqualsOriginWithoutPort`, covering
`AgentControl-FeedOrigin-EquivalentSpellings`.

**FeedOrigin_RefusesNonWebAddress**: Null, blank, relative, drive-path, `ftp` and `file` text is refused. This
scenario is tested by `FeedOrigin_TryParse_NotAWebAddress_IsRefused`, covering
`AgentControl-FeedOrigin-RefusesNonWebAddress`.

**FeedOrigin_RefusesUserInfo**: Addresses with a user name and password, or with a token as user name, are
refused by `TryParse`, including an absolute address built with `UriBuilder` from a user name and password. This
scenario is tested by `FeedOrigin_TryParse_UserInfo_IsRefused` and
`FeedOrigin_TryParse_UserInfoOnAbsoluteUri_IsRefusedWithoutRepeatingIt`, covering
`AgentControl-FeedOrigin-RefusesUserInfo`.

**FeedOrigin_RefusalNeverRepeatsAddress**: `Create` on an absolute address with user information (built with
`UriBuilder` so that it is absolute by construction) throws `ArgumentException` whose message does not contain
the sentinel, and the refusal of such an address by `TryParse` repeats nothing. This scenario is tested by
`FeedOrigin_Create_UserInfo_ThrowsWithoutRepeatingTheSecret` and
`FeedOrigin_TryParse_UserInfoOnAbsoluteUri_IsRefusedWithoutRepeatingIt`, covering
`AgentControl-FeedOrigin-RefusalNeverRepeatsAddress`.

**FeedOrigin_TextFormShowsOnlyOrigin**: The text form of a parsed origin is scheme and host without path,
query string or fragment; the default port is omitted, a non-default port is kept, and an IPv6 host is bracketed
with loopback addresses on different ports staying different origins. This scenario is tested by
`FeedOrigin_TryParse_FullFeedUrl_KeepsSchemeHostAndPortOnly`,
`FeedOrigin_Equals_ExplicitDefaultPort_EqualsOriginWithoutPort` and
`FeedOrigin_ToString_Ipv6AndPorts_BracketsHostAndKeepsPorts`, covering
`AgentControl-FeedOrigin-TextFormShowsOnlyOrigin`.

**FeedOrigin_MatchesSameOriginOnly**: Other addresses of the same server match; another host, a look-alike
host, another port, another scheme or a sub-domain does not. This scenario is tested by
`FeedOrigin_Matches_VariousUris_OnlyTheSameOriginMatches`, covering
`AgentControl-FeedOrigin-MatchesSameOriginOnly`.

**FeedOrigin_NeverMatchesRelativeOrMissingAddress**: A null or relative address never matches. This scenario is
tested by `FeedOrigin_Matches_NullOrRelativeUri_ReturnsFalse`, covering
`AgentControl-FeedOrigin-NeverMatchesRelativeOrMissingAddress`.
