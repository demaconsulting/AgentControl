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

using System.Diagnostics;
using System.Management;
using System.Text.Json;

namespace DemaConsulting.AgentControl.Puppet;

/// <summary>
///     What <c>launch</c> remembers about one AgentControl instance it started.
/// </summary>
/// <param name="Alias">The instance alias.</param>
/// <param name="Pid">The process id.</param>
/// <param name="StartTimeUtc">The process start time (UTC); together with the id it identifies the process, so a reused id is detected.</param>
/// <param name="ExePath">The executable that was started.</param>
/// <param name="ConfigDir">The <c>--config-dir</c> the instance was started with (absolute).</param>
internal sealed record InstanceRecord(string Alias, int Pid, DateTime StartTimeUtc, string ExePath, string ConfigDir);

/// <summary>
///     The verdict on a stored <see cref="InstanceRecord"/>.
/// </summary>
internal enum InstanceState
{
    /// <summary>
    ///     The recorded process is alive and is the one that was started.
    /// </summary>
    Running,

    /// <summary>
    ///     No process with the recorded id exists (the instance exited or was killed).
    /// </summary>
    NotRunning,

    /// <summary>
    ///     A process with the recorded id exists but started at a different time: the id was reused by another program.
    /// </summary>
    PidReused,

    /// <summary>
    ///     The process's command line does not carry the recorded <c>--config-dir</c>: it is not the recorded instance.
    /// </summary>
    ConfigMismatch,
}

/// <summary>
///     What is known about a live process.
/// </summary>
/// <param name="StartTimeUtc">The process start time (UTC).</param>
/// <param name="CommandLine">The command line, or <see langword="null"/> when it cannot be read (for example a protected process).</param>
internal sealed record ProcessSnapshot(DateTime StartTimeUtc, string? CommandLine);

/// <summary>
///     Looks at running processes; replaceable so the registry can be unit-tested without real processes.
/// </summary>
internal interface IProcessInspector
{
    /// <summary>
    ///     Inspects a process.
    /// </summary>
    /// <param name="pid">The process id.</param>
    /// <returns>The snapshot, or <see langword="null"/> when no such process is running.</returns>
    ProcessSnapshot? Inspect(int pid);
}

/// <summary>
///     Inspects real processes: start time from the process object, command line from WMI.
/// </summary>
internal sealed class SystemProcessInspector : IProcessInspector
{
    /// <inheritdoc />
    public ProcessSnapshot? Inspect(int pid)
    {
        DateTime start;
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited)
            {
                return null;
            }

            start = process.StartTime.ToUniversalTime();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }

        return new ProcessSnapshot(start, ReadCommandLine(pid));
    }

    /// <summary>
    ///     Reads a process's command line with WMI.
    /// </summary>
    /// <param name="pid">The process id.</param>
    /// <returns>The command line, or <see langword="null"/> when WMI does not provide it.</returns>
    private static string? ReadCommandLine(int pid)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
            using var results = searcher.Get();
            using var first = results.Cast<ManagementBaseObject>().FirstOrDefault();
            return first?["CommandLine"] as string;
        }
        catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            // WMI unavailable: the start time still identifies the process
        }

        return null;
    }
}

/// <summary>
///     The per-alias instance records that let a stateless command find the AgentControl process <c>launch</c> started.
/// </summary>
/// <remarks>
///     AgentControl has no automation endpoint and every window shares a handful of titles, so the Puppet remembers the
///     instances it launched in one small JSON file per alias. Every command validates the record before using it: the
///     process must be alive, must have started when the record says (an id can be reused) and must carry the recorded
///     <c>--config-dir</c> on its command line.
/// </remarks>
internal sealed class InstanceRegistry
{
    /// <summary>
    ///     The alias used when a command gives no <c>--instance</c>.
    /// </summary>
    internal const string DefaultAlias = "main";

    /// <summary>
    ///     The JSON options of the record files.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    ///     The folder with the record files.
    /// </summary>
    private readonly string _directory;

    /// <summary>
    ///     The process inspector.
    /// </summary>
    private readonly IProcessInspector _inspector;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InstanceRegistry"/> class.
    /// </summary>
    /// <param name="directory">The folder with the record files (created on the first save).</param>
    /// <param name="inspector">The process inspector.</param>
    internal InstanceRegistry(string directory, IProcessInspector inspector)
    {
        _directory = directory;
        _inspector = inspector;
    }

