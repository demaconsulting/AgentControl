# lint.ps1
#
# PURPOSE:
#   Runs all lint checks and reports failures. Exits 1 on error.
#   Used by CI/CD as the merge gate and by the lint-fix agent
#   during pre-PR cleanup.
#
#   To auto-fix formatting issues, run fix.ps1 instead.
#
# PARAMETERS:
#   -Changed      Runs only the checks whose inputs changed since -Base plus uncommitted and
#                 untracked files: yamllint for YAML, TextLint for the spell-checked file types,
#                 LintMark for Markdown, and each compliance tool only when its inputs changed
#                 (a tool manifest change runs them all). dotnet format is limited to the changed
#                 .cs files (at most 200), or covers the whole solution when a project or build
#                 configuration file changed. Each skipped check is printed. When the changed
#                 files cannot be determined, every check runs.
#   -Base <ref>   Git reference -Changed compares against (default origin/main, else main).
#   -DryRun       Prints which checks would run and exits without running them.
#
# USAGE:
#   pwsh ./lint.ps1                          All checks (CI merge gate)
#   pwsh ./lint.ps1 -Changed [-Base <ref>]   Only the checks whose inputs changed
#   Add -DryRun to print which checks would run without running them.
#
# CUSTOMIZATION:
#   This script belongs to the project: extend it as the project grows. Add steps at the
#   [PROJECT-SPECIFIC] points, update tool versions, and add optional parameters that focus
#   a run (here the scoped -Changed/-DryRun modes).
#   Run with no arguments it must still run every check (CI relies on that), and never
#   remove or weaken a check to make it pass.

param(
    # Run only the checks whose inputs changed.
    [switch]$Changed,

    # Git reference to compare against for -Changed (default: origin/main, else main).
    [string]$Base,

    # Print which checks would run and exit without running them.
    [switch]$DryRun
)

function Get-VenvActivateScript {
    if (Test-Path ".venv/Scripts/Activate.ps1") { return ".venv/Scripts/Activate.ps1" }
    if (Test-Path ".venv/bin/Activate.ps1") { return ".venv/bin/Activate.ps1" }
    return $null
}

function Initialize-PythonVenv {
    if (-not (Test-Path ".venv")) {
        python -m venv .venv
        if ($LASTEXITCODE -ne 0) { return $false }
    }

    $activateScript = Get-VenvActivateScript
    if (-not $activateScript) { return $false }
    & $activateScript
    if (-not (Get-Command deactivate -ErrorAction SilentlyContinue)) { return $false }

    $installSucceeded = $false
    try {
        pip install -r pip-requirements.txt --quiet --disable-pip-version-check
        $installSucceeded = $LASTEXITCODE -eq 0
        return $installSucceeded
    }
    finally {
        if (-not $installSucceeded -and (Get-Command deactivate -ErrorAction SilentlyContinue)) {
            deactivate 2>$null
        }
    }
}

function Get-ChangedFiles {
    param([string]$Base)

    # Default to origin/main, falling back to the local main branch
    if (-not $Base) {
        $Base = 'origin/main'
        git rev-parse --verify --quiet "$Base^{commit}" > $null 2>&1
        if ($LASTEXITCODE -ne 0) { $Base = 'main' }
    }
    git rev-parse --verify --quiet "$Base^{commit}" > $null 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Cannot resolve base reference '$Base'."
        return $null
    }
    Write-Host "Changes since: $Base (plus uncommitted and untracked files)"

    # Committed changes since the merge-base, then staged, unstaged, and untracked files
    $committed = @(git -c core.quotePath=false diff --name-only --no-renames "$Base...HEAD")
    if ($LASTEXITCODE -ne 0) { return $null }
    $working = @(git -c core.quotePath=false status --porcelain --untracked-files=all --no-renames)
    if ($LASTEXITCODE -ne 0) { return $null }

    $files = $committed + @($working | ForEach-Object { $_.Substring(3).Trim('"') })
    return , @($files | Where-Object { $_ } | ForEach-Object { $_ -replace '\\', '/' } | Sort-Object -Unique)
}

function Test-AnyMatch {
    param([string[]]$Files, [string]$Pattern)
    return @($Files | Where-Object { $_ -match $Pattern }).Count -gt 0
}

