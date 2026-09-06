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

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Unit tests for <see cref="SelectPackageWindowViewModel"/>.
/// </summary>
/// <remarks>
///     No Avalonia <see cref="Avalonia.Controls.Window"/> is ever constructed here, matching
///     <c>SettingsWindowViewModelTests</c>'s headless pattern; only real temporary
///     directories/zip fixtures are used for the underlying <see cref="PackageVersionCache"/>.
/// </remarks>
public sealed class SelectPackageWindowViewModelTests : IDisposable
{
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     Test that selecting a package name populates AvailableVersions descending and defaults
    ///     SelectedVersion to the first (latest) entry.
    /// </summary>
    [Fact]
    public void SelectPackageWindowViewModel_SelectPackageName_PopulatesVersionsDescendingAndDefaultsToLatest()
    {
        // Arrange: a source with three versions of one package
        var sourceDir = CreateTempDirectory();
        CreateFile(sourceDir, "contoso-agents-1.0.0.zip");
        CreateFile(sourceDir, "contoso-agents-2.5.0.zip");
        CreateFile(sourceDir, "contoso-agents-2.4.9.zip");
        var viewModel = new SelectPackageWindowViewModel(sourceDir, new PackageVersionCache());

        // Act
        viewModel.SelectedPackageName = "contoso-agents";

        // Assert: descending order, defaulting to the latest
        Assert.Equal(["2.5.0", "2.4.9", "1.0.0"], viewModel.AvailableVersions);
        Assert.Equal("2.5.0", viewModel.SelectedVersion);
    }

    /// <summary>
    ///     Test that changing SelectedPackageName again recomputes AvailableVersions and resets
    ///     SelectedVersion.
    /// </summary>
    [Fact]
    public void SelectPackageWindowViewModel_ChangeSelectedPackageName_RecomputesVersionsAndResetsSelection()
    {
        // Arrange: two distinct packages
        var sourceDir = CreateTempDirectory();
        CreateFile(sourceDir, "contoso-agents-1.0.0.zip");
        CreateFile(sourceDir, "other-package-3.0.0.zip");
        var viewModel = new SelectPackageWindowViewModel(sourceDir, new PackageVersionCache());
        viewModel.SelectedPackageName = "contoso-agents";
        Assert.Equal("1.0.0", viewModel.SelectedVersion);

        // Act: change to the other package
        viewModel.SelectedPackageName = "other-package";

        // Assert: versions/selection reflect the newly-selected package
        Assert.Equal(["3.0.0"], viewModel.AvailableVersions);
        Assert.Equal("3.0.0", viewModel.SelectedVersion);
    }

    /// <summary>
    ///     Test that CanConfirm/ConfirmCommand.CanExecute is false until both a name and version
    ///     are selected.
    /// </summary>
    [Fact]
    public void SelectPackageWindowViewModel_NoSelection_CanConfirmIsFalse()
    {
        // Arrange
        var sourceDir = CreateTempDirectory();
        CreateFile(sourceDir, "contoso-agents-1.0.0.zip");
        var viewModel = new SelectPackageWindowViewModel(sourceDir, new PackageVersionCache());

        // Assert: nothing selected yet
        Assert.False(viewModel.CanConfirm);
        Assert.False(viewModel.ConfirmCommand.CanExecute(null));

        // Act: select a name only (version auto-defaults, so this actually enables confirm -
        // verify the auto-default itself, then verify the "name only, no versions" case)
        viewModel.SelectedPackageName = "contoso-agents";
        Assert.True(viewModel.CanConfirm);

        // Act: clear the version explicitly
        viewModel.SelectedVersion = null;

        // Assert: no version selected means confirm is not available
        Assert.False(viewModel.CanConfirm);
        Assert.False(viewModel.ConfirmCommand.CanExecute(null));
    }

    /// <summary>
    ///     Test that executing ConfirmCommand raises Confirmed exactly once with the selected
    ///     (name, version) pair.
    /// </summary>
    [Fact]
    public void SelectPackageWindowViewModel_ConfirmCommand_RaisesConfirmedWithSelectedPair()
    {
        // Arrange
        var sourceDir = CreateTempDirectory();
        CreateFile(sourceDir, "contoso-agents-1.0.0.zip");
        var viewModel = new SelectPackageWindowViewModel(sourceDir, new PackageVersionCache());
        viewModel.SelectedPackageName = "contoso-agents";
        var raised = new List<(string Name, string Version)>();
        viewModel.Confirmed += (_, pair) => raised.Add(pair);

        // Act
        viewModel.ConfirmCommand.Execute(null);

        // Assert
        Assert.Single(raised);
        Assert.Equal("contoso-agents", raised[0].Name);
        Assert.Equal("1.0.0", raised[0].Version);
    }

    /// <summary>
    ///     Test that an empty source directory (no discoverable packages) yields an empty
    ///     PackageNames list and CanConfirm staying false.
    /// </summary>
    [Fact]
    public void SelectPackageWindowViewModel_EmptySource_EmptyPackageNamesAndCanConfirmFalse()
    {
        // Arrange
        var sourceDir = CreateTempDirectory();
        var viewModel = new SelectPackageWindowViewModel(sourceDir, new PackageVersionCache());

        // Assert
        Assert.Empty(viewModel.PackageNames);
        Assert.False(viewModel.CanConfirm);
    }

    /// <summary>
    ///     Creates a unique temporary directory tracked for cleanup in <see cref="Dispose"/>.
    /// </summary>
    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_select_package_vm_test_" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        return path;
    }

    /// <summary>
    ///     Creates an empty file with the given name inside a directory.
    /// </summary>
    private static void CreateFile(string directory, string fileName)
    {
        File.WriteAllText(Path.Combine(directory, fileName), string.Empty);
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
            catch (IOException)
            {
                // Best-effort cleanup; leftover temp directories do not fail the test.
            }
        }
    }
}
