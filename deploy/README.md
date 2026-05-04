# Deployment scripts

Скрипты раскатки агента WorkTimeTracker на парк Windows 10 рабочих станций.

## Файлы

- `install-task.ps1` — копирует publish-каталог на машину и регистрирует Scheduled Task.
- `uninstall-task.ps1` — удаляет Scheduled Task и (опционально) каталог установки.

## Почему Scheduled Task, а не Windows Service

Windows Service всегда запускается в Session 0, у которой нет интерактивного рабочего стола. Скриншоты с него получаются чёрные. Поэтому агент регистрируется как Scheduled Task с триггером `At log on` — процесс живёт внутри сессии пользователя и видит реальный экран. Ровно та же модель, что у большинства коммерческих time-tracker'ов на Win10.

## Локальная установка на одну машину

```powershell
# 1. Собрать self-contained пакет один раз на dev-машине:
dotnet publish src\WorkTimeTracker.Agent -c Release -r win-x64 --self-contained -o publish\Agent

# 2. На целевой машине от администратора:
.\deploy\install-task.ps1 `
    -SourcePath .\publish\Agent `
    -ServerUrl https://worktime.contoso.local `
    -AgentToken <DEVICE_TOKEN_FROM_ADMIN_UI>
```

## Массовая раскатка через PsExec

```powershell
$hosts = Get-Content workstations.txt
foreach ($h in $hosts) {
    Write-Host "=== $h ==="
    robocopy publish\Agent \\$h\C$\WorkTimeTracker\Agent-staging /MIR /NJH /NJS /NP
    Copy-Item .\deploy\install-task.ps1 \\$h\C$\WorkTimeTracker\install-task.ps1 -Force
    psexec \\$h -h -s powershell.exe -ExecutionPolicy Bypass -File `
        C:\WorkTimeTracker\install-task.ps1 -SourcePath C:\WorkTimeTracker\Agent-staging
}
```

## Через Intune (Win32 app)

1. Соберите `.intunewin` пакет:
   ```
   IntuneWinAppUtil.exe -c .\publish\Agent -s WorkTimeTracker.Agent.exe -o .\intune-out
   ```
   Положите `install-task.ps1` рядом с `publish\Agent` перед упаковкой.
2. В Intune создайте Win32 App:
   - **Install command**: `powershell.exe -ExecutionPolicy Bypass -File install-task.ps1 -SourcePath .`
   - **Uninstall command**: `powershell.exe -ExecutionPolicy Bypass -File uninstall-task.ps1 -InstallPath C:\WorkTimeTracker\Agent`
   - **Detection rule**: Custom script — `Get-ScheduledTask -TaskName WorkTimeTrackerAgent -ErrorAction SilentlyContinue` and exit 0 if found.
   - **Install behavior**: System.

## Проверка после установки

```powershell
Get-ScheduledTask -TaskName WorkTimeTrackerAgent
Get-Process WorkTimeTracker.Agent -ErrorAction SilentlyContinue
Get-Content C:\WorkTimeTracker\Agent\logs\*.log -Tail 50
```

В трее у пользователя должна появиться иконка с balloon-уведомлением "Этот компьютер находится под наблюдением". Это требование compliance, выключать его нельзя.

## Удаление

```powershell
.\deploy\uninstall-task.ps1 -InstallPath C:\WorkTimeTracker\Agent
```
