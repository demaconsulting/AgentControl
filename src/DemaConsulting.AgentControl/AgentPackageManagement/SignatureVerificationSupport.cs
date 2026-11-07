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

using System.Security.Cryptography.X509Certificates;

namespace DemaConsulting.AgentControl.AgentPackageManagement;

/// <summary>
///     Tells whether the current platform can verify NuGet package signatures, so the option to
///     require signed packages is only offered where it can be honored.
/// </summary>
/// <remarks>
///     <para>
///     Requiring signatures is only meaningful if verification really happens. The NuGet caching
///     library verifies signatures explicitly on Windows, Linux and macOS (NuGet's own
///     verification is skipped on Linux and macOS unless an environment variable opts in, which is
///     why the library does not rely on it), and AgentControl's continuous-integration matrix runs
///     on exactly those three platforms. Anywhere else the answer is "not supported", as it is when
///     a run-time probe of the platform's certificate-chain facility fails.
///     </para>
///     <para>
///     The answer is used in two places that must agree. The source editor disables its "Require
///     signed packages" checkbox, with an explanation, where verification is unsupported; and the
///     NuGet source provider refuses to acquire packages for a source that requires signatures
///     when verification is unsupported (a hand-edited settings file can enable the option
///     regardless of the editor) - it fails closed rather than silently skipping verification.
///     Stateless and thread-safe.
///     </para>
/// </remarks>
internal static class SignatureVerificationSupport
{
    /// <summary>
    ///     The explanation shown, as the checkbox tooltip and in the refusal message, where
    ///     signature verification is not supported.
    /// </summary>
    public const string UnsupportedMessage = "Package signature verification is not supported on this platform";

    /// <summary>
    ///     The run-time probe result, computed once on first use.
    /// </summary>
    private static readonly Lazy<bool> ProbeResult = new(() => RunProbe(ProbeCertificateChains));

    /// <summary>
    ///     Gets a value indicating whether signature verification is supported on the current
    ///     platform: the operating system is Windows, Linux or macOS and the platform's
    ///     certificate-chain facility is usable.
    /// </summary>
    public static bool IsSupported => Evaluate(
        OperatingSystem.IsWindows(), OperatingSystem.IsLinux(), OperatingSystem.IsMacOS(), () => ProbeResult.Value);

    /// <summary>
    ///     Decides support from the operating system and a capability probe: supported only when
    ///     the operating system is Windows, Linux or macOS and the probe ran and said so. The probe
    ///     is not run on any other operating system.
    /// </summary>
    /// <param name="isWindows">Whether the operating system is Windows.</param>
    /// <param name="isLinux">Whether the operating system is Linux.</param>
    /// <param name="isMacOS">Whether the operating system is macOS.</param>
    /// <param name="probe">The capability probe; a probe that fails in any way means "not
    ///     supported" (see <see cref="RunProbe"/>).</param>
    /// <returns><see langword="true"/> only when the platform is supported and the probe
    ///     succeeded.</returns>
    internal static bool Evaluate(bool isWindows, bool isLinux, bool isMacOS, Func<bool> probe) =>
        IsSupportedPlatform(isWindows, isLinux, isMacOS) && RunProbe(probe);

    /// <summary>
    ///     Decides support from the operating system alone.
    /// </summary>
    /// <param name="isWindows">Whether the operating system is Windows.</param>
    /// <param name="isLinux">Whether the operating system is Linux.</param>
    /// <param name="isMacOS">Whether the operating system is macOS.</param>
    /// <returns><see langword="true"/> for Windows, Linux and macOS; <see langword="false"/> for
    ///     every other operating system.</returns>
    internal static bool IsSupportedPlatform(bool isWindows, bool isLinux, bool isMacOS) =>
        isWindows || isLinux || isMacOS;

    /// <summary>
    ///     Runs a capability probe so that it can only ever answer "supported" by returning
    ///     <see langword="true"/>: a probe that returns <see langword="false"/> or fails in any
    ///     way (a cryptographic error, a native library that cannot be loaded, an unsupported
    ///     operation, anything else) means verification cannot be relied on, which is "not
    ///     supported" - the caller then fails closed.
    /// </summary>
    /// <param name="probe">The probe to run.</param>
    /// <returns><see langword="true"/> only when the probe ran and returned
    ///     <see langword="true"/>.</returns>
    internal static bool RunProbe(Func<bool> probe)
    {
        try
        {
            return probe();
        }
#pragma warning disable CA1031 // Any failure of the probe, whatever its type, means "not supported"
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
    }

    /// <summary>
    ///     Checks that the platform can build a certificate chain, which signature verification
    ///     needs.
    /// </summary>
    /// <returns><see langword="true"/> when a chain object can be created and used.</returns>
    private static bool ProbeCertificateChains()
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return chain.ChainPolicy is not null;
    }
}
