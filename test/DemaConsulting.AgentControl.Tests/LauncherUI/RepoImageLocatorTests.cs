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

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Unit tests for <see cref="RepoImageLocator"/>: which file in a repo's root becomes the card's
///     picture, which files are refused as unsafe or unusable, and that decoding never throws.
/// </summary>
public sealed class RepoImageLocatorTests : IDisposable
{
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     Test that icon.png is preferred over logo.png when both are present and acceptable.
    /// </summary>
    [Fact]
    public void RepoImageLocator_Find_IconAndLogoPresent_ReturnsIcon()
    {
        // Arrange
        var root = CreateRepo(("logo.png", TestPng.Create(8, 8)), ("icon.png", TestPng.Create(8, 8)));

        // Act
        var found = RepoImageLocator.Find(root);

        // Assert
        Assert.NotNull(found);
        Assert.Equal("icon.png", Path.GetFileName(found.Value.Path));
    }

    /// <summary>
    ///     Test that logo.png is used when there is no icon.png.
    /// </summary>
    [Fact]
    public void RepoImageLocator_Find_OnlyLogoPresent_ReturnsLogo()
    {
        // Arrange
        var root = CreateRepo(("logo.png", TestPng.Create(8, 8)));

        // Act
        var found = RepoImageLocator.Find(root);

        // Assert
        Assert.NotNull(found);
        Assert.Equal("logo.png", Path.GetFileName(found.Value.Path));
    }

    /// <summary>
    ///     Test that the names are matched without regard to case, so Icon.PNG and LOGO.png count.
    /// </summary>
    /// <param name="fileName">The file name as the repo spells it.</param>
    [Theory]
    [InlineData("ICON.PNG")]
    [InlineData("Icon.png")]
    [InlineData("Logo.Png")]
    [InlineData("LOGO.PNG")]
    public void RepoImageLocator_Find_NameInOtherCase_IsFound(string fileName)
    {
        // Arrange
        var root = CreateRepo((fileName, TestPng.Create(8, 8)));

        // Act
        var found = RepoImageLocator.Find(root);

        // Assert
        Assert.NotNull(found);
        Assert.Equal(fileName, Path.GetFileName(found.Value.Path));
    }

    /// <summary>
    ///     Test that a file named icon.png that is not a PNG (no signature) is refused - the
    ///     extension alone proves nothing - and that logo.png is then used instead.
    /// </summary>
    [Fact]
    public void RepoImageLocator_Find_IconWithoutPngSignature_IsRefusedAndLogoUsed()
    {
        // Arrange: a text file renamed to icon.png, and a real logo.png
        var root = CreateRepo(
            ("icon.png", System.Text.Encoding.ASCII.GetBytes("<svg>not a png</svg> padded to be long enough")),
            ("logo.png", TestPng.Create(8, 8)));

        // Act
        var found = RepoImageLocator.Find(root);

        // Assert
        Assert.NotNull(found);
        Assert.Equal("logo.png", Path.GetFileName(found.Value.Path));
    }

    /// <summary>
    ///     Test that nothing is found when the only candidates are refused.
    /// </summary>
    [Fact]
    public void RepoImageLocator_Find_OnlyNonPngCandidates_ReturnsNull()
    {
        // Arrange: a JPEG signature, and an empty file
        var jpeg = new byte[64];
        jpeg[0] = 0xFF;
        jpeg[1] = 0xD8;
        jpeg[2] = 0xFF;
        var root = CreateRepo(("icon.png", jpeg), ("logo.png", []));

        // Act / Assert
        Assert.Null(RepoImageLocator.Find(root));
    }

    /// <summary>
    ///     Test that a file over the size cap is refused, while one exactly at the cap is accepted.
    /// </summary>
    [Fact]
    public void RepoImageLocator_Find_FileOverSizeCap_IsRefused()
    {
        // Arrange: valid PNG headers, padded to just over and exactly at the cap
        var valid = TestPng.Create(8, 8);
        var over = new byte[RepoImageLocator.MaxFileBytes + 1];
        valid.CopyTo(over, 0);
        var atCap = new byte[RepoImageLocator.MaxFileBytes];
        valid.CopyTo(atCap, 0);
        var overRoot = CreateRepo(("icon.png", over));
        var capRoot = CreateRepo(("icon.png", atCap));

        // Act / Assert
        Assert.Null(RepoImageLocator.Find(overRoot));
        Assert.NotNull(RepoImageLocator.Find(capRoot));
    }

    /// <summary>
    ///     Test that a file declaring absurd dimensions (a small file that would expand into a huge
    ///     bitmap) is refused.
    /// </summary>
    /// <param name="width">The declared width.</param>
    /// <param name="height">The declared height.</param>
    [Theory]
    [InlineData(0u, 10u)]
    [InlineData(10u, 0u)]
    [InlineData(8193u, 10u)]
    [InlineData(5000u, 5000u)]
    [InlineData(uint.MaxValue, uint.MaxValue)]
    public void RepoImageLocator_Find_AbsurdDeclaredDimensions_IsRefused(uint width, uint height)
    {
        // Arrange
        var root = CreateRepo(("icon.png", TestPng.CreateHeaderOnly(width, height)));

        // Act / Assert
        Assert.Null(RepoImageLocator.Find(root));
    }

