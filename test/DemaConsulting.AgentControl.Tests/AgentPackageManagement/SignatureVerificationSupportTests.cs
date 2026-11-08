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
using System.Security.Cryptography.X509Certificates;
using DemaConsulting.AgentControl.AgentPackageManagement;

namespace DemaConsulting.AgentControl.Tests.AgentPackageManagement;

/// <summary>
///     Unit tests for <see cref="SignatureVerificationSupport"/>, the capability check that decides
///     whether "Require signed packages" can be offered.
/// </summary>
public class SignatureVerificationSupportTests
{
    /// <summary>
    ///     Test that signature verification is supported on exactly the three platforms the NuGet
    ///     caching library verifies on and the continuous-integration matrix covers, and nowhere
    ///     else.
    /// </summary>
    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(false, false, true, true)]
    [InlineData(false, false, false, false)]
    public void SignatureVerificationSupport_IsSupportedPlatform_WindowsLinuxAndMacOsOnly(
        bool isWindows, bool isLinux, bool isMacOS, bool expected)
    {
        // Act
        var supported = SignatureVerificationSupport.IsSupportedPlatform(isWindows, isLinux, isMacOS);

        // Assert
        Assert.Equal(expected, supported);
    }

    /// <summary>
    ///     Test that the platform the tests run on (Windows, Linux or macOS in the CI matrix) is
    ///     reported as supported, so the option is offered where the signature tests actually run.
    /// </summary>
    [Fact]
    public void SignatureVerificationSupport_IsSupported_OnTheCiPlatforms_IsTrue()
    {
        // Arrange
        var onAMatrixPlatform = OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

        // Act / Assert
        Assert.Equal(onAMatrixPlatform, SignatureVerificationSupport.IsSupported);
    }

    /// <summary>
    ///     Test that a probe is only trusted when it ran and said "supported".
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void SignatureVerificationSupport_RunProbe_ProbeAnswers_ReturnsTheAnswer(bool answer, bool expected)
    {
        // Act
        var supported = SignatureVerificationSupport.RunProbe(() => answer);

        // Assert
        Assert.Equal(expected, supported);
    }

    /// <summary>
    ///     Test that a probe that fails in any way - a cryptographic error, a native library that
    ///     cannot be loaded, an unsupported operation or anything unforeseen - means "not
    ///     supported" (fail closed) and never throws to the caller.
    /// </summary>
    [Fact]
    public void SignatureVerificationSupport_RunProbe_ProbeFails_IsNotSupportedAndNeverThrows()
    {
        // Arrange: the kinds of failure a certificate facility can produce, and one nobody foresaw
        Exception[] failures =
        [
            new System.Security.Cryptography.CryptographicException("no crypto"),
            new DllNotFoundException("libssl"),
            new EntryPointNotFoundException("missing entry point"),
            new PlatformNotSupportedException("no chains"),
            new NotSupportedException("no chains"),
            new InvalidOperationException("no chains"),
            new TypeInitializationException("Chain", new InvalidProgramException())
        ];

        foreach (var failure in failures)
        {
            // Act
            var supported = SignatureVerificationSupport.RunProbe(() => throw failure);

            // Assert
            Assert.False(supported, failure.GetType().Name);
        }
    }

    /// <summary>
    ///     Test that support needs both a supported operating system and a probe that ran and said
    ///     so: a failing or negative probe means "not supported" without throwing, and the probe is
    ///     not even run on an unsupported operating system.
    /// </summary>
    [Fact]
    public void SignatureVerificationSupport_Evaluate_NeedsPlatformAndASuccessfulProbe()
    {
        // Arrange
        var probeRuns = 0;

        // Act
        var good = SignatureVerificationSupport.Evaluate(true, false, false, () => true);
        var negative = SignatureVerificationSupport.Evaluate(false, true, false, () => false);
        var failing = SignatureVerificationSupport.Evaluate(
            false, false, true, () => throw new System.Security.Cryptography.CryptographicException("no chains"));
        var wrongPlatform = SignatureVerificationSupport.Evaluate(false, false, false, () =>
        {
            probeRuns++;
            return true;
        });

        // Assert
        Assert.True(good);
        Assert.False(negative);
        Assert.False(failing);
        Assert.False(wrongPlatform);
        Assert.Equal(0, probeRuns);
    }

    /// <summary>
    ///     Test that the concrete probe - not a stand-in - really builds a certificate chain offline
    ///     on the platforms the signature tests run on, so the capability check is true because a
    ///     chain was built, not because an object could be created.
    /// </summary>
    [Fact]
    public void SignatureVerificationSupport_ProbeCertificateChains_OnThisPlatform_BuildsAChainOfflineAndIsSupported()
    {
        // Arrange
        var onAMatrixPlatform = OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

        // Act
        var probed = SignatureVerificationSupport.RunProbe(SignatureVerificationSupport.ProbeCertificateChains);

        // Assert
        Assert.Equal(onAMatrixPlatform, probed);
    }

    /// <summary>
    ///     Test that a chain build that does not come out clean - here a certificate that has
    ///     expired - means "not supported", so the probe fails closed on an unexpected chain status.
    /// </summary>
    [Fact]
    public void SignatureVerificationSupport_ProbeChain_ExpiredCertificate_IsNotSupported()
    {
        // Arrange: a self-signed certificate whose validity ended days ago
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=expired probe", key, HashAlgorithmName.SHA256);
        using var expired = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddDays(-5));

        // Act
        var supported = SignatureVerificationSupport.ProbeChain(expired);

        // Assert
        Assert.False(supported);
    }

    /// <summary>
    ///     Test that having no certificate to build a chain for is "not supported" rather than an
    ///     exception.
    /// </summary>
    [Fact]
    public void SignatureVerificationSupport_ProbeChain_NullCertificate_IsNotSupported()
    {
        // Act / Assert
        Assert.False(SignatureVerificationSupport.ProbeChain(null));
    }

    /// <summary>
    ///     Test that the explanation shown for an unsupported platform is the documented text.
    /// </summary>
    [Fact]
    public void SignatureVerificationSupport_UnsupportedMessage_IsTheDocumentedExplanation()
    {
        // Assert
        Assert.Equal("Package signature verification is not supported on this platform", SignatureVerificationSupport.UnsupportedMessage);
    }
}