    /// <summary>
    ///     Gets the folder the Puppet keeps its per-user files in (instance records, temporary config folders).
    /// </summary>
    internal static string DefaultRoot => Path.Combine(Path.GetTempPath(), "AgentControlPuppet");

    /// <summary>
    ///     Creates the registry the commands use: records under the temp folder, real process inspection.
    /// </summary>
    /// <returns>The registry.</returns>
    internal static InstanceRegistry CreateDefault() =>
        new(Path.Combine(DefaultRoot, "instances"), new SystemProcessInspector());

    /// <summary>
    ///     Tests whether an alias is usable: letters, digits, <c>-</c> and <c>_</c>.
    /// </summary>
    /// <param name="alias">The alias.</param>
    /// <returns><see langword="true"/> when it is valid.</returns>
    internal static bool IsValidAlias(string? alias) =>
        !string.IsNullOrEmpty(alias) && alias.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>
    ///     Tests whether a process command line carries a <c>--config-dir</c> argument that names exactly the recorded folder.
    /// </summary>
    /// <remarks>
    ///     The command line is split into arguments with the Windows rules, and the value after the last <c>--config-dir</c>
    ///     (the application keeps the last one) is compared with the recorded folder after both are made absolute and stripped
    ///     of trailing separators; the comparison ignores case. A folder inside the recorded one, or one that merely shares
    ///     its prefix, is a different folder. The application accepts only the two-argument form (<c>--config-dir &lt;dir&gt;</c>).
    /// </remarks>
    /// <param name="commandLine">The process command line.</param>
    /// <param name="configDir">The recorded config folder.</param>
    /// <returns><see langword="true"/> when the command line names the folder after <c>--config-dir</c>.</returns>
    internal static bool CommandLineHasConfigDir(string commandLine, string configDir)
    {
        var arguments = WindowsCommandLine.Split(commandLine);
        var flag = arguments.FindLastIndex(argument => string.Equals(argument, "--config-dir", StringComparison.Ordinal));
        return flag >= 0 && flag + 1 < arguments.Count && SamePath(arguments[flag + 1], configDir);
    }

