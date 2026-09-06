### StartupOptions

#### Verification Approach

`StartupOptions` is verified with unit tests defined in `StartupOptionsTests.cs`. Every test
constructs an in-memory `string[]` argument array and inspects the parsed result or thrown
exception; no filesystem or process dependency is involved.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Every supported override (configuration directory, git executable, agent-tool command) is
  captured accurately, individually and in combination.
- Unspecified overrides remain null.
- An unsupported argument, a missing value for a recognized argument, or a null arguments
  array is rejected with a clear exception.

#### Test Scenarios

**StartupOptions_Parse_CapturesEachSupportedOverride**: Parsing with no arguments leaves all
overrides null; parsing a configuration-directory argument, a git-path argument, or an
agent-tool-command argument individually captures each value; and parsing all three together
captures all values. This scenario is tested by
`StartupOptions_Parse_NoArguments_AllOverridesNull`,
`StartupOptions_Parse_ConfigDirArgument_CapturesValue`,
`StartupOptions_Parse_GitPathArgument_CapturesValue`,
`StartupOptions_Parse_AgentToolCommandArgument_CapturesValue`, and
`StartupOptions_Parse_AllArguments_CapturesAllValues`, covering
`AgentControl-StartupOptions-Parse`.

**StartupOptions_Parse_RejectsUnsupportedMissingOrNullArguments**: An unsupported argument
and a recognized argument with a missing value both throw `ArgumentException`, and a null
arguments array throws `ArgumentNullException`. This scenario is tested by
`StartupOptions_Parse_UnsupportedArgument_ThrowsArgumentException`,
`StartupOptions_Parse_MissingValue_ThrowsArgumentException`, and
`StartupOptions_Parse_NullArguments_ThrowsArgumentNullException`, covering
`AgentControl-StartupOptions-Parse`.
