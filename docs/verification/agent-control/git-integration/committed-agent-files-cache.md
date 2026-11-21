### CommittedAgentFilesCache

#### Verification Approach

`CommittedAgentFilesCache` is verified with unit tests defined in
`CommittedAgentFilesCacheTests.cs`. Every test operates purely on in-memory repo-path/hash
keys and boolean values; no filesystem or git dependency is involved. The concurrent-use test runs the stores, lookups
and invalidations on thread-pool tasks started together and checks every value afterwards; it asserts no timing or
thread identity.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- A cached value is returned only for an exact repo-path-and-hash match.
- A different hash or an unknown repo path is treated as a cache miss rather than returning a
  stale or default value.
- Invalidating one repo path does not affect any other cached entry.
- Setting the same key twice overwrites the previous value.
- Values stored, looked up and invalidated from several threads at once are all kept and returned as stored.

#### Test Scenarios

**CommittedAgentFilesCache_Lookup_ReturnsCachedValueOnlyForExactMatch**: Requesting a value
for the same repo path and hash previously stored returns the cached value; requesting with a
different hash for the same repo path, or an entirely unknown repo path, is treated as a
cache miss. This scenario is tested by
`CommittedAgentFilesCache_SameRepoAndHash_ReturnsCachedValue`,
`CommittedAgentFilesCache_DifferentHash_IsTreatedAsCacheMiss`, and
`CommittedAgentFilesCache_UnknownRepoPath_IsTreatedAsCacheMiss`, covering
`AgentControl-CommittedAgentFilesCache-Cache`.

**CommittedAgentFilesCache_Mutation_InvalidatesOnlyTargetRepoAndOverwritesOnReset**:
Invalidating a repo path clears only that repo path's cached entry, leaving other entries
intact; setting the same key twice overwrites the previously cached value. This scenario is
tested by `CommittedAgentFilesCache_Invalidate_ClearsOnlyThatRepoPath` and
`CommittedAgentFilesCache_SetSameKeyTwice_OverwritesPreviousValue`, covering
`AgentControl-CommittedAgentFilesCache-Cache`.

**CommittedAgentFilesCache_ConcurrentUse_EveryStoredValueKept**: Sixty-four tasks started together each store a value
for its own repo and `HEAD`, invalidate an unrelated repo and read its value back; every task finds its value as
stored, and every value is still there afterwards. This scenario is tested by
`CommittedAgentFilesCache_ConcurrentSetAndGet_ReturnsEveryStoredValue`, covering
`AgentControl-CommittedAgentFilesCache-ConcurrentUse`.
