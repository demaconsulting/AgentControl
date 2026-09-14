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

using DemaConsulting.AgentControl.Utilities;

namespace DemaConsulting.AgentControl.Tests;

/// <summary>
///     Tests for the PathHelpers class.
/// </summary>
[Collection("Sequential")]
public class PathHelpersTests
{
    /// <summary>
    ///     Test that SafePathCombine correctly combines valid paths.
    /// </summary>
    [Fact]
    public void PathHelpers_SafePathCombine_ValidPaths_CombinesCorrectly()
    {
        // Arrange: setup valid base path and relative path for combining
        var basePath = "/home/user/project";
        var relativePath = "subfolder/file.txt";

        // Act: execute the operation being tested
        var result = PathHelpers.SafePathCombine(basePath, relativePath);

        // Assert: verify expected behavior
        Assert.Equal(Path.Combine(basePath, relativePath), result);
    }

    /// <summary>
    ///     Test that SafePathCombine throws ArgumentException for path traversal with double dots.
    /// </summary>
    [Fact]
    public void PathHelpers_SafePathCombine_PathTraversalWithDoubleDots_ThrowsArgumentException()
    {
        // Arrange: setup base path and dangerous path traversal using double dots
        var basePath = "/home/user/project";
        var relativePath = "../etc/passwd";

        // Act & Assert: attempt path traversal and verify ArgumentException is thrown with expected message
        var exception = Assert.Throws<ArgumentException>(() =>
            PathHelpers.SafePathCombine(basePath, relativePath));
        Assert.Contains("Invalid path component", exception.Message);
    }

    /// <summary>
    ///     Test that SafePathCombine throws ArgumentException for path with double dots in middle.
    /// </summary>
    [Fact]
    public void PathHelpers_SafePathCombine_DoubleDotsInMiddle_ThrowsArgumentException()
    {
        // Arrange: setup base path and path with double dots in middle for traversal attempt
        var basePath = "/home/user/project";
        var relativePath = "subfolder/../../../etc/passwd";

        // Act & Assert: attempt path traversal in middle and verify ArgumentException is thrown
        var exception = Assert.Throws<ArgumentException>(() =>
            PathHelpers.SafePathCombine(basePath, relativePath));
        Assert.Contains("Invalid path component", exception.Message);
    }

    /// <summary>
    ///     Test that SafePathCombine throws ArgumentException for absolute paths.
    /// </summary>
    [Fact]
    public void PathHelpers_SafePathCombine_AbsolutePath_ThrowsArgumentException()
    {
        // Arrange & Act & Assert: test Unix absolute path rejection
        var unixBasePath = "/home/user/project";
        var unixRelativePath = "/etc/passwd";
        var unixException = Assert.Throws<ArgumentException>(() =>
            PathHelpers.SafePathCombine(unixBasePath, unixRelativePath));
        Assert.Contains("Invalid path component", unixException.Message);

        // Arrange & Act & Assert: test Windows absolute path rejection (only on Windows)
        if (OperatingSystem.IsWindows())
        {
            var windowsBasePath = "C:\\Users\\project";
            var windowsRelativePath = "C:\\Windows\\System32\\file.txt";
            var windowsException = Assert.Throws<ArgumentException>(() =>
                PathHelpers.SafePathCombine(windowsBasePath, windowsRelativePath));
            Assert.Contains("Invalid path component", windowsException.Message);
        }
    }

    /// <summary>
    ///     Test that SafePathCombine correctly handles current directory reference.
    /// </summary>
    [Fact]
    public void PathHelpers_SafePathCombine_CurrentDirectoryReference_CombinesCorrectly()
    {
        // Arrange: setup base path and current directory reference for testing
        var basePath = "/home/user/project";
        var relativePath = "./subfolder/file.txt";

        // Act: execute the operation being tested
        var result = PathHelpers.SafePathCombine(basePath, relativePath);

        // Assert: verify expected behavior
        Assert.Equal(Path.Combine(basePath, relativePath), result);
    }

    /// <summary>
    ///     Test that SafePathCombine correctly handles nested paths.
    /// </summary>
    [Fact]
    public void PathHelpers_SafePathCombine_NestedPaths_CombinesCorrectly()
    {
        // Arrange: setup base path and deeply nested relative path
        var basePath = "/home/user/project";
        var relativePath = "level1/level2/level3/file.txt";

        // Act: execute the operation being tested
        var result = PathHelpers.SafePathCombine(basePath, relativePath);

        // Assert: verify expected behavior
        Assert.Equal(Path.Combine(basePath, relativePath), result);
    }

    /// <summary>
    ///     Test that SafePathCombine correctly handles empty relative path.
    /// </summary>
    [Fact]
    public void PathHelpers_SafePathCombine_EmptyRelativePath_ReturnsBasePath()
    {
        // Arrange: setup base path and empty relative path for testing edge case
        var basePath = "/home/user/project";
        var relativePath = "";

        // Act: execute the operation being tested
        var result = PathHelpers.SafePathCombine(basePath, relativePath);

        // Assert: verify expected behavior
        Assert.Equal(Path.Combine(basePath, relativePath), result);
    }

