param([string]$Editor = 'C:\Program Files\Unity\Hub\Editor\6000.3.21f1\Editor\Unity.exe')
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $workspace
try {
    dotnet build src/SephiriaSkins.Core -c Release
    if ($LASTEXITCODE) { throw 'Core build failed.' }
    New-Item -ItemType Directory -Path 'unity/Assets/Editor/Lib','.local' -Force | Out-Null
    Copy-Item -LiteralPath 'src/SephiriaSkins.Core/bin/Release/netstandard2.1/SephiriaSkins.Core.dll' -Destination 'unity/Assets/Editor/Lib'
    $project = Join-Path $workspace 'unity'
    $log = Join-Path $workspace '.local/unity-example-build.log'
    $process = Start-Process -FilePath $Editor -ArgumentList '-batchmode','-nographics','-projectPath',('"' + $project + '"'),'-executeMethod','SkinExporter.BuildExample','-quit','-logFile',('"' + $log + '"') -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    if ($process.ExitCode) { throw "Unity build failed. See $log" }
    $tmp = Get-ChildItem -LiteralPath 'unity/Library/PackageCache' -Filter 'TMP Essential Resources.unitypackage' -Recurse | Select-Object -First 1
    if (!(Test-Path -LiteralPath 'unity/Assets/TextMesh Pro/Resources/TMP Settings.asset')) {
        if (!$tmp) { throw 'TMP package not resolved. Import TMP Essential Resources in the editor.' }
        python tools/import_tmp.py $tmp.FullName $project
        if ($LASTEXITCODE) { throw 'TMP import failed.' }
        $retry = Start-Process -FilePath $Editor -ArgumentList '-batchmode','-nographics','-projectPath',('"' + $project + '"'),'-executeMethod','SkinExporter.BuildExample','-quit','-logFile',('"' + $log + '"') -WindowStyle Hidden -PassThru
        $retry.WaitForExit()
        if ($retry.ExitCode) { throw "Unity build failed. See $log" }
    }
    if (!(Select-String -LiteralPath $log -Pattern 'SEPHIRIA_SKIN_EXPORT_OK' -Quiet)) { throw "No successful export marker in $log" }
    if (Select-String -LiteralPath $log -Pattern 'is not supported because the module' -Quiet) { throw "Required Unity module disabled. See $log" }
    dotnet run --project tools/PackTool -c Release -- validate catalog/catalog-1.0.33.json examples/unity-pack
    if ($LASTEXITCODE) { throw 'Exported pack validation failed.' }
}
finally { Pop-Location }
