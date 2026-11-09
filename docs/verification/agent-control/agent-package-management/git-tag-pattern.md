### GitTagPattern

#### Verification Approach

`GitTagPattern` is verified with unit tests in `GitTagPatternTests.cs`. The unit is pure: tests create patterns from
text and match tag names against them. Valid and invalid patterns, matching and non-matching tags and hostile server tag
names are theory data, so every rule has a named case.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- A pattern with exactly one lower-case `{version}` and legal literal text is accepted; every other shape (no or several
  placeholders, other spellings, braces, characters git forbids, illegal ref names, too long) is refused with a message
  that does not repeat the pattern.
- A tag matches only as prefix + strict semantic version + suffix, with case-sensitive literal parts; other tags,
  metadata, four-part versions and hostile names do not match.
- `Format` reproduces the tag, `ListingGlob` is `refs/tags/` + prefix + `*`, and `IsSafeTagName` accepts only legal,
  safe names.

#### Test Scenarios

**GitTagPattern_Validate_PlaceholderAndRefNameRules**: valid patterns (`v{version}`, `release/{version}`, `{version}`,
`{version}-final`, nested folders) are accepted; patterns with no or two placeholders, `{Version}`, stray braces, spaces,
`~ ^ : ? * [ \`, `..`, `//`, a leading `-` or `.`, a `.lock` component, a trailing `.`, `@`, non-ASCII text or a line
feed are refused, a too-long pattern is refused and the message never repeats the text. This scenario is tested by
`GitTagPattern_TryCreate_ValidPattern_IsAccepted`, `GitTagPattern_TryCreate_InvalidPattern_IsRefused` and
`GitTagPattern_TryCreate_TooLongOrSecretLooking_IsRefusedWithoutEchoing`, covering
`AgentControl-GitTagPattern-RequireSinglePlaceholder` and `AgentControl-GitTagPattern-RejectInvalidRefNames`.

**GitTagPattern_Match_StrictVersionsOnlyAndCaseSensitive**: `v1.2.3`, `v1.2.3-beta.1`, `release/2.0.0-rc.1`,
`1.2.3-final` and `1.2.3-beta-final` match (and format back to the tag); `V1.2.3`, a missing prefix, `v1.2`,
`v1.2.3.4`, `v1.2.3+build`, names with a space, a line feed, `;`, `/../`, a hyphen after the prefix, `release/1.0.0/extra`
and a case change of the literal text do not. This scenario is tested by
`GitTagPattern_TryMatch_VersionTag_ReturnsVersion` and `GitTagPattern_TryMatch_NotAVersionTag_DoesNotMatch`, covering
`AgentControl-GitTagPattern-ExtractStrictVersion` and `AgentControl-GitTagPattern-FormatTag`.

**GitTagPattern_ListingGlobAndServerTagNames**: the glob is built from the literal prefix, and server-reported names with
a leading hyphen, a space, `..`, a trailing dot, an empty or dot-leading component, `.lock`, `: ~ ^ *`, shell syntax,
a line feed, non-ASCII text or more than 255 characters are not safe. This scenario is tested by
`GitTagPattern_ListingGlob_UsesLiteralPrefix`, `GitTagPattern_IsSafeTagName_ChecksCharactersAndRefNameRules` and
`GitTagPattern_IsSafeTagName_TooLong_IsNotSafe`, covering `AgentControl-GitTagPattern-ListingPrefix` and
`AgentControl-GitTagPattern-VetServerTagNames`.
