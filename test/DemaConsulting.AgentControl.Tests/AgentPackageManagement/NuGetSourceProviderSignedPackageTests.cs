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
///     Tests of the signature requirement of <see cref="NuGetSourceProvider"/> with a real signed
///     package (Newtonsoft.Json 13.0.1, see <see cref="SignedNuGetFixture"/>): it is accepted with
///     no pinned publisher and with its author's certificate pinned, and rejected - and not
///     installed - when the pinned publisher is someone else, whatever the user's own NuGet
///     configuration says about trusted signers.
/// </summary>
/// <remarks>
///     <para>
///     The fixture package is a library, not an agent package, so once its signature has been
///     accepted the provider stops at its next check, "the package has no <c>content</c> folder".
///     "Accepted" therefore means: the package was unpacked into the private cache and that
///     layout check - not a signature failure - is what the provider reports. The tests run on
///     every platform and are skipped only when nuget.org cannot be reached or the machine's trust
///     store lacks the roots the fixture needs (see <see cref="SignedNuGetFixture"/>). They share a
///     non-parallel collection because they set a process-wide environment variable.
///     </para>
/// </remarks>
[Collection(SignedFixtureCollection.Name)]
public sealed class NuGetSourceProviderSignedPackageTests : IDisposable
{
    /// <summary>
    ///     The temporary folders created by the running test, removed on disposal.
    /// </summary>
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     Test that a validly signed package is accepted when signatures are required and no
    ///     publisher is pinned: any valid signature that chains to a trusted root is enough.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_Acquire_SignedPackageAndNoPinnedSigner_SignatureIsAccepted()
    {
        // Arrange
        using var scope = SignedNuGetFixture.Arrange();
        var (provider, package) = await ArrangeAsync(fingerprint: null);

        // Act / Assert
        await AssertSignatureAcceptedAsync(provider, package);
    }

    /// <summary>
    ///     Test that the signed package is accepted when the pinned publisher is its author, however
    ///     the fingerprint was typed (lower case with colons here).
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_Acquire_SignedPackageAndMatchingAuthorPinned_SignatureIsAccepted()
    {
        // Arrange
        using var scope = SignedNuGetFixture.Arrange();
        var typed = string.Join(':', Enumerable.Range(0, 32).Select(i => SignedNuGetFixture.AuthorFingerprint.Substring(i * 2, 2))).ToLowerInvariant();
        var (provider, package) = await ArrangeAsync(fingerprint: typed);

        // Act / Assert
        await AssertSignatureAcceptedAsync(provider, package);
    }

    /// <summary>
    ///     Test that the signed package is rejected - and not installed - when the pinned publisher
    ///     is someone else, with the "not signed by the trusted publisher" message.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_Acquire_SignedPackageAndOtherSignerPinned_IsRejectedAndNotInstalled()
    {
        // Arrange
        using var scope = SignedNuGetFixture.Arrange();
        var (provider, package) = await ArrangeAsync(SignedNuGetFixture.UnknownFingerprint);

        // Act
        var ex = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => provider.AcquireAsync(package, CancellationToken.None));

