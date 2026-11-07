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
///     Integration tests of <see cref="GitCredentialClient"/> against the real git executable and
///     the real <c>git credential</c> protocol, with the credential store replaced by a throw-away
///     <c>store --file=...</c> helper in a temporary folder so that a test can never read or write
///     the user's actual credential store (Git Credential Manager, a keychain, and so on). The
///     tests are skipped when git is not installed.
/// </summary>
[Collection("RealProcess")]
public class GitCredentialClientIntegrationTests
{
    /// <summary>
    ///     A feed on a loopback address that nothing listens on; only ever described to git, never
    ///     contacted.
    /// </summary>
    private static readonly FeedOrigin Origin = FeedOrigin.Create(new Uri("https://127.0.0.1:59999/v3/index.json"));

    /// <summary>
    ///     Test that a credential walks the whole life cycle through real git: nothing is found at
    ///     first (without git ever prompting), approving stores it, a lookup then returns it, and
    ///     rejecting removes it again.
    /// </summary>
    [Fact]
    public async Task GitCredentialClient_RealGitWithIsolatedStore_MissApproveFillRejectLifeCycle()
    {
        // Arrange
        SkipWithoutGit();
        var folder = CreateTempDirectory();
        try
        {
            var client = CreateClient(folder);
            var credential = new NuGetCredential("svc", "integration-pw-4418");
            var cancellation = TestContext.Current.CancellationToken;

            // Assert: the isolated helper applies, but holds nothing yet; git must not prompt (this
            // would hang or fail the test if it did)
            Assert.True(await client.IsAvailableAsync(Origin, cancellation));
            Assert.Null(await client.FillAsync(Origin, cancellation));

            // Act / Assert: approve stores it, fill finds it
            Assert.True(await client.ApproveAsync(Origin, credential, cancellation));
            var found = await client.FillAsync(Origin, cancellation);
            Assert.Equal(credential, found);

            // Act / Assert: reject removes it
            Assert.True(await client.RejectAsync(Origin, credential, cancellation));
            Assert.Null(await client.FillAsync(Origin, cancellation));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test that rejecting with only the origin removes whatever is stored for it - the way a
    ///     saved sign-in is forgotten - and leaves another origin's credential alone.
    /// </summary>
    [Fact]
    public async Task GitCredentialClient_RealGit_RejectWithoutCredential_RemovesOnlyThatOrigin()
    {
        // Arrange
        SkipWithoutGit();
        var folder = CreateTempDirectory();
        try
        {
            var client = CreateClient(folder);
            var other = FeedOrigin.Create(new Uri("https://127.0.0.1:59998/v3/index.json"));
            var cancellation = TestContext.Current.CancellationToken;
            await client.ApproveAsync(Origin, new NuGetCredential("svc", "pw-one-4418"), cancellation);
            await client.ApproveAsync(other, new NuGetCredential("svc", "pw-two-4418"), cancellation);

            // Act
            await client.RejectAsync(Origin, null, cancellation);

            // Assert
            Assert.Null(await client.FillAsync(Origin, cancellation));
            Assert.Equal("pw-two-4418", (await client.FillAsync(other, cancellation))!.Secret);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test that with the credential helper list reset to nothing git reports no helper, so the
    ///     "Remember" option is unavailable, and a lookup finds nothing.
    /// </summary>
    [Fact]
    public async Task GitCredentialClient_RealGit_NoCredentialHelper_IsUnavailableAndFindsNothing()
    {
        // Arrange
        SkipWithoutGit();
        var folder = CreateTempDirectory();
        try
        {
            var client = new GitCredentialClient("git", extraConfiguration: ["credential.helper="], workingDirectory: folder);

            // Act / Assert
            Assert.False(await client.IsAvailableAsync(Origin, TestContext.Current.CancellationToken));
            Assert.Null(await client.FillAsync(Origin, TestContext.Current.CancellationToken));
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
        var path = Path.Combine(Path.GetTempPath(), $"agentcontrol_git_credential_it_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
