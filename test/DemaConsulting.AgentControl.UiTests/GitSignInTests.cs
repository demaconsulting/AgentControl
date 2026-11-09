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

using DemaConsulting.AgentControl.Tests.GitIntegration;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     End-to-end tests of signing in to a private Git repository: the published application runs the real
///     <c>git</c> against a private Git server of the test (see <see cref="GitHttpsServer"/>: TLS with a
///     throw-away certificate, 401 without the right credentials) with the user's own sign-in replaced by a
///     test credential helper that behaves like Git Credential Manager - it supplies the credential only when it
///     may show its sign-in UI - in an isolated git configuration, so no real credential store and no network
///     are involved.
/// </summary>
/// <remarks>
///     The helper writes <c>asked</c> to a log when it was asked but may not prompt (git was started by work the
///     application started itself) and <c>prompted</c> when it was asked and allowed to prompt (work the user
///     started). The tests assert on that log, on what the server received and on what the card and the
///     editor show. Git Credential Manager's own dialog is not exercised; that is a manual check.
/// </remarks>
public sealed class GitSignInTests
{
    /// <summary>
    ///     The user name the test helper supplies.
    /// </summary>
    private const string User = "agent";

    /// <summary>
    ///     The password the test helper supplies; it must never appear in a file the application writes.
    /// </summary>
    private const string Password = "git-sentinel-pw-7d41c9";

    /// <summary>
    ///     A repo pinned to a Git source whose repository needs a sign-in never prompts for the background
    ///     upgrade check: the card says a sign-in is needed, git asked the user's helper but was not allowed to
    ///     prompt, the server received no credential and no sign-in dialog opened.
    /// </summary>
    [Fact]
    public async Task RepoCard_PinnedToPrivateGitRepository_BackgroundCheckNeverPromptsAndReportsSignInNeeded()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: a private repository, and a repo pinned to its version 1.0.0
        using var fixture = GitFixtureRepository.CreateOrSkip();
        var commit = fixture.ResolveTag("release/1.0.0");
        await using var server = await GitHttpsServer.StartAsync(fixture.BarePath, User, Password);
        var (configuration, helperLog) = GitHttpsServer.CreateUserConfiguration(fixture.Root, User, Password);
        using var context = CreatePinnedContext(server, configuration, commit);

        // Act: start the application; the background check of the pinned source runs on its own
        var mainWindow = context.Launch();

        // Assert: the card says a sign-in is needed
        Assert.True(GitSourceTests.WaitForText(mainWindow, "SourceProblemBadge", "Sign-in needed"));

        // Assert: git asked the user's helper, which may not prompt in the background; nothing prompted
        Assert.True(await WaitForHelperLogAsync(helperLog, lines => lines.Contains("asked")));
        Assert.DoesNotContain("prompted", ReadHelperLog(helperLog));

