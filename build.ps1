$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

Push-Location $root
try {
    dotnet restore .\MianMianPet.slnx
    dotnet build .\MianMianPet.slnx -c Debug --no-restore
    Write-Host "Build succeeded." -ForegroundColor Green
}
finally {
    Pop-Location
}
