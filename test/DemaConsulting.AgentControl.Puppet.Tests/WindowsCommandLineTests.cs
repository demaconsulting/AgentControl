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

namespace DemaConsulting.AgentControl.Puppet.Tests;

/// <summary>
///     Tests for the Windows command-line splitter used to validate an instance's <c>--config-dir</c>.
/// </summary>
public class WindowsCommandLineTests
{
    /// <summary>
    ///     Validates the argument splitting rules, including the special rule of the program name.
    /// </summary>
    /// <param name="line">The command line.</param>
    /// <param name="expected">The arguments, separated by <c>|</c>.</param>
    [Theory]
    [InlineData("app.exe a b", "app.exe|a|b")]
    [InlineData("  app.exe   a \t b  ", "app.exe|a|b")]
    [InlineData("\"C:\\Program Files\\app.exe\" a", "C:\\Program Files\\app.exe|a")]
    [InlineData("\"C:\\dir\\\" a", "C:\\dir\\|a")]
    [InlineData("app.exe \"a b\" c", "app.exe|a b|c")]
    [InlineData("app.exe a\"b c\"d", "app.exe|ab cd")]
    [InlineData("app.exe \"C:\\dir with space\\\\\"", "app.exe|C:\\dir with space\\")]
    [InlineData("app.exe \"say \\\"hi\\\"\"", "app.exe|say \"hi\"")]
    [InlineData("app.exe C:\\dir\\file.txt", "app.exe|C:\\dir\\file.txt")]
    [InlineData("app.exe \\\\server\\share", "app.exe|\\\\server\\share")]
    [InlineData("app.exe \"a \"\"quoted\"\" b\"", "app.exe|a \"quoted\" b")]
    [InlineData("app.exe \"\" x", "app.exe||x")]
    public void WindowsCommandLine_Split_FollowsCommandLineToArgvWRules(string line, string expected)
    {
        // Arrange / Act
        var arguments = WindowsCommandLine.Split(line);

        // Assert
        Assert.Equal(expected.Split('|'), arguments);
    }

    /// <summary>
    ///     Validates that a blank command line has no arguments.
    /// </summary>
    /// <param name="line">The command line.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WindowsCommandLine_Split_Blank_IsEmpty(string line)
    {
        // Arrange / Act
        var arguments = WindowsCommandLine.Split(line);

        // Assert
        Assert.Empty(arguments);
    }

    /// <summary>
    ///     Validates that what <see cref="LaunchOptions.JoinArguments"/> writes is read back to the same arguments.
    /// </summary>
    [Fact]
    public void WindowsCommandLine_Split_RoundTripsLaunchArguments()
    {
        // Arrange
        string[] arguments = ["--config-dir", "C:\\my dir\\cfg\\", "--agent-tool-command", "echo \"hi there\"", "--git-path", "C:\\git\\git.exe"];

        // Act
        var parsed = WindowsCommandLine.Split("\"C:\\app\\x.exe\" " + LaunchOptions.JoinArguments(arguments));

        // Assert
        Assert.Equal(arguments.Prepend("C:\\app\\x.exe"), parsed);
    }

    /// <summary>
    ///     Validates the path comparison: absolute, no trailing separator, ignoring case, invalid paths never equal.
    /// </summary>
    /// <param name="left">The first path.</param>
    /// <param name="right">The second path.</param>
    /// <param name="expected">Whether they are the same folder.</param>
    [Theory]
    [InlineData("C:\\cfg\\one", "c:\\CFG\\One\\", true)]
    [InlineData("C:\\cfg\\one\\..\\one", "C:\\cfg\\one", true)]
    [InlineData("C:\\cfg\\one", "C:\\cfg\\one\\other", false)]
    [InlineData("C:\\cfg\\one", "C:\\cfg\\one2", false)]
    [InlineData("C:\\cfg\\one", "C:\\cfg\\o|ne", false)]
    public void InstanceRegistry_SamePath_ComparesNormalizedPaths(string left, string right, bool expected)
    {
        // Arrange / Act
        var same = InstanceRegistry.SamePath(left, right);

        // Assert
        Assert.Equal(expected, same);
    }
}
