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
///     Unit tests for <see cref="PackageSourceRegistry"/>, using
///     <see cref="FakePackageSourceProvider"/> so every provider call can be counted.
/// </summary>
public class PackageSourceRegistryTests
{
    /// <summary>
    ///     Test that resolving an alias returns the provider of exactly that source, matching the
    ///     alias case-insensitively.
    /// </summary>
    [Fact]
    public void PackageSourceRegistry_Resolve_ConfiguredAlias_ReturnsThatSourcesProvider()
    {
        // Arrange: two configured sources
        var a = new FakePackageSourceProvider("A");
        var b = new FakePackageSourceProvider("B");
        var registry = CreateRegistry(a, b);

        // Act
        var resolvedA = registry.Resolve("A");
        var resolvedBLower = registry.Resolve("b");

        // Assert
        Assert.Same(a, resolvedA);
        Assert.Same(b, resolvedBLower);
        Assert.True(registry.Contains("a"));
        Assert.Equal(["A", "B"], registry.Definitions.Select(d => d.Alias));
    }

    /// <summary>
    ///     Test that resolving an alias that is not configured is an error naming the alias.
    /// </summary>
    [Fact]
    public void PackageSourceRegistry_Resolve_MissingAlias_ThrowsUnknownSourceExceptionNamingAlias()
    {
        // Arrange
        var registry = CreateRegistry(new FakePackageSourceProvider("A"));

        // Act
        var ex = Assert.Throws<UnknownSourceException>(() => registry.Resolve("Missing"));

        // Assert
        Assert.Equal("Missing", ex.Alias);
        Assert.Contains("'Missing'", ex.Message, StringComparison.Ordinal);
        Assert.False(registry.Contains("Missing"));
    }

    /// <summary>
    ///     Test that the only configured source is never used as a stand-in for a missing alias:
    ///     there is no fallback to a single, default, or first source.
    /// </summary>
    [Fact]
    public void PackageSourceRegistry_Resolve_MissingAliasWithSingleConfiguredSource_DoesNotFallBack()
    {
        // Arrange: exactly one source, named Default
        var onlySource = new FakePackageSourceProvider(PackageSourceDefinition.DefaultAlias, ("pkg", "1.0.0"));
        var registry = CreateRegistry(onlySource);

        // Act / Assert: an unknown alias fails instead of resolving to the only (or Default) source
        Assert.Throws<UnknownSourceException>(() => registry.Resolve("Other"));
        Assert.Equal(0, onlySource.TotalCalls);
    }

    /// <summary>
    ///     Test the dependency-confusion scenario: the same package ID exists at two sources with A
    ///     holding the higher version; a pin to source B resolves and lists only B, and source A is
    ///     never contacted.
    /// </summary>
    [Fact]
    public async Task PackageSourceRegistry_PinToSourceB_SamePackageIdAtBothSources_OnlyBIsContacted()
    {
        // Arrange: A has a higher version of the same package ID than B
        var a = new FakePackageSourceProvider("A", ("contoso-agents", "9.0.0"));
        var b = new FakePackageSourceProvider("B", ("contoso-agents", "1.0.0"));
        var registry = CreateRegistry(a, b);

        // Act: resolve the pinned source and list the pinned package
        var versions = await registry.Resolve("B").ListVersionsAsync("contoso-agents", CancellationToken.None);

        // Assert: only B's version came back, and A was never contacted
        Assert.Equal("1.0.0", Assert.Single(versions).Version.ToString());
        Assert.Equal("B", versions[0].SourceAlias);
        Assert.Equal(1, b.ListVersionsCalls);
        Assert.Equal(0, a.TotalCalls);
    }

    /// <summary>
    ///     Test that a pin to a source whose alias was removed is an error and no other source is
    ///     contacted.
    /// </summary>
    [Fact]
    public void PackageSourceRegistry_PinToRemovedSource_ThrowsAndContactsNoOtherSource()
    {
        // Arrange: the pinned source "B" was removed; only A remains
        var a = new FakePackageSourceProvider("A", ("contoso-agents", "9.0.0"));
        var registry = CreateRegistry(a);

        // Act / Assert
        Assert.Throws<UnknownSourceException>(() => registry.Resolve("B"));
        Assert.Equal(0, a.TotalCalls);
    }