function Get-ReviewMarkPattern {
    # Builds one regex from the 'needs-review' globs in .reviewmark.yaml ('!' excludes are ignored,
    # which can only run the check more often); returns a match-all pattern if none are found
    $globs = @()
    $inList = $false
    foreach ($line in Get-Content .reviewmark.yaml -ErrorAction SilentlyContinue) {
        if ($line -match '^needs-review:') { $inList = $true; continue }
        if ($inList -and $line -match '^\s+-\s+["'']?([^"''#\s]+)') {
            if (-not $Matches[1].StartsWith('!')) { $globs += $Matches[1] }
        } elseif ($inList -and $line -match '^\S') { break }
    }
    if ($globs.Count -eq 0) { return '.' }

    $regexes = foreach ($glob in $globs) {
        $regex = [regex]::Escape($glob)
        $regex = $regex -replace '\\\*\\\*/', '(.*/)?' -replace '\\\*\\\*', '.*' -replace '\\\*', '[^/]*' -replace '\\\?', '[^/]'
        "^$regex$"
    }
    return ($regexes -join '|')
}

# --- Check Selection ---
# Without -Changed every check runs. With -Changed a check runs only when a file it reads
# changed; when the changed files cannot be determined, every check runs.
$run = @{
    Yaml        = $true
    Spelling    = $true
    Markdown    = $true
    ReqStream   = $true
    VersionMark = $true
    ReviewMark  = $true
    SysML       = $true
    Format      = $true
}
$formatInclude = @()