    /// <summary>
    ///     Tests whether two folder paths name the same folder: absolute, without trailing separators, ignoring case.
    /// </summary>
    /// <param name="left">The first path.</param>
    /// <param name="right">The second path.</param>
    /// <returns><see langword="true"/> when they are equal after normalization; <see langword="false"/> also for an invalid path.</returns>
    internal static bool SamePath(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Opens a handle to the process of a record and proves it is the recorded instance.
    /// </summary>
    /// <remarks>
    ///     The handle is opened <b>before</b> the record is validated and stays open, which keeps Windows from handing the
    ///     process id to another program meanwhile: whatever is done through the returned handle (waiting, killing) acts on
    ///     the validated process and never on a program that reused the id.
    /// </remarks>
    /// <param name="record">The record.</param>
    /// <param name="state">The verdict on the record.</param>
    /// <returns>The process handle (the caller disposes it), or <see langword="null"/> when the instance is gone or the id was reused.</returns>
    internal Process? TryOpenValidated(InstanceRecord record, out InstanceState state)
    {
        ArgumentNullException.ThrowIfNull(record);
        Process process;
        try
        {
            process = Process.GetProcessById(record.Pid);
        }
        catch (ArgumentException)
        {
            state = InstanceState.NotRunning;
            return null;
        }

        state = Validate(record);
        if (state == InstanceState.Running && !HasExited(process))
        {
            return process;
        }

        state = state == InstanceState.Running ? InstanceState.NotRunning : state;
        process.Dispose();
        return null;
    }

    /// <summary>
    ///     Opens a handle to the live instance of an alias (see <see cref="TryOpenValidated"/>).
    /// </summary>
    /// <param name="alias">The alias.</param>
    /// <param name="record">The alias's record, or <see langword="null"/> when there is none.</param>
    /// <param name="state">The verdict (<see cref="InstanceState.NotRunning"/> when there is no record).</param>
    /// <returns>The validated process handle, or <see langword="null"/> when the alias has no live instance.</returns>
    internal Process? TryOpenLive(string alias, out InstanceRecord? record, out InstanceState state)
    {
        record = TryLoad(alias);
        if (record is null)
        {
            state = InstanceState.NotRunning;
            return null;
        }

        return TryOpenValidated(record, out state);
    }

    /// <summary>
    ///     Tests whether a process handle's process has exited.
    /// </summary>
    /// <param name="process">The process.</param>
    /// <returns><see langword="true"/> when it has exited (or its state cannot be read).</returns>
    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return true;
        }
    }

    /// <summary>
    ///     Writes (or replaces) the record of an alias.
    /// </summary>
    /// <param name="record">The record.</param>
    internal void Save(InstanceRecord record)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FileOf(record.Alias), JsonSerializer.Serialize(record, JsonOptions));
    }

    /// <summary>
    ///     Reads the record of an alias.
    /// </summary>
    /// <param name="alias">The alias.</param>
    /// <returns>The record, or <see langword="null"/> when there is none or the file is unreadable.</returns>
    internal InstanceRecord? TryLoad(string alias)
    {
        var file = FileOf(alias);
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            var record = JsonSerializer.Deserialize<InstanceRecord>(File.ReadAllText(file));
            return record is { Pid: > 0 } && IsValidAlias(record.Alias) ? record : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Removes the record of an alias (nothing happens when there is none).
    /// </summary>
    /// <param name="alias">The alias.</param>
    internal void Delete(string alias)
    {
        try
        {
            File.Delete(FileOf(alias));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A record that cannot be removed is validated (and rejected) again next time
        }
    }

    /// <summary>
    ///     Decides whether a record still describes the process it was written for.
    /// </summary>
    /// <param name="record">The record.</param>
    /// <returns>The verdict.</returns>
    internal InstanceState Validate(InstanceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (_inspector.Inspect(record.Pid) is not { } snapshot)
        {
            return InstanceState.NotRunning;
        }

        // The same API reads the start time when the record is written and when it is checked, so the values of one
        // process are identical to the tick; any difference at all means the id now belongs to another process
        if (snapshot.StartTimeUtc != record.StartTimeUtc)
        {
            return InstanceState.PidReused;
        }

        // An unreadable command line is not held against the process: the start time already matched
        return snapshot.CommandLine is { } line && !CommandLineHasConfigDir(line, record.ConfigDir)
            ? InstanceState.ConfigMismatch
            : InstanceState.Running;
    }

    /// <summary>
    ///     Gets the record of an alias when it still describes a live instance.
    /// </summary>
    /// <param name="alias">The alias.</param>
    /// <param name="state">The verdict (<see cref="InstanceState.NotRunning"/> when there is no record).</param>
    /// <returns>The record, or <see langword="null"/> when the alias has no live instance.</returns>
    internal InstanceRecord? TryGetLive(string alias, out InstanceState state)
    {
        var record = TryLoad(alias);
        if (record is null)
        {
            state = InstanceState.NotRunning;
            return null;
        }

        state = Validate(record);
        return state == InstanceState.Running ? record : null;
    }

    /// <summary>
    ///     Lists the live instances and removes the records of the ones that are gone.
    /// </summary>
    /// <param name="pruned">Receives the aliases whose records were removed, with the reason.</param>
    /// <returns>The live instances, sorted by alias.</returns>
    internal IReadOnlyList<InstanceRecord> ListAndPrune(out IReadOnlyList<(string Alias, InstanceState State)> pruned)
    {
        var live = new List<InstanceRecord>();
        var removed = new List<(string, InstanceState)>();
        pruned = removed;
        if (!Directory.Exists(_directory))
        {
            return live;
        }

        foreach (var file in Directory.EnumerateFiles(_directory, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            var alias = Path.GetFileNameWithoutExtension(file);
            if (!IsValidAlias(alias))
            {
                continue;
            }

            var record = TryLoad(alias);
            var state = record is null ? InstanceState.NotRunning : Validate(record);
            if (state == InstanceState.Running)
            {
                live.Add(record!);
            }
            else
            {
                Delete(alias);
                removed.Add((alias, state));
            }
        }

        return live;
    }

    /// <summary>
    ///     Gets the record file of an alias.
    /// </summary>
    /// <param name="alias">The alias.</param>
    /// <returns>The path.</returns>
    /// <exception cref="ArgumentException">Thrown when the alias is not valid (it would name a file outside the folder).</exception>
    private string FileOf(string alias) =>
        IsValidAlias(alias)
            ? Path.Combine(_directory, alias + ".json")
            : throw new ArgumentException($"'{alias}' is not a valid instance alias.", nameof(alias));
}
