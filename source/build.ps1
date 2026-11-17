param([string]$Destination = (Join-Path $PSScriptRoot '..\app'))
$ErrorActionPreference = 'Stop'
dotnet publish (Join-Path $PSScriptRoot 'VitaPad.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o $Destination
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
$report = Join-Path $Destination 'self-test.txt'
$process = Start-Process -FilePath (Join-Path $Destination 'VitaPad.exe') -ArgumentList @('--self-test', ('"' + $report + '"')) -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Self-tests failed; see $report" }
Get-Content $report
