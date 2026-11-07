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
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DemaConsulting.AgentControl.Puppet;

/// <summary>
///     Which package formats <c>fixture</c> writes.
/// </summary>
internal enum PackageFormats
{
    /// <summary>
    ///     <c>{name}-{version}.zip</c> archives.
    /// </summary>
    Zip,

    /// <summary>
    ///     <c>{name}.{version}.nupkg</c> archives (the files under <c>content/</c>, plus a minimal nuspec).
    /// </summary>
    Nupkg,

    /// <summary>
    ///     Both, side by side.
    /// </summary>
    Both,
}

/// <summary>
///     A package a repository is pinned to (<c>.agentcontrol.json</c>).
/// </summary>
/// <param name="Name">The package name.</param>
/// <param name="Version">The package version.</param>
internal sealed record PackagePin(string Name, string Version);

/// <summary>
///     The options of the <c>fixture</c> command.
/// </summary>
/// <param name="Directory">The folder to create the fixture in (absolute).</param>
/// <param name="Formats">The package formats to write.</param>
/// <param name="CreateRepo">Whether to create a git repository and list it as a recent repo.</param>
/// <param name="Pin">The pin to write into the repository, or <see langword="null"/> for a never-synced repository.</param>
internal sealed record FixtureOptions(string Directory, PackageFormats Formats, bool CreateRepo, PackagePin? Pin)
{
    /// <summary>
    ///     Reads the options of <c>fixture</c>.
    /// </summary>
    /// <param name="parsed">The parsed command: the folder, <c>--packages zip|nupkg|both</c>, <c>--repo</c>, <c>--pin name@version</c>.</param>
    /// <returns>The options; <c>--pin</c> implies <c>--repo</c>.</returns>
    /// <exception cref="PuppetException">Thrown with a usage error when the folder is missing or an option is invalid.</exception>
    internal static FixtureOptions Parse(ParsedCommand parsed)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        const string usage = "agentcontrol-puppet fixture <dir> [--packages zip|nupkg|both] [--repo] [--pin <name>@<version>]";
        if (parsed.Positional.Count < 1 || string.IsNullOrWhiteSpace(parsed.Positional[0]))
        {
            throw PuppetException.Usage(usage);
        }

        var formats = parsed.GetOption("packages")?.ToLowerInvariant() switch
        {
            null when !parsed.HasOption("packages") => PackageFormats.Zip,
            "zip" => PackageFormats.Zip,
            "nupkg" => PackageFormats.Nupkg,
            "both" => PackageFormats.Both,
            _ => throw PuppetException.Usage("--packages must be zip, nupkg or both."),
        };

        PackagePin? pin = null;
        if (parsed.HasOption("pin"))
        {
            pin = ParsePin(parsed.GetOption("pin"));
        }

        return new FixtureOptions(Path.GetFullPath(parsed.Positional[0]), formats, parsed.HasOption("repo") || pin is not null, pin);
    }

    /// <summary>
    ///     Parses a <c>name@version</c> pin.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The pin.</returns>
    /// <exception cref="PuppetException">Thrown with a usage error when the text is not <c>name@version</c>.</exception>
    internal static PackagePin ParsePin(string? text)
    {
        var at = text?.LastIndexOf('@') ?? -1;
        if (text is null || at <= 0 || at == text.Length - 1)
        {
            throw PuppetException.Usage("--pin must be <name>@<version>, for example contoso-agents@1.0.0.");
        }

        var name = text[..at];
        var version = text[(at + 1)..];
        return FixtureBuilder.IsValidPackageName(name) && FixtureBuilder.IsValidVersion(version)
            ? new PackagePin(name, version)
            : throw PuppetException.Usage($"--pin '{text}' is not valid: the name takes letters, digits, '.', '-' and '_', the version looks like 1.2.3 or 1.2.3-beta.");
    }
}

/// <summary>
///     What <c>fixture</c> created.
/// </summary>
/// <param name="Root">The fixture folder.</param>
/// <param name="PackagesDirectory">The package source folder (the <c>PackageSourcePath</c> of the settings).</param>
/// <param name="Packages">The package files written.</param>
/// <param name="RepoDirectory">The git repository, or <see langword="null"/> when none was asked for.</param>
/// <param name="ConfigDirectory">The folder to pass to <c>launch --config-dir</c>.</param>
/// <param name="SettingsFile">The <c>settings.json</c> inside the config folder.</param>
internal sealed record FixtureResult(
    string Root,
    string PackagesDirectory,
    IReadOnlyList<string> Packages,
    string? RepoDirectory,
    string ConfigDirectory,
    string SettingsFile);

/// <summary>
///     Runs git for the fixture; replaceable so the fixture can be unit-tested without git.
/// </summary>
internal interface IGitRunner
{
    /// <summary>
    ///     Runs <c>git</c> and requires success.
    /// </summary>
    /// <param name="workingDirectory">The folder to run in.</param>
    /// <param name="arguments">The git arguments.</param>
    /// <exception cref="PuppetException">Thrown when git cannot be started or fails.</exception>
    void Run(string workingDirectory, IReadOnlyList<string> arguments);
}

