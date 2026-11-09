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
- A tag matches only as prefix + strict Semantic Versioning 2.0 version + suffix, with case-sensitive literal parts;
  other tags, metadata, four-part versions, leading zeros (in the numeric parts or in a numeric prerelease identifier)
  and hostile names do not match.
- `Format` reproduces the tag, and reports no tag (`null`) for a version whose tag would exceed 255 characters or
  would not read back as the same version; `ListingGlob` is `refs/tags/` + prefix + `*`, and `IsSafeTagName`
  accepts only legal, safe names of at most 255 characters.

#### Test Scenarios

**GitTagPattern_Validate_PlaceholderAndRefNameRules**: valid patterns (`v{version}`, `release/{version}`, `{version}`,
`{version}-final`, nested folders) are accepted; patterns with no or two placeholders, `{Version}`, stray braces,
spaces,
`~ ^ : ? * [ \`, `..`, `//`, a leading `-` or `.`, a `.lock` component, a trailing `.`, `@`, non-ASCII text or a line
feed are refused, a pattern of 201 characters is refused (200 is accepted) and no message repeats the text (the long
pattern and a secret-looking one are asserted not to appear in the message). This scenario is tested by
`GitTagPattern_TryCreate_ValidPattern_IsAccepted`, `GitTagPattern_TryCreate_InvalidPattern_IsRefused`,
`GitTagPattern_TryCreate_TooLong_IsRefusedWithoutEchoing` and
`GitTagPattern_TryCreate_SecretLooking_IsRefusedWithoutEchoing`, covering
`AgentControl-GitTagPattern-RequireSinglePlaceholder` and `AgentControl-GitTagPattern-RejectInvalidRefNames`.

**GitTagPattern_Match_StrictVersionsOnlyAndCaseSensitive**: `v1.2.3`, `v1.2.3-beta.1`, `release/2.0.0-rc.1`,
`1.2.3-final` and `1.2.3-beta-final` match (and format back to the tag); `V1.2.3`, a missing prefix, `v1.2`,
`v1.2.3.4`, `v1.2.3+build`, names with a space, a line feed, `;`, `/../`, a hyphen after the prefix,
`release/1.0.0/extra`
and a case change of the literal text do not. This scenario is tested by
`GitTagPattern_TryMatch_VersionTag_ReturnsVersion` and `GitTagPattern_TryMatch_NotAVersionTag_DoesNotMatch`, covering
`AgentControl-GitTagPattern-ExtractStrictVersion` and `AgentControl-GitTagPattern-FormatTag`.

**GitTagPattern_Match_StrictSemanticVersioning**: `v01.2.3`, `v1.02.3`, `v1.2.03`, `v1.2.3-01`,
`v1.2.3-alpha.01`, an empty prerelease or identifier, `_` in a prerelease, `v1.2.3+meta`, two or four numeric parts
and an empty numeric part do not match, while `v0.0.0`, `v1.0.0-0`, `v1.0.0-0.3.7`, `v1.0.0-x.7.z.92`,
`v1.0.0-alpha-1` and `v1.0.0-01a` do. This scenario is tested by
`GitTagPattern_TryMatch_NotStrictSemVer_DoesNotMatch` and `GitTagPattern_TryMatch_StrictSemVer_Matches`, covering
`AgentControl-GitTagPattern-ExtractStrictVersion`.

**GitTagPattern_Format_BoundariesAndRoundTrip**: a version whose tag is exactly 255 characters is formatted and one
a character longer has no tag (also with a 95-character prefix and suffix around a long prerelease); versions with build
metadata, a fourth part or a leading zero have no tag; a null version throws `ArgumentNullException`. This scenario
is tested by `GitTagPattern_Format_PrereleaseAtAndOverTagLengthLimit_FormatsOrReturnsNull`,
`GitTagPattern_Format_LongPatternPlusVersion_FormatsAtLimitAndReturnsNullOverIt`,
`GitTagPattern_Format_VersionThatDoesNotRoundTrip_ReturnsNull` and
`GitTagPattern_Format_NullVersion_ThrowsArgumentNullException`, covering
`AgentControl-GitTagPattern-FormatRefusesUnsafeTag`.

**GitTagPattern_ListingGlobAndServerTagNames**: the glob is built from the literal prefix, and server-reported names
with
a leading hyphen, a space, `..`, a trailing dot, an empty or dot-leading component, `.lock`, `: ~ ^ *`, shell syntax,
a line feed, non-ASCII text or more than 255 characters are not safe (a name of exactly 255 characters is safe). This
scenario is tested by
`GitTagPattern_ListingGlob_UsesLiteralPrefix`, `GitTagPattern_IsSafeTagName_ChecksCharactersAndRefNameRules` and
`GitTagPattern_IsSafeTagName_TooLong_IsNotSafe`, covering `AgentControl-GitTagPattern-ListingPrefix` and
`AgentControl-GitTagPattern-VetServerTagNames`.
