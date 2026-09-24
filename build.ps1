param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts\app'))
$ErrorActionPreference = 'Stop'
$localSdk = Join-Path $PSScriptRoot '.build\dotnet\dotnet.exe'
$sdk = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.build\cli-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.build\packages'
& $sdk publish (Join-Path $PSScriptRoot 'CodexPad.csproj') -c Release -o $OutputDirectory -p:NuGetAudit=false -p:DebugType=None -p:DebugSymbols=false "-p:PathMap=$PSScriptRoot=/_/CodexPad"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets') -Destination $OutputDirectory -Recurse -Force
Write-Output "Application built: $OutputDirectory"
