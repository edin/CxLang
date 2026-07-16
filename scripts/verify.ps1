[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

function Invoke-NativeStep {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    Write-Host ""
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

Push-Location $repositoryRoot
try {
    Invoke-NativeStep "Build solution" "dotnet" @(
        "build", "Cx.sln",
        "--configuration", "Release",
        "--verbosity", "minimal"
    )

    Invoke-NativeStep "Run compiler tests" "dotnet" @(
        "test", "Cx.sln",
        "--configuration", "Release",
        "--no-build",
        "--verbosity", "minimal"
    )

    Invoke-NativeStep "Run standard-library tests" "dotnet" @(
        "run", "--project", "src/Cx.Cli",
        "--configuration", "Release",
        "--no-build", "--",
        "test", "--std"
    )

    Invoke-NativeStep "Check configured project" "dotnet" @(
        "run", "--project", "src/Cx.Cli",
        "--configuration", "Release",
        "--no-build", "--",
        "check"
    )

    Invoke-NativeStep "Audit structured AST" "dotnet" @(
        "run", "--project", "src/Cx.Cli",
        "--configuration", "Release",
        "--no-build", "--",
        "check", "--ast-audit", "--include-std"
    )

    Invoke-NativeStep "Audit generic specialization discovery" "dotnet" @(
        "run", "--project", "src/Cx.Cli",
        "--configuration", "Release",
        "--no-build", "--",
        "check", "--generic-raw-audit"
    )

    Invoke-NativeStep "Check diff hygiene" "git" @(
        "-c", "core.safecrlf=false",
        "diff", "--check"
    )

    Write-Host ""
    Write-Host "Verification passed." -ForegroundColor Green
}
finally {
    Pop-Location
}
