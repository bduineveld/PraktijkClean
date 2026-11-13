#requires -Version 5.1
<#
.SYNOPSIS
    PraktijkClean - ruimt oude bestanden op uit expliciet geselecteerde mappen.

.DESCRIPTION
    Dit script is GENERIEK: alle instellingen staan in config.json. Bij het wijzigen van
    instellingen verandert alleen config.json, nooit dit script.

    Veiligheidsprincipes (zie ook docs/ARCHITECTUUR.md):
      - Een fout in config.json betekent: NIETS verwijderen.
      - Alleen bestanden direct in de geselecteerde map (submappen alleen als expliciet aangezet).
      - Nooit mappen verwijderen, nooit symbolische links/junctions/reparse points volgen.
      - Systeemmappen, OneDrive/cloud en netwerklocaties worden geweigerd (cloud/netwerk alleen
        na expliciete toestemming per map).
      - Bestanden die in gebruik zijn, verborgen, systeem- of alleen-lezen zijn worden overgeslagen.
      - Maximaal aantal bestanden per uitvoering; bij overschrijding wordt er niets verwijderd.
      - Logboeken bevatten nooit bestandsnamen of paden.
      - Prullenbak legen (optioneel, standaard uit): alleen de eigen Prullenbak van deze gebruiker,
        alleen items die langer dan de ingestelde termijn in de Prullenbak staan, en alleen wanneer
        er ook bestanden verwijderd mogen worden (dus niet in simulatiemodus of bij pauze).
      - Geen Invoke-Expression of andere uitvoering van instellingen als code.

    Bestandsleeftijd: leeftijd = nu - de NIEUWSTE van (aanmaakdatum, wijzigingsdatum).
    Een pas gedownload of gekopieerd bestand heeft een nieuwe aanmaakdatum en wordt dus niet
    direct als "oud" gezien, ook als de wijzigingsdatum oud is.

.PARAMETER ConfigPath
    Pad naar config.json. Standaard: config.json naast dit script.

.PARAMETER Mode
    Scheduled  - automatische uitvoering vanuit Taakplanner (respecteert simulatie en pauze).
    Simulate   - alleen tellen, nooit verwijderen.
    CleanNow   - handmatig opruimen; vereist -ConfirmedByUser.

.PARAMETER ShowFileNames
    Alleen voor handmatige simulatie vanuit de beheerapp: bestandsnamen in de JSON-uitvoer
    (stdout). Bestandsnamen worden NOOIT naar het logboek of status.json geschreven.

.PARAMETER Json
    Schrijf het resultaat als JSON naar stdout (voor de beheerapp).

.NOTES
    Exitcodes: 0 = gelukt, 1 = configuratiefout, 2 = uitgevoerd met fouten,
               3 = limiet overschreden (niets verwijderd), 4 = er draait al een opschoning.
#>
[CmdletBinding()]
param(
    [string]$ConfigPath = '',
    [ValidateSet('Scheduled', 'Simulate', 'CleanNow')]
    [string]$Mode = 'Scheduled',
    [string]$Trigger = '',
    [switch]$ConfirmedByUser,
    [switch]$ShowFileNames,
    [switch]$Json
)

Set-StrictMode -Version 2.0

$script:PcVersion = '1.2.0'
$script:PcSchemaVersion = 1
$script:PcMaxConfigBytes = 262144
$script:PcMaxEntriesPerFolder = 100000
$script:PcMaxFolders = 20
$script:PcIsWindows = ($env:OS -eq 'Windows_NT')
# Alleen voor de tests: vaste lijst Prullenbakmappen in plaats van de echte (zie Get-PcRecycleBinRoots).
$script:PcRecycleBinRootsOverride = $null
$script:PcMaxRecycleDepth = 64
$script:PcIgnoreCase = [System.StringComparison]::OrdinalIgnoreCase
$script:PcSep = [System.IO.Path]::DirectorySeparatorChar

# Bestanden die altijd worden overgeslagen, ongeacht de instellingen.
$script:PcAlwaysSkipNames = @('desktop.ini', 'thumbs.db')
$script:PcInProgressExtensions = @('.crdownload', '.part', '.partial', '.download', '.opdownload')

# Cloud-/placeholder-attributen (FILE_ATTRIBUTE_OFFLINE, RECALL_ON_OPEN, PINNED, UNPINNED,
# RECALL_ON_DATA_ACCESS). Komen voor bij OneDrive "bestanden op aanvraag" en vergelijkbare diensten.
$script:PcCloudAttributeMask = 0x1000 -bor 0x40000 -bor 0x80000 -bor 0x100000 -bor 0x400000

# ---------------------------------------------------------------------------------------------
# Hulpfuncties voor JSON-objecten (ConvertFrom-Json levert PSCustomObject op)
# ---------------------------------------------------------------------------------------------

function Test-PcHasProp {
    param($Object, [string]$Name)
    if ($null -eq $Object -or $Object -isnot [System.Management.Automation.PSCustomObject]) { return $false }
    return ($null -ne $Object.PSObject.Properties[$Name])
}

function Get-PcProp {
    param($Object, [string]$Name)
    if (-not (Test-PcHasProp $Object $Name)) { return $null }
    return , $Object.PSObject.Properties[$Name].Value
}

function Test-PcIsInt {
    param($Value)
    return (($Value -is [int]) -or ($Value -is [long])) -and ($Value -isnot [bool])
}

# ---------------------------------------------------------------------------------------------
# Paden
# ---------------------------------------------------------------------------------------------

