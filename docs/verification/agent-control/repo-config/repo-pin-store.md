### RepoPinStore

#### Verification Approach

`RepoPinStore` is verified with unit tests defined in `RepoPinStoreTests.cs`. Every test uses
a real temporary repo directory and a real `.agentcontrol.json` file on disk, so save/load
round-tripping and the no-pin-file case are exercised against genuine filesystem state.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- A repo with no pin file returns none rather than throwing.
- A saved pin's values are returned unchanged by a subsequent load.
- A null repo root path or a null pin value is rejected.

#### Test Scenarios

**RepoPinStore_Load_NoFileReturnsNullAndRejectsNullRoot**: Loading a repo with no pin file
returns null, and loading with a null repo root path throws `ArgumentNullException`. This
scenario is tested by `RepoPinStore_Load_NoPinFile_ReturnsNull` and
`RepoPinStore_Load_NullRepoRoot_ThrowsArgumentNullException`, covering
`AgentControl-RepoPinStore-Load`.

**RepoPinStore_SaveThenLoad_RoundTripsAllFields**: Saving a pin and then loading it back
returns all fields unchanged. This scenario is tested by
`RepoPinStore_SaveThenLoad_RoundTripsAllFields`, covering both
`AgentControl-RepoPinStore-Load` and `AgentControl-RepoPinStore-Save`.

**RepoPinStore_Save_OverwritesExistingAndRejectsNullPin**: Saving over an existing pin file
overwrites it with the new values, and saving a null pin throws `ArgumentNullException`. This
scenario is tested by `RepoPinStore_Save_ExistingPinFile_OverwritesWithNewValues` and
`RepoPinStore_Save_NullPin_ThrowsArgumentNullException`, covering
`AgentControl-RepoPinStore-Save`.
