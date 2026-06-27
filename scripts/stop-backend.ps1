param(
  [int[]]$Ports = @(8020),
  [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'

$currentPid = $PID
$parentPid = (Get-CimInstance Win32_Process -Filter "ProcessId = $currentPid").ParentProcessId
$backendRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$escapedBackendRoot = [regex]::Escape($backendRoot)

$backendCommandPattern = @(
  'dotnet(\.exe)?\s+run',
  'OnTrackGraphQLServer(\.dll|\.exe)?',
  'Amazon\.Lambda\.TestTool',
  'lambda-test-tool'
) -join '|'

$processesByCommand = Get-CimInstance Win32_Process |
  Where-Object {
    $_.ProcessId -ne $currentPid -and
    $_.ProcessId -ne $parentPid -and
    $_.CommandLine -and
    $_.CommandLine -match $escapedBackendRoot -and
    $_.CommandLine -match $backendCommandPattern
  }

$portProcessIds = Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
  Where-Object { $Ports -contains $_.LocalPort } |
  Select-Object -ExpandProperty OwningProcess -Unique

$processesByPort = foreach ($processId in $portProcessIds) {
  if ($processId -and $processId -ne $currentPid -and $processId -ne $parentPid) {
    Get-CimInstance Win32_Process -Filter "ProcessId = $processId" -ErrorAction SilentlyContinue
  }
}

$targets = @(@($processesByCommand) + @($processesByPort)) |
  Where-Object { $_ } |
  Sort-Object ProcessId -Unique

if (-not $targets -or $targets.Count -eq 0) {
  Write-Host "No running local backend processes found."
  exit 0
}

Write-Host "Local backend processes:"
$targets |
  Select-Object ProcessId, Name, CommandLine |
  Format-Table -AutoSize -Wrap

if ($WhatIf) {
  Write-Host "WhatIf mode: no processes were stopped."
  exit 0
}

foreach ($target in $targets) {
  try {
    Stop-Process -Id $target.ProcessId -Force -ErrorAction Stop
    Write-Host "Stopped process $($target.ProcessId)."
  } catch {
    Write-Warning "Could not stop process $($target.ProcessId): $($_.Exception.Message)"
  }
}
