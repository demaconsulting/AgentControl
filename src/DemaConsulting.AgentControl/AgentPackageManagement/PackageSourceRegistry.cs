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

using DemaConsulting.AgentControl.PackageCredentials;

namespace DemaConsulting.AgentControl.AgentPackageManagement;

/// <summary>
///     Raised when a package source alias is not configured, so a repo pinned to that source (or a
///     caller naming it) learns exactly which source is missing instead of silently being served
///     by a different one.
/// </summary>
internal sealed class UnknownSourceException : InvalidOperationException
{
    /// <summary>
    ///     Initializes the exception for a missing source alias.
    /// </summary>
    /// <param name="alias">The alias that is not configured.</param>
    public UnknownSourceException(string alias)
        : base($"Package source '{alias}' is not configured. Add a source named '{alias}' in Settings, " +
               "or select a package from a configured source.")
    {
        Alias = alias;
    }

    /// <summary>
    ///     Gets the alias that is not configured.
    /// </summary>
    public string Alias { get; }
}

/// <summary>
///     Maps a source alias to the provider that serves it, built from the configured source
///     definitions.
/// </summary>
/// <remarks>
///     <para>
///     The registry is the single place a source is looked up by alias, and it is the guarantee
///     behind pin integrity: <see cref="Resolve"/> returns the provider of exactly the named
///     source, and an alias that is not configured is an error (<see cref="UnknownSourceException"/>)
///     - it never falls back to another source, to a default, or to "the only source". A package ID
///     is not unique across sources, so resolving by anything weaker than the alias could install
///     another source's package under a pin that names a different one.
///     </para>
///     <para>
///     Aliases compare case-insensitively and must be unique; a duplicate (for example from a
///     hand-edited settings file) is rejected at construction rather than resolved ambiguously.
///     Providers are created lazily on first use and then reused, so a misconfigured source only
///     fails when it is actually used. An instance is a snapshot of the definitions it was built
///     from; callers build a new one when settings change. Thread-safe.
///     </para>
/// </remarks>
internal sealed class PackageSourceRegistry
{
    /// <summary>
    ///     The configured definitions by alias (case-insensitive), in configuration order via
    ///     <see cref="_ordered"/>.
    /// </summary>
    private readonly Dictionary<string, PackageSourceDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     The definitions in the order they were configured.
    /// </summary>
    private readonly List<PackageSourceDefinition> _ordered = [];

    /// <summary>
    ///     The providers created so far by alias (case-insensitive).
    /// </summary>
    private readonly Dictionary<string, IPackageSourceProvider> _providers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Creates the provider for a definition on first use.
    /// </summary>
    private readonly Func<PackageSourceDefinition, IPackageSourceProvider> _providerFactory;

    /// <summary>
    ///     Initializes a registry over source definitions with a custom provider factory (used by
    ///     tests to substitute fake providers).
    /// </summary>
    /// <param name="definitions">The configured sources.</param>
    /// <param name="providerFactory">Creates the provider that serves a definition; called at most
    ///     once per alias, on first <see cref="Resolve"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when either argument is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a definition has a blank alias or
    ///     two definitions share an alias (compared case-insensitively).</exception>
    public PackageSourceRegistry(
        IEnumerable<PackageSourceDefinition> definitions,
        Func<PackageSourceDefinition, IPackageSourceProvider> providerFactory)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(providerFactory);

        _providerFactory = providerFactory;
        foreach (var definition in definitions)
        {
            if (string.IsNullOrWhiteSpace(definition.Alias))
            {
                throw new InvalidOperationException("A configured package source has no alias.");
            }

            if (!_definitions.TryAdd(definition.Alias, definition))
            {
                throw new InvalidOperationException(
                    $"Package source alias '{definition.Alias}' is configured more than once; aliases must be unique.");
            }

            _ordered.Add(definition);
        }
    }

    /// <summary>
    ///     Creates a registry whose providers are the built-in zip-folder and NuGet providers.
    /// </summary>
    /// <param name="definitions">The configured sources.</param>
    /// <param name="nugetRootBase">Folder under which NuGet sources keep their private caches, or
    ///     <see langword="null"/> for the per-user default (see
    ///     <see cref="NuGetSourceProvider.GetDefaultRootBase"/>).</param>
    /// <param name="credentials">The credential facilities NuGet server sources use to sign in,
    ///     or <see langword="null"/> when they have none (a server source then only uses what
    ///     NuGet itself supplies). Pass a context without a prompt for work that must not open a
    ///     dialog.</param>
    /// <returns>The registry.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="definitions"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when aliases are blank or duplicated.</exception>
    public static PackageSourceRegistry Create(
        IEnumerable<PackageSourceDefinition> definitions,
        string? nugetRootBase = null,
        PackageCredentialContext? credentials = null) =>
        new(definitions, definition => definition.Type switch
        {
            PackageSourceType.Zip => new ZipFolderSourceProvider(definition),
            PackageSourceType.NuGet => new NuGetSourceProvider(definition, nugetRootBase, credentials),
            _ => throw new InvalidOperationException(
                $"Package source '{definition.Alias}' has an unsupported type '{definition.Type}'.")
        });

    /// <summary>
    ///     Gets the configured source definitions in configuration order.
    /// </summary>
    public IReadOnlyList<PackageSourceDefinition> Definitions => _ordered;

    /// <summary>
    ///     Determines whether a source with the given alias is configured.
    /// </summary>
    /// <param name="alias">The alias to look for; <see langword="null"/> or blank is never
    ///     configured.</param>
    /// <returns><see langword="true"/> if configured (case-insensitively).</returns>
    public bool Contains(string? alias) => !string.IsNullOrWhiteSpace(alias) && _definitions.ContainsKey(alias);

    /// <summary>
    ///     Gets the provider that serves exactly the named source.
    /// </summary>
    /// <param name="alias">The source alias (case-insensitive).</param>
    /// <returns>The provider for that source - never a different source's provider.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="alias"/> is null, empty or
    ///     whitespace.</exception>
    /// <exception cref="UnknownSourceException">Thrown when no source with that alias is
    ///     configured. There is deliberately no fallback.</exception>
    public IPackageSourceProvider Resolve(string alias)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);

        if (!_definitions.TryGetValue(alias, out var definition))
        {
            throw new UnknownSourceException(alias);
        }

        // Create the provider once, lazily, so a misconfigured source only fails when used
        lock (_providers)
        {
            if (!_providers.TryGetValue(alias, out var provider))
            {
                provider = _providerFactory(definition);
                _providers[alias] = provider;
            }

            return provider;
        }
    }
}
