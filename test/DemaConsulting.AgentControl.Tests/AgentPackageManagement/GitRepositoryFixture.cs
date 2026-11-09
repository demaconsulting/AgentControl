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
using DemaConsulting.AgentControl.AgentPackageManagement;

namespace DemaConsulting.AgentControl.Tests.AgentPackageManagement;

/// <summary>
///     Builds real Git repositories for the Git source tests: a working repository that is
///     committed to and tagged, and a bare repository (the "remote") that the tests point a source
///     at. Every git process runs with the user's global and system configuration replaced by an
///     empty file, so a developer's credential helpers, <c>insteadOf</c> rewrites or
///     <c>safe.directory</c> settings can never change a result.
/// </summary>
/// <remarks>
///     Symbolic-link and submodule entries are created with <c>hash-object</c> and
///     <c>update-index --cacheinfo</c>, which needs no operating-system privilege (creating a real
///     symbolic link on Windows does), so the tree-level refusal of links is exercised on every
///     platform. Not thread-safe; one fixture per test.
/// </remarks>
internal sealed class GitRepositoryFixture : IDisposable
{
    /// <summary>
    ///     The empty file that replaces the global git configuration.
    /// </summary>
    private readonly string _emptyConfig;

    /// <summary>
    ///     Initializes a fixture with an empty working repository and an empty bare remote.
    /// </summary>
    private GitRepositoryFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "agentcontrol_git_it_" + Guid.NewGuid().ToString("N")[..12]);
        WorkPath = Path.Combine(Root, "work");
        RemotePath = Path.Combine(Root, "remote.git");
        _emptyConfig = Path.Combine(Root, "empty.gitconfig");
        Directory.CreateDirectory(Root);
        File.WriteAllText(_emptyConfig, string.Empty);

        Git(Root, "init", "-q", "-b", "main", WorkPath);
        Git(Root, "init", "-q", "--bare", RemotePath);
    }

    /// <summary>
    ///     Gets the fixture's root folder; everything the fixture makes is below it.
    /// </summary>
    public string Root { get; }

    /// <summary>
    ///     Gets the working repository.
    /// </summary>
    public string WorkPath { get; }

    /// <summary>
    ///     Gets the bare repository that plays the remote (a local path, so the <c>file</c>
    ///     transport is used).
    /// </summary>
    public string RemotePath { get; }

    /// <summary>
    ///     Gets the environment that isolates git from the user's configuration, for the
    ///     <c>ExtraEnvironment</c> of the client under test.
    /// </summary>
    public IReadOnlyDictionary<string, string?> IsolationEnvironment => new Dictionary<string, string?>
    {
        ["GIT_CONFIG_GLOBAL"] = _emptyConfig,
        ["GIT_CONFIG_NOSYSTEM"] = "1",
        ["GIT_CONFIG_COUNT"] = null,
        ["GIT_CONFIG_PARAMETERS"] = null
    };

    /// <summary>
    ///     Creates a fixture, or skips the calling test when git cannot be run.
    /// </summary>
    /// <returns>The fixture.</returns>
    public static GitRepositoryFixture CreateOrSkip()
    {
        SkipWithoutGit();
        return new GitRepositoryFixture();
    }

    /// <summary>
    ///     Skips the calling test when no git executable can be run.
    /// </summary>
    public static void SkipWithoutGit()
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
            if (process is { ExitCode: 0 })
            {
                return;
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // Not installed: skipped below
        }

        Assert.Skip("git is not installed, so the Git source cannot be exercised against a real repository.");
    }

    /// <summary>
    ///     Replaces the files of the working repository's next commit: writes the files, stages
    ///     everything and commits.
    /// </summary>
    /// <param name="files">The files to write, by repository-relative <c>/</c>-separated path.</param>
    /// <param name="message">The commit message.</param>
    /// <returns>The commit id.</returns>
    public string Commit(IReadOnlyDictionary<string, string> files, string message)
    {
        foreach (var (path, content) in files)
        {
            var full = Path.Combine(WorkPath, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
            Git(WorkPath, "add", "-f", "--", path);
        }

        return CommitStaged(message);
    }

    /// <summary>
    ///     Commits whatever is staged (the files of <see cref="Commit"/>, and entries staged with
    ///     <see cref="AddSymbolicLink"/> or <see cref="AddSubmodule"/>).
    /// </summary>
    /// <param name="message">The commit message.</param>
    /// <returns>The commit id.</returns>
    public string CommitStaged(string message)
    {
        Git(WorkPath, "commit", "-q", "--allow-empty", "-m", message);
        return Git(WorkPath, "rev-parse", "HEAD").Trim();
    }

    /// <summary>
    ///     Stages a symbolic-link entry (mode 120000) without creating a link on disk.
    /// </summary>
    /// <param name="path">The repository path of the link.</param>
    /// <param name="target">The link's target text.</param>
    public void AddSymbolicLink(string path, string target)
    {
        var blob = Git(WorkPath, ["hash-object", "-w", "--stdin"], target).Trim();
        Git(WorkPath, "update-index", "--add", "--cacheinfo", $"120000,{blob},{path}");
    }

    /// <summary>
    ///     Stages a regular file entry (mode 100644) without writing it to the working folder, so
    ///     paths that a case-insensitive or normalizing file system would merge (<c>A.md</c> and
    ///     <c>a.md</c>) can be committed on every platform.
    /// </summary>
    /// <param name="path">The repository path of the file.</param>
    /// <param name="content">The file's text.</param>
    public void AddFile(string path, string content)
    {
        var blob = Git(WorkPath, ["hash-object", "-w", "--stdin"], content).Trim();
        // cspell:ignore ignorecase precomposeunicode protectntfs protecthfs
        Git(
            WorkPath,
            "-c", "core.ignorecase=false", "-c", "core.precomposeunicode=false",
            "-c", "core.protectNTFS=false", "-c", "core.protectHFS=false",
            "update-index", "--add", "--cacheinfo", $"100644,{blob},{path}");
    }

    /// <summary>
    ///     Stages a submodule entry (mode 160000, pointing at the given commit).
    /// </summary>
    /// <param name="path">The repository path of the submodule.</param>
    /// <param name="commit">The commit id the entry records.</param>
    public void AddSubmodule(string path, string commit) =>
        Git(WorkPath, "update-index", "--add", "--cacheinfo", $"160000,{commit},{path}");

    /// <summary>
    ///     Tags the current commit.
    /// </summary>
    /// <param name="name">The tag name.</param>
    /// <param name="annotated"><see langword="true"/> for an annotated tag object.</param>
    /// <param name="force"><see langword="true"/> to move an existing tag.</param>
    public void Tag(string name, bool annotated = false, bool force = false)
    {
        var arguments = new List<string> { "tag" };
        if (force)
        {
            arguments.Add("-f");
        }

        if (annotated)
        {
            arguments.AddRange(["-a", "-m", "release " + name]);
        }

        arguments.Add(name);
        Git(WorkPath, [.. arguments]);
    }

    /// <summary>
    ///     Pushes the branch and every tag to the bare remote, moving tags that changed.
    /// </summary>
    public void Push()
    {
        Git(WorkPath, "push", "-q", "--force", RemotePath, "main", "refs/tags/*:refs/tags/*");
    }

    /// <summary>
    ///     Gets the commit a ref names in the working repository.
    /// </summary>
    /// <param name="reference">The ref, for example <c>HEAD</c> or <c>v1.0.0</c>.</param>
    /// <returns>The commit id.</returns>
    public string Resolve(string reference) => Git(WorkPath, "rev-parse", reference + "^{commit}").Trim();

    /// <summary>
    ///     Gets the object a tag ref points to (the tag object for an annotated tag).
    /// </summary>
    /// <param name="tag">The tag name.</param>
    /// <returns>The object id.</returns>
    public string TagObject(string tag) => Git(WorkPath, "rev-parse", "refs/tags/" + tag).Trim();

    /// <summary>
    ///     Creates a source definition for the bare remote.
    /// </summary>
    /// <param name="alias">The alias.</param>
    /// <param name="pattern">The tag pattern.</param>
    /// <param name="subfolder">The sub-folder, or <see langword="null"/>.</param>
    /// <returns>The definition.</returns>
    public PackageSourceDefinition CreateDefinition(string alias = "Repo", string pattern = "v{version}", string? subfolder = null) =>
        new()
        {
            Alias = alias,
            Type = PackageSourceType.Git,
            Location = RemotePath,
            TagPattern = pattern,
            Subfolder = subfolder,
            PackageIds = ["contoso-agents"]
        };

    /// <summary>
    ///     Creates the options of a provider under test: the fixture's isolation, a private cache
    ///     folder inside the fixture, and short time limits.
    /// </summary>
    /// <param name="interactive">Whether the work is user-initiated.</param>
    /// <param name="logger">A logger to receive diagnostics, or <see langword="null"/>.</param>
    /// <returns>The options.</returns>
    public GitSourceOptions CreateOptions(bool interactive = false, Microsoft.Extensions.Logging.ILogger? logger = null) =>
        new(
            () => "git",
            Path.Combine(Root, "cache"),
            interactive,
            logger,
            ["protocol.file.allow=always"],
            IsolationEnvironment,
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(120));

    /// <summary>
    ///     Runs git in a folder with the user's configuration replaced and a fixed identity.
    /// </summary>
    /// <param name="folder">The working directory.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The standard output.</returns>
    /// <exception cref="InvalidOperationException">Thrown when git exits with a failure.</exception>
    public string Git(string folder, params string[] arguments) => Git(folder, arguments, input: null);

    /// <summary>
    ///     Runs git in a folder with the user's configuration replaced and a fixed identity.
    /// </summary>
    /// <param name="folder">The working directory.</param>
    /// <param name="arguments">The arguments.</param>
    /// <param name="input">Text for standard input, or <see langword="null"/>.</param>
    /// <returns>The standard output.</returns>
    /// <exception cref="InvalidOperationException">Thrown when git exits with a failure.</exception>
    public string Git(string folder, string[] arguments, string? input)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = folder,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["GIT_CONFIG_GLOBAL"] = _emptyConfig;
        startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        startInfo.Environment.Remove("GIT_CONFIG_COUNT");
        startInfo.Environment.Remove("GIT_CONFIG_PARAMETERS");
        startInfo.Environment["GIT_AUTHOR_NAME"] = "Test";
        startInfo.Environment["GIT_AUTHOR_EMAIL"] = "test@example.invalid";
        startInfo.Environment["GIT_COMMITTER_NAME"] = "Test";
        startInfo.Environment["GIT_COMMITTER_EMAIL"] = "test@example.invalid";
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("git did not start.");
        process.StandardInput.Write(input ?? string.Empty);
        process.StandardInput.Close();
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} failed ({process.ExitCode}): {error.Result}");
        }

        return output;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // git writes read-only object files; clear the attribute so Windows can delete them
        GitSourceProvider.DeleteDirectory(Root);
    }
}
