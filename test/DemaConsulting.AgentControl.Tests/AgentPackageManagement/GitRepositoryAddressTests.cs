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
    ///     Test that ssh addresses in URL form (with or without a user name and port, and an IPv6
    ///     host) are accepted and report the ssh transport.
    /// </summary>
    [Theory]
    [InlineData("ssh://git@github.com/org/repo.git")]
    [InlineData("ssh://host.example:2222/org/repo")]
    [InlineData("ssh://git@[::1]:22/org/repo")]
    public void GitRepositoryAddress_TryParse_SshUrl_IsAccepted(string text)
    {
        // Arrange: an ssh URL

        // Act
        var accepted = GitRepositoryAddress.TryParse(text, out var address, out var problem);

        // Assert
        Assert.True(accepted, problem);
        Assert.NotNull(address);
        Assert.Equal(GitAddressKind.Ssh, address.Kind);
        Assert.Equal("ssh", address.GitAllowProtocolName);
    }

    /// <summary>
    ///     Test that the scp-like <c>user@host:path</c> form is accepted and reports the ssh
    ///     transport.
    /// </summary>
    [Theory]
    [InlineData("git@github.com:org/repo.git")]
    [InlineData("deploy.user@host-1.example:srv/git/repo")]
    public void GitRepositoryAddress_TryParse_ScpLikeSsh_IsAccepted(string text)
    {
        // Arrange: an scp-like address

        // Act
        var accepted = GitRepositoryAddress.TryParse(text, out var address, out var problem);

        // Assert
        Assert.True(accepted, problem);
        Assert.NotNull(address);
        Assert.Equal(GitAddressKind.Ssh, address.Kind);
        Assert.Equal("ssh", address.GitAllowProtocolName);
        Assert.Equal(text, address.Value);
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
    [InlineData("https://host")]
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
        // Arrange: the complete text as it would be shown back to the user
        var input = text?.Trim() ?? string.Empty;

        // Act
        var accepted = GitRepositoryAddress.TryParse(text, out var address, out var problem);

        // Assert: refused with a message, which is the same one GetProblem reports
        Assert.False(accepted);
        Assert.Null(address);
        Assert.False(string.IsNullOrWhiteSpace(problem));
        Assert.Equal(problem, GitRepositoryAddress.GetProblem(text));

        // Assert: the message never contains the complete input (inputs of four characters or fewer
        // are skipped because they can appear in the fixed wording by chance, for example "repo")
        if (input.Length > 4)
        {
            Assert.DoesNotContain(input, problem, StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     Test that an address longer than the limit is refused.
    /// </summary>
    [Fact]
    public void GitRepositoryAddress_TryParse_TooLong_IsRefused()
    {
        // Arrange: an address far over the limit
        var text = "https://host.example/" + new string('a', 2100);

        // Act
        var problem = GitRepositoryAddress.GetProblem(text);

        // Assert: refused, without repeating the text
        Assert.NotNull(problem);
        Assert.DoesNotContain(text, problem, StringComparison.Ordinal);
    }

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

        // Assert: refused, and neither the secret nor the complete input is repeated
        Assert.False(accepted);
        Assert.NotNull(problem);
        Assert.DoesNotContain(Sentinel, problem, StringComparison.Ordinal);
        Assert.DoesNotContain(text, problem, StringComparison.Ordinal);
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
    ///     Test that an address that embeds a credential - user information on an https address, a
    ///     password in an ssh address, a query string or a fragment - is refused.
    /// </summary>
    [Theory]
    [InlineData("https://user:pw@host.example/org/repo.git")]
    [InlineData("https://token@host.example/org/repo.git")]
    [InlineData("ssh://user:pw@host.example/org/repo.git")]
    [InlineData("https://host.example/org/repo.git?token=abc")]
    [InlineData("https://host.example/org/repo.git#frag")]
    public void GitRepositoryAddress_TryParse_EmbeddedCredential_IsRefused(string text)
    {
        // Arrange: an address that carries a credential

        // Act
        var accepted = GitRepositoryAddress.TryParse(text, out var address, out var problem);

        // Assert: refused with a message, and no address is produced
        Assert.False(accepted);
        Assert.Null(address);
        Assert.False(string.IsNullOrWhiteSpace(problem));
        Assert.True(GitRepositoryAddress.EmbedsCredential(text));
    }

    /// <summary>
    ///     Test that an address that starts with a hyphen, or whose host, user name or path starts
    ///     with one, is refused, because git or ssh would read it as an option.
    /// </summary>
    [Theory]
    [InlineData("-oProxyCommand=calc")]
    [InlineData("--upload-pack=calc")]
    [InlineData("-u@host:path")]
    [InlineData("ssh://-oProxyCommand=calc/x")]
    [InlineData("https://-host.example/x")]
    [InlineData("ssh://-user@host.example/x")]
    [InlineData("git@host:-oProxyCommand=calc")]
    public void GitRepositoryAddress_TryParse_OptionLikeValue_IsRefused(string text)
    {
        // Arrange: a value that begins with a hyphen somewhere it could be read as an option

        // Act
        var accepted = GitRepositoryAddress.TryParse(text, out var address, out var problem);

        // Assert
        Assert.False(accepted);
        Assert.Null(address);
        Assert.False(string.IsNullOrWhiteSpace(problem));
        Assert.Equal(problem, GitRepositoryAddress.GetProblem(text));
    }

    /// <summary>
    ///     Test that an address with a control character, a line break or white space where it is not
    ///     allowed is refused.
    /// </summary>
    [Theory]
    [InlineData("https://host.example/x\nfoo")]
    [InlineData("https://host.example/x\0")]
    [InlineData("https://host.example/x\tfoo")]
    [InlineData("https://ho st.example/x")]
    [InlineData("ssh://us er@host.example/x")]
    [InlineData("git@host.example:org/re po.git")]
    public void GitRepositoryAddress_TryParse_ControlCharactersOrWhiteSpace_IsRefused(string text)
    {
        // Arrange: an address with a character that must never reach a command line

        // Act
        var accepted = GitRepositoryAddress.TryParse(text, out var address, out var problem);

        // Assert
        Assert.False(accepted);
        Assert.Null(address);
        Assert.False(string.IsNullOrWhiteSpace(problem));
        Assert.DoesNotContain(text, problem, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a host, port or path outside the accepted forms is refused.
    /// </summary>
    [Theory]
    [InlineData("https://host.example")]
    [InlineData("https://host")]
    [InlineData("https://host.example/")]
    [InlineData("https://host.example:0/x")]
    [InlineData("https://host.example:99999/x")]
    [InlineData("https://host.example:/x")]
    [InlineData("https://host.example:abc/x")]
    [InlineData("https://host.example/a\\b")]
    [InlineData("https://[::1/x")]
    [InlineData("git@host://x")]
    public void GitRepositoryAddress_TryParse_MalformedHostPortOrPath_IsRefused(string text)
    {
        // Arrange: an address whose host, port or path is not in an accepted form

        // Act
        var accepted = GitRepositoryAddress.TryParse(text, out var address, out var problem);

        // Assert
        Assert.False(accepted);
        Assert.Null(address);
        Assert.False(string.IsNullOrWhiteSpace(problem));
        Assert.Contains("address", problem, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that the cache key is 16 lower-case hexadecimal digits.
    /// </summary>
    [Theory]
    [InlineData("https://github.com/org/repo.git")]
    [InlineData("git@host.example:org/repo.git")]
    [InlineData("ssh://git@host.example:2222/org/repo")]
    public void GitRepositoryAddress_CacheKey_IsSixteenLowerCaseHexDigits(string text)
    {
        // Arrange
        Assert.True(GitRepositoryAddress.TryParse(text, out var address, out var problem), problem);

        // Act
        var key = address!.CacheKey;

        // Assert
        Assert.Matches("^[0-9a-f]{16}$", key);
    }

    /// <summary>
    ///     Test that the cache key is the same for addresses that differ only in the case of the scheme
    ///     or host, and for the same address spelled with surrounding white space.
    /// </summary>
    [Fact]
    public void GitRepositoryAddress_CacheKey_SchemeAndHostCaseDoNotChangeIt()
    {
        // Arrange
        GitRepositoryAddress Parse(string text)
        {
            Assert.True(GitRepositoryAddress.TryParse(text, out var address, out var problem), problem);
            return address!;
        }

        // Act
        var a = Parse("https://Host.Example/org/repo.git");
        var sameScheme = Parse("HTTPS://host.example/org/repo.git");
        var padded = Parse("  https://host.example/org/repo.git  ");

        // Assert
        Assert.Equal(a.CacheKey, sameScheme.CacheKey);
        Assert.Equal(a.CacheKey, padded.CacheKey);
    }

    /// <summary>
    ///     Test that different repositories - another path, another letter case in the path, another
    ///     kind of address - have different cache keys, so two repositories never share a folder.
    /// </summary>
    [Fact]
    public void GitRepositoryAddress_CacheKey_DifferentRepositoriesHaveDifferentKeys()
    {
        // Arrange
        GitRepositoryAddress Parse(string text)
        {
            Assert.True(GitRepositoryAddress.TryParse(text, out var address, out var problem), problem);
            return address!;
        }

        // Act
        var a = Parse("https://host.example/org/repo.git");
        var otherPath = Parse("https://host.example/org/other.git");
        var otherCase = Parse("https://host.example/Org/Repo.git");
        var ssh = Parse("git@host.example:org/repo.git");

        // Assert
        Assert.NotEqual(a.CacheKey, otherPath.CacheKey);
        Assert.NotEqual(a.CacheKey, otherCase.CacheKey);
        Assert.NotEqual(a.CacheKey, ssh.CacheKey);
    }
}
