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

using System.Xml.Linq;
using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.PackageCredentials;

namespace DemaConsulting.AgentControl.Tests.AgentPackageManagement;

/// <summary>
///     Tests of the signature requirement of <see cref="NuGetSourceProvider"/> that need no network
///     and no signed package: unsigned packages are refused, the requirement is re-checked for a
///     package that is already cached, a source that requires signatures refuses to work where
///     verification is unsupported (it fails closed), and the generated per-source
///     <c>nuget.config</c> holds exactly the trusted-signer allow-list the source calls for.
/// </summary>
/// <remarks>
///     Every test runs against a NuGet folder feed created on the fly in a temporary folder, with
///     its own cache base folder and package IDs unique to the test, so tests are safe to run in
///     parallel and never touch the developer's real NuGet cache. The acceptance of a genuinely
///     signed package is covered by <see cref="NuGetSourceProviderSignedPackageTests"/>.
/// </remarks>
public sealed class NuGetSourceProviderSignatureTests : IDisposable
{
    /// <summary>
    ///     A fingerprint in the forms users copy it: lower case with colons.
    /// </summary>
    private const string ColonSeparatedFingerprint =
        "ab:12:cd:34:ef:56:78:90:ab:12:cd:34:ef:56:78:90:ab:12:cd:34:ef:56:78:90:ab:12:cd:34:ef:56:78:90";

    /// <summary>
    ///     The same fingerprint normalized.
    /// </summary>
    private const string NormalizedFingerprint = "AB12CD34EF567890AB12CD34EF567890AB12CD34EF567890AB12CD34EF567890";

    /// <summary>
    ///     The temporary folders created by the running test, removed on disposal.
    /// </summary>
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     Test that an unsigned package is refused when the source requires signatures, with a
    ///     message that names the package, version and source, a "signature rejected" failure and a
    ///     sanitized inner exception, and that nothing is installed.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_Acquire_RequireSignedAndPackageUnsigned_IsRejectedAndNothingIsInstalled()
    {
        // Arrange
        var feed = CreateTempDirectory();
        var id = UniqueId();
        NuGetFeedBuilder.CreateAgentPackage(feed, id, "1.2.3");
        var rootBase = CreateTempDirectory();
        var provider = CreateProvider(feed, rootBase, requireSigned: true);
        var package = Assert.Single(await provider.ListVersionsAsync(id, CancellationToken.None));

        // Act
        var ex = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => provider.AcquireAsync(package, CancellationToken.None));

