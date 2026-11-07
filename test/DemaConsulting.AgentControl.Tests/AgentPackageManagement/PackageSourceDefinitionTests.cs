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

using System.Reflection;
using System.Text.Json;
using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.RepoConfig;

namespace DemaConsulting.AgentControl.Tests.AgentPackageManagement;

/// <summary>
///     Unit tests for the package-source model types of the AgentPackageManagement subsystem:
///     <see cref="PackageSourceDefinition"/>, <see cref="PackageSourceType"/> and
///     <see cref="PackageReference"/>.
/// </summary>
public class PackageSourceDefinitionTests
{
    /// <summary>
    ///     Test that the location decides whether a source is a folder or a server: an absolute
    ///     http/https URI is a server; rooted, drive-letter and UNC paths are folders.
    /// </summary>
    [Theory]
    [InlineData("https://feeds.example.com/v3/index.json", true)]
    [InlineData("http://feeds.example.com/v3/index.json", true)]
    [InlineData(@"C:\feeds\agents", false)]
    [InlineData(@"\\server\share\feed", false)]
    [InlineData("/var/feeds/agents", false)]
    [InlineData("file:///C:/feeds/agents", false)]
    [InlineData("", false)]
    public void PackageSourceDefinition_LocationKind_DerivedFromLocation(string location, bool expectedServer)
    {
        // Arrange
        var definition = new PackageSourceDefinition { Alias = "S", Type = PackageSourceType.NuGet, Location = location };

        // Act / Assert
        Assert.Equal(
            expectedServer ? PackageSourceLocationKind.Server : PackageSourceLocationKind.Folder, definition.LocationKind);
    }

