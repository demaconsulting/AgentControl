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

using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace DemaConsulting.AgentControl.Tests.AgentPackageManagement;

/// <summary>
///     Non-parallel collection for tests that use the real signed fixture package and so set the
///     process-wide <c>NUGET_CERT_REVOCATION_MODE</c> environment variable.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public static class SignedFixtureCollection
{
    /// <summary>
    ///     The collection name.
    /// </summary>
    public const string Name = "SignedFixture";
}

/// <summary>
///     Provides a real, unmodified, signed NuGet package (Newtonsoft.Json 13.0.1, signed by its
///     author and countersigned by the nuget.org repository) for the signature tests, following
///     the precedent of the DemaConsulting.NuGet.Caching library's own signature tests.
/// </summary>
/// <remarks>
///     <para>
///     The package is downloaded from nuget.org once per test run, verified against a pinned
///     SHA-256 (a mismatch fails the tests, because it would mean the package was tampered with)
///     and kept in a temporary folder that is removed when the run ends; no binary is checked in.
///     Tests that need it are skipped - and only then - when the download fails (offline) or when
///     the operating system cannot build a trusted certificate chain for every certificate the
///     package's signatures use (a Linux distribution that dropped the legacy DigiCert roots, for
///     example). The tests run on every platform; nothing here is Windows-specific.
///     </para>
///     <para>
///     While a test runs, <c>NUGET_CERT_REVOCATION_MODE</c> is <c>offline</c>, so verification never
///     depends on the network reachability of certificate revocation services; this is why the
///     tests that use it share a non-parallel collection (<see cref="SignedFixtureCollection"/>).
///     </para>
/// </remarks>
internal static class SignedNuGetFixture
{
    /// <summary>
    ///     The identifier of the signed fixture package.
    /// </summary>
    public const string PackageId = "Newtonsoft.Json";

    /// <summary>
    ///     The version of the signed fixture package.
    /// </summary>
    public const string PackageVersion = "13.0.1";

    /// <summary>
    ///     The SHA-256 fingerprint of the fixture's author signing certificate.
    /// </summary>
    public const string AuthorFingerprint = "A3AF7AF11EBB8EF729D2D91548509717E7E0FF55A129ABC3AEAA8A6940267641";

    /// <summary>
    ///     The SHA-256 fingerprint of the fixture's nuget.org repository signing certificate.
    /// </summary>
    public const string RepositoryFingerprint = "5A2901D6ADA3D18260B9C6DFE2133C95D74B9EEF6AE0E5DC334C8454D1477DF4";

    /// <summary>
    ///     A SHA-256 fingerprint that matches no certificate.
    /// </summary>
    public const string UnknownFingerprint = "0000000000000000000000000000000000000000000000000000000000000000";

    /// <summary>
    ///     The nuget.org flat-container address of the signed package.
    /// </summary>
    private const string PackageUrl =
        "https://api.nuget.org/v3-flatcontainer/newtonsoft.json/13.0.1/newtonsoft.json.13.0.1.nupkg";

    /// <summary>
    ///     The pinned SHA-256 of the unmodified package.
    /// </summary>
    private const string PackageSha256 = "2B6B52556E27E1B7913F33EEDEB95568110C746BD64AFFF74357F1683878323A";

    /// <summary>
    ///     The package downloaded for this run (or why it could not be).
    /// </summary>
    private static readonly Lazy<(string? Path, string? SkipReason)> Download = new(DownloadPackage);

    /// <summary>
    ///     The result of the one-time check that the operating system trusts the package's
    ///     certificate chains.
    /// </summary>
    private static readonly Lazy<string?> TrustPreflight = new(CheckTrust);

    /// <summary>
    ///     Prepares a test that uses the signed package: skips it when the package cannot be
    ///     obtained or trusted here, and otherwise makes certificate revocation checks offline for
    ///     the lifetime of the returned scope.
    /// </summary>
    /// <returns>A scope that restores the previous environment value when disposed.</returns>
    public static IDisposable Arrange()
    {
        var reason = Download.Value.SkipReason ?? TrustPreflight.Value;
        if (reason is not null)
        {
            Assert.Skip(reason);
        }

        return new EnvironmentVariableScope("NUGET_CERT_REVOCATION_MODE", "offline");
    }

    /// <summary>
    ///     Copies the signed package into a folder feed under its standard file name.
    /// </summary>
    /// <param name="feedFolder">The existing feed folder.</param>
    /// <returns>The path of the copied package.</returns>
    public static string CopyToFeed(string feedFolder)
    {
        var target = Path.Combine(feedFolder, $"{PackageId.ToLowerInvariant()}.{PackageVersion}.nupkg");
        File.Copy(Download.Value.Path!, target, overwrite: true);
        return target;
    }

