# build.ps1
#
# PURPOSE:
#   Builds the solution in Release configuration and runs the unit tests, either all of them
#   (-All, the full local build and test run) or only the test projects a change affects
#   (-Changed / -Project).
#
# USAGE:
#   pwsh ./build.ps1 -Help                     Print the usage text and exit 0
#   pwsh ./build.ps1 -All                      Full local build and all tests (including the FlaUI UI tests)
#   pwsh ./build.ps1 -All -SkipUiTests         Full build, headless tests only (no desktop automation)
#   pwsh ./build.ps1 -Changed -SkipUiTests [-Base <ref>]
#                                              Only the test projects affected by the changes since <ref>
#                                              (default origin/main, else main) plus uncommitted changes
#   pwsh ./build.ps1 -Project Puppet -SkipUiTests
#                                              Only the test projects affected by the named projects (full or
#                                              short names: Core is the application, other short names are
#                                              relative to it, so Puppet is DemaConsulting.AgentControl.Puppet)
#   Add -DryRun to any scope to print the selection without building anything.
#   With no arguments the usage text is printed and the script exits 1 (a scope must be chosen).
#
#   A changed project selects every project that references it, directly or transitively, from the
#   ProjectReference graph, and files linked into another project (Compile/None/EmbeddedResource items
#   that point outside the project folder) select that project too. Changes that affect the whole
#   build (solution, project, props/targets, global.json, .editorconfig, nuget.config, tool manifest,
#   this script, scripts/ProjectScripts.psm1) fall back to the full build; repository files tests read
#   at run time select their test projects through $RuntimeReads; other changes the build does not
#   read (documentation) select nothing. DemaConsulting.AgentControl.UiTests references the app, so any
#   src/ change selects it: combine with -SkipUiTests to build it without running it while someone is
#   working on the machine.
#
# EXTENSION POINTS:
#   Search for "[PROJECT-SPECIFIC]" comments to find the designated locations
#   for adding project-specific build or test operations.
#
# CUSTOMIZATION:
#   This script belongs to the project: extend it as the project grows. Add steps at the
#   [PROJECT-SPECIFIC] points and add optional parameters that focus a run (here -SkipUiTests).
#   The reusable scoping logic lives in scripts/ProjectScripts.psm1, which is delivered by the
#   Agents release zip, is shared across repositories, and must never be edited here. Slow or
#   environment-dependent test tiers (GUI automation, integration) may be opt-in switches.
#   With no arguments the script prints the usage text and exits 1; -All must still build and run
#   the standard test suite (agents rely on that), and never remove or weaken a test step to make
#   it pass.

param(
    # Full local build and all tests.
    [switch]$All,

    # Build and test only the projects affected by the changed files.
    [switch]$Changed,

    # Git reference to compare against for -Changed (default: origin/main, else main).
    [string]$Base,

    # Build and test only the projects affected by these projects (full or short names, e.g. Core, Puppet).
    [string[]]$Project,

    # Still build everything selected, but run only the headless test projects; skip the FlaUI UI tests
    # (DemaConsulting.AgentControl.UiTests), which launch the app and take over the real desktop. Use it
    # when someone is working on the machine. Without the switch, every selected test project runs.
    [switch]$SkipUiTests,

    # Print the selection and exit without building.
    [switch]$DryRun,

    # Print the usage text and exit 0.
    [switch]$Help
)

Import-Module (Join-Path $PSScriptRoot 'scripts/ProjectScripts.psm1') -Force

# [PROJECT-SPECIFIC] Repository files that tests read at run time (through the repository root, not through a
# project item), mapped to the test project that reads them. Keep in step with the tests. Example:
#   '^docs/user_guide/tool\.md$' = 'SystemName.Tests'
# None today: the tests read only their own build output (the app's deps.json, the built app and ArgLoggerStub
# executables) and files they create in temporary folders, never checked-in repository files.
$RuntimeReads = [ordered]@{}

# [PROJECT-SPECIFIC] The FlaUI test project that launches the app and drives the real desktop; -SkipUiTests never runs it
$UiTestProjectName = 'DemaConsulting.AgentControl.UiTests'

