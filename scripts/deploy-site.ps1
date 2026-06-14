param(
    [string]$HostName = "cxlang",
    [string]$RemotePath = "/var/www/cxlang",
    [string]$RemoteOwner = "www-data:www-data"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$siteRoot = Join-Path $repoRoot "site"
$distRoot = Join-Path $siteRoot "dist"
$remoteTemp = "/tmp/cxlang-site-deploy"

Push-Location $siteRoot
try {
    npm ci
    npm run build
}
finally {
    Pop-Location
}

if (-not (Test-Path $distRoot)) {
    throw "Build output was not found at $distRoot"
}

Write-Host "Publishing site/dist to ${HostName}:${RemotePath}"

tar -C $distRoot -czf - . |
    ssh $HostName "set -e; rm -rf '$remoteTemp'; mkdir -p '$remoteTemp'; tar -xzf - -C '$remoteTemp'; mkdir -p '$RemotePath'; rsync -a --delete '$remoteTemp/' '$RemotePath/'; chown -R '$RemoteOwner' '$RemotePath'; rm -rf '$remoteTemp'"

Write-Host "Deploy complete: ${HostName}:${RemotePath}"