/// <summary>
///     Runs the <c>git</c> found on the <c>PATH</c>.
/// </summary>
internal sealed class ProcessGitRunner : IGitRunner
{
    /// <inheritdoc />
    public void Run(string workingDirectory, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(FindGit())
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new PuppetException(ExitCodes.Error, "GIT_FAILED", "git could not be started.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new PuppetException(ExitCodes.Error, "GIT_FAILED", $"git {string.Join(' ', arguments)} failed with exit code {process.ExitCode}: {error.Result.Trim()} {output.Result.Trim()}".Trim());
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new PuppetException(ExitCodes.Error, "GIT_FAILED", $"git could not be started: {ex.Message}");
        }
    }

    /// <summary>
    ///     Finds <c>git</c> on the <c>PATH</c>.
    /// </summary>
    /// <returns>The absolute path of the executable.</returns>
    /// <exception cref="PuppetException">Thrown when there is no git on the PATH.</exception>
    private static string FindGit()
    {
        var names = OperatingSystem.IsWindows() ? new[] { "git.exe" } : new[] { "git" };
        var folders = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return folders
                .SelectMany(folder => names.Select(name => Path.Combine(folder, name)))
                .FirstOrDefault(File.Exists)
            ?? throw new PuppetException(ExitCodes.Error, "GIT_FAILED", "git was not found on the PATH.");
    }
}

/// <summary>
///     Builds the sandbox of the <c>fixture</c> command: sample packages, a git repository and a ready <c>settings.json</c>.
/// </summary>
internal static partial class FixtureBuilder
{
    /// <summary>
    ///     The note printed when the fixture contains <c>.nupkg</c> packages.
    /// </summary>
    internal const string NupkgNote =
        ".nupkg packages are prepared for NuGet package sources, which the current AgentControl (main) cannot consume yet; " +
        "they arrive with the multi-source packages feature. Use zip packages with the current application.";

    /// <summary>
    ///     The sample packages written to the package source: name and version.
    /// </summary>
    internal static readonly IReadOnlyList<(string Name, string Version)> SamplePackages =
    [
        ("contoso-agents", "1.0.0"),
        ("contoso-agents", "1.1.0"),
        ("fabrikam-agents", "2.0.0"),
    ];

    /// <summary>
    ///     The JSON options of the files that are written.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    ///     Tests whether a text is usable as a package name.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns><see langword="true"/> for letters, digits, <c>.</c>, <c>-</c> and <c>_</c>.</returns>
    internal static bool IsValidPackageName(string name) =>
        name.Length > 0 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');

    /// <summary>
    ///     Tests whether a text looks like a semantic version.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <returns><see langword="true"/> for <c>major.minor.patch</c> with an optional pre-release or build suffix.</returns>
    internal static bool IsValidVersion(string version) => VersionPattern().IsMatch(version);

    /// <summary>
    ///     Creates the fixture.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="git">Runs git for the repository.</param>
    /// <returns>What was created.</returns>
    /// <exception cref="PuppetException">Thrown when the folder is not empty or git fails.</exception>
    internal static FixtureResult Build(FixtureOptions options, IGitRunner git)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(git);
        if (Directory.Exists(options.Directory) && Directory.EnumerateFileSystemEntries(options.Directory).Any())
        {
            throw PuppetException.Usage($"fixture: '{options.Directory}' is not empty; give a new or empty folder.");
        }

        var packagesDirectory = Path.Combine(options.Directory, "packages");
        var configDirectory = Path.Combine(options.Directory, "config");
        Directory.CreateDirectory(packagesDirectory);
        Directory.CreateDirectory(configDirectory);

        var packages = new List<string>();
        foreach (var (name, version) in SamplePackages)
        {
            if (options.Formats is PackageFormats.Zip or PackageFormats.Both)
            {
                packages.Add(WriteZip(packagesDirectory, name, version));
            }

            if (options.Formats is PackageFormats.Nupkg or PackageFormats.Both)
            {
                packages.Add(WriteNupkg(packagesDirectory, name, version));
            }
        }

        string? repoDirectory = null;
        if (options.CreateRepo)
        {
            repoDirectory = Path.Combine(options.Directory, "repo");
            CreateRepository(repoDirectory, options.Pin, git);
        }

