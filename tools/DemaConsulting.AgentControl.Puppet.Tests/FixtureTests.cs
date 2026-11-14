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

using System.IO.Compression;
using System.Text.Json;

namespace DemaConsulting.AgentControl.Puppet.Tests;

/// <summary>
///     Tests for the <c>fixture</c> command: option parsing and the sandbox it creates (git is replaced by a recorder).
/// </summary>
public sealed class FixtureTests : IDisposable
{
    /// <summary>
    ///     The folder the fixtures are created under.
    /// </summary>
    private readonly TempFolder _folder = new();

    /// <summary>
    ///     Releases the temp folder.
    /// </summary>
    public void Dispose() => _folder.Dispose();

    /// <summary>
    ///     Validates that the defaults are zip packages and no repository.
    /// </summary>
    [Fact]
    public void FixtureOptions_Parse_FolderOnly_ZipPackagesNoRepo()
    {
        // Arrange
        var parsed = CommandLineParser.Parse(["fixture", "fx"]);

        // Act
        var options = FixtureOptions.Parse(parsed);

        // Assert
        Assert.Multiple(
            () => Assert.Equal(Path.GetFullPath("fx"), options.Directory),
            () => Assert.Equal(PackageFormats.Zip, options.Formats),
            () => Assert.False(options.CreateRepo),
            () => Assert.Null(options.Pin));
    }

    /// <summary>
    ///     Validates that <c>--pin</c> is parsed and implies a repository.
    /// </summary>
    [Fact]
    public void FixtureOptions_Parse_Pin_ImpliesRepo()
    {
        // Arrange
        var parsed = CommandLineParser.Parse(["fixture", "fx", "--packages", "BOTH", "--pin", "contoso-agents@1.0.0"]);

        // Act
        var options = FixtureOptions.Parse(parsed);

        // Assert
        Assert.Multiple(
            () => Assert.Equal(PackageFormats.Both, options.Formats),
            () => Assert.True(options.CreateRepo),
            () => Assert.Equal(new PackagePin("contoso-agents", "1.0.0"), options.Pin));
    }

    /// <summary>
    ///     Validates that a missing folder, an unknown format and a malformed pin are usage errors.
    /// </summary>
    /// <param name="line">The command line.</param>
    [Theory]
    [InlineData("fixture")]
    [InlineData("fixture fx --packages rar")]
    [InlineData("fixture fx --packages")]
    [InlineData("fixture fx --pin no-at-sign")]
    [InlineData("fixture fx --pin @1.0.0")]
    [InlineData("fixture fx --pin name@")]
    [InlineData("fixture fx --pin name@one")]
    [InlineData("fixture fx --pin bad/name@1.0.0")]
    [InlineData("fixture fx --pin")]
    public void FixtureOptions_Parse_Invalid_IsUsageError(string line)
    {
        // Arrange
        var parsed = CommandLineParser.Parse(line.Split(' '));

        // Act
        var exception = Assert.Throws<PuppetException>(() => FixtureOptions.Parse(parsed));

        // Assert
        Assert.Equal(ExitCodes.UsageError, exception.ExitCode);
    }

    /// <summary>
    ///     Validates that the zip fixture holds the sample packages with the layout the application extracts.
    /// </summary>
    [Fact]
    public void FixtureBuilder_Build_Zip_WritesPackagesWithManagedFolders()
    {
        // Arrange
        var git = new RecordingGit();

        // Act
        var result = FixtureBuilder.Build(Options("zip"), git);

        // Assert
        var names = result.Packages.Select(Path.GetFileName).ToList();
        using var archive = ZipFile.OpenRead(Path.Combine(result.PackagesDirectory, "contoso-agents-1.1.0.zip"));
        var entries = archive.Entries.Select(e => e.FullName).ToList();
        Assert.Multiple(
            () => Assert.Equal(["contoso-agents-1.0.0.zip", "contoso-agents-1.1.0.zip", "fabrikam-agents-2.0.0.zip"], names),
            () => Assert.Contains(".github/agents/sample.md", entries),
            () => Assert.Contains(".github/standards/sample.md", entries),
            () => Assert.Contains(".github/templates/sample.md", entries),
            () => Assert.Contains(".github/skills/sample/SKILL.md", entries),
            () => Assert.Contains("release-notes.md", entries),
            () => Assert.Contains("AGENTS.md", entries),
            () => Assert.Null(result.RepoDirectory),
            () => Assert.Empty(git.Calls));
    }

