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
using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.GitIntegration;
using DemaConsulting.AgentControl.Tests.GitIntegration;

namespace DemaConsulting.AgentControl.Tests.AgentPackageManagement;

/// <summary>
///     Tests of <see cref="GitSourceProvider"/> that need no repository: validation of the
///     definition (a hand-edited settings file fails closed before git runs), the arguments and
///     environment the provider asks the client for, the prompt rule of interactive and background
///     work, the translation of git failures into the provider's failure kinds, and cancellation.
///     A recording stub stands in for git.
/// </summary>
[Collection("RealProcess")]
public sealed class GitSourceProviderTests : IDisposable
{
    /// <summary>
    ///     An address nothing is ever contacted at.
    /// </summary>
    private const string Url = "https://example.invalid/org/repo.git";

    /// <summary>
    ///     A sentinel SHA-1 object id.
    /// </summary>
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";

    /// <summary>
    ///     The stubs to delete after a test.
    /// </summary>
    private readonly List<GitRemoteStub> _stubs = [];

    /// <summary>
    ///     The folders to delete after a test.
    /// </summary>
    private readonly List<string> _folders = [];

    /// <summary>
    ///     Test that a definition that is not a Git source, has no alias, or names a relative cache
    ///     root is refused at construction.
    /// </summary>
    [Fact]
    public void GitSourceProvider_Constructor_WrongTypeBlankAliasOrRelativeCache_ThrowsArgumentException()
    {
        // Arrange
        var options = new GitSourceOptions(() => "git", NewFolder());
        var zip = Definition();
        zip.Type = PackageSourceType.Zip;
        var blank = Definition();
        blank.Alias = " ";

        // Act / Assert
        Assert.Throws<ArgumentException>(() => new GitSourceProvider(zip, options));
        Assert.Throws<ArgumentException>(() => new GitSourceProvider(blank, options));
        Assert.Throws<ArgumentException>(() => new GitSourceProvider(Definition(), options with { CacheRoot = "relative/cache" }));
        Assert.Throws<ArgumentNullException>(() => new GitSourceProvider(null!, options));
    }

