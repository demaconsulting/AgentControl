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

namespace DemaConsulting.AgentControl.Tests.AgentPackageManagement;

/// <summary>
///     Subsystem tests for AgentPackageManagement: configured package-source definitions resolved
///     by the registry into providers and used together with the signature-support check, the way
///     the application drives them - without any user interface.
/// </summary>
/// <remarks>
///     The signed-package tests use a real signed package (see <see cref="SignedNuGetFixture"/>)
///     and are skipped only where nuget.org cannot be reached or the machine's trust store lacks
///     the needed roots; they share a non-parallel collection because they set a process-wide
///     environment variable.
/// </remarks>
[Collection(SignedFixtureCollection.Name)]
public sealed class AgentPackageManagementSubsystemTests : IDisposable
{
    /// <summary>
    ///     The temporary folders created by the running test, removed on disposal.
    /// </summary>
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     Test that, through the registry, a source that requires signed packages refuses an
    ///     unsigned package - nothing is installed - while a source of the same feed that does not
    ///     require them still serves it.
    /// </summary>
    [Fact]
    public async Task AgentPackageManagementSubsystem_RequireSignedSource_UnsignedPackageRefusedWhileOpenSourceServesIt()
    {
        // Arrange: one feed folder with an unsigned agent package, two sources over it
        var feed = CreateTempDirectory();
        var id = "contoso.agents." + Guid.NewGuid().ToString("N");
        NuGetFeedBuilder.CreateAgentPackage(feed, id, "1.0.0");
        var registry = PackageSourceRegistry.Create(
            [Definition("Open", feed, requireSigned: false), Definition("Strict", feed, requireSigned: true)],
            CreateTempDirectory());

        // Act
        var open = registry.Resolve("Open");
        var openPackage = Assert.Single(await open.ListVersionsAsync(id, CancellationToken.None));
        using var reader = await open.AcquireAsync(openPackage, CancellationToken.None);
        var strict = registry.Resolve("Strict");
        var strictPackage = Assert.Single(await strict.ListVersionsAsync(id, CancellationToken.None));
        var refusal = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => strict.AcquireAsync(strictPackage, CancellationToken.None));

