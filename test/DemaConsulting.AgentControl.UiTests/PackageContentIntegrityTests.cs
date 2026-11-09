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

using System.Security.Cryptography;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     End-to-end tests of package content integrity: the pin records which bytes a package version
///     delivered, and a version that is later republished with different content is not synced
///     silently.
/// </summary>
public sealed class PackageContentIntegrityTests
{
    /// <summary>
    ///     A republished package version (same version, different zip bytes) must not be re-synced
    ///     at launch: the launch tells the user the agent files were not synced, extracts nothing
    ///     and leaves the pin file exactly as it was.
    /// </summary>
    [Fact]
    public void LaunchButton_Click_PinnedVersionRepublished_ExplainsAndSyncsNothing()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        const string packageName = "contoso-agents";
        var sourceDirectory = Path.Combine(Path.GetTempPath(), "AgentControlUiTests-source-" + Guid.NewGuid());

        try
        {
            // Arrange: a repo pinned to 1.0.0 with the identity of the original zip, whose managed
            // folders are missing, and a source whose 1.0.0 zip has since been republished
            var originalZip = FixturePackageBuilder.Create(sourceDirectory, packageName, "1.0.0", "# original\n");
            var identity = Sha256Identity(originalZip);
            FixturePackageBuilder.Create(sourceDirectory, packageName, "1.0.0", "# republished with other content\n");

            using var context = new AgentControlTestContext(
                packageSourcePath: sourceDirectory,
                pinnedPackageName: packageName,
                pinnedPackageVersion: "1.0.0",
                pinnedSource: "Default",
                pinnedContentIdentity: identity);
            var pinPath = Path.Combine(context.RepoPath, ".agentcontrol.json");
            var pinBefore = File.ReadAllText(pinPath);
            var mainWindow = context.Launch();

            // Act: launch the agent tool, which first tries to restore the missing agent files
            var launchButton = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("LaunchButton"))?.AsButton(),
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200)).Result;
            Assert.NotNull(launchButton);
            launchButton.Invoke();

            // Assert: the refusal is explained in plain language, nothing was extracted, and the
            // pin is untouched
            var messageWindow = context.WaitForWindowWithDescendant("MessageText", TimeSpan.FromSeconds(30));
            Assert.NotNull(messageWindow);
            var message = messageWindow.FindFirstDescendant(cf => cf.ByAutomationId("MessageText"))?.Name;
            Assert.NotNull(message);
            Assert.Contains("not synced", message, StringComparison.Ordinal);
            Assert.Contains("now contains different files", message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(context.RepoPath, ".github")));
            Assert.Equal(pinBefore, File.ReadAllText(pinPath));
        }
        finally
        {
            TryDelete(sourceDirectory);
        }
    }

    /// <summary>
    ///     Selecting a package for a never-pinned repo must record the SHA-256 of the package zip in
    ///     the repo's pin file, so a later republished copy of the version can be told apart.
    /// </summary>
    [Fact]
    public void SelectPackageMenuItem_Click_RecordsSha256OfZipInPinFile()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        const string packageName = "contoso-agents";
        var sourceDirectory = Path.Combine(Path.GetTempPath(), "AgentControlUiTests-source-" + Guid.NewGuid());

        try
        {
            var zip = FixturePackageBuilder.Create(sourceDirectory, packageName, "1.0.0", "# v1.0.0\n");
            using var context = new AgentControlTestContext(packageSourcePath: sourceDirectory);
            var mainWindow = context.Launch();

            // Act: choose the package in the Select Package dialog
            mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("MoreActionsButton"))?.AsButton()?.Invoke();
            var selectPackageMenuItem = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("SelectPackageMenuItem")),
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200)).Result?.AsMenuItem();
            Assert.NotNull(selectPackageMenuItem);
            selectPackageMenuItem.Click();
            var dialog = context.WaitForWindowWithDescendant("SelectPackageNameList", TimeSpan.FromSeconds(15));
            Assert.NotNull(dialog);
            var nameItem = Retry.WhileNull(
                () => dialog.FindFirstDescendant(cf => cf.ByAutomationId("SelectPackageNameList"))
                    ?.FindFirstDescendant(cf => cf.ByName(packageName)),
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200)).Result?.AsListBoxItem();
            Assert.NotNull(nameItem);
            nameItem.Select();
            var confirmButton = Retry.WhileNull(
                () => dialog.FindFirstDescendant(cf => cf.ByAutomationId("SelectPackageConfirmButton")),
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200)).Result?.AsButton();
            Assert.NotNull(confirmButton);
            Assert.True(Retry.WhileFalse(
                () => confirmButton.IsEnabled,
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200)).Result);
            confirmButton.Invoke();
            Assert.NotNull(context.WaitForWindow(
                title => title.StartsWith("Release Notes", StringComparison.Ordinal),
                TimeSpan.FromSeconds(15)));

            // Assert: the pin names the identity of the zip that was applied
            using var pin = JsonDocument.Parse(File.ReadAllText(Path.Combine(context.RepoPath, ".agentcontrol.json")));
            Assert.Equal(Sha256Identity(zip), pin.RootElement.GetProperty("ContentIdentity").GetString());
            Assert.Equal(3, pin.RootElement.GetProperty("SchemaVersion").GetInt32());
        }
        finally
        {
            TryDelete(sourceDirectory);
        }
    }

    /// <summary>
    ///     Computes the <c>sha256:</c> content identity of a file independently of the application.
    /// </summary>
    /// <param name="path">The file to hash.</param>
    /// <returns>The identity text.</returns>
    private static string Sha256Identity(string path) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    /// <summary>
    ///     Best-effort recursive delete of a temporary fixture directory.
    /// </summary>
    /// <param name="path">The directory to delete.</param>
    private static void TryDelete(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup only.
        }
    }
}
