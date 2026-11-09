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
///     Tests of <see cref="GitRepositoryAddress"/>: the accepted forms, the refusal of dangerous
///     transports, option-like values and embedded credentials, messages that never echo the input,
///     and the stable cache key. Pure; no git is run.
/// </summary>
public sealed class GitRepositoryAddressTests
{
    /// <summary>
    ///     A secret planted in hostile inputs; it must never appear in a message.
    /// </summary>
    private const string Sentinel = "s3cr3t-sentinel";

    /// <summary>
    ///     Test that an https address without user information is accepted, with its host and
    ///     scheme case, an optional port, and no query.
    /// </summary>
    [Theory]
    [InlineData("https://github.com/org/repo.git")]
    [InlineData("https://host.example:8443/a/b/c")]
    [InlineData("HTTPS://Host.Example/org/repo")]
    [InlineData("  https://host.example/org/repo.git  ")]
    public void GitRepositoryAddress_TryParse_Https_IsAccepted(string text)
    {
        // Act
        var accepted = GitRepositoryAddress.TryParse(text, out var address, out var problem);

        // Assert
        Assert.True(accepted);
        Assert.Null(problem);
        Assert.NotNull(address);
        Assert.Equal(GitAddressKind.Https, address.Kind);
        Assert.Equal("https", address.GitAllowProtocolName);
        Assert.Equal(text.Trim(), address.Value);
    }

    /// <summary>
    ///     Test that ssh addresses are accepted in URL form (with or without a user name and port, and
    ///     an IPv6 host) and in <c>user@host:path</c> form.
    /// </summary>
    [Theory]
    [InlineData("ssh://git@github.com/org/repo.git")]
    [InlineData("ssh://host.example:2222/org/repo")]
    [InlineData("ssh://git@[::1]:22/org/repo")]
    [InlineData("git@github.com:org/repo.git")]
    [InlineData("deploy.user@host-1.example:srv/git/repo")]
    public void GitRepositoryAddress_TryParse_Ssh_IsAccepted(string text)
    {
        // Act
        var accepted = GitRepositoryAddress.TryParse(text, out var address, out var problem);

        // Assert
        Assert.True(accepted, problem);
        Assert.NotNull(address);
        Assert.Equal(GitAddressKind.Ssh, address.Kind);
        Assert.Equal("ssh", address.GitAllowProtocolName);
    }

    /// <summary>
    ///     Test that an absolute local path is accepted (and only checked for existence at use time),
    ///     including a UNC path on Windows.
    /// </summary>
    [Fact]
    public void GitRepositoryAddress_TryParse_AbsoluteLocalPath_IsAcceptedWithoutCheckingExistence()
    {
        // Arrange: a rooted path that does not exist
        var path = Path.Combine(Path.GetTempPath(), "no-such-folder-" + Guid.NewGuid().ToString("N"), "repo.git");

        // Act
        var accepted = GitRepositoryAddress.TryParse(path, out var address, out var problem);

        // Assert
        Assert.True(accepted, problem);
        Assert.NotNull(address);
        Assert.Equal(GitAddressKind.Local, address.Kind);
        Assert.Equal("file", address.GitAllowProtocolName);
        if (OperatingSystem.IsWindows())
        {
            Assert.True(GitRepositoryAddress.TryParse(@"\\server\share\repo.git", out var unc, out _));
            Assert.Equal(GitAddressKind.Local, unc!.Kind);
        }
    }

    /// <summary>
    ///     Test that dangerous or unsupported addresses are refused with a message: other schemes and
    ///     transports, remote-helper syntax, option-like values, relative paths, malformed hosts, ports
    ///     and paths, and control characters.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://host.example/org/repo")]
    [InlineData("git://host.example/org/repo")]
    [InlineData("file:///srv/git/repo")]
    [InlineData("ftp://host.example/repo")]
    [InlineData("ext::sh -c calc")]
    [InlineData("fd::17")]
    [InlineData("foo::bar")]
    [InlineData("https::host/x")]
    [InlineData("-oProxyCommand=calc")]
    [InlineData("--upload-pack=calc")]
    [InlineData("-u@host:path")]
    [InlineData("ssh://-oProxyCommand=calc/x")]
    [InlineData("https://-host.example/x")]
    [InlineData("git@host:-oProxyCommand=calc")]
    [InlineData("git@host://x")]
    [InlineData("host.example:org/repo")]
    [InlineData("relative/path/repo.git")]
    [InlineData(@"..\repo")]
    [InlineData("repo")]
    [InlineData("https://host.example")]
    [InlineData("https://host.example/")]
    [InlineData("https://ho st.example/x")]
    [InlineData("https://host.example:0/x")]
    [InlineData("https://host.example:99999/x")]
    [InlineData("https://host.example:/x")]
    [InlineData("https://host.example:abc/x")]
    [InlineData("https://host.example/a\\b")]
    [InlineData("ssh://us er@host.example/x")]
    [InlineData("ssh://-user@host.example/x")]
    [InlineData("https://host.example/x\nfoo")]
    [InlineData("https://host.example/x\0")]
    [InlineData("https://[::1/x")]
    public void GitRepositoryAddress_TryParse_DangerousOrUnsupported_IsRefused(string? text)
    {
        // Act
        var accepted = GitRepositoryAddress.TryParse(text, out var address, out var problem);

        // Assert
        Assert.False(accepted);
        Assert.Null(address);
        Assert.False(string.IsNullOrWhiteSpace(problem));
        Assert.Equal(problem, GitRepositoryAddress.GetProblem(text));
    }

