// Copyright (c) DEMA Consulting
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

namespace DemaConsulting.AgentControl.Puppet.Tests;

/// <summary>
///     A temporary folder that is deleted when the test ends.
/// </summary>
internal sealed class TempFolder : IDisposable
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="TempFolder"/> class and creates the folder.
    /// </summary>
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AgentControlPuppetTests-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(Path);
    }

    /// <summary>
    ///     Gets the folder's full path.
    /// </summary>
    public string Path { get; }

    /// <summary>
    ///     Deletes the folder with everything in it (best effort).
    /// </summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A locked temp file must not fail the test
        }
    }
}

/// <summary>
///     Tests for the instance records that let a stateless command find a launched instance, and their validation
///     against stale process ids, reused process ids and a different config folder (no real process is inspected).
/// </summary>
public sealed class InstanceRegistryTests : IDisposable
{
    /// <summary>
    ///     The moment the recorded process started.
    /// </summary>
    private static readonly DateTime Started = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    ///     The folder the records are written to.
    /// </summary>
    private readonly TempFolder _folder = new();

    /// <summary>
    ///     The pretend process table.
    /// </summary>
    private readonly FakeInspector _processes = new();

    /// <summary>
    ///     Releases the temp folder.
    /// </summary>
    public void Dispose() => _folder.Dispose();

    /// <summary>
    ///     Validates that a saved record is read back unchanged.
    /// </summary>
    [Fact]
    public void InstanceRegistry_Save_ThenTryLoad_RoundTrips()
    {
        // Arrange
        var registry = Registry();
        var record = Record("a", 1234);

        // Act
        registry.Save(record);
        var loaded = registry.TryLoad("a");

        // Assert
        Assert.Equal(record, loaded);
    }

    /// <summary>
    ///     Validates that an alias without a record, and a corrupt record file, load as nothing.
    /// </summary>
    [Fact]
    public void InstanceRegistry_TryLoad_MissingOrCorrupt_IsNull()
    {
        // Arrange
        var registry = Registry();
        Directory.CreateDirectory(_folder.Path);
        File.WriteAllText(Path.Combine(_folder.Path, "bad.json"), "{ not json");

        // Act
        var missing = registry.TryLoad("nothing");
        var corrupt = registry.TryLoad("bad");

        // Assert
        Assert.Multiple(
            () => Assert.Null(missing),
            () => Assert.Null(corrupt));
    }

    /// <summary>
    ///     Validates that an alias that could name a file outside the folder is refused.
    /// </summary>
    [Fact]
    public void InstanceRegistry_TryLoad_PathLikeAlias_IsRefused()
    {
        // Arrange
        var registry = Registry();

        // Act / Assert
        Assert.Throws<ArgumentException>(() => registry.TryLoad(@"..\escape"));
    }

    /// <summary>
    ///     Validates that a live process with the recorded start time and config folder is running.
    /// </summary>
    [Fact]
    public void InstanceRegistry_Validate_MatchingProcess_IsRunning()
    {
        // Arrange
        var registry = Registry();
        _processes.Add(1234, Started, CommandLine(@"C:\cfg\one"));

        // Act
        var state = registry.Validate(Record("a", 1234, @"C:\cfg\one"));

        // Assert
        Assert.Equal(InstanceState.Running, state);
    }

    /// <summary>
    ///     Validates that a process id with no process behind it is a stale record.
    /// </summary>
    [Fact]
    public void InstanceRegistry_Validate_NoProcess_IsNotRunning()
    {
        // Arrange
        var registry = Registry();

        // Act
        var state = registry.Validate(Record("a", 4321));

        // Assert
        Assert.Equal(InstanceState.NotRunning, state);
    }

    /// <summary>
    ///     Validates that a process id now used by a program that started at another time is a reused id.
    /// </summary>
    [Fact]
    public void InstanceRegistry_Validate_DifferentStartTime_IsPidReused()
    {
        // Arrange
        var registry = Registry();
        _processes.Add(1234, Started.AddHours(3), CommandLine(@"C:\cfg\one"));

        // Act
        var state = registry.Validate(Record("a", 1234, @"C:\cfg\one"));

        // Assert
        Assert.Equal(InstanceState.PidReused, state);
    }