function ConvertTo-PcNormalPath {
    # Zet scheidingstekens gelijk en verwijdert afsluitende scheidingstekens (behalve bij een stationswortel).
    param([string]$Path)
    if ([string]::IsNullOrEmpty($Path)) { return '' }
    $p = $Path
    if ($script:PcIsWindows) { $p = $p.Replace('/', '\') }
    $root = ''
    try { $root = [System.IO.Path]::GetPathRoot($p) } catch { $root = '' }
    while ($p.Length -gt 1 -and $p.Length -gt $root.Length -and ($p.EndsWith('\') -or $p.EndsWith('/'))) {
        $p = $p.Substring(0, $p.Length - 1)
    }
    return $p
}

function Test-PcPathEquals {
    param([string]$A, [string]$B)
    return [string]::Equals((ConvertTo-PcNormalPath $A), (ConvertTo-PcNormalPath $B), $script:PcIgnoreCase)
}

function Test-PcPathInside {
    # Waar als $Child gelijk is aan $Parent of daarbinnen ligt.
    param([string]$Child, [string]$Parent)
    $c = ConvertTo-PcNormalPath $Child
    $p = ConvertTo-PcNormalPath $Parent
    if ($c.Length -eq 0 -or $p.Length -eq 0) { return $false }
    if ([string]::Equals($c, $p, $script:PcIgnoreCase)) { return $true }
    $prefix = $p
    if (-not ($prefix.EndsWith('\') -or $prefix.EndsWith('/'))) { $prefix = $prefix + $script:PcSep }
    return $c.StartsWith($prefix, $script:PcIgnoreCase)
}

function Test-PcIsNetworkPath {
    # UNC-paden en niet-vaste stations (netwerkstation, USB-stick, cd) gelden als "niet lokaal".
    param([string]$Path)
    if ($Path.StartsWith('\\') -or $Path.StartsWith('//')) { return $true }
    if (-not $script:PcIsWindows) { return $false }
    try {
        $root = [System.IO.Path]::GetPathRoot($Path)
        $drive = New-Object System.IO.DriveInfo($root)
        return ($drive.DriveType -ne [System.IO.DriveType]::Fixed)
    } catch {
        return $true   # Onbekend = voor de zekerheid als netwerk behandelen.
    }
}

function Get-PcSyncRoots {
    # Verzamelt bekende synchronisatiemappen (OneDrive en andere cloudproviders).
    $roots = New-Object System.Collections.Generic.List[string]
    foreach ($name in @('OneDrive', 'OneDriveCommercial', 'OneDriveConsumer')) {
        $v = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrWhiteSpace($v)) { $roots.Add($v) }
    }
    if ($script:PcIsWindows) {
        try {
            $accounts = Get-ChildItem -Path 'HKCU:\Software\Microsoft\OneDrive\Accounts' -ErrorAction Stop
            foreach ($a in $accounts) {
                $uf = (Get-ItemProperty -Path $a.PSPath -Name 'UserFolder' -ErrorAction SilentlyContinue)
                if ($null -ne $uf -and -not [string]::IsNullOrWhiteSpace($uf.UserFolder)) { $roots.Add($uf.UserFolder) }
            }
        } catch { }
        try {
            # Alle providers die de Windows Cloud Files API gebruiken (OneDrive, Dropbox, Google Drive, iCloud, ...)
            $mgr = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SyncRootManager'
            foreach ($provider in (Get-ChildItem -Path $mgr -ErrorAction Stop)) {
                $userRoots = Join-Path $provider.PSPath 'UserSyncRoots'
                $item = Get-Item -Path $userRoots -ErrorAction SilentlyContinue
                if ($null -eq $item) { continue }
                foreach ($valueName in $item.GetValueNames()) {
                    $val = $item.GetValue($valueName)
                    if ($val -is [string] -and -not [string]::IsNullOrWhiteSpace($val)) { $roots.Add($val) }
                }
            }
        } catch { }
    }
    return , $roots.ToArray()
}

function Test-PcIsSyncPath {
    param([string]$Path, [string[]]$SyncRoots)
    foreach ($r in $SyncRoots) {
        if (Test-PcPathInside $Path $r) { return $true }
    }
    # Naamheuristiek als aanvulling (bijv. "OneDrive - Praktijk X\Bureaublad").
    $segments = (ConvertTo-PcNormalPath $Path).Split([char[]]@('\', '/'), [System.StringSplitOptions]::RemoveEmptyEntries)
    foreach ($s in $segments) {
        if ($s -match '^(?i)(OneDrive( - .*)?|Dropbox( \(.*\))?|Google Drive|iCloud ?Drive.*)$') { return $true }
    }
    try {
        $di = New-Object System.IO.DirectoryInfo($Path)
        if ($di.Exists -and (([int]$di.Attributes -band $script:PcCloudAttributeMask) -ne 0)) { return $true }
    } catch { }
    return $false
}

function Get-PcProtectedPaths {
    # NoTouchTrees: de map zelf, alles daarbinnen en alle bovenliggende mappen zijn verboden.
    # NoTouchExact: de map zelf en bovenliggende mappen verboden; submappen (bijv. Bureaublad) toegestaan.
    param([string]$InstallDir)
    $trees = New-Object System.Collections.Generic.List[string]
    $exact = New-Object System.Collections.Generic.List[string]
    if ($script:PcIsWindows) {
        foreach ($v in @($env:windir, $env:SystemRoot, $env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:ProgramW6432,
                         $env:ProgramData, $env:APPDATA, $env:LOCALAPPDATA, $env:CommonProgramFiles)) {
            if (-not [string]::IsNullOrWhiteSpace($v)) { $trees.Add($v) }
        }
        if (-not [string]::IsNullOrWhiteSpace($env:SystemDrive)) { $exact.Add($env:SystemDrive + '\Users') }
        foreach ($v in @($env:USERPROFILE, $env:PUBLIC)) {
            if (-not [string]::IsNullOrWhiteSpace($v)) { $exact.Add($v) }
        }
    } else {
        # Alleen bedoeld voor het draaien van de tests buiten Windows.
        foreach ($v in @('/bin', '/boot', '/dev', '/etc', '/lib', '/lib64', '/proc', '/sbin', '/sys', '/usr', '/opt')) { $trees.Add($v) }
        foreach ($v in @('/home', $env:HOME)) { if (-not [string]::IsNullOrWhiteSpace($v)) { $exact.Add($v) } }
    }
    if (-not [string]::IsNullOrWhiteSpace($InstallDir)) { $trees.Add($InstallDir) }
    return [pscustomobject]@{ Trees = $trees.ToArray(); Exact = $exact.ToArray() }
}

function Test-PcHasReparsePointInChain {
    # Controleert de map zelf en alle bovenliggende mappen op symbolische links / junctions.
    param([string]$Path)
    $dir = New-Object System.IO.DirectoryInfo($Path)
    while ($null -ne $dir) {
        if ($dir.Exists -and (($dir.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)) { return $true }
        $dir = $dir.Parent
    }
    return $false
}

function Test-PcFolderSafety {
    <#
      Controleert of een map veilig is om in op te ruimen. Retourneert een object met
      Ok (bool), Reason (tekst zonder bestandsnamen), IsNetwork, IsSync.
    #>
    param(
        [string]$Path,
        [bool]$AllowNetworkOrSync,
        [string]$InstallDir,
        [string[]]$SyncRoots
    )
    $result = [pscustomobject]@{ Ok = $false; Reason = ''; IsNetwork = $false; IsSync = $false; FullPath = '' }

    if ([string]::IsNullOrWhiteSpace($Path)) { $result.Reason = 'Geen pad opgegeven.'; return $result }
    if ($Path.IndexOfAny([System.IO.Path]::GetInvalidPathChars()) -ge 0 -or $Path.IndexOf('*') -ge 0 -or $Path.IndexOf('?') -ge 0) {
        $result.Reason = 'Pad bevat ongeldige tekens.'; return $result
    }
    if ($Path.StartsWith('\\?\') -or $Path.StartsWith('\\.\')) { $result.Reason = 'Apparaatpaden zijn niet toegestaan.'; return $result }

    $segments = $Path.Split([char[]]@('\', '/'), [System.StringSplitOptions]::None)
    foreach ($s in $segments) {
        if ($s -eq '..' -or $s -eq '.') { $result.Reason = 'Pad bevat ".." of "." (niet toegestaan).'; return $result }
        # Korte 8.3-namen (bijv. PROGRA~1) zouden de controle op beschermde mappen kunnen omzeilen.
        if ($s -match '~[0-9]') { $result.Reason = 'Korte padnamen (zoals PROGRA~1) zijn niet toegestaan; gebruik het volledige pad.'; return $result }
    }

    if ($script:PcIsWindows) {
        if (-not ($Path -match '^[A-Za-z]:\\' -or $Path -match '^\\\\[^\\]+\\[^\\]+')) {
            $result.Reason = 'Pad moet volledig zijn (bijv. C:\Users\naam\Downloads).'; return $result
        }
        # Een dubbele punt na de stationsletter duidt op alternate data streams.
        if ($Path.Substring(2).IndexOf(':') -ge 0) { $result.Reason = 'Pad bevat een ongeldige dubbele punt.'; return $result }
    } else {
        if (-not $Path.StartsWith('/')) { $result.Reason = 'Pad moet volledig zijn.'; return $result }
    }

    $full = ''
    try { $full = [System.IO.Path]::GetFullPath($Path) } catch { $result.Reason = 'Pad kan niet worden gelezen.'; return $result }
    if (-not (Test-PcPathEquals $full $Path)) { $result.Reason = 'Pad is niet eenduidig (wordt anders uitgelegd door Windows).'; return $result }
    $full = ConvertTo-PcNormalPath $full
    $result.FullPath = $full

    $root = [System.IO.Path]::GetPathRoot($full)
    if ([string]::IsNullOrEmpty($root) -or (Test-PcPathEquals $full $root)) { $result.Reason = 'Een stationswortel (bijv. C:\) is niet toegestaan.'; return $result }

    $protected = Get-PcProtectedPaths -InstallDir $InstallDir
    foreach ($t in $protected.Trees) {
        if ((Test-PcPathInside $full $t) -or (Test-PcPathInside $t $full)) {
            $result.Reason = 'Systeemmap, programmamap of PraktijkClean-map is niet toegestaan.'; return $result
        }
    }
    foreach ($e in $protected.Exact) {
        if (Test-PcPathInside $e $full) {
            $result.Reason = 'Deze map (of een bovenliggende map van het gebruikersprofiel) is niet toegestaan.'; return $result
        }
    }

    if (-not [System.IO.Directory]::Exists($full)) { $result.Reason = 'Map bestaat niet (meer).'; return $result }

    if (Test-PcHasReparsePointInChain $full) {
        $result.Reason = 'Map is (of ligt in) een symbolische koppeling of junction; wordt niet gevolgd.'; return $result
    }

    $result.IsNetwork = Test-PcIsNetworkPath $full
    $result.IsSync = Test-PcIsSyncPath -Path $full -SyncRoots $SyncRoots
    if (($result.IsNetwork -or $result.IsSync) -and -not $AllowNetworkOrSync) {
        if ($result.IsSync) {
            $result.Reason = 'Map wordt gesynchroniseerd met OneDrive of een andere clouddienst. Automatisch verwijderen is hier standaard geblokkeerd.'
        } else {
            $result.Reason = 'Map staat op een netwerklocatie of verwisselbaar station. Automatisch verwijderen is hier standaard geblokkeerd.'
        }
        return $result
    }

    $result.Ok = $true
    return $result
}

# ---------------------------------------------------------------------------------------------
# Configuratie
# ---------------------------------------------------------------------------------------------

function Test-PcStringList {
    # Valideert een lijst met tekst; $Kind bepaalt welke tekens zijn toegestaan.
    param($Value, [string]$Label, [string]$Kind, [System.Collections.Generic.List[string]]$Errors)
    $out = New-Object System.Collections.Generic.List[string]
    if ($null -eq $Value) { $Errors.Add("$Label ontbreekt."); return , $out.ToArray() }
    if ($Value -isnot [System.Array]) { $Errors.Add("$Label moet een lijst zijn."); return , $out.ToArray() }
    if ($Value.Count -gt 200) { $Errors.Add("$Label bevat te veel items (max 200)."); return , $out.ToArray() }
    foreach ($item in $Value) {
        if ($item -isnot [string] -or [string]::IsNullOrWhiteSpace($item) -or $item.Length -gt 260) {
            $Errors.Add("$Label bevat een ongeldig item."); continue
        }
        $s = $item.Trim()
        $bad = $false
        foreach ($ch in $s.ToCharArray()) { if ([int]$ch -lt 32) { $bad = $true } }
        switch ($Kind) {
            'extension' { if ($s -notmatch '^\.[A-Za-z0-9_\-]{1,20}$') { $bad = $true } }
            'pattern'   { if ($s.IndexOfAny([char[]]@('\', '/', ':', '[', ']', '"', '<', '>', '|')) -ge 0) { $bad = $true } }
            'relative'  {
                if ($s.IndexOfAny([char[]]@(':', '*', '?', '"', '<', '>', '|')) -ge 0) { $bad = $true }
                if ($s.StartsWith('\') -or $s.StartsWith('/')) { $bad = $true }
                foreach ($seg in $s.Split([char[]]@('\', '/'), [System.StringSplitOptions]::None)) {
                    if ($seg -eq '..' -or $seg -eq '.' -or $seg -eq '') { $bad = $true }
                }
            }
        }
        if ($bad) { $Errors.Add("$Label bevat een ongeldig item."); continue }
        if ($Kind -eq 'relative' -and $script:PcIsWindows) { $s = $s.Replace('/', '\') }
        $out.Add($s)
    }
    return , $out.ToArray()
}

function Test-PcConfig {
    <#
      Valideert de ingelezen configuratie STRIKT. Ontbrekende of ongeldige waarden krijgen
      nooit een standaardwaarde: elke fout maakt de hele configuratie ongeldig.
      Retourneert @{ Errors = string[]; Config = genormaliseerd object of $null }.
    #>
    param($Raw)
    $errors = New-Object System.Collections.Generic.List[string]

    if ($null -eq $Raw -or $Raw -isnot [System.Management.Automation.PSCustomObject]) {
        $errors.Add('De configuratie is geen geldig JSON-object.')
        return [pscustomobject]@{ Errors = $errors.ToArray(); Config = $null }
    }

    $schema = Get-PcProp $Raw 'schemaVersion'
    if (-not (Test-PcIsInt $schema) -or $schema -ne $script:PcSchemaVersion) { $errors.Add("schemaVersion moet $($script:PcSchemaVersion) zijn.") }

    $bools = @{}
    foreach ($name in @('simulationMode', 'automaticCleanupConfirmed', 'paused')) {
        $v = Get-PcProp $Raw $name
        if ($v -isnot [bool]) { $errors.Add("$name moet true of false zijn.") } else { $bools[$name] = $v }
    }

    $maxFiles = Get-PcProp $Raw 'maxFilesPerRun'
    if (-not (Test-PcIsInt $maxFiles) -or $maxFiles -lt 1 -or $maxFiles -gt 5000) { $errors.Add('maxFilesPerRun moet een geheel getal tussen 1 en 5000 zijn.') }

    $minAge = Get-PcProp $Raw 'minimumAgeMinutes'
    if (-not (Test-PcIsInt $minAge) -or $minAge -lt 1 -or $minAge -gt 1440) { $errors.Add('minimumAgeMinutes moet een geheel getal tussen 1 en 1440 zijn.') }

    $deleteMode = Get-PcProp $Raw 'deleteMode'
    if ($deleteMode -isnot [string] -or @('recycleBin', 'permanent') -notcontains $deleteMode) { $errors.Add('deleteMode moet "recycleBin" of "permanent" zijn.') }

    # Prullenbak legen: optioneel (ontbreekt = uit, zodat oudere config.json-bestanden geldig blijven).
    # Staat het aan, dan MOET de termijn aanwezig en geldig zijn (nooit een standaardtermijn aannemen).
    $emptyBin = $false
    $binDays = 0
    if (Test-PcHasProp $Raw 'emptyRecycleBin') {
        $v = Get-PcProp $Raw 'emptyRecycleBin'
        if ($v -isnot [bool]) { $errors.Add('emptyRecycleBin moet true of false zijn.') } else { $emptyBin = $v }
    }
    if ($emptyBin -or (Test-PcHasProp $Raw 'recycleBinRetentionDays')) {
        $v = Get-PcProp $Raw 'recycleBinRetentionDays'
        if (-not (Test-PcIsInt $v) -or $v -lt 0 -or $v -gt 3650) { $errors.Add('recycleBinRetentionDays moet een geheel getal tussen 0 en 3650 zijn.') } else { $binDays = [int]$v }
    }

    $folders = New-Object System.Collections.Generic.List[object]
    $rawFolders = Get-PcProp $Raw 'folders'
    if ($null -eq $rawFolders -or $rawFolders -isnot [System.Array]) {
        $errors.Add('folders moet een lijst zijn.')
    } elseif ($rawFolders.Count -gt $script:PcMaxFolders) {
        $errors.Add("Te veel mappen (maximaal $($script:PcMaxFolders)).")
    } else {
        $ids = @{}
        $index = 0
        foreach ($f in $rawFolders) {
            $index++
            $label = "Map $index"
            if ($f -isnot [System.Management.Automation.PSCustomObject]) { $errors.Add("$label is geen geldig object."); continue }
            $id = Get-PcProp $f 'id'
            if ($id -isnot [string] -or $id -notmatch '^[a-z0-9][a-z0-9\-]{0,39}$') { $errors.Add("$label heeft een ongeldige id."); continue }
            $label = "Map '$id'"
            if ($ids.ContainsKey($id)) { $errors.Add("$label komt dubbel voor."); continue }
            $ids[$id] = $true

            $enabled = Get-PcProp $f 'enabled'
            if ($enabled -isnot [bool]) { $errors.Add("${label}: enabled moet true of false zijn.") }

            $type = Get-PcProp $f 'type'
            $known = $null
            $path = $null
            if ($type -eq 'knownFolder') {
                $known = Get-PcProp $f 'knownFolder'
                if ($known -isnot [string] -or @('Desktop', 'Downloads') -notcontains $known) { $errors.Add("${label}: knownFolder moet Desktop of Downloads zijn.") }
            } elseif ($type -eq 'custom') {
                $path = Get-PcProp $f 'path'
                if ($path -isnot [string] -or [string]::IsNullOrWhiteSpace($path)) { $errors.Add("${label}: path ontbreekt.") }
            } else {
                $errors.Add("${label}: type moet knownFolder of custom zijn.")
            }

            $expected = Get-PcProp $f 'expectedPath'
            if ($expected -isnot [string] -or [string]::IsNullOrWhiteSpace($expected)) { $errors.Add("${label}: expectedPath ontbreekt.") }

            $days = Get-PcProp $f 'retentionDays'
            if (-not (Test-PcIsInt $days) -or $days -lt 0 -or $days -gt 3650) { $errors.Add("${label}: retentionDays moet een geheel getal tussen 0 en 3650 zijn.") }

            $recurse = Get-PcProp $f 'recurse'
            if ($recurse -isnot [bool]) { $errors.Add("${label}: recurse moet true of false zijn.") }

            $allow = Get-PcProp $f 'allowNetworkOrSync'
            if ($allow -isnot [bool]) { $errors.Add("${label}: allowNetworkOrSync moet true of false zijn.") }

            $ex = Get-PcProp $f 'exclusions'
            if ($ex -isnot [System.Management.Automation.PSCustomObject]) {
                $errors.Add("${label}: exclusions ontbreekt.")
                continue
            }
            $exFiles = Test-PcStringList (Get-PcProp $ex 'files') "${label}: exclusions.files" 'relative' $errors
            $exDirs = Test-PcStringList (Get-PcProp $ex 'subfolders') "${label}: exclusions.subfolders" 'relative' $errors
            $exPatterns = Test-PcStringList (Get-PcProp $ex 'patterns') "${label}: exclusions.patterns" 'pattern' $errors
            $exExt = Test-PcStringList (Get-PcProp $ex 'extensions') "${label}: exclusions.extensions" 'extension' $errors

            $folders.Add([pscustomobject]@{
                Id                 = $id
                Enabled            = $enabled
                Type               = $type
                KnownFolder        = $known
                Path               = $path
                ExpectedPath       = $expected
                RetentionDays      = $days
                Recurse            = $recurse
                AllowNetworkOrSync = $allow
                ExcludeFiles       = $exFiles
                ExcludeSubfolders  = $exDirs
                ExcludePatterns    = $exPatterns
                ExcludeExtensions  = $exExt
            })
        }
    }

    if ($errors.Count -gt 0) {
        return [pscustomobject]@{ Errors = $errors.ToArray(); Config = $null }
    }
    $cfg = [pscustomobject]@{
        SimulationMode            = $bools['simulationMode']
        AutomaticCleanupConfirmed = $bools['automaticCleanupConfirmed']
        Paused                    = $bools['paused']
        MaxFilesPerRun            = [int]$maxFiles
        MinimumAgeMinutes         = [int]$minAge
        DeleteMode                = $deleteMode
        EmptyRecycleBin           = $emptyBin
        RecycleBinRetentionDays   = $binDays
        Folders                   = $folders.ToArray()
    }
    return [pscustomobject]@{ Errors = @(); Config = $cfg }
}

function Read-PcConfig {
    # Leest en valideert config.json. Gooit nooit een uitzondering; fouten staan in .Errors.
    param([string]$Path)
    try {
        if (-not [System.IO.File]::Exists($Path)) {
            return [pscustomobject]@{ Errors = @('config.json niet gevonden.'); Config = $null }
        }
        $info = New-Object System.IO.FileInfo($Path)
        if ($info.Length -gt $script:PcMaxConfigBytes) {
            return [pscustomobject]@{ Errors = @('config.json is onverwacht groot.'); Config = $null }
        }
        $text = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
        if ([string]::IsNullOrWhiteSpace($text)) {
            return [pscustomobject]@{ Errors = @('config.json is leeg.'); Config = $null }
        }
        $raw = $text | ConvertFrom-Json -ErrorAction Stop
    } catch {
        return [pscustomobject]@{ Errors = @('config.json kan niet worden gelezen (ongeldige JSON).'); Config = $null }
    }
    return Test-PcConfig $raw
}

function Resolve-PcKnownFolder {
    # Bepaalt de werkelijke locatie van Bureaublad/Downloads via Windows (geen hardcoded paden).
    param([string]$Name)
    if ($Name -eq 'Desktop') {
        return [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
    }
    if ($Name -eq 'Downloads') {
        if (-not $script:PcIsWindows) { return '' }
        try {
            $shell = New-Object -ComObject Shell.Application
            $ns = $shell.NameSpace('shell:Downloads')
            if ($null -ne $ns) { return [string]$ns.Self.Path }
        } catch { }
        try {
            $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders'
            $raw = (Get-ItemProperty -Path $key -Name '{374DE290-123F-4565-9164-39C4925E467B}' -ErrorAction Stop).'{374DE290-123F-4565-9164-39C4925E467B}'
            return [Environment]::ExpandEnvironmentVariables($raw)
        } catch { }
    }
    return ''
}

# ---------------------------------------------------------------------------------------------
# Beoordelen van bestanden
# ---------------------------------------------------------------------------------------------

function Get-PcFileReferenceTimeUtc {
    # Leeftijdsregel: de NIEUWSTE van aanmaak- en wijzigingsdatum telt.
    param([System.IO.FileSystemInfo]$Item)
    return Get-PcNewestTime -CreationUtc $Item.CreationTimeUtc -LastWriteUtc $Item.LastWriteTimeUtc
}

function Get-PcNewestTime {
    param([datetime]$CreationUtc, [datetime]$LastWriteUtc)
    if ($CreationUtc -gt $LastWriteUtc) { return $CreationUtc }
    return $LastWriteUtc
}

function Test-PcIsOldEnough {
    param([datetime]$ReferenceUtc, [datetime]$NowUtc, [int]$RetentionDays, [int]$MinimumAgeMinutes)
    $age = $NowUtc - $ReferenceUtc
    if ($age.TotalMinutes -lt 0) { return $false }          # Datum in de toekomst: nooit oud.
    if ($age.TotalMinutes -lt $MinimumAgeMinutes) { return $false }
    return ($age.TotalDays -ge $RetentionDays)
}

function Get-PcExclusionReason {
    # Geeft de reden waarom een bestand wordt overgeslagen, of '' als het in aanmerking komt.
    param([System.IO.FileInfo]$File, [string]$RelativePath, $Folder)
    $attr = $File.Attributes
    if (($attr -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { return 'Koppeling of cloudbestand' }
    if (([int]$attr -band $script:PcCloudAttributeMask) -ne 0) { return 'Koppeling of cloudbestand' }
    if (($attr -band [System.IO.FileAttributes]::System) -ne 0 -or ($attr -band [System.IO.FileAttributes]::Hidden) -ne 0) { return 'Verborgen of systeembestand' }
    if (($attr -band [System.IO.FileAttributes]::ReadOnly) -ne 0) { return 'Alleen-lezen' }
    $name = $File.Name
    if ($script:PcIsWindows -eq $false -and $name.StartsWith('.')) { return 'Verborgen of systeembestand' }
    foreach ($n in $script:PcAlwaysSkipNames) { if ([string]::Equals($name, $n, $script:PcIgnoreCase)) { return 'Verborgen of systeembestand' } }
    if ($name.StartsWith('~$')) { return 'Tijdelijk Office-bestand (document open)' }
    $ext = [System.IO.Path]::GetExtension($name)
    foreach ($e in $script:PcInProgressExtensions) { if ([string]::Equals($ext, $e, $script:PcIgnoreCase)) { return 'Download nog bezig' } }
    foreach ($e in $Folder.ExcludeExtensions) { if ([string]::Equals($ext, $e, $script:PcIgnoreCase)) { return 'Uitzondering: extensie' } }
    foreach ($f in $Folder.ExcludeFiles) {
        if ([string]::Equals($RelativePath, $f, $script:PcIgnoreCase) -or [string]::Equals($name, $f, $script:PcIgnoreCase)) { return 'Uitzondering: bestand' }
    }
    foreach ($p in $Folder.ExcludePatterns) {
        $wp = New-Object System.Management.Automation.WildcardPattern($p, [System.Management.Automation.WildcardOptions]::IgnoreCase)
        if ($wp.IsMatch($name)) { return 'Uitzondering: patroon' }
    }
    return ''
}

function Test-PcSubfolderExcluded {
    param([string]$RelativeDir, $Folder)
    foreach ($d in $Folder.ExcludeSubfolders) {
        if ([string]::Equals($RelativeDir, $d, $script:PcIgnoreCase)) { return $true }
        if ($RelativeDir.StartsWith($d + $script:PcSep, $script:PcIgnoreCase)) { return $true }
    }
    return $false
}

function Get-PcFolderScan {
    <#
      Doorloopt een map (en alleen bij Recurse ook submappen, zonder reparse points te volgen)
      en deelt bestanden in: kandidaat, te jong of overgeslagen. Verwijdert niets.
    #>
    param($Folder, [string]$RootPath, [datetime]$NowUtc, [int]$MinimumAgeMinutes)
    $scan = [pscustomobject]@{
        Candidates      = New-Object System.Collections.Generic.List[object]
        Skipped         = New-Object System.Collections.Generic.List[object]
        TooYoungCount   = 0
        SkippedByReason = @{}
        ErrorCount      = 0
        Truncated       = $false
    }
    $stack = New-Object System.Collections.Generic.Stack[object]
    $stack.Push(@($RootPath, ''))
    $seen = 0
    while ($stack.Count -gt 0) {
        $entry = $stack.Pop()
        $dirPath = $entry[0]
        $relDir = $entry[1]
        $items = $null
        try {
            $di = New-Object System.IO.DirectoryInfo($dirPath)
            $items = $di.GetFileSystemInfos()
        } catch {
            $scan.ErrorCount++
            continue
        }
        foreach ($item in ($items | Sort-Object -Property Name)) {
            $seen++
            if ($seen -gt $script:PcMaxEntriesPerFolder) { $scan.Truncated = $true; break }
            if ($relDir -eq '') { $rel = $item.Name } else { $rel = $relDir + $script:PcSep + $item.Name }

            if ($item -is [System.IO.DirectoryInfo]) {
                # Mappen worden nooit verwijderd. Alleen doorzoeken als recursie aanstaat.
                if (-not $Folder.Recurse) { continue }
                if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
                if (($item.Attributes -band [System.IO.FileAttributes]::Hidden) -ne 0 -or ($item.Attributes -band [System.IO.FileAttributes]::System) -ne 0) { continue }
                if (Test-PcSubfolderExcluded $rel $Folder) { continue }
                $stack.Push(@($item.FullName, $rel))
                continue
            }

            $reason = Get-PcExclusionReason -File $item -RelativePath $rel -Folder $Folder
            if ($reason -eq '') {
                $refTime = Get-PcFileReferenceTimeUtc $item
                if (-not (Test-PcIsOldEnough -ReferenceUtc $refTime -NowUtc $NowUtc -RetentionDays $Folder.RetentionDays -MinimumAgeMinutes $MinimumAgeMinutes)) {
                    $scan.TooYoungCount++
                    continue
                }
                $scan.Candidates.Add([pscustomobject]@{ FullPath = $item.FullName; RelativePath = $rel; Size = [long]$item.Length })
            } else {
                $scan.Skipped.Add([pscustomobject]@{ RelativePath = $rel; Reason = $reason })
                if ($scan.SkippedByReason.ContainsKey($reason)) { $scan.SkippedByReason[$reason]++ } else { $scan.SkippedByReason[$reason] = 1 }
            }
        }
        if ($scan.Truncated) { break }
    }
    return $scan
}

# ---------------------------------------------------------------------------------------------
# Verwijderen
# ---------------------------------------------------------------------------------------------

function Remove-PcFile {
    <#
      Verwijdert een bestand na hercontrole. Retourneert '' bij succes, anders een reden.
      Hercontrole vlak voor verwijderen: bestaat nog, binnen de map, geen koppeling,
      niet verborgen/systeem/alleen-lezen, niet in gebruik, nog steeds oud genoeg.
    #>
    param([string]$FullPath, [string]$RootPath, $Folder, [string]$DeleteMode, [datetime]$NowUtc, [int]$MinimumAgeMinutes)
    try {
        if (-not (Test-PcPathInside $FullPath $RootPath) -or (Test-PcPathEquals $FullPath $RootPath)) { return 'Buiten geselecteerde map' }
        $fi = New-Object System.IO.FileInfo($FullPath)
        $fi.Refresh()
        if (-not $fi.Exists) { return 'Bestaat niet meer' }
        $parent = $fi.Directory
        while ($null -ne $parent -and -not (Test-PcPathEquals $parent.FullName $RootPath)) {
            if (($parent.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { return 'Koppeling of cloudbestand' }
            $parent = $parent.Parent
        }
        if ($null -eq $parent) { return 'Buiten geselecteerde map' }
        $rel = $FullPath.Substring((ConvertTo-PcNormalPath $RootPath).Length).TrimStart('\', '/')
        $reason = Get-PcExclusionReason -File $fi -RelativePath $rel -Folder $Folder
        if ($reason -ne '') { return $reason }
        if (-not (Test-PcIsOldEnough -ReferenceUtc (Get-PcFileReferenceTimeUtc $fi) -NowUtc $NowUtc -RetentionDays $Folder.RetentionDays -MinimumAgeMinutes $MinimumAgeMinutes)) { return 'Inmiddels gewijzigd' }

        # In gebruik? Probeer exclusief te openen.
        $fs = $null
        try {
            $fs = [System.IO.File]::Open($FullPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
        } catch {
            return 'In gebruik of niet toegankelijk'
        } finally {
            if ($null -ne $fs) { $fs.Dispose() }
        }

        if ($DeleteMode -eq 'recycleBin') {
            Add-Type -AssemblyName Microsoft.VisualBasic
            [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile(
                $FullPath,
                [Microsoft.VisualBasic.FileIO.UIOption]::OnlyErrorDialogs,
                [Microsoft.VisualBasic.FileIO.RecycleOption]::SendToRecycleBin,
                [Microsoft.VisualBasic.FileIO.UICancelOption]::ThrowException)
        } else {
            [System.IO.File]::Delete($FullPath)
        }
        if ([System.IO.File]::Exists($FullPath)) { return 'Verwijderen mislukt' }
        return ''
    } catch {
        # Bewust alleen het type fout; foutteksten kunnen paden bevatten.
        return 'Fout: ' + $_.Exception.GetType().Name
    }
}

# ---------------------------------------------------------------------------------------------
# Prullenbak legen
# ---------------------------------------------------------------------------------------------
# Windows bewaart per station en per gebruiker de Prullenbak in <station>:\$Recycle.Bin\<SID>.
# Elk verwijderd item bestaat uit een $I-bestand (oorspronkelijk pad, grootte en tijdstip van
# verwijderen) en een $R-bestand of -map met de inhoud. PraktijkClean leest de $I-bestanden zelf,
# zodat items op leeftijd (tijd in de Prullenbak) kunnen worden geselecteerd.

function Get-PcRecycleBinRoots {
    # De Prullenbakmappen van de huidige gebruiker op alle lokale vaste schijven.
    if ($null -ne $script:PcRecycleBinRootsOverride) { return @($script:PcRecycleBinRootsOverride) }
    if (-not $script:PcIsWindows) { return @() }
    $roots = @()
    try { $sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value } catch { return @() }
    foreach ($d in [System.IO.DriveInfo]::GetDrives()) {
        try {
            if ($d.DriveType -ne [System.IO.DriveType]::Fixed -or -not $d.IsReady) { continue }
            $p = [System.IO.Path]::Combine($d.RootDirectory.FullName, '$Recycle.Bin', $sid)
            if ([System.IO.Directory]::Exists($p)) { $roots += $p }
        } catch { }
    }
    return $roots
}

function Read-PcRecycleInfo {
    # Leest een $I-bestand (versie 1: Vista/7, versie 2: Windows 10/11). $null als het onleesbaar is.
    param([string]$Path)
    try {
        $b = [System.IO.File]::ReadAllBytes($Path)
        if ($b.Length -lt 24) { return $null }
        $ver = [System.BitConverter]::ToInt64($b, 0)
        $size = [System.BitConverter]::ToInt64($b, 8)
        $ft = [System.BitConverter]::ToInt64($b, 16)
        if ($ft -le 0) { return $null }
        $deleted = [DateTime]::FromFileTimeUtc($ft)
        $name = ''
        if ($ver -eq 1) {
            $name = [System.Text.Encoding]::Unicode.GetString($b, 24, [Math]::Min(520, $b.Length - 24))
        } elseif ($ver -eq 2) {
            if ($b.Length -ge 28) {
                $n = [System.BitConverter]::ToInt32($b, 24)
                if ($n -gt 0 -and (28 + 2 * [long]$n) -le $b.Length) { $name = [System.Text.Encoding]::Unicode.GetString($b, 28, 2 * $n) }
            }
        } else {
            return $null
        }
        $name = $name.Split([char]0)[0]
        if ($size -lt 0) { $size = 0 }
        return [pscustomobject]@{ Size = [long]$size; DeletedUtc = $deleted; OriginalPath = $name }
    } catch {
        return $null
    }
}

function Get-PcRecycleBinScan {
    # Bepaalt welke items lang genoeg in de Prullenbak staan. Verwijdert niets.
    param([string[]]$Roots, [int]$RetentionDays, [datetime]$NowUtc, [int]$MinimumAgeMinutes)
    $items = New-Object System.Collections.Generic.List[object]
    $tooYoung = 0
    $errorCount = 0
    foreach ($root in $Roots) {
        try {
            $rd = New-Object System.IO.DirectoryInfo($root)
            if (-not $rd.Exists) { continue }
            if (($rd.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { $errorCount++; continue }
            foreach ($fi in $rd.GetFiles('$I*')) {
                if (-not $fi.Name.StartsWith('$I', $script:PcIgnoreCase) -or $fi.Name.Length -lt 3 -or $fi.Length -gt 65536) { continue }
                if (($fi.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
                $info = Read-PcRecycleInfo $fi.FullName
                if ($null -eq $info) { continue }
                $dataPath = [System.IO.Path]::Combine($rd.FullName, '$R' + $fi.Name.Substring(2))
                if (-not ([System.IO.File]::Exists($dataPath) -or [System.IO.Directory]::Exists($dataPath))) { continue }
                if (-not (Test-PcIsOldEnough -ReferenceUtc $info.DeletedUtc -NowUtc $NowUtc -RetentionDays $RetentionDays -MinimumAgeMinutes $MinimumAgeMinutes)) {
                    $tooYoung++
                    continue
                }
                $items.Add([pscustomobject]@{
                    Root = $rd.FullName; InfoPath = $fi.FullName; DataPath = $dataPath
                    OriginalPath = $info.OriginalPath; Size = $info.Size; DeletedUtc = $info.DeletedUtc
                })
            }
        } catch {
            $errorCount++
        }
    }
    return [pscustomobject]@{ Items = $items.ToArray(); TooYoungCount = $tooYoung; ErrorCount = $errorCount }
}

function Remove-PcRecycleTree {
    # Verwijdert een map uit de Prullenbak. Koppelingen (junctions/symbolische links) worden alleen
    # zelf verwijderd en NOOIT gevolgd, zodat er nooit iets buiten de Prullenbak wordt geraakt.
    param([string]$Path, [int]$Depth = 0)
    if ($Depth -gt $script:PcMaxRecycleDepth) { throw (New-Object System.IO.IOException('Te diep geneste map.')) }
    $di = New-Object System.IO.DirectoryInfo($Path)
    if (($di.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        [System.IO.Directory]::Delete($di.FullName, $false)
        return
    }
    foreach ($e in $di.GetFileSystemInfos()) {
        if (($e.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            if ($e -is [System.IO.DirectoryInfo]) { [System.IO.Directory]::Delete($e.FullName, $false) } else { [System.IO.File]::Delete($e.FullName) }
        } elseif ($e -is [System.IO.DirectoryInfo]) {
            Remove-PcRecycleTree -Path $e.FullName -Depth ($Depth + 1)
        } else {
            if (($e.Attributes -band [System.IO.FileAttributes]::ReadOnly) -ne 0) { $e.Attributes = [System.IO.FileAttributes]::Normal }
            [System.IO.File]::Delete($e.FullName)
        }
    }
    if (($di.Attributes -band [System.IO.FileAttributes]::ReadOnly) -ne 0) { $di.Attributes = [System.IO.FileAttributes]::Directory }
    [System.IO.Directory]::Delete($di.FullName, $false)
}

function Remove-PcRecycleItem {
    # Verwijdert een Prullenbak-item definitief ($R-inhoud eerst, dan het $I-bestand).
    # Retourneert '' bij succes, anders een reden (zonder paden).
    param($Item)
    try {
        if (-not (Test-PcPathInside $Item.DataPath $Item.Root) -or (Test-PcPathEquals $Item.DataPath $Item.Root)) { return 'Buiten de Prullenbak' }
        if (-not (Test-PcPathInside $Item.InfoPath $Item.Root) -or (Test-PcPathEquals $Item.InfoPath $Item.Root)) { return 'Buiten de Prullenbak' }
        if ([System.IO.Directory]::Exists($Item.DataPath)) {
            Remove-PcRecycleTree -Path $Item.DataPath
        } elseif ([System.IO.File]::Exists($Item.DataPath)) {
            $fi = New-Object System.IO.FileInfo($Item.DataPath)
            if (($fi.Attributes -band [System.IO.FileAttributes]::ReadOnly) -ne 0) { $fi.Attributes = [System.IO.FileAttributes]::Normal }
            [System.IO.File]::Delete($Item.DataPath)
        } else {
            return 'Bestaat niet meer'
        }
        if ([System.IO.File]::Exists($Item.DataPath) -or [System.IO.Directory]::Exists($Item.DataPath)) { return 'Verwijderen mislukt' }
        if ([System.IO.File]::Exists($Item.InfoPath)) { [System.IO.File]::Delete($Item.InfoPath) }
        return ''
    } catch {
        return 'Fout: ' + $_.Exception.GetType().Name
    }
}

# ---------------------------------------------------------------------------------------------
# Hoofdlogica
# ---------------------------------------------------------------------------------------------

function Invoke-PcCleanup {
    <#
      Voert simulatie of opschoning uit en retourneert een resultaatobject.
      Schrijft zelf geen log/status; dat doet Invoke-PcMain.
    #>
    param(
        [string]$ConfigPath,
        [string]$Mode,
        [bool]$ConfirmedByUser,
        [bool]$IncludeFileNames,
        [datetime]$NowUtc,
        [string]$InstallDir
    )
    $result = [pscustomobject]@{
        Version         = $script:PcVersion
        Mode            = $Mode
        Action          = 'geen'
        ExitCode        = 0
        Messages        = New-Object System.Collections.Generic.List[string]
        CandidateCount  = 0
        CandidateBytes  = [long]0
        DeletedCount    = 0
        DeletedBytes    = [long]0
        SkippedCount    = 0
        TooYoungCount   = 0
        ErrorCount      = 0
        Folders         = New-Object System.Collections.Generic.List[object]
        RecycleBin      = $null
    }

    $read = Read-PcConfig $ConfigPath
    if ($null -eq $read.Config) {
        $result.ExitCode = 1
        $result.Action = 'configuratiefout'
        $result.ErrorCount = 1
        foreach ($e in $read.Errors) { $result.Messages.Add($e) }
        $result.Messages.Add('Er is niets verwijderd.')
        return $result
    }
    $cfg = $read.Config

    $doDelete = $false
    if ($Mode -eq 'CleanNow') {
        if (-not $ConfirmedByUser) {
            $result.ExitCode = 1
            $result.Action = 'geweigerd'
            $result.ErrorCount = 1
            $result.Messages.Add('Handmatig opruimen vereist bevestiging. Er is niets verwijderd.')
            return $result
        }
        $doDelete = $true
    } elseif ($Mode -eq 'Scheduled') {
        if ($cfg.Paused) {
            $result.Action = 'gepauzeerd'
            $result.Messages.Add('Automatische opschoning is gepauzeerd.')
            return $result
        }
        $doDelete = (-not $cfg.SimulationMode) -and $cfg.AutomaticCleanupConfirmed
        if (-not $doDelete) { $result.Messages.Add('Simulatiemodus: er wordt niets verwijderd.') }
    }
    if ($doDelete) { $result.Action = 'opgeschoond' } else { $result.Action = 'simulatie' }

    $syncRoots = Get-PcSyncRoots
    $work = New-Object System.Collections.Generic.List[object]

    foreach ($folder in $cfg.Folders) {
        $fr = [pscustomobject]@{
            Id              = $folder.Id
            Status          = 'OK'
            Message         = ''
            CandidateCount  = 0
            CandidateBytes  = [long]0
            DeletedCount    = 0
            DeletedBytes    = [long]0
            TooYoungCount   = 0
            SkippedCount    = 0
            SkippedByReason = @{}
            ErrorCount      = 0
            Candidates      = @()
            Skipped         = @()
        }
        $result.Folders.Add($fr)
        if (-not $folder.Enabled) { $fr.Status = 'Uitgeschakeld'; continue }

        if ($folder.Type -eq 'knownFolder') { $path = Resolve-PcKnownFolder $folder.KnownFolder } else { $path = $folder.Path }
        if ([string]::IsNullOrWhiteSpace($path)) {
            $fr.Status = 'Fout'; $fr.Message = 'Locatie van de map kon niet worden bepaald.'; $fr.ErrorCount++; continue
        }
        if (-not (Test-PcPathEquals $path $folder.ExpectedPath)) {
            $fr.Status = 'Fout'
            $fr.Message = 'De locatie van deze map is gewijzigd sinds de instellingen zijn opgeslagen. Open PraktijkClean en sla de instellingen opnieuw op.'
            $fr.ErrorCount++
            continue
        }
        $safety = Test-PcFolderSafety -Path $path -AllowNetworkOrSync $folder.AllowNetworkOrSync -InstallDir $InstallDir -SyncRoots $syncRoots
        if (-not $safety.Ok) {
            $fr.Status = 'Geblokkeerd'; $fr.Message = $safety.Reason; $fr.ErrorCount++; continue
        }

        $scan = Get-PcFolderScan -Folder $folder -RootPath $safety.FullPath -NowUtc $NowUtc -MinimumAgeMinutes $cfg.MinimumAgeMinutes
        $fr.CandidateCount = $scan.Candidates.Count
        foreach ($c in $scan.Candidates) { $fr.CandidateBytes += $c.Size }
        $fr.TooYoungCount = $scan.TooYoungCount
        $fr.SkippedCount = $scan.Skipped.Count
        $fr.SkippedByReason = $scan.SkippedByReason
        $fr.ErrorCount += $scan.ErrorCount
        if ($scan.ErrorCount -gt 0) { $fr.Message = 'Een of meer (sub)mappen konden niet worden gelezen.' }
        if ($scan.Truncated) { $fr.Status = 'Fout'; $fr.Message = 'Map bevat te veel items; doorzoeken afgebroken.'; $fr.ErrorCount++ }
        if ($IncludeFileNames) {
            $fr.Candidates = @($scan.Candidates | ForEach-Object { [pscustomobject]@{ Name = $_.RelativePath; Size = $_.Size } })
            $fr.Skipped = @($scan.Skipped | ForEach-Object { [pscustomobject]@{ Name = $_.RelativePath; Reason = $_.Reason } })
        }
        $work.Add([pscustomobject]@{ Folder = $folder; Root = $safety.FullPath; Scan = $scan; Report = $fr })
    }

    foreach ($fr in $result.Folders) {
        $result.CandidateCount += $fr.CandidateCount
        $result.CandidateBytes += $fr.CandidateBytes
        $result.TooYoungCount += $fr.TooYoungCount
        $result.SkippedCount += $fr.SkippedCount
        $result.ErrorCount += $fr.ErrorCount
    }

    # Prullenbak: alleen bekijken als de optie aan staat. Totalen hierboven gaan alleen over de mappen.
    $binScan = $null
    if ($cfg.EmptyRecycleBin) {
        $bin = [pscustomobject]@{
            RetentionDays  = $cfg.RecycleBinRetentionDays
            Status         = 'OK'
            Message        = ''
            CandidateCount = 0
            CandidateBytes = [long]0
            DeletedCount   = 0
            DeletedBytes   = [long]0
            TooYoungCount  = 0
            ErrorCount     = 0
            Candidates     = @()
        }
        $result.RecycleBin = $bin
        $binScan = Get-PcRecycleBinScan -Roots (Get-PcRecycleBinRoots) -RetentionDays $cfg.RecycleBinRetentionDays -NowUtc $NowUtc -MinimumAgeMinutes $cfg.MinimumAgeMinutes
        $bin.CandidateCount = $binScan.Items.Count
        foreach ($i in $binScan.Items) { $bin.CandidateBytes += $i.Size }
        $bin.TooYoungCount = $binScan.TooYoungCount
        $bin.ErrorCount = $binScan.ErrorCount
        if ($binScan.ErrorCount -gt 0) { $bin.Status = 'Fout'; $bin.Message = 'De Prullenbak kon (deels) niet worden gelezen.' }
        $result.ErrorCount += $binScan.ErrorCount
        if ($IncludeFileNames) {
            $bin.Candidates = @($binScan.Items | ForEach-Object { [pscustomobject]@{ Name = $_.OriginalPath; Size = $_.Size } })
        }
    }

    if ($doDelete) {
        if ($result.CandidateCount -gt $cfg.MaxFilesPerRun) {
            $result.ExitCode = 3
            $result.Action = 'limiet overschreden'
            $result.ErrorCount++
            $result.Messages.Add("Er komen $($result.CandidateCount) bestanden in aanmerking, meer dan de ingestelde limiet van $($cfg.MaxFilesPerRun). Uit voorzorg is er NIETS verwijderd. Controleer dit met een simulatie.")
            return $result
        }
        foreach ($w in $work) {
            foreach ($c in $w.Scan.Candidates) {
                $why = Remove-PcFile -FullPath $c.FullPath -RootPath $w.Root -Folder $w.Folder -DeleteMode $cfg.DeleteMode -NowUtc $NowUtc -MinimumAgeMinutes $cfg.MinimumAgeMinutes
                if ($why -eq '') {
                    $w.Report.DeletedCount++
                    $w.Report.DeletedBytes += $c.Size
                } else {
                    $w.Report.SkippedCount++
                    $result.SkippedCount++
                    if ($w.Report.SkippedByReason.ContainsKey($why)) { $w.Report.SkippedByReason[$why]++ } else { $w.Report.SkippedByReason[$why] = 1 }
                    if ($why.StartsWith('Fout') -or $why -eq 'Verwijderen mislukt') { $w.Report.ErrorCount++; $result.ErrorCount++ }
                }
            }
            $result.DeletedCount += $w.Report.DeletedCount
            $result.DeletedBytes += $w.Report.DeletedBytes
        }
        if ($null -ne $binScan) {
            $bin = $result.RecycleBin
            foreach ($i in $binScan.Items) {
                $why = Remove-PcRecycleItem $i
                if ($why -eq '') {
                    $bin.DeletedCount++
                    $bin.DeletedBytes += $i.Size
                } elseif ($why -ne 'Bestaat niet meer') {
                    $bin.ErrorCount++
                    $result.ErrorCount++
                    $bin.Message = 'Een of meer items in de Prullenbak konden niet worden verwijderd.'
                }
            }
        }
    }

    if ($result.ErrorCount -gt 0 -and $result.ExitCode -eq 0) { $result.ExitCode = 2 }
    return $result
}

function Write-PcLog {
    # Schrijft een regel naar het logboek, zonder bestandsnamen. Rouleert boven 512 kB.
    param([string]$LogDir, [string]$Line)
    try {
        if (-not [System.IO.Directory]::Exists($LogDir)) { [void][System.IO.Directory]::CreateDirectory($LogDir) }
        $log = Join-Path $LogDir 'praktijkclean.log'
        if ([System.IO.File]::Exists($log) -and (New-Object System.IO.FileInfo($log)).Length -gt 524288) {
            $old = Join-Path $LogDir 'praktijkclean.1.log'
            if ([System.IO.File]::Exists($old)) { [System.IO.File]::Delete($old) }
            [System.IO.File]::Move($log, $old)
        }
        $stamp = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
        [System.IO.File]::AppendAllText($log, "$stamp $Line`r`n", (New-Object System.Text.UTF8Encoding($false)))
    } catch { }
}

function ConvertTo-PcAsciiJson {
    # JSON met alle niet-ASCII-tekens als \uXXXX, zodat de uitvoer onafhankelijk is van de console-codering.
    param($Object)
    $jsonText = $Object | ConvertTo-Json -Depth 6 -Compress
    $evaluator = [System.Text.RegularExpressions.MatchEvaluator] { param($m) '\u' + ([int][char]$m.Value).ToString('x4') }
    return [System.Text.RegularExpressions.Regex]::Replace($jsonText, '[^\x00-\x7F]', $evaluator)
}

function Get-PcStatusObject {
    # Status zonder bestandsnamen, voor status.json en de beheerapp.
    param($Result, [string]$Trigger, [datetime]$NowUtc)
    $folders = @()
    foreach ($f in $Result.Folders) {
        $reasons = @{}
        foreach ($k in $f.SkippedByReason.Keys) { $reasons[$k] = $f.SkippedByReason[$k] }
        $folders += [pscustomobject]@{
            id = $f.Id; status = $f.Status; message = $f.Message
            candidates = $f.CandidateCount; candidateBytes = $f.CandidateBytes
            deleted = $f.DeletedCount; deletedBytes = $f.DeletedBytes
            tooYoung = $f.TooYoungCount; skipped = $f.SkippedCount; skippedByReason = $reasons; errors = $f.ErrorCount
        }
    }
    $recycleBin = $null
    $b = $Result.RecycleBin
    if ($null -ne $b) {
        $recycleBin = [pscustomobject]@{
            retentionDays = $b.RetentionDays; status = $b.Status; message = $b.Message
            candidates = $b.CandidateCount; candidateBytes = $b.CandidateBytes
            deleted = $b.DeletedCount; deletedBytes = $b.DeletedBytes; tooYoung = $b.TooYoungCount; errors = $b.ErrorCount
        }
    }
    return [pscustomobject]@{
        scriptVersion = $Result.Version
        lastRunUtc = $NowUtc.ToString('o')
        mode = $Result.Mode
        trigger = $Trigger
        action = $Result.Action
        exitCode = $Result.ExitCode
        candidates = $Result.CandidateCount
        candidateBytes = $Result.CandidateBytes
        deleted = $Result.DeletedCount
        deletedBytes = $Result.DeletedBytes
        skipped = $Result.SkippedCount
        tooYoung = $Result.TooYoungCount
        errors = $Result.ErrorCount
        messages = @($Result.Messages)
        folders = $folders
        recycleBin = $recycleBin
    }
}

function Write-PcStatus {
    param([string]$Path, $Status)
    try {
        $tmp = $Path + '.tmp'
        [System.IO.File]::WriteAllText($tmp, (ConvertTo-PcAsciiJson $Status), (New-Object System.Text.UTF8Encoding($false)))
        if ([System.IO.File]::Exists($Path)) { [System.IO.File]::Delete($Path) }
        [System.IO.File]::Move($tmp, $Path)
    } catch { }
}

function Invoke-PcMain {
    param(
        [string]$ConfigPath, [string]$Mode, [string]$Trigger,
        [bool]$ConfirmedByUser, [bool]$ShowFileNames, [bool]$Json
    )
    $installDir = $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($ConfigPath)) { $ConfigPath = Join-Path $installDir 'config.json' }
    $dataDir = Split-Path -Parent $ConfigPath
    if ([string]::IsNullOrWhiteSpace($Trigger)) { $Trigger = $Mode }
    # Bestandsnamen alleen bij handmatige simulatie, en alleen naar stdout.
    $includeNames = $ShowFileNames -and $Json -and ($Mode -eq 'Simulate')

    $mutex = $null
    $owns = $false
    try {
        try {
            $mutex = New-Object System.Threading.Mutex($false, 'Local\PraktijkClean-Cleanup')
            $owns = $mutex.WaitOne(0)
        } catch {
            $inner = $_.Exception
            while ($null -ne $inner.InnerException) { $inner = $inner.InnerException }
            if ($inner -is [System.Threading.AbandonedMutexException]) {
                $owns = $true   # Vorige uitvoering is afgebroken; wij hebben de mutex nu.
            } else {
                # Mutex niet beschikbaar (bijv. testomgeving): zonder mutex doorgaan.
                if ($null -ne $mutex) { $mutex.Dispose() }
                $mutex = $null
                $owns = $true
            }
        }
        if (-not $owns) {
            if ($Json) { $script:PcJsonOutput = ConvertTo-PcAsciiJson ([pscustomobject]@{ exitCode = 4; action = 'al bezig'; messages = @('Er draait al een opschoning.') }) }
            return 4
        }

        $now = [DateTime]::UtcNow
        $result = Invoke-PcCleanup -ConfigPath $ConfigPath -Mode $Mode -ConfirmedByUser $ConfirmedByUser -IncludeFileNames $includeNames -NowUtc $now -InstallDir $installDir
        $status = Get-PcStatusObject -Result $result -Trigger $Trigger -NowUtc $now

        # Vaste opbouw: de beheerapp leest de aantallen hieruit voor het overzicht van de afgelopen dagen.
        $mb = [math]::Round($result.CandidateBytes / 1MB, 1).ToString([System.Globalization.CultureInfo]::InvariantCulture)
        $delMb = [math]::Round($result.DeletedBytes / 1MB, 1).ToString([System.Globalization.CultureInfo]::InvariantCulture)
        $line = "[$Trigger] $($result.Action): $($result.CandidateCount) in aanmerking ($mb MB), $($result.DeletedCount) verwijderd ($delMb MB), $($result.SkippedCount) overgeslagen, $($result.TooYoungCount) te recent, $($result.ErrorCount) fouten."
        foreach ($m in $result.Messages) { $line += " | $m" }
        foreach ($f in $result.Folders) { if ($f.Message -ne '') { $line += " | map '$($f.Id)': $($f.Message)" } }
        if ($null -ne $result.RecycleBin) {
            $b = $result.RecycleBin
            $binMb = [math]::Round($b.CandidateBytes / 1MB, 1).ToString([System.Globalization.CultureInfo]::InvariantCulture)
            $binDelMb = [math]::Round($b.DeletedBytes / 1MB, 1).ToString([System.Globalization.CultureInfo]::InvariantCulture)
            $line += " | Prullenbak: $($b.CandidateCount) in aanmerking ($binMb MB), $($b.DeletedCount) geleegd ($binDelMb MB), $($b.TooYoungCount) te recent, $($b.ErrorCount) fouten."
            if ($b.Message -ne '') { $line += " $($b.Message)" }
        }
        Write-PcLog -LogDir (Join-Path $dataDir 'logs') -Line $line
        Write-PcStatus -Path (Join-Path $dataDir 'status.json') -Status $status

        if ($Json) {
            $out = $status
            if ($includeNames) {
                $names = @()
                foreach ($f in $result.Folders) {
                    $names += [pscustomobject]@{ id = $f.Id; candidates = $f.Candidates; skipped = $f.Skipped }
                }
                $out | Add-Member -NotePropertyName fileDetails -NotePropertyValue $names
                if ($null -ne $result.RecycleBin) {
                    $out | Add-Member -NotePropertyName recycleBinDetails -NotePropertyValue @($result.RecycleBin.Candidates)
                }
            }
            $script:PcJsonOutput = ConvertTo-PcAsciiJson $out
        }
        return $result.ExitCode
    } finally {
        if ($null -ne $mutex) {
            if ($owns) { try { $mutex.ReleaseMutex() } catch { } }
            $mutex.Dispose()
        }
    }
}

# Alleen uitvoeren als het script direct wordt gestart (niet bij dot-sourcen door de tests).
if ($MyInvocation.InvocationName -ne '.') {
    $script:PcJsonOutput = $null
    $code = Invoke-PcMain -ConfigPath $ConfigPath -Mode $Mode -Trigger $Trigger -ConfirmedByUser $ConfirmedByUser.IsPresent -ShowFileNames $ShowFileNames.IsPresent -Json $Json.IsPresent
    if ($null -ne $script:PcJsonOutput) { Write-Output $script:PcJsonOutput }
    exit $code
}
