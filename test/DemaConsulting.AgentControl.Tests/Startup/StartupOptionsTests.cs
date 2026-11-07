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

using DemaConsulting.AgentControl.Startup;

namespace DemaConsulting.AgentControl.Tests.Startup;

/// <summary>
///     Unit tests for <see cref="StartupOptions"/>.
/// </summary>
public class StartupOptionsTests
{
    /// <summary>
    ///     Test that parsing an empty argument list produces an instance with all overrides null.
    /// </summary>
    [Fact]
    public void StartupOptions_Parse_NoArguments_AllOverridesNull()
    {
        // Act: parse an empty argument list
        var options = StartupOptions.Parse([]);

        // Assert: no overrides were supplied
        Assert.Null(options.ConfigDirectory);
        Assert.Null(options.GitExecutableOverride);
        Assert.Null(options.AgentToolCommandOverride);
    }

    /// <summary>
    ///     Test that parsing --config-dir captures the following value.
    /// </summary>
    [Fact]
    public void StartupOptions_Parse_ConfigDirArgument_CapturesValue()
    {
        // Arrange: an absolute config directory valid on every platform
        var configDir = Path.Combine(Path.GetTempPath(), "isolated", "config");

        // Act: parse a config-dir override
        var options = StartupOptions.Parse(["--config-dir", configDir]);

        // Assert: the config directory is captured
        Assert.Equal(configDir, options.ConfigDirectory);
    }

    /// <summary>
    ///     Test that a relative --config-dir is resolved to a full path at parse time, so nothing
    ///     downstream can interpret it against a different current directory.
    /// </summary>
    [Fact]
    public void StartupOptions_Parse_RelativeConfigDirArgument_IsResolvedToFullPath()
    {
        // Arrange: a relative config directory
        var relative = Path.Combine("relative", "config");

        // Act: parse the override
        var options = StartupOptions.Parse(["--config-dir", relative]);

        // Assert: the captured value is absolute and equals the launch-time resolution
        Assert.NotNull(options.ConfigDirectory);
        Assert.True(Path.IsPathFullyQualified(options.ConfigDirectory));
        Assert.Equal(Path.GetFullPath(relative), options.ConfigDirectory);
    }

    /// <summary>
    ///     Test that parsing --git-path captures the following value.
    /// </summary>
    [Fact]
    public void StartupOptions_Parse_GitPathArgument_CapturesValue()
    {
        // Act: parse a git-path override
        var options = StartupOptions.Parse(["--git-path", @"C:\stubs\git.bat"]);

        // Assert: the git executable override is captured
        Assert.Equal(@"C:\stubs\git.bat", options.GitExecutableOverride);
    }

    /// <summary>
    ///     Test that parsing --agent-tool-command captures the following value.
    /// </summary>
    [Fact]
    public void StartupOptions_Parse_AgentToolCommandArgument_CapturesValue()
    {
        // Act: parse an agent-tool-command override
        var options = StartupOptions.Parse(["--agent-tool-command", @"C:\stubs\arg-logger.exe"]);

        // Assert: the agent tool command override is captured
        Assert.Equal(@"C:\stubs\arg-logger.exe", options.AgentToolCommandOverride);
    }

    /// <summary>
    ///     Test that parsing all three supported options together captures all values.
    /// </summary>
    [Fact]
    public void StartupOptions_Parse_AllArguments_CapturesAllValues()
    {
        // Arrange: all three supported overrides
        var configDir = Path.Combine(Path.GetTempPath(), "isolated", "config");
        var args = new[]
        {
            "--config-dir", configDir,
            "--git-path", @"C:\stubs\git.bat",
            "--agent-tool-command", @"C:\stubs\arg-logger.exe"
        };

        // Act: parse the combined argument list
        var options = StartupOptions.Parse(args);

        // Assert: all values are captured independently
        Assert.Equal(configDir, options.ConfigDirectory);
        Assert.Equal(@"C:\stubs\git.bat", options.GitExecutableOverride);
        Assert.Equal(@"C:\stubs\arg-logger.exe", options.AgentToolCommandOverride);
    }

    /// <summary>
    ///     Test that an unsupported argument throws an ArgumentException.
    /// </summary>
    [Fact]
    public void StartupOptions_Parse_UnsupportedArgument_ThrowsArgumentException()
    {
        // Act / Assert: an unrecognized flag is rejected
        Assert.Throws<ArgumentException>(() => StartupOptions.Parse(["--unknown"]));
    }

    /// <summary>
    ///     Test that an option missing its required value throws an ArgumentException.
    /// </summary>
    [Fact]
    public void StartupOptions_Parse_MissingValue_ThrowsArgumentException()
    {
        // Act / Assert: --config-dir with no following value is rejected
        Assert.Throws<ArgumentException>(() => StartupOptions.Parse(["--config-dir"]));
    }

    /// <summary>
    ///     Test that passing a null argument array throws an ArgumentNullException.
    /// </summary>
    [Fact]
    public void StartupOptions_Parse_NullArguments_ThrowsArgumentNullException()
    {
        // Act / Assert: null args array is rejected
        Assert.Throws<ArgumentNullException>(() => StartupOptions.Parse(null!));
    }
}