    /// <summary>
    ///     Validates that a start time a second away (clock granularity) is still the same process.
    /// </summary>
    [Fact]
    public void InstanceRegistry_Validate_StartTimeWithinTolerance_IsRunning()
    {
        // Arrange
        var registry = Registry();
        _processes.Add(1234, Started.AddSeconds(1), CommandLine(@"C:\cfg\one"));

        // Act
        var state = registry.Validate(Record("a", 1234, @"C:\cfg\one"));

        // Assert
        Assert.Equal(InstanceState.Running, state);
    }

    /// <summary>
    ///     Validates that a process started with another config folder is not the recorded instance.
    /// </summary>
    [Fact]
    public void InstanceRegistry_Validate_OtherConfigDir_IsConfigMismatch()
    {
        // Arrange
        var registry = Registry();
        _processes.Add(1234, Started, CommandLine(@"C:\cfg\other"));

        // Act
        var state = registry.Validate(Record("a", 1234, @"C:\cfg\one"));

        // Assert
        Assert.Equal(InstanceState.ConfigMismatch, state);
    }

    /// <summary>
    ///     Validates that an unreadable command line does not disqualify a process whose start time matches.
    /// </summary>
    [Fact]
    public void InstanceRegistry_Validate_UnreadableCommandLine_IsRunning()
    {
        // Arrange
        var registry = Registry();
        _processes.Add(1234, Started, null);

        // Act
        var state = registry.Validate(Record("a", 1234, @"C:\cfg\one"));

        // Assert
        Assert.Equal(InstanceState.Running, state);
    }

