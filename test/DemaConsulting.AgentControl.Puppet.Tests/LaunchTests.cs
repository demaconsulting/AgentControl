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
///     Tests for the <c>launch</c> command's argument building and for finding the AgentControl build (nothing is started).
/// </summary>
public sealed class LaunchTests : IDisposable
{
    /// <summary>
    ///     A folder standing in for the repository.
    /// </summary>
    private readonly TempFolder _repo = new();

    /// <summary>
    ///     Releases the temp folder.
    /// </summary>
    public void Dispose() => _repo.Dispose();

    /// <summary>
    ///     Validates that a given config folder is passed to the application as an absolute path, with the tool overrides after it.
    /// </summary>
    [Fact]
    public void LaunchOptions_TryBuild_AllOptions_BuildsApplicationArguments()
    {
        // Arrange
        var parsed = CommandLineParser.Parse(["launch", "--instance", "a", "--config-dir", "cfg", "--git-path", "C:\\git\\git.exe", "--agent-tool-command", "echo hi"]);

        // Act
        var ok = LaunchOptions.TryBuild(parsed, _repo.Path, out var plan, out var error);

        // Assert
        var expectedConfig = Path.GetFullPath("cfg");
        Assert.Multiple(
            () => Assert.True(ok, error),
            () => Assert.Equal("a", plan.Alias),
            () => Assert.Equal(expectedConfig, plan.ConfigDir),
            () => Assert.True(plan.ConfigDirGiven),
            () => Assert.Equal(["--config-dir", expectedConfig, "--git-path", "C:\\git\\git.exe", "--agent-tool-command", "echo hi"], plan.Arguments));
    }

    /// <summary>
    ///     Validates that without <c>--config-dir</c> a fresh folder under the temp root is chosen, different every time.
    /// </summary>
    [Fact]
    public void LaunchOptions_TryBuild_NoConfigDir_UsesFreshFolderUnderTempRoot()
    {
        // Arrange
        var parsed = CommandLineParser.Parse(["launch", "--instance", "demo"]);

        // Act
        var first = LaunchOptions.TryBuild(parsed, _repo.Path, out var planOne, out _);
        var second = LaunchOptions.TryBuild(parsed, _repo.Path, out var planTwo, out _);

        // Assert
        Assert.Multiple(
            () => Assert.True(first),
            () => Assert.True(second),
            () => Assert.False(planOne.ConfigDirGiven),
            () => Assert.Equal(_repo.Path, Path.GetDirectoryName(planOne.ConfigDir)),
            () => Assert.StartsWith("config-demo-", Path.GetFileName(planOne.ConfigDir), StringComparison.Ordinal),
            () => Assert.NotEqual(planOne.ConfigDir, planTwo.ConfigDir),
            () => Assert.Equal(["--config-dir", planOne.ConfigDir], planOne.Arguments));
    }

    /// <summary>
    ///     Validates that a trailing directory separator is dropped from the config folder.
    /// </summary>
    [Fact]
    public void LaunchOptions_TryBuild_ConfigDirWithTrailingSeparator_IsTrimmed()
    {
        // Arrange
        var given = Path.Combine(_repo.Path, "cfg") + Path.DirectorySeparatorChar;
        var parsed = CommandLineParser.Parse(["launch", "--config-dir", given]);

        // Act
        var ok = LaunchOptions.TryBuild(parsed, _repo.Path, out var plan, out var error);

        // Assert
        Assert.Multiple(
            () => Assert.True(ok, error),
            () => Assert.Equal(Path.Combine(_repo.Path, "cfg"), plan.ConfigDir));
    }

    /// <summary>
    ///     Validates that a bad alias and a value option without a value are rejected.
    /// </summary>
    /// <param name="line">The command line.</param>
    [Theory]
    [InlineData("launch --instance bad/alias")]
    [InlineData("launch --instance")]
    [InlineData("launch --config-dir")]
    [InlineData("launch --git-path")]
    [InlineData("launch --agent-tool-command")]
    [InlineData("launch --exe")]
    public void LaunchOptions_TryBuild_Invalid_IsRejectedWithMessage(string line)
    {
        // Arrange
        var parsed = CommandLineParser.Parse(line.Split(' '));

        // Act
        var ok = LaunchOptions.TryBuild(parsed, _repo.Path, out _, out var error);

        // Assert
        Assert.Multiple(
            () => Assert.False(ok),
            () => Assert.NotEmpty(error));
    }

