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

namespace DemaConsulting.AgentControl.Tests;

/// <summary>
///     Unit tests for the Program class.
/// </summary>
/// <remarks>
///     <see cref="Program.Main"/> is only exercised here for the argument-parsing error path:
///     any successful parse hands off to Avalonia's <c>StartWithClassicDesktopLifetime</c>,
///     which blocks for the lifetime of the application and requires a real UI backend, so it
///     must never be invoked from a headless unit test. <see cref="Program.BuildAvaloniaApp"/>
///     is exercised directly instead, since configuring (but not starting) an
///     <see cref="Avalonia.AppBuilder"/> is safe to do headlessly.
/// </remarks>
[Collection("Sequential")]
public class ProgramTests
{
    /// <summary>
    ///     Test that Version returns a non-empty string.
    /// </summary>
    [Fact]
    public void Program_Version_ReturnsNonEmptyString()
    {
        // Act: read the version property
        var version = Program.Version;

        // Assert: version is a non-empty string
        Assert.False(string.IsNullOrWhiteSpace(version));
    }

    /// <summary>
    ///     Test that BuildAvaloniaApp returns a configured, non-null AppBuilder without starting
    ///     any UI lifetime.
    /// </summary>
    [Fact]
    public void Program_BuildAvaloniaApp_ReturnsConfiguredAppBuilder()
    {
        // Act: configure (but do not start) the Avalonia application builder
        var builder = Program.BuildAvaloniaApp();

        // Assert: a builder was returned, targeting the App class
        Assert.NotNull(builder);
        Assert.Equal(typeof(App), builder.ApplicationType);
    }

    /// <summary>
    ///     Test that Main with an unsupported argument writes an error and returns a non-zero
    ///     exit code without attempting to start the Avalonia UI.
    /// </summary>
    [Fact]
    public void Program_Main_WithInvalidArgument_ReturnsNonZeroExitCode()
    {
        // Arrange: redirect stderr to suppress error output during the test
        var originalError = Console.Error;
        try
        {
            using var errWriter = new StringWriter();
            Console.SetError(errWriter);

            // Act: invoke Main with an unsupported argument
            var result = Program.Main(["--not-a-real-option"]);

            // Assert: unsupported arguments produce a non-zero exit code and an error message
            Assert.Equal(1, result);
            Assert.Contains("Error", errWriter.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }
}
