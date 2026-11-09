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
using DemaConsulting.AgentControl.RepoSync;
using DemaConsulting.AgentControl.Tests.AgentPackageManagement;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Unit tests for the NuGet-package-server mode of <see cref="SourceEditorViewModel"/>:
///     validation of the feed address, the insecure-HTTP choice and the connection test with its
///     progress, cancel and result.
/// </summary>
/// <remarks>
///     The connection test is driven through a fake provider, so no network or dialog is involved;
///     the provider's own behavior against a real server is covered by its own tests.
/// </remarks>
public class SourceEditorServerViewModelTests
{
    /// <summary>
    ///     A valid https feed address used by most tests.
    /// </summary>
    private const string FeedAddress = "https://feeds.example.com/artifactory/api/nuget/v3/agents/index.json";

    /// <summary>
    ///     Test that a new server editor has the server title, the feed-address label and
    ///     examples, the package ID section, and insecure HTTP off.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_Constructor_NewServer_HasServerTitleLabelAndExamples()
    {
        // Act
        var editor = CreateEditor();

        // Assert
        Assert.Equal("Add NuGet-Package Server", editor.Title);
        Assert.True(editor.IsServer);
        Assert.True(editor.IsNuGet);
        Assert.Equal("Feed address", editor.LocationLabel);
        Assert.Contains("index.json", editor.LocationPlaceholder, StringComparison.Ordinal);
        Assert.Contains("JFrog Artifactory: https://<host>/artifactory/api/nuget/v3/<repo>/index.json",
            SourceEditorViewModel.LocationExamplesText, StringComparison.Ordinal);
        Assert.Contains("Azure Artifacts: https://pkgs.dev.azure.com/<org>/_packaging/<feed>/nuget/v3/index.json",
            SourceEditorViewModel.LocationExamplesText, StringComparison.Ordinal);
        Assert.False(editor.AllowInsecureHttp);
        Assert.False(editor.ShowInsecureWarning);
        Assert.False(editor.CanSave);
    }

    /// <summary>
    ///     Test that a folder editor is not a server editor and does not offer the connection test.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_Constructor_NuGetFolder_IsNotAServerAndCannotTest()
    {
        // Act
        var editor = new SourceEditorViewModel(PackageSourceType.NuGet, null, _ => false);

        // Assert
        Assert.False(editor.IsServer);
        Assert.Equal("Folder", editor.LocationLabel);
        Assert.False(editor.CanTestConnection);
    }

    /// <summary>
    ///     Test that feed addresses that are not acceptable are reported with a message that says
    ///     what to do, and that a credential in the address is refused without repeating it.
    /// </summary>
    [Theory]
    [InlineData("   ", "Enter the address")]
    [InlineData("C:\\feeds\\agents", "web address")]
    [InlineData("https://feeds.example.com/v3/", "must end in index.json")]
    [InlineData("https://feeds.example.com/v3/index.json?sig=sentinel-secret", "must not contain")]
    [InlineData("https://user:sentinel-secret@feeds.example.com/v3/index.json", "must not contain")]
    [InlineData("http://feeds.example.com/v3/index.json", "Plain http")]
    public void SourceEditorViewModel_ServerLocation_Invalid_ReportsProblemAndBlocksSave(string location, string expected)
    {
        // Arrange
        var editor = CreateEditor();
        editor.Alias = "Feed";
        editor.NewPackageId = "contoso-agents";
        editor.AddPackageIdCommand.Execute(null);

        // Act
        editor.Location = location;

        // Assert
        Assert.Contains(expected, editor.LocationError, StringComparison.Ordinal);
        Assert.DoesNotContain("sentinel-secret", editor.LocationError, StringComparison.Ordinal);
        Assert.False(editor.CanSave);
        Assert.False(editor.CanTestConnection);
    }

    /// <summary>
    ///     Test that ticking "Allow insecure HTTP" makes a plain http address acceptable and shows
    ///     the warning, and clearing the box brings the error back.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_AllowInsecureHttp_Toggled_AcceptsHttpAndShowsWarning()
    {
        // Arrange
        var editor = CreateEditor();
        editor.Alias = "Feed";
        editor.NewPackageId = "contoso-agents";
        editor.AddPackageIdCommand.Execute(null);
        editor.Location = "http://127.0.0.1:5000/v3/index.json";
        Assert.False(editor.CanSave);

        // Act
        editor.AllowInsecureHttp = true;

        // Assert: accepted, with the warning visible
        Assert.Null(editor.LocationError);
        Assert.True(editor.CanSave);
        Assert.True(editor.ShowInsecureWarning);

        // Act: clear the box
        editor.AllowInsecureHttp = false;

        // Assert
        Assert.NotNull(editor.LocationError);
        Assert.False(editor.CanSave);
        Assert.False(editor.ShowInsecureWarning);
    }

