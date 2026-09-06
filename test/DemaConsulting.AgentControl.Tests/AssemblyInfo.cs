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

// Initializes the same Serilog-backed logging pipeline the shipped app uses, once for the
// whole assembly, so that GitClient/AgentToolLauncher process-launch diagnostics logged during
// test runs (notably GitClientTests, where the flaky exit-code -1 failure this logging exists
// to root-cause actually occurs) are captured to disk. See TestLoggingFixture for full detail.
[assembly: Xunit.AssemblyFixture(typeof(DemaConsulting.AgentControl.Tests.TestLoggingFixture))]

namespace DemaConsulting.AgentControl.Tests;

// Tests share Console state, so they must not run in parallel.
/// <summary>
/// Defines the Sequential test collection.
/// Tests in this collection are disabled from running in parallel to
/// prevent conflicts when sharing Console state.
/// </summary>
[CollectionDefinition("Sequential", DisableParallelization = true)]
public sealed class SequentialCollection { }

// GitClientTests and RepoCardViewModelTests both spawn real child processes (git-stub
// .bat/.sh scripts via GitClient.RunGit/Process.Start), so they must not run concurrently
// with each other: racing real OS process creation/exit between the two classes has been
// observed to cause intermittent stub-process failures (e.g. non-zero/unexpected exit
// codes or stale stdout/stderr reads) under xUnit's default cross-class parallelism.
/// <summary>
/// Defines the RealProcess test collection.
/// Tests in this collection are disabled from running in parallel to
/// prevent contention over OS-level process-creation resources between
/// tests that spawn real child processes (git-stub scripts).
/// </summary>
[CollectionDefinition("RealProcess", DisableParallelization = true)]
public sealed class RealProcessCollection { }
