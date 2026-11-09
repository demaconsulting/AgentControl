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

using System.ComponentModel;
using System.Diagnostics;
using DemaConsulting.AgentControl.GitIntegration;
using DemaConsulting.AgentControl.PackageCredentials;
using DemaConsulting.NuGet.Caching;

namespace DemaConsulting.AgentControl.Tests.GitIntegration;

/// <summary>
///     Subsystem tests for GitIntegration: the saved-sign-in capability used the way the rest of the
///     application uses it - through the <see cref="IRememberedCredentialStore"/> contract, by
///     separate instances (as in separate sessions) and against the real git executable and
///     <c>git credential</c> protocol - with the credential store replaced by a throw-away
///     <c>store --file=...</c> helper in a temporary folder so that a test can never read or write
///     the user's actual credential store. Skipped when git is not installed.
/// </summary>
[Collection("RealProcess")]
public class GitIntegrationSubsystemTests
{
    /// <summary>
    ///     A feed on a loopback address that nothing listens on; only ever described to git, never
    ///     contacted.
    /// </summary>
    private static readonly FeedOrigin Origin = FeedOrigin.Create(new Uri("https://127.0.0.1:59997/v3/index.json"));

    /// <summary>
    ///     Test the remote Git capability as a whole through its client, against a real local
    ///     repository with every isolation in force: tags are listed without a clone, one tag is
    ///     fetched shallow and its tree read, and a transport other than the one allowed is refused.
    /// </summary>
    [Fact]
    public async Task GitIntegrationSubsystem_RemoteClient_ListsFetchesOneTagAndRefusesOtherTransports()
    {
        // Arrange
        using var fixture = AgentPackageManagement.GitRepositoryFixture.CreateOrSkip();
        var commit = fixture.Commit(
            new Dictionary<string, string> { [".github/agents/copilot.md"] = "x", ["release-notes.md"] = "notes" }, "one");
        fixture.Tag("v1.0.0", annotated: true);
        fixture.Push();
        var client = new GitRemoteClient(new GitRemoteOptions(
            () => "git", Path.Combine(fixture.Root, "work"), ExtraEnvironment: fixture.IsolationEnvironment));
        var target = new GitRemoteTarget(fixture.RemotePath, GitTransport.File);
        var repository = Path.Combine(fixture.Root, "fetched");

        // Act
        var tags = await client.ListTagsAsync(target, "refs/tags/*", TestContext.Current.CancellationToken);
        await client.InitRepositoryAsync(repository, TestContext.Current.CancellationToken);
        var fetched = await client.FetchTagAsync(target, repository, "v1.0.0", TestContext.Current.CancellationToken);
        var entries = await client.ListTreeAsync(
            repository, fetched, [".github/agents"], recursive: true, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(commit, Assert.Single(tags).CommitSha);
        Assert.Equal(commit, fetched);
        Assert.Equal(".github/agents/copilot.md", Assert.Single(entries).Path);
        await Assert.ThrowsAsync<GitRemoteException>(() => client.ListTagsAsync(
            new GitRemoteTarget(fixture.RemotePath, GitTransport.Https), "refs/tags/*", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a sign-in kept by one session is found by another and removed on request: it
    ///     lives in the user's helper, not in the instance that kept it, and nothing is ever asked
    ///     of the user along the way.
    /// </summary>
    [Fact]
    public async Task GitIntegrationSubsystem_SavedSignIn_KeptInOneSessionFoundInAnotherAndRemovedOnRequest()
    {
        // Arrange: two independent instances sharing only the user's (isolated) helper
        SkipWithoutGit();
        var folder = CreateTempDirectory();
        try
        {
            IRememberedCredentialStore firstSession = CreateClient(folder);
            IRememberedCredentialStore laterSession = CreateClient(folder);
            var credential = new NuGetCredential("svc", "subsystem-pw-4419");
            var cancellation = TestContext.Current.CancellationToken;

            // Act / Assert: nothing saved yet; the first session keeps a sign-in
            Assert.True(await laterSession.IsAvailableAsync(Origin, cancellation));
            Assert.Null(await laterSession.FillAsync(Origin, cancellation));
            Assert.True(await firstSession.ApproveAsync(Origin, credential, cancellation));

            // Assert: a later session finds it
            Assert.Equal(credential, await laterSession.FillAsync(Origin, cancellation));

            // Act / Assert: forgetting by origin removes it for every session
            Assert.True(await laterSession.RejectAsync(Origin, null, cancellation));
            Assert.Null(await firstSession.FillAsync(Origin, cancellation));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a sign-in the line-based credential protocol cannot carry - one with a line
    ///     feed, carriage return or NUL - is never handed to the helper (nothing is stored and a
    ///     lookup finds nothing), while one whose password contains <c>=</c> (a Base64 token) is
    ///     kept and found unchanged.
    /// </summary>
    [Fact]
    public async Task GitIntegrationSubsystem_SavedSignIn_ValueWithLineBreakIsNeverStoredButEqualsIs()
    {
        // Arrange
        SkipWithoutGit();
        var folder = CreateTempDirectory();
        try
        {
            IRememberedCredentialStore store = CreateClient(folder);
            var cancellation = TestContext.Current.CancellationToken;

            // Act: values the protocol cannot carry
            var withLineFeed = await store.ApproveAsync(Origin, new NuGetCredential("svc", "pw\nusername=evil"), cancellation);
            var withCarriageReturn = await store.ApproveAsync(Origin, new NuGetCredential("svc", "pw\rx"), cancellation);
            var found = await store.FillAsync(Origin, cancellation);

            // Assert: refused, and nothing reached the helper
            Assert.False(withLineFeed);
            Assert.False(withCarriageReturn);
            Assert.Null(found);
            Assert.False(File.Exists(Path.Combine(folder, "credentials.txt")));

            // Act / Assert: a Base64-style token with padding is kept and found unchanged
            Assert.True(await store.ApproveAsync(Origin, new NuGetCredential("svc", "dG9rZW4tYWJjMTI=="), cancellation));
            Assert.Equal("dG9rZW4tYWJjMTI==", (await store.FillAsync(Origin, cancellation))!.Secret);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test that with no credential helper configured the subsystem reports that nothing can be
    ///     kept and nothing is found, instead of failing.
    /// </summary>
    [Fact]
    public async Task GitIntegrationSubsystem_SavedSignIn_NoHelperConfigured_ReportsUnavailableAndNothingSaved()
    {
        // Arrange
        SkipWithoutGit();
        var folder = CreateTempDirectory();
        try
        {
            IRememberedCredentialStore store = new GitCredentialClient(
                "git", extraConfiguration: ["credential.helper="], workingDirectory: folder);
            var cancellation = TestContext.Current.CancellationToken;

            // Act / Assert
            Assert.False(await store.IsAvailableAsync(Origin, cancellation));
            Assert.Null(await store.FillAsync(Origin, cancellation));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Creates a client whose only credential helper is a file store inside a temporary folder.
    /// </summary>
    /// <param name="folder">The temporary folder holding the store and used as the working
    ///     directory.</param>
    /// <returns>The client.</returns>
    private static GitCredentialClient CreateClient(string folder) => new(
        "git",
        extraConfiguration:
        [
            "credential.helper=",
            $"credential.helper=store --file={Path.Combine(folder, "credentials.txt").Replace('\\', '/')}"
        ],
        workingDirectory: folder);

    /// <summary>
    ///     Skips the calling test when no git executable can be run.
    /// </summary>
    private static void SkipWithoutGit()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            process?.WaitForExit();
            if (process is { ExitCode: 0 })
            {
                return;
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // Not installed: skipped below
        }

        Assert.Skip("git is not installed, so the credential protocol cannot be exercised.");
    }

    /// <summary>
    ///     Creates a fresh temporary directory.
    /// </summary>
    /// <returns>The directory path.</returns>
    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"agentcontrol_git_subsystem_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