        // Assert
        Assert.NotEmpty(reader.ListFiles());
        Assert.Equal(PackageSourceFailureKind.SignatureRejected, refusal.Kind);
        Assert.Contains("is not validly signed", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that on a platform that cannot verify signatures a source whose settings require
    ///     them refuses to list or acquire (it never skips the check), while a source that does not
    ///     require them is unaffected.
    /// </summary>
    [Fact]
    public async Task AgentPackageManagementSubsystem_RequireSignedOnUnsupportedPlatform_SourceRefusesAndOthersWork()
    {
        // Arrange: the support check answers "unsupported", as it does for a failing probe
        var supported = SignatureVerificationSupport.Evaluate(
            isWindows: true, isLinux: false, isMacOS: false, () => throw new InvalidOperationException("no chains"));
        var feed = CreateTempDirectory();
        var id = "contoso.agents." + Guid.NewGuid().ToString("N");
        NuGetFeedBuilder.CreateAgentPackage(feed, id, "1.0.0");
        var rootBase = CreateTempDirectory();
        var registry = new PackageSourceRegistry(
            [Definition("Open", feed, requireSigned: false), Definition("Strict", feed, requireSigned: true)],
            definition => new NuGetSourceProvider(definition, rootBase, signatureVerificationSupported: supported));

        // Act
        var open = Assert.Single(await registry.Resolve("Open").ListVersionsAsync(id, CancellationToken.None));
        var refusal = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => registry.Resolve("Strict").ListVersionsAsync(id, CancellationToken.None));

        // Assert
        Assert.False(supported);
        Assert.Equal("1.0.0", open.Location);
        Assert.Equal(PackageSourceFailureKind.SignatureRejected, refusal.Kind);
        Assert.Contains("not supported on this platform", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a hand-edited settings file with an unreadable signer fingerprint stops the
    ///     source when it is first used instead of widening trust to any signer.
    /// </summary>
    [Fact]
    public void AgentPackageManagementSubsystem_UnreadableSignerFingerprint_SourceIsRefusedWhenResolved()
    {
        // Arrange
        var definition = Definition("Strict", CreateTempDirectory(), requireSigned: true);
        definition.SignerFingerprint = "not a fingerprint";
        var registry = PackageSourceRegistry.Create([definition], CreateTempDirectory());

        // Act / Assert
        Assert.Throws<InvalidOperationException>(() => registry.Resolve("Strict"));
    }

    /// <summary>
    ///     Test that, through the registry, a validly signed package is accepted by a source that
    ///     requires signatures without a pinned publisher and by one pinned to the package's author
    ///     (however the fingerprint was typed), and rejected - and not installed - by a source pinned
    ///     to another publisher.
    /// </summary>
    [Fact]
    public async Task AgentPackageManagementSubsystem_SignedPackage_AcceptedWhenAnyOrAuthorPinnedAndRejectedWhenOtherPinned()
    {
        // Arrange: three sources over the same feed holding a real signed package
        using var scope = SignedNuGetFixture.Arrange();
        var feed = CreateTempDirectory();
        SignedNuGetFixture.CopyToFeed(feed);
        var typedAuthor = string.Join(' ', Enumerable.Range(0, 32).Select(i => SignedNuGetFixture.AuthorFingerprint.Substring(i * 2, 2))).ToLowerInvariant();
        var registry = PackageSourceRegistry.Create(
            [
                Definition("Any", feed, requireSigned: true),
                Definition("Author", feed, requireSigned: true, typedAuthor),
                Definition("Other", feed, requireSigned: true, SignedNuGetFixture.UnknownFingerprint)
            ],
            CreateTempDirectory());

        // Act
        var acceptedAny = await AcquireAsync(registry, "Any");
        var acceptedAuthor = await AcquireAsync(registry, "Author");
        var rejected = await AcquireAsync(registry, "Other");

        // Assert: accepted means the package was installed and the provider went on to the
        // content check (the fixture is a library, not an agent package)
        Assert.IsNotType<PackageSourceAccessException>(acceptedAny);
        Assert.Contains("has no 'content' folder", acceptedAny.Message, StringComparison.Ordinal);
        Assert.IsNotType<PackageSourceAccessException>(acceptedAuthor);
        Assert.Contains("has no 'content' folder", acceptedAuthor.Message, StringComparison.Ordinal);
        var refusal = Assert.IsType<PackageSourceAccessException>(rejected);
        Assert.Equal(PackageSourceFailureKind.SignatureRejected, refusal.Kind);
        Assert.Contains("is not signed by the trusted publisher", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Lists the signed fixture package from a source and tries to acquire it.
    /// </summary>
    /// <param name="registry">The registry.</param>
    /// <param name="alias">The source alias.</param>
    /// <returns>The exception the acquisition ended with.</returns>
    private static async Task<InvalidOperationException> AcquireAsync(PackageSourceRegistry registry, string alias)
    {
        var provider = registry.Resolve(alias);
        var package = Assert.Single(await provider.ListVersionsAsync(SignedNuGetFixture.PackageId, CancellationToken.None));
        return await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => provider.AcquireAsync(package, CancellationToken.None));
    }

    /// <summary>
    ///     Creates a NuGet folder-source definition.
    /// </summary>
    /// <param name="alias">The source alias.</param>
    /// <param name="feed">The feed folder.</param>
    /// <param name="requireSigned">Whether the source requires signed packages.</param>
    /// <param name="fingerprint">The pinned publisher fingerprint, or <see langword="null"/>.</param>
    /// <returns>The definition.</returns>
    private static PackageSourceDefinition Definition(
        string alias, string feed, bool requireSigned, string? fingerprint = null) =>
        new()
        {
            Alias = alias,
            Type = PackageSourceType.NuGet,
            Location = feed,
            PackageIds = [],
            RequireSignedPackages = requireSigned,
            SignerFingerprint = fingerprint
        };

    /// <summary>
    ///     Creates a unique temporary directory removed when the test ends.
    /// </summary>
    /// <returns>The directory path.</returns>
    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_pm_subsystem_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        return path;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var path in _tempPaths.Where(Directory.Exists))
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup; leftover temp directories do not fail the test.
            }
        }
    }
}