    /// <summary>
    ///     Test that a valid server source is saved with its trimmed address, package IDs and the
    ///     insecure-HTTP choice.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_SaveCommand_ValidServer_SavesAddressPackageIdsAndInsecureChoice()
    {
        // Arrange
        var editor = CreateEditor();
        editor.Alias = "Feed";
        editor.Location = "  http://127.0.0.1:5000/v3/index.json  ";
        editor.AllowInsecureHttp = true;
        editor.NewPackageId = "contoso-agents";
        editor.AddPackageIdCommand.Execute(null);
        PackageSourceDefinition? saved = null;
        editor.Saved += (_, definition) => saved = definition;

        // Act
        editor.SaveCommand.Execute(null);

        // Assert
        Assert.NotNull(saved);
        Assert.Equal("Feed", saved.Alias);
        Assert.Equal(PackageSourceType.NuGet, saved.Type);
        Assert.Equal("http://127.0.0.1:5000/v3/index.json", saved.Location);
        Assert.True(saved.AllowInsecureHttp);
        Assert.Equal(["contoso-agents"], saved.PackageIds);
        Assert.Equal(PackageSourceLocationKind.Server, saved.LocationKind);
    }

    /// <summary>
    ///     Test that a server package list that is empty is reported with a server-specific
    ///     message and blocks saving.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_PackageIds_EmptyForServer_ReportsServerSpecificMessage()
    {
        // Arrange
        var editor = CreateEditor();
        editor.Alias = "Feed";
        editor.Location = FeedAddress;

        // Assert
        Assert.Contains("NuGet-package server", editor.PackageIdsError, StringComparison.Ordinal);
        Assert.False(editor.CanSave);
    }

    /// <summary>
    ///     Test that editing an existing server source keeps it a server, shows its settings and
    ///     preserves the insecure-HTTP choice.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_EditExistingServer_StaysAServerWithItsSettings()
    {
        // Arrange
        var existing = new PackageSourceDefinition
        {
            Alias = "Feed",
            Type = PackageSourceType.NuGet,
            Location = "http://127.0.0.1:5000/v3/index.json",
            PackageIds = ["contoso-agents"],
            AllowInsecureHttp = true
        };

        // Act
        var editor = new SourceEditorViewModel(PackageSourceType.Zip, existing, _ => true);

        // Assert
        Assert.True(editor.IsServer);
        Assert.Equal("Edit NuGet-Package Server", editor.Title);
        Assert.True(editor.AllowInsecureHttp);
        Assert.True(editor.ShowInsecureWarning);
        Assert.True(editor.CanSave);
        Assert.True(editor.CanTestConnection);
    }

