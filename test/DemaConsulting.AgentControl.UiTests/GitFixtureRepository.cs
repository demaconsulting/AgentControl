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
    ///     Creates the fixture, or skips the calling test when no git executable can be run - or fails
    ///     it when the environment variable <c>CI</c> is set, so that Git evidence a pipeline requires
    ///     is never silently skipped.
    /// </summary>
    /// <returns>The fixture.</returns>
    public static GitFixtureRepository CreateOrSkip()
    {
        if (!IsGitAvailable())
        {
            const string reason = "git is not installed, so a Git package source cannot be exercised against a real repository.";
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")))
            {
                Assert.Fail($"{reason} The environment variable CI is set, so the test cannot be skipped.");
            }

            Assert.Skip(reason);
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
    ///     Publishes a version whose tag holds a package layout that a safe reader must refuse, and
    ///     leaves the repository's branch where it was, so later versions do not inherit the hostile
    ///     entry.
    /// </summary>
    /// <param name="version">The version, for example <c>1.2.0</c>.</param>
    /// <param name="kind">What is wrong with the layout: <c>symbolic-link</c> (a link in a managed
    ///     folder, staged without creating a link on disk, which needs no privilege), <c>submodule</c>
    ///     or <c>case-collision</c> (two files whose names differ only in letter case).</param>
    /// <returns>The id of the commit the new tag points to.</returns>
    public string PublishHostileVersion(string version, string kind)
    {
        var good = Git(_workPath, "rev-parse", "HEAD").Trim();
        CommitVersion($"hostile {kind}", "# Release Notes\n\nHostile fixture package.\n");
        switch (kind)
        {
            case "symbolic-link":
                StageEntry("120000", Git(_workPath, ["hash-object", "-w", "--stdin"], "../../../outside.txt", null).Trim(),
                    ".github/agents/evil.md");
                break;
            case "submodule":
                StageEntry("160000", good, ".github/skills/sub");
                break;
            case "case-collision":
                StageEntry("100644", Git(_workPath, ["hash-object", "-w", "--stdin"], "first", null).Trim(), ".github/agents/A.md");
                StageEntry("100644", Git(_workPath, ["hash-object", "-w", "--stdin"], "second", null).Trim(), ".github/agents/a.md");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown kind of hostile layout.");
        }

        Git(_workPath, "commit", "-q", "--allow-empty", "-m", "hostile " + kind);
        var commit = Git(_workPath, "rev-parse", "HEAD").Trim();
        Git(_workPath, "tag", "release/" + version);
        Git(_workPath, "reset", "-q", "--hard", good);
        Push();
        return commit;
    }

    /// <summary>
    ///     Publishes a version that is a perfectly good package whose content a careless reader would change:
    ///     a file with carriage returns and one with an identifier, in folders whose attributes select the
    ///     <c>pwn</c> filter, expand identifiers and convert line endings.
    /// </summary>
    /// <param name="version">The version, for example <c>1.3.0</c>.</param>
    /// <returns>The id of the commit the new tag points to.</returns>
    public string PublishVersionWithConversionAttributes(string version)
    {
        Write(".github/standards/crlf.md", CrlfContent);
        Write(".github/agents/ident.md", IdentContent);
        Write("README.md", "Not part of the package layout.\n");
        Write("docs/guide.md", "Not part of the package layout either.\n");
        CommitVersion("attributes " + version, "# Release Notes\n\nAttributes fixture package.\n");
        Write(".github/agents/.gitattributes", "* filter=pwn ident\n");
        Write(".github/standards/.gitattributes", "* text eol=lf filter=pwn\n");

        // Stage only the attribute files: re-adding the others would let git renormalize them under the
        // new attributes, so the committed bytes would depend on file timestamps
        Git(_workPath, "add", "--", ".github/agents/.gitattributes", ".github/standards/.gitattributes");
        Git(_workPath, "commit", "-q", "-m", "attributes");
        var commit = Git(_workPath, "rev-parse", "HEAD").Trim();
        Git(_workPath, "tag", "release/" + version);
        Push();
        return commit;
    }

    /// <summary>
    ///     The content of the file with carriage returns published by
    ///     <see cref="PublishVersionWithConversionAttributes"/>.
    /// </summary>
    public const string CrlfContent = "one\r\ntwo\r\nthree\r\n";

    /// <summary>
    ///     The content of the file with an identifier published by
    ///     <see cref="PublishVersionWithConversionAttributes"/>.
    /// </summary>
    public const string IdentContent = "$Id$ stays literal\n";

    /// <summary>
    ///     Builds an environment in which the user's own git configuration would run code for any repository: a
    ///     post-checkout hook installed into every new repository (through a template directory and a global
    ///     hooks path), and a <c>pwn</c> filter driver (selected by attributes) whose smudge command writes a
    ///     marker; plus the isolation the application runs under (no prompts, no credential helper).
    /// </summary>
    /// <param name="hookMarker">The file the hook writes.</param>
    /// <param name="filterMarker">The file the filter's smudge command writes.</param>
    /// <returns>The environment variables, for <see cref="AgentControlTestContext.LaunchEnvironment"/>.</returns>
    public IReadOnlyDictionary<string, string> CreateHostileEnvironment(string hookMarker, string filterMarker)
    {
        var hook = "#!/bin/sh\necho ran > \"" + hookMarker.Replace('\\', '/') + "\"\n";
        var template = Path.Combine(Root, "hostile-template");
        Directory.CreateDirectory(Path.Combine(template, "hooks"));
        File.WriteAllText(Path.Combine(template, "hooks", "post-checkout"), hook);
        var hooksPath = Path.Combine(Root, "hostile-hooks");
        Directory.CreateDirectory(hooksPath);
        File.WriteAllText(Path.Combine(hooksPath, "post-checkout"), hook);
        var smudge = $"echo ran >> \\\"{filterMarker.Replace('\\', '/')}\\\" && cat";
        var config = Path.Combine(Root, "hostile.gitconfig");
        File.WriteAllText(
            config,
            $"[core]\n\thooksPath = {hooksPath.Replace('\\', '/')}\n\tautocrlf = true\n" +
            $"[filter \"pwn\"]\n\tsmudge = \"{smudge}\"\n\tclean = cat\n\trequired = false\n");
        return new Dictionary<string, string>(IsolationEnvironment)
        {
            ["GIT_CONFIG_GLOBAL"] = config,
            ["GIT_TEMPLATE_DIR"] = template
        };
    }

    /// <summary>
    ///     Shows that the environment of <see cref="CreateHostileEnvironment"/> really runs code: a plain
    ///     <c>git checkout</c> of a tag under it writes the hook's and the filter's markers (which are removed
    ///     again), so a test that then finds no marker has shown something.
    /// </summary>
    /// <param name="environment">The hostile environment.</param>
    /// <param name="hookMarker">The file the hook writes.</param>
    /// <param name="filterMarker">The file the filter writes.</param>
    /// <param name="tag">A tag of the repository whose layout is selected by the attributes, for example
    ///     <c>release/1.3.0</c>.</param>
    /// <returns><see langword="true"/> when both the hook and the filter ran for the plain git.</returns>
    public bool HostileEnvironmentRunsCodeForPlainGit(
        IReadOnlyDictionary<string, string> environment, string hookMarker, string filterMarker, string tag)
    {
        var control = Path.Combine(Root, "control");
        Git(Root, ["init", "-q", control], null, environment);
        Git(control, ["fetch", "-q", BarePath, $"refs/tags/{tag}:refs/tags/{tag}"], null, environment);
        Git(control, ["checkout", "-q", $"refs/tags/{tag}", "--", ".github/agents", ".github/standards"], null, environment);
        var ran = File.Exists(hookMarker) && File.Exists(filterMarker);
        File.Delete(hookMarker);
        File.Delete(filterMarker);
        return ran;
    }

    /// <summary>
    ///     Stages one tree entry without touching the working folder.
    /// </summary>
    /// <param name="mode">The git file mode, for example <c>120000</c> for a symbolic link.</param>
    /// <param name="objectId">The object the entry points to.</param>
    /// <param name="path">The repository path of the entry.</param>
    private void StageEntry(string mode, string objectId, string path) =>
        // cspell:ignore ignorecase precomposeunicode protectntfs protecthfs
        Git(
            _workPath,
            "-c", "core.ignorecase=false", "-c", "core.precomposeunicode=false",
            "-c", "core.protectNTFS=false", "-c", "core.protectHFS=false",
            "update-index", "--add", "--cacheinfo", $"{mode},{objectId},{path}");

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
    ///     Computes the identity of the agent files <see cref="CommitVersion"/> commits for a marker,
    ///     independently of the application.
    /// </summary>
    /// <param name="marker">The content of <c>.github/agents/git.md</c>.</param>
    /// <returns>The <c>files-sha256:</c> identity text.</returns>
    internal static string FilesIdentity(string marker) =>
        TestFilesIdentity.OfText(
            (".github/agents/git.md", marker),
            (".github/standards/git.md", "# Git Standard\n"),
            (".github/templates/git.md", "# Git Template\n"),
            (".github/skills/git.md", "# Git Skill\n"));

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
    private string Git(string folder, params string[] arguments) => Git(folder, arguments, null, null);

    /// <summary>
    ///     Runs git in a folder with the user's configuration replaced, a fixed identity, no signing
    ///     and no credential helper.
    /// </summary>
    /// <param name="folder">The working directory.</param>
    /// <param name="arguments">The git arguments.</param>
    /// <param name="input">Text for standard input, or <see langword="null"/>.</param>
    /// <param name="environment">Variables that replace the isolation environment's for this run (a
    ///     control that shows a hostile environment really runs code), or <see langword="null"/>.</param>
    /// <returns>The standard output.</returns>
    /// <exception cref="InvalidOperationException">Thrown when git fails or takes too long.</exception>
    private string Git(
        string folder, string[] arguments, string? input, IReadOnlyDictionary<string, string>? environment)
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

        foreach (var (name, value) in environment ?? IsolationEnvironment)
        {
            startInfo.Environment[name] = value;
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("git did not start.");
        process.StandardInput.Write(input ?? string.Empty);
        process.StandardInput.Close();
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
