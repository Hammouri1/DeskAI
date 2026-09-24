#requires -Version 5.1
<#
.SYNOPSIS
  Runs the folder-color probe (ADR 0046) inside Windows Sandbox. Nothing runs on this PC's Desktop.
.DESCRIPTION
  Publishes the probe self-contained into artifacts/color-probe/app, writes a .wsb that maps that
  folder read-only and artifacts/color-probe/results writable, turns networking off, and starts the
  Sandbox. Keep the Sandbox window open and in front (not minimized) until results/report.txt
  appears: the probe reads the Sandbox's screen. Then close the Sandbox; it is thrown away.
#>
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$root = Join-Path $repo 'artifacts\color-probe'
$app = Join-Path $root 'app'
$results = Join-Path $root 'results'
$sandbox = Join-Path $env:SystemRoot 'System32\WindowsSandbox.exe'

if (-not (Test-Path $sandbox)) {
    throw 'Windows Sandbox is not turned on. Turn on "Windows Sandbox" in Windows Features, restart, and run this again.'
}

dotnet publish (Join-Path $PSScriptRoot 'FolderColorProbe.csproj') -c Release --self-contained -o $app
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
New-Item -ItemType Directory -Force $results | Out-Null
# Only the probe's own earlier results: its report and pictures.
Get-ChildItem $results -File | Where-Object { $_.Name -eq 'report.txt' -or $_.Extension -eq '.png' } | Remove-Item

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
    <Command>C:\Probe\FolderColorProbe.exe C:\ProbeResults</Command>
  </LogonCommand>
</Configuration>
"@
$wsbPath = Join-Path $root 'color-probe.wsb'
Set-Content -Path $wsbPath -Value $wsb -Encoding utf8
Start-Process $sandbox -ArgumentList "`"$wsbPath`""
Write-Host "Sandbox started. Keep its window open and in front for about a minute, until $results\report.txt appears. Then close the Sandbox."
