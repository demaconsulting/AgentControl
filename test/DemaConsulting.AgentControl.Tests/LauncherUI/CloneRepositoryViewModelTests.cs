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

using DemaConsulting.AgentControl.GitIntegration;
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
    [InlineData("CON" + "IN$", true)]
    [InlineData("COM1.txt", true)]
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
    ///     Test that a folder name that starts or ends with a space, or ends with a dot, is refused as
    ///     typed - never silently trimmed - with a message, and that Clone stays disabled.
    /// </summary>
    /// <param name="name">The folder name.</param>
    [Theory]
    [InlineData("repo ")]
    [InlineData(" repo")]
    [InlineData("  repo  ")]
    [InlineData("repo.")]
    [InlineData("repo..")]
    public void CloneRepositoryViewModel_FolderName_TrailingOrLeadingSpace_IsRefusedAndNotTrimmed(string name)
    {
        // Arrange
        var parent = CreateTempDirectory();
        var viewModel = new CloneRepositoryViewModel([], parent)
        {
            RepositoryUrl = "https://example.com/team/repo.git"
        };

        // Act
        viewModel.FolderName = name;

        // Assert: the name is kept as typed, refused with the rule, and the path shown is the raw one
        Assert.Equal(name, viewModel.FolderName);
        Assert.True(viewModel.HasError);
        Assert.Contains("must not start or end with a space", viewModel.ErrorText, StringComparison.Ordinal);
        Assert.False(viewModel.CanAccept);
        Assert.False(viewModel.AcceptCommand.CanExecute(null));
        Assert.Equal(Path.Combine(parent, name), viewModel.DestinationPath);
    }

    /// <summary>
    ///     Test that a folder name of nothing but white space counts as empty - no error, Clone disabled,
    ///     no destination.
    /// </summary>
    [Fact]
    public void CloneRepositoryViewModel_FolderName_WhitespaceOnly_IsEmptyNotAnError()
    {
        // Arrange
        var viewModel = new CloneRepositoryViewModel([], CreateTempDirectory())
        {
            RepositoryUrl = "https://example.com/team/repo.git"
        };

        // Act
        viewModel.FolderName = "   ";

        // Assert
        Assert.False(viewModel.HasError);
        Assert.False(viewModel.CanAccept);
        Assert.Equal(string.Empty, viewModel.DestinationPath);
        Assert.Equal("Enter a name for the new folder.", CloneRepositoryViewModel.CheckFolderName("   "));
    }

    /// <summary>
    ///     Test that the destination uses the folder name exactly as typed (inner spaces included), and
    ///     the parent folder with its surrounding white space removed.
    /// </summary>
    [Fact]
    public void CloneRepositoryViewModel_DestinationPath_UsesTheNameAsTyped()
    {
        // Arrange
        var parent = CreateTempDirectory();
        var viewModel = new CloneRepositoryViewModel([], "  " + parent + "  ")
        {
            RepositoryUrl = "https://example.com/team/repo.git"
        };

        // Act
        viewModel.FolderName = "my  repo.v2";

        // Assert
        Assert.False(viewModel.HasError);
        Assert.True(viewModel.CanAccept);
        Assert.Equal(Path.Combine(parent, "my  repo.v2"), viewModel.DestinationPath);
    }

    /// <summary>
    ///     Test that device names are refused whatever follows them or whichever digit - also a
    ///     superscript one - they carry, including the console handles and a name with a trailing space.
    /// </summary>
    [Fact]
    public void CloneRepositoryViewModel_FolderName_ReservedDeviceNames_AreRefused()
    {
        // Arrange: names built by code so that no unusual character is typed in the source
        var superscripts = new[] { (char)0x00B9, (char)0x00B2, (char)0x00B3 };
        var names = new List<string> { "CON ", "con .txt", "CON" + "IN$", "CON" + "OUT$", "con" + "out$.log", "Aux.tar.gz" };
        names.AddRange(superscripts.SelectMany(digit => new[] { "COM" + digit, "lpt" + digit + ".txt" }));

        // Act / Assert
        Assert.All(names, name => Assert.NotNull(CloneRepositoryViewModel.CheckFolderName(name)));
        Assert.All(
            new[] { "COMET", "CONSOLE", "COM10", "LPT", "console.log", "auxiliary" },
            name => Assert.Null(CloneRepositoryViewModel.CheckFolderName(name)));
    }

    /// <summary>
    ///     Test that clearing the folder name box lets it follow the address again from the next change
    ///     of the address, while a name that is typed stays.
    /// </summary>
    [Fact]
    public void CloneRepositoryViewModel_FolderName_ClearedFollowsTheUrlAgain()
    {
        // Arrange
        var viewModel = new CloneRepositoryViewModel([], CreateTempDirectory());
        viewModel.RepositoryUrl = "https://example.com/team/first.git";

        // Act: a typed name stays through an address change; cleared, the next address names it again
        viewModel.FolderName = "mine";
        viewModel.RepositoryUrl = "https://example.com/team/second.git";
        var kept = viewModel.FolderName;
        viewModel.FolderName = string.Empty;
        var cleared = viewModel.FolderName;
        viewModel.RepositoryUrl = "https://example.com/team/third.git";

        // Assert
        Assert.Equal("mine", kept);
        Assert.Equal(string.Empty, cleared);
        Assert.Equal("third", viewModel.FolderName);
    }

    /// <summary>
    ///     Test that any text of the form <c>name::address</c> is refused as a remote helper - not only
    ///     <c>ext</c> and <c>fd</c> - and that an address that starts with a dash after leading white space
    ///     is refused too.
    /// </summary>
    /// <param name="url">The address.</param>
    [Theory]
    [InlineData("foo::bar")]
    [InlineData("a+b.c::x")]
    [InlineData("foo_bar::x")]
    [InlineData("a b::x")]
    [InlineData("ext::sh -c x")]
    [InlineData("fd::0")]
    [InlineData("  -x")]
    [InlineData("\t--upload-pack=calc")]
    public void CloneRepositoryViewModel_Url_GenericHelperPrefixOrDashAfterWhitespace_IsRefused(string url)
    {
        // Arrange
        var viewModel = new CloneRepositoryViewModel([], CreateTempDirectory());

        // Act
        viewModel.RepositoryUrl = url;

        // Assert
        Assert.True(viewModel.HasError);
        Assert.False(viewModel.CanAccept);
        Assert.NotNull(GitClient.CheckCloneUrl(url));
    }

    /// <summary>
    ///     Test that addresses with colons that are not a remote helper - a bracketed IPv6 literal, a
    ///     drive letter, an scp-like address - are accepted by the dialog, by the same rule as the clone.
    /// </summary>
    /// <param name="url">The address.</param>
    [Theory]
    [InlineData("https://[::1]/r.git")]
    [InlineData("ssh://git@[::1]:22/r.git")]
    [InlineData(@"C:\repos\r")]
    [InlineData("git@host:org/r.git")]
    public void CloneRepositoryViewModel_Url_ColonsThatAreNotAHelper_AreAccepted(string url)
    {
        // Arrange
        var viewModel = new CloneRepositoryViewModel([], CreateTempDirectory());

        // Act
        viewModel.RepositoryUrl = url;

        // Assert
        Assert.False(viewModel.HasError);
        Assert.Null(GitClient.CheckCloneUrl(url));
    }

    /// <summary>
    ///     Test that an answer of the disk check that arrives after the user changed the address, the
    ///     parent folder, the folder name or the group is discarded: nothing is shown, the dialog does not
    ///     end, checking stops, and Clone is available again for what the dialog now holds.
    /// </summary>
    /// <param name="edit">What the user changes while the disk is read.</param>
    /// <param name="destinationExists">Whether the first destination exists, so that a stale answer
    ///     would have been a refusal; otherwise it would have been an acceptance.</param>
    [Theory]
    [InlineData("url", true)]
    [InlineData("url", false)]
    [InlineData("parent", true)]
    [InlineData("parent", false)]
    [InlineData("name", true)]
    [InlineData("name", false)]
    [InlineData("group", true)]
    [InlineData("group", false)]
    public async Task CloneRepositoryViewModel_Accept_InputEditedWhileChecking_DiscardsTheStaleResult(string edit, bool destinationExists)
    {
        // Arrange: a destination that does or does not exist; the check is held at a gate
        var parent = CreateTempDirectory();
        var otherParent = CreateTempDirectory();
        if (destinationExists)
        {
            Directory.CreateDirectory(Path.Combine(parent, "repo"));
        }

        var workGroup = new CloneGroupChoice("Work", "Work");
        var gate = new GatedBackgroundWorkRunner();
        var viewModel = new CloneRepositoryViewModel([workGroup], parent, gate)
        {
            RepositoryUrl = "https://example.com/team/repo.git"
        };
        var requests = new List<CloneRepositoryRequest?>();
        viewModel.Completed += (_, request) => requests.Add(request);
        var accepting = viewModel.AcceptCommand.ExecuteAsync();
        await gate.FirstCall;
        Assert.True(viewModel.IsChecking);

        // Act: edit while the disk is read, then let the check answer
        switch (edit)
        {
            case "url":
                viewModel.RepositoryUrl = "https://example.com/team/other.git";
                break;
            case "parent":
                viewModel.ParentFolder = otherParent;
                break;
            case "name":
                viewModel.FolderName = "other";
                break;
            default:
                viewModel.SelectedGroup = workGroup;
                break;
        }

        gate.Release();
        await accepting;

        // Assert: the stale answer is neither shown nor acted on
        Assert.False(viewModel.IsChecking);
        Assert.Null(viewModel.ErrorText);
        Assert.Empty(requests);
        Assert.True(viewModel.CanAccept);

        // Act: Clone again for what the dialog holds now - the new input is checked on its own
        await viewModel.AcceptCommand.ExecuteAsync();

        // Assert: the new input decides
        var expectsRefusal = destinationExists && edit is "group";
        if (expectsRefusal)
        {
            Assert.Empty(requests);
            Assert.Contains("already exists", viewModel.ErrorText, StringComparison.Ordinal);
        }
        else
        {
            var request = Assert.Single(requests);
            Assert.Equal(viewModel.DestinationPath, request!.DestinationPath);
            Assert.Equal(edit == "group" ? workGroup.GroupName : null, request.GroupName);
        }
    }

    /// <summary>
    ///     Test that an unexpected failure of the disk check is shown as text with the secret removed,
    ///     checking ends and the dialog stays open.
    /// </summary>
    [Fact]
    public async Task CloneRepositoryViewModel_Accept_CheckThrows_ShowsRedactedReasonAndEndsChecking()
    {
        // Arrange: a check that fails with a message that carries a password
        const string secret = "Hunter2PasswordValue";
        var runner = new ThrowingBackgroundWorkRunner(
            new IOException($"The share for https://bob:{secret}@example.com/team/repo.git is gone."));
        var viewModel = new CloneRepositoryViewModel([], CreateTempDirectory(), runner)
        {
            RepositoryUrl = "https://example.com/team/repo.git"
        };
        var requests = new List<CloneRepositoryRequest?>();
        viewModel.Completed += (_, request) => requests.Add(request);

        // Act
        await viewModel.AcceptCommand.ExecuteAsync();

        // Assert
        Assert.False(viewModel.IsChecking);
        Assert.StartsWith("The folder could not be checked:", viewModel.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, viewModel.ErrorText, StringComparison.Ordinal);
        Assert.Empty(requests);
        Assert.True(viewModel.CanAccept);
    }

    /// <summary>
    ///     Test that Cancel while the disk check is in flight completes the dialog once with no request,
    ///     and that the check's late answer raises nothing more.
    /// </summary>
    [Fact]
    public async Task CloneRepositoryViewModel_Cancel_WhileCheckInFlight_CompletesOnceWithNone()
    {
        // Arrange
        var gate = new GatedBackgroundWorkRunner();
        var viewModel = new CloneRepositoryViewModel([], CreateTempDirectory(), gate)
        {
            RepositoryUrl = "https://example.com/team/repo.git"
        };
        var requests = new List<CloneRepositoryRequest?>();
        viewModel.Completed += (_, request) => requests.Add(request);
        var accepting = viewModel.AcceptCommand.ExecuteAsync();
        await gate.FirstCall;

        // Act: cancel, then let the check answer
        viewModel.Cancel();
        gate.Release();
        await accepting;

        // Assert
        Assert.Equal<CloneRepositoryRequest?>([null], requests);
        Assert.False(viewModel.IsChecking);
        Assert.Null(viewModel.ErrorText);
    }

    /// <summary>
    ///     Test that a destination that is an existing file is refused with a reason that says so.
    /// </summary>
    [Fact]
    public async Task CloneRepositoryViewModel_Accept_DestinationIsAFile_ShowsReason()
    {
        // Arrange
        var parent = CreateTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(parent, "repo"), "x", TestContext.Current.CancellationToken);
        var viewModel = new CloneRepositoryViewModel([], parent)
        {
            RepositoryUrl = "https://example.com/team/repo.git"
        };
        var requests = new List<CloneRepositoryRequest?>();
        viewModel.Completed += (_, request) => requests.Add(request);

        // Act
        await viewModel.AcceptCommand.ExecuteAsync();

        // Assert
        Assert.Contains("is a file", viewModel.ErrorText, StringComparison.Ordinal);
        Assert.Empty(requests);
    }

    /// <summary>
    ///     Test that the destination is checked on disk when Clone is pressed, off the UI thread: the
    ///     check is held at a gate while the calling thread (guarded against blocking calls) is free and
    ///     the dialog shows it is checking, and a destination that exists keeps the dialog
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
        Assert.Contains("already exists; choose a name that does not exist yet", viewModel.ErrorText, StringComparison.Ordinal);
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
    ///     Test that choosing another group clears the finding of an earlier destination check and tells
    ///     the dialog (the error text and the has-error flag change), like a change of any other field -
    ///     the finding was about a request for the other group.
    /// </summary>
    [Fact]
    public async Task CloneRepositoryViewModel_SelectedGroup_Changed_ClearsDestinationFindingAndRaisesError()
    {
        // Arrange: a destination that exists, refused by the check, so a finding is shown
        var parent = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(parent, "repo"));
        var workGroup = new CloneGroupChoice("Work", "Work");
        var viewModel = new CloneRepositoryViewModel([workGroup], parent)
        {
            RepositoryUrl = "https://example.com/team/repo.git"
        };
        await viewModel.AcceptCommand.ExecuteAsync();
        var shownBefore = viewModel.HasError;
        var raised = new List<string?>();
        viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        // Act
        viewModel.SelectedGroup = workGroup;

        // Assert: the stale finding is gone and the dialog was told
        Assert.True(shownBefore);
        Assert.False(viewModel.HasError);
        Assert.Null(viewModel.ErrorText);
        Assert.Contains(nameof(CloneRepositoryViewModel.ErrorText), raised);
        Assert.Contains(nameof(CloneRepositoryViewModel.HasError), raised);
        Assert.True(viewModel.CanAccept);
    }

    /// <summary>
    ///     Test that a destination that does not exist is accepted, and that a missing parent folder, an
    ///     existing empty folder and a file are refused with their reasons.
    /// </summary>
    [Fact]
    public async Task CloneRepositoryViewModel_Accept_NewFolderIsAcceptedExistingEmptyFolderAndMissingParentRefused()
    {
        // Arrange
        var parent = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(parent, "empty"));
        await File.WriteAllTextAsync(Path.Combine(parent, "a-file"), "x", TestContext.Current.CancellationToken);
        var viewModel = new CloneRepositoryViewModel([], Path.Combine(parent, "missing-parent"))
        {
            RepositoryUrl = "https://example.com/team/repo.git"
        };
        var requests = new List<CloneRepositoryRequest?>();
        viewModel.Completed += (_, request) => requests.Add(request);

        // Act: a parent folder that does not exist
        await viewModel.AcceptCommand.ExecuteAsync();
        var missingParentMessage = viewModel.ErrorText;

        // Act: an existing empty folder is refused now, and so is a file
        viewModel.ParentFolder = parent;
        viewModel.FolderName = "empty";
        await viewModel.AcceptCommand.ExecuteAsync();
        var emptyFolderMessage = viewModel.ErrorText;
        var afterEmptyFolder = requests.Count;
        viewModel.FolderName = "a-file";
        await viewModel.AcceptCommand.ExecuteAsync();
        var fileMessage = viewModel.ErrorText;
        var afterFile = requests.Count;

        // Act: a name that does not exist yet
        viewModel.FolderName = "fresh";
        await viewModel.AcceptCommand.ExecuteAsync();

        // Assert
        Assert.Contains("does not exist", missingParentMessage, StringComparison.Ordinal);
        Assert.Contains("already exists", emptyFolderMessage, StringComparison.Ordinal);
        Assert.Contains("is a file", fileMessage, StringComparison.Ordinal);
        Assert.Equal(0, afterEmptyFolder);
        Assert.Equal(0, afterFile);
        var request = Assert.Single(requests);
        Assert.Equal(Path.Combine(parent, "fresh"), request!.DestinationPath);
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

    /// <summary>
    ///     A background work runner whose work always fails with a given error, standing in for a disk
    ///     that cannot be read.
    /// </summary>
    /// <param name="error">The error every call fails with.</param>
    private sealed class ThrowingBackgroundWorkRunner(Exception error) : IBackgroundWorkRunner
    {
        /// <inheritdoc />
        public Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken) => Task.FromException<T>(error);
    }
}