    /// <summary>
    ///     Validates how the command line is checked for the recorded config folder.
    /// </summary>
    /// <param name="commandLine">The process command line.</param>
    /// <param name="expected">Whether it names the folder after <c>--config-dir</c>.</param>
    [Theory]
    [InlineData("\"C:\\app\\AgentControl.exe\" --config-dir \"C:\\cfg\\one\"", true)]
    [InlineData("AgentControl.exe --config-dir C:\\cfg\\one --git-path git", true)]
    [InlineData("AgentControl.exe --config-dir c:\\CFG\\ONE", true)]
    [InlineData("AgentControl.exe --config-dir C:\\cfg\\two", false)]
    [InlineData("AgentControl.exe --config-dir C:\\cfg\\one2", false)]
    [InlineData("AgentControl.exe --git-path C:\\cfg\\one", false)]
    [InlineData("AgentControl.exe", false)]
    public void InstanceRegistry_CommandLineHasConfigDir_ChecksFolderAfterFlag(string commandLine, bool expected)
    {
        // Arrange / Act
        var result = InstanceRegistry.CommandLineHasConfigDir(commandLine, @"C:\cfg\one\");

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    ///     Validates that a live record is returned and a stale one is not, with the reason.
    /// </summary>
    [Fact]
    public void InstanceRegistry_TryGetLive_ReturnsLiveRecordOnly()
    {
        // Arrange
        var registry = Registry();
        registry.Save(Record("live", 100));
        registry.Save(Record("stale", 200));
        _processes.Add(100, Started, CommandLine(@"C:\cfg\one"));

        // Act
        var live = registry.TryGetLive("live", out var liveState);
        var stale = registry.TryGetLive("stale", out var staleState);
        var none = registry.TryGetLive("none", out var noneState);

        // Assert
        Assert.Multiple(
            () => Assert.Equal(100, live?.Pid),
            () => Assert.Equal(InstanceState.Running, liveState),
            () => Assert.Null(stale),
            () => Assert.Equal(InstanceState.NotRunning, staleState),
            () => Assert.Null(none),
            () => Assert.Equal(InstanceState.NotRunning, noneState));
    }

    /// <summary>
    ///     Validates that listing returns the live instances in alias order and removes the stale records.
    /// </summary>
    [Fact]
    public void InstanceRegistry_ListAndPrune_KeepsLiveRemovesStale()
    {
        // Arrange
        var registry = Registry();
        registry.Save(Record("b-live", 100));
        registry.Save(Record("a-live", 101));
        registry.Save(Record("gone", 102));
        registry.Save(Record("reused", 103));
        _processes.Add(100, Started, CommandLine(@"C:\cfg\one"));
        _processes.Add(101, Started, CommandLine(@"C:\cfg\one"));
        _processes.Add(103, Started.AddDays(1), CommandLine(@"C:\cfg\one"));

        // Act
        var live = registry.ListAndPrune(out var pruned);

        // Assert
        Assert.Multiple(
            () => Assert.Equal(["a-live", "b-live"], live.Select(r => r.Alias)),
            () => Assert.Equal([("gone", InstanceState.NotRunning), ("reused", InstanceState.PidReused)], pruned),
            () => Assert.Null(registry.TryLoad("gone")),
            () => Assert.Null(registry.TryLoad("reused")),
            () => Assert.NotNull(registry.TryLoad("a-live")));
    }

    /// <summary>
    ///     Validates that listing an empty (not yet created) registry yields nothing.
    /// </summary>
    [Fact]
    public void InstanceRegistry_ListAndPrune_NoFolder_IsEmpty()
    {
        // Arrange
        var registry = new InstanceRegistry(Path.Combine(_folder.Path, "missing"), _processes);

        // Act
        var live = registry.ListAndPrune(out var pruned);

        // Assert
        Assert.Multiple(
            () => Assert.Empty(live),
            () => Assert.Empty(pruned));
    }

    /// <summary>
    ///     Validates which aliases are accepted.
    /// </summary>
    /// <param name="alias">The alias.</param>
    /// <param name="valid">Whether it is valid.</param>
    [Theory]
    [InlineData("main", true)]
    [InlineData("a-1_b", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("a b", false)]
    [InlineData("a/b", false)]
    [InlineData("..", false)]
    public void InstanceRegistry_IsValidAlias_AcceptsLettersDigitsDashUnderscore(string? alias, bool valid)
    {
        // Arrange / Act
        var result = InstanceRegistry.IsValidAlias(alias);

        // Assert
        Assert.Equal(valid, result);
    }

    /// <summary>
    ///     Creates the registry under the temp folder with the pretend process table.
    /// </summary>
    /// <returns>The registry.</returns>
    private InstanceRegistry Registry() => new(_folder.Path, _processes);

    /// <summary>
    ///     Creates a record started at <see cref="Started"/>.
    /// </summary>
    /// <param name="alias">The alias.</param>
    /// <param name="pid">The process id.</param>
    /// <param name="configDir">The config folder.</param>
    /// <returns>The record.</returns>
    private static InstanceRecord Record(string alias, int pid, string configDir = @"C:\cfg\one") =>
        new(alias, pid, Started, @"C:\app\DemaConsulting.AgentControl.exe", configDir);

    /// <summary>
    ///     Builds the command line AgentControl is launched with.
    /// </summary>
    /// <param name="configDir">The config folder.</param>
    /// <returns>The command line.</returns>
    private static string CommandLine(string configDir) => $"\"C:\\app\\DemaConsulting.AgentControl.exe\" --config-dir \"{configDir}\"";

    /// <summary>
    ///     A process table the tests fill in.
    /// </summary>
    private sealed class FakeInspector : IProcessInspector
    {
        /// <summary>
        ///     The pretend processes by id.
        /// </summary>
        private readonly Dictionary<int, ProcessSnapshot> _table = [];

        /// <summary>
        ///     Adds a pretend process.
        /// </summary>
        /// <param name="pid">The process id.</param>
        /// <param name="start">The start time (UTC).</param>
        /// <param name="commandLine">The command line, or <see langword="null"/> when unreadable.</param>
        public void Add(int pid, DateTime start, string? commandLine) => _table[pid] = new ProcessSnapshot(start, commandLine);

        /// <inheritdoc />
        public ProcessSnapshot? Inspect(int pid) => _table.GetValueOrDefault(pid);
    }
}
