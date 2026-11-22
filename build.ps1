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
#   -Affected     Builds and tests only the projects affected by the files changed since -Base
#                 plus uncommitted and untracked files. A changed file selects the project that
#                 owns it (or links it in), then every project that references that project,
#                 directly or transitively. Changes that affect the whole build (solution,
#                 project, props/targets, global.json, .editorconfig, nuget.config, the tool
#                 manifest, this script) fall back to the full build; changes the build does not
#                 read (documentation) build nothing. DemaConsulting.AgentControl.UiTests
#                 references the app, so any src/ change selects it: combine with -SkipUiTests
#                 to build it without running it while someone is working on the machine.
#   -Base <ref>   Git reference -Affected compares against (default origin/main, else main).
#   -Project      Builds and tests only the projects affected by the named projects (full or
#                 short names, for example Puppet, Tests, or Core for the application). Combines
#                 with -SkipUiTests the same way as -Affected.
#   -DryRun       Prints the selection and exits without building anything.
#
# USAGE:
#   pwsh ./build.ps1                                        Full build and all tests
#   pwsh ./build.ps1 -SkipUiTests                           Full build, headless tests only
#   pwsh ./build.ps1 -Affected -SkipUiTests [-Base <ref>]   Only the affected headless test projects
#   pwsh ./build.ps1 -Project Puppet -SkipUiTests           Only the projects affected by Puppet
#   Add -DryRun to print the selection without building anything.
#
# CUSTOMIZATION:
#   This script belongs to the project: extend it as the project grows. Add steps at the
#   [PROJECT-SPECIFIC] points and add optional parameters that focus a run (here -SkipUiTests
#   and the scoped -Affected/-Project/-DryRun modes). Slow or environment-dependent test tiers
#   (GUI automation, integration) may be opt-in switches.
#   Run with no arguments it must still build and run the standard test suite (agents rely
#   on that), and never remove or weaken a test step to make it pass.

[CmdletBinding()]
param(
    # Run only the headless test projects; skip the desktop-driving FlaUI UI tests.
    [switch]$SkipUiTests,

    # Build and test only the projects affected by the changed files.
    [switch]$Affected,

    # Git reference to compare against for -Affected (default: origin/main, else main).
    [string]$Base,

    # Build and test only the projects affected by these projects (full or short names).
    [string[]]$Project,

    # Print the selection and exit without building.
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

# ==============================================================================
# HELPER FUNCTIONS (scoped modes only)
# ==============================================================================

function Get-ChangedFiles {
    param([string]$Base)

    # Default to origin/main, falling back to the local main branch
    if (-not $Base) {
        $Base = 'origin/main'
        git -C $PSScriptRoot rev-parse --verify --quiet "$Base^{commit}" > $null 2>&1
        if ($LASTEXITCODE -ne 0) { $Base = 'main' }
    }
    git -C $PSScriptRoot rev-parse --verify --quiet "$Base^{commit}" > $null 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Cannot resolve base reference '$Base'."
        return $null
    }
    Write-Host "Changes since: $Base (plus uncommitted and untracked files)"

    # Committed changes since the merge-base, then staged, unstaged, and untracked files
    $committed = @(git -C $PSScriptRoot -c core.quotePath=false diff --name-only --no-renames "$Base...HEAD")
    if ($LASTEXITCODE -ne 0) { return $null }
    $working = @(git -C $PSScriptRoot -c core.quotePath=false status --porcelain --untracked-files=all --no-renames)
    if ($LASTEXITCODE -ne 0) { return $null }

    $files = $committed + @($working | ForEach-Object { $_.Substring(3).Trim('"') })
    return , @($files | Where-Object { $_ } | ForEach-Object { $_ -replace '\\', '/' } | Sort-Object -Unique)
}

function Resolve-RepoPath {
    param([string]$Directory, [string]$RelativePath)

    # Combine a repository-relative directory with a relative path, collapsing '.' and '..'
    $segments = [System.Collections.Generic.List[string]]::new()
    foreach ($segment in "$Directory/$RelativePath" -replace '\\', '/' -split '/') {
        if ($segment -eq '..') { if ($segments.Count -gt 0) { $segments.RemoveAt($segments.Count - 1) } }
        elseif ($segment -and $segment -ne '.') { $segments.Add($segment) }
    }
    return $segments -join '/'
}

