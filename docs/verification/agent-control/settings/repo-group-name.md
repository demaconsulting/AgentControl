### RepoGroupName

#### Verification Approach

`RepoGroupName` is verified with unit tests defined in `RepoGroupNameTests.cs`. The class is a pure, stateless static
helper with no dependencies, so every test calls its methods directly with literal names and compares the results;
nothing is mocked, and no file, window or process is involved. How the settings loader applies the rules to a stored
settings file is verified by the `SettingsStore` unit (`SettingsStoreOrganizingTests.cs`), and how the name prompt
uses them by the `GroupNameViewModel` unit.

#### Test Environment

N/A - standard test environment (no files, windows or processes are involved).

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Cleaning up a name trims its ends and turns each run of white space and control characters into one space; a name
  with nothing left, or no name at all, becomes no group.
- A usable name - not empty, at most 40 characters (exactly 40 included) and not a built-in section name - is reported
  as usable, and each refusal reports its own reason: empty, too long, or reserved (`Favorites` and `Ungrouped` in any
  letter case and with surrounding white space).
- A stored name is repaired and never refused: a usable one is cleaned up, an overlong one is cut to 40 characters,
  and a missing, blank or reserved one reads as no group.
- Names that differ only in letter case are listed as one group spelled like the first one, the list is sorted
  ignoring letter case, and repos in no group are left out.
- A name matching an existing group ignoring letter case takes that group's spelling, and any other name is left as
  typed.

#### Test Scenarios

**RepoGroupName_Normalize_CleansWhiteSpaceAndControlCharacters**: A theory over typed and stored texts: a clean name
stays as it is; surrounding spaces are removed; a tab and extra spaces, a carriage return and line feed and a bell
character each become one space between words; empty, white-space-only and control-characters-only texts and `null`
give no name. This scenario is tested by `RepoGroupName_Normalize_CleansWhiteSpaceAndControlCharacters`, covering
`AgentControl-RepoGroupName-Normalize` (the cases with text) and `AgentControl-RepoGroupName-NormalizeNothingLeft`
(the cases with nothing left).

**RepoGroupName_Validate_ReportsEachProblem**: A usable name and a name of exactly 40 characters are acceptable; a
name of 41 characters is too long; white space and `null` are empty; `Favorites` and `ungrouped` with surrounding spaces
are reserved. This
scenario is tested by `RepoGroupName_Validate_ReportsEachProblem`, covering `AgentControl-RepoGroupName-AcceptsUsableName`,
`AgentControl-RepoGroupName-RefusesBlankName`, `AgentControl-RepoGroupName-RefusesTooLongName` and
`AgentControl-RepoGroupName-RefusesReservedNames`.

**RepoGroupName_Sanitize_RepairsStoredNames**: A name with surrounding spaces is cleaned; a name 15 characters over the
limit is cut to 40 characters; `FAVORITES`, `Ungrouped`, an empty text and `null` give no group. This scenario is
tested by `RepoGroupName_Sanitize_RepairsStoredNames`, covering `AgentControl-RepoGroupName-SanitizeStoredName`,
`AgentControl-RepoGroupName-SanitizeCutsLongName` and `AgentControl-RepoGroupName-SanitizeReservedIsNoGroup`.

**RepoGroupName_DistinctNames_UnifiesCaseAndSorts**: The names `work`, no group, `Alpha`, `Work`, white space, `beta`
and `ALPHA` are listed as `Alpha`, `beta` and `work`: each group once with the first spelling, in alphabetical order
ignoring letter case, with the repos in no group left out. This scenario is tested by
`RepoGroupName_DistinctNames_UnifiesCaseAndSorts`, covering `AgentControl-RepoGroupName-DistinctNames`,
`AgentControl-RepoGroupName-DistinctNamesSorted` and `AgentControl-RepoGroupName-DistinctNamesLeaveOutNoGroup`.

**RepoGroupName_Canonicalize_AdoptsExistingSpelling**: Among the existing groups `Home`, `Work` and no group, `wORK`
becomes `Work`; among `Home` and `Work`, `Fresh` stays `Fresh`. This scenario is tested by
`RepoGroupName_Canonicalize_AdoptsExistingSpelling`, covering `AgentControl-RepoGroupName-CanonicalizeAdoptsExisting`
and `AgentControl-RepoGroupName-CanonicalizeKeepsNewName`.