    /// <summary>
    ///     Test that the provider for an alias is created once, lazily, on first use, and reused.
    /// </summary>
    [Fact]
    public void PackageSourceRegistry_Resolve_RepeatedCalls_CreatesProviderOnceAndLazily()
    {
        // Arrange: a factory that counts creations
        var created = 0;
        var provider = new FakePackageSourceProvider("A");
        var registry = new PackageSourceRegistry(
            [new PackageSourceDefinition { Alias = "A", Type = PackageSourceType.Zip, Location = "x" }],
            _ =>
            {
                created++;
                return provider;
            });
        Assert.Equal(0, created);

        // Act
        registry.Resolve("A");
        registry.Resolve("a");

        // Assert
        Assert.Equal(1, created);
    }

    /// <summary>
    ///     Test that a duplicated alias (compared case-insensitively) or a blank alias is rejected
    ///     when the registry is built.
    /// </summary>
    [Fact]
    public void PackageSourceRegistry_Constructor_DuplicateOrBlankAlias_ThrowsInvalidOperationException()
    {
        // Arrange
        PackageSourceDefinition Source(string alias) =>
            new() { Alias = alias, Type = PackageSourceType.Zip, Location = "x" };

        // Act / Assert
        Assert.Throws<InvalidOperationException>(
            () => new PackageSourceRegistry([Source("A"), Source("a")], _ => new FakePackageSourceProvider("A")));
        Assert.Throws<InvalidOperationException>(
            () => new PackageSourceRegistry([Source(" ")], _ => new FakePackageSourceProvider("A")));
    }

    /// <summary>
    ///     Test that the default factory builds the right provider kind for each source type and
    ///     that an empty registry has no sources.
    /// </summary>
    [Fact]
    public void PackageSourceRegistry_Create_ZipAndNuGetDefinitions_ResolvesMatchingProviderKinds()
    {
        // Arrange
        var registry = PackageSourceRegistry.Create(
        [
            new PackageSourceDefinition { Alias = "Zips", Type = PackageSourceType.Zip, Location = Path.GetTempPath() },
            new PackageSourceDefinition { Alias = "Feed", Type = PackageSourceType.NuGet, Location = Path.GetTempPath() }
        ], Path.Combine(Path.GetTempPath(), "agentcontrol_registry_test_nuget"));

        // Act / Assert
        Assert.IsType<ZipFolderSourceProvider>(registry.Resolve("Zips"));
        Assert.IsType<NuGetSourceProvider>(registry.Resolve("Feed"));
        Assert.Empty(PackageSourceRegistry.Create([]).Definitions);
    }

    /// <summary>
    ///     Test that the default factory builds the Git provider for a valid Git source, passes the
    ///     options on, and fails closed (at resolve time, not at construction of the registry) for a
    ///     Git source whose settings are not valid.
    /// </summary>
    [Fact]
    public void PackageSourceRegistry_Create_GitSource_ResolvesGitProviderAndFailsClosedWhenInvalid()
    {
        // Arrange
        var registry = PackageSourceRegistry.Create(
            [
                new PackageSourceDefinition
                {
                    Alias = "Repo",
                    Type = PackageSourceType.Git,
                    Location = "https://example.invalid/org/repo.git",
                    TagPattern = "v{version}",
                    PackageIds = ["contoso-agents"]
                },
                new PackageSourceDefinition
                {
                    Alias = "Bad",
                    Type = PackageSourceType.Git,
                    Location = "ext::sh -c calc",
                    TagPattern = "v{version}",
                    PackageIds = ["contoso-agents"]
                }
            ],
            git: new GitSourceOptions(() => "git", Path.Combine(Path.GetTempPath(), "agentcontrol_registry_test_git")));

        // Act / Assert
        var provider = Assert.IsType<GitSourceProvider>(registry.Resolve("Repo"));
        Assert.Equal("Repo", provider.Alias);
        Assert.Throws<InvalidOperationException>(() => registry.Resolve("Bad"));
        Assert.Throws<UnknownSourceException>(() => registry.Resolve("Missing"));
    }

    /// <summary>
    ///     Test that resolving a NuGet server source through the default factory yields a NuGet
    ///     provider for a valid https address, and fails with a clear error for plain http that
    ///     was not explicitly allowed.
    /// </summary>
    [Fact]
    public void PackageSourceRegistry_Create_NuGetServerSource_ResolvesHttpsAndRefusesPlainHttpWithoutOptIn()
    {
        // Arrange
        var registry = PackageSourceRegistry.Create(
        [
            new PackageSourceDefinition
            {
                Alias = "Secure",
                Type = PackageSourceType.NuGet,
                Location = "https://feeds.example.com/v3/index.json"
            },
            new PackageSourceDefinition
            {
                Alias = "Plain",
                Type = PackageSourceType.NuGet,
                Location = "http://feeds.example.com/v3/index.json"
            }
        ]);

        // Act / Assert
        Assert.IsType<NuGetSourceProvider>(registry.Resolve("Secure"));
        Assert.Throws<InvalidOperationException>(() => registry.Resolve("Plain"));
    }

