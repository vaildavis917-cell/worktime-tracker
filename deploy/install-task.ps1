<#
.SYNOPSIS
Installs WorkTimeTracker Agent as a per-user Scheduled Task on a Windows 10/11 workstation.

.DESCRIPTION
The agent must run inside the user's logon session (not Session 0 / a Windows Service)
so that screenshot capture sees the real desktop. This script:

  1. Copies the agent payload to $InstallPath (default C:\WorkTimeTracker\Agent).
  2. Optionally writes / updates appsettings.json with provided ServerUrl + AgentToken.
  3. Registers a Scheduled Task that runs at user logon at Limited (non-admin) integrity.
  4. Starts the task immediately so the agent is up without waiting for the next logon.

The task runs as the BUILTIN\Users group so any user who logs in will spawn the agent
inside their own session.

.PARAMETER SourcePath
Path to the published agent build (the "publish\Agent" directory from `dotnet publish`).

.PARAMETER InstallPath
Destination directory on the local machine. Default: C:\WorkTimeTracker\Agent.

.PARAMETER ServerUrl
Optional. If provided, overwrites the ServerUrl in appsettings.json.

.PARAMETER AgentToken
Optional. If provided, overwrites the AgentToken in appsettings.json.

.PARAMETER TaskName
Scheduled task name. Default: WorkTimeTrackerAgent.

.EXAMPLE
.\install-task.ps1 -SourcePath .\publish\Agent -ServerUrl https://worktime.contoso.local -AgentToken abc123

.NOTES
Run elevated. Idempotent — re-running upgrades the binaries and re-registers the task.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePath,

    [string]$InstallPath = "C:\WorkTimeTracker\Agent",

    [string]$ServerUrl,

    [string]$AgentToken,

    [string]$TaskName = "WorkTimeTrackerAgent"
)

$ErrorActionPreference = "Stop"

if (-not ([System.Security.Principal.WindowsPrincipal] [System.Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script elevated (Administrator). Required for ProgramData / Scheduled Tasks."
}

if (-not (Test-Path $SourcePath)) {
    throw "SourcePath not found: $SourcePath"
}

$agentExe = Join-Path $InstallPath "WorkTimeTracker.Agent.exe"

Write-Host "Copying agent payload from $SourcePath to $InstallPath ..."
New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
robocopy $SourcePath $InstallPath /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) {
    throw "robocopy failed with exit code $LASTEXITCODE"
}

$cfgPath = Join-Path $InstallPath "appsettings.json"
if (-not (Test-Path $cfgPath)) {
    throw "appsettings.json missing in destination after copy"
}
$cfg = Get-Content $cfgPath -Raw | ConvertFrom-Json
if (-not $cfg.Agent) {
    $cfg | Add-Member -NotePropertyName Agent -NotePropertyValue (New-Object psobject)
}

# AgentToken — generate if missing or still the placeholder. Server requires
# at least 16 chars; we produce 32 hex chars (128 bits) which is plenty.
if (-not $AgentToken -and ($cfg.Agent.AgentToken -in @($null, "", "REPLACE_ME_WITH_DEVICE_TOKEN"))) {
    $bytes = New-Object byte[] 16
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    $AgentToken = -join ($bytes | ForEach-Object { $_.ToString("x2") })
    Write-Host "Generated random AgentToken: $AgentToken"
    Write-Host "  -> Save this value if you plan to disable auto-register on the server."
}

if ($ServerUrl)  { $cfg.Agent | Add-Member -NotePropertyName ServerUrl  -NotePropertyValue $ServerUrl  -Force }
if ($AgentToken) {
    if ($AgentToken.Length -lt 16) {
        throw "AgentToken must be at least 16 characters long (server enforces ServerOptions.MinTokenLength)."
    }
    $cfg.Agent | Add-Member -NotePropertyName AgentToken -NotePropertyValue $AgentToken -Force
}
($cfg | ConvertTo-Json -Depth 10) | Set-Content $cfgPath -Encoding UTF8
Write-Host "appsettings.json updated."

Write-Host "Registering Scheduled Task '$TaskName' ..."

if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

$action = New-ScheduledTaskAction `
    -Execute $agentExe `
    -WorkingDirectory $InstallPath

$trigger = New-ScheduledTaskTrigger -AtLogOn

$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -MultipleInstances IgnoreNew

$principal = New-ScheduledTaskPrincipal `
    -GroupId "BUILTIN\Users" `
    -RunLevel Limited

Register-ScheduledTask `
    -TaskName $TaskName `
    -Action $action `
    -Trigger $trigger `
    -Settings $settings `
    -Principal $principal `
    -Description "WorkTimeTracker Agent — корпоративный учёт рабочего времени и активности. Установлен IT-отделом по заявке руководства." | Out-Null

Write-Host "Starting task immediately for current session ..."
try {
    Start-ScheduledTask -TaskName $TaskName
} catch {
    Write-Warning "Could not start task immediately ($_). It will start at the next user logon."
}

Write-Host "`nInstallation complete. Verify with:"
Write-Host "  Get-ScheduledTask -TaskName $TaskName"
Write-Host "  Get-Process WorkTimeTracker.Agent -ErrorAction SilentlyContinue"