    /// <summary>
    ///     Validates how arguments are quoted for the Windows command line.
    /// </summary>
    /// <param name="argument">The argument.</param>
    /// <param name="expected">The quoted form.</param>
    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("C:\\Program Files\\x", "\"C:\\Program Files\\x\"")]
    [InlineData("", "\"\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData("C:\\dir with space\\", "\"C:\\dir with space\\\\\"")]
    public void LaunchOptions_Quote_QuotesWhatNeedsIt(string argument, string expected)
    {
        // Arrange / Act
        var quoted = LaunchOptions.Quote(argument);

        // Assert
        Assert.Equal(expected, quoted);
    }

    /// <summary>
    ///     Validates that arguments are joined with spaces, each quoted as needed.
    /// </summary>
    [Fact]
    public void LaunchOptions_JoinArguments_JoinsQuotedArguments()
    {
        // Arrange / Act
        var line = LaunchOptions.JoinArguments(["--config-dir", "C:\\my dir"]);

        // Assert
        Assert.Equal("--config-dir \"C:\\my dir\"", line);
    }

    /// <summary>
    ///     Validates that the newest build of the repository's application is chosen, whatever its configuration.
    /// </summary>
    [Fact]
    public void AppLocator_FindNewest_PicksMostRecentBuild()
    {
        // Arrange
        var root = MakeRepo();
        var release = MakeExe(root, "Release", DateTime.UtcNow.AddMinutes(-30));
        var debug = MakeExe(root, "Debug", DateTime.UtcNow);

        // Act
        var found = AppLocator.FindNewest(Path.Combine(root, "tools", "DemaConsulting.AgentControl.Puppet", "bin"));

        // Assert
        Assert.Multiple(
            () => Assert.Equal(debug, found),
            () => Assert.NotEqual(release, found));
    }

    /// <summary>
    ///     Validates that a repository without a build, and a folder outside any repository, find nothing.
    /// </summary>
    [Fact]
    public void AppLocator_FindNewest_NoBuild_IsNull()
    {
        // Arrange
        var root = MakeRepo();
        using var outside = new TempFolder();

        // Act
        var inRepo = AppLocator.FindNewest(root);
        var outsideRepo = AppLocator.FindNewest(outside.Path);

        // Assert
        Assert.Multiple(
            () => Assert.Null(inRepo),
            () => Assert.Null(outsideRepo));
    }

    /// <summary>
    ///     Validates that the repository root is found by climbing from a deep folder.
    /// </summary>
    [Fact]
    public void AppLocator_FindRepositoryRoot_ClimbsToSolutionFile()
    {
        // Arrange
        var root = MakeRepo();
        var deep = Directory.CreateDirectory(Path.Combine(root, "tools", "x", "bin")).FullName;

        // Act
        var found = AppLocator.FindRepositoryRoot(deep);

        // Assert
        Assert.Equal(root, found);
    }

    /// <summary>
    ///     Validates that an explicit <c>--exe</c> must exist and is returned as a full path.
    /// </summary>
    [Fact]
    public void AppLocator_TryResolveExePath_Explicit_RequiresExistingFile()
    {
        // Arrange
        var exe = Path.Combine(_repo.Path, "app.exe");
        File.WriteAllText(exe, string.Empty);

        // Act
        var found = AppLocator.TryResolveExePath(exe, _repo.Path, out var path, out var error);
        var missing = AppLocator.TryResolveExePath(Path.Combine(_repo.Path, "nope.exe"), _repo.Path, out _, out var missingError);

        // Assert
        Assert.Multiple(
            () => Assert.True(found, error),
            () => Assert.Equal(exe, path),
            () => Assert.False(missing),
            () => Assert.Contains("not found", missingError, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Validates that a missing build is reported with advice.
    /// </summary>
    [Fact]
    public void AppLocator_TryResolveExePath_NoBuild_AdvisesBuilding()
    {
        // Arrange
        var root = MakeRepo();

        // Act
        var found = AppLocator.TryResolveExePath(null, root, out _, out var error);

        // Assert
        Assert.Multiple(
            () => Assert.False(found),
            () => Assert.Contains("--exe", error, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Creates a folder that looks like the repository root.
    /// </summary>
    /// <returns>The root.</returns>
    private string MakeRepo()
    {
        File.WriteAllText(Path.Combine(_repo.Path, "DemaConsulting.AgentControl.slnx"), "<Solution />");
        return _repo.Path;
    }

    /// <summary>
    ///     Creates a fake application build.
    /// </summary>
    /// <param name="root">The repository root.</param>
    /// <param name="configuration">The build configuration folder.</param>
    /// <param name="written">The file's last write time (UTC).</param>
    /// <returns>The executable's path.</returns>
    private static string MakeExe(string root, string configuration, DateTime written)
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, "src", "DemaConsulting.AgentControl", "bin", configuration, "net10.0")).FullName;
        var exe = Path.Combine(folder, AppLocator.ExeFileName);
        File.WriteAllText(exe, string.Empty);
        File.SetLastWriteTimeUtc(exe, written);
        return exe;
    }
}
