#requires -Version 5.1
<#
.SYNOPSIS
    Geautomatiseerde tests voor cleanup.ps1, zonder externe modules (geen Pester nodig).

.DESCRIPTION
    Alle tests werken UITSLUITEND in een nieuwe tijdelijke map met fictieve bestanden
    (%TEMP%\PraktijkCleanTest-<guid>). Echte mappen zoals Bureaublad, Downloads of Documenten
    worden nooit gebruikt. De tijdelijke map wordt na afloop verwijderd.

    Uitvoeren (Windows PowerShell 5.1, vanuit de projectmap):
        powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Script\Run-ScriptTests.ps1
    (De -ExecutionPolicy Bypass geldt alleen voor dit ene testproces op de ontwikkelcomputer.)

    Exitcode 0 = alle tests geslaagd.
#>
param([switch]$KeepTemp)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$scriptDir = Join-Path $repoRoot 'script'
$scriptPath = Join-Path $scriptDir 'cleanup.ps1'

# Laad de functies uit het script zonder het hoofdprogramma te starten.
. $scriptPath
$ErrorActionPreference = 'Stop'
# VEILIGHEID: de tests raken nooit de echte Prullenbak. Standaard een lege lijst; tests voor
# 'Prullenbak legen' zetten hier een nagebootste Prullenbakmap in de testmap neer.
$script:PcRecycleBinRootsOverride = @()

