param([Parameter(ValueFromRemainingArguments = $true)][string[]]$GameArguments)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$DotnetCommand = if ($env:DOTNET) { $env:DOTNET } else { 'dotnet' }
& $DotnetCommand build source/OpenTPW/OpenTPW.csproj --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $DotnetCommand source/OpenTPW/bin/Debug/net10.0/OpenTPW.dll @GameArguments
exit $LASTEXITCODE
