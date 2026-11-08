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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.Settings;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Tests of the repo's own picture on its card: the view model that loads it in the background
///     (and caches it), and the avatar tile that shows it in place of the generated initials.
/// </summary>
/// <remarks>
///     Runs in the <c>RealProcess</c> collection because showing a card spawns real git
///     subprocesses as part of the card's eager refresh.
/// </remarks>
[Collection("RealProcess")]
public sealed class RepoCardImageTests : IDisposable
{
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     Test that a card finds the repo's root icon.png, shows it (not the initials), and that a
    ///     second refresh of an unchanged file does not decode it again - while a changed file is
    ///     decoded again.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_RepoImage_IconPresent_LoadsOnceUntilFileChanges() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: a repo with an icon, and a decoder that counts its calls
        var repo = CreateRepo(("icon.png", TestPng.Create(64, 64)));
        var decodes = 0;
        var card = CreateCard(repo, file =>
        {
            decodes++;
            return RepoImageLocator.Decode(file);
        });
        Assert.False(card.HasRepoImage);
        Assert.True(card.ShowAvatarInitials);

        // Act: the initial load
        card.RefreshCheap();
        await card.RepoImageRefreshTask;

        // Assert: the picture shows instead of the initials
        Assert.True(card.HasRepoImage);
        Assert.NotNull(card.RepoImage);
        Assert.False(card.ShowAvatarInitials);
        Assert.False(card.ShowAvatarFolderGlyph);
        Assert.Equal(1, decodes);

        // Act: refresh again with nothing changed
        card.RefreshCheap();
        await card.RepoImageRefreshTask;

        // Assert: cached - no second decode, same bitmap
        Assert.Equal(1, decodes);
        Assert.True(card.HasRepoImage);

        // Act: the repo replaces its icon
        File.WriteAllBytes(Path.Combine(repo, "icon.png"), TestPng.Create(128, 64));
        card.RefreshCheap();
        await card.RepoImageRefreshTask;