    /// <summary>
    ///     Test that the connection test needs a valid address and at least one package ID, and
    ///     explains what is missing in the disabled button's tooltip.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_CanTestConnection_NeedsValidAddressAndPackageId()
    {
        // Arrange
        var editor = CreateEditor();
        var disabledTip = editor.TestConnectionTooltip;

        // Act / Assert: nothing entered
        Assert.False(editor.CanTestConnection);
        Assert.Contains("Enter a valid feed address", disabledTip, StringComparison.Ordinal);

        // Address only
        editor.Location = FeedAddress;
        Assert.False(editor.CanTestConnection);

        // Address and a package ID: the alias is not needed
        editor.NewPackageId = "contoso-agents";
        editor.AddPackageIdCommand.Execute(null);
        Assert.True(editor.CanTestConnection);
        Assert.True(editor.TestConnectionCommand.CanExecute(null));
        Assert.Contains("lists the first package ID", editor.TestConnectionTooltip, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a successful connection test lists the first package ID only, uses a
    ///     placeholder alias when none was typed, and reports the number of versions and the latest.
    /// </summary>
    [Fact]
    public async Task SourceEditorViewModel_TestConnection_ServerAnswers_ReportsVersionCountAndLatest()
    {
        // Arrange
        var fake = new FakePackageSourceProvider("any", ("contoso-agents", "1.0.0"), ("contoso-agents", "1.10.0"), ("contoso-agents", "1.2.0"));
        PackageSourceDefinition? tested = null;
        var editor = CreateEditor(definition =>
        {
            tested = definition;
            return fake;
        });
        editor.Location = FeedAddress;
        editor.NewPackageId = "contoso-agents";
        editor.AddPackageIdCommand.Execute(null);
        editor.NewPackageId = "second-agents";
        editor.AddPackageIdCommand.Execute(null);

        // Act
        await editor.TestConnectionCommand.ExecuteAsync();

        // Assert: success, with the count and the highest version; only the first ID was asked for
        Assert.True(editor.HasTestResult);
        Assert.True(editor.TestSucceeded);
        Assert.Equal("Connection OK", editor.TestResultTitle);
        Assert.Contains("3 versions of 'contoso-agents'", editor.TestResultDetail, StringComparison.Ordinal);
        Assert.Contains("latest 1.10.0", editor.TestResultDetail, StringComparison.Ordinal);
        Assert.Equal(1, fake.ListVersionsCalls);
        Assert.False(editor.IsTesting);
        Assert.NotNull(tested);
        Assert.Equal(FeedAddress, tested.Location);
        Assert.False(string.IsNullOrWhiteSpace(tested.Alias));
    }

    /// <summary>
    ///     Test that a server that answers but does not have the package is reported as "package
    ///     not found" with advice.
    /// </summary>
    [Fact]
    public async Task SourceEditorViewModel_TestConnection_PackageMissing_ReportsPackageNotFound()
    {
        // Arrange
        var editor = CreateEditor(_ => new FakePackageSourceProvider("any"));
        AddValidServerValues(editor);

        // Act
        await editor.TestConnectionCommand.ExecuteAsync();

        // Assert
        Assert.True(editor.TestFailed);
        Assert.Equal("Package not found", editor.TestResultTitle);
        Assert.Contains("contoso-agents", editor.TestResultDetail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that each way a source can fail is reported with its own headline and the message
    ///     the provider gave, and that nothing is a crash.
    /// </summary>
    /// <param name="kindName">The name of the failure cause the provider reports.</param>
    /// <param name="expectedTitle">The headline expected.</param>
    [Theory]
    [InlineData("AuthenticationFailed", "Sign-in failed")]
    [InlineData("SignInCanceled", "Sign-in canceled")]
    [InlineData("NotFound", "Package not found")]
    [InlineData("TimedOut", "Timed out")]
    [InlineData("Unreachable", "Server unreachable")]
    public async Task SourceEditorViewModel_TestConnection_ProviderFails_ReportsHeadlineAndMessage(
        string kindName, string expectedTitle)
    {
        // Arrange
        var kind = Enum.Parse<PackageSourceFailureKind>(kindName);
        var fake = new FakePackageSourceProvider("any") { ListVersionsException = new PackageSourceAccessException(kind, "the provider's message") };
        var editor = CreateEditor(_ => fake);
        AddValidServerValues(editor);

        // Act
        await editor.TestConnectionCommand.ExecuteAsync();

        // Assert
        Assert.True(editor.TestFailed);
        Assert.Equal(expectedTitle, editor.TestResultTitle);
        Assert.Equal("the provider's message", editor.TestResultDetail);
    }

    /// <summary>
    ///     Test that a configuration error raised when the provider is created, and an unexpected
    ///     failure, are both reported in the dialog.
    /// </summary>
    [Fact]
    public async Task SourceEditorViewModel_TestConnection_OtherFailures_AreReportedInTheDialog()
    {
        // Arrange: first the provider cannot be created, then it throws something unforeseen
        var editor = CreateEditor(_ => throw new InvalidOperationException("The cache root is not usable."));
        AddValidServerValues(editor);

        // Act / Assert: a configuration problem
        await editor.TestConnectionCommand.ExecuteAsync();
        Assert.Equal("Connection failed", editor.TestResultTitle);
        Assert.Equal("The cache root is not usable.", editor.TestResultDetail);

        // Arrange: an unforeseen failure
        var unforeseen = CreateEditor(_ => new FakePackageSourceProvider("any") { ListVersionsException = new NotSupportedException("boom") });
        AddValidServerValues(unforeseen);

        // Act
        await unforeseen.TestConnectionCommand.ExecuteAsync();

        // Assert
        Assert.True(unforeseen.TestFailed);
        Assert.Contains("could not be completed", unforeseen.TestResultDetail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that while a test runs the dialog shows progress naming the package and offers
    ///     Cancel, that canceling ends it with a neutral "canceled" result, and that the test can
    ///     be run again afterwards.
    /// </summary>
    [Fact]
    public async Task SourceEditorViewModel_TestConnection_CancelWhileRunning_StopsWithCanceledResult()
    {
        // Arrange: a provider that waits until released
        var release = new TaskCompletionSource();
        var fake = new FakePackageSourceProvider("any", ("contoso-agents", "1.0.0")) { ListVersionsGate = release.Task };
        var editor = CreateEditor(_ => fake);
        AddValidServerValues(editor);
        Assert.False(editor.CancelTestCommand.CanExecute(null));

        // Act: start the test
        var running = editor.TestConnectionCommand.ExecuteAsync();

        // Assert: progress is shown, the test cannot be started twice, and Cancel is offered
        Assert.True(editor.IsTesting);
        Assert.Contains("contoso-agents", editor.TestProgressText, StringComparison.Ordinal);
        Assert.False(editor.TestConnectionCommand.CanExecute(null));
        Assert.True(editor.CancelTestCommand.CanExecute(null));
        Assert.False(editor.HasTestResult);

        // Act: cancel
        editor.CancelTestCommand.Execute(null);
        await running;

        // Assert
        Assert.False(editor.IsTesting);
        Assert.True(editor.TestWasCanceled);
        Assert.Equal("Test canceled", editor.TestResultTitle);
        Assert.False(editor.CancelTestCommand.CanExecute(null));
        Assert.True(editor.TestConnectionCommand.CanExecute(null));
        release.SetResult();
    }

    /// <summary>
    ///     Test that a test result is dropped when the address or the package IDs change, because
    ///     it described the old values.
    /// </summary>
    [Fact]
    public async Task SourceEditorViewModel_TestConnection_ResultDroppedWhenAddressChanges()
    {
        // Arrange
        var editor = CreateEditor(_ => new FakePackageSourceProvider("any", ("contoso-agents", "1.0.0")));
        AddValidServerValues(editor);
        await editor.TestConnectionCommand.ExecuteAsync();
        Assert.True(editor.TestSucceeded);

        // Act
        editor.Location = FeedAddress + "x";

        // Assert
        Assert.False(editor.HasTestResult);
        Assert.Equal(string.Empty, editor.TestResultTitle);
    }

    /// <summary>
    ///     Test that when the address is edited while a connection test runs, the test is stopped
    ///     and its result - even one produced by a provider that ignores cancellation and finishes
    ///     successfully - is never shown, because it describes an address that is no longer there.
    /// </summary>
    [Fact]
    public async Task SourceEditorViewModel_TestConnection_LocationEditedWhileTesting_StaleResultIsNotShown()
    {
        // Arrange: a provider that ignores cancellation and answers "success" once released
        var release = new TaskCompletionSource();
        Assert.True(PackageVersion.TryParse("1.0.0", out var version));
        var provider = new NonCancelableProvider(release.Task, [new DiscoveredPackage("contoso-agents", version!, "1.0.0", "any")]);
        var editor = CreateEditor(_ => provider);
        AddValidServerValues(editor);
        var running = editor.TestConnectionCommand.ExecuteAsync();
        Assert.True(editor.IsTesting);

        // Act: the address changes mid-test, then the old test finishes
        editor.Location = "https://other.example.com/v3/index.json";
        release.SetResult();
        await running;

        // Assert: nothing about the old address is shown
        Assert.False(editor.IsTesting);
        Assert.False(editor.HasTestResult);
        Assert.False(editor.TestSucceeded);
        Assert.Equal(string.Empty, editor.TestResultTitle);
        Assert.Equal(string.Empty, editor.TestResultDetail);
    }

    /// <summary>
    ///     Test that when the package IDs are edited while a connection test runs, the test's result
    ///     is not shown.
    /// </summary>
    [Fact]
    public async Task SourceEditorViewModel_TestConnection_PackageIdsEditedWhileTesting_StaleResultIsNotShown()
    {
        // Arrange
        var release = new TaskCompletionSource();
        var fake = new FakePackageSourceProvider("any", ("contoso-agents", "1.0.0")) { ListVersionsGate = release.Task };
        var editor = CreateEditor(_ => fake);
        AddValidServerValues(editor);
        var running = editor.TestConnectionCommand.ExecuteAsync();
        Assert.True(editor.IsTesting);

        // Act: another package ID is added mid-test, then the old test ends
        editor.NewPackageId = "second-agents";
        editor.AddPackageIdCommand.Execute(null);
        release.SetResult();
        await running;

        // Assert
        Assert.False(editor.IsTesting);
        Assert.False(editor.HasTestResult);
        Assert.Equal(string.Empty, editor.TestResultTitle);
    }

    /// <summary>
    ///     Test that when the insecure-HTTP choice is changed while a connection test runs, the
    ///     test's result is not shown.
    /// </summary>
    [Fact]
    public async Task SourceEditorViewModel_TestConnection_AllowInsecureHttpChangedWhileTesting_StaleResultIsNotShown()
    {
        // Arrange
        var release = new TaskCompletionSource();
        var fake = new FakePackageSourceProvider("any", ("contoso-agents", "1.0.0")) { ListVersionsGate = release.Task };
        var editor = CreateEditor(_ => fake);
        AddValidServerValues(editor);
        var running = editor.TestConnectionCommand.ExecuteAsync();
        Assert.True(editor.IsTesting);

        // Act
        editor.AllowInsecureHttp = true;
        release.SetResult();
        await running;

        // Assert
        Assert.False(editor.IsTesting);
        Assert.False(editor.HasTestResult);
        Assert.Equal(string.Empty, editor.TestResultTitle);
    }

    /// <summary>
    ///     Test that a test started after an edit reports normally: only a test whose inputs changed
    ///     underneath it is ignored.
    /// </summary>
    [Fact]
    public async Task SourceEditorViewModel_TestConnection_StartedAfterTheEdit_ReportsItsResult()
    {
        // Arrange
        var editor = CreateEditor(_ => new FakePackageSourceProvider("any", ("contoso-agents", "1.0.0")));
        AddValidServerValues(editor);
        editor.Location = "https://other.example.com/v3/index.json";

        // Act
        await editor.TestConnectionCommand.ExecuteAsync();

        // Assert
        Assert.True(editor.TestSucceeded);
        Assert.Equal("Connection OK", editor.TestResultTitle);
    }

    /// <summary>
    ///     Creates a new server editor with no aliases taken.
    /// </summary>
    /// <param name="providerFactory">Creates the provider the connection test uses, or
    ///     <see langword="null"/> for the default.</param>
    /// <returns>The editor.</returns>
    private static SourceEditorViewModel CreateEditor(
        Func<PackageSourceDefinition, IPackageSourceProvider>? providerFactory = null) =>
        new(PackageSourceType.NuGet, null, _ => false, isServer: true, providerFactory);

    /// <summary>
    ///     Fills in a valid address and one package ID.
    /// </summary>
    /// <param name="editor">The editor to fill.</param>
    private static void AddValidServerValues(SourceEditorViewModel editor)
    {
        editor.Location = FeedAddress;
        editor.NewPackageId = "contoso-agents";
        editor.AddPackageIdCommand.Execute(null);
    }

    /// <summary>
    ///     A provider that ignores cancellation: its version listing finishes with the given
    ///     versions as soon as its gate opens, whatever the token says - like a source that cannot
    ///     interrupt a request it has already sent.
    /// </summary>
    /// <param name="gate">The task the listing waits for.</param>
    /// <param name="versions">The versions the listing then returns.</param>
    private sealed class NonCancelableProvider(Task gate, IReadOnlyList<DiscoveredPackage> versions) : IPackageSourceProvider
    {
        /// <inheritdoc />
        public string Alias => "any";

        /// <inheritdoc />
        public Task<IReadOnlyList<string>> ListPackageIdsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        /// <inheritdoc />
        public async Task<IReadOnlyList<DiscoveredPackage>> ListVersionsAsync(
            string packageId, CancellationToken cancellationToken)
        {
            await gate;
            return versions;
        }

        /// <inheritdoc />
        public Task<IPackageContentReader> AcquireAsync(DiscoveredPackage package, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The connection test never acquires a package.");
    }
}
