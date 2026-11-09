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
using DemaConsulting.AgentControl.Tests.PackageCredentials;

// cspell:ignore CONOUT ufeff gitx
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
    ///     A second sentinel SHA-1 object id.
    /// </summary>
    private const string OtherSha = "fedcba9876543210fedcba9876543210fedcba98";

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
        Assert.DoesNotContain("example.invalid", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a failure that is not one the user can act on, a timeout and a missing git are
    ///     reported with the right type and a message that points to the fix.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_OtherFailures_AreReportedClearly()
    {
        // Arrange
        var other = NewStub().Respond(
            "ls-remote", standardError: "fatal: detected dubious ownership in repository at '/srv/private/repo.git'", exitCode: 128);
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
        Assert.Contains("owned by another user", unsafeFailure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("/srv/private", unsafeFailure.Message, StringComparison.Ordinal);
        Assert.Equal(PackageSourceFailureKind.TimedOut, timedOut.Kind);
        Assert.Contains("Settings", missing.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that canceling a listing stops it with an <see cref="OperationCanceledException"/> and
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
    ///     Test that no versions are offered for a package ID other than the configured one, and that
    ///     git is not run to find that out.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_ListVersions_OtherPackageId_ReturnsEmptyWithoutRunningGit()
    {
        // Arrange: a source for contoso-agents over a remote that does have tags
        var stub = NewStub().Respond("ls-remote", $"{Sha}\trefs/tags/v1.0.0\n");
        var provider = new GitSourceProvider(Definition(), new GitSourceOptions(() => stub.Path, NewFolder()));

        // Act
        var versions = await provider.ListVersionsAsync("some-other-package", TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(versions);
        Assert.Empty(stub.ReadInvocations());
    }

    /// <summary>
    ///     Test that two sources over different repositories keep their commits in different folders
    ///     under the same cache root, and that a source keeps the same folder for the same
    ///     repository however its address is capitalized.
    /// </summary>
    [Fact]
    public void GitSourceProvider_RepositoryCacheFolder_DiffersForDifferentRepositories()
    {
        // Arrange: three sources sharing one cache root
        var cacheRoot = NewFolder();
        var stub = NewStub();
        var options = new GitSourceOptions(() => stub.Path, cacheRoot);
        var first = Definition();
        var second = Definition();
        second.Location = "https://example.invalid/org/other.git";
        var sameAsFirst = Definition();
        sameAsFirst.Location = "HTTPS://EXAMPLE.INVALID/org/repo.git";

        // Act
        var firstFolder = new GitSourceProvider(first, options).RepositoryCacheFolder;
        var secondFolder = new GitSourceProvider(second, options).RepositoryCacheFolder;
        var sameFolder = new GitSourceProvider(sameAsFirst, options).RepositoryCacheFolder;

        // Assert: different repositories never share a folder, and one repository always has the same one
        Assert.NotEqual(firstFolder, secondFolder);
        Assert.Equal(firstFolder, sameFolder);
        Assert.StartsWith(Path.GetFullPath(cacheRoot), firstFolder, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that an ssh server whose host key is not trusted yet is reported as unreachable with
    ///     the instruction to connect once with ssh and accept the host key, without the address.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_GitFailure_UntrustedHostKey_TellsHowToTrustIt()
    {
        // Arrange
        var stub = NewStub().Respond(
            "ls-remote", standardError: "Host key verification failed.\nfatal: Could not read from remote repository.", exitCode: 128);
        var definition = Definition();
        definition.Location = "git@example.invalid:org/repo.git";
        var provider = new GitSourceProvider(definition, new GitSourceOptions(() => stub.Path, NewFolder()));

        // Act
        var failure = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => provider.ListVersionsAsync("contoso-agents", TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(PackageSourceFailureKind.Unreachable, failure.Kind);
        Assert.Contains("Connect once with ssh from a terminal and accept the host key", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("example.invalid", failure.Message, StringComparison.Ordinal);
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
    ///     Test that two tags that name the same version (they differ only in the letter case of the
    ///     prerelease, which version comparison ignores) keep the smallest tag name and log a warning.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_ListVersions_TwoTagsSameVersion_KeepsSmallestTagAndWarns()
    {
        // Arrange: the remote reports two tags for one version
        var stub = NewStub().Respond(
            "ls-remote", $"{Sha}\trefs/tags/v1.0.0-beta\n{OtherSha}\trefs/tags/v1.0.0-BETA\n");
        var logger = new ListLogger();
        var provider = new GitSourceProvider(Definition(), new GitSourceOptions(() => stub.Path, NewFolder(), Logger: logger));

        // Act
        var versions = await provider.ListVersionsAsync("contoso-agents", TestContext.Current.CancellationToken);

        // Assert: the smaller name in ordinal order is kept, and the choice is logged
        var kept = Assert.Single(versions);
        Assert.Equal("v1.0.0-BETA", kept.Location);
        Assert.Equal(OtherSha, kept.Revision);
        Assert.Contains(logger.Snapshot(), line => line.Contains("same version", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that a package whose tag the repository lists, but at another commit than the package
    ///     names (a forged or stale listing entry), is refused before anything is fetched.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_Acquire_ListedTagAtOtherCommit_IsRefusedBeforeFetching()
    {
        // Arrange: the repository lists v1.0.0 at Sha; the package claims another commit
        var stub = NewStub().Respond("ls-remote", $"{Sha}\trefs/tags/v1.0.0\n");
        var provider = new GitSourceProvider(Definition(), new GitSourceOptions(() => stub.Path, NewFolder()));
        Assert.True(PackageVersion.TryParseStrict("1.0.0", out var version));
        var forged = new DiscoveredPackage("contoso-agents", version!, "v1.0.0", "Repo", OtherSha);

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.AcquireAsync(forged, TestContext.Current.CancellationToken));

        // Assert: refused for the right reason, and only the listing ran - no repository was created or fetched
        Assert.Contains("Refresh the list", failure.Message, StringComparison.Ordinal);
        Assert.Equal(["ls-remote"], stub.ReadInvocations().Select(i => i.Command));
    }

    /// <summary>
    ///     Test that a package for a version the repository does not list at all - another tag that
    ///     matches the pattern, never listed - is refused before anything is fetched.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_Acquire_VersionNotListed_IsRefusedBeforeFetching()
    {
        // Arrange: the repository lists only v1.0.0; the package names v2.0.0, which matches the pattern
        var stub = NewStub().Respond("ls-remote", $"{Sha}\trefs/tags/v1.0.0\n");
        var provider = new GitSourceProvider(Definition(), new GitSourceOptions(() => stub.Path, NewFolder()));
        Assert.True(PackageVersion.TryParseStrict("2.0.0", out var version));
        var forged = new DiscoveredPackage("contoso-agents", version!, "v2.0.0", "Repo", OtherSha);

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.AcquireAsync(forged, TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("not offered by the repository", failure.Message, StringComparison.Ordinal);
        Assert.Equal(["ls-remote"], stub.ReadInvocations().Select(i => i.Command));
    }

    /// <summary>
    ///     Test that a package whose tag name is not the tag the repository lists for that version
    ///     (the version is listed under a smaller tag name) is refused before anything is fetched.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_Acquire_TagThatIsNotTheListedOneForTheVersion_IsRefusedBeforeFetching()
    {
        // Arrange: both spellings are tags, but only v1.0.0-BETA is listed for the version
        var stub = NewStub().Respond(
            "ls-remote", $"{Sha}\trefs/tags/v1.0.0-beta\n{OtherSha}\trefs/tags/v1.0.0-BETA\n");
        var provider = new GitSourceProvider(Definition(), new GitSourceOptions(() => stub.Path, NewFolder()));
        Assert.True(PackageVersion.TryParseStrict("1.0.0-beta", out var version));
        var forged = new DiscoveredPackage("contoso-agents", version!, "v1.0.0-beta", "Repo", Sha);

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.AcquireAsync(forged, TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("not offered by the repository", failure.Message, StringComparison.Ordinal);
        Assert.Equal(["ls-remote"], stub.ReadInvocations().Select(i => i.Command));
    }

    /// <summary>
    ///     Test which path components are safe to create on every file system and which name the
    ///     repository metadata folder in any of its spellings.
    /// </summary>
    [Theory]
    [InlineData("agents", true, false)]
    [InlineData("a b", true, false)]
    [InlineData("caf\u00e9.md", true, false)]
    [InlineData("release-notes.md", true, false)]
    [InlineData(".gitattributes", true, false)]
    [InlineData(".gitignore", true, false)]
    [InlineData("CON", false, false)]
    [InlineData("con.md", false, false)]
    [InlineData("Nul.txt", false, false)]
    [InlineData("COM1", false, false)]
    [InlineData("lpt9.x", false, false)]
    [InlineData("CONOUT$", false, false)]
    [InlineData("COM\u00b9", false, false)]
    [InlineData("com\u00b2.md", false, false)]
    [InlineData("COM\u00b3.tar.gz", false, false)]
    [InlineData("LPT\u00b9", false, false)]
    [InlineData("lpt\u00b2.txt", false, false)]
    [InlineData("Lpt\u00b3", false, false)]
    [InlineData("COM\u00b9.", false, false)]
    [InlineData("LPT\u00b2 ", false, false)]
    [InlineData("COM\u2074", true, false)]
    [InlineData("COM\u00b9x", true, false)]
    [InlineData("COM10", true, false)]
    [InlineData("console.md", true, false)]
    [InlineData("a:b", false, false)]
    [InlineData("a:stream", false, false)]
    [InlineData("a\\b", false, false)]
    [InlineData("a*b", false, false)]
    [InlineData("a?b", false, false)]
    [InlineData("a\"b", false, false)]
    [InlineData("a<b", false, false)]
    [InlineData("a>b", false, false)]
    [InlineData("a|b", false, false)]
    [InlineData("ends.", false, false)]
    [InlineData("ends ", false, false)]
    [InlineData("tab\tinside", false, false)]
    [InlineData("", false, false)]
    [InlineData(".", false, false)]
    [InlineData("..", false, false)]
    [InlineData(".git", true, true)]
    [InlineData(".GIT", true, true)]
    [InlineData(".Git", true, true)]
    [InlineData("git~1", true, true)]
    [InlineData("GIT~12", true, true)]
    [InlineData(".g\u200cit", true, true)]
    [InlineData(".\u200eGit", true, true)]
    [InlineData("\ufeff.git", true, true)]
    [InlineData(".git.", false, true)]
    [InlineData("git~", true, false)]
    [InlineData(".gitx", true, false)]
    [InlineData("a.git", true, false)]
    public void GitSourceProvider_PathComponentRules_ClassifyUnsafeNamesAndGitFolderSpellings(
        string component, bool safe, bool gitFolder)
    {
        // Arrange: one component of a repository path

        // Act
        var isSafe = GitSourceProvider.IsSafePathComponent(component);
        var isGitFolder = GitSourceProvider.IsGitFolderName(component);

        // Assert: ".git." is a git-folder spelling (NTFS ignores the trailing dot) and is not safe either; the names
        // that only spell the git folder in an unusual way are safe in themselves, which is why the layout check asks both
        Assert.Equal(safe, isSafe);
        Assert.Equal(gitFolder, isGitFolder);
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