        // Assert: no credential reached the server, and no sign-in dialog opened
        Assert.True(server.AnonymousRequestCount > 0);
        Assert.Equal(0, server.SignedInRequestCount);
        Assert.Null(context.WaitForWindowWithDescendant("CredentialPromptUserNameTextBox", TimeSpan.FromSeconds(2)));
    }

    /// <summary>
    ///     On the same repo, the card's Refresh - work the user starts - lets the user's own sign-in prompt: the
    ///     helper is allowed to prompt and supplies the credential, the server is signed in, the sign-in
    ///     notice goes away and the check completes (the newer version is offered). Neither the settings, nor the
    ///     pin file, nor the log folder holds the password.
    /// </summary>
    [Fact]
    public async Task RepoCard_PrivateGitRepositoryNeedingSignIn_RefreshMayPromptAndThenSignsIn()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: the same repo; the background check has already reported "sign-in needed"
        using var fixture = GitFixtureRepository.CreateOrSkip();
        var commit = fixture.ResolveTag("release/1.0.0");
        await using var server = await GitHttpsServer.StartAsync(fixture.BarePath, User, Password);
        var (configuration, helperLog) = GitHttpsServer.CreateUserConfiguration(fixture.Root, User, Password);
        using var context = CreatePinnedContext(server, configuration, commit);
        var mainWindow = context.Launch();
        Assert.True(GitSourceTests.WaitForText(mainWindow, "SourceProblemBadge", "Sign-in needed"));
        Assert.Equal(0, server.SignedInRequestCount);

        // Act: the user chooses Refresh in the card's menu
        GitSourceTests.OpenCardMenuItem(mainWindow, "RefreshMenuItem").AsMenuItem().Invoke();

        // Assert: the helper was allowed to prompt and signed in, so the server was signed in
        Assert.True(await WaitForHelperLogAsync(helperLog, lines => lines.Contains("prompted")));
        Assert.True(Retry.WhileFalse(
            () => server.SignedInRequestCount > 0, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(200)).Result);

        // Assert: the sign-in notice goes away and the check finds the newer version
        Assert.True(Retry.WhileNotNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("SourceProblemBadge")),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMilliseconds(200)).Success);
        Assert.NotNull(Retry.WhileNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("UpgradeAvailableBadge")),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMilliseconds(200)).Result);

        // Assert: the application never wrote the password anywhere
        foreach (var file in Directory.EnumerateFiles(context.RootDirectory, "*", SearchOption.AllDirectories))
        {
            Assert.DoesNotContain(Password, ReadSharedText(file), StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     In the Git source editor, Test connection against a private repository - work the user starts - lets
    ///     the user's own sign-in prompt: the helper is allowed to prompt and the connection is reported OK with
    ///     the tags that match the pattern.
    /// </summary>
    [Fact]
    public async Task SettingsWindow_TestConnectionToPrivateGitRepository_MaySignInAndListsTheTags()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: a private repository, and the real git with the user's own helper
        using var fixture = GitFixtureRepository.CreateOrSkip();
        await using var server = await GitHttpsServer.StartAsync(fixture.BarePath, User, Password);
        var (configuration, helperLog) = GitHttpsServer.CreateUserConfiguration(fixture.Root, User, Password);
        using var context = new AgentControlTestContext(useRealGit: true)
        {
            LaunchEnvironment = UserEnvironment(configuration)
        };
        var mainWindow = context.Launch();
        var settingsWindow = GitSourceTests.OpenSettings(context, mainWindow);
        GitSourceTests.Find(settingsWindow, "AddSourceButton").AsButton().Invoke();
        GitSourceTests.Find(settingsWindow, "AddGitSourceButton").AsMenuItem().Invoke();
        var editor = context.WaitForWindowWithDescendant("SourceGitPackageIdTextBox", TimeSpan.FromSeconds(15));
        Assert.NotNull(editor);
        GitSourceTests.Find(editor, "SourceAliasTextBox").AsTextBox().Text = "Private";
        GitSourceTests.Find(editor, "SourceLocationTextBox").AsTextBox().Text = server.RepositoryUrl;
        GitSourceTests.Find(editor, "SourceTagPatternTextBox").AsTextBox().Text = "release/{version}";
        var test = GitSourceTests.Find(editor, "SourceTestConnectionButton").AsButton();
        Assert.True(Retry.WhileFalse(() => test.IsEnabled, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200)).Result);

        // Act: the user tests the connection
        test.Invoke();

        // Assert: the helper was allowed to prompt, and the connection works with the tags listed
        Assert.True(GitSourceTests.WaitForText(editor, "SourceTestResultTitleText", "Connection OK"));
        Assert.True(await WaitForHelperLogAsync(helperLog, lines => lines.Contains("prompted")));
        Assert.True(server.SignedInRequestCount > 0);
        Assert.StartsWith(
            "2 of 4 tags match the pattern.",
            GitSourceTests.Find(editor, "SourceTagPreviewSummaryText").Name,
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     Creates a context for a repo pinned to version 1.0.0 of a Git source that points at the private
    ///     server, with the user's own git configuration (and its credential helper) in effect.
    /// </summary>
    /// <param name="server">The private Git server.</param>
    /// <param name="configuration">The isolated global git configuration with the test helper.</param>
    /// <param name="commit">The commit the pin records as the delivered content.</param>
    /// <returns>The context, not yet launched.</returns>
    private static AgentControlTestContext CreatePinnedContext(GitHttpsServer server, string configuration, string commit) =>
        new(
            pinnedPackageName: GitFixtureRepository.PackageName,
            pinnedPackageVersion: "1.0.0",
            pinnedSource: "Private",
            pinnedContentIdentity: "git-commit:" + commit,
            gitSource: new GitSourceSettings(
                "Private", server.RepositoryUrl, "release/{version}", GitFixtureRepository.PackageName))
        {
            LaunchEnvironment = UserEnvironment(configuration)
        };

    /// <summary>
    ///     Builds the environment of the application: the user's git configuration replaced by the isolated one
    ///     that has the test helper, no system configuration, and no <c>GCM_INTERACTIVE</c> (the application
    ///     decides that for background work, and work the user starts leaves it alone).
    /// </summary>
    /// <param name="configuration">The isolated global git configuration.</param>
    /// <returns>The environment variables.</returns>
    private static Dictionary<string, string> UserEnvironment(string configuration) => new()
    {
        ["GIT_CONFIG_GLOBAL"] = configuration,
        ["GIT_CONFIG_NOSYSTEM"] = "1"
    };

    /// <summary>
    ///     Reads the lines the test helper logged so far.
    /// </summary>
    /// <param name="helperLog">The helper's log file.</param>
    /// <returns>The lines, or none when the helper was never asked.</returns>
    private static string[] ReadHelperLog(string helperLog) =>
        File.Exists(helperLog) ? ReadSharedText(helperLog).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];

    /// <summary>
    ///     Waits until the helper's log satisfies a condition.
    /// </summary>
    /// <param name="helperLog">The helper's log file.</param>
    /// <param name="condition">The condition on the logged lines.</param>
    /// <returns><see langword="true"/> when the condition held in time.</returns>
    private static async Task<bool> WaitForHelperLogAsync(string helperLog, Func<string[], bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (condition(ReadHelperLog(helperLog)))
            {
                return true;
            }

            await Task.Delay(200, TestContext.Current.CancellationToken);
        }

        return false;
    }

    /// <summary>
    ///     Reads a file that another process may still hold open for writing.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The file's text.</returns>
    private static string ReadSharedText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