    /// <summary>
    ///     Validates that the nupkg fixture holds a nuspec and the files under <c>content/</c>.
    /// </summary>
    [Fact]
    public void FixtureBuilder_Build_Nupkg_WritesNuspecAndContentFolder()
    {
        // Arrange / Act
        var result = FixtureBuilder.Build(Options("nupkg"), new RecordingGit());

        // Assert
        using var archive = ZipFile.OpenRead(Path.Combine(result.PackagesDirectory, "contoso-agents.1.0.0.nupkg"));
        var entries = archive.Entries.Select(e => e.FullName).ToList();
        string nuspec;
        using (var reader = new StreamReader(archive.GetEntry("contoso-agents.nuspec")!.Open()))
        {
            nuspec = reader.ReadToEnd();
        }

        Assert.Multiple(
            () => Assert.All(result.Packages, p => Assert.EndsWith(".nupkg", p, StringComparison.Ordinal)),
            () => Assert.Equal(3, result.Packages.Count),
            () => Assert.Contains("content/.github/agents/sample.md", entries),
            () => Assert.Contains("content/.github/skills/sample/SKILL.md", entries),
            () => Assert.Contains("content/release-notes.md", entries),
            () => Assert.Contains("content/AGENTS.md", entries),
            () => Assert.Contains("<id>contoso-agents</id>", nuspec, StringComparison.Ordinal),
            () => Assert.Contains("<version>1.0.0</version>", nuspec, StringComparison.Ordinal),
            () => Assert.DoesNotContain(".github/agents/sample.md", entries));
    }

    /// <summary>
    ///     Validates that <c>both</c> writes the two formats side by side.
    /// </summary>
    [Fact]
    public void FixtureBuilder_Build_Both_WritesZipAndNupkg()
    {
        // Arrange / Act
        var result = FixtureBuilder.Build(Options("both"), new RecordingGit());

        // Assert
        Assert.Multiple(
            () => Assert.Equal(6, result.Packages.Count),
            () => Assert.Equal(3, result.Packages.Count(p => p.EndsWith(".zip", StringComparison.Ordinal))),
            () => Assert.Equal(3, result.Packages.Count(p => p.EndsWith(".nupkg", StringComparison.Ordinal))));
    }