        // Assert
        Assert.Equal(PackageSourceFailureKind.SignatureRejected, ex.Kind);
        Assert.Contains($"Package '{id}' 1.2.3 from source 'Feed' is not validly signed", ex.Message, StringComparison.Ordinal);
        Assert.Contains("requires signed packages", ex.Message, StringComparison.Ordinal);
        Assert.IsType<SanitizedException>(ex.InnerException);
        Assert.Empty(FindInstalledPackages(provider));
    }

    /// <summary>
    ///     Test that, for a source pinned to a publisher, the message says the package is not signed
    ///     by the trusted publisher and names the start and end of the pinned certificate's
    ///     fingerprint, never the whole of it.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_Acquire_PinnedSignerAndPackageUnsigned_MessageNamesTheTrustedPublisher()
    {
        // Arrange
        var feed = CreateTempDirectory();
        var id = UniqueId();
        NuGetFeedBuilder.CreateAgentPackage(feed, id, "1.0.0");
        var provider = CreateProvider(feed, CreateTempDirectory(), requireSigned: true, fingerprint: ColonSeparatedFingerprint);
        var package = Assert.Single(await provider.ListVersionsAsync(id, CancellationToken.None));

        // Act
        var ex = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => provider.AcquireAsync(package, CancellationToken.None));

        // Assert
        Assert.Equal(PackageSourceFailureKind.SignatureRejected, ex.Kind);
        Assert.Contains($"Package '{id}' 1.0.0 from source 'Feed' is not signed by the trusted publisher", ex.Message, StringComparison.Ordinal);
        Assert.Contains("AB12CD34…7890", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(NormalizedFingerprint, ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that without the requirement an unsigned package is acquired as before (the
    ///     requirement is opt-in per source).
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_Acquire_SignaturesNotRequired_UnsignedPackageIsAccepted()
    {
        // Arrange
        var feed = CreateTempDirectory();
        var id = UniqueId();
        NuGetFeedBuilder.CreateAgentPackage(feed, id, "1.0.0", marker: "unsigned is fine");
        var provider = CreateProvider(feed, CreateTempDirectory(), requireSigned: false);
        var package = Assert.Single(await provider.ListVersionsAsync(id, CancellationToken.None));

        // Act
        using var reader = await provider.AcquireAsync(package, CancellationToken.None);

        // Assert
        Assert.Contains(".github/agents/copilot.md", reader.ListFiles());
    }

    /// <summary>
    ///     Test that turning the requirement on for a source whose package is already in the private
    ///     cache (acquired while signatures were not required) rejects the cached package instead of
    ///     trusting it.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_Acquire_RequirementEnabledAfterPackageWasCached_RejectsTheCachedPackage()
    {
        // Arrange: the package is cached while signatures are not required
        var feed = CreateTempDirectory();
        var id = UniqueId();
        NuGetFeedBuilder.CreateAgentPackage(feed, id, "1.0.0");
        var rootBase = CreateTempDirectory();
        var lenient = CreateProvider(feed, rootBase, requireSigned: false);
        var package = Assert.Single(await lenient.ListVersionsAsync(id, CancellationToken.None));
        using (await lenient.AcquireAsync(package, CancellationToken.None))
        {
            Assert.NotEmpty(FindInstalledPackages(lenient));
        }

        // Act: the same source (same alias, location and cache), now requiring signatures
        var strict = CreateProvider(feed, rootBase, requireSigned: true);
        var ex = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => strict.AcquireAsync(package, CancellationToken.None));

        // Assert
        Assert.Equal(PackageSourceFailureKind.SignatureRejected, ex.Kind);
        Assert.Contains("is not validly signed", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a source that requires signatures on a platform that cannot verify them refuses
    ///     to list or acquire anything - it fails closed - with a message that says why, and creates
    ///     nothing.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_RequireSignedOnUnsupportedPlatform_FailsClosedForListAndAcquire()
    {
        // Arrange: a perfectly good (unsigned) feed, and a platform that cannot verify signatures
        var feed = CreateTempDirectory();
        var id = UniqueId();
        NuGetFeedBuilder.CreateAgentPackage(feed, id, "1.0.0");
        var rootBase = CreateTempDirectory();
        var provider = CreateProvider(feed, rootBase, requireSigned: true, signaturesSupported: false);

        // Act
        var listing = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => provider.ListVersionsAsync(id, CancellationToken.None));
        var acquiring = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => provider.AcquireAsync(new DiscoveredPackage(id, ParseVersion("1.0.0"), "1.0.0", "Feed"), CancellationToken.None));

        // Assert
        Assert.All(new[] { listing, acquiring }, failure =>
        {
            Assert.Equal(PackageSourceFailureKind.SignatureRejected, failure.Kind);
            Assert.Contains("requires signed packages", failure.Message, StringComparison.Ordinal);
            Assert.Contains("not supported on this platform", failure.Message, StringComparison.Ordinal);
        });
        Assert.False(Directory.Exists(provider.RootFolder));
    }

    /// <summary>
    ///     Test that a platform whose capability probe cannot run (the probe throws) is treated as
    ///     unable to verify: a source that requires signatures refuses to list or acquire - it fails
    ///     closed - instead of throwing from the probe or skipping the verification.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_RequireSignedAndProbeFails_FailsClosedWithoutThrowingFromTheProbe()
    {
        // Arrange: support decided by a probe that fails the way a broken crypto stack does
        var feed = CreateTempDirectory();
        var id = UniqueId();
        NuGetFeedBuilder.CreateAgentPackage(feed, id, "1.0.0");
        var supported = SignatureVerificationSupport.Evaluate(
            isWindows: true, isLinux: false, isMacOS: false, () => throw new System.Security.Cryptography.CryptographicException("no chains"));
        var provider = CreateProvider(feed, CreateTempDirectory(), requireSigned: true, signaturesSupported: supported);

        // Act
        var listing = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => provider.ListVersionsAsync(id, CancellationToken.None));
        var acquiring = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => provider.AcquireAsync(new DiscoveredPackage(id, ParseVersion("1.0.0"), "1.0.0", "Feed"), CancellationToken.None));

        // Assert
        Assert.False(supported);
        Assert.Equal(PackageSourceFailureKind.SignatureRejected, listing.Kind);
        Assert.Equal(PackageSourceFailureKind.SignatureRejected, acquiring.Kind);
    }

    /// <summary>
    ///     Test that a platform that cannot verify signatures does not affect a source that does not
    ///     require them.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_SignaturesNotRequiredOnUnsupportedPlatform_WorksAsUsual()
    {
        // Arrange
        var feed = CreateTempDirectory();
        var id = UniqueId();
        NuGetFeedBuilder.CreateAgentPackage(feed, id, "1.0.0");
        var provider = CreateProvider(feed, CreateTempDirectory(), requireSigned: false, signaturesSupported: false);

        // Act
        var package = Assert.Single(await provider.ListVersionsAsync(id, CancellationToken.None));
        using var reader = await provider.AcquireAsync(package, CancellationToken.None);

        // Assert
        Assert.NotEmpty(reader.ListFiles());
    }

    /// <summary>
    ///     Test that a source pinned to a publisher gets a <c>nuget.config</c> whose
    ///     <c>trustedSigners</c> section clears every inherited signer and then lists exactly one
    ///     author entry with the normalized SHA-256 fingerprint and no trust for untrusted roots,
    ///     next to the private packages folder, and writes no signature mode and no sources.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_PinnedSigner_GeneratesTrustedSignersWithOnlyThatAuthor()
    {
        // Arrange
        var feed = CreateTempDirectory();
        var provider = CreateProvider(feed, CreateTempDirectory(), requireSigned: true, fingerprint: ColonSeparatedFingerprint);

        // Act: the first use prepares the root
        await provider.ListVersionsAsync(UniqueId(), CancellationToken.None);

        // Assert
        var configuration = XDocument.Load(Path.Combine(provider.RootFolder, "nuget.config")).Root!;
        var trustedSigners = Assert.Single(configuration.Elements("trustedSigners"));
        Assert.Equal(["clear", "author"], trustedSigners.Elements().Select(element => element.Name.LocalName));
        var author = trustedSigners.Element("author")!;
        Assert.Equal("AgentControl pinned signer", (string?)author.Attribute("name"));
        var certificate = Assert.Single(author.Elements("certificate"));
        Assert.Equal(NormalizedFingerprint, (string?)certificate.Attribute("fingerprint"));
        Assert.Equal("SHA256", (string?)certificate.Attribute("hashAlgorithm"));
        Assert.Equal("false", (string?)certificate.Attribute("allowUntrustedRoot"));
        Assert.Single(configuration.Element("config")!.Elements("add"));
        Assert.Equal("globalPackagesFolder", (string?)configuration.Element("config")!.Element("add")!.Attribute("key"));
        Assert.Null(configuration.Element("packageSources"));
    }

    /// <summary>
    ///     Test that a source that requires signatures without a pinned publisher gets a
    ///     <c>trustedSigners</c> section that only clears inherited signers (so a signer allow-list
    ///     in the user's own configuration cannot narrow what the source accepts), and that a source
    ///     that does not require signatures gets none (so a fingerprint left in the settings does
    ///     nothing).
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_RequireSignedWithoutPin_ClearsInheritedSignersAndNoRequirementWritesNone()
    {
        // Arrange
        var feed = CreateTempDirectory();
        var unpinned = CreateProvider(feed, CreateTempDirectory(), requireSigned: true);
        var notRequired = CreateProvider(feed, CreateTempDirectory(), requireSigned: false, fingerprint: NormalizedFingerprint);

        // Act
        await unpinned.ListVersionsAsync(UniqueId(), CancellationToken.None);
        await notRequired.ListVersionsAsync(UniqueId(), CancellationToken.None);

        // Assert
        var unpinnedSigners = XDocument.Load(Path.Combine(unpinned.RootFolder, "nuget.config")).Root!.Element("trustedSigners")!;
        Assert.Equal(["clear"], unpinnedSigners.Elements().Select(element => element.Name.LocalName));
        Assert.Null(XDocument.Load(Path.Combine(notRequired.RootFolder, "nuget.config")).Root!.Element("trustedSigners"));
    }

    /// <summary>
    ///     Test that changing the pinned publisher of an existing source rewrites the generated
    ///     configuration in the same private root, so the next acquisition (including of an already
    ///     cached package) is checked against the new publisher.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_PinnedSignerChanged_RewritesTheConfigurationInTheSameRoot()
    {
        // Arrange
        var feed = CreateTempDirectory();
        var rootBase = CreateTempDirectory();
        var first = CreateProvider(feed, rootBase, requireSigned: true, fingerprint: NormalizedFingerprint);
        await first.ListVersionsAsync(UniqueId(), CancellationToken.None);

        // Act
        var second = CreateProvider(feed, rootBase, requireSigned: true, fingerprint: SignedNuGetFixture.UnknownFingerprint);
        await second.ListVersionsAsync(UniqueId(), CancellationToken.None);

        // Assert
        Assert.Equal(first.RootFolder, second.RootFolder);
        var certificate = XDocument.Load(Path.Combine(second.RootFolder, "nuget.config"))
            .Descendants("certificate").Single();
        Assert.Equal(SignedNuGetFixture.UnknownFingerprint, (string?)certificate.Attribute("fingerprint"));
    }

    /// <summary>
    ///     Test that a fingerprint that is not a SHA-256 fingerprint stops a source that requires
    ///     signatures (it is never ignored, which would widen trust to any signer) with a message
    ///     that does not repeat the text, and is irrelevant for a source that does not.
    /// </summary>
    [Theory]
    [InlineData("not-a-fingerprint")]
    [InlineData("AB12")]
    [InlineData("ZZ:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99")]
    public void NuGetSourceProvider_Constructor_InvalidFingerprint_RefusesWhenSignaturesAreRequired(string fingerprint)
    {
        // Arrange
        var feed = CreateTempDirectory();

        // Act / Assert: refused when it matters
        var ex = Assert.Throws<InvalidOperationException>(
            () => CreateProvider(feed, CreateTempDirectory(), requireSigned: true, fingerprint: fingerprint));
        Assert.Contains("SHA-256 fingerprint", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(fingerprint, ex.Message, StringComparison.Ordinal);

        // Act / Assert: harmless when signatures are not required
        _ = CreateProvider(feed, CreateTempDirectory(), requireSigned: false, fingerprint: fingerprint);
    }

    /// <summary>
    ///     Creates a provider over a feed folder.
    /// </summary>
    /// <param name="feed">The feed folder.</param>
    /// <param name="rootBase">The cache base folder.</param>
    /// <param name="requireSigned">Whether the source requires signed packages.</param>
    /// <param name="fingerprint">The pinned publisher fingerprint text, or <see langword="null"/>.</param>
    /// <param name="signaturesSupported">Whether the platform is taken to support verification, or
    ///     <see langword="null"/> for the real answer.</param>
    /// <returns>The provider.</returns>
    private static NuGetSourceProvider CreateProvider(
        string feed, string rootBase, bool requireSigned, string? fingerprint = null, bool? signaturesSupported = null) =>
        new(
            new PackageSourceDefinition
            {
                Alias = "Feed",
                Type = PackageSourceType.NuGet,
                Location = feed,
                RequireSignedPackages = requireSigned,
                SignerFingerprint = fingerprint
            },
            rootBase,
            signatureVerificationSupported: signaturesSupported);

    /// <summary>
    ///     Finds the packages unpacked into a provider's private cache.
    /// </summary>
    /// <param name="provider">The provider.</param>
    /// <returns>The paths of the completion markers of installed packages.</returns>
    private static string[] FindInstalledPackages(NuGetSourceProvider provider)
    {
        var packages = Path.Combine(provider.RootFolder, "packages");
        return Directory.Exists(packages)
            ? Directory.GetFiles(packages, ".nupkg.metadata", SearchOption.AllDirectories)
            : [];
    }

    /// <summary>
    ///     Parses a version for test setup, failing loudly if invalid.
    /// </summary>
    /// <param name="version">The version text.</param>
    /// <returns>The parsed version.</returns>
    private static PackageVersion ParseVersion(string version) =>
        PackageVersion.TryParse(version, out var parsed) && parsed is not null
            ? parsed
            : throw new ArgumentException($"Invalid test version '{version}'.", nameof(version));

    /// <summary>
    ///     Generates a package ID unique to one test.
    /// </summary>
    /// <returns>A unique package ID.</returns>
    private static string UniqueId() => "agentcontrol.test." + Guid.NewGuid().ToString("N");

    /// <summary>
    ///     Creates a unique temporary directory removed when the test ends.
    /// </summary>
    /// <returns>The directory path.</returns>
    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_signature_test_" + Guid.NewGuid().ToString("N"));
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
