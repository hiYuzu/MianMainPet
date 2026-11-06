$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$publishDir = Join-Path $root 'publish\win-x64'
$publishExe = Join-Path $publishDir 'MianMianPet.exe'

Push-Location $root
try {
    if (Get-Process -Name 'MianMianPet' -ErrorAction SilentlyContinue | Where-Object Path -EQ $publishExe) {
        throw "MianMianPet is running from '$publishExe'. Exit it from the system tray before publishing."
    }

    dotnet restore .\MianMianPet.slnx -r win-x64
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed (exit code $LASTEXITCODE)."
    }

    dotnet publish .\MianMianPet\MianMianPet.csproj `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishProfile=WinX64 `
        --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed (exit code $LASTEXITCODE)."
    }

    Write-Host "Published to $publishDir" -ForegroundColor Green
}
finally {
    Pop-Location
}