function Convert-GlobToRegex {
    param([string]$Glob)

    # MSBuild wildcards: '**' spans folders, '*' and '?' stay within one folder
    $regex = [regex]::Escape($Glob)
    $regex = $regex -replace '\\\*\\\*/', '(.*/)?' -replace '\\\*\\\*', '.*' -replace '\\\*', '[^/]*' -replace '\\\?', '[^/]'
    return "^$regex$"
}

function Get-ProjectGraph {
    # Reads every project under src/, test/, and tools/: its folder, its project references,
    # and the files outside its folder that it compiles, embeds, or copies (linked files).
    $graph = [ordered]@{}
    $roots = @('src', 'test', 'tools') | ForEach-Object { Join-Path $PSScriptRoot $_ }
    $files = Get-ChildItem -Path $roots -Recurse -Filter *.csproj -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[/\\](bin|obj)[/\\]' } |
        Sort-Object FullName
    foreach ($file in $files) {
        $path = [System.IO.Path]::GetRelativePath($PSScriptRoot, $file.FullName) -replace '\\', '/'
        $directory = (Split-Path $path -Parent) -replace '\\', '/'
        [xml]$xml = Get-Content -Raw $file.FullName

        $references = @()
        $links = @()
        foreach ($item in $xml.SelectNodes('/Project/ItemGroup/*')) {
            $include = $item.GetAttribute('Include')
            if (-not $include) { continue }

            if ($item.LocalName -eq 'ProjectReference') {
                $references += [System.IO.Path]::GetFileNameWithoutExtension(($include -replace '\\', '/'))
                continue
            }
            if ($item.LocalName -notin @('Compile', 'None', 'Content', 'EmbeddedResource')) { continue }

            # Files only packed into the NuGet package (README.md, Icon.png, licenses) do not affect tests
            $pack = $item.GetAttribute('Pack') + $item.SelectSingleNode('Pack')?.InnerText
            $copy = $item.GetAttribute('CopyToOutputDirectory') + $item.SelectSingleNode('CopyToOutputDirectory')?.InnerText
            if ($item.LocalName -eq 'None' -and $pack -eq 'true' -and -not $copy) { continue }

            foreach ($part in $include -split ';') {
                $resolved = Resolve-RepoPath $directory $part.Trim()
                if (-not $resolved.StartsWith("$directory/", [System.StringComparison]::OrdinalIgnoreCase)) {
                    $links += Convert-GlobToRegex $resolved
                }
            }
        }

        $name = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
        $graph[$name] = [pscustomobject]@{
            Name       = $name
            Path       = $path
            Directory  = $directory
            References = $references
            Links      = $links
        }
    }
    return $graph
}

function Test-BuildWideFile {
    param([string]$File)

    # Files that configure the whole build: any of them changing needs the full build
    $leaf = Split-Path $File -Leaf
    return ($leaf -match '^(Directory\.Build\.(props|targets|rsp)|Directory\.Packages\.props|global\.json|nuget\.config|\.editorconfig|\.globalconfig)$') -or
        ($File -match '\.(sln|slnx|slnf|csproj|props|targets)$') -or
        ($File -in @('.config/dotnet-tools.json', 'build.ps1'))
}

function Test-TestProject {
    param($Entry)

    # Test projects are named *.Tests or *.UiTests; other projects (the app, the ArgLoggerStub
    # helper under test/, the Puppet tool) are built only
    return $Entry.Name -match '\.(Ui)?Tests$'
}

# [PROJECT-SPECIFIC] Repository files that tests read at run time (through the repository root, not through a
# project item), and the test projects that read them. Keep in step with the tests.
# None today: the tests read only their own build output (the app's deps.json, the built app and ArgLoggerStub
# executables) and files they create in temporary folders, never checked-in repository files.
$RuntimeReads = [ordered]@{}

# The FlaUI test project that launches the app and drives the real desktop; -SkipUiTests never runs it
$UiTestProjectName = 'DemaConsulting.AgentControl.UiTests'

function Resolve-ProjectName {
    param($Graph, [string]$Name)

    # Accept full names, names without the application prefix (Tests, Puppet), or Core for the application
    $core = $Graph.Keys | Where-Object { $Graph[$_].Directory -like 'src/*' } |
        Sort-Object Length | Select-Object -First 1
    if ($Name -eq 'Core') { return $core }
    foreach ($candidate in @($Name, "$core.$Name")) {
        $match = $Graph.Keys | Where-Object { $_ -eq $candidate }
        if ($match) { return $match }
    }
    return $null
}

