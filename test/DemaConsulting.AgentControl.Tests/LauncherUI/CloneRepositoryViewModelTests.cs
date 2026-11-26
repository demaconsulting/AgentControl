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

using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.Utilities;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Unit tests for <see cref="CloneRepositoryViewModel"/>: the address and folder rules, the folder
///     name that follows the address, the group choices, the destination check that runs off the UI
///     thread, and how the dialog ends.
/// </summary>
/// <remarks>
///     The destination check reads the real file system (a temporary folder per test); no git is
///     started and no Avalonia window is constructed.
/// </remarks>
public sealed class CloneRepositoryViewModelTests : IDisposable
{
    /// <summary>
    ///     Folders to delete when the test ends.
    /// </summary>
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     Test that Clone cannot be pressed while the address, the parent folder or the folder name is
    ///     missing, and that an empty field is not shown as an error.
    /// </summary>
    [Fact]
    public void CloneRepositoryViewModel_EmptyUrl_CannotAccept()
    {
        // Arrange: nothing typed yet
        var viewModel = new CloneRepositoryViewModel([], CreateTempDirectory());

        // Assert: disabled, and quiet
        Assert.False(viewModel.CanAccept);
        Assert.False(viewModel.AcceptCommand.CanExecute(null));
        Assert.False(viewModel.HasError);

        // Act: an address makes the folder name follow it
        viewModel.RepositoryUrl = "https://example.com/owner/repo.git";

        // Assert
        Assert.True(viewModel.CanAccept);
        Assert.True(viewModel.AcceptCommand.CanExecute(null));

        // Act: a blank parent folder, then a blank folder name
        var parent = viewModel.ParentFolder;
        viewModel.ParentFolder = " ";
        var withoutParent = viewModel.CanAccept;
        viewModel.ParentFolder = parent;
        viewModel.FolderName = string.Empty;

        // Assert
        Assert.False(withoutParent);
        Assert.False(viewModel.CanAccept);
        Assert.False(viewModel.HasError);
    }

