<#
.SYNOPSIS
Removes the WorkTimeTracker Agent scheduled task and (optionally) deletes the install directory.

.PARAMETER InstallPath
Directory to remove. If omitted, only the scheduled task is unregistered and files are left in place.

.PARAMETER TaskName
Scheduled task name. Default: WorkTimeTrackerAgent.

.EXAMPLE
.\uninstall-task.ps1 -InstallPath C:\WorkTimeTracker\Agent
#>

[CmdletBinding()]
param(
    [string]$InstallPath,
    [string]$TaskName = "WorkTimeTrackerAgent"
)

$ErrorActionPreference = "Stop"

if (-not ([System.Security.Principal.WindowsPrincipal] [System.Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script elevated (Administrator)."
}

# Stop running agent (optional — best effort)
Get-Process WorkTimeTracker.Agent -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "Stopping process $($_.Id) ..."
    try { $_.Kill() } catch { Write-Warning "Could not stop pid $($_.Id): $_" }
}

if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
    Write-Host "Unregistering Scheduled Task '$TaskName' ..."
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
} else {
    Write-Host "Task '$TaskName' is not registered."
}

if ($InstallPath) {
    if (Test-Path $InstallPath) {
        Write-Host "Removing $InstallPath ..."
        Remove-Item -Path $InstallPath -Recurse -Force
    } else {
        Write-Host "InstallPath '$InstallPath' does not exist, nothing to delete."
    }
}

Write-Host "Done."