# ==============================================================================
# SCOPED BUILD (-Affected / -Project)
# Selects the affected projects; falls back to the full build when needed.
# ==============================================================================

if ($Affected -or $Project) {
    $graph = Get-ProjectGraph
    $selected = [System.Collections.Generic.HashSet[string]]::new()
    $fullReason = $null

    foreach ($name in $Project) {
        foreach ($single in $name -split ',') {
            $resolved = Resolve-ProjectName $graph $single.Trim()
            if (-not $resolved) {
                Write-Host "Unknown project '$single'. Known projects: $($graph.Keys -join ', ')"
                exit 1
            }
            [void]$selected.Add($resolved)
        }
    }

    if ($Affected) {
        $changed = Get-ChangedFiles -Base $Base
        if ($null -eq $changed) {
            $fullReason = 'the changed files could not be determined'
        } else {
            Write-Host "Changed files: $($changed.Count)"
            foreach ($file in $changed) {
                if (Test-BuildWideFile $file) { $fullReason = "$file affects the whole build"; break }
                foreach ($pattern in $RuntimeReads.Keys) {
                    if ($file -match $pattern -and $graph.Contains($RuntimeReads[$pattern])) { [void]$selected.Add($RuntimeReads[$pattern]) }
                }
                foreach ($entry in $graph.Values) {
                    $owned = $file.StartsWith("$($entry.Directory)/", [System.StringComparison]::OrdinalIgnoreCase)
                    $linked = @($entry.Links | Where-Object { $file -match $_ }).Count -gt 0
                    if ($owned -or $linked) { [void]$selected.Add($entry.Name) }
                }
            }
        }
    }

    if ($fullReason) {
        Write-Host "Full build required: $fullReason."
        if ($DryRun) {
            if ($SkipUiTests) { Write-Host "Dry run: would run the full build and the headless tests (-SkipUiTests)." }
            else { Write-Host "Dry run: would run the full build and all tests." }
            exit 0
        }
    } else {
        # Add every project that references a selected project, directly or transitively
        $queue = [System.Collections.Generic.Queue[string]]::new([string[]]@($selected))
        while ($queue.Count -gt 0) {
            $current = $queue.Dequeue()
            foreach ($entry in $graph.Values) {
                if ($entry.References -contains $current -and $selected.Add($entry.Name)) { $queue.Enqueue($entry.Name) }
            }
        }

        # Test projects are built and tested; other projects are built only. With -SkipUiTests the UI
        # test project is still built (as the full -SkipUiTests build does) but never run.
        $chosen = @($graph.Values | Where-Object { $selected.Contains($_.Name) })
        $tests = @($chosen | Where-Object { (Test-TestProject $_) -and -not ($SkipUiTests -and $_.Name -eq $UiTestProjectName) })
        $others = @($chosen | Where-Object { $tests -notcontains $_ })
        if ($chosen.Count -eq 0) {
            Write-Host "Nothing to build: no changed file is read by the build."
            exit 0
        }

        Write-Host "Affected test projects:"
        $tests | ForEach-Object { Write-Host "  $($_.Name)" }
        if ($tests.Count -eq 0) { Write-Host "  (none)" }
        if ($others.Count -gt 0) {
            Write-Host "Affected projects (build only):"
            $others | ForEach-Object { Write-Host "  $($_.Name)" }
        }
        if ($SkipUiTests -and $selected.Contains($UiTestProjectName)) {
            Write-Host "UI tests skipped (-SkipUiTests): $UiTestProjectName is built but not run."
        }
        if ($DryRun) { exit 0 }

        foreach ($entry in $tests + $others) {
            Write-Host "Building $($entry.Name)..."
            dotnet build (Join-Path $PSScriptRoot $entry.Path) --configuration Release
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        }

        foreach ($entry in $tests) {
            Write-Host "Running unit tests: $($entry.Name)..."
            dotnet test --project (Join-Path $PSScriptRoot $entry.Path) --configuration Release --max-parallel-test-modules 1
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        }

        Write-Host "Affected build and tests completed successfully!"
        exit 0
    }
}

if ($DryRun) {
    if ($SkipUiTests) { Write-Host "Dry run: would run the full build and the headless tests (-SkipUiTests)." }
    else { Write-Host "Dry run: would run the full build and all tests." }
    exit 0
}

# ==============================================================================
# FULL BUILD
# ==============================================================================

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
