# build.ps1
#
# PURPOSE:
#   Unified cross-platform build script (replaces build.bat and build.sh).
#   Builds the solution in Release configuration and runs all unit tests.
#
# PARAMETERS:
#   -SkipUiTests  Still builds the whole solution, but runs only the headless test
#                 projects (DemaConsulting.AgentControl.Tests and
#                 DemaConsulting.AgentControl.Puppet.Tests) and skips the FlaUI-based
#                 DemaConsulting.AgentControl.UiTests project, which launches the app and
#                 takes over the real desktop. Use it when someone is working on the
#                 machine. Without the switch, every test project runs as before.
#
# EXTENSION POINTS:
#   Search for "[PROJECT-SPECIFIC]" comments to find the designated locations
#   for adding project-specific build or test operations.
#
# MODIFICATION POLICY:
#   Only modify this file to add project-specific operations at the designated
#   [PROJECT-SPECIFIC] extension points.

[CmdletBinding()]
param(
    # Run only the headless test projects; skip the desktop-driving FlaUI UI tests.
    [switch]$SkipUiTests
)

$ErrorActionPreference = 'Stop'

Write-Host "Building project..."
dotnet build --configuration Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# [PROJECT-SPECIFIC] Add additional build steps here.

if ($SkipUiTests) {
    Write-Host "Running unit tests (UI tests skipped: -SkipUiTests)..."
    # The headless test projects, passed explicitly so the FlaUI UiTests project (which
    # launches the app and drives the real mouse, keyboard and windows) is never run.
    # Puppet.Tests is headless: it tests option parsing, planning and arithmetic with fakes
    # and never starts the app, moves the cursor or touches a window.
    $headlessTestProjects = @(
        'test/DemaConsulting.AgentControl.Tests/DemaConsulting.AgentControl.Tests.csproj',
        'tools/DemaConsulting.AgentControl.Puppet.Tests/DemaConsulting.AgentControl.Puppet.Tests.csproj'
    )
    foreach ($testProject in $headlessTestProjects) {
        dotnet test --project (Join-Path $PSScriptRoot $testProject) --configuration Release --max-parallel-test-modules 1
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    Write-Host "UI tests skipped (-SkipUiTests): test/DemaConsulting.AgentControl.UiTests was not run."
} else {
    Write-Host "Running unit tests..."
    # --max-parallel-test-modules 1 forces the Tests and UiTests projects to run one after
    # another rather than concurrently: both spawn real child processes (git subprocesses,
    # FlaUI-driven app instances), and running them at the same time causes intermittent,
    # non-deterministic contention failures between the two (confirmed via live CI runs).
    dotnet test --configuration Release --max-parallel-test-modules 1
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# [PROJECT-SPECIFIC] Add additional test or post-build steps here.

Write-Host "Build and tests completed successfully!"
