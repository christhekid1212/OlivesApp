param(
    [Parameter(Mandatory=$true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string]$RepositoryUrl,
    [switch]$AllowUnconfigured
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    if (-not $RepositoryUrl) { $RepositoryUrl = (Get-Content updates.json -Raw | ConvertFrom-Json).RepositoryUrl }
    if ($RepositoryUrl -and $RepositoryUrl -notmatch '^https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/?$') { throw 'Use https://github.com/OWNER/REPOSITORY.' }
    if (-not $RepositoryUrl -and -not $AllowUnconfigured) { throw 'Set RepositoryUrl before a public release.' }
    $buildFolder = Join-Path $projectRoot ('artifacts/build-' + $Version + '-' + [Guid]::NewGuid().ToString('N'))
    $publishFolder = Join-Path $buildFolder 'publish'
    $releaseFolder = Join-Path $buildFolder 'Releases'
    dotnet publish OlivesApp.csproj -c Release -r win-x64 --self-contained true -p:Version=$Version -o $publishFolder
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    $config = @{ RepositoryUrl = $RepositoryUrl } | ConvertTo-Json
    [IO.File]::WriteAllText((Join-Path $publishFolder 'updates.json'), $config, [Text.UTF8Encoding]::new($false))
    $vpk = Join-Path $projectRoot '.tools/vpk.exe'
    if (-not (Test-Path $vpk)) {
        dotnet tool install vpk --version 1.2.0 --tool-path .tools
        if ($LASTEXITCODE -ne 0) { throw 'Velopack installation failed.' }
    }
    & $vpk pack --packId OlivesApp --packTitle OlivesApp --packVersion $Version --packDir $publishFolder --mainExe OlivesApp.exe --icon Assets/olive.ico --shortcuts 'Desktop,StartMenuRoot' --channel win --runtime win-x64 --noPortable --outputDir $releaseFolder
    if ($LASTEXITCODE -ne 0) { throw 'Packaging failed.' }
    Write-Host "Release files: $releaseFolder"
    Write-Host 'Upload ALL files in this Releases folder to the same GitHub Release.'
} finally { Pop-Location }
