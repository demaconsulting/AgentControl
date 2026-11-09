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
using Avalonia.Threading;
using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.Tests.AgentPackageManagement;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Headless tests of the Git-repository mode of <see cref="SourceEditorWindow"/>: which sections
///     are shown for a Git source and which are hidden, the tag pattern, folder and package name
///     boxes, the sign-in note, the tag preview after a connection test, and saving.
/// </summary>
public class SourceEditorGitWindowTests
{
    /// <summary>
    ///     A valid https repository address.
    /// </summary>
    private const string RepositoryAddress = "https://git.example.com/org/agents.git";

    /// <summary>
    ///     Test that the Git editor shows the package, versions, folder, sign-in and connection test
    ///     sections with their boxes and the sign-in note, and hides the sections that only apply to
    ///     NuGet sources.
    /// </summary>
    [Fact]
    public void SourceEditorWindow_GitSource_ShowsGitSectionsAndHidesNuGetOnes() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var window = new SourceEditorWindow(CreateEditor());
        window.Show();
        try
        {
            // Assert: the Git boxes exist, with their defaults
            Assert.Equal("v{version}", window.FindControl<TextBox>("SourceTagPatternTextBox")!.Text);
            Assert.Equal(string.Empty, window.FindControl<TextBox>("SourceSubfolderTextBox")!.Text);
            Assert.True(window.FindControl<TextBox>("SourceGitPackageIdTextBox")!.IsVisible);
            Assert.Equal("Agents", window.FindControl<TextBox>("SourceGitPackageIdTextBox")!.PlaceholderText);
            Assert.Equal(SourceEditorViewModel.GitSignInHelpText, window.FindControl<TextBlock>("SourceGitSignInHelpText")!.Text);
            Assert.Equal(SourceEditorViewModel.TagPatternHelpText, window.FindControl<TextBlock>("SourceTagPatternHelpText")!.Text);

            // Assert: the Git sections and the connection test are shown, the NuGet ones are not
            Assert.True(window.FindControl<StackPanel>("SourceEditorTestSection")!.IsVisible);
            Assert.True(window.FindControl<StackPanel>("SourceEditorGitSignInSection")!.IsVisible);
            Assert.False(window.FindControl<StackPanel>("SourceEditorPackageIdsSection")!.IsVisible);
            Assert.False(window.FindControl<StackPanel>("SourceEditorSignatureSection")!.IsVisible);
            Assert.False(window.FindControl<StackPanel>("SourceEditorSignInSection")!.IsVisible);
            Assert.Equal("Repository URL", window.FindControl<TextBlock>("SourceLocationLabel")!.Text);
            var examples = window.FindControl<TextBlock>("SourceGitLocationExamplesText")!;
            Assert.True(examples.IsVisible);
            Assert.Equal(SourceEditorViewModel.GitLocationExamplesText, examples.Text);
            Assert.False(window.FindControl<TextBlock>("SourceLocationExamplesText")!.IsVisible);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that a bad tag pattern shows its error under the box and disables Save, and that
    ///     fixing it hides the error again.
    /// </summary>
    [Fact]
    public void SourceEditorWindow_GitSource_BadTagPattern_ShowsErrorAndDisablesSave() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var window = new SourceEditorWindow(CreateEditor());
        window.Show();
        try
        {
            var pattern = window.FindControl<TextBox>("SourceTagPatternTextBox")!;
            var error = window.FindControl<TextBlock>("SourceTagPatternErrorText")!;
            var save = window.FindControl<Button>("SourceEditorSaveButton")!;
            Assert.False(error.IsVisible);
            Assert.True(save.IsEffectivelyEnabled);

            // Act
            pattern.Text = "v";

            // Assert
            Assert.True(error.IsVisible);
            Assert.Contains("exactly once", error.Text, StringComparison.Ordinal);
            Assert.False(save.IsEffectivelyEnabled);

            // Act
            pattern.Text = "release/{version}";

            // Assert
            Assert.False(error.IsVisible);
            Assert.True(save.IsEffectivelyEnabled);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that clicking Test connection lists the matching tags in the preview list under the
    ///     result, with the summary above it.
    /// </summary>
    [Fact]
    public void SourceEditorWindow_GitSource_TestConnection_ShowsTagPreviewList() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var provider = new FakeGitSourceProvider
        {
            Preview = new GitTagPreview(
                3,
                [FakeGitSourceProvider.Version("1.1.0", "v1.1.0"), FakeGitSourceProvider.Version("1.0.0", "v1.0.0")],
                1,
                ["latest"])
        };
        var editor = CreateEditor(_ => provider);
        var window = new SourceEditorWindow(editor);
        window.Show();
        try
        {
            var panel = window.FindControl<StackPanel>("SourceTagPreviewPanel")!;
            Assert.False(panel.IsVisible);

            // Act
            DialogWindowTests.Click(window, "SourceTestConnectionButton");
            await WaitUntilAsync(() => editor.HasTestResult);
            Dispatcher.UIThread.RunJobs();

            // Assert
            Assert.Equal("Connection OK", window.FindControl<TextBlock>("SourceTestResultTitleText")!.Text);
            Assert.True(panel.IsVisible);
            Assert.Equal(
                "2 of 3 tags match the pattern. 1 other tag does not match.",
                window.FindControl<TextBlock>("SourceTagPreviewSummaryText")!.Text);
            var list = window.FindControl<ListBox>("SourceTagPreviewList")!;
            Assert.Equal(
                ["1.1.0   (tag v1.1.0)", "1.0.0   (tag v1.0.0)"],
                list.Items.Cast<string>().ToList());
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that Save closes the dialog with the Git definition the boxes describe.
    /// </summary>
    [Fact]
    public void SourceEditorWindow_GitSource_Save_ClosesWithTheGitDefinition() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var owner = new Window();
        owner.Show();
        try
        {
            var window = new SourceEditorWindow(CreateEditor());
            var result = window.ShowDialog<PackageSourceDefinition?>(owner);
            window.FindControl<TextBox>("SourceTagPatternTextBox")!.Text = "release/{version}";
            window.FindControl<TextBox>("SourceSubfolderTextBox")!.Text = "packaging\\agents";
            window.FindControl<TextBox>("SourceGitPackageIdTextBox")!.Text = "contoso-agents";

            // Act
            DialogWindowTests.Click(window, "SourceEditorSaveButton");
            var saved = await result;

            // Assert
            Assert.NotNull(saved);
            Assert.Equal(PackageSourceType.Git, saved.Type);
            Assert.Equal("Agents", saved.Alias);
            Assert.Equal(RepositoryAddress, saved.Location);
            Assert.Equal("release/{version}", saved.TagPattern);
            Assert.Equal("packaging/agents", saved.Subfolder);
            Assert.Equal(["contoso-agents"], saved.PackageIds);
        }
        finally
        {
            owner.Close();
        }
    });

    /// <summary>
    ///     Creates a new Git editor with an alias and address filled in.
    /// </summary>
    /// <param name="providerFactory">Creates the provider the connection test uses, or
    ///     <see langword="null"/> for the default.</param>
    /// <returns>The editor.</returns>
    private static SourceEditorViewModel CreateEditor(Func<PackageSourceDefinition, IPackageSourceProvider>? providerFactory = null) =>
        new(PackageSourceType.Git, null, _ => false, providerFactory: providerFactory)
        {
            Alias = "Agents",
            Location = RepositoryAddress
        };

    /// <summary>
    ///     Polls on the UI thread until a condition holds, processing queued UI work meanwhile.
    /// </summary>
    /// <param name="condition">The condition to wait for.</param>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 1200 && !condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }
}
