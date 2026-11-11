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

using Avalonia.Controls;
using Avalonia.VisualTree;
using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.RepoConfig;
using DemaConsulting.AgentControl.Settings;
using DemaConsulting.AgentControl.Startup;
using DemaConsulting.AgentControl.Tests.AgentPackageManagement;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Tests of the signature indicator of the agent package chip of <see cref="RepoCardViewModel"/>
///     and <see cref="MainWindow"/>: a small shield, with a tooltip, that appears only when the
///     pinned package's source requires signed packages.
/// </summary>
public sealed class RepoCardSignatureTests : IDisposable
{
    /// <summary>
    ///     The temporary folders created by the running test, removed on disposal.
    /// </summary>
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     Test that a card whose package is supplied by a NuGet source that requires signatures shows the
    ///     shield, and that its tooltip says whether a signer is pinned (naming the start and end of the
    ///     fingerprint only) or any trusted signer is accepted.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_SupplyingSourceRequiresSignatures_ShowsShieldWithTooltip()
    {
        // Arrange
        var anySigner = new PackageSourceDefinition
        {
            Alias = "Any",
            Type = PackageSourceType.NuGet,
            Location = Path.GetTempPath(),
            RequireSignedPackages = true
        };
        var pinned = new PackageSourceDefinition
        {
            Alias = "Pinned",
            Type = PackageSourceType.NuGet,
            Location = Path.GetTempPath(),
            RequireSignedPackages = true,
            SignerFingerprint = "ab:12:cd:34:ef:56:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:98:76"
        };

        // Act
        var anyCard = CreateCard("Any", anySigner);
        var pinnedCard = CreateCard("Pinned", pinned);

        // Assert
        Assert.True(anyCard.RequiresSignature);
        Assert.Equal("Signature required (any trusted signer)", anyCard.SignatureTooltip);
        Assert.True(pinnedCard.RequiresSignature);
        Assert.Equal("Signature required (signer pinned: AB12CD34…9876)", pinnedCard.SignatureTooltip);
    }

    /// <summary>
    ///     Test that no shield is shown for a source without the requirement, for a zip source (a zip
    ///     has no signature), or when the source that supplied the package is no longer configured.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_NoSignatureRequirement_ShowsNoShield()
    {
        // Arrange
        var notRequired = new PackageSourceDefinition { Alias = "Lenient", Type = PackageSourceType.NuGet, Location = Path.GetTempPath() };
        var zip = new PackageSourceDefinition
        {
            Alias = "Zips",
            Type = PackageSourceType.Zip,
            Location = Path.GetTempPath(),
            RequireSignedPackages = true
        };

        // Act / Assert
        Assert.False(CreateCard("Lenient", notRequired).RequiresSignature);
        Assert.False(CreateCard("Zips", zip).RequiresSignature);
        Assert.False(CreateCard("Missing", notRequired).RequiresSignature);
    }

    /// <summary>
    ///     Test that the shield follows the settings: when the source's requirement changes and the
    ///     card refreshes, the card raises change notifications for the shield and its tooltip.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_SourceRequirementChanges_RaisesChangeNotificationsForTheShield()
    {
        // Arrange
        var source = new PackageSourceDefinition { Alias = "Feed", Type = PackageSourceType.NuGet, Location = Path.GetTempPath() };
        var card = CreateCard("Feed", source);
        var raised = new List<string?>();
        card.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        Assert.False(card.RequiresSignature);

        // Act
        source.RequireSignedPackages = true;
        card.RefreshCheap();

        // Assert
        Assert.True(card.RequiresSignature);
        Assert.Contains(nameof(RepoCardViewModel.RequiresSignature), raised);
        Assert.Contains(nameof(RepoCardViewModel.SignatureTooltip), raised);
    }