    /// <summary>
    ///     Test that a location embedding user information, or a server URL carrying a query
    ///     string or fragment, is rejected, and that the message never repeats the secret.
    /// </summary>
    /// <param name="location">A credential-bearing location.</param>
    [Theory]
    [InlineData("https://user:SentinelSecretValue@feeds.example.com/v3/index.json")]
    [InlineData("https://SentinelSecretValue@feeds.example.com/v3/index.json")]
    [InlineData("http://user:SentinelSecretValue@feeds.example.com/nuget")]
    [InlineData("https://feeds.example.com/v3/index.json?token=SentinelSecretValue")]
    [InlineData("https://feeds.example.com/v3/index.json#SentinelSecretValue")]
    [InlineData("ftp://user:SentinelSecretValue@host/feed")]
    public void PackageSourceDefinition_GetCredentialProblem_CredentialBearingLocation_ReturnsMessageWithoutSecret(
        string location)
    {
        // Arrange
        var definition = new PackageSourceDefinition { Alias = "Feed", Type = PackageSourceType.NuGet, Location = location };

        // Act
        var problem = definition.GetCredentialProblem();

        // Assert
        Assert.NotNull(problem);
        Assert.Contains("Feed", problem, StringComparison.Ordinal);
        Assert.DoesNotContain("SentinelSecretValue", problem, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that ordinary locations - folders, UNC shares and plain server URLs - are accepted.
    /// </summary>
    /// <param name="location">An acceptable location.</param>
    [Theory]
    [InlineData(@"C:\feeds\agents")]
    [InlineData(@"\\server\share\feed")]
    [InlineData("/var/feeds/agents")]
    [InlineData("https://feeds.example.com/v3/index.json")]
    [InlineData("")]
    public void PackageSourceDefinition_GetCredentialProblem_OrdinaryLocation_ReturnsNull(string location)
    {
        // Arrange
        var definition = new PackageSourceDefinition { Alias = "Feed", Type = PackageSourceType.NuGet, Location = location };

        // Act / Assert
        Assert.Null(definition.GetCredentialProblem());
    }

    /// <summary>
    ///     Test that new definitions default to the safe values: no insecure http, no signature
    ///     requirement, no signer pin, no package IDs.
    /// </summary>
    [Fact]
    public void PackageSourceDefinition_Defaults_AreSafeAndEmpty()
    {
        // Act
        var definition = new PackageSourceDefinition();

        // Assert
        Assert.False(definition.AllowInsecureHttp);
        Assert.False(definition.RequireSignedPackages);
        Assert.Null(definition.SignerFingerprint);
        Assert.Empty(definition.PackageIds);
        Assert.Equal(string.Empty, definition.Alias);
    }

    /// <summary>
    ///     Test that no member of the persisted model can hold a secret: no property name looks
    ///     like a password, token, key, credential or secret, on the definition or the reference.
    /// </summary>
    [Fact]
    public void PackageSourceDefinition_Members_HaveNoSecretLikeProperty()
    {
        // Arrange
        string[] secretMarkers = ["password", "passwd", "secret", "token", "credential", "key"];
        var names = typeof(PackageSourceDefinition)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Concat(typeof(PackageReference).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Select(p => p.Name)
            .ToList();

        // Act / Assert: the model is non-trivial, and no name contains a secret marker
        Assert.Contains("Alias", names);
        Assert.All(
            names,
            name => Assert.DoesNotContain(
                secretMarkers, marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    ///     Test that the source type is persisted by name and the derived location kind is never
    ///     serialized.
    /// </summary>
    [Fact]
    public void PackageSourceDefinition_Serialize_TypeAsNameAndNoDerivedLocationKind()
    {
        // Arrange
        var definition = new PackageSourceDefinition
        {
            Alias = "Feed",
            Type = PackageSourceType.NuGet,
            Location = @"\\server\feed",
            PackageIds = ["a"]
        };

        // Act
        var json = JsonSerializer.Serialize(definition);
        var roundTripped = JsonSerializer.Deserialize<PackageSourceDefinition>(json);

        // Assert
        Assert.Contains("\"Type\":\"NuGet\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("LocationKind", json, StringComparison.Ordinal);
        Assert.NotNull(roundTripped);
        Assert.Equal(PackageSourceType.NuGet, roundTripped.Type);
        Assert.Equal(["a"], roundTripped.PackageIds);
    }

    /// <summary>
    ///     Test the alias rules: letters/digits first, then letters, digits, space, dot, underscore
    ///     or hyphen; 1-64 characters; no trailing space or dot.
    /// </summary>
    [Theory]
    [InlineData("Default", true)]
    [InlineData("Team A", true)]
    [InlineData("a.b-c_d", true)]
    [InlineData("1", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData(" lead", false)]
    [InlineData("trail ", false)]
    [InlineData("trail.", false)]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    [InlineData("a:b", false)]
    [InlineData("-dash", false)]
    public void PackageSourceDefinition_IsValidAlias_AppliesAliasRules(string? alias, bool expected)
    {
        // Act / Assert
        Assert.Equal(expected, PackageSourceDefinition.IsValidAlias(alias));
    }

    /// <summary>
    ///     Test that an alias of exactly 64 characters is valid and 65 is not.
    /// </summary>
    [Fact]
    public void PackageSourceDefinition_IsValidAlias_LengthBoundary()
    {
        // Act / Assert
        Assert.True(PackageSourceDefinition.IsValidAlias(new string('a', 64)));
        Assert.False(PackageSourceDefinition.IsValidAlias(new string('a', 65)));
    }

    /// <summary>
    ///     Test that CreateDefaultZip produces the source a legacy single folder migrates to.
    /// </summary>
    [Fact]
    public void PackageSourceDefinition_CreateDefaultZip_ProducesDefaultAliasZipSource()
    {
        // Act
        var definition = PackageSourceDefinition.CreateDefaultZip(@"D:\packages");

        // Assert
        Assert.Equal("Default", definition.Alias);
        Assert.Equal(PackageSourceType.Zip, definition.Type);
        Assert.Equal(@"D:\packages", definition.Location);
        Assert.Equal(RepoPin.LegacySourceAlias, definition.Alias);
    }

    /// <summary>
    ///     Test that Clone makes an independent deep copy (changing the copy's package IDs leaves
    ///     the original alone) and the copy is equivalent until changed.
    /// </summary>
    [Fact]
    public void PackageSourceDefinition_Clone_IsDeepAndEquivalentUntilChanged()
    {
        // Arrange
        var original = new PackageSourceDefinition
        {
            Alias = "Feed",
            Type = PackageSourceType.NuGet,
            Location = "https://x/index.json",
            PackageIds = ["a"],
            AllowInsecureHttp = true,
            RequireSignedPackages = true,
            SignerFingerprint = "FF"
        };

        // Act
        var copy = original.Clone();
        var equivalentBefore = original.IsEquivalentTo(copy);
        copy.PackageIds.Add("b");

        // Assert
        Assert.True(equivalentBefore);
        Assert.Equal(["a"], original.PackageIds);
        Assert.False(original.IsEquivalentTo(copy));
        Assert.True(copy.AllowInsecureHttp);
        Assert.Equal("FF", copy.SignerFingerprint);
    }

    /// <summary>
    ///     Test that IsEquivalentTo notices a change in each field that affects behavior, and
    ///     compares aliases case-insensitively.
    /// </summary>
    [Fact]
    public void PackageSourceDefinition_IsEquivalentTo_DetectsEveryBehavioralDifference()
    {
        // Arrange
        var baseline = PackageSourceDefinition.CreateDefaultZip(@"C:\a");

        // Act / Assert
        Assert.True(baseline.IsEquivalentTo(new PackageSourceDefinition { Alias = "DEFAULT", Type = PackageSourceType.Zip, Location = @"C:\a" }));
        Assert.False(baseline.IsEquivalentTo(new PackageSourceDefinition { Alias = "Default", Type = PackageSourceType.NuGet, Location = @"C:\a" }));
        Assert.False(baseline.IsEquivalentTo(new PackageSourceDefinition { Alias = "Default", Type = PackageSourceType.Zip, Location = @"C:\b" }));
        Assert.False(baseline.IsEquivalentTo(new PackageSourceDefinition { Alias = "Other", Type = PackageSourceType.Zip, Location = @"C:\a" }));
        Assert.False(baseline.IsEquivalentTo(new PackageSourceDefinition { Alias = "Default", Type = PackageSourceType.Zip, Location = @"C:\a", RequireSignedPackages = true }));
        Assert.False(baseline.IsEquivalentTo(new PackageSourceDefinition { Alias = "Default", Type = PackageSourceType.Zip, Location = @"C:\a", SignerFingerprint = "x" }));
        Assert.False(baseline.IsEquivalentTo(new PackageSourceDefinition { Alias = "Default", Type = PackageSourceType.Zip, Location = @"C:\a", AllowInsecureHttp = true }));
    }

    /// <summary>
    ///     Test that a certificate fingerprint is accepted as users copy it - any letter case,
    ///     separated by colons, spaces or hyphens - and normalized to 64 upper-case hexadecimal
    ///     characters.
    /// </summary>
    [Theory]
    [InlineData("A3AF7AF11EBB8EF729D2D91548509717E7E0FF55A129ABC3AEAA8A6940267641")]
    [InlineData("a3af7af11ebb8ef729d2d91548509717e7e0ff55a129abc3aeaa8a6940267641")]
    [InlineData("A3:AF:7A:F1:1E:BB:8E:F7:29:D2:D9:15:48:50:97:17:E7:E0:FF:55:A1:29:AB:C3:AE:AA:8A:69:40:26:76:41")]
    [InlineData("  a3 af 7a f1 1e bb 8e f7 29 d2 d9 15 48 50 97 17 e7 e0 ff 55 a1 29 ab c3 ae aa 8a 69 40 26 76 41  ")]
    [InlineData("A3-AF-7A-F1-1E-BB-8E-F7-29-D2-D9-15-48-50-97-17-E7-E0-FF-55-A1-29-AB-C3-AE-AA-8A-69-40-26-76-41")]
    public void PackageSourceDefinition_TryNormalizeFingerprint_ValidSpellings_NormalizeToUpperCaseHex(string text)
    {
        // Act
        var ok = PackageSourceDefinition.TryNormalizeFingerprint(text, out var normalized);

        // Assert
        Assert.True(ok);
        Assert.Equal("A3AF7AF11EBB8EF729D2D91548509717E7E0FF55A129ABC3AEAA8A6940267641", normalized);
    }

    /// <summary>
    ///     Test that text that is not exactly 64 hexadecimal digits is refused.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A3AF")]
    [InlineData("A3AF7AF11EBB8EF729D2D91548509717E7E0FF55A129ABC3AEAA8A69402676410")]
    [InlineData("G000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("000000000000000000000000000000000000000000000000000000000000000.")]
    public void PackageSourceDefinition_TryNormalizeFingerprint_InvalidText_IsRefused(string? text)
    {
        // Act
        var ok = PackageSourceDefinition.TryNormalizeFingerprint(text, out var normalized);

        // Assert
        Assert.False(ok);
        Assert.Equal(string.Empty, normalized);
    }

    /// <summary>
    ///     Test that the signature settings are only a problem when they cannot be applied: an
    ///     unreadable fingerprint on a NuGet source that requires signatures. Zip sources, sources
    ///     that do not require signatures and empty fingerprints are fine, and the message never
    ///     repeats the fingerprint text.
    /// </summary>
    [Fact]
    public void PackageSourceDefinition_GetSignaturePolicyProblem_OnlyAnUnreadableFingerprintOfARequiringNuGetSource()
    {
        // Arrange
        var invalid = "not-a-fingerprint-4419";
        PackageSourceDefinition Make(PackageSourceType type, bool require, string? fingerprint) =>
            new() { Alias = "S", Type = type, Location = @"C:\a", RequireSignedPackages = require, SignerFingerprint = fingerprint };

        // Act
        var problem = Make(PackageSourceType.NuGet, true, invalid).GetSignaturePolicyProblem();

        // Assert
        Assert.NotNull(problem);
        Assert.Contains("SHA-256 fingerprint", problem, StringComparison.Ordinal);
        Assert.DoesNotContain(invalid, problem, StringComparison.Ordinal);
        Assert.Null(Make(PackageSourceType.NuGet, true, "A3:AF:7A:F1:1E:BB:8E:F7:29:D2:D9:15:48:50:97:17:E7:E0:FF:55:A1:29:AB:C3:AE:AA:8A:69:40:26:76:41").GetSignaturePolicyProblem());
        Assert.Null(Make(PackageSourceType.NuGet, true, null).GetSignaturePolicyProblem());
        Assert.Null(Make(PackageSourceType.NuGet, true, "  ").GetSignaturePolicyProblem());
        Assert.Null(Make(PackageSourceType.NuGet, false, invalid).GetSignaturePolicyProblem());
        Assert.Null(Make(PackageSourceType.Zip, true, invalid).GetSignaturePolicyProblem());
    }

    /// <summary>
    ///     Test that a package reference carries the alias, ID and version text, and renders
    ///     without any location.
    /// </summary>
    [Fact]
    public void PackageReference_ToString_ShowsPackageVersionAndSourceAliasOnly()
    {
        // Arrange
        var reference = new PackageReference("Feed", "contoso-agents", "1.2.3+build");

        // Act / Assert
        Assert.Equal("contoso-agents 1.2.3+build (source 'Feed')", reference.ToString());
        Assert.Equal(new PackageReference("Feed", "contoso-agents", "1.2.3+build"), reference);
        Assert.NotEqual(new PackageReference("Other", "contoso-agents", "1.2.3+build"), reference);
    }
}
