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

using System.ComponentModel;
using System.Diagnostics;

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     Builds a real Git repository holding an agent package for FlaUI end-to-end tests: a working
///     repository with the agent package layout at its root, committed once per version and tagged
///     <c>release/{version}</c>, and the bare repository cloned from it that tests point a Git source
///     at (a local path, so no network and no credentials are involved).
/// </summary>
/// <remarks>
///     Mirrors <see cref="FixturePackageBuilder"/> (zip) and <see cref="NuGetFixturePackageBuilder"/>
///     (NuGet). Every git process runs with the user's global and system configuration replaced by an
///     empty file, a fixed author and no credential helper or terminal prompt, so nothing on the
///     developer's machine can change a result and nothing is stored in a credential store. The same
///     isolation is offered to the application under test through <see cref="IsolationEnvironment"/>.
///     Not thread-safe; one fixture per test.
/// </remarks>
internal sealed class GitFixtureRepository : IDisposable
{
    /// <summary>
    ///     The package name the repository delivers in the tests.
    /// </summary>
    public const string PackageName = "contoso-agents";

    /// <summary>
    ///     The empty file that replaces the user's global git configuration.
    /// </summary>
    private readonly string _emptyConfig;

    /// <summary>
    ///     The working repository the commits and tags are made in.
    /// </summary>
    private readonly string _workPath;

    /// <summary>
    ///     Initializes a fixture with a working repository, a first version <c>1.0.0</c> (lightweight tag
    ///     <c>release/1.0.0</c>), a second version <c>1.1.0</c> (annotated tag <c>release/1.1.0</c>), two
    ///     tags that are no versions (<c>latest</c> and <c>build-42</c>), and the bare clone of it.
    /// </summary>
    private GitFixtureRepository()
    {
        Root = Path.Combine(Path.GetTempPath(), "AgentControlUiTests-git-" + Guid.NewGuid());
        _workPath = Path.Combine(Root, "work");
        BarePath = Path.Combine(Root, "package-source.git");
        _emptyConfig = Path.Combine(Root, "empty.gitconfig");
        Directory.CreateDirectory(Root);
        File.WriteAllText(_emptyConfig, string.Empty);

        Git(Root, "init", "-q", "-b", "main", _workPath);
        CommitVersion("git 1.0.0", "# Release Notes\n\nGit fixture package 1.0.0.\n");
        Git(_workPath, "tag", "release/1.0.0");
        CommitVersion("git 1.1.0", "# Release Notes\n\nGit fixture package 1.1.0.\n");
        Git(_workPath, "tag", "-a", "-m", "release 1.1.0", "release/1.1.0");
        Git(_workPath, "tag", "latest");
        Git(_workPath, "tag", "build-42");
        Git(Root, "clone", "-q", "--bare", _workPath, BarePath);
    }

    /// <summary>
    ///     Gets the fixture's root folder; everything the fixture makes is below it.
    /// </summary>
    public string Root { get; }

    /// <summary>
    ///     Gets the path of the bare repository a Git source points at.
    /// </summary>
    public string BarePath { get; }

    /// <summary>
    ///     Gets the environment variables that keep git away from the user's configuration, credential
    ///     helpers and terminal prompts, for <see cref="AgentControlTestContext.LaunchEnvironment"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string> IsolationEnvironment => new Dictionary<string, string>
    {
        ["GIT_CONFIG_GLOBAL"] = _emptyConfig,
        ["GIT_CONFIG_NOSYSTEM"] = "1",
        ["GIT_TERMINAL_PROMPT"] = "0",
        ["GCM_INTERACTIVE"] = "never"
    };

    /// <summary>
    ///     Creates the fixture, or skips the calling test when no git executable can be run.
    /// </summary>
    /// <returns>The fixture.</returns>
    public static GitFixtureRepository CreateOrSkip()
    {
        if (!IsGitAvailable())
        {
            Assert.Skip("git is not installed, so a Git package source cannot be exercised against a real repository.");
        }

        return new GitFixtureRepository();
    }

    /// <summary>
    ///     Describes the Git source that points at this fixture's bare repository.
    /// </summary>
    /// <param name="alias">The source alias.</param>
    /// <param name="tagPattern">The tag pattern.</param>
    /// <returns>The source settings, delivering <see cref="PackageName"/>.</returns>
    public GitSourceSettings CreateSource(string alias = "Repo", string tagPattern = "release/{version}") =>
        new(alias, BarePath, tagPattern, PackageName);

