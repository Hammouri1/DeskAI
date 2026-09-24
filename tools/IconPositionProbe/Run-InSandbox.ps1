#requires -Version 5.1
<#
.SYNOPSIS
  Runs the icon-position probe (ADR 0043) inside Windows Sandbox. Nothing runs on this PC's Desktop.
.DESCRIPTION
  Publishes the probe self-contained into artifacts/icon-probe/app, writes a .wsb that maps that
  folder read-only and artifacts/icon-probe/results writable, turns networking off, and starts the
  Sandbox. The probe writes results/report.txt. Close the Sandbox afterwards; it is thrown away.
#>
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$root = Join-Path $repo 'artifacts\icon-probe'
$app = Join-Path $root 'app'
$results = Join-Path $root 'results'
$sandbox = Join-Path $env:SystemRoot 'System32\WindowsSandbox.exe'

if (-not (Test-Path $sandbox)) {
    throw 'Windows Sandbox is not turned on. Turn on "Windows Sandbox" in Windows Features, restart, and run this again.'
}

dotnet publish (Join-Path $PSScriptRoot 'IconPositionProbe.csproj') -c Release --self-contained -o $app
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
if (Test-Path (Join-Path $results 'report.txt')) { Remove-Item (Join-Path $results 'report.txt') }
New-Item -ItemType Directory -Force $results | Out-Null

$wsb = @"
<Configuration>
  <Networking>Disable</Networking>
  <ClipboardRedirection>Disable</ClipboardRedirection>
  <PrinterRedirection>Disable</PrinterRedirection>
  <MappedFolders>
    <MappedFolder>
      <HostFolder>$app</HostFolder>
      <SandboxFolder>C:\Probe</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$results</HostFolder>
      <SandboxFolder>C:\ProbeResults</SandboxFolder>
      <ReadOnly>false</ReadOnly>
    </MappedFolder>
  </MappedFolders>
  <LogonCommand>
    <Command>C:\Probe\IconPositionProbe.exe C:\ProbeResults</Command>
  </LogonCommand>
</Configuration>
"@
$wsbPath = Join-Path $root 'icon-probe.wsb'
Set-Content -Path $wsbPath -Value $wsb -Encoding utf8
Start-Process $sandbox -ArgumentList "`"$wsbPath`""
Write-Host "Sandbox started. When it finishes, read $results\report.txt, then close the Sandbox."