        // Assert
        Assert.Equal(PackageSourceFailureKind.SignatureRejected, ex.Kind);
        Assert.Contains(
            $"Package '{SignedNuGetFixture.PackageId}' {SignedNuGetFixture.PackageVersion} from source 'Feed' is not signed by the trusted publisher",
            ex.Message,
            StringComparison.Ordinal);
        Assert.Contains("00000000…0000", ex.Message, StringComparison.Ordinal);
        Assert.Empty(InstalledPackages(provider));
    }

    /// <summary>
    ///     Test that a package whose content was altered after it was signed is rejected - and not
    ///     installed - as a signature failure, because its signature no longer matches its content.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_Acquire_SignedPackageWithAlteredContent_IsRejectedAndNotInstalled()
    {
        // Arrange: the signed package with one byte of its content flipped, and no pinned publisher
        using var scope = SignedNuGetFixture.Arrange();
        var feed = CreateTempDirectory();
        SignedNuGetFixture.CopyAlteredToFeed(feed);
        var provider = new NuGetSourceProvider(Definition(feed, fingerprint: null), CreateTempDirectory());
        var package = Assert.Single(await provider.ListVersionsAsync(SignedNuGetFixture.PackageId, CancellationToken.None));

        // Act
        var ex = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => provider.AcquireAsync(package, CancellationToken.None));

        // Assert
        Assert.Equal(PackageSourceFailureKind.SignatureRejected, ex.Kind);
        Assert.Empty(InstalledPackages(provider));
    }

    /// <summary>
    ///     Test that the pin names the package's author: pinning the nuget.org repository's
    ///     certificate (which only countersigns) does not match, so the package is rejected.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_Acquire_RepositoryCertificatePinned_IsNotTheAuthorAndIsRejected()
    {
        // Arrange
        using var scope = SignedNuGetFixture.Arrange();
        var (provider, package) = await ArrangeAsync(SignedNuGetFixture.RepositoryFingerprint);

        // Act
        var ex = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => provider.AcquireAsync(package, CancellationToken.None));

        // Assert
        Assert.Equal(PackageSourceFailureKind.SignatureRejected, ex.Kind);
    }

    /// <summary>
    ///     Test that trusted signers in the user's own NuGet configuration - found in a folder above
    ///     AgentControl's cache - cannot widen a pinned source: a mismatching pin is still rejected
    ///     although that configuration trusts the package's author.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_Acquire_UsersConfigurationTrustsTheAuthorButOtherSignerPinned_IsStillRejected()
    {
        // Arrange: a nuget.config above the per-source root that trusts the package's author
        using var scope = SignedNuGetFixture.Arrange();
        var rootBase = CreateTempDirectory();
        WriteUsersTrustedSigners(rootBase, SignedNuGetFixture.AuthorFingerprint);
        var (provider, package) = await ArrangeAsync(SignedNuGetFixture.UnknownFingerprint, rootBase);

        // Act
        var ex = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => provider.AcquireAsync(package, CancellationToken.None));

        // Assert
        Assert.Equal(PackageSourceFailureKind.SignatureRejected, ex.Kind);
    }

    /// <summary>
    ///     Test that trusted signers in the user's own NuGet configuration cannot narrow a source
    ///     that requires signatures without a pinned publisher: a configuration that trusts only an
    ///     unrelated certificate does not make the source reject a validly signed package.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_Acquire_UsersConfigurationTrustsOnlyAnotherSignerAndNoneIsPinned_SignatureIsAccepted()
    {
        // Arrange
        using var scope = SignedNuGetFixture.Arrange();
        var rootBase = CreateTempDirectory();
        WriteUsersTrustedSigners(rootBase, SignedNuGetFixture.UnknownFingerprint);
        var (provider, package) = await ArrangeAsync(fingerprint: null, rootBase);

        // Act / Assert
        await AssertSignatureAcceptedAsync(provider, package);
    }

    /// <summary>
    ///     Test that a package already in the private cache is checked against the pinned publisher
    ///     again: after the author's pin let it in, changing the pin to someone else rejects the
    ///     cached package.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_Acquire_PinnedSignerChangedAfterPackageWasCached_RejectsTheCachedPackage()
    {
        // Arrange: cached under the author's pin
        using var scope = SignedNuGetFixture.Arrange();
        var rootBase = CreateTempDirectory();
        var feed = CreateFeed();
        var authorPinned = new NuGetSourceProvider(Definition(feed, SignedNuGetFixture.AuthorFingerprint), rootBase);
        var package = Assert.Single(await authorPinned.ListVersionsAsync(SignedNuGetFixture.PackageId, CancellationToken.None));
        await AssertSignatureAcceptedAsync(authorPinned, package);

        // Act: the same source, now pinned to someone else
        var otherPinned = new NuGetSourceProvider(Definition(feed, SignedNuGetFixture.UnknownFingerprint), rootBase);
        var ex = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => otherPinned.AcquireAsync(package, CancellationToken.None));

        // Assert
        Assert.Equal(PackageSourceFailureKind.SignatureRejected, ex.Kind);
    }

    /// <summary>
    ///     Asserts that a package's signature was accepted: the package was unpacked into the
    ///     private cache, and the provider then stopped at the (expected) missing-content check
    ///     rather than at a signature failure.
    /// </summary>
    /// <param name="provider">The provider.</param>
    /// <param name="package">The package to acquire.</param>
    /// <returns>A task that completes when the assertions have passed.</returns>
    private static async Task AssertSignatureAcceptedAsync(NuGetSourceProvider provider, DiscoveredPackage package)
    {
        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => provider.AcquireAsync(package, CancellationToken.None));

        Assert.IsNotType<PackageSourceAccessException>(ex);
        Assert.Contains("has no 'content' folder", ex.Message, StringComparison.Ordinal);
        Assert.NotEmpty(InstalledPackages(provider));
    }

    /// <summary>
    ///     Finds the packages unpacked into a provider's private cache.
    /// </summary>
    /// <param name="provider">The provider.</param>
    /// <returns>The paths of the completion markers of installed packages.</returns>
    private static string[] InstalledPackages(NuGetSourceProvider provider)
    {
        var packages = Path.Combine(provider.RootFolder, "packages");
        return Directory.Exists(packages)
            ? Directory.GetFiles(packages, ".nupkg.metadata", SearchOption.AllDirectories)
            : [];
    }

    /// <summary>
    ///     Writes a <c>nuget.config</c> that trusts one author certificate, as a user's own
    ///     configuration might, into the folder above AgentControl's per-source roots so that NuGet's
    ///     configuration discovery finds it.
    /// </summary>
    /// <param name="folder">The cache base folder.</param>
    /// <param name="fingerprint">The trusted author's fingerprint.</param>
    private static void WriteUsersTrustedSigners(string folder, string fingerprint) =>
        File.WriteAllText(
            Path.Combine(folder, "nuget.config"),
            $"""
             <?xml version="1.0" encoding="utf-8"?>
             <configuration>
               <trustedSigners>
                 <author name="users-own-signer">
                   <certificate fingerprint="{fingerprint}" hashAlgorithm="SHA256" allowUntrustedRoot="false" />
                 </author>
               </trustedSigners>
             </configuration>
             """);

    /// <summary>
    ///     Creates a provider over a new folder feed holding the signed package, requiring
    ///     signatures, and lists the package's version.
    /// </summary>
    /// <param name="fingerprint">The pinned publisher fingerprint, or <see langword="null"/>.</param>
    /// <param name="rootBase">The cache base folder, or <see langword="null"/> for a new one.</param>
    /// <returns>The provider and the listed package.</returns>
    private async Task<(NuGetSourceProvider Provider, DiscoveredPackage Package)> ArrangeAsync(
        string? fingerprint, string? rootBase = null)
    {
        var provider = new NuGetSourceProvider(Definition(CreateFeed(), fingerprint), rootBase ?? CreateTempDirectory());
        var package = Assert.Single(await provider.ListVersionsAsync(SignedNuGetFixture.PackageId, CancellationToken.None));
        return (provider, package);
    }
    /// <summary>
    ///     Creates a folder feed holding the signed package.
    /// </summary>
    /// <returns>The feed folder.</returns>
    private string CreateFeed()
    {
        var feed = CreateTempDirectory();
        SignedNuGetFixture.CopyToFeed(feed);
        return feed;
    }

    /// <summary>
    ///     Creates a source definition that requires signatures.
    /// </summary>
    /// <param name="feed">The feed folder.</param>
    /// <param name="fingerprint">The pinned publisher fingerprint, or <see langword="null"/>.</param>
    /// <returns>The definition.</returns>
    private static PackageSourceDefinition Definition(string feed, string? fingerprint) =>
        new()
        {
            Alias = "Feed",
            Type = PackageSourceType.NuGet,
            Location = feed,
            RequireSignedPackages = true,
            SignerFingerprint = fingerprint
        };

    /// <summary>
    ///     Creates a unique temporary directory removed when the test ends.
    /// </summary>
    /// <returns>The directory path.</returns>
    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_signed_test_" + Guid.NewGuid().ToString("N"));
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
