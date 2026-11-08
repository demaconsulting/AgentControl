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
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.Logging;
using DemaConsulting.AgentControl.PackageCredentials;
using DemaConsulting.AgentControl.RepoConfig;
using DemaConsulting.AgentControl.Settings;
using DemaConsulting.AgentControl.Tests.AgentPackageManagement;
using DemaConsulting.AgentControl.Tests.PackageCredentials;
using DemaConsulting.NuGet.Caching;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Tests that an exception carrying a password, an Authorization value and a URL with a query
///     string - raised by a package source, inside an inner exception, or as an unexpected
///     failure - never reaches what the user sees (Test connection, Select Package and repo-card
///     status text) or what is logged.
/// </summary>
/// <remarks>
///     In the <c>RealProcess</c> collection because the repo-card tests swap the process-wide
///     logger factory to capture what the card logs.
/// </remarks>
[Collection("RealProcess")]
public sealed class PackageSourceErrorRedactionTests : IDisposable
{
    /// <summary>
    ///     A credential whose secret is registered for the test, so it is removed even where no
    ///     pattern would match it.
    /// </summary>
    private readonly NuGetCredential _credential = new("svc-agents", "sec-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    ///     The temporary folders created by the running test.
    /// </summary>
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     Initializes the test: the password is "in use".
    /// </summary>
    public PackageSourceErrorRedactionTests() => SecretRedactor.Register(_credential);

    /// <inheritdoc />
    public void Dispose()
    {
        SecretRedactor.Unregister(_credential);
        foreach (var path in _tempPaths)
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup of a temporary folder
            }
        }
    }

    /// <summary>
    ///     Test that a package-source exception keeps only a sanitized description of its inner
    ///     exception: <c>ToString</c>, the message and the inner exception's text hold none of the
    ///     secrets, and the original object is not reachable.
    /// </summary>
    [Fact]
    public void PackageSourceAccessException_InnerExceptionWithSecrets_IsReplacedByASanitizedDescription()
    {
        // Arrange
        var inner = Leaky();

        // Act
        var ex = new PackageSourceAccessException(
            PackageSourceFailureKind.Unreachable, $"The source said {_credential.Secret}.", inner);

        // Assert
        var sanitized = Assert.IsType<SanitizedException>(ex.InnerException);
        Assert.NotSame(inner, sanitized);
        Assert.Null(sanitized.InnerException);
        AssertClean(ex.ToString());
        AssertClean(ex.Message);
        AssertClean(sanitized.Message);
        Assert.Contains("InvalidOperationException", sanitized.Message, StringComparison.Ordinal);
        Assert.Contains("https://feeds.example.com/v3/index.json", sanitized.Message, StringComparison.Ordinal);
        Assert.Null(new PackageSourceAccessException(PackageSourceFailureKind.TimedOut, "slow").InnerException);
    }

    /// <summary>
    ///     Test that Test connection shows only redacted text for a source that fails with an
    ///     exception that carries secrets - a package-source failure, a plain invalid-operation
    ///     failure, and an unforeseen exception type.
    /// </summary>
    [Fact]
    public async Task SourceEditorViewModel_TestConnection_ExceptionsWithSecrets_ShowOnlyRedactedText()
    {
        // Arrange / Act / Assert for each kind of failure
        foreach (var failure in new Exception[]
        {
            new PackageSourceAccessException(PackageSourceFailureKind.Unreachable, $"down {_credential.Secret}", Leaky()),
            Leaky(),
            new NotSupportedException(Leaky().Message, Leaky())
        })
        {
            var editor = new SourceEditorViewModel(
                PackageSourceType.NuGet,
                null,
                _ => false,
                isServer: true,
                _ => new FakePackageSourceProvider("any") { ListVersionsException = failure });
            editor.Location = "https://feeds.example.com/v3/index.json";
            editor.NewPackageId = "contoso-agents";
            editor.AddPackageIdCommand.Execute(null);

            await editor.TestConnectionCommand.ExecuteAsync();

            Assert.True(editor.TestFailed);
            AssertClean(editor.TestResultTitle);
            AssertClean(editor.TestResultDetail);
        }
    }

    /// <summary>
    ///     Test that Select Package shows only redacted text, inline on the source row and for the
    ///     version list, when a source fails with an exception that carries secrets.
    /// </summary>
    [Fact]
    public async Task SelectPackageWindowViewModel_SourceAndVersionFailuresWithSecrets_ShowOnlyRedactedText()
    {
        // Arrange: one source whose package listing fails, one whose version listing fails
        var idsFail = new ThrowingIdsProvider("IdsFail", Leaky());
        var versionsFail = new FakePackageSourceProvider("VersionsFail", ("contoso-agents", "1.0.0"))
        {
            ListVersionsException = Leaky()
        };
        var registry = new PackageSourceRegistry(
            [Source("IdsFail"), Source("VersionsFail")],
            definition => definition.Alias == "IdsFail" ? idsFail : versionsFail);
        var viewModel = new SelectPackageWindowViewModel(registry, new PackageVersionCache());

        // Act
        await viewModel.LoadAsync(CancellationToken.None);
        var failedRow = viewModel.Sources.Single(source => source.Alias == "IdsFail");
        viewModel.SelectedSource = viewModel.Sources.Single(source => source.Alias == "VersionsFail");
        viewModel.SelectedPackageName = "contoso-agents";
        await viewModel.PendingVersionsLoad;

        // Assert
        Assert.True(failedRow.HasError);
        AssertClean(failedRow.ErrorMessage!);
        Assert.True(viewModel.HasVersionsError);
        AssertClean(viewModel.VersionsError!);
    }

    /// <summary>
    ///     Test that the repo card shows only redacted status text, and logs only redacted text, when
    ///     the package source fails with an exception that carries secrets, and that an unexpected
    ///     exception from the operation is handled the same way.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_PackageSourceFailureWithSecrets_StatusTextAndLogsAreRedacted()
    {
        // Arrange: capture what the card logs
        var log = new ListLogger();
        var previousFactory = AppLogging.Factory;
        AppLogging.Factory = new SingleLoggerFactory(log);
        try
        {
            foreach (var unexpected in new[] { false, true })
            {
                // An unexpected exception type is not handled by the operation itself, so it
                // reaches the card's last-resort handler (the Refresh command's error handler)
                Exception failure = unexpected ? new NotSupportedException(Leaky().Message, Leaky()) : Leaky();
                var provider = new FakePackageSourceProvider("Default", ("contoso-agents", "1.0.0"))
                {
                    ListVersionsException = failure
                };
                var registry = new PackageSourceRegistry([Source("Default")], _ => provider);
                var repoRoot = CreateTempDirectory();
                RepoPinStore.Save(
                    repoRoot, new RepoPin { Source = "Default", PackageName = "contoso-agents", Version = "1.0.0" });
                var card = new RepoCardViewModel(
                    new RecentRepo
                    {
                        Path = repoRoot,
                        PinnedSource = "Default",
                        PinnedPackageName = "contoso-agents",
                        PinnedPackageVersion = "1.0.0"
                    },
                    () => new AppSettings(),
                    new PackageVersionCache(),
                    registryFactory: _ => registry);
                var errors = new List<string>();
                card.ErrorOccurred += (_, message) => errors.Add(message);

                // Act
                if (unexpected)
                {
                    await card.RefreshCommand.ExecuteAsync();
                }
                else
                {
                    await card.ApplySelectedPackageAsync(
                        new PackageReference("Default", "contoso-agents", "1.0.0"), TestContext.Current.CancellationToken);
                }

                // Assert
                Assert.True(errors.Count == 1, $"unexpected={unexpected}, errors={errors.Count}, status={card.StatusMessage}, busy={card.IsBusy}");
                var error = errors[0];
                Assert.Contains(unexpected ? "The operation failed" : "Select Package failed", error, StringComparison.Ordinal);
                AssertClean(error);
                AssertClean(card.StatusMessage ?? string.Empty);
            }
        }
        finally
        {
            AppLogging.Factory = previousFactory;
        }

        Assert.NotEmpty(log.Snapshot());
        Assert.All(log.Snapshot(), AssertClean);
    }

    /// <summary>
    ///     Creates an exception whose message and inner exception carry the password, an
    ///     Authorization value and URLs with query strings and user information.
    /// </summary>
    /// <returns>The exception.</returns>
    private InvalidOperationException Leaky() => new(
        "request to https://feeds.example.com/v3/index.json?sig=sentinel-query failed for " +
        $"{_credential.Secret} with Authorization: Basic sentinel-authorization",
        new IOException(
            $"inner https://svc:sentinel-userinfo@h.example.com/x?t=sentinel-inner {_credential.Secret} " +
            "Proxy-Authorization: Digest response=sentinel-proxy"));

    /// <summary>
    ///     Asserts that text holds none of the secrets that <see cref="Leaky"/> plants.
    /// </summary>
    /// <param name="text">The text a user could see or a log could hold.</param>
    private void AssertClean(string text)
    {
        Assert.DoesNotContain(_credential.Secret, text, StringComparison.Ordinal);
        Assert.DoesNotContain("sentinel", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Creates a zip source definition for a fake provider to serve.
    /// </summary>
    /// <param name="alias">The alias.</param>
    /// <returns>The definition.</returns>
    private static PackageSourceDefinition Source(string alias) =>
        new() { Alias = alias, Type = PackageSourceType.Zip, Location = "unused" };

    /// <summary>
    ///     Creates a new empty temporary directory removed when the test ends.
    /// </summary>
    /// <returns>The directory's absolute path.</returns>
    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_redaction_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        return path;
    }

    /// <summary>
    ///     A source whose package listing throws.
    /// </summary>
    private sealed class ThrowingIdsProvider : IPackageSourceProvider
    {
        /// <summary>
        ///     The exception to throw.
        /// </summary>
        private readonly Exception _exception;

        /// <summary>
        ///     Initializes the provider.
        /// </summary>
        /// <param name="alias">The alias.</param>
        /// <param name="exception">The exception to throw.</param>
        public ThrowingIdsProvider(string alias, Exception exception)
        {
            Alias = alias;
            _exception = exception;
        }

        /// <inheritdoc />
        public string Alias { get; }

        /// <inheritdoc />
        public Task<IReadOnlyList<string>> ListPackageIdsAsync(CancellationToken cancellationToken) =>
            throw _exception;

        /// <inheritdoc />
        public Task<IReadOnlyList<DiscoveredPackage>> ListVersionsAsync(string packageId, CancellationToken cancellationToken) =>
            throw _exception;

        /// <inheritdoc />
        public Task<DemaConsulting.AgentControl.RepoSync.IPackageContentReader> AcquireAsync(
            DiscoveredPackage package, CancellationToken cancellationToken) => throw _exception;
    }

    /// <summary>
    ///     A logger factory that hands every category the same logger.
    /// </summary>
    private sealed class SingleLoggerFactory : ILoggerFactory
    {
        /// <summary>
        ///     The logger every category receives.
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        ///     Initializes the factory.
        /// </summary>
        /// <param name="logger">The logger every category receives.</param>
        public SingleLoggerFactory(ILogger logger) => _logger = logger;

        /// <inheritdoc />
        public void AddProvider(ILoggerProvider provider)
        {
            // Providers are not used by this factory
        }

        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName) => _logger;

        /// <inheritdoc />
        public void Dispose()
        {
            // Nothing to release
        }
    }
}