    /// <summary>
    ///     Test that an address git would read as an option, a remote-helper address and a multi-line
    ///     address are refused with a message, and that Clone stays disabled.
    /// </summary>
    [Theory]
    [InlineData("-u=evil")]
    [InlineData("--upload-pack=calc.exe")]
    [InlineData("ext::sh -c touch% /tmp/pwned")]
    [InlineData("fd::17/repo")]
    [InlineData("https://example.com/a.git\nhttps://example.com/b.git")]
    public void CloneRepositoryViewModel_UrlStartingWithDashOrHelperOrMultiline_IsRefused(string url)
    {
        // Arrange
        var viewModel = new CloneRepositoryViewModel([], CreateTempDirectory());

        // Act
        viewModel.RepositoryUrl = url;

        // Assert
        Assert.True(viewModel.HasError);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.ErrorText));
        Assert.False(viewModel.CanAccept);
        Assert.False(viewModel.AcceptCommand.CanExecute(null));
    }

    /// <summary>
    ///     Test that the folder name follows the address - the last segment without <c>.git</c> - for
    ///     URLs, scp-like addresses and local paths.
    /// </summary>
    [Theory]
    [InlineData("https://github.com/owner/repo.git", "repo")]
    [InlineData("https://github.com/owner/repo", "repo")]
    [InlineData("https://github.com/owner/repo/", "repo")]
    [InlineData("https://github.com/owner/repo.git/", "repo")]
    [InlineData("https://github.com/owner/Repo.GIT", "Repo")]
    [InlineData("https://github.com/owner/repo.git?ref=main#frag", "repo")]
    [InlineData("git@github.com:owner/repo.git", "repo")]
    [InlineData("git@example.com:repo.git", "repo")]
    [InlineData("ssh://git@example.com:2222/team/proj.git", "proj")]
    [InlineData(@"C:\repos\origin.git", "origin")]
    [InlineData("/srv/git/origin.git", "origin")]
    [InlineData("  https://example.com/x/y.git  ", "y")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void CloneRepositoryViewModel_DeriveFolderName_FollowsTheLastSegment(string? url, string expected)
    {
        // Act / Assert
        Assert.Equal(expected, CloneRepositoryViewModel.DeriveFolderName(url));
    }

    /// <summary>
    ///     Test that the folder name follows the address until the user types a name, and then stays.
    /// </summary>
    [Fact]
    public void CloneRepositoryViewModel_FolderName_FollowsUrlUntilEdited()
    {
        // Arrange
        var viewModel = new CloneRepositoryViewModel([], CreateTempDirectory());

        // Act: the address is typed, then changed
        viewModel.RepositoryUrl = "https://example.com/team/first.git";
        var first = viewModel.FolderName;
        viewModel.RepositoryUrl = "https://example.com/team/second.git";
        var second = viewModel.FolderName;

        // Act: the user chooses a name; the address changes again
        viewModel.FolderName = "my-copy";
        viewModel.RepositoryUrl = "https://example.com/team/third.git";

        // Assert
        Assert.Equal("first", first);
        Assert.Equal("second", second);
        Assert.Equal("my-copy", viewModel.FolderName);
        Assert.Equal(Path.Combine(viewModel.ParentFolder, "my-copy"), viewModel.DestinationPath);
    }

    /// <summary>
    ///     Test that a folder name that is not one valid Windows file name is refused with a message and
    ///     keeps Clone disabled, and that ordinary names pass.
    /// </summary>
    [Theory]
    [InlineData("a/b", true)]
    [InlineData("a\\b", true)]
    [InlineData("..", true)]
    [InlineData(".", true)]
    [InlineData("name.", true)]
    [InlineData("a:b", true)]
    [InlineData("a*b", true)]
    [InlineData("a?b", true)]
    [InlineData("a\"b", true)]
    [InlineData("a<b", true)]
    [InlineData("a>b", true)]
    [InlineData("a|b", true)]
    [InlineData("a\tb", true)]
    [InlineData("a\u0001b", true)]
    [InlineData("con", true)]
    [InlineData("NUL.txt", true)]
    [InlineData("com1", true)]
    [InlineData("LPT9", true)]
    [InlineData("repo", false)]
    [InlineData("my repo", false)]
    [InlineData("repo.v2", false)]
    [InlineData("console", false)]
    public void CloneRepositoryViewModel_FolderName_InvalidNames_AreRefused(string name, bool refused)
    {
        // Arrange
        var viewModel = new CloneRepositoryViewModel([], CreateTempDirectory())
        {
            RepositoryUrl = "https://example.com/team/repo.git"
        };

        // Act
        viewModel.FolderName = name;

        // Assert
        Assert.Equal(refused, viewModel.HasError);
        Assert.Equal(!refused, viewModel.CanAccept);
        Assert.Equal(refused, CloneRepositoryViewModel.CheckFolderName(name) is not null);
    }

    /// <summary>
    ///     Test that a parent folder that is not an absolute path is refused with a message.
    /// </summary>
    [Fact]
    public void CloneRepositoryViewModel_RelativeParentFolder_IsRefused()
    {
        // Arrange
        var viewModel = new CloneRepositoryViewModel([], "relative\\folder")
        {
            RepositoryUrl = "https://example.com/team/repo.git"
        };

        // Assert
        Assert.True(viewModel.HasError);
        Assert.Contains("absolute", viewModel.ErrorText, StringComparison.Ordinal);
        Assert.False(viewModel.CanAccept);
    }

    /// <summary>
    ///     Test that the destination is checked on disk when Clone is pressed, off the UI thread: the
    ///     check is held at a gate while the calling thread (guarded against blocking calls) is free and
    ///     the dialog shows it is checking, and a destination that exists and is not empty keeps the dialog
    ///     open with the reason.
    /// </summary>
    [Fact]
    public async Task CloneRepositoryViewModel_Accept_ChecksDestinationInBackground_ShowsExistingFolderMessage()
    {
        // Arrange: the destination exists and holds a file; the check is held at a gate
        var parent = CreateTempDirectory();
        var existing = Path.Combine(parent, "repo");
        Directory.CreateDirectory(existing);
        await File.WriteAllTextAsync(Path.Combine(existing, "keep.txt"), "mine", TestContext.Current.CancellationToken);
        var gate = new GatedBackgroundWorkRunner();
        var viewModel = new CloneRepositoryViewModel([], parent, gate)
        {
            RepositoryUrl = "https://example.com/team/repo.git"
        };
        CloneRepositoryRequest? completed = null;
        var raised = 0;
        viewModel.Completed += (_, request) =>
        {
            completed = request;
            raised++;
        };
        BlockingCallGuard.TakeViolations();

        // Act: press Clone from a guarded thread; the check is held
        Task accepting;
        using (BlockingCallGuard.GuardCurrentThread())
        {
            accepting = viewModel.AcceptCommand.ExecuteAsync();
        }

        var violations = BlockingCallGuard.TakeViolations();
        await gate.FirstCall;
        var checkingWhileHeld = viewModel.IsChecking;
        var canAcceptWhileHeld = viewModel.CanAccept;
        gate.Release();
        await accepting;

        // Assert: the caller was free; the dialog was checking meanwhile; the folder is refused with the reason
        Assert.Empty(violations);
        Assert.True(checkingWhileHeld);
        Assert.False(canAcceptWhileHeld);
        Assert.False(viewModel.IsChecking);
        Assert.Contains("already exists and is not empty", viewModel.ErrorText, StringComparison.Ordinal);
        Assert.Equal(0, raised);
        Assert.Null(completed);
        Assert.True(File.Exists(Path.Combine(existing, "keep.txt")));

        // Act: another name clears the finding
        viewModel.FolderName = "repo2";

        // Assert
        Assert.False(viewModel.HasError);
        Assert.True(viewModel.CanAccept);
    }

    /// <summary>
    ///     Test that a destination that does not exist, and one that is an existing empty folder, are
    ///     accepted, and that a missing parent folder is refused.
    /// </summary>
    [Fact]
    public async Task CloneRepositoryViewModel_Accept_NewOrEmptyFolderIsAcceptedMissingParentRefused()
    {
        // Arrange
        var parent = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(parent, "empty"));
        var viewModel = new CloneRepositoryViewModel([], Path.Combine(parent, "missing-parent"))
        {
            RepositoryUrl = "https://example.com/team/repo.git"
        };
        var requests = new List<CloneRepositoryRequest?>();
        viewModel.Completed += (_, request) => requests.Add(request);

        // Act: a parent folder that does not exist
        await viewModel.AcceptCommand.ExecuteAsync();
        var missingParentMessage = viewModel.ErrorText;

        // Act: an existing empty folder
        viewModel.ParentFolder = parent;
        viewModel.FolderName = "empty";
        await viewModel.AcceptCommand.ExecuteAsync();

        // Assert
        Assert.Contains("does not exist", missingParentMessage, StringComparison.Ordinal);
        var request = Assert.Single(requests);
        Assert.Equal(Path.Combine(parent, "empty"), request!.DestinationPath);
    }

    /// <summary>
    ///     Test that the group box offers Ungrouped first and selects it by default, lists the groups it
    ///     is given in order, and that the chosen group ends up in the request.
    /// </summary>
    [Fact]
    public void CloneRepositoryViewModel_Groups_UngroupedIsDefaultAndTreePathsListed()
    {
        // Arrange: the groups of the list, without the Ungrouped choice
        var viewModel = new CloneRepositoryViewModel(
            [new CloneGroupChoice("Work", "Work"), new CloneGroupChoice("Backend", "Work > Backend")],
            CreateTempDirectory());

        // Assert: Ungrouped first and selected
        Assert.Equal(["Ungrouped", "Work", "Work > Backend"], viewModel.Groups.Select(choice => choice.DisplayName));
        Assert.Null(viewModel.SelectedGroup.GroupName);
        Assert.Equal("Ungrouped", viewModel.SelectedGroup.DisplayName);

        // Act: a null choice falls back to Ungrouped
        viewModel.SelectedGroup = viewModel.Groups[2];
        var chosen = viewModel.SelectedGroup;
        viewModel.SelectedGroup = null!;

        // Assert
        Assert.Equal("Backend", chosen.GroupName);
        Assert.Null(viewModel.SelectedGroup.GroupName);
    }

    /// <summary>
    ///     Test that a usable input completes the dialog once with the request - the trimmed address, the
    ///     destination (parent plus folder name) and the chosen group.
    /// </summary>
    [Fact]
    public async Task CloneRepositoryViewModel_Accept_CompletesWithRequest()
    {
        // Arrange
        var parent = CreateTempDirectory();
        var viewModel = new CloneRepositoryViewModel(
            [new CloneGroupChoice("Work", "Work")],
            parent)
        {
            RepositoryUrl = "  https://example.com/team/repo.git  ",
            SelectedGroup = new CloneGroupChoice("Work", "Work")
        };
        var requests = new List<CloneRepositoryRequest?>();
        viewModel.Completed += (_, request) => requests.Add(request);

        // Act: accept, then try again and cancel after the end
        await viewModel.AcceptCommand.ExecuteAsync();
        viewModel.Cancel();

        // Assert: told once
        var request = Assert.Single(requests);
        Assert.Equal(new CloneRepositoryRequest("https://example.com/team/repo.git", Path.Combine(parent, "repo"), "Work"), request);
        Assert.False(viewModel.CanAccept);
    }

    /// <summary>
    ///     Test that Cancel completes the dialog with no request, once, however often it is called.
    /// </summary>
    [Fact]
    public void CloneRepositoryViewModel_Cancel_CompletesWithNone()
    {
        // Arrange
        var viewModel = new CloneRepositoryViewModel([], CreateTempDirectory())
        {
            RepositoryUrl = "https://example.com/team/repo.git"
        };
        var requests = new List<CloneRepositoryRequest?>();
        viewModel.Completed += (_, request) => requests.Add(request);

        // Act
        viewModel.CancelCommand.Execute(null);
        viewModel.Cancel();

        // Assert
        Assert.Equal<CloneRepositoryRequest?>([null], requests);
    }

    /// <summary>
    ///     Test that the constructor refuses missing groups.
    /// </summary>
    [Fact]
    public void CloneRepositoryViewModel_Constructor_NullGroups_Throws()
    {
        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => new CloneRepositoryViewModel(null!));
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
                // Best-effort cleanup; a leftover temporary folder does not fail the test
            }
        }
    }

    /// <summary>
    ///     Creates a unique temporary folder, deleted when the test ends.
    /// </summary>
    /// <returns>The folder's path.</returns>
    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_clone_vm_" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        return path;
    }
}