        // Assert: decoded again
        Assert.Equal(2, decodes);
        Assert.True(card.HasRepoImage);
    });

    /// <summary>
    ///     Test that removing the picture file and refreshing puts the generated initials back.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_RepoImage_FileRemoved_FallsBackToInitials() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var repo = CreateRepo(("logo.png", TestPng.Create(64, 64)));
        var card = CreateCard(repo, file => RepoImageLocator.Decode(file));
        card.RefreshCheap();
        await card.RepoImageRefreshTask;
        Assert.True(card.HasRepoImage);

        // Act
        File.Delete(Path.Combine(repo, "logo.png"));
        card.RefreshCheap();
        await card.RepoImageRefreshTask;

        // Assert
        Assert.False(card.HasRepoImage);
        Assert.True(card.ShowAvatarInitials);
    });

    /// <summary>
    ///     Test that a repo folder that does not exist gets no picture lookup at all.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_RepoImage_MissingFolder_NeverLooksForAnImage() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var decodes = 0;
        var card = CreateCard(
            Path.Combine(Path.GetTempPath(), "agentcontrol_missing_repo_img_" + Guid.NewGuid().ToString("N")),
            _ =>
            {
                decodes++;
                return null;
            });

        // Act
        card.RefreshCheap();
        await card.RepoImageRefreshTask;

        // Assert
        Assert.True(card.IsMissing);
        Assert.Equal(0, decodes);
        Assert.False(card.HasRepoImage);
    });

    /// <summary>
    ///     Test that a picture that cannot be decoded - or whose decoder throws - leaves the
    ///     generated tile, never breaks the card, and is not retried until the file changes.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_RepoImage_DecodeFailsOrThrows_KeepsTileAndDoesNotRetry() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: a decoder that fails (null), and a repo whose icon passes the header check
        var repo = CreateRepo(("icon.png", TestPng.Create(8, 8)));
        var decodes = 0;
        var card = CreateCard(repo, _ =>
        {
            decodes++;
            return null;
        });

        // Act: two refreshes
        card.RefreshCheap();
        await card.RepoImageRefreshTask;
        card.RefreshCheap();
        await card.RepoImageRefreshTask;

        // Assert: tile kept, one attempt only
        Assert.False(card.HasRepoImage);
        Assert.True(card.ShowAvatarInitials);
        Assert.Equal(1, decodes);

        // Arrange: a decoder that throws
        var throwing = CreateCard(repo, _ => throw new InvalidOperationException("decoder exploded"));

        // Act
        throwing.RefreshCheap();
        await throwing.RepoImageRefreshTask;

        // Assert: swallowed, tile kept
        Assert.False(throwing.HasRepoImage);
        Assert.True(throwing.ShowAvatarInitials);

        // Arrange / Act: decoders that throw other kinds of exception, resource exhaustion included
        foreach (Exception failure in new Exception[] { new FormatException(), new OutOfMemoryException(), new DivideByZeroException() })
        {
            var unexpected = CreateCard(repo, _ => throw failure);
            unexpected.RefreshCheap();
            await unexpected.RepoImageRefreshTask;

            // Assert: whatever it was, the background task did not fault and the tile is kept
            Assert.True(unexpected.RepoImageRefreshTask.IsCompletedSuccessfully);
            Assert.False(unexpected.HasRepoImage);
            Assert.True(unexpected.ShowAvatarInitials);
        }
    });

    /// <summary>
    ///     Test that an older background refresh that fails after a newer one has published the
    ///     picture does not erase it: only the latest refresh may clear the picture on failure.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_RepoImage_OlderRefreshFailsAfterNewerPublished_KeepsNewerImage() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: the first decode waits to be released and then throws; the second succeeds at once
        var repo = CreateRepo(("icon.png", TestPng.Create(64, 64)));
        using var release = new ManualResetEventSlim(false);
        var calls = 0;
        var card = CreateCard(repo, file =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                Assert.True(release.Wait(TimeSpan.FromSeconds(30)), "the older decode was never released");
                throw new InvalidOperationException("stale decoder failure");
            }

            return RepoImageLocator.Decode(file);
        });

        // Act: start the older refresh (blocked in its decoder), then a newer one that publishes
        card.RefreshCheap();
        var older = card.RepoImageRefreshTask;
        card.RefreshCheap();
        var newer = card.RepoImageRefreshTask;
        await newer;
        Assert.True(card.HasRepoImage);
        var published = card.RepoImage;

        // Act: the older refresh now fails
        release.Set();
        await older;

        // Assert: it completed without faulting and left the newer picture alone
        Assert.True(older.IsCompletedSuccessfully);
        Assert.True(card.HasRepoImage);
        Assert.Same(published, card.RepoImage);
        Assert.Equal(2, calls);
    });

    /// <summary>
    ///     Test that the card owns the bitmaps it publishes: a picture that is replaced by a newer one,
    ///     or cleared because the file is gone, or dropped because the card was retired, is disposed
    ///     once it is no longer published, while the current picture stays usable.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_RepoImage_SupersededClearedOrRetired_BitmapIsDisposed() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: a card showing a picture
        var repo = CreateRepo(("icon.png", TestPng.Create(64, 64)));
        var card = CreateCard(repo, file => RepoImageLocator.Decode(file));
        card.RefreshCheap();
        await card.RepoImageRefreshTask;
        var first = card.RepoImage!;
        Assert.True(first.PixelSize.Width > 0);

        // Act: the repo replaces its icon
        File.WriteAllBytes(Path.Combine(repo, "icon.png"), TestPng.Create(128, 64));
        card.RefreshCheap();
        await card.RepoImageRefreshTask;
        var second = card.RepoImage!;

        // Assert: the superseded bitmap is released, the current one is not
        Assert.NotSame(first, second);
        Assert.Throws<ObjectDisposedException>(() => first.PixelSize);
        Assert.True(second.PixelSize.Width > 0);

        // Act: the icon is removed
        File.Delete(Path.Combine(repo, "icon.png"));
        card.RefreshCheap();
        await card.RepoImageRefreshTask;

        // Assert: cleared, and the bitmap that was shown is released
        Assert.Null(card.RepoImage);
        Assert.Throws<ObjectDisposedException>(() => second.PixelSize);

        // Act: a picture again, then the card is retired
        File.WriteAllBytes(Path.Combine(repo, "logo.png"), TestPng.Create(64, 64));
        card.RefreshCheap();
        await card.RepoImageRefreshTask;
        var third = card.RepoImage!;
        card.Retire();

        // Assert
        Assert.Null(card.RepoImage);
        Assert.Throws<ObjectDisposedException>(() => third.PixelSize);
    });

    /// <summary>
    ///     Test that a bitmap decoded by a refresh that a newer one has overtaken (so it is never
    ///     published) is disposed by the refresh that decoded it, and the published one is not.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_RepoImage_DecodedByOvertakenRefresh_IsDisposedAndNotPublished() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: the first decode waits to be released and then returns a bitmap; the second returns at once
        var repo = CreateRepo(("icon.png", TestPng.Create(64, 64)));
        using var release = new ManualResetEventSlim(false);
        Bitmap? overtaken = null;
        var calls = 0;
        var card = CreateCard(repo, file =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                Assert.True(release.Wait(TimeSpan.FromSeconds(30)), "the older decode was never released");
                overtaken = RepoImageLocator.Decode(file);
                return overtaken;
            }

            return RepoImageLocator.Decode(file);
        });

        // Act: the older refresh is blocked in its decoder, the newer one publishes, then the older finishes
        card.RefreshCheap();
        var older = card.RepoImageRefreshTask;
        card.RefreshCheap();
        await card.RepoImageRefreshTask;
        var published = card.RepoImage!;
        release.Set();
        await older;

        // Assert: the overtaken bitmap was released, the published one is intact and still shown
        Assert.NotNull(overtaken);
        Assert.NotSame(published, overtaken);
        Assert.Throws<ObjectDisposedException>(() => overtaken.PixelSize);
        Assert.Same(published, card.RepoImage);
        Assert.True(published.PixelSize.Width > 0);
    });

    /// <summary>
    ///     Test that the main window's avatar tile shows the repo's picture when it has one - the
    ///     image visible, the initials hidden - and the initials when it has none.
    /// </summary>
    [Fact]
    public void MainWindow_RepoCard_AvatarShowsRepoPictureWhenPresent() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: one repo with an icon and one without
        var withIcon = CreateRepo("with-icon", ("icon.png", TestPng.Create(48, 48)));
        var withoutIcon = CreateRepo("without-icon");
        var viewModel = new MainWindowViewModel(
            new AppSettings { RecentRepos = [new RecentRepo { Path = withIcon }, new RecentRepo { Path = withoutIcon }] },
            configDirectory: CreateRepo("config"));
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        try
        {
            // Act: wait for the background loads, then let the bindings settle
            foreach (var card in viewModel.RepoCards)
            {
                await card.RepoImageRefreshTask;
            }

            Dispatcher.UIThread.RunJobs();

            // Assert: the tile of the repo with an icon shows the image, the other the initials
            var tiles = window.GetVisualDescendantsOf<Border>("RepoAvatar").ToList();
            Assert.Equal(2, tiles.Count);
            var imageTile = tiles.Single(tile => tile.DataContext is RepoCardViewModel { HasRepoImage: true });
            var plainTile = tiles.Single(tile => tile.DataContext is RepoCardViewModel { HasRepoImage: false });
            Assert.True(imageTile.GetVisualDescendantsOf<Image>("RepoAvatarImage").Single().IsVisible);
            Assert.NotNull(imageTile.GetVisualDescendantsOf<Image>("RepoAvatarImage").Single().Source);
            Assert.False(imageTile.GetVisualDescendantsOf<TextBlock>("RepoAvatarText").Single().IsVisible);
            Assert.Contains("hasImage", imageTile.Classes);
            Assert.False(plainTile.GetVisualDescendantsOf<Image>("RepoAvatarImage").Single().IsVisible);
            Assert.True(plainTile.GetVisualDescendantsOf<TextBlock>("RepoAvatarText").Single().IsVisible);
            Assert.DoesNotContain("hasImage", plainTile.Classes);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Deletes the temporary folders the tests created.
    /// </summary>
    public void Dispose()
    {
        foreach (var path in _tempPaths.Where(Directory.Exists))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    /// <summary>
    ///     Creates a card for a repo folder with a substitute picture decoder.
    /// </summary>
    /// <param name="repoPath">The repo folder.</param>
    /// <param name="decode">The decoder the card uses.</param>
    /// <returns>The card.</returns>
    private static RepoCardViewModel CreateCard(string repoPath, Func<RepoImageFile, Bitmap?> decode) =>
        new(new RecentRepo { Path = repoPath }, () => new AppSettings(), new PackageVersionCache(), decodeImage: decode);

    /// <summary>
    ///     Creates a temporary repo folder with a fixed name, holding the given files.
    /// </summary>
    /// <param name="name">The folder's name.</param>
    /// <param name="files">The file names and contents to create in the folder's root.</param>
    /// <returns>The folder's path.</returns>
    private string CreateRepo(string name, params (string Name, byte[] Content)[] files)
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_card_image_" + Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(path);
        _tempPaths.Add(Path.GetDirectoryName(path)!);
        foreach (var (fileName, content) in files)
        {
            File.WriteAllBytes(Path.Combine(path, fileName), content);
        }

        return path;
    }

    /// <summary>
    ///     Creates a temporary repo folder holding the given files.
    /// </summary>
    /// <param name="files">The file names and contents to create in the folder's root.</param>
    /// <returns>The folder's path.</returns>
    private string CreateRepo(params (string Name, byte[] Content)[] files) => CreateRepo("repo", files);
}

/// <summary>
///     Helpers for finding named controls in a rendered window's visual tree.
/// </summary>
internal static class VisualTestExtensions
{
    /// <summary>
    ///     Finds the controls of a type with a given XAML name below a visual (a template that is
    ///     instantiated per item gives every item its own copy, which a name-scope lookup cannot
    ///     tell apart).
    /// </summary>
    /// <typeparam name="T">The control type.</typeparam>
    /// <param name="root">The visual to search below.</param>
    /// <param name="name">The control's XAML name.</param>
    /// <returns>The matching controls, in visual-tree order.</returns>
    public static IEnumerable<T> GetVisualDescendantsOf<T>(this Visual root, string name)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>().Where(control => control.Name == name);
}
