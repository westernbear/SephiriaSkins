param([string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Sephiria')
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $workspace
try {
    $releaseProperties = [xml](Get-Content -Raw -LiteralPath 'Directory.Build.props')
    $releaseVersion = @($releaseProperties.Project.PropertyGroup.Version | Where-Object { $_ })[0]
    if ($releaseVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid release version in Directory.Build.props.' }
    $builtinPacks = @(Get-ChildItem -LiteralPath 'packs' -Directory | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'skin.json') } | Sort-Object Name)
    if (!$builtinPacks.Count) { throw 'No built-in pack folders found.' }
    dotnet build -c Release "-p:GamePath=$GamePath"
    if ($LASTEXITCODE) { throw 'Build failed.' }
    dotnet test tests/SephiriaSkins.Tests -c Release --no-build
    if ($LASTEXITCODE) { throw 'Tests failed.' }
    foreach ($packRoot in @('packs','examples')) {
        dotnet run --project tools/PackTool -c Release --no-build -- validate-all catalog/catalog-1.0.33.json $packRoot
        if ($LASTEXITCODE) { throw "Invalid pack collection: $packRoot" }
    }
    $buildId = Get-Date -Format 'yyyyMMdd-HHmmss'
    $stage = Join-Path $workspace "dist/stage-$buildId"
    $plugin = Join-Path $stage 'BepInEx/plugins/SephiriaSkins'
    New-Item -ItemType Directory -Path "$plugin/Skins","$stage/docs","$stage/examples","$stage/catalog","$stage/licenses","$stage/assets" -Force | Out-Null
    $output = Join-Path $workspace 'src/SephiriaSkins.Plugin/bin/Release/netstandard2.1'
    foreach ($name in @('SephiriaSkins.Plugin.dll','SephiriaSkins.Core.dll','Newtonsoft.Json.dll')) {
        Copy-Item -LiteralPath (Join-Path $output $name) -Destination $plugin
    }
    foreach ($builtinPack in $builtinPacks) { Copy-Item -LiteralPath $builtinPack.FullName -Destination "$plugin/Skins" -Recurse }
    Copy-Item -LiteralPath 'examples/simple-pack','examples/unity-pack' -Destination "$stage/examples" -Recurse
    Copy-Item -LiteralPath 'README.md','LICENSE','THIRD_PARTY_NOTICES.md' -Destination $stage
    Copy-Item -Path 'docs/*' -Destination "$stage/docs" -Recurse
    Copy-Item -LiteralPath 'catalog/catalog-1.0.33.json' -Destination "$stage/catalog"
    Copy-Item -Path 'licenses/*' -Destination "$stage/licenses"
    $runtimePayload = & "$PSScriptRoot/StageBepInEx.ps1" -Stage $stage
    foreach ($builtinPack in $builtinPacks) {
        $recordOutput = Join-Path "$stage/assets" $builtinPack.Name
        New-Item -ItemType Directory -Path $recordOutput | Out-Null
        foreach ($record in @('PROVENANCE.md','prompts.txt')) {
            $recordSource = Join-Path $builtinPack.FullName $record
            if (Test-Path -LiteralPath $recordSource) { Copy-Item -LiteralPath $recordSource -Destination $recordOutput }
        }
    }
    $packTool = Join-Path $stage 'tools/PackTool'
    dotnet publish tools/PackTool -c Release -r win-x64 --self-contained false -o $packTool
    if ($LASTEXITCODE) { throw 'PackTool publish failed.' }
    & "$packTool/PackTool.exe" validate-all "$stage/catalog/catalog-1.0.33.json" "$plugin/Skins"
    if ($LASTEXITCODE) { throw 'Published PackTool validation failed.' }
    $skinZips = @()
    foreach ($builtinPack in $builtinPacks) {
        $manifest = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $builtinPack.FullName 'skin.json') | ConvertFrom-Json
        $skinZip = Join-Path $workspace "dist/SephiriaSkin-$($manifest.id)-$($manifest.version)-$buildId.zip"
        & "$packTool/PackTool.exe" zip "$stage/catalog/catalog-1.0.33.json" $builtinPack.FullName $skinZip
        if ($LASTEXITCODE) { throw "Skin ZIP creation failed: $($manifest.id)" }
        & "$packTool/PackTool.exe" validate "$stage/catalog/catalog-1.0.33.json" $skinZip
        if ($LASTEXITCODE) { throw "Skin ZIP validation failed: $($manifest.id)" }
        $skinZips += $skinZip
    }
    # Only project DLLs and the pinned official runtime may enter the installer.
    $allowed = @(
        'BepInEx/plugins/SephiriaSkins/SephiriaSkins.Plugin.dll',
        'BepInEx/plugins/SephiriaSkins/SephiriaSkins.Core.dll',
        'BepInEx/plugins/SephiriaSkins/Newtonsoft.Json.dll',
        'tools/PackTool/PackTool.dll','tools/PackTool/SephiriaSkins.Core.dll','tools/PackTool/Newtonsoft.Json.dll'
    )
    $allowed += $runtimePayload.Dlls
    foreach ($dll in Get-ChildItem -LiteralPath $stage -Filter '*.dll' -Recurse) {
        $relative = $dll.FullName.Substring($stage.Length + 1).Replace('\','/')
        if ($relative -notin $allowed) { throw "Unexpected DLL in release: $relative" }
    }
    $zip = Join-Path $workspace "dist/SephiriaSkins-$releaseVersion-$buildId.zip"
    $installerArchive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($installerFile in Get-ChildItem -LiteralPath $stage -Recurse -File -Force | Sort-Object FullName) {
            $installerRelative = $installerFile.FullName.Substring($stage.Length + 1).Replace('\','/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($installerArchive, $installerFile.FullName, $installerRelative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally { $installerArchive.Dispose() }
    & "$PSScriptRoot/TestInstaller.ps1" -Installer $zip
    # Source archive follows the checked-in ignore rules; never include local game data.
    $sourceFiles = @(rg --files --hidden --no-require-git -g '!.git/**' -g '!dist/**')
    if ($LASTEXITCODE) { throw 'Source inventory failed (rg required).' }
    $sourceZip = Join-Path $workspace "dist/SephiriaSkins-$releaseVersion-source-$buildId.zip"
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::Open($sourceZip, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($sourceFile in $sourceFiles | Sort-Object) {
            $relative = $sourceFile.Replace('\','/')
            if ($relative -match '(^|/)(\.local|\.tools|bin|obj|Library|Temp|Logs|UserSettings)(/|$)' -or [IO.Path]::GetExtension($relative) -eq '.dll') {
                throw "Ignored/binary file reached source inventory: $relative"
            }
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $workspace $sourceFile), $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally { $archive.Dispose() }
    $hashes = @($zip,$sourceZip) + $skinZips | ForEach-Object { $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256; "$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_))" }
    $hashFile = Join-Path $workspace "dist/SHA256SUMS-$buildId.txt"
    [IO.File]::WriteAllLines($hashFile, $hashes, [Text.UTF8Encoding]::new($false))
    Write-Output $zip
    Write-Output $sourceZip
    Write-Output $skinZips
    Write-Output $hashFile
}
finally { Pop-Location }