    /// <summary>
    ///     Test that SafePathCombine correctly handles paths with segments beginning with double dots (not traversal).
    /// </summary>
    [Fact]
    public void PathHelpers_SafePathCombine_DotDotPrefixedName_CombinesCorrectly()
    {
        // Arrange: setup base path and directory name starting with double dots (not traversal)
        var basePath = "/home/user/project";
        var relativePath = "..data/file.txt";

        // Act: execute the operation being tested
        var result = PathHelpers.SafePathCombine(basePath, relativePath);

        // Assert: verify expected behavior
        Assert.Equal(Path.Combine(basePath, relativePath), result);
    }

    /// <summary>
    ///     Test that SafePathCombine throws ArgumentNullException when basePath is null.
    /// </summary>
    [Fact]
    public void PathHelpers_SafePathCombine_NullBasePath_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert: null basePath throws ArgumentNullException
        Assert.Throws<ArgumentNullException>(() =>
            PathHelpers.SafePathCombine(null!, "file.txt"));
    }

    /// <summary>
    ///     Test that SafePathCombine throws ArgumentNullException when relativePath is null.
    /// </summary>
    [Fact]
    public void PathHelpers_SafePathCombine_NullRelativePath_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert: null relativePath throws ArgumentNullException
        Assert.Throws<ArgumentNullException>(() =>
            PathHelpers.SafePathCombine("/home/user", null!));
    }

    /// <summary>
    ///     Test that FindReparsePointInAncestry returns null when no segment between path and
    ///     root is a reparse point.
    /// </summary>
    [Fact]
    public void PathHelpers_FindReparsePointInAncestry_NoReparsePoints_ReturnsNull()
    {
        // Arrange: an ordinary nested directory with no symlinks/junctions anywhere
        var root = CreateTempDirectory();
        try
        {
            var nested = Path.Combine(root, "a", "b");
            Directory.CreateDirectory(nested);

            // Act
            var result = PathHelpers.FindReparsePointInAncestry(root, nested);

            // Assert
            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     Test that FindReparsePointInAncestry finds a junction between root and path.
    /// </summary>
    [Fact]
    public void PathHelpers_FindReparsePointInAncestry_AncestorIsJunction_ReturnsJunctionPath()
    {
        // Arrange: root/link is a junction, and the path being checked is a descendant of it
        var root = CreateTempDirectory();
        var linkTarget = CreateTempDirectory();
        var linkPath = Path.Combine(root, "link");
        try
        {
            CreateJunction(linkPath, linkTarget);
            var descendant = Path.Combine(linkPath, "nested");

            // Act
            var result = PathHelpers.FindReparsePointInAncestry(root, descendant);

            // Assert
            Assert.Equal(linkPath, result);
        }
        finally
        {
            Directory.Delete(linkPath, recursive: false);
            Directory.Delete(root, recursive: true);
            Directory.Delete(linkTarget, recursive: true);
        }
    }

    /// <summary>
    ///     Test that FindReparsePointInAncestry finds root itself when it is a junction.
    /// </summary>
    [Fact]
    public void PathHelpers_FindReparsePointInAncestry_RootIsJunction_ReturnsRoot()
    {
        // Arrange: root is itself a junction
        var parent = CreateTempDirectory();
        var linkTarget = CreateTempDirectory();
        var root = Path.Combine(parent, "root-link");
        try
        {
            CreateJunction(root, linkTarget);
            var descendant = Path.Combine(root, "nested");

            // Act
            var result = PathHelpers.FindReparsePointInAncestry(root, descendant);

            // Assert
            Assert.Equal(root, result);
        }
        finally
        {
            Directory.Delete(root, recursive: false);
            Directory.Delete(parent, recursive: true);
            Directory.Delete(linkTarget, recursive: true);
        }
    }

    /// <summary>
    ///     Test that FindReparsePointInAncestry finds a dangling junction ancestor - one whose
    ///     target no longer exists - rather than silently missing it (as Directory.Exists would).
    /// </summary>
    [Fact]
    public void PathHelpers_FindReparsePointInAncestry_AncestorIsDanglingLink_ReturnsLinkPath()
    {
        // Arrange
        var root = CreateTempDirectory();
        var linkPath = Path.Combine(root, "dangling");
        try
        {
            CreateDanglingLink(linkPath);
            var descendant = Path.Combine(linkPath, "nested", "deeper");

            // Act
            var result = PathHelpers.FindReparsePointInAncestry(root, descendant);

            // Assert
            Assert.Equal(linkPath, result);
        }
        finally
        {
            if (OperatingSystem.IsWindows())
            {
                Directory.Delete(linkPath, recursive: false);
            }
            else
            {
                File.Delete(linkPath);
            }

            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     Test that FindReparsePointInAncestry throws ArgumentNullException when root is null.
    /// </summary>
    [Fact]
    public void PathHelpers_FindReparsePointInAncestry_NullRoot_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            PathHelpers.FindReparsePointInAncestry(null!, "/some/path"));
    }

    /// <summary>
    ///     Test that FindReparsePointInAncestry throws ArgumentNullException when path is null.
    /// </summary>
    [Fact]
    public void PathHelpers_FindReparsePointInAncestry_NullPath_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            PathHelpers.FindReparsePointInAncestry("/some/root", null!));
    }

    /// <summary>
    ///     Test that FindReparsePointInDescendants returns null for a tree with no reparse points.
    /// </summary>
    [Fact]
    public void PathHelpers_FindReparsePointInDescendants_NoReparsePoints_ReturnsNull()
    {
        // Arrange: an ordinary nested directory tree with no symlinks/junctions anywhere
        var root = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "a", "b"));

            // Act
            var result = PathHelpers.FindReparsePointInDescendants(root);

            // Assert
            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     Test that FindReparsePointInDescendants finds a nested junction anywhere in the tree.
    /// </summary>
    [Fact]
    public void PathHelpers_FindReparsePointInDescendants_NestedJunction_ReturnsJunctionPath()
    {
        // Arrange: root/a/linked is a junction nested two levels deep
        var root = CreateTempDirectory();
        var linkTarget = CreateTempDirectory();
        var linkPath = Path.Combine(root, "a", "linked");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "a"));
            CreateJunction(linkPath, linkTarget);

            // Act
            var result = PathHelpers.FindReparsePointInDescendants(root);

            // Assert
            Assert.Equal(linkPath, result);
        }
        finally
        {
            Directory.Delete(linkPath, recursive: false);
            Directory.Delete(root, recursive: true);
            Directory.Delete(linkTarget, recursive: true);
        }
    }

    /// <summary>
    ///     Test that FindReparsePointInDescendants returns the directory itself when it is a
    ///     reparse point.
    /// </summary>
    [Fact]
    public void PathHelpers_FindReparsePointInDescendants_DirectoryItselfIsJunction_ReturnsDirectory()
    {
        // Arrange
        var parent = CreateTempDirectory();
        var linkTarget = CreateTempDirectory();
        var linkPath = Path.Combine(parent, "link");
        try
        {
            CreateJunction(linkPath, linkTarget);

            // Act
            var result = PathHelpers.FindReparsePointInDescendants(linkPath);

            // Assert
            Assert.Equal(linkPath, result);
        }
        finally
        {
            Directory.Delete(linkPath, recursive: false);
            Directory.Delete(parent, recursive: true);
            Directory.Delete(linkTarget, recursive: true);
        }
    }

    /// <summary>
    ///     Test that FindReparsePointInDescendants throws ArgumentNullException when directory is
    ///     null.
    /// </summary>
    [Fact]
    public void PathHelpers_FindReparsePointInDescendants_NullDirectory_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            PathHelpers.FindReparsePointInDescendants(null!));
    }

    /// <summary>
    ///     Creates an NTFS directory junction at <paramref name="linkPath"/> pointing to
    ///     <paramref name="targetPath"/> (Windows), or a real directory symbolic link
    ///     (Linux/macOS), since junctions and symbolic links behave identically for the purposes
    ///     of these tests - both are reparse points reported by
    ///     <see cref="File.GetAttributes(string)"/> - and symbolic links do not require an
    ///     existing target or elevated privileges on non-Windows platforms.
    /// </summary>
    /// <param name="linkPath">The link's path; its parent must exist and it must not already
    ///     exist.</param>
    /// <param name="targetPath">The existing directory the link points to.</param>
    private static void CreateJunction(string linkPath, string targetPath)
    {
        if (OperatingSystem.IsWindows())
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{linkPath}\" \"{targetPath}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo)
                                 ?? throw new InvalidOperationException("Failed to start 'cmd.exe' to create junction.");
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Failed to create junction '{linkPath}' -> '{targetPath}': {process.StandardError.ReadToEnd()}");
            }
        }
        else
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
        }
    }

    /// <summary>
    ///     Creates a dangling directory symlink/junction at <paramref name="linkPath"/> - one
    ///     whose target does not exist - using a real symbolic link on Linux/macOS (created
    ///     without any target validation) or an NTFS junction on Windows (created against a real
    ///     temporary target that is deleted immediately afterward).
    /// </summary>
    /// <param name="linkPath">The link's path; its parent must exist and it must not already
    ///     exist.</param>
    private static void CreateDanglingLink(string linkPath)
    {
        if (OperatingSystem.IsWindows())
        {
            var target = CreateTempDirectory();
            CreateJunction(linkPath, target);
            Directory.Delete(target);
        }
        else
        {
            Directory.CreateSymbolicLink(linkPath, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        }
    }

    /// <summary>
    ///     Creates a unique temporary directory for test isolation.
    /// </summary>
    /// <returns>The created directory's path.</returns>
    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_path_helpers_test_" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        return path;
    }
}


