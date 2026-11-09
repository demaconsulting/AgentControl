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

using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.Settings;
using DemaConsulting.AgentControl.Startup;
using DemaConsulting.AgentControl.Tests.GitIntegration;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Tests of how <see cref="RepoCardViewModel"/> sets up Git package sources: work the user
///     started may let git show its credential UI, background work never may, the git executable
///     comes from the settings or startup override, and fetched packages are cached under the
///     isolated configuration directory when one is given. A recording stub stands in for git.
/// </summary>
[Collection("RealProcess")]
public sealed class RepoCardGitSourceTests : IDisposable
{
    /// <summary>
    ///     The stubs to delete after a test.
    /// </summary>
    private readonly List<GitRemoteStub> _stubs = [];

    /// <summary>
    ///     The folders to delete after a test.
    /// </summary>
    private readonly List<string> _folders = [];

    /// <summary>
    ///     Test that the registry built for background work (the upgrade check, the launch re-sync)
    ///     runs git without any prompt, and the registry built for work the user started does not
    ///     suppress the credential UI.
    /// </summary>
    [Theory]
    [InlineData(false, "never")]
    [InlineData(true, "")]
    public async Task RepoCardViewModel_CreateSourceRegistry_GitSource_PromptsOnlyForUserStartedWork(
        bool allowPrompt, string expectedGcmInteractive)
    {
        // Arrange: a Git source, the stub as the git executable from the startup override
        var previous = Environment.GetEnvironmentVariable("GCM_INTERACTIVE");
        Environment.SetEnvironmentVariable("GCM_INTERACTIVE", null);
        var stub = GitRemoteStub.Create();
        _stubs.Add(stub);
        try
        {
            var settings = new AppSettings
            {
                Sources =
                [
                    new PackageSourceDefinition
                    {
                        Alias = "Repo",
                        Type = PackageSourceType.Git,
                        Location = "https://example.invalid/org/repo.git",
                        TagPattern = "v{version}",
                        PackageIds = ["contoso-agents"]
                    }
                ]
            };
            var configDir = Path.Combine(Path.GetTempPath(), "agentcontrol_card_git_" + Guid.NewGuid().ToString("N"));
            _folders.Add(configDir);
            var options = StartupOptions.Parse(["--config-dir", configDir, "--git-path", stub.Path]);
            var card = new RepoCardViewModel(
                new RecentRepo { Path = Path.GetTempPath() }, () => settings, new PackageVersionCache(), options);

            // Act
            var provider = card.CreateSourceRegistry(allowPrompt).Resolve("Repo");
            await provider.ListVersionsAsync("contoso-agents", TestContext.Current.CancellationToken);

            // Assert: the version gate runs first (user-facing sources require a supported git), then
            // the listing; both run in the requested prompt mode
            var invocations = stub.ReadInvocations();
            Assert.Equal(["version", "ls-remote"], invocations.Select(item => item.Command));
            foreach (var invocation in invocations)
            {
                Assert.Equal(expectedGcmInteractive, invocation.Environment["GCM_INTERACTIVE"]);
                Assert.Equal(!allowPrompt, invocation.Arguments.Contains("core.askPass=", StringComparison.Ordinal));
                Assert.Equal("0", invocation.Environment["GIT_TERMINAL_PROMPT"]);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("GCM_INTERACTIVE", previous);
        }
    }

    /// <summary>
    ///     Test that a Git source's registry runs the git executable the user configured in the settings,
    ///     or the one named at startup when the settings name none.
    /// </summary>
    /// <param name="useStartupOverride">Whether the stub is named at startup (the settings then name
    ///     none) rather than in the settings.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepoCardViewModel_CreateSourceRegistry_GitSource_RunsTheConfiguredOrOverriddenGit(bool useStartupOverride)
    {
        // Arrange: the stub is the configured git, or the startup override while the settings name none
        var stub = GitRemoteStub.Create();
        _stubs.Add(stub);
        var settings = new AppSettings
        {
            GitExecutablePath = useStartupOverride ? string.Empty : stub.Path,
            Sources =
            [
                new PackageSourceDefinition
                {
                    Alias = "Repo",
                    Type = PackageSourceType.Git,
                    Location = "https://example.invalid/org/repo.git",
                    TagPattern = "v{version}",
                    PackageIds = ["contoso-agents"]
                }
            ]
        };
        var configDir = Path.Combine(Path.GetTempPath(), "agentcontrol_card_git_" + Guid.NewGuid().ToString("N"));
        _folders.Add(configDir);
        var options = StartupOptions.Parse(
            useStartupOverride ? ["--config-dir", configDir, "--git-path", stub.Path] : ["--config-dir", configDir]);
        var card = new RepoCardViewModel(
            new RecentRepo { Path = Path.GetTempPath() }, () => settings, new PackageVersionCache(), options);

        // Act
        var provider = card.CreateSourceRegistry(allowPrompt: false).Resolve("Repo");
        await provider.ListVersionsAsync("contoso-agents", TestContext.Current.CancellationToken);

        // Assert: the listing ran through the stub
        Assert.Contains("ls-remote", stub.ReadInvocations().Select(item => item.Command));
    }

    /// <summary>
    ///     Test that Git sources cache under a <c>git-cache</c> folder inside the isolated
    ///     configuration directory when one was given at startup, and use the per-user default
    ///     (<see langword="null"/> here) otherwise.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_GetGitCacheRoot_UsesConfigDirectoryWhenGiven()
    {
        // Arrange
        var configDir = Path.Combine(Path.GetTempPath(), "agentcontrol_card_git_cfg");

        // Act / Assert
        Assert.Equal(
            Path.Combine(Path.GetFullPath(configDir), "git-cache"),
            RepoCardViewModel.GetGitCacheRoot(StartupOptions.Parse(["--config-dir", configDir])));
        Assert.Null(RepoCardViewModel.GetGitCacheRoot(StartupOptions.Parse([])));
        Assert.Null(RepoCardViewModel.GetGitCacheRoot(null));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var stub in _stubs)
        {
            stub.Delete();
        }

        foreach (var folder in _folders.Where(Directory.Exists))
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