    /// <summary>
    ///     Test that only the repo root is searched: an icon in a subfolder, and a folder that is
    ///     merely named icon.png, are ignored.
    /// </summary>
    [Fact]
    public void RepoImageLocator_Find_SubfolderImageAndFolderNamedIcon_AreIgnored()
    {
        // Arrange
        var root = CreateRepo();
        Directory.CreateDirectory(Path.Combine(root, "docs"));
        File.WriteAllBytes(Path.Combine(root, "docs", "icon.png"), TestPng.Create(8, 8));
        Directory.CreateDirectory(Path.Combine(root, "logo.png"));

        // Act / Assert
        Assert.Null(RepoImageLocator.Find(root));
    }

    /// <summary>
    ///     Test that a missing repo folder has no picture and does not throw.
    /// </summary>
    [Fact]
    public void RepoImageLocator_Find_MissingFolder_ReturnsNull()
    {
        // Act / Assert
        Assert.Null(RepoImageLocator.Find(Path.Combine(Path.GetTempPath(), "agentcontrol_no_such_" + Guid.NewGuid())));
    }

    /// <summary>
    ///     Test that a symbolic link named icon.png is refused, even when it points at a valid PNG
    ///     (here one outside the repo), and that a plain logo.png beside it is then used.
    /// </summary>
    [Fact]
    public void RepoImageLocator_Find_SymbolicLinkIcon_IsRefused()
    {
        // Arrange: a valid PNG outside the repo, linked as icon.png
        var outside = CreateRepo(("secret.png", TestPng.Create(8, 8)));
        var root = CreateRepo(("logo.png", TestPng.Create(8, 8)));
        try
        {
            File.CreateSymbolicLink(Path.Combine(root, "icon.png"), Path.Combine(outside, "secret.png"));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Skip("Creating a symbolic link needs a privilege this account does not have.");
            return;
        }

        // Act
        var found = RepoImageLocator.Find(root);

        // Assert: the link is skipped, the real logo.png wins
        Assert.NotNull(found);
        Assert.Equal("logo.png", Path.GetFileName(found.Value.Path));
    }

    /// <summary>
    ///     Test that the found file's identity (the cache key) changes when the file's size or
    ///     last-write time changes, and is equal when nothing changed.
    /// </summary>
    [Fact]
    public void RepoImageLocator_Find_FileChanged_KeyDiffers()
    {
        // Arrange
        var root = CreateRepo(("icon.png", TestPng.Create(8, 8)));
        var first = RepoImageLocator.Find(root);
        var same = RepoImageLocator.Find(root);

        // Act: replace the file with a bigger picture
        File.WriteAllBytes(Path.Combine(root, "icon.png"), TestPng.Create(32, 32));
        var changed = RepoImageLocator.Find(root);

        // Assert
        Assert.Equal(first, same);
        Assert.NotEqual(first, changed);
    }

    /// <summary>
    ///     Test that decoding a valid PNG gives a bitmap scaled down to the requested width, and
    ///     keeps the aspect ratio of a wide logo.
    /// </summary>
    [Fact]
    public void RepoImageLocator_Decode_ValidLargePng_IsScaledDownToTileWidth() => HeadlessTestApp.Run(() =>
    {
        // Arrange: a 400 x 200 logo
        var root = CreateRepo(("logo.png", TestPng.Create(400, 200)));
        var file = RepoImageLocator.Find(root)!.Value;

        // Act
        using var bitmap = RepoImageLocator.Decode(file);

        // Assert
        Assert.NotNull(bitmap);
        Assert.Equal(RepoImageLocator.TileDecodeWidth, bitmap.PixelSize.Width);
        Assert.Equal(RepoImageLocator.TileDecodeWidth / 2, bitmap.PixelSize.Height);
    });

    /// <summary>
    ///     Test that a file that passes the header check but cannot be decoded gives no bitmap
    ///     instead of throwing, as does a file that disappeared or turned into something else after
    ///     it was found.
    /// </summary>
    [Fact]
    public void RepoImageLocator_Decode_CorruptVanishedOrReplacedFile_ReturnsNull() => HeadlessTestApp.Run(() =>
    {
        // Arrange: a header with sane dimensions but no image data
        var corruptRoot = CreateRepo(("icon.png", TestPng.CreateHeaderOnly(16, 16)));
        var corrupt = RepoImageLocator.Find(corruptRoot)!.Value;

        var vanishingRoot = CreateRepo(("icon.png", TestPng.Create(8, 8)));
        var vanishing = RepoImageLocator.Find(vanishingRoot)!.Value;
        File.Delete(vanishing.Path);

        var replacedRoot = CreateRepo(("icon.png", TestPng.Create(8, 8)));
        var replaced = RepoImageLocator.Find(replacedRoot)!.Value;
        File.WriteAllText(replaced.Path, "no longer a PNG, but long enough to look like a file");

        // Act / Assert
        Assert.Null(RepoImageLocator.Decode(corrupt));
        Assert.Null(RepoImageLocator.Decode(vanishing));
        Assert.Null(RepoImageLocator.Decode(replaced));
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
    ///     Creates a temporary repo folder holding the given files.
    /// </summary>
    /// <param name="files">The file names and contents to create in the folder's root.</param>
    /// <returns>The folder's path.</returns>
    private string CreateRepo(params (string Name, byte[] Content)[] files)
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_repo_image_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        foreach (var (name, content) in files)
        {
            File.WriteAllBytes(Path.Combine(path, name), content);
        }

        return path;
    }
}
