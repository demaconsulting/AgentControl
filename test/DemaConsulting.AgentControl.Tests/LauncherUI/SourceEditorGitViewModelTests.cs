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
using DemaConsulting.AgentControl.GitIntegration;
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.Tests.AgentPackageManagement;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Unit tests for the Git-repository mode of <see cref="SourceEditorViewModel"/>: the title and
///     labels, the defaults, the validation of the address, tag pattern, folder and package name,
///     the definition it builds, and the connection test with its tag preview and failure reports.
/// </summary>
/// <remarks>
///     The connection test is driven through fake providers, so no git, repository or network is
///     involved; one test uses a real local repository to prove the editor's own provider is a Git
///     provider.
/// </remarks>
[Collection("RealProcess")]
public class SourceEditorGitViewModelTests
{
    /// <summary>
    ///     A valid https repository address used by most tests.
    /// </summary>
    private const string RepositoryAddress = "https://git.example.com/org/agents.git";

    /// <summary>
    ///     Test that a new Git editor has the Git title, the repository label and examples, the
    ///     default tag pattern, no folder, the connection test section and no NuGet parts.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_Constructor_NewGit_HasGitTitleLabelAndDefaults()
    {
        // Act
        var editor = CreateEditor();

        // Assert
        Assert.Equal("Add Git Repository", editor.Title);
        Assert.True(editor.IsGit);
        Assert.False(editor.IsNuGet);
        Assert.False(editor.IsServer);
        Assert.True(editor.ShowTestSection);
        Assert.Equal("Repository URL", editor.LocationLabel);
        Assert.Equal("https://host/org/repo.git", editor.LocationPlaceholder);
        Assert.Equal("v{version}", editor.TagPattern);
        Assert.Equal(string.Empty, editor.Subfolder);
        Assert.Equal(string.Empty, editor.GitPackageId);
        Assert.Null(editor.TagPatternError);
        Assert.Contains("https://host/org/repo.git", SourceEditorViewModel.GitLocationExamplesText, StringComparison.Ordinal);
        Assert.Contains("git@host:org/repo.git", SourceEditorViewModel.GitLocationExamplesText, StringComparison.Ordinal);
        Assert.Contains("case", SourceEditorViewModel.TagPatternHelpText, StringComparison.Ordinal);
        Assert.Contains("never stores credentials", SourceEditorViewModel.GitSignInHelpText, StringComparison.Ordinal);
        Assert.False(editor.CanSave);
        Assert.False(editor.HasTagPreview);
    }

