param([string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Sephiria')
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $workspace
try {
    dotnet build -c Release "-p:GamePath=$GamePath"
    if ($LASTEXITCODE) { throw 'Build failed.' }
    dotnet test tests/SephiriaSkins.Tests -c Release --no-build
    if ($LASTEXITCODE) { throw 'Tests failed.' }
    foreach ($pack in @('packs/hachiware','examples/simple-pack','examples/unity-pack')) {
        dotnet run --project tools/PackTool -c Release --no-build -- validate catalog/catalog-1.0.33.json $pack
        if ($LASTEXITCODE) { throw "Invalid pack: $pack" }
    }
    $buildId = Get-Date -Format 'yyyyMMdd-HHmmss'
    $stage = Join-Path $workspace "dist/stage-$buildId"
    $plugin = Join-Path $stage 'BepInEx/plugins/SephiriaSkins'
    New-Item -ItemType Directory -Path "$plugin/Skins","$stage/docs","$stage/examples","$stage/catalog","$stage/licenses","$stage/assets/hachiware" -Force | Out-Null
    $output = Join-Path $workspace 'src/SephiriaSkins.Plugin/bin/Release/netstandard2.1'
    foreach ($name in @('SephiriaSkins.Plugin.dll','SephiriaSkins.Core.dll','Newtonsoft.Json.dll')) {
        Copy-Item -LiteralPath (Join-Path $output $name) -Destination $plugin
    }
    Copy-Item -LiteralPath 'packs/hachiware' -Destination "$plugin/Skins" -Recurse
    Copy-Item -LiteralPath 'examples/simple-pack','examples/unity-pack' -Destination "$stage/examples" -Recurse
    Copy-Item -LiteralPath 'README.md','LICENSE','THIRD_PARTY_NOTICES.md' -Destination $stage
    Copy-Item -Path 'docs/*' -Destination "$stage/docs" -Recurse
    Copy-Item -LiteralPath 'catalog/catalog-1.0.33.json' -Destination "$stage/catalog"
    Copy-Item -Path 'licenses/*' -Destination "$stage/licenses"
    Copy-Item -LiteralPath 'assets/hachiware/PROVENANCE.md','assets/hachiware/prompts.txt' -Destination "$stage/assets/hachiware"
    $packTool = Join-Path $stage 'tools/PackTool'
    dotnet publish tools/PackTool -c Release -r win-x64 --self-contained false -o $packTool
    if ($LASTEXITCODE) { throw 'PackTool publish failed.' }
    & "$packTool/PackTool.exe" validate "$stage/catalog/catalog-1.0.33.json" "$plugin/Skins/hachiware"
    if ($LASTEXITCODE) { throw 'Published PackTool validation failed.' }
    # Explicit paths prevent accidentally shipping game or BepInEx DLLs.
    $allowed = @(
        'BepInEx/plugins/SephiriaSkins/SephiriaSkins.Plugin.dll',
        'BepInEx/plugins/SephiriaSkins/SephiriaSkins.Core.dll',
        'BepInEx/plugins/SephiriaSkins/Newtonsoft.Json.dll',
        'tools/PackTool/PackTool.dll','tools/PackTool/SephiriaSkins.Core.dll','tools/PackTool/Newtonsoft.Json.dll'
    )
    foreach ($dll in Get-ChildItem -LiteralPath $stage -Filter '*.dll' -Recurse) {
        $relative = $dll.FullName.Substring($stage.Length + 1).Replace('\','/')
        if ($relative -notin $allowed) { throw "Unexpected DLL in release: $relative" }
    }
    $zip = Join-Path $workspace "dist/SephiriaSkins-0.1.1-$buildId.zip"
    Compress-Archive -Path "$stage/*" -DestinationPath $zip
    # Source archive follows the checked-in ignore rules; never include local game data.
    $sourceFiles = @(rg --files --hidden --no-require-git -g '!.git/**' -g '!dist/**')
    if ($LASTEXITCODE) { throw 'Source inventory failed (rg required).' }
    $sourceZip = Join-Path $workspace "dist/SephiriaSkins-0.1.1-source-$buildId.zip"
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
    $hashes = @($zip,$sourceZip) | ForEach-Object { $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256; "$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_))" }
    $hashFile = Join-Path $workspace "dist/SHA256SUMS-$buildId.txt"
    [IO.File]::WriteAllLines($hashFile, $hashes, [Text.UTF8Encoding]::new($false))
    Write-Output $zip
    Write-Output $sourceZip
    Write-Output $hashFile
}
finally { Pop-Location }