    /// <summary>
    ///     Publishes a new version: commits the package layout, tags the commit <c>release/{version}</c>
    ///     and pushes both to the bare repository.
    /// </summary>
    /// <param name="version">The version, for example <c>1.2.0</c>.</param>
    /// <param name="marker">The content of <c>.github/agents/git.md</c>, which tests read back to tell
    ///     which commit a repo's files came from.</param>
    /// <returns>The id of the commit the new tag points to.</returns>
    public string PublishVersion(string version, string marker)
    {
        var commit = CommitVersion(marker, $"# Release Notes\n\nGit fixture package {version}.\n");
        Git(_workPath, "tag", "release/" + version);
        Push();
        return commit;
    }

    /// <summary>
    ///     Moves an existing tag of the bare repository to a new commit with different content, as
    ///     a force-pushed tag does.
    /// </summary>
    /// <param name="tag">The tag to move, for example <c>release/1.0.0</c>.</param>
    /// <param name="marker">The content of <c>.github/agents/git.md</c> in the new commit.</param>
    /// <returns>The id of the commit the tag points to now.</returns>
    public string MoveTag(string tag, string marker)
    {
        var commit = CommitVersion(marker, "# Release Notes\n\nGit fixture package, tag moved.\n");
        Git(_workPath, "tag", "-f", tag);
        Push();
        return commit;
    }

    /// <summary>
    ///     Gets the commit a tag points to in the bare repository.
    /// </summary>
    /// <param name="tag">The tag name, for example <c>release/1.0.0</c>.</param>
    /// <returns>The commit id (the commit an annotated tag points at, not the tag object).</returns>
    public string ResolveTag(string tag) =>
        Git(Root, "--git-dir", BarePath, "rev-parse", $"refs/tags/{tag}^{{commit}}").Trim();

    /// <inheritdoc />
    public void Dispose() => DeleteTree(Root);

    /// <summary>
    ///     Best-effort recursive delete that also removes read-only files, which git writes for its
    ///     object files and which a plain delete cannot remove on Windows.
    /// </summary>
    /// <param name="path">The folder to delete.</param>
    internal static void DeleteTree(string path)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup only.
        }
    }

    /// <summary>
    ///     Tells whether a git executable can be run.
    /// </summary>
    /// <returns><see langword="true"/> when <c>git --version</c> succeeds.</returns>
    private static bool IsGitAvailable()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            process?.WaitForExit();
            return process is { ExitCode: 0 };
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Writes the package layout with the given content and commits it.
    /// </summary>
    /// <param name="marker">The content of <c>.github/agents/git.md</c>.</param>
    /// <param name="releaseNotes">The content of <c>release-notes.md</c>.</param>
    /// <returns>The id of the new commit.</returns>
    private string CommitVersion(string marker, string releaseNotes)
    {
        Write(".github/agents/git.md", marker);
        Write(".github/standards/git.md", "# Git Standard\n");
        Write(".github/templates/git.md", "# Git Template\n");
        Write(".github/skills/git.md", "# Git Skill\n");
        Write("release-notes.md", releaseNotes);
        Git(_workPath, "add", "-A");
        Git(_workPath, "commit", "-q", "-m", marker);
        return Git(_workPath, "rev-parse", "HEAD").Trim();
    }

    /// <summary>
    ///     Writes a file of the working repository.
    /// </summary>
    /// <param name="relativePath">The <c>/</c>-separated path below the repository root.</param>
    /// <param name="content">The file's text.</param>
    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_workPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <summary>
    ///     Pushes the branch and every tag to the bare repository, moving tags that changed.
    /// </summary>
    private void Push() =>
        Git(_workPath, "push", "-q", "--force", BarePath, "HEAD:refs/heads/main", "refs/tags/*:refs/tags/*");

    /// <summary>
    ///     Runs git in a folder with the user's configuration replaced, a fixed identity, no signing
    ///     and no credential helper.
    /// </summary>
    /// <param name="folder">The working directory.</param>
    /// <param name="arguments">The git arguments.</param>
    /// <returns>The standard output.</returns>
    /// <exception cref="InvalidOperationException">Thrown when git fails or takes too long.</exception>
    private string Git(string folder, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // -c options come first so they apply to every command; credential.helper is blanked so a
        // helper can never be reached, and the author and default branch are fixed
        foreach (var option in new[]
                 {
                     "user.name=Test", "user.email=test@example.invalid", "commit.gpgsign=false",
                     "tag.gpgsign=false", "init.defaultBranch=main", "credential.helper=", "core.autocrlf=false"
                 })
        {
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(option);
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in IsolationEnvironment)
        {
            startInfo.Environment[name] = value;
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("git did not start.");
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(60)))
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} did not finish in time.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} failed ({process.ExitCode}): {error.Result}");
        }

        return output.Result;
    }
}
