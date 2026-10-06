param(
    [string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Sephiria',
    [string[]]$PackIds = @(),
    [ValidateRange(0,180)][int]$SoakMinutes = 0,
    [switch]$SkipOutput,
    [switch]$SkipCycle,
    [switch]$SkipPlay,
    [switch]$SkipPersisted
)
$ErrorActionPreference = 'Stop'
$gameTestWorkspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$gameTestRoot = [IO.Path]::GetFullPath($GamePath)
$gameTestPlugin = Join-Path $gameTestRoot 'BepInEx/plugins/SephiriaSkins'
$gameTestExe = Join-Path $gameTestRoot 'Sephiria.exe'
$gameTestConfig = Join-Path $gameTestRoot 'BepInEx/config/dev.sephiria.skins.cfg'
$gameTestSaveRoot = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Saved Games/Sephiria'
if (!(Test-Path -LiteralPath $gameTestExe) -or !(Test-Path -LiteralPath "$gameTestPlugin/SephiriaSkins.Plugin.dll")) { throw 'Install the built plugin and packs before running game diagnostics.' }
if (Get-Process Sephiria -ErrorAction SilentlyContinue) { throw 'Close the existing game before starting diagnostics.' }
if (!(Get-Process steam -ErrorAction SilentlyContinue)) { throw 'Start Steam and sign in before running diagnostics.' }
$gameTestManifests = @(Get-ChildItem -LiteralPath (Join-Path $gameTestWorkspace 'packs') -Directory | ForEach-Object {
    $manifestPath = Join-Path $_.FullName 'skin.json'
    if (Test-Path -LiteralPath $manifestPath) { Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json }
})
if (!$PackIds.Count) { $PackIds = @($gameTestManifests.id | Sort-Object) }
if (!$PackIds.Count -or @($PackIds | Select-Object -Unique).Count -ne $PackIds.Count -or @($PackIds | Where-Object { $_ -notmatch '^[a-z0-9][a-z0-9._-]{2,63}$' -or $_ -notin $gameTestManifests.id }).Count) { throw 'Choose unique built-in pack IDs.' }
$gameTestInstalledIds = @(Get-ChildItem -LiteralPath "$gameTestPlugin/Skins" -Directory | ForEach-Object {
    $path = Join-Path $_.FullName 'skin.json'
    if (Test-Path -LiteralPath $path) { (Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json).id }
})
if (@($PackIds | Where-Object { $_ -notin $gameTestInstalledIds }).Count) { throw 'A requested pack is not installed as a folder.' }
foreach ($pack in $PackIds) {
    $source = @(Get-ChildItem -LiteralPath (Join-Path $gameTestWorkspace 'packs') -Directory | Where-Object {
        $manifest = Join-Path $_.FullName 'skin.json'
        (Test-Path -LiteralPath $manifest) -and (Get-Content -LiteralPath $manifest -Raw -Encoding UTF8 | ConvertFrom-Json).id -eq $pack
    })[0]
    $installed = @(Get-ChildItem -LiteralPath "$gameTestPlugin/Skins" -Directory | Where-Object {
        $manifest = Join-Path $_.FullName 'skin.json'
        (Test-Path -LiteralPath $manifest) -and (Get-Content -LiteralPath $manifest -Raw -Encoding UTF8 | ConvertFrom-Json).id -eq $pack
    })[0]
    $manifest = Get-Content -LiteralPath (Join-Path $source.FullName 'skin.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($file in @('skin.json') + @($manifest.resources.PSObject.Properties.Value.file | Sort-Object -Unique)) {
        if ((Get-FileHash -LiteralPath (Join-Path $source.FullName $file) -Algorithm SHA256).Hash -ne
            (Get-FileHash -LiteralPath (Join-Path $installed.FullName $file) -Algorithm SHA256).Hash) { throw "Installed pack differs from authored source: $pack/$file" }
    }
}
$gameTestEvidence = Join-Path $gameTestWorkspace ('.local/game-suite-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $gameTestEvidence | Out-Null
$gameTestConfigBytes = if (Test-Path -LiteralPath $gameTestConfig) { [IO.File]::ReadAllBytes($gameTestConfig) } else { $null }
function Get-GameTestSaveHashes {
    $hashes = @{}
    if (Test-Path -LiteralPath $gameTestSaveRoot) {
        foreach ($file in Get-ChildItem -LiteralPath $gameTestSaveRoot -Recurse -File) {
            $hashes[$file.FullName.Substring($gameTestSaveRoot.Length)] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        }
    }
    return $hashes
}
$gameTestSaveHashes = Get-GameTestSaveHashes
$gameTestSaveHashes | ConvertTo-Json | Set-Content -LiteralPath "$gameTestEvidence/saves-before.json" -Encoding UTF8
function Assert-GameTestSaves {
    $after = Get-GameTestSaveHashes
    if ($after.Count -ne $gameTestSaveHashes.Count -or @($gameTestSaveHashes.Keys | Where-Object { $after[$_] -ne $gameTestSaveHashes[$_] }).Count) { throw 'Existing save files changed; retained evidence without overwriting saves.' }
}
function Invoke-GameTestPython([string[]]$Arguments) {
    & python -X utf8 @Arguments
    if ($LASTEXITCODE) { throw "Game evidence verification failed: $($Arguments[0])" }
}
function Invoke-GameTestRun([string]$Label, [string]$Pack, [string[]]$Options, [int]$MaximumMinutes = 30) {
    $export = Join-Path $gameTestPlugin ('Export/diagnostics/' + $Pack)
    $previous = @(if (Test-Path -LiteralPath $export) { Get-ChildItem -LiteralPath $export -Directory | Select-Object -ExpandProperty Name })
    $log = Join-Path $gameTestEvidence ($Label + '.log')
    $launch = $Options + @('--skins-probe-exit','-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',('"' + $log + '"'))
    $priorSteamId = $env:SteamAppId
    try { $env:SteamAppId = '2436940'; $process = Start-Process -FilePath $gameTestExe -WorkingDirectory $gameTestRoot -ArgumentList $launch -WindowStyle Hidden -PassThru }
    finally { $env:SteamAppId = $priorSteamId }
    Write-Host "Game diagnostic started: $Label (PID $($process.Id))"
    $deadline = [DateTime]::UtcNow.AddMinutes($MaximumMinutes)
    $lastStatus = [DateTime]::MinValue
    while (!$process.HasExited) {
        if ([DateTime]::UtcNow -gt $deadline) {
            foreach ($current in @(Get-ChildItem -LiteralPath $export -Directory | Where-Object Name -NotIn $previous)) {
                [IO.File]::WriteAllText((Join-Path $current.FullName 'cancel.request'), 'Diagnostic suite timeout; request orderly cleanup.')
            }
            throw "Diagnostic exceeded its bounded window; requested cleanup without terminating the game: $Label"
        }
        if (([DateTime]::UtcNow - $lastStatus).TotalSeconds -ge 30) {
            if (Test-Path -LiteralPath $log) { Write-Host "$Label : $((Get-Content -LiteralPath $log -Tail 10 | Where-Object { $_ -match 'SKINS_PLAY_PHASE|SKINS_SOAK_PROGRESS|SKINS_PROBE' } | Select-Object -Last 1))" }
            $lastStatus = [DateTime]::UtcNow
        }
        Start-Sleep -Seconds 2
        $process.Refresh()
    }
    $created = @(Get-ChildItem -LiteralPath $export -Directory | Where-Object Name -NotIn $previous)
    if ($created.Count -ne 1) { throw "Expected one new diagnostic run for $Label; got $($created.Count)." }
    $run = Get-Content -LiteralPath (Join-Path $created[0].FullName 'run.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($run.pluginSha256 -ne (Get-FileHash -LiteralPath "$gameTestPlugin/SephiriaSkins.Plugin.dll" -Algorithm SHA256).Hash.ToLowerInvariant()) { throw 'Loaded plugin differs from installed build.' }
    Assert-GameTestSaves
    [IO.File]::WriteAllText((Join-Path $gameTestEvidence ($Label + '-location.txt')), $created[0].FullName)
    return $created[0].FullName
}
Push-Location $gameTestWorkspace
try {
    if (!$SkipPlay) {
    foreach ($pack in $PackIds) {
        $arguments = @('--skins-playtest',"--skins-test-pack=$pack",'--skins-test-weapons=defaults','--skins-playtest-with-visual','--skins-playtest-with-controls','--skins-playtest-with-encounter')
        if ($pack -eq $PackIds[-1] -and $SoakMinutes) { $arguments += "--skins-soak-minutes=$SoakMinutes" }
        # Launch status belongs on the host output; keep only the returned path.
        $run = @(Invoke-GameTestRun $pack $pack $arguments (30 + $SoakMinutes))[-1]
        $play = Join-Path $run 'play-results.json'
        Invoke-GameTestPython @('tools/verify_play_results.py',$play,'--controls',$play,'--encounter',$play)
        Invoke-GameTestPython @('tools/verify_visual_results.py',(Join-Path $run 'visual/renderers.json'),$play)
    }
    }
    if (!$SkipCycle -and $PackIds.Count -ge 2) {
        $run = @(Invoke-GameTestRun 'cycle' $PackIds[0] @('--skins-playtest',"--skins-test-pack=$($PackIds[0])",'--skins-test-weapons=defaults',('--skins-cycle-packs=' + ($PackIds -join ','))))[-1]
        Invoke-GameTestPython @('tools/verify_cycle_results.py',(Join-Path $run 'play-results.json'))
    }
    if (!$SkipOutput) {
        $run = @(Invoke-GameTestRun 'output-all' 'fan.hachiware' @('--skins-probe','--skins-probe-host','--skins-probe-lobby','--skins-probe-packs','--skins-probe-zip'))[-1]
        Invoke-GameTestPython @('tools/verify_output_results.py',(Join-Path $run 'probe-results.json'),(Join-Path $run 'probe-cleanup.json'))
    }
    if (!$SkipPersisted) {
        if ($null -eq $gameTestConfigBytes) { throw 'Persisted startup requires an existing config to back up.' }
        foreach ($pack in $PackIds) {
            foreach ($enabled in @($false,$true)) {
                $text = [Text.Encoding]::UTF8.GetString($gameTestConfigBytes)
                foreach ($key in @('Selected','PixelArt','GameUi')) {
                    if (![Text.RegularExpressions.Regex]::IsMatch($text,"(?m)^$key\s*=")) { throw "Config key missing: $key" }
                }
                $text = [Text.RegularExpressions.Regex]::Replace($text,'(?m)^Selected\s*=.*$',"Selected = $pack")
                $value = $enabled.ToString().ToLowerInvariant()
                $text = [Text.RegularExpressions.Regex]::Replace($text,'(?m)^PixelArt\s*=.*$',"PixelArt = $value")
                $text = [Text.RegularExpressions.Regex]::Replace($text,'(?m)^GameUi\s*=.*$',"GameUi = $value")
                [IO.File]::WriteAllText($gameTestConfig,$text,[Text.UTF8Encoding]::new($false))
                $state = if ($enabled) { 'on' } else { 'off' }
                $run = @(Invoke-GameTestRun ("persisted-$pack-$state") $pack @('--skins-playtest','--skins-playtest-persisted',"--skins-test-pack=$pack",'--skins-test-weapons=defaults'))[-1]
                Invoke-GameTestPython @('tools/verify_persisted_results.py',(Join-Path $run 'play-results.json'),'--pixel',$state,'--ui',$state)
                [IO.File]::WriteAllBytes($gameTestConfig,$gameTestConfigBytes)
            }
        }
    }
    Write-Output "Game pack suite verified. Local evidence: $gameTestEvidence"
}
finally {
    Pop-Location
    if (!(Get-Process Sephiria -ErrorAction SilentlyContinue) -and $null -ne $gameTestConfigBytes) { [IO.File]::WriteAllBytes($gameTestConfig, $gameTestConfigBytes) }
    Assert-GameTestSaves
}