    /// <summary>
    ///     Validates that the settings file has the shape the application reads: the package sources and the recent repos,
    ///     and that the pin names the source (current schema).
    /// </summary>
    [Fact]
    public void FixtureBuilder_Build_WithRepoAndPin_WritesSettingsShapeOfTheApplication()
    {
        // Arrange
        var git = new RecordingGit();

        // Act
        var result = FixtureBuilder.Build(Options("zip", repo: true, pin: new PackagePin("contoso-agents", "1.0.0")), git);

        // Assert
        using var settings = JsonDocument.Parse(File.ReadAllText(result.SettingsFile));
        var root = settings.RootElement;
        var source = Assert.Single(root.GetProperty("Sources").EnumerateArray());
        var repo = root.GetProperty("RecentRepos")[0];
        var pin = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.RepoDirectory!, ".agentcontrol.json"))).RootElement;
        Assert.Multiple(
            () => Assert.Equal(Path.Combine(result.Root, "config", "settings.json"), result.SettingsFile),
            () => Assert.Equal("Default", source.GetProperty("Alias").GetString()),
            () => Assert.Equal("Zip", source.GetProperty("Type").GetString()),
            () => Assert.Equal(result.PackagesDirectory, source.GetProperty("Location").GetString()),
            () => Assert.Equal(result.RepoDirectory, repo.GetProperty("Path").GetString()),
            () => Assert.Equal("contoso-agents", repo.GetProperty("PinnedPackageName").GetString()),
            () => Assert.Equal("1.0.0", repo.GetProperty("PinnedPackageVersion").GetString()),
            () => Assert.Equal("Default", repo.GetProperty("PinnedSource").GetString()),
            () => Assert.Equal(2, pin.GetProperty("SchemaVersion").GetInt32()),
            () => Assert.Equal("Default", pin.GetProperty("Source").GetString()),
            () => Assert.Equal("contoso-agents", pin.GetProperty("PackageName").GetString()),
            () => Assert.Equal("1.0.0", pin.GetProperty("Version").GetString()),
            () => Assert.Equal(["Sources", "RecentRepos"], root.EnumerateObject().Select(p => p.Name)));
    }

    /// <summary>
    ///     Validates that <c>nupkg</c> packages are offered by a NuGet folder source naming the sample package IDs, and that
    ///     a pin then names that source.
    /// </summary>
    [Fact]
    public void FixtureBuilder_Build_NupkgWithPin_ConfiguresNuGetSourceAndNamesItInThePin()
    {
        // Arrange / Act
        var result = FixtureBuilder.Build(Options("nupkg", repo: true, pin: new PackagePin("contoso-agents", "1.0.0")), new RecordingGit());

        // Assert
        using var settings = JsonDocument.Parse(File.ReadAllText(result.SettingsFile));
        var source = Assert.Single(settings.RootElement.GetProperty("Sources").EnumerateArray());
        var pin = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.RepoDirectory!, ".agentcontrol.json"))).RootElement;
        Assert.Multiple(
            () => Assert.Equal("NuGet", source.GetProperty("Alias").GetString()),
            () => Assert.Equal("NuGet", source.GetProperty("Type").GetString()),
            () => Assert.Equal(result.PackagesDirectory, source.GetProperty("Location").GetString()),
            () => Assert.Equal(["contoso-agents", "fabrikam-agents"], source.GetProperty("PackageIds").EnumerateArray().Select(e => e.GetString())),
            () => Assert.Equal("NuGet", pin.GetProperty("Source").GetString()),
            () => Assert.Equal("NuGet", settings.RootElement.GetProperty("RecentRepos")[0].GetProperty("PinnedSource").GetString()));
    }

    /// <summary>
    ///     Validates that both formats configure both sources, in this order.
    /// </summary>
    [Fact]
    public void FixtureBuilder_Build_Both_ConfiguresZipAndNuGetSources()
    {
        // Arrange / Act
        var result = FixtureBuilder.Build(Options("both"), new RecordingGit());

        // Assert
        using var settings = JsonDocument.Parse(File.ReadAllText(result.SettingsFile));
        Assert.Equal(
            ["Default", "NuGet"],
            settings.RootElement.GetProperty("Sources").EnumerateArray().Select(e => e.GetProperty("Alias").GetString()));
    }

    /// <summary>
    ///     Validates that a fixture without a repository has an empty recent-repos list.
    /// </summary>
    [Fact]
    public void FixtureBuilder_Build_NoRepo_WritesEmptyRecentRepos()
    {
        // Arrange / Act
        var result = FixtureBuilder.Build(Options("zip"), new RecordingGit());

        // Assert
        using var settings = JsonDocument.Parse(File.ReadAllText(result.SettingsFile));
        Assert.Multiple(
            () => Assert.Equal(0, settings.RootElement.GetProperty("RecentRepos").GetArrayLength()),
            () => Assert.True(Directory.Exists(result.ConfigDirectory)));
    }

    /// <summary>
    ///     Validates that the repository is initialized and committed with git, and that a repo without a pin has no pin file.
    /// </summary>
    [Fact]
    public void FixtureBuilder_Build_Repo_InitializesAndCommitsWithGit()
    {
        // Arrange
        var git = new RecordingGit();

        // Act
        var result = FixtureBuilder.Build(Options("zip", repo: true), git);

        // Assert
        Assert.Multiple(
            () => Assert.True(File.Exists(Path.Combine(result.RepoDirectory!, "README.md"))),
            () => Assert.False(File.Exists(Path.Combine(result.RepoDirectory!, ".agentcontrol.json"))),
            () => Assert.Equal(3, git.Calls.Count),
            () => Assert.All(git.Calls, call => Assert.Equal(result.RepoDirectory, call.WorkingDirectory)),
            () => Assert.Equal(["init", "-b", "main"], git.Calls[0].Arguments),
            () => Assert.Equal(["add", "-A"], git.Calls[1].Arguments),
            () => Assert.Contains("commit", git.Calls[2].Arguments),
            () => Assert.Contains("Initial commit", git.Calls[2].Arguments));
    }

    /// <summary>
    ///     Validates that <c>git</c> and <c>all</c> are accepted (case-insensitively) and that the Git format writes no
    ///     package files.
    /// </summary>
    /// <param name="value">The <c>--packages</c> value.</param>
    /// <param name="expected">The expected format name.</param>
    [Theory]
    [InlineData("git", "Git")]
    [InlineData("GIT", "Git")]
    [InlineData("all", "All")]
    public void FixtureOptions_Parse_GitAndAll_AreAccepted(string value, string expected)
    {
        // Arrange
        var parsed = CommandLineParser.Parse(["fixture", "fx", "--packages", value]);

        // Act
        var options = FixtureOptions.Parse(parsed);

        // Assert
        Assert.Equal(expected, options.Formats.ToString());
    }

    /// <summary>
    ///     Validates the exact git command sequence of the Git source: a scratch repository with one commit per version
    ///     of the first sample package, a lightweight and an annotated tag, and a push into a bare repository, with the
    ///     identity fixed and signing off.
    /// </summary>
    [Fact]
    public void FixtureBuilder_Build_Git_CreatesBareRepositoryWithTaggedVersions()
    {
        // Arrange
        var git = new RecordingGit();
        var options = Options("git");

        // Act
        var result = FixtureBuilder.Build(options, git);

        // Assert
        var work = Path.Combine(result.Root, "git-source-work");
        var bare = Path.Combine(result.Root, "git-source.git");
        var commands = git.Calls.Select(c => string.Join(' ', c.Arguments.Where(a => !a.StartsWith("user.", StringComparison.Ordinal) && a != "-c" && !a.EndsWith("gpgsign=false", StringComparison.Ordinal)))).ToList();
        Assert.Multiple(
            () => Assert.Equal(bare, result.GitSourceDirectory),
            () => Assert.Empty(result.Packages),
            () => Assert.Equal(
                [
                    "init -b main",
                    "add -A",
                    "commit -m contoso-agents 1.0.0",
                    "tag release/1.0.0",
                    "add -A",
                    "commit -m contoso-agents 1.1.0",
                    "tag -a release/1.1.0 -m contoso-agents 1.1.0",
                    $"init --bare -b main {bare}",
                    $"push --quiet {bare} main --tags",
                ],
                commands),
            () => Assert.Equal(work, git.Calls[0].WorkingDirectory),
            () => Assert.Equal(result.Root, git.Calls[7].WorkingDirectory),
            () => Assert.Equal(work, git.Calls[8].WorkingDirectory),
            () => Assert.Contains("user.email=puppet@agentcontrol.invalid", git.Calls[2].Arguments),
            () => Assert.Contains("commit.gpgsign=false", git.Calls[2].Arguments),
            () => Assert.Contains("tag.gpgsign=false", git.Calls[6].Arguments),
            () => Assert.False(Directory.Exists(work)));
    }

    /// <summary>
    ///     Validates that the Git source is configured with the fields the application reads, and that a pin names it.
    /// </summary>
    [Fact]
    public void FixtureBuilder_Build_GitWithPin_ConfiguresGitSourceAndNamesItInThePin()
    {
        // Arrange / Act
        var result = FixtureBuilder.Build(Options("git", repo: true, pin: new PackagePin("contoso-agents", "1.0.0")), new RecordingGit());

        // Assert
        using var settings = JsonDocument.Parse(File.ReadAllText(result.SettingsFile));
        var source = Assert.Single(settings.RootElement.GetProperty("Sources").EnumerateArray());
        var pin = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.RepoDirectory!, ".agentcontrol.json"))).RootElement;
        Assert.Multiple(
            () => Assert.Equal("Git", source.GetProperty("Alias").GetString()),
            () => Assert.Equal("Git", source.GetProperty("Type").GetString()),
            () => Assert.Equal(Path.Combine(result.Root, "git-source.git"), source.GetProperty("Location").GetString()),
            () => Assert.Equal("release/{version}", source.GetProperty("TagPattern").GetString()),
            () => Assert.Equal(["contoso-agents"], source.GetProperty("PackageIds").EnumerateArray().Select(e => e.GetString())),
            () => Assert.Equal("Git", pin.GetProperty("Source").GetString()),
            () => Assert.Equal("Git", settings.RootElement.GetProperty("RecentRepos")[0].GetProperty("PinnedSource").GetString()));
    }

    /// <summary>
    ///     Validates that <c>all</c> writes zip and nupkg packages and the Git source, configures the three sources in
    ///     this order, and that a pin names the zip source.
    /// </summary>
    [Fact]
    public void FixtureBuilder_Build_All_IncludesGitAlongsideZipAndNupkg()
    {
        // Arrange
        var git = new RecordingGit();

        // Act
        var result = FixtureBuilder.Build(Options("all", repo: true, pin: new PackagePin("contoso-agents", "1.0.0")), git);

        // Assert
        using var settings = JsonDocument.Parse(File.ReadAllText(result.SettingsFile));
        Assert.Multiple(
            () => Assert.Equal(6, result.Packages.Count),
            () => Assert.NotNull(result.GitSourceDirectory),
            () => Assert.Equal(
                ["Default", "NuGet", "Git"],
                settings.RootElement.GetProperty("Sources").EnumerateArray().Select(e => e.GetProperty("Alias").GetString())),
            () => Assert.Equal("Default", settings.RootElement.GetProperty("RecentRepos")[0].GetProperty("PinnedSource").GetString()),
            () => Assert.Contains(git.Calls, call => call.Arguments.Contains("--bare")));
    }

    /// <summary>
    ///     Validates that a fixture without the Git format creates no Git source.
    /// </summary>
    [Fact]
    public void FixtureBuilder_Build_Both_HasNoGitSource()
    {
        // Arrange
        var git = new RecordingGit();

        // Act
        var result = FixtureBuilder.Build(Options("both"), git);

        // Assert
        Assert.Multiple(
            () => Assert.Null(result.GitSourceDirectory),
            () => Assert.Empty(git.Calls));
    }

    /// <summary>
    ///     Validates that a folder that already has content is refused, and that an existing empty folder is accepted.
    /// </summary>
    [Fact]
    public void FixtureBuilder_Build_NonEmptyFolder_IsUsageErrorButEmptyFolderIsFine()
    {
        // Arrange
        var empty = Directory.CreateDirectory(Path.Combine(_folder.Path, "empty")).FullName;
        var busy = Directory.CreateDirectory(Path.Combine(_folder.Path, "busy")).FullName;
        File.WriteAllText(Path.Combine(busy, "keep.txt"), "x");

        // Act
        var ok = FixtureBuilder.Build(new FixtureOptions(empty, PackageFormats.Zip, false, null), new RecordingGit());
        var exception = Assert.Throws<PuppetException>(() => FixtureBuilder.Build(new FixtureOptions(busy, PackageFormats.Zip, false, null), new RecordingGit()));

        // Assert
        Assert.Multiple(
            () => Assert.Equal(empty, ok.Root),
            () => Assert.Equal(ExitCodes.UsageError, exception.ExitCode),
            () => Assert.True(File.Exists(Path.Combine(busy, "keep.txt"))));
    }

    /// <summary>
    ///     Validates the package name and version checks.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="validName">Whether it is a valid package name.</param>
    /// <param name="validVersion">Whether it is a valid version.</param>
    [Theory]
    [InlineData("contoso-agents", true, false)]
    [InlineData("1.2.3", true, true)]
    [InlineData("1.2.3-beta.1", true, true)]
    [InlineData("1.2", true, false)]
    [InlineData("bad name", false, false)]
    [InlineData("", false, false)]
    public void FixtureBuilder_Validation_AcceptsExpectedForms(string text, bool validName, bool validVersion)
    {
        // Arrange / Act
        var name = FixtureBuilder.IsValidPackageName(text);
        var version = FixtureBuilder.IsValidVersion(text);

        // Assert
        Assert.Multiple(
            () => Assert.Equal(validName, name),
            () => Assert.Equal(validVersion, version));
    }

    /// <summary>
    ///     Creates fixture options for a new folder under the temp folder.
    /// </summary>
    /// <param name="packages">The package format name.</param>
    /// <param name="repo">Whether to create a repository.</param>
    /// <param name="pin">The pin, or <see langword="null"/>.</param>
    /// <returns>The options.</returns>
    private FixtureOptions Options(string packages, bool repo = false, PackagePin? pin = null) =>
        new(
            Path.Combine(_folder.Path, "fx-" + Guid.NewGuid().ToString("N")[..8]),
            packages switch
            {
                "nupkg" => PackageFormats.Nupkg,
                "both" => PackageFormats.Both,
                "git" => PackageFormats.Git,
                "all" => PackageFormats.All,
                _ => PackageFormats.Zip,
            },
            repo,
            pin);

    /// <summary>
    ///     A git runner that records what it is asked and does nothing.
    /// </summary>
    private sealed class RecordingGit : IGitRunner
    {
        /// <summary>
        ///     Gets the calls made, in order.
        /// </summary>
        public List<(string WorkingDirectory, IReadOnlyList<string> Arguments)> Calls { get; } = [];

        /// <inheritdoc />
        public void Run(string workingDirectory, IReadOnlyList<string> arguments) => Calls.Add((workingDirectory, arguments));
    }
}
