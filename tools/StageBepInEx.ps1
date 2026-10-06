param([Parameter(Mandatory=$true)][string]$Stage)
$ErrorActionPreference = 'Stop'
$runtimeWorkspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runtimeStage = [IO.Path]::GetFullPath($Stage)
$runtimeDist = [IO.Path]::GetFullPath((Join-Path $runtimeWorkspace 'dist')) + [IO.Path]::DirectorySeparatorChar
if (!$runtimeStage.StartsWith($runtimeDist, [StringComparison]::OrdinalIgnoreCase)) { throw 'Runtime staging must stay inside workspace/dist.' }
$runtimeLock = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $PSScriptRoot 'bepinex-runtime.lock.json') | ConvertFrom-Json
$runtimeCache = Join-Path $runtimeWorkspace '.local/runtime-cache'
New-Item -ItemType Directory -Path $runtimeCache -Force | Out-Null
function Get-PinnedRuntimeFile([string]$Url, [string]$Sha256, [string]$Name) {
    $cached = Join-Path $runtimeCache $Name
    if (!(Test-Path -LiteralPath $cached)) {
        $temporary = Join-Path $runtimeCache ($Name + '.' + [Guid]::NewGuid().ToString('N') + '.download')
        Invoke-WebRequest -Uri $Url -OutFile $temporary -UseBasicParsing
        if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash.ToLowerInvariant() -ne $Sha256) { throw "Official download checksum mismatch: $Name" }
        Move-Item -LiteralPath $temporary -Destination $cached
    }
    if ((Get-FileHash -LiteralPath $cached -Algorithm SHA256).Hash.ToLowerInvariant() -ne $Sha256) { throw "Cached runtime checksum mismatch: $Name" }
    return $cached
}
$runtimeZip = Get-PinnedRuntimeFile $runtimeLock.url $runtimeLock.sha256 "BepInEx_win_x64_$($runtimeLock.version).zip"
$runtimeSource = Get-PinnedRuntimeFile $runtimeLock.doorstopSource.url $runtimeLock.doorstopSource.sha256 $runtimeLock.doorstopSource.file
Add-Type -AssemblyName System.IO.Compression.FileSystem
$runtimeArchive = [IO.Compression.ZipFile]::OpenRead($runtimeZip)
$runtimeDlls = @()
try {
    $runtimeFiles = @($runtimeLock.files.PSObject.Properties)
    if ($runtimeArchive.Entries.Count -ne $runtimeFiles.Count) { throw 'Unexpected files in pinned BepInEx archive.' }
    foreach ($runtimeFile in $runtimeFiles) {
        $runtimeEntry = $runtimeArchive.GetEntry($runtimeFile.Name)
        if (!$runtimeEntry) { throw "Missing runtime file: $($runtimeFile.Name)" }
        $runtimeRelative = if ($runtimeFile.Name -eq 'changelog.txt') { 'docs/BepInEx-CHANGELOG.txt' } else { $runtimeFile.Name }
        $runtimeTarget = [IO.Path]::GetFullPath((Join-Path $runtimeStage $runtimeRelative))
        if (!$runtimeTarget.StartsWith($runtimeStage + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Runtime entry escapes stage.' }
        if (Test-Path -LiteralPath $runtimeTarget) { throw "Runtime staging would overwrite an existing file: $runtimeRelative" }
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($runtimeTarget)) -Force | Out-Null
        $runtimeInput = $runtimeEntry.Open()
        $runtimeOutput = [IO.File]::Open($runtimeTarget, [IO.FileMode]::CreateNew)
        try { $runtimeInput.CopyTo($runtimeOutput) } finally { $runtimeInput.Dispose(); $runtimeOutput.Dispose() }
        if ((Get-FileHash -LiteralPath $runtimeTarget -Algorithm SHA256).Hash.ToLowerInvariant() -ne $runtimeFile.Value) { throw "Runtime file checksum mismatch: $runtimeRelative" }
        if ([IO.Path]::GetExtension($runtimeRelative) -eq '.dll') { $runtimeDlls += $runtimeRelative }
    }
}
finally { $runtimeArchive.Dispose() }
New-Item -ItemType Directory -Path (Join-Path $runtimeStage 'third-party') -Force | Out-Null
Copy-Item -LiteralPath $runtimeSource -Destination (Join-Path $runtimeStage ('third-party/' + $runtimeLock.doorstopSource.file))
foreach ($runtimeLicense in $runtimeLock.licenses) {
    if (!(Test-Path -LiteralPath (Join-Path $runtimeStage ('licenses/' + $runtimeLicense)))) { throw "Required runtime license missing: $runtimeLicense" }
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bepinex-runtime.lock.json') -Destination (Join-Path $runtimeStage 'third-party/bepinex-runtime.lock.json')
return [pscustomobject]@{ Version=$runtimeLock.version; ArchiveSha256=$runtimeLock.sha256; Dlls=$runtimeDlls }