        var settingsFile = Path.Combine(configDirectory, "settings.json");
        File.WriteAllText(settingsFile, SettingsJson(packagesDirectory, repoDirectory, options.Pin));
        return new FixtureResult(options.Directory, packagesDirectory, packages, repoDirectory, configDirectory, settingsFile);
    }

    /// <summary>
    ///     Builds the content of <c>settings.json</c>: the package source and the recent-repos list.
    /// </summary>
    /// <remarks>
    ///     Only <c>PackageSourcePath</c> and <c>RecentRepos</c> are written: every other setting keeps its default, and
    ///     unknown settings of a newer version are never emitted, so the file loads in any version of the application.
    /// </remarks>
    /// <param name="packagesDirectory">The package source folder.</param>
    /// <param name="repoDirectory">The repository folder, or <see langword="null"/>.</param>
    /// <param name="pin">The pin, or <see langword="null"/>.</param>
    /// <returns>The JSON text.</returns>
    internal static string SettingsJson(string packagesDirectory, string? repoDirectory, PackagePin? pin)
    {
        var repos = repoDirectory is null
            ? []
            : new[] { new { Path = repoDirectory, PinnedPackageName = pin?.Name, PinnedPackageVersion = pin?.Version } };
        return JsonSerializer.Serialize(new { PackageSourcePath = packagesDirectory, RecentRepos = repos }, JsonOptions);
    }

    /// <summary>
    ///     Gets the files of a sample package, relative to the package root.
    /// </summary>
    /// <param name="name">The package name.</param>
    /// <param name="version">The package version.</param>
    /// <returns>The relative path (forward slashes) and text of every file.</returns>
    internal static IReadOnlyList<(string Path, string Content)> PackageFiles(string name, string version) =>
    [
        (".github/agents/sample.md", $"# Sample agent\n\nShipped by {name} {version}.\n"),
        (".github/standards/sample.md", $"# Sample standard\n\nShipped by {name} {version}.\n"),
        (".github/templates/sample.md", $"# Sample template\n\nShipped by {name} {version}.\n"),
        (".github/skills/sample/SKILL.md", $"# Sample skill\n\nShipped by {name} {version}.\n"),
        ("release-notes.md", $"# {name} {version}\n\n- Sample release notes of the fixture package.\n"),
        ("AGENTS.md", $"# AGENTS.md\n\nSample template shipped by {name} {version}.\n"),
    ];

    /// <summary>
    ///     Builds the minimal nuspec of a sample package.
    /// </summary>
    /// <param name="name">The package name.</param>
    /// <param name="version">The package version.</param>
    /// <returns>The XML text.</returns>
    internal static string Nuspec(string name, string version) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
        "<package xmlns=\"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd\">\n" +
        "  <metadata>\n" +
        $"    <id>{name}</id>\n" +
        $"    <version>{version}</version>\n" +
        "    <authors>AgentControl Puppet</authors>\n" +
        $"    <description>Sample agent package {name} {version} for AgentControl fixtures.</description>\n" +
        "  </metadata>\n" +
        "</package>\n";

    /// <summary>
    ///     Matches <c>major.minor.patch</c> with an optional pre-release or build suffix.
    /// </summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"^\d+\.\d+\.\d+([-+][0-9A-Za-z.\-+]+)?$")]
    private static partial Regex VersionPattern();

    /// <summary>
    ///     Writes a <c>{name}-{version}.zip</c> package.
    /// </summary>
    /// <param name="directory">The package source folder.</param>
    /// <param name="name">The package name.</param>
    /// <param name="version">The package version.</param>
    /// <returns>The file written.</returns>
    private static string WriteZip(string directory, string name, string version)
    {
        var path = Path.Combine(directory, $"{name}-{version}.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (file, content) in PackageFiles(name, version))
        {
            AddEntry(archive, file, content);
        }

        return path;
    }

    /// <summary>
    ///     Writes a <c>{name}.{version}.nupkg</c> package: the files under <c>content/</c> and a minimal nuspec.
    /// </summary>
    /// <param name="directory">The package source folder.</param>
    /// <param name="name">The package name.</param>
    /// <param name="version">The package version.</param>
    /// <returns>The file written.</returns>
    private static string WriteNupkg(string directory, string name, string version)
    {
        var path = Path.Combine(directory, $"{name}.{version}.nupkg");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddEntry(archive, $"{name}.nuspec", Nuspec(name, version));
        foreach (var (file, content) in PackageFiles(name, version))
        {
            AddEntry(archive, "content/" + file, content);
        }

        return path;
    }

    /// <summary>
    ///     Adds a UTF-8 text entry to an archive.
    /// </summary>
    /// <param name="archive">The archive.</param>
    /// <param name="entryName">The entry name (forward slashes).</param>
    /// <param name="content">The text.</param>
    private static void AddEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    /// <summary>
    ///     Creates a git repository with one commit (and the pin file, when there is one).
    /// </summary>
    /// <param name="directory">The repository folder.</param>
    /// <param name="pin">The pin, or <see langword="null"/>.</param>
    /// <param name="git">Runs git.</param>
    private static void CreateRepository(string directory, PackagePin? pin, IGitRunner git)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "README.md"), "# Sample repository\n\nCreated by agentcontrol-puppet fixture.\n");
        if (pin is not null)
        {
            File.WriteAllText(
                Path.Combine(directory, ".agentcontrol.json"),
                JsonSerializer.Serialize(new { PackageName = pin.Name, pin.Version }, JsonOptions));
        }

        git.Run(directory, ["init", "-b", "main"]);
        git.Run(directory, ["add", "-A"]);
        git.Run(directory, ["-c", "user.name=AgentControl Puppet", "-c", "user.email=puppet@agentcontrol.invalid", "-c", "commit.gpgsign=false", "commit", "-m", "Initial commit"]);
    }
}