# ---------------------------------------------------------------------------------------------
# Tijdelijke testomgeving
# ---------------------------------------------------------------------------------------------
# Op Windows %LOCALAPPDATA%\Temp met het volledige pad (GetTempPath() kan een korte 8.3-naam geven,
# die het script terecht weigert).
$tempBase = [System.IO.Path]::GetTempPath()
if ($script:PcIsWindows -and -not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA) -and [System.IO.Directory]::Exists((Join-Path $env:LOCALAPPDATA 'Temp'))) {
    $tempBase = Join-Path $env:LOCALAPPDATA 'Temp'
}
$TestRoot = Join-Path $tempBase ('PraktijkCleanTest-' + [guid]::NewGuid().ToString('N'))
[void][System.IO.Directory]::CreateDirectory($TestRoot)
$TestRoot = (New-Object System.IO.DirectoryInfo($TestRoot)).FullName.TrimEnd('\', '/')

function Assert-InTestRoot([string]$Path) {
    if (-not (Test-PcPathInside $Path $TestRoot)) { throw "VEILIGHEID: testpad buiten testmap: $Path" }
}

# De echte beveiliging beschermt %LOCALAPPDATA% (waar %TEMP% in ligt). Voor de tests halen we
# alleen de beschermde mappen weg die BOVEN de testmap liggen; de rest blijft actief.
# Test 'Echte beschermde mappen' controleert de onaangepaste functie.
$script:OriginalProtected = ${function:Get-PcProtectedPaths}
function Get-PcProtectedPaths {
    param([string]$InstallDir)
    $p = & $script:OriginalProtected -InstallDir $InstallDir
    $trees = @($p.Trees | Where-Object { -not (Test-PcPathInside $TestRoot $_) })
    $exact = @($p.Exact | Where-Object { -not (Test-PcPathInside $TestRoot $_) })
    return [pscustomobject]@{ Trees = $trees; Exact = $exact }
}

$script:Counter = 0
function New-TestDir([string]$Name = 'map') {
    $script:Counter++
    $p = Join-Path $TestRoot ("$($script:Counter)-$Name")
    Assert-InTestRoot $p
    [void][System.IO.Directory]::CreateDirectory($p)
    return $p
}

function New-TestFile([string]$Dir, [string]$Name, [double]$AgeDays = 30, [int]$Bytes = 10) {
    $p = Join-Path $Dir $Name
    Assert-InTestRoot $p
    $parent = Split-Path -Parent $p
    if (-not [System.IO.Directory]::Exists($parent)) { [void][System.IO.Directory]::CreateDirectory($parent) }
    [System.IO.File]::WriteAllBytes($p, (New-Object byte[] $Bytes))
    $t = [DateTime]::UtcNow.AddDays(-$AgeDays)
    [System.IO.File]::SetLastWriteTimeUtc($p, $t)
    [System.IO.File]::SetCreationTimeUtc($p, $t)
    return $p
}

function New-FolderSpec([string]$Path, [hashtable]$Override = @{}) {
    $f = [ordered]@{
        id = 'test'; displayName = 'Test'; enabled = $true; type = 'custom'; knownFolder = $null
        path = $Path; expectedPath = $Path; retentionDays = 7; recurse = $false; allowNetworkOrSync = $false
        exclusions = [ordered]@{ files = @(); subfolders = @(); patterns = @(); extensions = @('.lnk', '.url') }
    }
    foreach ($k in $Override.Keys) { $f[$k] = $Override[$k] }
    return $f
}

function New-TestConfig([object[]]$Folders, [hashtable]$Override = @{}) {
    $c = [ordered]@{
        schemaVersion = 1; simulationMode = $true; automaticCleanupConfirmed = $false; paused = $false
        maxFilesPerRun = 200; minimumAgeMinutes = 15; deleteMode = 'permanent'
        schedule = [ordered]@{ daily = $true; dailyTime = '12:30'; atLogon = $true }
        folders = @($Folders)
    }
    foreach ($k in $Override.Keys) { $c[$k] = $Override[$k] }
    $dir = New-TestDir 'config'
    $path = Join-Path $dir 'config.json'
    [System.IO.File]::WriteAllText($path, ($c | ConvertTo-Json -Depth 8), (New-Object System.Text.UTF8Encoding($false)))
    return $path
}

function Write-RawConfig([string]$Text) {
    $dir = New-TestDir 'config'
    $path = Join-Path $dir 'config.json'
    [System.IO.File]::WriteAllText($path, $Text, (New-Object System.Text.UTF8Encoding($false)))
    return $path
}

function Invoke-Test([string]$ConfigPath, [string]$Mode = 'CleanNow', [bool]$Confirmed = $true, [bool]$Names = $false) {
    return Invoke-PcCleanup -ConfigPath $ConfigPath -Mode $Mode -ConfirmedByUser $Confirmed -IncludeFileNames $Names -NowUtc ([DateTime]::UtcNow) -InstallDir $scriptDir
}

# ---------------------------------------------------------------------------------------------
# Mini-testraamwerk
# ---------------------------------------------------------------------------------------------
$script:Passed = 0; $script:Failed = 0; $script:Skipped = 0
$script:Failures = New-Object System.Collections.Generic.List[string]

function Test-Case([string]$Name, [scriptblock]$Body) {
    try {
        & $Body
        $script:Passed++
        Write-Host "  [OK]    $Name" -ForegroundColor Green
    } catch {
        $msg = $_.Exception.Message
        if ($msg.StartsWith('SKIP:')) {
            $script:Skipped++
            Write-Host "  [SKIP]  $Name ($($msg.Substring(5).Trim()))" -ForegroundColor Yellow
        } else {
            $script:Failed++
            $script:Failures.Add("$Name : $msg")
            Write-Host "  [FOUT]  $Name : $msg" -ForegroundColor Red
        }
    }
}
function Skip([string]$Why) { throw "SKIP: $Why" }
function Assert-Equal($Expected, $Actual, [string]$What = 'waarde') {
    if ($Expected -ne $Actual) { throw "$What verwacht '$Expected' maar was '$Actual'" }
}
function Assert-True($Condition, [string]$What) { if (-not $Condition) { throw "Verwacht waar: $What" } }
function Assert-Exists([string]$Path) { if (-not [System.IO.File]::Exists($Path)) { throw "Bestand onterecht verwijderd: $(Split-Path -Leaf $Path)" } }
function Assert-Gone([string]$Path) { if ([System.IO.File]::Exists($Path)) { throw "Bestand niet verwijderd: $(Split-Path -Leaf $Path)" } }

Write-Host "PraktijkClean scripttests (PowerShell $($PSVersionTable.PSVersion), Windows: $script:PcIsWindows)"
Write-Host "Testmap: $TestRoot"

try {
    # ---------------------------------------------------------------- Leeftijd
    Write-Host "`nBestandsleeftijd"
    Test-Case 'Nieuwste van aanmaak- en wijzigingsdatum telt' {
        $old = [DateTime]::UtcNow.AddDays(-100); $new = [DateTime]::UtcNow.AddMinutes(-5)
        Assert-Equal $new (Get-PcNewestTime -CreationUtc $new -LastWriteUtc $old) 'aanmaak nieuwer'
        Assert-Equal $new (Get-PcNewestTime -CreationUtc $old -LastWriteUtc $new) 'wijziging nieuwer'
    }
    Test-Case 'Ouder/jonger dan bewaartermijn, datum in toekomst, minimumleeftijd' {
        $now = [DateTime]::UtcNow
        Assert-True (Test-PcIsOldEnough $now.AddDays(-8) $now 7 15) '8 dagen oud bij 7 dagen'
        Assert-True (-not (Test-PcIsOldEnough $now.AddDays(-6) $now 7 15)) '6 dagen oud bij 7 dagen'
        Assert-True (-not (Test-PcIsOldEnough $now.AddDays(5) $now 0 15)) 'datum in de toekomst'
        Assert-True (-not (Test-PcIsOldEnough $now.AddMinutes(-5) $now 0 15)) '"direct" maar jonger dan 15 minuten'
        Assert-True (Test-PcIsOldEnough $now.AddMinutes(-20) $now 0 15) '"direct" en ouder dan 15 minuten'
    }
    Test-Case 'Oud bestand verwijderd, jong bestand behouden' {
        $d = New-TestDir
        $old = New-TestFile $d 'oud.pdf' 10
        $young = New-TestFile $d 'jong.pdf' 2
        $r = Invoke-Test (New-TestConfig @(New-FolderSpec $d))
        Assert-Equal 0 $r.ExitCode 'exitcode'
        Assert-Gone $old; Assert-Exists $young
        Assert-Equal 1 $r.DeletedCount 'verwijderd'
        Assert-Equal 1 $r.TooYoungCount 'te recent'
    }
    Test-Case 'Pas gedownload bestand met oude wijzigingsdatum blijft staan' {
        if (-not $script:PcIsWindows) { Skip 'aanmaakdatum is alleen op Windows apart in te stellen' }
        $d = New-TestDir
        $f = New-TestFile $d 'download.pdf' 60
        [System.IO.File]::SetCreationTimeUtc($f, [DateTime]::UtcNow.AddHours(-1))
        $r = Invoke-Test (New-TestConfig @(New-FolderSpec $d))
        Assert-Exists $f
        Assert-Equal 0 $r.CandidateCount 'kandidaten'
    }

    # ---------------------------------------------------------------- Mappen
    Write-Host "`nMappen en submappen"
    Test-Case 'Lege map geeft geen fouten' {
        $d = New-TestDir
        $r = Invoke-Test (New-TestConfig @(New-FolderSpec $d))
        Assert-Equal 0 $r.ExitCode 'exitcode'; Assert-Equal 0 $r.CandidateCount 'kandidaten'
    }
    Test-Case 'Submappen worden standaard niet doorzocht en nooit verwijderd' {
        $d = New-TestDir
        $sub = Join-Path $d 'sub'
        $f = New-TestFile $sub 'oud.docx' 30
        $r = Invoke-Test (New-TestConfig @(New-FolderSpec $d))
        Assert-Exists $f
        Assert-True ([System.IO.Directory]::Exists($sub)) 'submap bestaat nog'
    }
    Test-Case 'Recursie (optie): bestanden in submap weg, submap zelf blijft' {
        $d = New-TestDir
        $sub = Join-Path $d 'sub'
        $f = New-TestFile $sub 'oud.docx' 30
        $r = Invoke-Test (New-TestConfig @(New-FolderSpec $d @{ recurse = $true }))
        Assert-Gone $f
        Assert-True ([System.IO.Directory]::Exists($sub)) 'submap bestaat nog'
    }
    Test-Case 'Uitgeschakelde map wordt overgeslagen' {
        $d = New-TestDir
        $f = New-TestFile $d 'oud.pdf' 30
        $r = Invoke-Test (New-TestConfig @(New-FolderSpec $d @{ enabled = $false }))
        Assert-Exists $f
    }

    # ---------------------------------------------------------------- Uitzonderingen
    Write-Host "`nUitzonderingen"
    Test-Case 'Snelkoppelingen, bestanden, patronen en extensies worden behouden' {
        $d = New-TestDir
        $lnk = New-TestFile $d 'Praktijk.lnk' 30
        $named = New-TestFile $d 'Bewaren.pdf' 30
        $pattern = New-TestFile $d 'protocol-2024.docx' 30
        $ext = New-TestFile $d 'scan.tif' 30
        $gone = New-TestFile $d 'brief.pdf' 30
        $spec = New-FolderSpec $d @{ exclusions = [ordered]@{ files = @('bewaren.pdf'); subfolders = @(); patterns = @('protocol-*'); extensions = @('.lnk', '.TIF') } }
        $r = Invoke-Test (New-TestConfig @($spec))
        Assert-Exists $lnk; Assert-Exists $named; Assert-Exists $pattern; Assert-Exists $ext
        Assert-Gone $gone
        Assert-Equal 4 $r.SkippedCount 'overgeslagen'
    }
    Test-Case 'Uitgesloten submap wordt niet doorzocht (bij recursie)' {
        $d = New-TestDir
        $keep = New-TestFile (Join-Path $d 'Bewaar') 'a.pdf' 30
        $gone = New-TestFile (Join-Path $d 'Weg') 'b.pdf' 30
        $spec = New-FolderSpec $d @{ recurse = $true; exclusions = [ordered]@{ files = @(); subfolders = @('Bewaar'); patterns = @(); extensions = @() } }
        $r = Invoke-Test (New-TestConfig @($spec))
        Assert-Exists $keep; Assert-Gone $gone
    }
    Test-Case 'Verborgen, alleen-lezen, lopende downloads en Office-tijdelijke bestanden blijven staan' {
        $d = New-TestDir
        $ro = New-TestFile $d 'alleenlezen.pdf' 30
        (New-Object System.IO.FileInfo($ro)).IsReadOnly = $true
        $dl = New-TestFile $d 'bijna.pdf.crdownload' 30
        $office = New-TestFile $d '~$brief.docx' 30
        $ini = New-TestFile $d 'desktop.ini' 30
        $hidden = New-TestFile $d 'verborgen.pdf' 30
        if ($script:PcIsWindows) {
            [System.IO.File]::SetAttributes($hidden, [System.IO.FileAttributes]::Hidden)
        } else {
            $hidden = New-TestFile $d '.verborgen.pdf' 30
        }
        $r = Invoke-Test (New-TestConfig @(New-FolderSpec $d))
        Assert-Exists $ro; Assert-Exists $dl; Assert-Exists $office; Assert-Exists $ini; Assert-Exists $hidden
        (New-Object System.IO.FileInfo($ro)).IsReadOnly = $false
    }

    # ---------------------------------------------------------------- Vergrendeld
    Write-Host "`nVergrendelde bestanden"
    Test-Case 'Bestand in gebruik wordt overgeslagen' {
        $d = New-TestDir
        $f = New-TestFile $d 'open.pdf' 30
        $fs = [System.IO.File]::Open($f, 'Open', 'ReadWrite', 'None')
        try {
            $probeLocked = $false
            try { $p = [System.IO.File]::Open($f, 'Open', 'ReadWrite', 'None'); $p.Dispose() } catch { $probeLocked = $true }
            if (-not $probeLocked) { Skip 'besturingssysteem dwingt exclusieve vergrendeling niet af' }
            $r = Invoke-Test (New-TestConfig @(New-FolderSpec $d))
            Assert-Exists $f
            Assert-Equal 0 $r.DeletedCount 'verwijderd'
            Assert-True ($r.Folders[0].SkippedByReason.ContainsKey('In gebruik of niet toegankelijk')) 'reden in gebruik'
        } finally { $fs.Dispose() }
    }

    # ---------------------------------------------------------------- Configuratie
    Write-Host "`nOntbrekende of ongeldige instellingen (nooit verwijderen)"
    $victimDir = New-TestDir 'slachtoffer'
    $victim = New-TestFile $victimDir 'moet-blijven.pdf' 365
    $vEsc = $victimDir.Replace('\', '\\')
    Test-Case 'Ontbrekende config.json' {
        $r = Invoke-Test (Join-Path $TestRoot 'bestaat-niet.json')
        Assert-Equal 1 $r.ExitCode 'exitcode'; Assert-Exists $victim
    }
    Test-Case 'Ongeldige JSON' {
        $r = Invoke-Test (Write-RawConfig '{ "schemaVersion": 1, "folders": [ ')
        Assert-Equal 1 $r.ExitCode 'exitcode'; Assert-Exists $victim
    }
    Test-Case 'Lege config.json' {
        $r = Invoke-Test (Write-RawConfig '   ')
        Assert-Equal 1 $r.ExitCode 'exitcode'; Assert-Exists $victim
    }
    Test-Case 'Ontbrekende bewaartermijn wordt NIET als 0 dagen gezien' {
        $spec = New-FolderSpec $victimDir
        $spec.Remove('retentionDays')
        $r = Invoke-Test (New-TestConfig @($spec))
        Assert-Equal 1 $r.ExitCode 'exitcode'; Assert-Exists $victim
    }
    Test-Case 'Verkeerde types (tekst i.p.v. getal/boolean)' {
        $r = Invoke-Test (New-TestConfig @(New-FolderSpec $victimDir @{ retentionDays = '7' }))
        Assert-Equal 1 $r.ExitCode 'retentionDays als tekst'; Assert-Exists $victim
        $r = Invoke-Test (New-TestConfig @(New-FolderSpec $victimDir @{ enabled = 'true' }))
        Assert-Equal 1 $r.ExitCode 'enabled als tekst'; Assert-Exists $victim
    }
    Test-Case 'Ongeldige globale waarden' {
        foreach ($o in @(@{ maxFilesPerRun = 0 }, @{ maxFilesPerRun = 99999 }, @{ deleteMode = 'shred' }, @{ minimumAgeMinutes = 0 }, @{ schemaVersion = 2 }, @{ simulationMode = 'nee' })) {
            $r = Invoke-Test (New-TestConfig @(New-FolderSpec $victimDir) $o)
            Assert-Equal 1 $r.ExitCode ("exitcode bij " + ($o.Keys -join ','))
        }
        Assert-Exists $victim
    }
    Test-Case 'Ongeldige uitzonderingen maken de configuratie ongeldig' {
        foreach ($ex in @(
            [ordered]@{ files = @('..\..\geheim.pdf'); subfolders = @(); patterns = @(); extensions = @() },
            [ordered]@{ files = @(); subfolders = @('C:\Windows'); patterns = @(); extensions = @() },
            [ordered]@{ files = @(); subfolders = @('/etc'); patterns = @(); extensions = @() },
            [ordered]@{ files = @(); subfolders = @(); patterns = @('a\b*'); extensions = @() },
            [ordered]@{ files = @(); subfolders = @(); patterns = @(); extensions = @('pdf') },
            [ordered]@{ files = @(); subfolders = @(); patterns = @(); extensions = @(5) })) {
            $r = Invoke-Test (New-TestConfig @(New-FolderSpec $victimDir @{ exclusions = $ex }))
            Assert-Equal 1 $r.ExitCode 'exitcode'
        }
        Assert-Exists $victim
    }
    Test-Case 'Dubbele map-id, onbekend type, ontbrekende map-lijst' {
        $a = New-FolderSpec $victimDir; $b = New-FolderSpec $victimDir
        Assert-Equal 1 (Invoke-Test (New-TestConfig @($a, $b))).ExitCode 'dubbele id'
        Assert-Equal 1 (Invoke-Test (New-TestConfig @(New-FolderSpec $victimDir @{ type = 'alles' }))).ExitCode 'onbekend type'
        Assert-Equal 1 (Invoke-Test (Write-RawConfig '{"schemaVersion":1,"simulationMode":false,"automaticCleanupConfirmed":true,"paused":false,"maxFilesPerRun":10,"minimumAgeMinutes":15,"deleteMode":"permanent"}')).ExitCode 'geen folders'
        Assert-Exists $victim
    }
    Test-Case 'Locatie van map gewijzigd (expectedPath klopt niet): map overslaan' {
        $r = Invoke-Test (New-TestConfig @(New-FolderSpec $victimDir @{ expectedPath = (Join-Path $TestRoot 'elders') }))
        Assert-Equal 'Fout' $r.Folders[0].Status 'status'
        Assert-Exists $victim
    }

    # ---------------------------------------------------------------- Padveiligheid
    Write-Host "`nPadveiligheid"
    Test-Case 'Stationswortel, relatieve paden, ".." en systeemmappen worden geweigerd' {
        $roots = @()
        if ($script:PcIsWindows) {
            $bad = @('C:\', 'C:', 'Downloads', "$TestRoot\..\x", $env:windir, (Join-Path $env:ProgramFiles 'x'), $env:USERPROFILE, "$env:SystemDrive\Users", '\\?\C:\Temp', "${TestRoot}:stream", 'C:\PROGRA~1\Test')
        } else {
            $bad = @('/', 'Downloads', "$TestRoot/../x", '/etc', '/usr/lib', '/home', $env:HOME, "$TestRoot/PROGRA~1")
        }
        foreach ($b in $bad) {
            $s = Test-PcFolderSafety -Path $b -AllowNetworkOrSync $true -InstallDir $scriptDir -SyncRoots @()
            Assert-True (-not $s.Ok) "geweigerd: $b"
        }
    }
    Test-Case 'Korte 8.3-padnaam (bijv. PROGRA~1) wordt geweigerd, ook als de map bestaat' {
        $d = Join-Path (New-TestDir) 'MAP~1'
        $f = New-TestFile $d 'oud.pdf' 30
        $s = Test-PcFolderSafety -Path $d -AllowNetworkOrSync $false -InstallDir $scriptDir -SyncRoots @()
        Assert-True (-not $s.Ok) 'korte naam geweigerd'
        Assert-True ($s.Reason -match 'Korte') 'reden'
        $r = Invoke-Test (New-TestConfig @(New-FolderSpec $d))
        Assert-Exists $f
    }
    Test-Case 'PraktijkClean-installatiemap wordt geweigerd' {
        $s = Test-PcFolderSafety -Path $scriptDir -AllowNetworkOrSync $true -InstallDir $scriptDir -SyncRoots @()
        Assert-True (-not $s.Ok) 'installatiemap'
    }
    Test-Case 'Niet-bestaande map wordt geweigerd' {
        $s = Test-PcFolderSafety -Path (Join-Path $TestRoot 'bestaat-niet') -AllowNetworkOrSync $false -InstallDir $scriptDir -SyncRoots @()
        Assert-True (-not $s.Ok) 'niet-bestaand'
    }
    Test-Case 'Echte beschermde mappen bevatten systeem- en AppData-mappen' {
        $p = & $script:OriginalProtected -InstallDir $scriptDir
        if ($script:PcIsWindows) {
            foreach ($must in @($env:windir, $env:ProgramFiles, $env:LOCALAPPDATA, $env:APPDATA, $env:ProgramData)) {
                Assert-True (@($p.Trees | Where-Object { Test-PcPathEquals $_ $must }).Count -gt 0) "beschermd: $must"
            }
            Assert-True (@($p.Exact | Where-Object { Test-PcPathEquals $_ $env:USERPROFILE }).Count -gt 0) 'gebruikersprofiel'
        } else {
            Assert-True (@($p.Trees | Where-Object { $_ -eq '/etc' }).Count -gt 0) '/etc'
        }
        Assert-True (@($p.Trees | Where-Object { Test-PcPathEquals $_ $scriptDir }).Count -gt 0) 'installatiemap'
    }

    # ---------------------------------------------------------------- Koppelingen
    Write-Host "`nSymbolische links en junctions"
    function New-DirLink([string]$Link, [string]$Target) {
        if ($script:PcIsWindows) {
            $null = cmd.exe /c mklink /J "`"$Link`"" "`"$Target`"" 2>&1
        } else {
            $null = New-Item -ItemType SymbolicLink -Path $Link -Target $Target
        }
        return [System.IO.Directory]::Exists($Link)
    }
    Test-Case 'Junction/symlink naar andere map wordt niet gevolgd (ook niet bij recursie)' {
        $d = New-TestDir
        $target = New-TestDir 'doel'
        $tf = New-TestFile $target 'doelbestand.pdf' 30
        if (-not (New-DirLink (Join-Path $d 'koppeling') $target)) { Skip 'kon geen koppeling maken' }
        $r = Invoke-Test (New-TestConfig @(New-FolderSpec $d @{ recurse = $true }))
        Assert-Exists $tf
        Assert-True ([System.IO.Directory]::Exists((Join-Path $d 'koppeling'))) 'koppeling bestaat nog'
    }
    Test-Case 'Geselecteerde map die zelf een koppeling is wordt geblokkeerd' {
        $target = New-TestDir 'doel'
        $tf = New-TestFile $target 'doelbestand.pdf' 30
        $link = Join-Path $TestRoot ('koppeling-' + [guid]::NewGuid().ToString('N'))
        if (-not (New-DirLink $link $target)) { Skip 'kon geen koppeling maken' }
        $r = Invoke-Test (New-TestConfig @(New-FolderSpec $link))
        Assert-Equal 'Geblokkeerd' $r.Folders[0].Status 'status'
        Assert-Exists $tf
    }
    Test-Case 'Symbolische link naar bestand wordt overgeslagen, doel blijft' {
        $d = New-TestDir
        $target = New-TestDir 'doel'
        $tf = New-TestFile $target 'echt.pdf' 30
        $link = Join-Path $d 'link.pdf'
        try { $null = New-Item -ItemType SymbolicLink -Path $link -Target $tf -ErrorAction Stop } catch { Skip 'symbolische links vereisen hier extra rechten (Windows: ontwikkelaarsmodus)' }
        $r = Invoke-Test (New-TestConfig @(New-FolderSpec $d))
        Assert-Exists $tf
        Assert-True ((Test-Path -LiteralPath $link)) 'link bestaat nog'
    }

    # ---------------------------------------------------------------- OneDrive / netwerk
    Write-Host "`nOneDrive- en netwerklocaties"
    Test-Case 'Map binnen OneDrive (omgevingsvariabele) wordt geblokkeerd' {
        $od = New-TestDir 'OneDriveRoot'
        $desk = Join-Path $od 'Bureaublad'
        $f = New-TestFile $desk 'oud.pdf' 30
        $saved = $env:OneDrive
        try {
            $env:OneDrive = $od
            $r = Invoke-Test (New-TestConfig @(New-FolderSpec $desk))
            Assert-Equal 'Geblokkeerd' $r.Folders[0].Status 'status'
            Assert-Exists $f
            Assert-True ($r.Folders[0].Message -match 'OneDrive') 'melding noemt OneDrive'
        } finally { $env:OneDrive = $saved }
    }
    Test-Case 'Map met OneDrive-naam wordt geblokkeerd; na expliciete toestemming toegestaan' {
        $od = Join-Path (New-TestDir) 'OneDrive - Huisartsenpraktijk'
        $f = New-TestFile $od 'oud.pdf' 30
        $r = Invoke-Test (New-TestConfig @(New-FolderSpec $od))
        Assert-Equal 'Geblokkeerd' $r.Folders[0].Status 'zonder toestemming'
        Assert-Exists $f
        $r = Invoke-Test (New-TestConfig @(New-FolderSpec $od @{ allowNetworkOrSync = $true }))
        Assert-Equal 'OK' $r.Folders[0].Status 'met toestemming'
        Assert-Gone $f
    }
    Test-Case 'Netwerkpaden worden herkend' {
        Assert-True (Test-PcIsNetworkPath '\\server\share\Bureaublad') 'UNC'
        Assert-True (Test-PcIsNetworkPath '//server/share/x') 'UNC met slashes'
        Assert-True (-not (Test-PcIsNetworkPath $TestRoot)) 'lokale testmap'
    }
    Test-Case 'Netwerkpad wordt zonder toestemming geweigerd' {
        if (-not $script:PcIsWindows) { Skip 'UNC-paden alleen op Windows' }
        $s = Test-PcFolderSafety -Path '\\server-bestaat-niet\share\map' -AllowNetworkOrSync $false -InstallDir $scriptDir -SyncRoots @()
        Assert-True (-not $s.Ok) 'UNC geweigerd'
    }

    # ---------------------------------------------------------------- Simulatie en modi
    Write-Host "`nSimulatiemodus, pauze, bevestiging en limiet"
    Test-Case 'Simulatie verwijdert niets en telt correct' {
        $d = New-TestDir
        $a = New-TestFile $d 'a.pdf' 30 1000
        $b = New-TestFile $d 'b.pdf' 30 2000
        $c = New-TestFile $d 'c.lnk' 30
        $r = Invoke-Test (New-TestConfig @(New-FolderSpec $d)) 'Simulate' $false $true
        Assert-Exists $a; Assert-Exists $b; Assert-Exists $c
        Assert-Equal 'simulatie' $r.Action 'actie'
        Assert-Equal 2 $r.CandidateCount 'kandidaten'
        Assert-Equal 3000 $r.CandidateBytes 'bytes'
        Assert-Equal 1 $r.SkippedCount 'overgeslagen'
        Assert-Equal 2 @($r.Folders[0].Candidates).Count 'namen voor handmatige simulatie'
    }
    Test-Case 'Geplande uitvoering in simulatiemodus verwijdert niets' {
        $d = New-TestDir
        $a = New-TestFile $d 'a.pdf' 30
        $r = Invoke-Test (New-TestConfig @(New-FolderSpec $d) @{ simulationMode = $true; automaticCleanupConfirmed = $true }) 'Scheduled' $false
        Assert-Exists $a; Assert-Equal 'simulatie' $r.Action 'actie'
    }
    Test-Case 'Geplande uitvoering zonder expliciete bevestiging verwijdert niets' {
        $d = New-TestDir
        $a = New-TestFile $d 'a.pdf' 30
        $r = Invoke-Test (New-TestConfig @(New-FolderSpec $d) @{ simulationMode = $false; automaticCleanupConfirmed = $false }) 'Scheduled' $false
        Assert-Exists $a
    }
    Test-Case 'Geplande uitvoering met bevestiging en zonder simulatie verwijdert wel' {
        $d = New-TestDir
        $a = New-TestFile $d 'a.pdf' 30
        $r = Invoke-Test (New-TestConfig @(New-FolderSpec $d) @{ simulationMode = $false; automaticCleanupConfirmed = $true }) 'Scheduled' $false
        Assert-Gone $a; Assert-Equal 'opgeschoond' $r.Action 'actie'
    }
    Test-Case 'Gepauzeerd: niets doen' {
        $d = New-TestDir
        $a = New-TestFile $d 'a.pdf' 30
        $r = Invoke-Test (New-TestConfig @(New-FolderSpec $d) @{ simulationMode = $false; automaticCleanupConfirmed = $true; paused = $true }) 'Scheduled' $false
        Assert-Exists $a; Assert-Equal 'gepauzeerd' $r.Action 'actie'
    }
    Test-Case 'Nu opruimen zonder bevestiging wordt geweigerd' {
        $d = New-TestDir
        $a = New-TestFile $d 'a.pdf' 30
        $r = Invoke-Test (New-TestConfig @(New-FolderSpec $d)) 'CleanNow' $false
        Assert-Exists $a; Assert-Equal 1 $r.ExitCode 'exitcode'
    }
    Test-Case 'Boven de limiet wordt er NIETS verwijderd' {
        $d = New-TestDir
        $files = @(1..5 | ForEach-Object { New-TestFile $d "f$_.pdf" 30 })
        $r = Invoke-Test (New-TestConfig @(New-FolderSpec $d) @{ maxFilesPerRun = 3 })
        Assert-Equal 3 $r.ExitCode 'exitcode'
        foreach ($f in $files) { Assert-Exists $f }
    }
    Test-Case 'Meerdere mappen met eigen bewaartermijn' {
        $d1 = New-TestDir; $d2 = New-TestDir
        $a = New-TestFile $d1 'a.pdf' 5
        $b = New-TestFile $d2 'b.pdf' 5
        $s1 = New-FolderSpec $d1 @{ id = 'een'; retentionDays = 3 }
        $s2 = New-FolderSpec $d2 @{ id = 'twee'; retentionDays = 14 }
        $r = Invoke-Test (New-TestConfig @($s1, $s2))
        Assert-Gone $a; Assert-Exists $b
    }
    Test-Case 'Herhaald uitvoeren is veilig' {
        $d = New-TestDir
        $a = New-TestFile $d 'a.pdf' 30
        $cfg = New-TestConfig @(New-FolderSpec $d)
        $r1 = Invoke-Test $cfg; $r2 = Invoke-Test $cfg
        Assert-Gone $a; Assert-Equal 0 $r2.CandidateCount 'tweede keer'; Assert-Equal 0 $r2.ExitCode 'exitcode'
    }

    # ---------------------------------------------------------------- Prullenbak legen
    Write-Host "`nPrullenbak legen (nagebootste Prullenbak, nooit de echte)"
    function New-FakeBin {
        $root = New-TestDir 'S-1-5-21-test'
        $script:PcRecycleBinRootsOverride = @($root)
        return $root
    }
    function New-FakeRecycleItem([string]$Root, [string]$Id, [double]$DeletedAgeDays, [string]$Original = 'C:\Users\test\Desktop\brief.pdf', [switch]$Directory, [int]$Version = 2, [long]$Size = 100) {
        Assert-InTestRoot $Root
        $info = Join-Path $Root ('$I' + $Id)
        $data = Join-Path $Root ('$R' + $Id)
        $ft = [DateTime]::UtcNow.AddDays(-$DeletedAgeDays).ToFileTimeUtc()
        $ms = New-Object System.IO.MemoryStream
        $bw = New-Object System.IO.BinaryWriter($ms)
        $bw.Write([long]$Version); $bw.Write([long]$Size); $bw.Write([long]$ft)
        if ($Version -eq 2) {
            $name = $Original + [char]0
            $bw.Write([int]$name.Length); $bw.Write([System.Text.Encoding]::Unicode.GetBytes($name))
        } else {
            $buf = New-Object byte[] 520
            $nb = [System.Text.Encoding]::Unicode.GetBytes($Original)
            [Array]::Copy($nb, $buf, [Math]::Min($nb.Length, 518))
            $bw.Write($buf)
        }
        $bw.Flush()
        [System.IO.File]::WriteAllBytes($info, $ms.ToArray())
        if ($Directory) {
            [void][System.IO.Directory]::CreateDirectory((Join-Path $data 'sub'))
            [System.IO.File]::WriteAllBytes((Join-Path $data 'a.pdf'), (New-Object byte[] 10))
            $ro = Join-Path $data 'sub\alleen-lezen.pdf'
            [System.IO.File]::WriteAllBytes($ro, (New-Object byte[] 10))
            [System.IO.File]::SetAttributes($ro, [System.IO.FileAttributes]::ReadOnly)
        } else {
            [System.IO.File]::WriteAllBytes($data, (New-Object byte[] 10))
        }
        return [pscustomobject]@{ Info = $info; Data = $data }
    }
    function Test-ItemExists($Item) { return ([System.IO.File]::Exists($Item.Data) -or [System.IO.Directory]::Exists($Item.Data)) -and [System.IO.File]::Exists($Item.Info) }
    function Test-ItemGone($Item) { return -not ([System.IO.File]::Exists($Item.Data) -or [System.IO.Directory]::Exists($Item.Data) -or [System.IO.File]::Exists($Item.Info)) }

    Test-Case 'Optie ontbreekt = uit; aan zonder geldige termijn = configuratiefout' {
        $d = New-TestDir
        $r = Invoke-Test (New-TestConfig @(New-FolderSpec $d))
        Assert-Equal 0 $r.ExitCode 'oude config zonder prullenbakvelden is geldig'
        Assert-True ($null -eq $r.RecycleBin) 'geen prullenbakresultaat als de optie ontbreekt'
        foreach ($o in @(@{ emptyRecycleBin = $true }, @{ emptyRecycleBin = $true; recycleBinRetentionDays = -1 },
                         @{ emptyRecycleBin = $true; recycleBinRetentionDays = 4000 }, @{ emptyRecycleBin = 'ja'; recycleBinRetentionDays = 7 },
                         @{ emptyRecycleBin = $false; recycleBinRetentionDays = 'zeven' })) {
            $r = Invoke-Test (New-TestConfig @(New-FolderSpec $d) $o)
            Assert-Equal 1 $r.ExitCode ("configuratiefout voor " + (($o.Keys | ForEach-Object { "$_=$($o[$_])" }) -join ', '))
        }
    }
    Test-Case '$I-bestanden versie 1 en 2 worden goed gelezen' {
        $bin = New-FakeBin
        $a = New-FakeRecycleItem $bin 'AAA111.pdf' 10 'C:\Users\test\Desktop\uitslag.pdf' -Version 2 -Size 1234
        $b = New-FakeRecycleItem $bin 'BBB222.pdf' 3 'D:\oud\brief.pdf' -Version 1 -Size 99
        $ia = Read-PcRecycleInfo $a.Info
        $ib = Read-PcRecycleInfo $b.Info
        Assert-Equal 'C:\Users\test\Desktop\uitslag.pdf' $ia.OriginalPath 'pad v2'
        Assert-Equal 1234 $ia.Size 'grootte v2'
        Assert-True ([Math]::Abs(([DateTime]::UtcNow - $ia.DeletedUtc).TotalDays - 10) -lt 0.01) 'tijdstip v2'
        Assert-Equal 'D:\oud\brief.pdf' $ib.OriginalPath 'pad v1'
        Assert-Equal 99 $ib.Size 'grootte v1'
        $junk = Join-Path $bin '$Ikapot.pdf'
        [System.IO.File]::WriteAllBytes($junk, (New-Object byte[] 5))
        Assert-True ($null -eq (Read-PcRecycleInfo $junk)) 'onleesbaar bestand'
    }
    Test-Case 'Simulatie telt Prullenbak-items en verwijdert niets' {
        $bin = New-FakeBin
        $old = New-FakeRecycleItem $bin 'OUD001.pdf' 10 -Size 500
        $young = New-FakeRecycleItem $bin 'NIEUW1.pdf' 2
        $r = Invoke-Test (New-TestConfig @(New-FolderSpec (New-TestDir)) @{ emptyRecycleBin = $true; recycleBinRetentionDays = 7 }) 'Simulate' $false $true
        Assert-True (Test-ItemExists $old) 'oud item staat er nog'
        Assert-True (Test-ItemExists $young) 'nieuw item staat er nog'
        Assert-Equal 1 $r.RecycleBin.CandidateCount 'kandidaten'
        Assert-Equal 500 $r.RecycleBin.CandidateBytes 'bytes uit $I'
        Assert-Equal 1 $r.RecycleBin.TooYoungCount 'te recent'
        Assert-Equal 0 $r.RecycleBin.DeletedCount 'verwijderd'
        Assert-Equal 'C:\Users\test\Desktop\brief.pdf' @($r.RecycleBin.Candidates)[0].Name 'oorspronkelijk pad in handmatige simulatie'
    }
    Test-Case 'Nu opruimen leegt alleen oude items ($R en $I), ook verwijderde mappen' {
        $bin = New-FakeBin
        $oldFile = New-FakeRecycleItem $bin 'OUD002.pdf' 30
        $oldDir = New-FakeRecycleItem $bin 'OUD003' 30 -Directory
        $young = New-FakeRecycleItem $bin 'NIEUW2.pdf' 1
        $orphan = Join-Path $bin '$RWEES01.pdf'
        [System.IO.File]::WriteAllBytes($orphan, (New-Object byte[] 3))
        $r = Invoke-Test (New-TestConfig @(New-FolderSpec (New-TestDir)) @{ emptyRecycleBin = $true; recycleBinRetentionDays = 7 })
        Assert-True (Test-ItemGone $oldFile) 'oud bestand weg'
        Assert-True (Test-ItemGone $oldDir) 'oude map (met alleen-lezenbestand) weg'
        Assert-True (Test-ItemExists $young) 'recent item blijft'
        Assert-True ([System.IO.File]::Exists($orphan)) '$R zonder $I blijft staan'
        Assert-Equal 2 $r.RecycleBin.DeletedCount 'aantal geleegd'
        Assert-Equal 0 $r.ErrorCount 'fouten'
        Assert-Equal 0 $r.ExitCode 'exitcode'
    }
    Test-Case 'Termijn 0 = alles, maar nooit iets van minder dan 15 minuten geleden' {
        $bin = New-FakeBin
        $old = New-FakeRecycleItem $bin 'OUD004.pdf' 0.1
        $justNow = New-FakeRecycleItem $bin 'NET001.pdf' (5.0 / 1440)
        $r = Invoke-Test (New-TestConfig @(New-FolderSpec (New-TestDir)) @{ emptyRecycleBin = $true; recycleBinRetentionDays = 0 })
        Assert-True (Test-ItemGone $old) 'item van 2,4 uur geleden weg'
        Assert-True (Test-ItemExists $justNow) 'item van 5 minuten geleden blijft'
    }
    Test-Case 'Simulatiemodus, ontbrekende bevestiging en pauze: Prullenbak blijft onaangeroerd' {
        $bin = New-FakeBin
        $old = New-FakeRecycleItem $bin 'OUD005.pdf' 30
        $d = New-TestDir
        $base = @{ emptyRecycleBin = $true; recycleBinRetentionDays = 7 }
        $r = Invoke-Test (New-TestConfig @(New-FolderSpec $d) ($base + @{ simulationMode = $true; automaticCleanupConfirmed = $true })) 'Scheduled' $false
        Assert-True (Test-ItemExists $old) 'simulatiemodus'
        Assert-Equal 1 $r.RecycleBin.CandidateCount 'wel geteld in simulatiemodus'
        $null = Invoke-Test (New-TestConfig @(New-FolderSpec $d) ($base + @{ simulationMode = $false; automaticCleanupConfirmed = $false })) 'Scheduled' $false
        Assert-True (Test-ItemExists $old) 'zonder bevestiging'
        $null = Invoke-Test (New-TestConfig @(New-FolderSpec $d) ($base + @{ simulationMode = $false; automaticCleanupConfirmed = $true; paused = $true })) 'Scheduled' $false
        Assert-True (Test-ItemExists $old) 'gepauzeerd'
        $null = Invoke-Test (New-TestConfig @(New-FolderSpec $d) $base) 'CleanNow' $false
        Assert-True (Test-ItemExists $old) 'Nu opruimen zonder bevestiging'
        $r = Invoke-Test (New-TestConfig @(New-FolderSpec $d) ($base + @{ simulationMode = $false; automaticCleanupConfirmed = $true })) 'Scheduled' $false
        Assert-True (Test-ItemGone $old) 'actief: wel geleegd'
    }
    Test-Case 'Limiet overschreden: ook de Prullenbak wordt niet geleegd' {
        $bin = New-FakeBin
        $old = New-FakeRecycleItem $bin 'OUD006.pdf' 30
        $d = New-TestDir
        1..3 | ForEach-Object { $null = New-TestFile $d "f$_.pdf" 30 }
        $r = Invoke-Test (New-TestConfig @(New-FolderSpec $d) @{ emptyRecycleBin = $true; recycleBinRetentionDays = 7; maxFilesPerRun = 2 })
        Assert-Equal 3 $r.ExitCode 'limiet'
        Assert-True (Test-ItemExists $old) 'prullenbak onaangeroerd'
    }
    Test-Case 'Koppeling in een verwijderde map wordt niet gevolgd' {
        $bin = New-FakeBin
        $item = New-FakeRecycleItem $bin 'OUD007' 30 -Directory
        $target = New-TestDir 'doel-buiten-prullenbak'
        $tf = New-TestFile $target 'moet-blijven.pdf' 30
        if (-not (New-DirLink (Join-Path $item.Data 'koppeling') $target)) { Skip 'kon geen koppeling maken' }
        $r = Invoke-Test (New-TestConfig @(New-FolderSpec (New-TestDir)) @{ emptyRecycleBin = $true; recycleBinRetentionDays = 7 })
        Assert-Exists $tf
        Assert-True (Test-ItemGone $item) 'item zelf weg'
        Assert-Equal 1 $r.RecycleBin.DeletedCount 'geleegd'
    }
    Test-Case 'Status en logboek bevatten geen namen uit de Prullenbak' {
        $bin = New-FakeBin
        $null = New-FakeRecycleItem $bin 'OUD008.pdf' 30 'C:\Users\test\Desktop\GEHEIM-patient.pdf'
        $r = Invoke-Test (New-TestConfig @(New-FolderSpec (New-TestDir)) @{ emptyRecycleBin = $true; recycleBinRetentionDays = 7 }) 'Simulate' $false $true
        $json = ConvertTo-PcAsciiJson (Get-PcStatusObject -Result $r -Trigger 'test' -NowUtc ([DateTime]::UtcNow))
        Assert-True ($json -notmatch 'GEHEIM') 'geen naam in status'
        Assert-True ($json -match '"recycleBin"') 'prullenbak in status'
    }
    $script:PcRecycleBinRootsOverride = @()

    # ---------------------------------------------------------------- Logboek en uitvoer
    Write-Host "`nLogboek, status en JSON-uitvoer (end-to-end, apart proces)"
    Test-Case 'Logboek en status bevatten geen bestandsnamen; JSON alleen bij handmatige simulatie' {
        $d = New-TestDir
        $secret = 'Patient-Jansen-labuitslag-' + [guid]::NewGuid().ToString('N').Substring(0, 6) + '.pdf'
        $f = New-TestFile $d $secret 30
        $f2 = New-TestFile $d 'Brief-Müller-é.pdf' 30
        $cfg = New-TestConfig @(New-FolderSpec $d)
        $exe = (Get-Process -Id $PID).Path
        $out = & $exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $scriptPath -ConfigPath $cfg -Mode Simulate -Json -ShowFileNames
        $exit = $LASTEXITCODE
        $json = ($out -join "`n") | ConvertFrom-Json
        Assert-Exists $f; Assert-Exists $f2
        $cfgDir = Split-Path -Parent $cfg
        $log = [System.IO.File]::ReadAllText((Join-Path (Join-Path $cfgDir 'logs') 'praktijkclean.log'))
        $status = [System.IO.File]::ReadAllText((Join-Path $cfgDir 'status.json'))
        foreach ($text in @($log, $status)) {
            Assert-True ($text.IndexOf('Patient-Jansen') -lt 0) 'geen bestandsnaam in log/status'
            Assert-True ($text.IndexOf($d) -lt 0) 'geen volledig pad in log/status'
        }
        if ($script:PcIsWindows) {
            # In %TEMP% (binnen %LOCALAPPDATA%) moet de echte beveiliging de map weigeren.
            Assert-Equal 'Geblokkeerd' $json.folders[0].status 'status in %TEMP% (beschermde AppData)'
        } else {
            Assert-Equal 0 $exit 'exitcode'
            Assert-Equal 2 $json.candidates 'kandidaten'
            $names = @($json.fileDetails[0].candidates | ForEach-Object { $_.Name })
            Assert-True ($names -contains $secret) 'bestandsnaam in JSON voor beheerapp'
            Assert-True ($names -contains 'Brief-Müller-é.pdf') 'niet-ASCII-naam correct'
        }
        # Geplande run: nooit bestandsnamen in de JSON.
        $out2 = & $exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $scriptPath -ConfigPath $cfg -Mode Scheduled -Json -ShowFileNames
        Assert-True ((($out2 -join '')).IndexOf('Patient-Jansen') -lt 0) 'geen namen bij geplande run'
    }
    Test-Case 'Configuratiefout geeft exitcode 1 via apart proces' {
        $cfg = Write-RawConfig '{ kapot'
        $exe = (Get-Process -Id $PID).Path
        $null = & $exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $scriptPath -ConfigPath $cfg -Mode Scheduled
        Assert-Equal 1 $LASTEXITCODE 'exitcode'
    }
    Test-Case 'Logboek rouleert boven 512 kB' {
        $dir = New-TestDir 'logs'
        $log = Join-Path $dir 'praktijkclean.log'
        [System.IO.File]::WriteAllText($log, ('x' * 600000))
        Write-PcLog -LogDir $dir -Line 'test'
        Assert-True ([System.IO.File]::Exists((Join-Path $dir 'praktijkclean.1.log'))) 'oud logboek'
        Assert-True ((New-Object System.IO.FileInfo($log)).Length -lt 1000) 'nieuw logboek klein'
    }
    Test-Case 'Script gebruikt geen Invoke-Expression' {
        # Controle op de syntaxisboom (commentaar telt dus niet mee).
        $tokens = $null; $parseErrors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$tokens, [ref]$parseErrors)
        Assert-Equal 0 $parseErrors.Count 'syntaxfouten'
        $cmds = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true) |
            ForEach-Object { $_.GetCommandName() } | Where-Object { $_ -match '(?i)^(Invoke-Expression|iex|Invoke-Command|icm|Start-Process)$' }
        Assert-Equal 0 @($cmds).Count 'verboden commandos'
        $text = [System.IO.File]::ReadAllText($scriptPath)
        Assert-True ($text -notmatch '(?i)\[scriptblock\]::Create|AddScript\(|NewScriptBlock') 'geen dynamische scriptblokken'
    }
} finally {
    if (-not $KeepTemp) {
        # Alleen de eigen tijdelijke testmap opruimen (eerst alleen-lezen-attributen weghalen).
        Assert-InTestRoot $TestRoot
        Get-ChildItem -LiteralPath $TestRoot -Recurse -Force -ErrorAction SilentlyContinue |
            Where-Object { -not $_.PSIsContainer -and $_.IsReadOnly } |
            ForEach-Object { $_.IsReadOnly = $false }
        # Koppelingen eerst losmaken zodat nooit het doel wordt doorlopen.
        Get-ChildItem -LiteralPath $TestRoot -Recurse -Force -ErrorAction SilentlyContinue |
            Where-Object { ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 } |
            ForEach-Object { if ($_.PSIsContainer) { [System.IO.Directory]::Delete($_.FullName) } else { [System.IO.File]::Delete($_.FullName) } }
        Remove-Item -LiteralPath $TestRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ''
Write-Host "Resultaat: $($script:Passed) geslaagd, $($script:Failed) mislukt, $($script:Skipped) overgeslagen."
if ($script:Failed -gt 0) {
    foreach ($f in $script:Failures) { Write-Host " - $f" -ForegroundColor Red }
    exit 1
}
exit 0