# [PROJECT-SPECIFIC] The headless test projects that -SkipUiTests runs for the full build, passed explicitly so the FlaUI
# UiTests project (which launches the app and drives the real mouse, keyboard and windows) is never run.
# Puppet.Tests is headless: it tests option parsing, planning and arithmetic with fakes and never starts the app,
# moves the cursor or touches a window.
$HeadlessTestProjects = @(
    'test/DemaConsulting.AgentControl.Tests/DemaConsulting.AgentControl.Tests.csproj',
    'tools/DemaConsulting.AgentControl.Puppet.Tests/DemaConsulting.AgentControl.Puppet.Tests.csproj'
)

# Test projects are named *.Tests or *.UiTests; the other projects (the app, the ArgLoggerStub helper under
# test/, the Puppet tool) are built only
function Test-TestProject {
    param($Entry)
    return $Entry.Name -match '\.(Ui)?Tests$'
}

# ==============================================================================
# USAGE AND SCOPE VALIDATION
# ==============================================================================

function Show-Usage {
    Write-ScriptUsage -Script 'build.ps1' -IterateWith '-Changed -SkipUiTests (or -Project)' `
        -Purpose 'build the solution (Release) and run the unit tests.' `
        -Options ([ordered]@{
            '-All'             = 'Full local build and all tests; cannot be combined with another scope.'
            '-Changed'         = 'Only the test projects affected by files changed since the base, plus uncommitted files.'
            '-Base <ref>'      = 'Base reference for -Changed (default origin/main, else main).'
            '-Project <names>' = 'Only the test projects affected by these projects (full or short names, e.g. Puppet).'
            '-SkipUiTests'     = 'Build everything but run only the headless tests; skip the FlaUI tests that drive the desktop.'
            '-DryRun'          = 'Print what would be built, tested, and skipped, then exit 0 without running anything.'
            '-Help'            = 'Print this text and exit 0.'
        }) `
        -ChecksLabel 'Projects (-Project names; Core is the application)' `
        -Checks @(try { (Get-ProjectGraph).Keys } catch { }) `
        -Examples @('-Changed -SkipUiTests', '-Project Puppet -SkipUiTests', '-Changed -DryRun', '-All', '-All -SkipUiTests')
}

if ($Help) { Show-Usage; exit 0 }

$scopeProblems = Test-ScopeArguments -Base $Base -Scopes ([ordered]@{
        All     = $All.IsPresent
        Changed = $Changed.IsPresent
        Project = [bool]$Project
    })
if ($scopeProblems.Count -gt 0) {
    $scopeProblems | ForEach-Object { Write-Host "Error: $_" }
    Write-Host ""
    Show-Usage
    exit 1
}

$fullDescription = if ($SkipUiTests) { 'the full build and the headless tests (-SkipUiTests)' } else { 'the full build and all tests' }
$buildError = $false

# ==============================================================================
# SCOPED BUILD (-Changed / -Project)
# Selects the affected projects; falls back to the full build when needed.
# ==============================================================================

$scoped = $false
if (-not $All) {
    $graph = Get-ProjectGraph
    $selected = [System.Collections.Generic.HashSet[string]]::new()
    $fullReason = $null

    $names = @($Project | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($Project -and $names.Count -eq 0) {
        Write-Host "Error: no project named in '$($Project -join ' ')'. Known projects: $($graph.Keys -join ', ')"
        exit 1
    }
    foreach ($single in $names) {
        $resolved = Resolve-ProjectName $graph $single
        if (-not $resolved) {
            Write-Host "Unknown project '$single'. Known projects: $($graph.Keys -join ', ')"
            exit 1
        }
        [void]$selected.Add($resolved)
    }

    if ($Changed) {
        $changedFiles = Get-ChangedFiles -Base $Base
        if ($null -eq $changedFiles) {
            $fullReason = 'the changed files could not be determined'
        } else {
            Write-Host "Changed files: $($changedFiles.Count)"
            $fullReason = Add-ChangedProjects -Graph $graph -Files $changedFiles -Selected $selected -RuntimeReads $RuntimeReads
        }
    }

    if ($fullReason) {
        Write-Host "Full build required: $fullReason."
    } else {
        Expand-ReferencingProjects -Graph $graph -Selected $selected

        # Test projects are built and tested; other projects are built only. With -SkipUiTests the UI
        # test project is still built (as the full -SkipUiTests build does) but never run.
        $chosen = @($graph.Values | Where-Object { $selected.Contains($_.Name) })
        $tests = @($chosen | Where-Object { (Test-TestProject $_) -and -not ($SkipUiTests -and $_.Name -eq $UiTestProjectName) })
        $others = @($chosen | Where-Object { $tests -notcontains $_ })
        if ($chosen.Count -eq 0) {
            Write-Host "Nothing to build: no changed file is read by the build."
            exit 0
        }

        Write-Host "Affected test projects (built and tested):"
        $tests | ForEach-Object { Write-Host "  $($_.Name)" }
        if ($tests.Count -eq 0) { Write-Host "  (none)" }
        if ($others.Count -gt 0) {
            Write-Host "Affected projects (build only):"
            $others | ForEach-Object { Write-Host "  $($_.Name)" }
        }
        if ($SkipUiTests -and $selected.Contains($UiTestProjectName)) {
            Write-Host "UI tests skipped (-SkipUiTests): $UiTestProjectName is built but not run."
        }
        if ($DryRun) {
            $skipped = @($graph.Values | Where-Object { (Test-TestProject $_) -and -not $selected.Contains($_.Name) })
            Write-Host "Skipped test projects ($($skipped.Count)): $(($skipped | ForEach-Object { $_.Name }) -join ', ')"
            Write-Host "Dry run: nothing was built or tested."
            exit 0
        }

        $scoped = $true
        foreach ($entry in $tests + $others) {
            Write-Host "Building $($entry.Name)..."
            dotnet build $entry.Path --configuration Release
            if ($LASTEXITCODE -ne 0) { $buildError = $true }
        }

        if (-not $buildError) {
            foreach ($entry in $tests) {
                Write-Host "Running unit tests: $($entry.Name)..."
                dotnet test --project $entry.Path --configuration Release --max-parallel-test-modules 1
                if ($LASTEXITCODE -ne 0) { $buildError = $true }
            }
        } else {
            Write-Host "Skipping tests: the build failed."
        }
    }
}

if ($DryRun -and -not $scoped) { Write-Host "Dry run: would run $fullDescription."; exit 0 }

# ==============================================================================
# FULL BUILD (-All, or a scoped run that needs everything)
# ==============================================================================

if (-not $scoped) {
    # Each step runs only if the steps before it succeeded, so tests never run against stale binaries
    Write-Host "Building project..."
    dotnet build --configuration Release
    if ($LASTEXITCODE -ne 0) { $buildError = $true }

    # [PROJECT-SPECIFIC] Add additional build steps here (e.g., packaging, publishing).
    # Guard steps that depend on the build with: if (-not $buildError) { ... }

    if ($buildError) {
        Write-Host "Skipping tests: the build failed."
    } elseif ($SkipUiTests) {
        Write-Host "Running unit tests (UI tests skipped: -SkipUiTests)..."
        foreach ($testProject in $HeadlessTestProjects) {
            dotnet test --project (Join-Path $PSScriptRoot $testProject) --configuration Release --max-parallel-test-modules 1
            if ($LASTEXITCODE -ne 0) { $buildError = $true }
        }
        Write-Host "UI tests skipped (-SkipUiTests): test/DemaConsulting.AgentControl.UiTests was not run."
    } else {
        Write-Host "Running unit tests..."
        # --max-parallel-test-modules 1 forces the Tests and UiTests projects to run one after
        # another rather than concurrently: both spawn real child processes (git subprocesses,
        # FlaUI-driven app instances), and running them at the same time causes intermittent,
        # non-deterministic contention failures between the two (confirmed via live CI runs).
        dotnet test --configuration Release --max-parallel-test-modules 1
        if ($LASTEXITCODE -ne 0) { $buildError = $true }
    }

    # [PROJECT-SPECIFIC] Add additional test or post-build steps here.
}

if (-not $buildError) {
    Write-Host "Build and tests completed successfully!"
}

exit ($buildError ? 1 : 0)
