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
    ///     Test that the explanation shown for an unsupported platform is the documented text.
    /// </summary>
    [Fact]
    public void SignatureVerificationSupport_UnsupportedMessage_IsTheDocumentedExplanation()
    {
        // Assert
        Assert.Equal("Package signature verification is not supported on this platform", SignatureVerificationSupport.UnsupportedMessage);
    }
}