    /// <summary>
    ///     Test that the sign-in note of a Git source says whose sign-in is used, that no credentials
    ///     are stored, where Git's sign-in may be shown and that background checks never prompt.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_GitSignInHelpText_ExplainsWhoSignsInAndWhenPromptsAppear()
    {
        // Arrange / Act
        var text = SourceEditorViewModel.GitSignInHelpText;

        // Assert: the note's four statements
        Assert.Contains("normal Git sign-in", text, StringComparison.Ordinal);
        Assert.Contains("never stores credentials", text, StringComparison.Ordinal);
        Assert.Contains("Test connection and Select Package may show Git's sign-in", text, StringComparison.Ordinal);
        Assert.Contains("background never prompt", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the explanation of the tag pattern gives examples and says that its literal parts
    ///     are case-sensitive.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_TagPatternHelpText_GivesExamplesAndSaysCaseMatters()
    {
        // Arrange / Act
        var text = SourceEditorViewModel.TagPatternHelpText;

        // Assert
        Assert.Contains("Examples: v{version} finds v1.2.3.", text, StringComparison.Ordinal);
        Assert.Contains("release/V{version} finds release/V1.2.3", text, StringComparison.Ordinal);
        Assert.Contains("upper and lower case, so V{version} does not find v1.2.3", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that editing a Git source shows the edit title, keeps the alias read-only and loads
    ///     the pattern, folder and package name of the source.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_Constructor_ExistingGit_LoadsFieldsAndKeepsAliasReadOnly()
    {
        // Arrange
        var existing = CreateDefinition();

        // Act
        var editor = CreateEditor(existing: existing);

        // Assert
        Assert.Equal("Edit Git Repository", editor.Title);
        Assert.False(editor.IsAliasEditable);
        Assert.Equal("Agents", editor.Alias);
        Assert.Equal(RepositoryAddress, editor.Location);
        Assert.Equal("release/{version}", editor.TagPattern);
        Assert.Equal("packaging/agents", editor.Subfolder);
        Assert.Equal("contoso-agents", editor.GitPackageId);
        Assert.True(editor.CanSave);
    }

    /// <summary>
    ///     Test that repository addresses that are not acceptable are reported with a message that
    ///     says what to do, block saving and testing, and never repeat a credential that was typed.
    /// </summary>
    /// <param name="location">The address typed.</param>
    /// <param name="expected">Text the message contains.</param>
    [Theory]
    [InlineData("   ", "Enter the address")]
    [InlineData("http://git.example.com/org/agents.git", "Only https and ssh")]
    [InlineData("ext::sh -c touch% /tmp/x", "Remote-helper")]
    [InlineData("-oProxyCommand=calc", "must not start with '-'")]
    [InlineData("agents.git", "relative or ambiguous")]
    [InlineData("https://user:sentinel-secret@git.example.com/org/agents.git", "must not contain a user name")]
    [InlineData("https://sentinel-secret@git.example.com/org/agents.git", "must not contain a user name")]
    [InlineData("https://git.example.com/org/agents.git?token=sentinel-secret", "must not contain a user name")]
    public void SourceEditorViewModel_GitLocation_Invalid_ReportsProblemAndBlocksSaveAndTest(string location, string expected)
    {
        // Arrange
        var editor = CreateEditor();
        editor.Alias = "Agents";

        // Act
        editor.Location = location;

        // Assert
        Assert.Contains(expected, editor.LocationError, StringComparison.Ordinal);
        Assert.DoesNotContain("sentinel-secret", editor.LocationError, StringComparison.Ordinal);
        Assert.False(editor.CanSave);
        Assert.False(editor.CanTestConnection);
    }

    /// <summary>
    ///     Test that the address forms the Git editor accepts (https, ssh in both spellings and a
    ///     local path) have no error and allow saving.
    /// </summary>
    /// <param name="location">The address typed.</param>
    [Theory]
    [InlineData(RepositoryAddress)]
    [InlineData("ssh://git@git.example.com/org/agents.git")]
    [InlineData("git@git.example.com:org/agents.git")]
    public void SourceEditorViewModel_GitLocation_Valid_IsAcceptedAndAllowsSave(string location)
    {
        // Arrange
        var editor = CreateEditor();
        editor.Alias = "Agents";

        // Act
        editor.Location = location;

        // Assert
        Assert.Null(editor.LocationError);
        Assert.True(editor.CanSave);
        Assert.True(editor.CanTestConnection);
    }

    /// <summary>
    ///     Test that a local repository path is accepted as a Git address.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_GitLocation_LocalPath_IsAccepted()
    {
        // Arrange
        var editor = CreateEditor();
        editor.Alias = "Agents";

        // Act
        editor.Location = Path.Combine(Path.GetTempPath(), "agent-packages.git");

        // Assert
        Assert.Null(editor.LocationError);
        Assert.True(editor.CanSave);
    }

    /// <summary>
    ///     Test that a tag pattern that is not acceptable is reported live, as soon as the user types,
    ///     without repeating it, and blocks saving and testing.
    /// </summary>
    /// <param name="pattern">The pattern typed.</param>
    /// <param name="expected">Text the message contains.</param>
    [Theory]
    [InlineData("", "Enter a tag pattern")]
    [InlineData("v", "exactly once")]
    [InlineData("v{version}-{version}", "exactly once")]
    [InlineData("v{Version}", "exactly once")]
    [InlineData("v{version}{", "braces")]
    [InlineData("v {version}", "may contain only letters")]
    [InlineData("v~{version}", "may contain only letters")]
    [InlineData("a..b/{version}", "valid Git tag name")]
    public void SourceEditorViewModel_TagPattern_Invalid_ReportsProblemLiveAndBlocksSaveAndTest(string pattern, string expected)
    {
        // Arrange
        var editor = CreateValidEditor();
        Assert.Null(editor.TagPatternError);

        // Act
        editor.TagPattern = pattern;

        // Assert
        Assert.Contains(expected, editor.TagPatternError, StringComparison.Ordinal);
        Assert.False(editor.CanSave);
        Assert.False(editor.CanTestConnection);
    }

    /// <summary>
    ///     Test that a folder inside the repository that is not acceptable (parent references, a drive,
    ///     the <c>.git</c> folder, wildcards) is reported without repeating it and blocks saving.
    /// </summary>
    /// <param name="subfolder">The folder typed.</param>
    [Theory]
    [InlineData("..")]
    [InlineData("packaging/../other")]
    [InlineData("packaging\\..\\other")]
    [InlineData("C:\\packaging")]
    [InlineData("C:/packaging")]
    [InlineData(".git")]
    [InlineData("packaging/.git/hooks")]
    [InlineData("pack*ing")]
    public void SourceEditorViewModel_Subfolder_Invalid_ReportsProblemAndBlocksSave(string subfolder)
    {
        // Arrange
        var editor = CreateValidEditor();
        Assert.Null(editor.SubfolderError);

        // Act
        editor.Subfolder = subfolder;

        // Assert
        Assert.Contains("relative path of folder names", editor.SubfolderError, StringComparison.Ordinal);
        Assert.DoesNotContain("packaging", editor.SubfolderError, StringComparison.Ordinal);
        Assert.False(editor.CanSave);
        Assert.False(editor.CanTestConnection);
    }

    /// <summary>
    ///     Test that a folder typed the Windows way (backslashes, leading or trailing separators) is
    ///     accepted and saved with plain slashes and no leading or trailing slash.
    /// </summary>
    /// <param name="typed">The folder typed.</param>
    [Theory]
    [InlineData("packaging\\agents")]
    [InlineData("\\packaging\\agents\\")]
    [InlineData("/packaging/agents/")]
    [InlineData("  packaging/agents  ")]
    public void SourceEditorViewModel_Subfolder_WindowsStyle_IsNormalizedWhenSaved(string typed)
    {
        // Arrange
        var editor = CreateValidEditor();
        editor.Subfolder = typed;
        PackageSourceDefinition? saved = null;
        editor.Saved += (_, definition) => saved = definition;

        // Act
        editor.SaveCommand.Execute(null);

        // Assert
        Assert.Null(editor.SubfolderError);
        Assert.NotNull(saved);
        Assert.Equal("packaging/agents", saved.Subfolder);
    }

    /// <summary>
    ///     Test that an empty package name defaults to the alias, shown as the box's placeholder and
    ///     used in the saved definition.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_GitPackageId_Empty_DefaultsToTheAlias()
    {
        // Arrange
        var editor = CreateValidEditor();
        editor.Alias = "my-agents";
        editor.GitPackageId = string.Empty;
        PackageSourceDefinition? saved = null;
        editor.Saved += (_, definition) => saved = definition;

        // Act
        editor.SaveCommand.Execute(null);

        // Assert
        Assert.Equal("my-agents", editor.GitPackageIdPlaceholder);
        Assert.Null(editor.GitPackageIdError);
        Assert.NotNull(saved);
        Assert.Equal(["my-agents"], saved.PackageIds);
    }

    /// <summary>
    ///     Test that an alias that is not a valid package name (it has a space) cannot stand in for
    ///     the package name: the editor asks for a name, blocks saving and testing, and accepts a
    ///     typed name.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_GitPackageId_AliasIsNotAValidName_RequiresAName()
    {
        // Arrange
        var editor = CreateValidEditor();

        // Act
        editor.Alias = "My Agents";
        editor.GitPackageId = string.Empty;

        // Assert: the alias cannot be used as the package name
        Assert.Contains("alias cannot be used", editor.GitPackageIdError, StringComparison.Ordinal);
        Assert.False(editor.CanSave);
        Assert.False(editor.CanTestConnection);

        // Act
        editor.GitPackageId = "my-agents";

        // Assert
        Assert.Null(editor.GitPackageIdError);
        Assert.True(editor.CanSave);
        Assert.True(editor.CanTestConnection);
    }

    /// <summary>
    ///     Test that typing only an alias that cannot be a package name already shows why the empty
    ///     package name box cannot stand in for it (Save would otherwise be disabled without a
    ///     visible reason), and that the box then suggests a typed name instead of the alias.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_GitPackageId_AliasTypedButNotAValidName_ExplainsAndSuggestsAName()
    {
        // Arrange: a new Git editor in which only the alias was typed
        var editor = new SourceEditorViewModel(PackageSourceType.Git, null, _ => false);
        Assert.Null(editor.GitPackageIdError);
        Assert.Equal("Defaults to the alias", editor.GitPackageIdPlaceholder);

        // Act
        editor.Alias = "Contoso Git";

        // Assert
        Assert.Contains("alias cannot be used", editor.GitPackageIdError, StringComparison.Ordinal);
        Assert.Equal("For example: contoso-agents", editor.GitPackageIdPlaceholder);
    }

    /// <summary>
    ///     Test that a typed package name with characters a package name cannot have is reported and
    ///     blocks saving.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_GitPackageId_InvalidCharacters_ReportsProblemAndBlocksSave()
    {
        // Arrange
        var editor = CreateValidEditor();

        // Act
        editor.GitPackageId = "bad name!";

        // Assert
        Assert.Contains("letters, digits", editor.GitPackageIdError, StringComparison.Ordinal);
        Assert.False(editor.CanSave);
    }

    /// <summary>
    ///     Test that saving needs a valid alias and address in addition to the Git fields, and that
    ///     Save does nothing until every rule is met.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_CanSave_Git_NeedsAliasAndAddress()
    {
        // Arrange
        var editor = CreateEditor();
        var saved = 0;
        editor.Saved += (_, _) => saved++;

        // Act / Assert: nothing typed
        Assert.False(editor.CanSave);
        editor.SaveCommand.Execute(null);
        Assert.Equal(0, saved);

        // Act / Assert: an alias alone is not enough
        editor.Alias = "Agents";
        Assert.False(editor.CanSave);

        // Act / Assert: an alias and an address is
        editor.Location = RepositoryAddress;
        Assert.True(editor.CanSave);
        editor.SaveCommand.Execute(null);
        Assert.Equal(1, saved);
    }

    /// <summary>
    ///     Test that the connection test can run before an alias is typed when a package name is
    ///     given (a placeholder alias is used), but not when the package name would have to come from
    ///     the missing alias.
    /// </summary>
    [Fact]
    public async Task SourceEditorViewModel_CanTestConnection_Git_DoesNotNeedAnAliasWhenThePackageNameIsGiven()
    {
        // Arrange
        PackageSourceDefinition? tested = null;
        var editor = CreateEditor(definition =>
        {
            tested = definition;
            return new FakeGitSourceProvider();
        });
        editor.Location = RepositoryAddress;

        // Assert: no alias and no package name: the name cannot be known
        Assert.False(editor.CanTestConnection);

        // Act: a package name makes the test possible
        editor.GitPackageId = "contoso-agents";
        Assert.True(editor.CanTestConnection);
        await editor.TestConnectionCommand.ExecuteAsync();

        // Assert: the provider was given a complete definition with a placeholder alias
        Assert.NotNull(tested);
        Assert.Equal(PackageSourceType.Git, tested.Type);
        Assert.Equal("Connection test", tested.Alias);
        Assert.Equal(["contoso-agents"], tested.PackageIds);
        Assert.Equal(RepositoryAddress, tested.Location);
        Assert.Equal("v{version}", tested.TagPattern);
    }

    /// <summary>
    ///     Test that the definition of a new Git source has the trimmed alias and address, the
    ///     pattern, the normalized folder and the effective package name, and no NuGet settings.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_Save_NewGit_BuildsTheGitDefinition()
    {
        // Arrange
        var editor = CreateEditor();
        editor.Alias = "Agents";
        editor.Location = "  " + RepositoryAddress + "  ";
        editor.TagPattern = " release/{version} ";
        editor.Subfolder = "packaging\\agents";
        editor.GitPackageId = "contoso-agents";
        PackageSourceDefinition? saved = null;
        editor.Saved += (_, definition) => saved = definition;

        // Act
        editor.SaveCommand.Execute(null);

        // Assert
        Assert.NotNull(saved);
        Assert.Equal(PackageSourceType.Git, saved.Type);
        Assert.Equal("Agents", saved.Alias);
        Assert.Equal(RepositoryAddress, saved.Location);
        Assert.Equal("release/{version}", saved.TagPattern);
        Assert.Equal("packaging/agents", saved.Subfolder);
        Assert.Equal(["contoso-agents"], saved.PackageIds);
        Assert.False(saved.AllowInsecureHttp);
        Assert.False(saved.RequireSignedPackages);
        Assert.Null(saved.SignerFingerprint);
        Assert.Null(saved.GetGitSettingsProblem());
    }

    /// <summary>
    ///     Test that saving an edited Git source keeps its alias, applies the edits and leaves the
    ///     folder empty (null) when it was cleared.
    /// </summary>
    [Fact]
    public void SourceEditorViewModel_Save_ExistingGit_KeepsAliasAndAppliesEdits()
    {
        // Arrange
        var editor = CreateEditor(existing: CreateDefinition());
        editor.Location = "ssh://git@git.example.com/org/agents.git";
        editor.TagPattern = "v{version}";
        editor.Subfolder = string.Empty;
        editor.GitPackageId = "other-agents";
        PackageSourceDefinition? saved = null;
        editor.Saved += (_, definition) => saved = definition;

        // Act
        editor.SaveCommand.Execute(null);

        // Assert
        Assert.NotNull(saved);
        Assert.Equal("Agents", saved.Alias);
        Assert.Equal("ssh://git@git.example.com/org/agents.git", saved.Location);
        Assert.Equal("v{version}", saved.TagPattern);
        Assert.Null(saved.Subfolder);
        Assert.Equal(["other-agents"], saved.PackageIds);
    }

    /// <summary>
    ///     Test that a connection test shows the matching tags newest first, with the tag name of each
    ///     version, a summary of how many tags match and how many do not, and a success headline.
    /// </summary>
    [Fact]
    public async Task SourceEditorViewModel_TestConnection_Git_ShowsMatchingTagsAndSummary()
    {
        // Arrange
        var provider = new FakeGitSourceProvider
        {
            Preview = new GitTagPreview(
                5,
                [FakeGitSourceProvider.Version("1.1.0", "release/1.1.0"), FakeGitSourceProvider.Version("1.0.0", "release/1.0.0")],
                3,
                ["latest", "nightly", "v1.0.0"])
        };
        var editor = CreateValidEditor(_ => provider);

        // Act
        await editor.TestConnectionCommand.ExecuteAsync();

        // Assert
        Assert.True(editor.TestSucceeded);
        Assert.Equal("Connection OK", editor.TestResultTitle);
        Assert.Contains("Found 2 versions of 'agents'", editor.TestResultDetail, StringComparison.Ordinal);
        Assert.Contains("latest 1.1.0", editor.TestResultDetail, StringComparison.Ordinal);
        Assert.Equal(["1.1.0   (tag release/1.1.0)", "1.0.0   (tag release/1.0.0)"], editor.TagPreviewItems);
        Assert.Equal("2 of 5 tags match the pattern. 3 other tags do not match.", editor.TagPreviewSummary);
        Assert.True(editor.HasTagPreview);
        Assert.Equal(1, provider.PreviewCalls);
    }

    /// <summary>
    ///     Test that a preview of more matching tags than fit lists the newest ten and says so.
    /// </summary>
    [Fact]
    public async Task SourceEditorViewModel_TestConnection_Git_ManyTags_ListsTheNewestTen()
    {
        // Arrange
        var versions = Enumerable.Range(1, 12)
            .Reverse()
            .Select(minor => FakeGitSourceProvider.Version($"1.{minor}.0", $"v1.{minor}.0"))
            .ToList();
        var provider = new FakeGitSourceProvider { Preview = new GitTagPreview(12, versions, 0, []) };
        var editor = CreateValidEditor(_ => provider);

        // Act
        await editor.TestConnectionCommand.ExecuteAsync();

        // Assert
        Assert.Equal(10, editor.TagPreviewItems.Count);
        Assert.Equal("1.12.0   (tag v1.12.0)", editor.TagPreviewItems[0]);
        Assert.Equal("12 of 12 tags match the pattern. Showing the newest 10.", editor.TagPreviewSummary);
    }

    /// <summary>
    ///     Test that when no tag matches but the repository has tags, the result says no tag matched,
    ///     names some of the tags the repository does have (so a capitalization mistake is easy to
    ///     see), mentions that the fixed parts are case-sensitive, and shows no preview list.
    /// </summary>
    [Fact]
    public async Task SourceEditorViewModel_TestConnection_Git_NoTagMatches_ShowsSampleTagsAndCaseHint()
    {
        // Arrange: the repository uses a capital V, the pattern a small one
        var provider = new FakeGitSourceProvider { Preview = new GitTagPreview(8, [], 8, ["V1.0.0", "V1.1.0", "latest"]) };
        var editor = CreateValidEditor(_ => provider);

        // Act
        await editor.TestConnectionCommand.ExecuteAsync();

        // Assert
        Assert.True(editor.TestFailed);
        Assert.Equal("No matching tags", editor.TestResultTitle);
        Assert.Contains("has 8 tags", editor.TestResultDetail, StringComparison.Ordinal);
        Assert.Contains("V1.0.0, V1.1.0, latest, ...", editor.TestResultDetail, StringComparison.Ordinal);
        Assert.Contains("case-sensitive", editor.TestResultDetail, StringComparison.Ordinal);
        Assert.False(editor.HasTagPreview);
        Assert.Empty(editor.TagPreviewItems);
    }

    /// <summary>
    ///     Test that a repository with no tags at all is reported as having no tags yet, with the
    ///     way to make one.
    /// </summary>
    [Fact]
    public async Task SourceEditorViewModel_TestConnection_Git_NoTagsAtAll_SaysTheRepositoryHasNoTagsYet()
    {
        // Arrange
        var editor = CreateValidEditor(_ => new FakeGitSourceProvider());

        // Act
        await editor.TestConnectionCommand.ExecuteAsync();

        // Assert
        Assert.True(editor.TestFailed);
        Assert.Equal("No matching tags", editor.TestResultTitle);
        Assert.Contains("no tags yet", editor.TestResultDetail, StringComparison.Ordinal);
        Assert.Contains("git tag v1.0.0", editor.TestResultDetail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that each way the repository can fail is reported with its own headline and the
    ///     message the provider gave.
    /// </summary>
    /// <param name="kindName">The failure cause the provider reports.</param>
    /// <param name="expectedTitle">The headline expected.</param>
    [Theory]
    [InlineData("AuthenticationFailed", "Sign-in needed")]
    [InlineData("NotFound", "Repository not found")]
    [InlineData("Unreachable", "Repository unreachable")]
    [InlineData("TimedOut", "Timed out")]
    public async Task SourceEditorViewModel_TestConnection_Git_ProviderFails_ReportsHeadlineAndMessage(
        string kindName, string expectedTitle)
    {
        // Arrange
        var kind = Enum.Parse<PackageSourceFailureKind>(kindName);
        var provider = new FakeGitSourceProvider { PreviewException = new PackageSourceAccessException(kind, "the provider's message") };
        var editor = CreateValidEditor(_ => provider);

        // Act
        await editor.TestConnectionCommand.ExecuteAsync();

        // Assert
        Assert.True(editor.TestFailed);
        Assert.Equal(expectedTitle, editor.TestResultTitle);
        Assert.Equal("the provider's message", editor.TestResultDetail);
        Assert.False(editor.HasTagPreview);
    }

    /// <summary>
    ///     Test that a git that is too old is reported as such, not as a sign-in or network problem.
    /// </summary>
    [Fact]
    public async Task SourceEditorViewModel_TestConnection_Git_GitTooOld_ReportsGitIsTooOld()
    {
        // Arrange
        var provider = new FakeGitSourceProvider
        {
            PreviewException = new GitRemoteException(GitFailureKind.GitTooOld, "Git 2.20 is too old; install Git 2.30 or newer.")
        };
        var editor = CreateValidEditor(_ => provider);

        // Act
        await editor.TestConnectionCommand.ExecuteAsync();

        // Assert
        Assert.True(editor.TestFailed);
        Assert.Equal("Git is too old", editor.TestResultTitle);
        Assert.Equal("Git 2.20 is too old; install Git 2.30 or newer.", editor.TestResultDetail);
    }

    /// <summary>
    ///     Test that a configuration error when the provider is created is reported in the dialog.
    /// </summary>
    [Fact]
    public async Task SourceEditorViewModel_TestConnection_Git_OtherFailure_ReportsConnectionFailed()
    {
        // Arrange
        var editor = CreateValidEditor(_ => throw new InvalidOperationException("The cache root is not usable."));

        // Act
        await editor.TestConnectionCommand.ExecuteAsync();

        // Assert
        Assert.True(editor.TestFailed);
        Assert.Equal("Connection failed", editor.TestResultTitle);
        Assert.Equal("The cache root is not usable.", editor.TestResultDetail);
    }

    /// <summary>
    ///     Test that canceling a running Git test ends it with a neutral "canceled" result and no
    ///     preview, and that the progress text speaks of the repository.
    /// </summary>
    [Fact]
    public async Task SourceEditorViewModel_TestConnection_Git_CancelWhileRunning_StopsWithCanceledResult()
    {
        // Arrange
        var release = new TaskCompletionSource();
        var provider = new FakeGitSourceProvider { Gate = release.Task };
        var editor = CreateValidEditor(_ => provider);

        // Act
        var running = editor.TestConnectionCommand.ExecuteAsync();

        // Assert: progress is shown and Cancel offered
        Assert.True(editor.IsTesting);
        Assert.Contains("listing its tags", editor.TestProgressText, StringComparison.Ordinal);
        Assert.True(editor.CancelTestCommand.CanExecute(null));

        // Act
        editor.CancelTestCommand.Execute(null);
        await running;

        // Assert
        Assert.False(editor.IsTesting);
        Assert.True(editor.TestWasCanceled);
        Assert.Equal("Test canceled", editor.TestResultTitle);
        Assert.False(editor.HasTagPreview);
        release.SetResult();
    }

    /// <summary>
    ///     Test that when any input of a Git test (the address, the tag pattern, the folder or the package
    ///     name) is edited while the test runs, the result of the old test - even from a provider that
    ///     ignores cancellation - is never shown, because it describes inputs that are no longer in the
    ///     dialog.
    /// </summary>
    /// <param name="field">The input that is edited while the test runs.</param>
    [Theory]
    [InlineData("Location")]
    [InlineData("TagPattern")]
    [InlineData("Subfolder")]
    [InlineData("GitPackageId")]
    public async Task SourceEditorViewModel_TestConnection_Git_EditedWhileTesting_StaleResultIsNotShown(string field)
    {
        // Arrange
        var release = new TaskCompletionSource();
        var provider = new FakeGitSourceProvider
        {
            Gate = release.Task,
            IgnoreCancellation = true,
            Preview = new GitTagPreview(1, [FakeGitSourceProvider.Version("1.0.0", "v1.0.0")], 0, [])
        };
        var editor = CreateValidEditor(_ => provider);
        var running = editor.TestConnectionCommand.ExecuteAsync();
        Assert.True(editor.IsTesting);

        // Act: an edit of one input mid-test, then the old test finishes
        switch (field)
        {
            case "Location":
                editor.Location = "https://git.example.com/other/agents.git";
                break;
            case "TagPattern":
                editor.TagPattern = "release/{version}";
                break;
            case "Subfolder":
                editor.Subfolder = "packaging";
                break;
            default:
                editor.GitPackageId = "other-agents";
                break;
        }

        release.SetResult();
        await running;

        // Assert: neither the result nor the preview of the old test is shown
        Assert.False(editor.IsTesting);
        Assert.False(editor.HasTestResult);
        Assert.False(editor.HasTagPreview);
        Assert.Empty(editor.TagPreviewItems);
        Assert.Equal(string.Empty, editor.TagPreviewSummary);
    }

    /// <summary>
    ///     Test that the result and the preview of a finished test are dropped when the tag pattern is
    ///     changed, because they described the old pattern.
    /// </summary>
    [Fact]
    public async Task SourceEditorViewModel_TagPattern_ChangedAfterTest_ClearsResultAndPreview()
    {
        // Arrange
        var provider = new FakeGitSourceProvider
        {
            Preview = new GitTagPreview(1, [FakeGitSourceProvider.Version("1.0.0", "v1.0.0")], 0, [])
        };
        var editor = CreateValidEditor(_ => provider);
        await editor.TestConnectionCommand.ExecuteAsync();
        Assert.True(editor.HasTagPreview);

        // Act
        editor.TagPattern = "release/{version}";

        // Assert
        Assert.False(editor.HasTestResult);
        Assert.False(editor.HasTagPreview);
        Assert.Empty(editor.TagPreviewItems);
        Assert.Equal(string.Empty, editor.TagPreviewSummary);
    }

    /// <summary>
    ///     Test that a provider that cannot preview (it is not a Git provider) still gives a preview,
    ///     made from its list of versions of the effective package name.
    /// </summary>
    [Fact]
    public async Task SourceEditorViewModel_TestConnection_Git_ProviderWithoutPreview_FallsBackToItsVersions()
    {
        // Arrange
        var provider = new FakePackageSourceProvider("any", ("agents", "1.0.0"), ("agents", "1.1.0"), ("other", "9.0.0"));
        var editor = CreateValidEditor(_ => provider);

        // Act
        await editor.TestConnectionCommand.ExecuteAsync();

        // Assert
        Assert.True(editor.TestSucceeded);
        Assert.Equal(1, provider.ListVersionsCalls);
        Assert.Equal(["1.1.0   (tag 1.1.0)", "1.0.0   (tag 1.0.0)"], editor.TagPreviewItems);
        Assert.Equal("2 of 2 tags match the pattern.", editor.TagPreviewSummary);
    }

    /// <summary>
    ///     Test that an editor created without a provider factory tests a Git source through a real
    ///     Git provider (not a NuGet one): a local repository with one version tag and one other tag
    ///     gives a preview of the matching tag.
    /// </summary>
    [Fact]
    public async Task SourceEditorViewModel_TestConnection_Git_DefaultProviderFactory_UsesGitProvider()
    {
        // Arrange
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        fixture.Commit(new Dictionary<string, string> { [".github/copilot-instructions.md"] = "marker" }, "one");
        fixture.Tag("v1.0.0");
        fixture.Tag("latest");
        fixture.Push();
        var editor = new SourceEditorViewModel(PackageSourceType.Git, null, _ => false)
        {
            Alias = "Agents",
            Location = fixture.RemotePath
        };

        // Act
        await editor.TestConnectionCommand.ExecuteAsync();

        // Assert
        Assert.True(editor.TestSucceeded, editor.TestResultTitle + ": " + editor.TestResultDetail);
        Assert.Equal(["1.0.0   (tag v1.0.0)"], editor.TagPreviewItems);
        Assert.Equal("1 of 2 tags match the pattern. 1 other tag does not match.", editor.TagPreviewSummary);
    }

    /// <summary>
    ///     Creates a new Git editor with no aliases taken.
    /// </summary>
    /// <param name="providerFactory">Creates the provider the connection test uses, or
    ///     <see langword="null"/> for the default.</param>
    /// <param name="existing">The source to edit, or <see langword="null"/> for a new one.</param>
    /// <returns>The editor.</returns>
    private static SourceEditorViewModel CreateEditor(
        Func<PackageSourceDefinition, IPackageSourceProvider>? providerFactory = null,
        PackageSourceDefinition? existing = null) =>
        new(PackageSourceType.Git, existing, _ => false, providerFactory: providerFactory);

    /// <summary>
    ///     Creates a new Git editor with a valid alias, address and package name.
    /// </summary>
    /// <param name="providerFactory">Creates the provider the connection test uses, or
    ///     <see langword="null"/> for the default.</param>
    /// <returns>The editor.</returns>
    private static SourceEditorViewModel CreateValidEditor(
        Func<PackageSourceDefinition, IPackageSourceProvider>? providerFactory = null)
    {
        var editor = CreateEditor(providerFactory);
        editor.Alias = "Agents";
        editor.Location = RepositoryAddress;
        editor.GitPackageId = "agents";
        return editor;
    }

    /// <summary>
    ///     Creates a saved Git source with every Git field set.
    /// </summary>
    /// <returns>The definition.</returns>
    private static PackageSourceDefinition CreateDefinition() => new()
    {
        Alias = "Agents",
        Type = PackageSourceType.Git,
        Location = RepositoryAddress,
        TagPattern = "release/{version}",
        Subfolder = "packaging/agents",
        PackageIds = ["contoso-agents"]
    };
}