    /// <summary>
    ///     Test that an address longer than the limit is refused.
    /// </summary>
    [Fact]
    public void GitRepositoryAddress_TryParse_TooLong_IsRefused() =>
        Assert.NotNull(GitRepositoryAddress.GetProblem("https://host.example/" + new string('a', 2100)));

    /// <summary>
    ///     Test that credentials embedded in an address are refused and that no message ever repeats
    ///     the text of the address, so a pasted secret cannot reach an error display or a log.
    /// </summary>
    [Theory]
    [InlineData("https://user:" + Sentinel + "@host.example/x")]
    [InlineData("https://tok-" + Sentinel + "@host.example/x")]
    [InlineData("ssh://user:" + Sentinel + "@host.example/x")]
    [InlineData("http://host.example/x?token=" + Sentinel)]
    [InlineData("https://host.example/x?token=" + Sentinel)]
    [InlineData("https://host.example/x#" + Sentinel)]
    [InlineData("ext::" + Sentinel)]
    [InlineData(Sentinel + "/relative/path")]
    [InlineData("--" + Sentinel)]
    public void GitRepositoryAddress_TryParse_Hostile_NeverEchoesInput(string text)
    {
        // Act
        var accepted = GitRepositoryAddress.TryParse(text, out _, out var problem);

        // Assert
        Assert.False(accepted);
        Assert.NotNull(problem);
        Assert.DoesNotContain(Sentinel, problem, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test the detection of credentials in an address text: user information on a non-ssh URL, a
    ///     password in an ssh URL, a query or fragment - but not a plain ssh user name.
    /// </summary>
    [Theory]
    [InlineData("https://user:pw@host.example/x", true)]
    [InlineData("https://token@host.example/x", true)]
    [InlineData("http://user:pw@host.example/x", true)]
    [InlineData("ssh://user:pw@host.example/x", true)]
    [InlineData("https://host.example/x?sig=abc", true)]
    [InlineData("https://host.example/x#frag", true)]
    [InlineData("ssh://git@host.example/x", false)]
    [InlineData("git@host.example:org/repo.git", false)]
    [InlineData("https://host.example/org/repo.git", false)]
    [InlineData("/srv/git/repo", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void GitRepositoryAddress_EmbedsCredential_DetectsSecretsOnlyInUrls(string? text, bool expected) =>
        Assert.Equal(expected, GitRepositoryAddress.EmbedsCredential(text));

    /// <summary>
    ///     Test that the cache key is 16 lower-case hex digits, is the same for addresses that differ
    ///     only in the case of the scheme or host, and differs for different repositories and kinds.
    /// </summary>
    [Fact]
    public void GitRepositoryAddress_CacheKey_IsStableAndDistinguishesRepositories()
    {
        // Arrange
        GitRepositoryAddress Parse(string text)
        {
            Assert.True(GitRepositoryAddress.TryParse(text, out var address, out var problem), problem);
            return address!;
        }

        var a = Parse("https://Host.Example/org/repo.git");
        var same = Parse("HTTPS://host.example/org/repo.git");
        var otherPath = Parse("https://host.example/org/other.git");
        var otherCase = Parse("https://host.example/Org/Repo.git");
        var ssh = Parse("git@host.example:org/repo.git");

        // Assert
        Assert.Matches("^[0-9a-f]{16}$", a.CacheKey);
        Assert.Equal(a.CacheKey, same.CacheKey);
        Assert.NotEqual(a.CacheKey, otherPath.CacheKey);
        Assert.NotEqual(a.CacheKey, otherCase.CacheKey);
        Assert.NotEqual(a.CacheKey, ssh.CacheKey);
    }
}
