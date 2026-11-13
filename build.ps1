#requires -Version 5.1
<#
.SYNOPSIS
    Test en bouwt PraktijkClean-<versie>.exe (Windows).

.DESCRIPTION
    1. Voert de scripttests uit met Windows PowerShell 5.1 (tijdelijke testmappen, fictieve bestanden).
    2. Voert de C#-tests uit.
    3. Bouwt PraktijkClean-<versie>.exe (.NET Framework 4.8, standaard aanwezig in Windows 10/11) en zet die in .\dist.
       De versie staat in src\PraktijkClean\PraktijkClean.csproj (<Version>).
       Het is één los bestand van enkele honderden kB; er hoeft niets extra's geïnstalleerd te worden op de werkplek.

    Uitvoeren vanuit de projectmap:
        powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
    (-ExecutionPolicy Bypass geldt alleen voor dit ene bouwproces op de ontwikkelcomputer.)

.PARAMETER SkipTests
    Sla de tests over (niet aanbevolen).
#>
param([switch]$SkipTests)

$ErrorActionPreference = 'Stop'
Set-Location -Path $PSScriptRoot

function Step([string]$Text) { Write-Host "`n==> $Text" -ForegroundColor Cyan }

Step 'Controleren of de .NET SDK aanwezig is'
try { $sdk = (& dotnet --version) } catch { throw 'De .NET SDK is niet gevonden. Installeer de .NET SDK (8 of nieuwer, zie README).' }
Write-Host ".NET SDK $sdk"

if (-not $SkipTests) {
    Step 'Scripttests (Windows PowerShell 5.1)'
    & "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File '.\tests\Script\Run-ScriptTests.ps1'
    if ($LASTEXITCODE -ne 0) { throw 'Scripttests mislukt.' }

    Step 'C#-tests'
    & dotnet run --project '.\tests\PraktijkClean.Tests\PraktijkClean.Tests.csproj' -c Release
    if ($LASTEXITCODE -ne 0) { throw 'C#-tests mislukt.' }
}

# Versie uit het projectbestand; de exe krijgt die in de naam, bijv. PraktijkClean-1.2.0.exe.
$version = ([xml](Get-Content -Raw -LiteralPath '.\src\PraktijkClean\PraktijkClean.csproj')).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'Geen <Version> gevonden in src\PraktijkClean\PraktijkClean.csproj.' }
$exeName = "PraktijkClean-$version.exe"

Step "$exeName bouwen"
$dist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
# Oude builds (ook van eerdere versies) weghalen, zodat in dist alleen de nieuwste staat.
Remove-Item -Path (Join-Path $dist 'PraktijkClean*.exe*') -ErrorAction SilentlyContinue
# Bewust GEEN compressie of andere 'packing': dat lijkt voor virusscanners op verdachte software.
$out = Join-Path $PSScriptRoot 'src\PraktijkClean\bin\Release\net48'
& dotnet build '.\src\PraktijkClean\PraktijkClean.csproj' -c Release -p:DebugType=None
if ($LASTEXITCODE -ne 0) { throw 'Bouwen mislukt.' }
# De Core-code zit in de exe zelf; alleen dit ene bestand is nodig.
$exe = Join-Path $dist $exeName
Copy-Item -LiteralPath (Join-Path $out 'PraktijkClean.exe') -Destination $exe

$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $exe).Hash
Set-Content -LiteralPath "$exe.sha256" -Value "$hash  $exeName" -Encoding ASCII
Write-Host "`nKlaar: $exe" -ForegroundColor Green
Write-Host ("Grootte: {0:N0} kB" -f ((Get-Item -LiteralPath $exe).Length / 1KB))
Write-Host "SHA-256: $hash"