    /// <summary>
    ///     Test that the main window's package chip shows the shield icon, with the card's tooltip,
    ///     only for a card whose source requires signatures.
    /// </summary>
    [Fact]
    public void MainWindow_PackageChip_ShowsShieldOnlyWhenSignaturesAreRequired() => HeadlessTestApp.Run(() =>
    {
        // Arrange: two repos supplied by two sources (each offering the package), one requiring signatures
        var signedFeed = CreateTempDirectory();
        var openFeed = CreateTempDirectory();
        NuGetFeedBuilder.CreateAgentPackage(signedFeed, "contoso-agents", "1.0.0");
        NuGetFeedBuilder.CreateAgentPackage(openFeed, "contoso-agents", "1.0.0");
        var required = new PackageSourceDefinition
        {
            Alias = "Signed",
            Type = PackageSourceType.NuGet,
            Location = signedFeed,
            RequireSignedPackages = true,
            SignerFingerprint = "A3AF7AF11EBB8EF729D2D91548509717E7E0FF55A129ABC3AEAA8A6940267641"
        };
        var lenient = new PackageSourceDefinition { Alias = "Open", Type = PackageSourceType.NuGet, Location = openFeed };
        var settings = new AppSettings { Sources = [required, lenient] };
        var signedRepo = CreateRepoWithPin();
        var openRepo = CreateRepoWithPin();
        settings.RecentRepos =
        [
            new RecentRepo { Path = signedRepo, PinnedPackageName = "contoso-agents", PinnedPackageVersion = "1.0.0", PinnedSource = "Signed" },
            new RecentRepo { Path = openRepo, PinnedPackageName = "contoso-agents", PinnedPackageVersion = "1.0.0", PinnedSource = "Open" }
        ];
        var viewModel = new MainWindowViewModel(settings, StartupOptions.Parse(["--config-dir", CreateTempDirectory()]), CreateTempDirectory());
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Act: find the shield icons in the realized chips
        var shields = window.GetVisualDescendants()
            .OfType<Material.Icons.Avalonia.MaterialIcon>()
            .Where(icon => icon.Name == "PackageSignatureIcon")
            .ToList();

        // Assert: one shield per card, visible only for the signed source, with the right tooltip
        Assert.Equal(2, shields.Count);
        var visible = Assert.Single(shields, shield => shield.IsVisible);
        Assert.Equal("Signature required (signer pinned: A3AF7AF1…7641)", ToolTip.GetTip(visible));
        window.Close();
    });

    /// <summary>
    ///     Creates a card supplied by a source alias (remembered in the user's own settings, not in the pin),
    ///     over a settings object with the given source (the source is configured only when its alias
    ///     matches the supplier).
    /// </summary>
    /// <param name="supplyingAlias">The alias of the source that supplies the package.</param>
    /// <param name="source">The configured source.</param>
    /// <returns>The card.</returns>
    private RepoCardViewModel CreateCard(string supplyingAlias, PackageSourceDefinition source)
    {
        var settings = new AppSettings { Sources = [source] };
        var recentRepo = new RecentRepo
        {
            Path = CreateRepoWithPin(),
            PinnedPackageName = "contoso-agents",
            PinnedPackageVersion = "1.0.0",
            PinnedSource = supplyingAlias
        };
        var startupOptions = StartupOptions.Parse(["--config-dir", CreateTempDirectory()]);
        return new RepoCardViewModel(recentRepo, () => settings, new PackageVersionCache(), startupOptions);
    }

    /// <summary>
    ///     Creates a repo folder whose pin file names a package and version (and no source).
    /// </summary>
    /// <returns>The repo folder.</returns>
    private string CreateRepoWithPin()
    {
        var repo = CreateTempDirectory();
        RepoPinStore.Save(repo, new RepoPin { PackageName = "contoso-agents", Version = "1.0.0" });
        return repo;
    }

    /// <summary>
    ///     Creates a new empty temporary directory removed when the test ends.
    /// </summary>
    /// <returns>The directory's absolute path.</returns>
    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_card_signature_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        return path;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var path in _tempPaths.Where(Directory.Exists))
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
}
