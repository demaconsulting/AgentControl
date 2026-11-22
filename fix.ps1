# fix.ps1
#
# PURPOSE:
#   Applies all available auto-fixers with progress output. Always exits 0.
#   Run this after making changes to automatically handle formatting
#   so agents and developers do not need to respond to lint output.
#   Handles: dotnet format, LintMark (markdown), yamlfix, YAML line endings.
#
# PARAMETERS:
#   -Changed      Runs only the fixers for the file types changed since -Base plus uncommitted
#                 and untracked files: yamlfix for YAML, LintMark for Markdown, and dotnet format
#                 limited to the changed .cs files (at most 200), or the whole solution when a
#                 project or build configuration file changed. When the changed files cannot be
#                 determined, every fixer runs.
#   -Base <ref>   Git reference -Changed compares against (default origin/main, else main).
#
# USAGE:
#   pwsh ./fix.ps1                          All fixers
#   pwsh ./fix.ps1 -Changed [-Base <ref>]   Only the fixers for the changed file types
#
# CUSTOMIZATION:
#   This script belongs to the project: extend it as the project grows. Add steps at the
#   [PROJECT-SPECIFIC] points, update tool versions, and add optional parameters that focus
#   a run (here the scoped -Changed mode).
#   Run with no arguments it must still apply every fixer, and it always exits 0.

param(
    # Run only the fixers for the changed file types.
    [switch]$Changed,

    # Git reference to compare against for -Changed (default: origin/main, else main).
    [string]$Base
)

function Get-VenvActivateScript {
    if (Test-Path ".venv/Scripts/Activate.ps1") { return ".venv/Scripts/Activate.ps1" }
    if (Test-Path ".venv/bin/Activate.ps1") { return ".venv/bin/Activate.ps1" }
    return $null
}

function Initialize-PythonVenv {
    param([switch]$Silent)

    if (-not (Test-Path ".venv")) {
        if ($Silent) { python -m venv .venv 2>$null } else { python -m venv .venv }
        if ($LASTEXITCODE -ne 0) { return $false }
    }

    $activateScript = Get-VenvActivateScript
    if (-not $activateScript) { return $false }
    if ($Silent) { & $activateScript 2>$null } else { & $activateScript }
    if (-not (Get-Command deactivate -ErrorAction SilentlyContinue)) { return $false }

    $installSucceeded = $false
    try {
        if ($Silent) {
            pip install -r pip-requirements.txt --quiet --disable-pip-version-check 2>$null
        } else {
            pip install -r pip-requirements.txt --quiet --disable-pip-version-check
        }
        $installSucceeded = $LASTEXITCODE -eq 0
        return $installSucceeded
    }
    finally {
        if (-not $installSucceeded -and (Get-Command deactivate -ErrorAction SilentlyContinue)) {
            deactivate 2>$null
        }
    }
}

function Normalize-YamlLineEndings {
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)

    Get-ChildItem -Recurse -Include "*.yaml", "*.yml" |
        Where-Object { $_.FullName -notmatch '[/\\](\.git|node_modules|\.venv|thirdparty|third-party|3rd-party|\.agent-logs|generated)[/\\]' } |
        ForEach-Object {
            $raw = [System.IO.File]::ReadAllText($_.FullName)
            $fixed = $raw.Replace("`r`n", "`n")
            if ($raw -ne $fixed) {
                [System.IO.File]::WriteAllText($_.FullName, $fixed, $utf8NoBom)
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

# --- Fixer Selection ---
# Without -Changed every fixer runs. With -Changed a fixer runs only when a file type it
# handles changed; when the changed files cannot be determined, every fixer runs.
$fixYaml = $true
$fixMarkdown = $true
$fixDotnet = $true
$formatInclude = @()

if ($Changed) {
    $files = Get-ChangedFiles -Base $Base
    if ($null -eq $files) {
        Write-Host "Running all fixers: the changed files could not be determined."
    } else {
        Write-Host "Changed files: $($files.Count)"
        $fixYaml = Test-AnyMatch $files '\.ya?ml$|^\.yamlfix\.toml$'
        $fixMarkdown = Test-AnyMatch $files '\.md$|(^|/)\.lintmark\.yaml$|(^|/)\.editorconfig$'
        $projectChanged = Test-AnyMatch $files `
            '\.(csproj|props|targets|sln|slnx)$|(^|/)(\.editorconfig|\.globalconfig|global\.json|nuget\.config)$'
        $csFiles = @($files | Where-Object { $_ -match '\.cs$' -and (Test-Path -LiteralPath $_) })
        $fixDotnet = $projectChanged -or $csFiles.Count -gt 0
        if (-not $projectChanged -and $csFiles.Count -gt 0 -and $csFiles.Count -le 200) { $formatInclude = $csFiles }
    }
}

# --- YAML Auto-Fix ---
if ($fixYaml) {
    Write-Host "Fixing: YAML..."
    if (Initialize-PythonVenv -Silent) {
        yamlfix . 2>$null
        deactivate 2>$null
    }
    Normalize-YamlLineEndings
} else {
    Write-Host "Fixing: YAML... skipped (no YAML files changed)"
}

# --- Markdown Auto-Fix ---
if ($fixMarkdown) {
    Write-Host "Fixing: markdown..."
    dotnet tool restore > $null 2>&1
    if ($LASTEXITCODE -eq 0) {
        dotnet lintmark --fix 2>$null
    }
} else {
    Write-Host "Fixing: markdown... skipped (no Markdown files changed)"
}

# [PROJECT-SPECIFIC] Add additional auto-fixers here.
# C/C++ example:
#   Get-ChildItem -Recurse -Include "*.cpp","*.hpp","*.h" |
#       ForEach-Object { clang-format -i $_.FullName } 2>$null

# --- .NET Auto-Format ---
if ($fixDotnet) {
    if ($formatInclude.Count -gt 0) {
        Write-Host "Fixing: dotnet format ($($formatInclude.Count) changed C# files)..."
    } else {
        Write-Host "Fixing: dotnet format..."
    }
    $slnFiles = @(Get-ChildItem -Filter "*.sln" -ErrorAction SilentlyContinue) +
                @(Get-ChildItem -Filter "*.slnx" -ErrorAction SilentlyContinue)
    if ($slnFiles.Count -gt 0) {
        $formatArgs = @()
        if ($formatInclude.Count -gt 0) { $formatArgs = @('--include') + $formatInclude }
        dotnet format @formatArgs 2>$null
    }
} else {
    Write-Host "Fixing: dotnet format... skipped (no C# or project files changed)"
}

Write-Host "Auto-fix complete."
exit 0