    /// <summary>
    ///     Test that blank or null aliases passed to the lookup members are handled: Contains is
    ///     false, Resolve rejects them.
    /// </summary>
    [Fact]
    public void PackageSourceRegistry_BlankAlias_ContainsFalseAndResolveThrowsArgumentException()
    {
        // Arrange
        var registry = CreateRegistry(new FakePackageSourceProvider("A"));

        // Act / Assert
        Assert.False(registry.Contains(null));
        Assert.False(registry.Contains(" "));
        Assert.Throws<ArgumentException>(() => registry.Resolve(" "));
        Assert.Throws<ArgumentNullException>(() => registry.Resolve(null!));
    }

    /// <summary>
    ///     Test that the registry is a genuine snapshot: changing the caller's definition objects
    ///     or the list they came in after the registry was built changes neither which aliases
    ///     exist nor the definition a provider is created from.
    /// </summary>
    [Fact]
    public void PackageSourceRegistry_Constructor_DefinitionsMutatedAfterwards_RegistryIsASnapshot()
    {
        // Arrange
        var original = new PackageSourceDefinition { Alias = "A", Type = PackageSourceType.Zip, Location = "first" };
        var list = new List<PackageSourceDefinition> { original };
        PackageSourceDefinition? seen = null;
        var registry = new PackageSourceRegistry(
            list,
            definition =>
            {
                seen = definition;
                return new FakePackageSourceProvider(definition.Alias);
            });

        // Act: the caller changes its objects and its list after the registry was built
        original.Location = "changed";
        original.Alias = "Z";
        original.PackageIds.Add("late-package");
        list.Add(new PackageSourceDefinition { Alias = "B", Type = PackageSourceType.Zip, Location = "added" });
        registry.Resolve("A");

        // Assert: what the registry resolves and builds from is what it was given
        Assert.NotNull(seen);
        Assert.Equal("A", seen.Alias);
        Assert.Equal("first", seen.Location);
        Assert.Empty(seen.PackageIds);
        Assert.True(registry.Contains("A"));
        Assert.False(registry.Contains("Z"));
        Assert.False(registry.Contains("B"));
        Assert.Single(registry.Definitions);
    }

    /// <summary>
    ///     Test that the definitions the registry reports are copies: changing them, in the list or
    ///     in the objects, cannot change what the registry reports or resolves later.
    /// </summary>
    [Fact]
    public void PackageSourceRegistry_Definitions_CallerMutatesReturnedDefinitions_RegistryIsUnaffected()
    {
        // Arrange
        PackageSourceDefinition? seen = null;
        var registry = new PackageSourceRegistry(
            [new PackageSourceDefinition { Alias = "A", Type = PackageSourceType.Zip, Location = "first" }],
            definition =>
            {
                seen = definition;
                return new FakePackageSourceProvider(definition.Alias);
            });

        // Act: the caller changes what it was handed
        var handedOut = registry.Definitions;
        handedOut[0].Location = "hacked";
        handedOut[0].PackageIds.Add("injected");

        // Assert: every access is a new copy, and the registry's own data is untouched
        Assert.NotSame(handedOut[0], registry.Definitions[0]);
        Assert.Equal("first", registry.Definitions[0].Location);
        Assert.Empty(registry.Definitions[0].PackageIds);
        registry.Resolve("A");
        Assert.Equal("first", seen!.Location);
    }

    /// <summary>
    ///     Test that a null entry in the definitions (for example from a hand-edited settings file)
    ///     is reported as a configuration problem rather than an incidental null reference.
    /// </summary>
    [Fact]
    public void PackageSourceRegistry_Constructor_NullDefinitionEntry_ThrowsInvalidOperationException()
    {
        // Act
        var ex = Assert.Throws<InvalidOperationException>(
            () => new PackageSourceRegistry(
                [new PackageSourceDefinition { Alias = "A", Type = PackageSourceType.Zip, Location = "x" }, null!],
                _ => new FakePackageSourceProvider("A")));

        // Assert
        Assert.Contains("missing", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Creates a registry that serves the given fake providers by their aliases.
    /// </summary>
    private static PackageSourceRegistry CreateRegistry(params FakePackageSourceProvider[] providers) =>
        new(
            providers.Select(p => new PackageSourceDefinition { Alias = p.Alias, Type = PackageSourceType.Zip, Location = "unused" }),
            definition => providers.Single(p => p.Alias == definition.Alias));
}