    /// <summary>
    ///     Test that a hand-edited definition with an invalid address, pattern, sub-folder or package
    ///     list fails closed with a message at construction, before any git runs.
    /// </summary>
    [Theory]
    [InlineData("location", "ext::sh -c calc")]
    [InlineData("location", "http://example.invalid/r.git")]
    [InlineData("location", "https://user:pw@example.invalid/r.git")]
    [InlineData("location", "")]
    [InlineData("pattern", "v")]
    [InlineData("pattern", "{version}{version}")]
    [InlineData("pattern", null)]
    [InlineData("subfolder", "../escape")]
    [InlineData("subfolder", "/absolute")]
    [InlineData("subfolder", "a/.git/b")]
    [InlineData("ids", "none")]
    [InlineData("ids", "two")]
    [InlineData("ids", "bad id!")]
    public void GitSourceProvider_Constructor_InvalidSettings_FailsClosed(string field, string? value)
    {
        // Arrange
        var stub = NewStub();
        var definition = Definition();
        switch (field)
        {
            case "location":
                definition.Location = value!;
                break;
            case "pattern":
                definition.TagPattern = value;
                break;
            case "subfolder":
                definition.Subfolder = value;
                break;
            default:
                definition.PackageIds = value switch
                {
                    "none" => [],
                    "two" => ["a", "b"],
                    _ => [value!]
                };
                break;
        }

        // Act
        var failure = Assert.Throws<InvalidOperationException>(
            () => new GitSourceProvider(definition, new GitSourceOptions(() => stub.Path, NewFolder())));

        // Assert: nothing ran, and the message does not echo a secret that was in the location
        Assert.Contains("Repo", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("pw@", failure.Message, StringComparison.Ordinal);
        Assert.Empty(stub.ReadInvocations());
    }

    /// <summary>
    ///     Test that listing narrows the remote listing with the pattern's literal prefix, allows the
    ///     transport of the address only, and keeps only the tags that are versions.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_ListVersions_UsesPatternGlobAndAddressTransport()
    {
        // Arrange
        var stub = NewStub().Respond(
            "ls-remote",
            $"{Sha}\trefs/tags/release/1.0.0\n{Sha}\trefs/tags/release/notes\n{Sha}\trefs/tags/release/2.0.0^{{}}\n");
        var definition = Definition("release/{version}");
        var provider = new GitSourceProvider(definition, new GitSourceOptions(() => stub.Path, NewFolder()));

        // Act
        var versions = await provider.ListVersionsAsync("contoso-agents", TestContext.Current.CancellationToken);

        // Assert
        var package = Assert.Single(versions);
        Assert.Equal("release/1.0.0", package.Location);
        Assert.Equal(Sha, package.Revision);
        var invocation = Assert.Single(stub.ReadInvocations());
        Assert.EndsWith($"ls-remote --tags -- {Url} refs/tags/release/*", invocation.Arguments, StringComparison.Ordinal);
        Assert.Contains("protocol.https.allow=always", invocation.Arguments, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an ssh address allows only ssh, and that listing needs no network call for the
    ///     package ID.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_SshAddress_AllowsOnlySsh_AndListPackageIdsNeedsNoGit()
    {
        // Arrange
        var stub = NewStub();
        var definition = Definition();
        definition.Location = "git@example.invalid:org/repo.git";
        var provider = new GitSourceProvider(definition, new GitSourceOptions(() => stub.Path, NewFolder()));

        // Act
        var ids = await provider.ListPackageIdsAsync(TestContext.Current.CancellationToken);
        await provider.ListVersionsAsync("contoso-agents", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["contoso-agents"], ids);
        var invocations = stub.ReadInvocations();
        var list = invocations.Single(i => i.Command == "ls-remote");
        Assert.Contains("protocol.ssh.allow=always", list.Arguments, StringComparison.Ordinal);
        Assert.DoesNotContain("protocol.https.allow", list.Arguments, StringComparison.Ordinal);
        Assert.Equal("ssh", list.Environment["GIT_ALLOW_PROTOCOL"]);
    }

    /// <summary>
    ///     Test that the interactive flag of the options reaches git: background work sets the
    ///     no-prompt variables, user-initiated work leaves the credential manager free.
    /// </summary>
    [Theory]
    [InlineData(false, "never")]
    [InlineData(true, "")]
    public async Task GitSourceProvider_InteractiveFlag_IsHonored(bool interactive, string expectedGcmInteractive)
    {
        // Arrange
        var previous = Environment.GetEnvironmentVariable("GCM_INTERACTIVE");
        Environment.SetEnvironmentVariable("GCM_INTERACTIVE", null);
        try
        {
            var stub = NewStub();
            var provider = new GitSourceProvider(
                Definition(), new GitSourceOptions(() => stub.Path, NewFolder(), Interactive: interactive));

            // Act
            await provider.ListVersionsAsync("contoso-agents", TestContext.Current.CancellationToken);

            // Assert
            var invocation = Assert.Single(stub.ReadInvocations());
            Assert.Equal(expectedGcmInteractive, invocation.Environment["GCM_INTERACTIVE"]);
            Assert.Equal(!interactive, invocation.Arguments.Contains("core.askPass=", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GCM_INTERACTIVE", previous);
        }
    }

    /// <summary>
    ///     Test that what git says when it fails becomes the provider's failure kind, with a message
    ///     that tells the user what to do and never repeats git's raw text or the address.
    /// </summary>
    [Theory]
    [InlineData("fatal: could not read Username for 'https://example.invalid': terminal prompts disabled", "AuthenticationFailed", "Sign-in needed")]
    [InlineData("remote: Invalid username or password.\nfatal: Authentication failed", "AuthenticationFailed", "Sign-in needed")]
    [InlineData("git@example.invalid: Permission denied (publickey).", "AuthenticationFailed", "Sign-in needed")]
    [InlineData("Host key verification failed.", "Unreachable", "host key")]
    [InlineData("fatal: repository 'https://example.invalid/org/repo.git/' not found", "NotFound", "was not found")]
    [InlineData("fatal: unable to access: Could not resolve host: example.invalid", "Unreachable", "could not be reached")]
    public async Task GitSourceProvider_GitFailure_IsTranslatedToFailureKind(string stderr, string kind, string messagePart)
    {
        // Arrange
        var stub = NewStub().Respond("ls-remote", standardError: stderr, exitCode: 128);
        var provider = new GitSourceProvider(Definition(), new GitSourceOptions(() => stub.Path, NewFolder()));

        // Act
        var failure = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => provider.ListVersionsAsync("contoso-agents", TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(Enum.Parse<PackageSourceFailureKind>(kind), failure.Kind);
        Assert.Contains(messagePart, failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'Repo'", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Invalid username or password", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a failure that is not one the user can act on, a timeout and a missing git are
    ///     reported with the right type and a message that points to the fix.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_OtherFailures_AreReportedClearly()
    {
        // Arrange
        var other = NewStub().Respond("ls-remote", standardError: "fatal: detected dubious ownership", exitCode: 128);
        var slow = NewStub().Respond("ls-remote", sleepSeconds: 30);

        // Act
        var unsafeFailure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new GitSourceProvider(Definition(), new GitSourceOptions(() => other.Path, NewFolder()))
                .ListVersionsAsync("contoso-agents", TestContext.Current.CancellationToken));
        var timedOut = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => new GitSourceProvider(
                    Definition(), new GitSourceOptions(() => slow.Path, NewFolder(), ListTimeout: TimeSpan.FromSeconds(1)))
                .ListVersionsAsync("contoso-agents", TestContext.Current.CancellationToken));
        var missing = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new GitSourceProvider(
                    Definition(), new GitSourceOptions(() => Path.Combine(NewFolder(), "no-git"), NewFolder()))
                .ListVersionsAsync("contoso-agents", TestContext.Current.CancellationToken));

        // Assert
        Assert.IsNotType<PackageSourceAccessException>(unsafeFailure);
        Assert.Contains("dubious ownership", unsafeFailure.Message, StringComparison.Ordinal);
        Assert.Equal(PackageSourceFailureKind.TimedOut, timedOut.Kind);
        Assert.Contains("Settings", missing.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that cancelling a listing stops it with an <see cref="OperationCanceledException"/> and
    ///     kills git.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_ListVersions_Cancelled_ThrowsOperationCanceled()
    {
        // Arrange
        var stub = NewStub().Respond("ls-remote", sleepSeconds: 30);
        var provider = new GitSourceProvider(Definition(), new GitSourceOptions(() => stub.Path, NewFolder()));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(1));
        var stopwatch = Stopwatch.StartNew();

        // Act / Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.ListVersionsAsync("contoso-agents", cancellation.Token));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(20));
    }

    /// <summary>
    ///     Test that acquiring a package this source did not report is refused before git runs.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_Acquire_UnreportedPackage_IsRefusedWithoutRunningGit()
    {
        // Arrange
        var stub = NewStub();
        var provider = new GitSourceProvider(Definition(), new GitSourceOptions(() => stub.Path, NewFolder()));
        Assert.True(PackageVersion.TryParseStrict("1.0.0", out var version));
        var package = new DiscoveredPackage("contoso-agents", version!, "v1.0.0", "Repo", Sha);

        // Act / Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.AcquireAsync(package with { SourceAlias = "Other" }, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.AcquireAsync(package with { Revision = null }, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.AcquireAsync(package with { Location = "v2.0.0" }, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => provider.AcquireAsync(null!, TestContext.Current.CancellationToken));
        Assert.Empty(stub.ReadInvocations());
    }

    /// <summary>
    ///     Test the detection of names that a case-folding or normalizing file system would merge,
    ///     without any repository: case, a file against a folder, folders, and Unicode forms are
    ///     collisions; identical and genuinely different names are not.
    /// </summary>
    [Theory]
    [InlineData(".github/agents/A.md|.github/agents/a.md", true)]
    [InlineData(".github/skills/x|.github/skills/X/y.md", true)]
    [InlineData(".github/standards/Docs/a.md|.github/standards/docs/b.md", true)]
    [InlineData(".github/Agents/a.md|.github/agents/b.md", true)]
    [InlineData(".github/agents/caf\u00e9.md|.github/agents/cafe\u0301.md", true)]
    [InlineData(".github/agents/CAF\u00c9.md|.github/agents/cafe\u0301.md", true)]
    [InlineData(".github/agents/a.md|.github/agents/b.md|.github/skills/a.md|release-notes.md", false)]
    [InlineData(".github/agents/a.md|.github/agents/a.md", false)]
    [InlineData(".github/skills/s/one.md|.github/skills/s/two.md|.github/skills/t/one.md", false)]
    [InlineData("", false)]
    public void GitSourceProvider_FindFoldedPathCollision_DetectsCaseAndUnicodeFormCollisions(string paths, bool expectCollision)
    {
        // Act
        var collision = GitSourceProvider.FindFoldedPathCollision(paths.Split('|', StringSplitOptions.RemoveEmptyEntries));

        // Assert
        Assert.Equal(expectCollision, collision is not null);
    }

    /// <summary>
    ///     Test that, with the version check on, a git that is too old is refused before anything is
    ///     listed or fetched - for listing, previewing and acquiring - with the client's own
    ///     exception of kind <c>GitTooOld</c> (not wrapped, not prefixed with the alias) so the
    ///     application can recognize it.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_EnforceMinimumGitVersion_OldGit_IsRefusedAsGitTooOldBeforeAnyGitWork()
    {
        // Arrange
        var stub = NewStub().Respond("version", "git version 2.20.1\n");
        var provider = new GitSourceProvider(
            Definition(), new GitSourceOptions(() => stub.Path, NewFolder(), EnforceMinimumGitVersion: true));
        Assert.True(PackageVersion.TryParseStrict("1.0.0", out var version));
        var package = new DiscoveredPackage("contoso-agents", version!, "v1.0.0", "Repo", Sha);

        // Act
        var listing = await Assert.ThrowsAsync<GitRemoteException>(
            () => provider.ListVersionsAsync("contoso-agents", TestContext.Current.CancellationToken));
        var preview = await Assert.ThrowsAsync<GitRemoteException>(
            () => provider.PreviewAsync(TestContext.Current.CancellationToken));
        var acquire = await Assert.ThrowsAsync<GitRemoteException>(
            () => provider.AcquireAsync(package, TestContext.Current.CancellationToken));

        // Assert
        foreach (var failure in new[] { listing, preview, acquire })
        {
            Assert.Equal(GitFailureKind.GitTooOld, failure.Kind);
            Assert.Contains("Git 2.29 or newer", failure.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("Repo", failure.Message, StringComparison.Ordinal);
        }

        Assert.All(stub.ReadInvocations(), invocation => Assert.Equal("version", invocation.Command));
    }

    /// <summary>
    ///     Test that, with the version check on, a new enough git is checked once, before the first
    ///     listing, and that the check is not repeated for later work.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_EnforceMinimumGitVersion_NewGit_IsCheckedOnceBeforeTheFirstListing()
    {
        // Arrange
        var stub = NewStub()
            .Respond("version", "git version 2.43.0\n")
            .Respond("ls-remote", $"{Sha}\trefs/tags/v1.0.0\n");
        var provider = new GitSourceProvider(
            Definition(), new GitSourceOptions(() => stub.Path, NewFolder(), EnforceMinimumGitVersion: true));

        // Act
        var first = await provider.ListVersionsAsync("contoso-agents", TestContext.Current.CancellationToken);
        await provider.PreviewAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(first);
        Assert.Equal(["version", "ls-remote", "ls-remote"], stub.ReadInvocations().Select(i => i.Command));
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
            GitSourceProvider.DeleteDirectory(folder);
        }
    }

    /// <summary>
    ///     Creates a valid Git source definition.
    /// </summary>
    private static PackageSourceDefinition Definition(string pattern = "v{version}") => new()
    {
        Alias = "Repo",
        Type = PackageSourceType.Git,
        Location = Url,
        TagPattern = pattern,
        PackageIds = ["contoso-agents"]
    };

    /// <summary>
    ///     Creates a stub that is deleted after the test.
    /// </summary>
    private GitRemoteStub NewStub()
    {
        var stub = GitRemoteStub.Create();
        _stubs.Add(stub);
        return stub;
    }

    /// <summary>
    ///     Creates a folder path that is removed after the test.
    /// </summary>
    private string NewFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_git_provider_test_" + Guid.NewGuid().ToString("N"));
        _folders.Add(path);
        return path;
    }
}
