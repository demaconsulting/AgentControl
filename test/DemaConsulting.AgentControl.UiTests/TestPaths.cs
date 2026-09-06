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

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     Locates the built AgentControl.exe and arg-logger stub executable that these black-box
///     FlaUI tests drive, without taking a compile-time dependency on either project.
/// </summary>
/// <remarks>
///     Resolution order for each executable: an explicit environment-variable override (so CI or
///     a developer can point tests at a specific build without relying on relative-path
///     guessing), otherwise a path computed relative to the repository root using the same build
///     configuration (Debug/Release) this test assembly itself was built with - inferred from
///     this assembly's own output folder name, since `dotnet test`/`dotnet build` always build
///     every solution project with the same configuration in one invocation. Stateless and
///     thread-safe.
/// </remarks>
internal static class TestPaths
{
    /// <summary>
    ///     Directory separator characters used when splitting a path into segments to infer the
    ///     build configuration.
    /// </summary>
    private static readonly char[] PathSeparators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    /// <summary>
    ///     Environment variable that, if set, overrides the computed path to AgentControl.exe.
    /// </summary>
    private const string AgentControlExeOverrideVariable = "AGENTCONTROL_EXE_PATH";

    /// <summary>
    ///     Environment variable that, if set, overrides the computed path to the arg-logger stub
    ///     executable.
    /// </summary>
    private const string ArgLoggerStubExeOverrideVariable = "AGENTCONTROL_ARGLOGGERSTUB_EXE_PATH";

    /// <summary>
    ///     Gets the absolute path to the built <c>DemaConsulting.AgentControl.exe</c>.
    /// </summary>
    /// <returns>The resolved executable path.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the executable cannot be found at the
    ///     resolved path (e.g. the app has not been built yet).</exception>
    public static string ResolveAgentControlExe() =>
        ResolveExe(
            AgentControlExeOverrideVariable,
            "src",
            "DemaConsulting.AgentControl",
            "DemaConsulting.AgentControl.exe");

    /// <summary>
    ///     Gets the absolute path to the built arg-logger stub executable.
    /// </summary>
    /// <returns>The resolved executable path.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the executable cannot be found at the
    ///     resolved path (e.g. the stub has not been built yet).</exception>
    public static string ResolveArgLoggerStubExe() =>
        ResolveExe(
            ArgLoggerStubExeOverrideVariable,
            "test",
            "DemaConsulting.AgentControl.ArgLoggerStub",
            "DemaConsulting.AgentControl.ArgLoggerStub.exe");

    /// <summary>
    ///     Resolves a sibling project's built executable path, preferring an environment-variable
    ///     override.
    /// </summary>
    /// <param name="overrideVariable">Environment variable name that may override the computed path.</param>
    /// <param name="rootFolderName">The top-level repository folder containing the project
    ///     (<c>src</c> or <c>test</c>).</param>
    /// <param name="projectFolderName">The project folder name under <paramref name="rootFolderName"/>.</param>
    /// <param name="exeFileName">The built executable's file name.</param>
    /// <returns>The resolved absolute path to the executable.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the executable cannot be found at the
    ///     resolved path.</exception>
    private static string ResolveExe(
        string overrideVariable, string rootFolderName, string projectFolderName, string exeFileName)
    {
        var overridePath = Environment.GetEnvironmentVariable(overrideVariable);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            if (!File.Exists(overridePath))
            {
                throw new FileNotFoundException(
                    $"{overrideVariable} was set to '{overridePath}' but no file exists there.", overridePath);
            }

            return overridePath;
        }

        var repositoryRoot = FindRepositoryRoot();
        var configuration = InferBuildConfiguration();
        var candidate = Path.Combine(
            repositoryRoot, rootFolderName, projectFolderName, "bin", configuration, "net10.0", exeFileName);

        if (!File.Exists(candidate))
        {
            throw new FileNotFoundException(
                $"Could not find '{exeFileName}' at '{candidate}'. Build the solution (dotnet build) " +
                $"before running {nameof(DemaConsulting)}.{nameof(AgentControl)}.UiTests, or set the " +
                $"{overrideVariable} environment variable to an explicit path.",
                candidate);
        }

        return candidate;
    }

    /// <summary>
    ///     Walks up from this test assembly's own output directory to find the repository root,
    ///     identified by the presence of <c>DemaConsulting.AgentControl.slnx</c>.
    /// </summary>
    /// <returns>The absolute repository root path.</returns>
    /// <exception cref="DirectoryNotFoundException">Thrown when no ancestor directory contains
    ///     the solution file.</exception>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DemaConsulting.AgentControl.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate the repository root (DemaConsulting.AgentControl.slnx) above '{AppContext.BaseDirectory}'.");
    }

    /// <summary>
    ///     Infers the build configuration (<c>Debug</c> or <c>Release</c>) this test assembly was
    ///     built with, from its own output directory name.
    /// </summary>
    /// <returns><c>"Release"</c> if this assembly's output path contains a "Release" segment;
    ///     otherwise <c>"Debug"</c>.</returns>
    private static string InferBuildConfiguration()
    {
        var segments = AppContext.BaseDirectory.Split(PathSeparators);
        return segments.Any(segment => string.Equals(segment, "Release", StringComparison.OrdinalIgnoreCase))
            ? "Release"
            : "Debug";
    }
}
