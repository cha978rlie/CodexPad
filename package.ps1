param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference = 'Stop'
$version = '0.3.0'
$pythonVersion = '3.13.15'
$pythonHash = 'D1F04D990AEE1253D8569E8E5104E30FA9F5FA830899F14843448872D936A2CF'
$stageRoot = Join-Path $PSScriptRoot ('.build\package-' + [guid]::NewGuid().ToString('N'))
$app = Join-Path $stageRoot "CodexPad-$version-win-x64"
$source = Join-Path $stageRoot "CodexPad-$version-source"
New-Item -ItemType Directory -Path $app,$source,$OutputDirectory -Force | Out-Null
$publicBuild = Join-Path $PSScriptRoot 'publishing\build.ps1'
if (-not (Test-Path -LiteralPath $publicBuild)) { $publicBuild = Join-Path $PSScriptRoot 'build.ps1' }
# The public build script expects the repository root as PSScriptRoot. Invoke the
# SDK here too, so this script works both in development and in the clean export.
$localSdk = Join-Path $PSScriptRoot '.build\dotnet\dotnet.exe'
$sdk = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.build\cli-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.build\packages'
& $sdk publish (Join-Path $PSScriptRoot 'CodexPad.csproj') -c Release -o $app -p:NuGetAudit=false -p:DebugType=None -p:DebugSymbols=false "-p:PathMap=$PSScriptRoot=/_/CodexPad"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$helpers = @('pad_backend.py','pad_status.py','pad_hid.py','pad_led.py','pad_device.py')
$sources = @('CodexPad.cs','ProductUi.cs','ProductCore.cs','ProductTests.cs','Program.cs','PadTest.cs','ChatScroller.cs','DesignUi.cs','PadTheme.cs','CodexPad.csproj','package.ps1','.gitignore','LICENSE','THIRD-PARTY-NOTICES.md','CONTRIBUTING.md','SECURITY.md','test_pad_protocol.py','test_pad_status.py','test_pad_backend.py','CodexPad starten.cmd','LED testen.cmd','Pad testen.cmd') + $helpers
foreach ($file in $sources) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $source }
Copy-Item -LiteralPath $publicBuild -Destination (Join-Path $source 'build.ps1')
$readme = Join-Path $PSScriptRoot 'publishing\README.md'
if (-not (Test-Path -LiteralPath $readme)) { $readme = Join-Path $PSScriptRoot 'README.md' }
foreach ($target in @($source,$app)) {
    Copy-Item -LiteralPath $readme -Destination (Join-Path $target 'README.md')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets') -Destination $target -Recurse
    foreach ($file in @('LICENSE','THIRD-PARTY-NOTICES.md','CONTRIBUTING.md','SECURITY.md')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $target -Force }
    $docs = Join-Path $PSScriptRoot 'publishing\docs'
    if (-not (Test-Path -LiteralPath $docs)) { $docs = Join-Path $PSScriptRoot 'docs' }
    if (Test-Path -LiteralPath $docs) { Copy-Item -LiteralPath $docs -Destination $target -Recurse }
}
foreach ($file in ($helpers + @('CodexPad starten.cmd','LED testen.cmd','Pad testen.cmd'))) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $app }
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot '.github')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot '.github') -Destination $source -Recurse }
$downloadDir = Join-Path $PSScriptRoot '.build\downloads'
New-Item -ItemType Directory -Path $downloadDir -Force | Out-Null
$pythonZip = Join-Path $downloadDir "python-$pythonVersion-embed-amd64.zip"
if (-not (Test-Path -LiteralPath $pythonZip)) {
    Invoke-WebRequest -Uri "https://www.python.org/ftp/python/$pythonVersion/python-$pythonVersion-embed-amd64.zip" -OutFile $pythonZip
}
if ((Get-FileHash -LiteralPath $pythonZip -Algorithm SHA256).Hash -ne $pythonHash) { throw 'Python archive checksum mismatch.' }
$pythonDirectory = Join-Path $app 'runtime\python'
Expand-Archive -LiteralPath $pythonZip -DestinationPath $pythonDirectory
@('python313.zip','.','..\..') | Set-Content -LiteralPath (Join-Path $pythonDirectory 'python313._pth') -Encoding ascii
$licenses = Join-Path $app 'licenses'; New-Item -ItemType Directory -Path $licenses | Out-Null
$core = Get-ChildItem (Join-Path $env:NUGET_PACKAGES 'microsoft.netcore.app.runtime.win-x64') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$desktop = Get-ChildItem (Join-Path $env:NUGET_PACKAGES 'microsoft.windowsdesktop.app.runtime.win-x64') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
Copy-Item -LiteralPath (Join-Path $core.FullName 'LICENSE.TXT') -Destination (Join-Path $licenses 'dotnet-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $core.FullName 'THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $licenses 'dotnet-THIRD-PARTY-NOTICES.txt')
Copy-Item -LiteralPath (Join-Path $desktop.FullName 'LICENSE') -Destination (Join-Path $licenses 'windowsdesktop-LICENSE.txt')
# No unreviewed working files or personal state may enter an archive.
$forbidden = Get-ChildItem $stageRoot -Recurse -File | Where-Object { $_.Name -match '^(codexpad-config|status-config|Diktat-Diagnose|Pad-Geraetestatus|Pad-Pruefergebnis)' -or $_.Extension -in '.log','.jsonl','.pdb' }
if ($forbidden) { throw 'Personal state or debug artifacts unexpectedly present in package.' }
foreach ($tree in @($app,$source)) {
    $zip = Join-Path $OutputDirectory ((Split-Path $tree -Leaf) + '.zip')
    Compress-Archive -LiteralPath $tree -DestinationPath $zip -Force
}
Get-ChildItem $OutputDirectory -Filter "CodexPad-$version-*.zip" | Get-FileHash -Algorithm SHA256 |
    ForEach-Object { $_.Hash.ToLowerInvariant() + '  ' + (Split-Path $_.Path -Leaf) } |
    Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding ascii
Write-Output "Packages: $OutputDirectory"
Write-Output "Reviewable staging directory: $stageRoot"