    /// <summary>
    ///     Downloads and verifies the signed package.
    /// </summary>
    /// <returns>The package path, or a skip reason when the download fails.</returns>
    private static (string? Path, string? SkipReason) DownloadPackage()
    {
        byte[] bytes;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            bytes = Task.Run(() => client.GetByteArrayAsync(PackageUrl)).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException)
        {
            return (null, $"The signed package {PackageId} {PackageVersion} could not be downloaded from nuget.org " +
                          $"(offline or network error): {ex.Message}");
        }

        var actual = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(actual, PackageSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The downloaded {PackageId} {PackageVersion} package has SHA-256 {actual} but {PackageSha256} " +
                "was expected; the package may have been tampered with.");
        }

        var folder = Path.Combine(Path.GetTempPath(), "agentcontrol_signed_fixture_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{PackageId.ToLowerInvariant()}.{PackageVersion}.nupkg");
        File.WriteAllBytes(path, bytes);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup of the temporary download folder
            }
        };
        return (path, null);
    }

    /// <summary>
    ///     Checks that every certificate used by the package's signatures (author, repository
    ///     countersignature and their timestamps) chains to a trusted root on this machine. The
    ///     certificates are long expired, so only the chain to a trusted root is checked, with
    ///     revocation and validity dates ignored.
    /// </summary>
    /// <returns>A skip reason, or <see langword="null"/> when trust can be established.</returns>
    private static string? CheckTrust()
    {
        const string timestampTokenOid = "1.2.840.113549.1.9.16.2.14";
        try
        {
            byte[] signature;
            using (var archive = System.IO.Compression.ZipFile.OpenRead(Download.Value.Path!))
            using (var entryStream = archive.GetEntry(".signature.p7s")!.Open())
            using (var buffer = new MemoryStream())
            {
                entryStream.CopyTo(buffer);
                signature = buffer.ToArray();
            }

            var cms = new SignedCms();
            cms.Decode(signature);
            var primary = cms.SignerInfos[0];
            var signers = new List<SignerInfo> { primary };
            signers.AddRange(primary.CounterSignerInfos.Cast<SignerInfo>());

            var certificates = new List<(bool IsTimestamp, X509Certificate2 Certificate, X509Certificate2Collection Extra)>();
            foreach (var signer in signers)
            {
                if (signer.Certificate is not null)
                {
                    certificates.Add((false, signer.Certificate, cms.Certificates));
                }

                foreach (var attribute in signer.UnsignedAttributes.Cast<CryptographicAttributeObject>()
                             .Where(a => a.Oid?.Value == timestampTokenOid))
                {
                    var timestamp = new SignedCms();
                    timestamp.Decode(attribute.Values[0].RawData);
                    var timestampCertificate = timestamp.SignerInfos[0].Certificate;
                    if (timestampCertificate is not null)
                    {
                        certificates.Add((true, timestampCertificate, timestamp.Certificates));
                    }
                }
            }

            // Every signing certificate must chain to a trusted root. Timestamp chains are held to a
            // weaker standard (at least one must be trusted), as NuGet accepts the package where the
            // legacy timestamp root is missing but a modern one is trusted
            string? failure = null;
            var trustedTimestamps = 0;
            var timestampCount = 0;
            foreach (var (isTimestamp, certificate, extra) in certificates)
            {
                using var chain = new X509Chain();
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.IgnoreNotTimeValid;
                chain.ChainPolicy.ExtraStore.AddRange(extra);
                chain.Build(certificate);
                var trusted = !chain.ChainStatus.Any(s => s.Status is X509ChainStatusFlags.UntrustedRoot or X509ChainStatusFlags.PartialChain);
                if (isTimestamp)
                {
                    timestampCount++;
                    trustedTimestamps += trusted ? 1 : 0;
                }
                else if (!trusted)
                {
                    failure ??= $"the signature certificate '{certificate.Subject}' does not chain to a trusted root";
                }
            }

            if (failure is null && timestampCount > 0 && trustedTimestamps == 0)
            {
                failure = "no timestamp certificate chains to a trusted root";
            }

            return failure is null
                ? null
                : $"The signed fixture package cannot be verified in this environment: {failure}. The fixture needs the " +
                  "DigiCert Assured ID and High Assurance EV root certificates in the operating system trust store.";
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or InvalidDataException or ArgumentOutOfRangeException)
        {
            return $"The signed fixture package signature could not be inspected for the trust preflight: {ex.Message}";
        }
    }

    /// <summary>
    ///     Temporarily sets a process environment variable.
    /// </summary>
    private sealed class EnvironmentVariableScope : IDisposable
    {
        /// <summary>
        ///     The variable name.
        /// </summary>
        private readonly string _name;

        /// <summary>
        ///     The value to restore.
        /// </summary>
        private readonly string? _previous;

        /// <summary>
        ///     Initializes the scope and sets the variable.
        /// </summary>
        /// <param name="name">The variable name.</param>
        /// <param name="value">The temporary value.</param>
        public EnvironmentVariableScope(string name, string value)
        {
            _name = name;
            _previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        /// <inheritdoc />
        public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
    }
}
