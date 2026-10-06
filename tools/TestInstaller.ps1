param([Parameter(Mandatory=$true)][string]$Installer)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$installerLock = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $PSScriptRoot 'bepinex-runtime.lock.json') | ConvertFrom-Json
$installerArchive = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($Installer))
function Get-InstallerEntryHash($Entry) {
    $entryStream = $Entry.Open()
    $entryHash = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($entryHash.ComputeHash($entryStream))).Replace('-','').ToLowerInvariant() }
    finally { $entryStream.Dispose(); $entryHash.Dispose() }
}
try {
    $installerNames = @($installerArchive.Entries.FullName)
    $installerSeen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($installerName in $installerNames) {
        if (!$installerSeen.Add($installerName)) { throw 'Duplicate installer entries.' }
        if ($installerName.Contains('\') -or $installerName.StartsWith('/') -or $installerName.Contains(':') -or
            @($installerName.Split('/') | Where-Object { $_ -eq '' -or $_ -eq '.' -or $_ -eq '..' }).Count) {
            throw "Non-relative or non-portable installer path: $installerName"
        }
    }
    foreach ($installerRuntimeFile in $installerLock.files.PSObject.Properties) {
        $relative = if ($installerRuntimeFile.Name -eq 'changelog.txt') { 'docs/BepInEx-CHANGELOG.txt' } else { $installerRuntimeFile.Name }
        $entry = $installerArchive.GetEntry($relative)
        $actual = if ($entry) { Get-InstallerEntryHash $entry } else { 'missing' }
        if ($actual -ne $installerRuntimeFile.Value) { throw "Pinned runtime file missing/changed in installer: $relative ($actual)" }
    }
    foreach ($required in @('BepInEx/plugins/SephiriaSkins/SephiriaSkins.Plugin.dll','BepInEx/plugins/SephiriaSkins/SephiriaSkins.Core.dll','BepInEx/plugins/SephiriaSkins/Newtonsoft.Json.dll','README.md','THIRD_PARTY_NOTICES.md')) {
        if (!$installerArchive.GetEntry($required)) { throw "Installer file missing: $required" }
    }
    foreach ($license in $installerLock.licenses) {
        $licenseEntry = $installerArchive.GetEntry('licenses/' + $license)
        $licenseExpected = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot ('../licenses/' + $license)) -Algorithm SHA256).Hash.ToLowerInvariant()
        if (!$licenseEntry -or (Get-InstallerEntryHash $licenseEntry) -ne $licenseExpected) { throw "Installer runtime license missing/changed: $license" }
    }
    $embeddedLock = $installerArchive.GetEntry('third-party/bepinex-runtime.lock.json')
    $expectedLockHash = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'bepinex-runtime.lock.json') -Algorithm SHA256).Hash.ToLowerInvariant()
    if (!$embeddedLock -or (Get-InstallerEntryHash $embeddedLock) -ne $expectedLockHash) { throw 'Installer runtime provenance lock missing/changed.' }
    $sourceEntry = $installerArchive.GetEntry('third-party/' + $installerLock.doorstopSource.file)
    if (!$sourceEntry -or (Get-InstallerEntryHash $sourceEntry) -ne $installerLock.doorstopSource.sha256) { throw 'Doorstop corresponding source missing/changed.' }
    $builtinFolders = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '../packs') -Directory | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'skin.json') })
    foreach ($builtin in $builtinFolders) {
        if (!$installerArchive.GetEntry('BepInEx/plugins/SephiriaSkins/Skins/' + $builtin.Name + '/skin.json')) { throw "Installer built-in pack missing: $($builtin.Name)" }
    }
    $allowedDlls = @('BepInEx/plugins/SephiriaSkins/SephiriaSkins.Plugin.dll','BepInEx/plugins/SephiriaSkins/SephiriaSkins.Core.dll','BepInEx/plugins/SephiriaSkins/Newtonsoft.Json.dll',
        'tools/PackTool/PackTool.dll','tools/PackTool/SephiriaSkins.Core.dll','tools/PackTool/Newtonsoft.Json.dll') + @($installerLock.files.PSObject.Properties.Name | Where-Object { $_.EndsWith('.dll') })
    foreach ($name in $installerNames) {
        if ($name.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase) -and $name -notin $allowedDlls) { throw "Unexpected installer DLL: $name" }
        if ($name -match '(^|/)(Sephiria_Data|MonoBleedingEdge|\.local|\.tools|bin|obj)(/|$)' -or $name -match '(^|/)(Sephiria\.exe|UnityPlayer\.dll|Assembly-CSharp\.dll)$' -or $name.StartsWith('BepInEx/config/', [StringComparison]::OrdinalIgnoreCase) -or $name.StartsWith('BepInEx/cache/', [StringComparison]::OrdinalIgnoreCase)) { throw "Game/local configuration reached installer: $name" }
    }
    Write-Output "Installer verified: BepInEx $($installerLock.version), $($builtinFolders.Count) built-in packs, root bootstrap and corresponding source."
}
finally { $installerArchive.Dispose() }
