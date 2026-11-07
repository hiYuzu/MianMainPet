$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

Push-Location $root
try {
    dotnet restore .\MianMianPet.slnx
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed (exit code $LASTEXITCODE)."
    }
    dotnet build .\MianMianPet.slnx -c Debug --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed (exit code $LASTEXITCODE)."
    }
    Write-Host "Build succeeded." -ForegroundColor Green
}
finally {
    Pop-Location
}
