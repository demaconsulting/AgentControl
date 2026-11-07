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
using DemaConsulting.AgentControl.GitIntegration;
using DemaConsulting.AgentControl.PackageCredentials;
using DemaConsulting.AgentControl.Tests.PackageCredentials;
using DemaConsulting.NuGet.Caching;

namespace DemaConsulting.AgentControl.Tests.GitIntegration;

/// <summary>
///     Unit tests for <see cref="GitCredentialClient"/> against a stub git executable that records
///     what it was asked (arguments, environment, standard input) and answers with canned output,
///     so the exact requests, the non-interactive environment, the time limit and the absence of
///     any logged secret can be verified without a real credential store.
/// </summary>
[Collection("RealProcess")]
public class GitCredentialClientTests
{
    /// <summary>
    ///     A feed on a non-default port, so the <c>host</c> value must carry the port.
    /// </summary>
    private static readonly FeedOrigin Origin = FeedOrigin.Create(new Uri("https://feeds.example.com:8443/v3/index.json"));

    /// <summary>
    ///     A password with characters that must survive parsing and must never be logged.
    /// </summary>
    private const string SentinelPassword = "sentinel-credential-pw-4417";

    /// <summary>
    ///     Test that availability is decided by asking git which helper applies to the feed's
    ///     origin, and that a configured helper means "available".
    /// </summary>
    [Fact]
    public async Task GitCredentialClient_IsAvailable_HelperConfigured_ReturnsTrueAndAsksAboutTheOrigin()
    {
        // Arrange
        var stub = GitCredentialStub.Create(configOutput: "manager");
        var folder = CreateTempDirectory();
        try
        {
            var client = new GitCredentialClient(stub.Path, workingDirectory: folder);

            // Act
            var available = await client.IsAvailableAsync(Origin, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(available);
            var invocation = Assert.Single(stub.ReadInvocations());
            Assert.Equal(
                "-c core.askPass= config --get-urlmatch credential.helper https://feeds.example.com:8443/",
                invocation.Arguments);
        }
        finally
        {
            stub.Delete();
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test that no helper (empty answer, or git failing) means "not available".
    /// </summary>
    [Theory]
    [InlineData("", 0)]
    [InlineData("", 1)]
    [InlineData("manager", 128)]
    public async Task GitCredentialClient_IsAvailable_NoHelperOrGitFails_ReturnsFalse(string configOutput, int exitCode)
    {
        // Arrange
        var stub = GitCredentialStub.Create(configOutput: configOutput, configExitCode: exitCode);
        var folder = CreateTempDirectory();
        try
        {
            var client = new GitCredentialClient(stub.Path, workingDirectory: folder);

            // Act
            var available = await client.IsAvailableAsync(Origin, TestContext.Current.CancellationToken);

            // Assert
            Assert.False(available);
        }
        finally
        {
            stub.Delete();
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a missing git executable is "not available" and "nothing found", never an
    ///     exception.
    /// </summary>
    [Fact]
    public async Task GitCredentialClient_GitExecutableMissing_ReportsNothingWithoutThrowing()
    {
        // Arrange
        var missing = Path.Combine(Path.GetTempPath(), $"no-such-git-{Guid.NewGuid():N}.exe");
        var client = new GitCredentialClient(missing);
        var credential = new NuGetCredential("svc", SentinelPassword);

        // Act / Assert
        Assert.False(await client.IsAvailableAsync(Origin, TestContext.Current.CancellationToken));
        Assert.Null(await client.FillAsync(Origin, TestContext.Current.CancellationToken));
        Assert.False(await client.ApproveAsync(Origin, credential, TestContext.Current.CancellationToken));
        Assert.False(await client.RejectAsync(Origin, credential, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a lookup sends exactly the feed's protocol and host (with its port) on standard
    ///     input, runs <c>credential fill</c> with the generic provider fixed for that origin only,
    ///     in a non-interactive environment, and returns the credential git printed.
    /// </summary>
    [Fact]
    public async Task GitCredentialClient_Fill_CredentialStored_ReturnsItWithTheDocumentedRequestAndEnvironment()
    {
        // Arrange
        var stub = GitCredentialStub.Create(
            fillOutput: $"protocol=https|host=feeds.example.com:8443|username=svc|password={SentinelPassword}");
        var folder = CreateTempDirectory();
        try
        {
            var client = new GitCredentialClient(stub.Path, workingDirectory: folder);

            // Act
            var credential = await client.FillAsync(Origin, TestContext.Current.CancellationToken);

            // Assert: the credential
            Assert.NotNull(credential);
            Assert.Equal("svc", credential.UserName);
            Assert.Equal(SentinelPassword, credential.Secret);

            // Assert: the request
            var invocation = Assert.Single(stub.ReadInvocations());
            Assert.Equal(
                "-c core.askPass= -c credential.https://feeds.example.com:8443.provider=generic credential fill",
                invocation.Arguments);
            Assert.Equal("protocol=https\nhost=feeds.example.com:8443\n\n", invocation.StandardInput);

            // Assert: nothing can prompt, and git runs away from any repository
            Assert.Contains("GIT_TERMINAL_PROMPT=0", invocation.Environment, StringComparison.Ordinal);
            Assert.Contains("GCM_INTERACTIVE=never", invocation.Environment, StringComparison.Ordinal);
            Assert.Contains("GIT_ASKPASS=[]", invocation.Environment, StringComparison.Ordinal);
            Assert.Contains("SSH_ASKPASS=[]", invocation.Environment, StringComparison.Ordinal);
            Assert.Equal(
                Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(invocation.WorkingDirectory).TrimEnd(Path.DirectorySeparatorChar),
                ignoreCase: OperatingSystem.IsWindows());
        }
        finally
        {
            stub.Delete();
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test that an address on the scheme's default port is described without a port.
    /// </summary>
    [Fact]
    public async Task GitCredentialClient_Fill_DefaultPort_SendsHostWithoutPort()
    {
        // Arrange
        var stub = GitCredentialStub.Create();
        var folder = CreateTempDirectory();
        try
        {
            var client = new GitCredentialClient(stub.Path, workingDirectory: folder);

            // Act
            await client.FillAsync(FeedOrigin.Create(new Uri("https://feeds.example.com/v3/index.json")), TestContext.Current.CancellationToken);

            // Assert
            var invocation = Assert.Single(stub.ReadInvocations());
            Assert.Equal("protocol=https\nhost=feeds.example.com\n\n", invocation.StandardInput);
            Assert.Contains("credential.https://feeds.example.com.provider=generic", invocation.Arguments, StringComparison.Ordinal);
        }
        finally
        {
            stub.Delete();
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a lookup that finds nothing (git exits non-zero because it may not prompt), or
    ///     that finds an incomplete credential, yields nothing.
    /// </summary>
    [Theory]
    [InlineData("", 128)]
    [InlineData("username=svc", 0)]
    [InlineData("password=only-a-password", 0)]
    [InlineData("", 0)]
    public async Task GitCredentialClient_Fill_NothingOrIncomplete_ReturnsNull(string fillOutput, int exitCode)
    {
        // Arrange
        var stub = GitCredentialStub.Create(fillOutput: fillOutput, fillExitCode: exitCode);
        var folder = CreateTempDirectory();
        try
        {
            var client = new GitCredentialClient(stub.Path, workingDirectory: folder);

            // Act
            var credential = await client.FillAsync(Origin, TestContext.Current.CancellationToken);

            // Assert
            Assert.Null(credential);
        }
        finally
        {
            stub.Delete();
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test that saving a credential runs <c>credential approve</c> and sends the protocol, host,
    ///     user name and password.
    /// </summary>
    [Fact]
    public async Task GitCredentialClient_Approve_Credential_SendsItWithApprove()
    {
        // Arrange
        var stub = GitCredentialStub.Create();
        var folder = CreateTempDirectory();
        try
        {
            var client = new GitCredentialClient(stub.Path, workingDirectory: folder);

            // Act
            var accepted = await client.ApproveAsync(Origin, new NuGetCredential("svc", SentinelPassword), TestContext.Current.CancellationToken);

            // Assert
            Assert.True(accepted);
            var invocation = Assert.Single(stub.ReadInvocations());
            Assert.EndsWith("credential approve", invocation.Arguments, StringComparison.Ordinal);
            Assert.Equal(
                $"protocol=https\nhost=feeds.example.com:8443\nusername=svc\npassword={SentinelPassword}\n\n",
                invocation.StandardInput);
        }
        finally
        {
            stub.Delete();
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test that removing a credential runs <c>credential reject</c>, with the credential when
    ///     one is given and with the origin alone when not.
    /// </summary>
    [Fact]
    public async Task GitCredentialClient_Reject_WithAndWithoutCredential_SendsItWithReject()
    {
        // Arrange
        var stub = GitCredentialStub.Create();
        var folder = CreateTempDirectory();
        try
        {
            var client = new GitCredentialClient(stub.Path, workingDirectory: folder);

            // Act
            var withCredential = await client.RejectAsync(Origin, new NuGetCredential("svc", SentinelPassword), TestContext.Current.CancellationToken);
            var withoutCredential = await client.RejectAsync(Origin, null, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(withCredential);
            Assert.True(withoutCredential);
            var invocations = stub.ReadInvocations();
            Assert.Equal(2, invocations.Count);
            Assert.All(invocations, invocation => Assert.EndsWith("credential reject", invocation.Arguments, StringComparison.Ordinal));
            Assert.Equal(
                $"protocol=https\nhost=feeds.example.com:8443\nusername=svc\npassword={SentinelPassword}\n\n",
                invocations[0].StandardInput);
            Assert.Equal("protocol=https\nhost=feeds.example.com:8443\n\n", invocations[1].StandardInput);
        }
        finally
        {
            stub.Delete();
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a credential store that refuses the request is reported as <see langword="false"/>.
    /// </summary>
    [Fact]
    public async Task GitCredentialClient_ApproveAndReject_GitFails_ReturnFalse()
    {
        // Arrange
        var stub = GitCredentialStub.Create(approveExitCode: 1, rejectExitCode: 1);
        var folder = CreateTempDirectory();
        try
        {
            var client = new GitCredentialClient(stub.Path, workingDirectory: folder);
            var credential = new NuGetCredential("svc", SentinelPassword);

            // Act / Assert
            Assert.False(await client.ApproveAsync(Origin, credential, TestContext.Current.CancellationToken));
            Assert.False(await client.RejectAsync(Origin, credential, TestContext.Current.CancellationToken));
        }
        finally
        {
            stub.Delete();
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test that every character the <c>key=value</c> protocol cannot carry - a line feed, a
    ///     carriage return and NUL - is refused in each value that goes into a request (user name,
    ///     password; host and protocol of the origin are checked by the same rule), for approve and
    ///     reject, and git is not even run, so a malformed request is never written.
    /// </summary>
    [Theory]
    [InlineData("svc\nhost=evil.example.com", "pw")]
    [InlineData("svc", "pw\r\nprotocol=http")]
    [InlineData("svc", "pw\rx")]
    [InlineData("svc", "pw\nx")]
    [InlineData("sv\rc", "pw")]
    [InlineData("sv\0c", "pw")]
    [InlineData("svc", "p\0w")]
    public async Task GitCredentialClient_ApproveAndReject_ValueWithLineFeedCarriageReturnOrNul_AreRefusedWithoutRunningGit(string userName, string secret)
    {
        // Arrange
        var stub = GitCredentialStub.Create();
        var folder = CreateTempDirectory();
        try
        {
            var client = new GitCredentialClient(stub.Path, workingDirectory: folder);
            var credential = new NuGetCredential(userName, secret);

            // Act
            var approved = await client.ApproveAsync(Origin, credential, TestContext.Current.CancellationToken);
            var rejected = await client.RejectAsync(Origin, credential, TestContext.Current.CancellationToken);

            // Assert
            Assert.False(approved);
            Assert.False(rejected);
            Assert.Empty(stub.ReadInvocations());
        }
        finally
        {
            stub.Delete();
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a password containing <c>=</c> - including the <c>=</c> and <c>==</c> padding of
    ///     a Base64 token - is sent to git unchanged: git splits each line at the first <c>=</c>, so
    ///     the value may contain any number of them.
    /// </summary>
    [Theory]
    [InlineData("dG9rZW4tYWJjMTIz=")]
    [InlineData("dG9rZW4tYWJjMTI==")]
    [InlineData("a=b=c")]
    [InlineData("=leading")]
    public async Task GitCredentialClient_Approve_PasswordWithEquals_IsSentUnchanged(string password)
    {
        // Arrange
        var stub = GitCredentialStub.Create();
        var folder = CreateTempDirectory();
        try
        {
            var client = new GitCredentialClient(stub.Path, workingDirectory: folder);

            // Act
            var approved = await client.ApproveAsync(Origin, new NuGetCredential("svc", password), TestContext.Current.CancellationToken);

            // Assert
            Assert.True(approved);
            var invocation = Assert.Single(stub.ReadInvocations());
            Assert.Equal(
                $"protocol=https\nhost=feeds.example.com:8443\nusername=svc\npassword={password}\n\n",
                invocation.StandardInput);
        }
        finally
        {
            stub.Delete();
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a request is built from values that contain <c>=</c>, is not built from values
    ///     with a line break, and that a password a helper returned with <c>=</c> inside is read in
    ///     full.
    /// </summary>
    [Fact]
    public void GitCredentialClient_BuildRequestAndParse_EqualsRoundTripsAndLineBreaksAreNull()
    {
        // Arrange
        var withEquals = new NuGetCredential("svc", "a=b==");
        var withLineBreak = new NuGetCredential("svc", "pw\nusername=evil");

        // Act
        var request = GitCredentialClient.BuildRequest(Origin, withEquals);
        var refused = GitCredentialClient.BuildRequest(Origin, withLineBreak);
        var parsed = GitCredentialClient.ParseCredential("username=svc\npassword=a=b==\n");

        // Assert
        Assert.Equal("protocol=https\nhost=feeds.example.com:8443\nusername=svc\npassword=a=b==\n\n", request);
        Assert.Null(refused);
        Assert.NotNull(parsed);
        Assert.Equal("a=b==", parsed.Secret);
    }

    /// <summary>
    ///     Test the character rule itself: only a line feed, a carriage return and NUL are
    ///     rejected; everything else - <c>=</c>, other control characters, Unicode separators,
    ///     spaces and non-ASCII letters - is allowed.
    /// </summary>
    [Theory]
    [InlineData("plain-Value_1.2", true)]
    [InlineData("with space", true)]
    [InlineData("p\u00e4ssw\u00f6rd", true)]
    [InlineData("", true)]
    [InlineData("a=b", true)]
    [InlineData("a==", true)]
    [InlineData("a\tb", true)]
    [InlineData("a\u2028b", true)]
    [InlineData("a\nb", false)]
    [InlineData("a\rb", false)]
    [InlineData("a\0b", false)]
    public void GitCredentialClient_IsSafeValue_OnlyLineFeedCarriageReturnAndNul_AreRejected(string value, bool expected)
    {
        // Act
        var safe = GitCredentialClient.IsSafeValue(value);

        // Assert
        Assert.Equal(expected, safe);
    }

    /// <summary>
    ///     Test that extra configuration (used by the integration tests to isolate the credential
    ///     store) is passed to git with <c>-c</c> for every kind of invocation.
    /// </summary>
    [Fact]
    public async Task GitCredentialClient_ExtraConfiguration_IsPassedToGitWithDashC()
    {
        // Arrange
        var stub = GitCredentialStub.Create();
        var folder = CreateTempDirectory();
        try
        {
            var client = new GitCredentialClient(
                stub.Path, extraConfiguration: ["credential.helper=", "credential.helper=store"], workingDirectory: folder);

            // Act
            await client.IsAvailableAsync(Origin, TestContext.Current.CancellationToken);
            await client.FillAsync(Origin, TestContext.Current.CancellationToken);

            // Assert
            var invocations = stub.ReadInvocations();
            Assert.Equal(2, invocations.Count);
            Assert.All(
                invocations,
                invocation => Assert.Contains("-c credential.helper= -c credential.helper=store", invocation.Arguments, StringComparison.Ordinal));
        }
        finally
        {
            stub.Delete();
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test that the git executable is resolved for every call, so a changed setting applies at
    ///     once.
    /// </summary>
    [Fact]
    public async Task GitCredentialClient_ResolverConstructor_ResolvesTheExecutableOnEveryCall()
    {
        // Arrange: two stubs, and a resolver that switches from the first to the second
        var first = GitCredentialStub.Create();
        var second = GitCredentialStub.Create();
        var current = first.Path;
        var folder = CreateTempDirectory();
        try
        {
            var client = new GitCredentialClient(() => current, workingDirectory: folder);

            // Act
            await client.FillAsync(Origin, TestContext.Current.CancellationToken);
            current = second.Path;
            await client.FillAsync(Origin, TestContext.Current.CancellationToken);

            // Assert
            Assert.Single(first.ReadInvocations());
            Assert.Single(second.ReadInvocations());
        }
        finally
        {
            first.Delete();
            second.Delete();
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a git process that does not finish in time is stopped (killed) and reported as
    ///     "nothing", promptly, and that it never gets to finish what it was doing.
    /// </summary>
    [Fact]
    public async Task GitCredentialClient_Fill_ProcessHangs_IsKilledAtTheTimeLimitAndReturnsNull()
    {
        // Arrange: a stub that hangs for 3 seconds, and a limit of a quarter of a second
        var stub = GitCredentialStub.Create(fillOutput: "username=svc|password=pw", sleepSeconds: 3);
        var folder = CreateTempDirectory();
        try
        {
            var client = new GitCredentialClient(stub.Path, timeout: TimeSpan.FromMilliseconds(250), workingDirectory: folder);
            var stopwatch = Stopwatch.StartNew();

            // Act
            var credential = await client.FillAsync(Origin, TestContext.Current.CancellationToken);
            stopwatch.Stop();

            // Assert: given up long before the stub would have finished
            Assert.Null(credential);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2.5), $"Took {stopwatch.Elapsed}.");

            // Assert: the process was killed, so it never reaches the end of its sleep
            await Task.Delay(TimeSpan.FromSeconds(3.5), TestContext.Current.CancellationToken);
            Assert.False(stub.WroteFinishedMarker());
        }
        finally
        {
            stub.Delete();
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test that cancelling the caller's token while git hangs stops the process and surfaces as
    ///     a cancellation.
    /// </summary>
    [Fact]
    public async Task GitCredentialClient_Fill_CancelledWhileGitHangs_KillsTheProcessAndThrows()
    {
        // Arrange
        var stub = GitCredentialStub.Create(sleepSeconds: 3);
        var folder = CreateTempDirectory();
        try
        {
            var client = new GitCredentialClient(stub.Path, workingDirectory: folder);
            using var cancellation = new CancellationTokenSource();

            // Act
            var pending = client.FillAsync(Origin, cancellation.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
            await cancellation.CancelAsync();

            // Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            await Task.Delay(TimeSpan.FromSeconds(3.5), TestContext.Current.CancellationToken);
            Assert.False(stub.WroteFinishedMarker());
        }
        finally
        {
            stub.Delete();
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test that no operation logs what it sent to git or what git answered: neither the password
    ///     nor the request lines reach the log.
    /// </summary>
    [Fact]
    public async Task GitCredentialClient_AllOperations_NeverLogRequestOrResponseContent()
    {
        // Arrange
        var stub = GitCredentialStub.Create(
            fillOutput: $"protocol=https|host=feeds.example.com:8443|username=svc-user-4417|password={SentinelPassword}");
        var folder = CreateTempDirectory();
        var logger = new ListLogger();
        try
        {
            var client = new GitCredentialClient(stub.Path, logger, workingDirectory: folder);
            var credential = new NuGetCredential("svc-user-4417", SentinelPassword);

            // Act
            await client.IsAvailableAsync(Origin, TestContext.Current.CancellationToken);
            var filled = await client.FillAsync(Origin, TestContext.Current.CancellationToken);
            await client.ApproveAsync(Origin, credential, TestContext.Current.CancellationToken);
            await client.RejectAsync(Origin, credential, TestContext.Current.CancellationToken);

            // Assert
            Assert.NotNull(filled);
            var lines = logger.Snapshot();
            Assert.NotEmpty(lines);
            Assert.All(lines, line => Assert.DoesNotContain(SentinelPassword, line, StringComparison.Ordinal));
            Assert.All(lines, line => Assert.DoesNotContain("svc-user-4417", line, StringComparison.Ordinal));
            Assert.All(lines, line => Assert.DoesNotContain("password=", line, StringComparison.Ordinal));
        }
        finally
        {
            stub.Delete();
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test that the constructors refuse a missing resolver, a blank path and a non-positive
    ///     time limit.
    /// </summary>
    [Fact]
    public void GitCredentialClient_Constructors_InvalidArguments_Throw()
    {
        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => new GitCredentialClient((Func<string>)null!));
        Assert.Throws<ArgumentException>(() => new GitCredentialClient(" "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GitCredentialClient("git", timeout: TimeSpan.Zero));
    }

    /// <summary>
    ///     Test that the response parser takes the last values, tolerates carriage returns and
    ///     unknown keys, and refuses incomplete answers.
    /// </summary>
    [Fact]
    public void GitCredentialClient_ParseCredential_VariousResponses_ParsesOnlyCompleteOnes()
    {
        // Act
        var complete = GitCredentialClient.ParseCredential("protocol=https\r\nhost=h\r\nusername=u\r\npassword=p=q\r\noauth_refresh_token=x\r\n");
        var noPassword = GitCredentialClient.ParseCredential("username=u\n");
        var emptyPassword = GitCredentialClient.ParseCredential("username=u\npassword=\n");

        // Assert
        Assert.Equal(new NuGetCredential("u", "p=q"), complete);
        Assert.Null(noPassword);
        Assert.Null(emptyPassword);
    }

    /// <summary>
    ///     Creates a fresh temporary directory.
    /// </summary>
    /// <returns>The directory path.</returns>
    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"agentcontrol_git_credential_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