if ($Changed) {
    $files = Get-ChangedFiles -Base $Base
    if ($null -eq $files) {
        Write-Host "Running all checks: the changed files could not be determined."
    } else {
        Write-Host "Changed files: $($files.Count)"

        # A tool manifest change can change any dotnet tool, so it runs them all
        $toolManifest = Test-AnyMatch $files '^\.config/dotnet-tools\.json$'

        # yamllint: YAML files (including .yamllint.yaml) and its Python requirements
        $run.Yaml = Test-AnyMatch $files '\.ya?ml$|^pip-requirements\.txt$'

        # TextLint: the file types in the 'files' setting of .textlint.yaml (and that YAML file itself)
        $run.Spelling = $toolManifest -or (Test-AnyMatch $files '\.(md|yaml|yml|json|cs|cpp|hpp|h|txt)$')

        # LintMark: Markdown files, .lintmark.yaml, and .editorconfig (LintMark reads both)
        $run.Markdown = $toolManifest -or (Test-AnyMatch $files '\.md$|(^|/)\.lintmark\.yaml$|(^|/)\.editorconfig$')

        # ReqStream: the requirements tree
        $run.ReqStream = $toolManifest -or (Test-AnyMatch $files '^requirements\.yaml$|^docs/reqstream/')

        # VersionMark: its definition file
        $run.VersionMark = $toolManifest -or (Test-AnyMatch $files '^\.versionmark\.yaml$')

        # ReviewMark: its definition and the files its 'needs-review' patterns track
        $run.ReviewMark = $toolManifest -or (Test-AnyMatch $files "^\.reviewmark\.yaml$|$(Get-ReviewMarkPattern)")

        # SysML2Tools: the architecture model
        $run.SysML = $toolManifest -or (Test-AnyMatch $files '^docs/sysml2/')

        # dotnet format: the changed .cs files only, or everything when project or build configuration changed
        $projectChanged = Test-AnyMatch $files `
            '\.(csproj|props|targets|sln|slnx)$|(^|/)(\.editorconfig|\.globalconfig|global\.json|nuget\.config)$'
        $csFiles = @($files | Where-Object { $_ -match '\.cs$' -and (Test-Path -LiteralPath $_) })
        $run.Format = $projectChanged -or $csFiles.Count -gt 0
        if (-not $projectChanged -and $csFiles.Count -gt 0 -and $csFiles.Count -le 200) { $formatInclude = $csFiles }
    }
}

if ($DryRun) {
    foreach ($check in 'Yaml', 'Spelling', 'Markdown', 'ReqStream', 'VersionMark', 'ReviewMark', 'SysML', 'Format') {
        Write-Host ("  {0,-12} {1}" -f $check, ($run[$check] ? 'run' : 'skip'))
    }
    if ($formatInclude.Count -gt 0) { Write-Host "  dotnet format limited to: $($formatInclude -join ' ')" }
    exit 0
}

$lintError = $false

# --- YAML ---
if ($run.Yaml) {
    Write-Host "Linting: YAML..."
    $skipPython = -not (Initialize-PythonVenv)
    if ($skipPython) { $lintError = $true }

    if (-not $skipPython) {
        yamllint .
        if ($LASTEXITCODE -ne 0) { $lintError = $true }
        deactivate
    }
} else {
    Write-Host "Linting: YAML... skipped (no YAML files changed)"
}

# --- dotnet Tools ---
$skipDotnetTools = $true
if ($run.Spelling -or $run.Markdown -or $run.ReqStream -or $run.VersionMark -or $run.ReviewMark -or $run.SysML) {
    $skipDotnetTools = $false
    dotnet tool restore > $null
    if ($LASTEXITCODE -ne 0) { $lintError = $true; $skipDotnetTools = $true }
}

# --- Spelling ---
if ($run.Spelling) {
    Write-Host "Linting: spelling..."
    if (-not $skipDotnetTools) {
        dotnet textlint
        if ($LASTEXITCODE -ne 0) { $lintError = $true }
    }
} else {
    Write-Host "Linting: spelling... skipped (no spell-checked files changed)"
}

# --- Markdown ---
if ($run.Markdown) {
    Write-Host "Linting: markdown..."
    if (-not $skipDotnetTools) {
        dotnet lintmark
        if ($LASTEXITCODE -ne 0) { $lintError = $true }
    }
} else {
    Write-Host "Linting: markdown... skipped (no Markdown files changed)"
}

# [PROJECT-SPECIFIC] Add additional lint checks here.

# --- Compliance Tools ---
Write-Host "Linting: compliance tools..."
if (-not ($run.ReqStream -or $run.VersionMark -or $run.ReviewMark -or $run.SysML)) {
    Write-Host "  skipped (no compliance tool inputs changed)"
} elseif (-not $skipDotnetTools) {
    if ($run.ReqStream) {
        dotnet reqstream --lint --enforce --requirements requirements.yaml
        if ($LASTEXITCODE -ne 0) { $lintError = $true }
    } else {
        Write-Host "  reqstream skipped (no requirements files changed)"
    }

    if ($run.VersionMark) {
        dotnet versionmark --lint
        if ($LASTEXITCODE -ne 0) { $lintError = $true }
    } else {
        Write-Host "  versionmark skipped (.versionmark.yaml not changed)"
    }

    if ($run.ReviewMark) {
        dotnet reviewmark --lint
        if ($LASTEXITCODE -ne 0) { $lintError = $true }
    } else {
        Write-Host "  reviewmark skipped (no reviewed files changed)"
    }

    if ($run.SysML -and (Test-Path docs/sysml2)) {
        dotnet sysml2tools lint --strict-unresolved 'docs/sysml2/**/*.sysml'
        if ($LASTEXITCODE -ne 0) { $lintError = $true }
    } elseif (-not $run.SysML) {
        Write-Host "  sysml2tools skipped (no SysML2 model files changed)"
    }
}

# [PROJECT-SPECIFIC] Add additional dotnet tool checks here.

# --- dotnet Format ---
if ($run.Format) {
    if ($formatInclude.Count -gt 0) {
        Write-Host "Linting: dotnet format ($($formatInclude.Count) changed C# files)..."
    } else {
        Write-Host "Linting: dotnet format..."
    }
    $skipDotnetFormat = $false
    dotnet restore > $null
    if ($LASTEXITCODE -ne 0) { $lintError = $true; $skipDotnetFormat = $true }

    if (-not $skipDotnetFormat) {
        $formatArgs = @('--verify-no-changes', '--no-restore')
        if ($formatInclude.Count -gt 0) { $formatArgs += @('--include') + $formatInclude }
        dotnet format @formatArgs
        if ($LASTEXITCODE -ne 0) { $lintError = $true }
    }
} else {
    Write-Host "Linting: dotnet format... skipped (no C# or project files changed)"
}

# [PROJECT-SPECIFIC] C/C++ clang-format check example:
#   Get-ChildItem -Recurse -Include "*.cpp","*.hpp","*.h","*.c" |
#       Where-Object { $_.FullName -notmatch '[/\\](thirdparty|third-party|3rd-party|generated)[/\\]' } |
#       ForEach-Object {
#           $result = clang-format --dry-run --Werror $_.FullName 2>&1
#           if ($LASTEXITCODE -ne 0) { Write-Output $result; $lintError = $true }
#       }

exit ($lintError ? 1 : 0)
