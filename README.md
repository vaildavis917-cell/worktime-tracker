# WorkTimeTracker

Корпоративная система учёта рабочего времени и активности сотрудников. Агент устанавливается на **рабочие станции Windows 10**, к которым сотрудники подключаются по RDP (или работают за консолью), сервер и админка — на отдельной машине. Аналог по функциональности продуктам класса Mirabase / StaffCop / Kickidler — фиксирует время RDP-сессий, делает скриншоты по событиям, ведёт журнал запусков процессов и посещений сайтов, выгружает данные в централизованную админку с авторизацией через Active Directory.

> ⚠️ **Юридическое предупреждение.** Скрытое наблюдение за работниками без их письменного уведомления и согласия в большинстве юрисдикций (РФ — ст. 86 ТК, ЕС — GDPR, США — ECPA) является нарушением закона. Перед развёртыванием обязательно: (1) включите пункт о мониторинге в трудовой договор / ЛНА; (2) разместите видимое уведомление при входе в RDP-сессию; (3) согласуйте обработку персональных данных с DPO/юристом. Поддержка скрытого режима в этом репозитории не реализуется.

---

## Архитектура

Решение состоит из четырёх .NET 8 проектов:

| Проект | Тип | Где разворачивается | Назначение |
|---|---|---|---|
| `WorkTimeTracker.Agent` | Worker Service | На каждой Windows 10 рабочей станции | Слушает события WTS (console + RDP), считает время активности, делает скриншоты, мониторит процессы и URL-адреса, отправляет батчи на сервер |
| `WorkTimeTracker.Server` | ASP.NET Core Web API | Отдельный сервер (Windows Server или Linux), в домене | Принимает данные от агентов, хранит в PostgreSQL и файловом хранилище, выдаёт API для админки |
| `WorkTimeTracker.Admin` | Blazor Server | Тот же сервер (или отдельный IIS / Kestrel) | Веб-интерфейс админа с AD/Kerberos-аутентификацией: список сотрудников, таймлайн RDP-сессий, галерея скриншотов, отчёты |
| `WorkTimeTracker.Shared` | Class Library | Зависимость для Agent/Server/Admin | Общие DTO и модели, контракты HTTP API |

### Поток данных

```
[Win10 рабочая станция #1] ──┐
[Win10 рабочая станция #2] ──┼─► HTTPS + device-token ─► [Server Web API] ─► PostgreSQL
[Win10 рабочая станция #N] ──┘                                              └─► Screenshot storage (FS / S3)
                                                                                       ▲
                                                                              Kerberos │
                                                                                       │
                                                                           [Admin Blazor] ◄── Domain admin (browser)
```

> **Особенность Windows 10 (vs Windows Server / RDS):** на клиентских версиях Windows одновременно активна **только одна интерактивная сессия** — либо консоль, либо одно RDP-подключение. При входе по RDP консольная сессия автоматически отключается (и наоборот). Агент это учитывает: всегда отслеживает максимум один активный таймер на машину, и при переходе console ↔ remote — закрывает предыдущий интервал и открывает новый с пометкой типа сессии.

### События, которые фиксирует агент

- **Сессии:** `SESSION_LOGON`, `SESSION_LOGOFF`, `SESSION_LOCK`, `SESSION_UNLOCK`, `CONSOLE_CONNECT`, `CONSOLE_DISCONNECT`, `REMOTE_CONNECT`, `REMOTE_DISCONNECT` через `WTSRegisterSessionNotification` (Win32 API). Тип сессии (console vs remote) определяется через `WTSQuerySessionInformation(WTSClientProtocolType)`: 0 = console, 2 = RDP.
- **Скриншоты:** все мониторы (объединённый bitmap по `SystemInformation.VirtualScreen`), каждые 30 секунд по умолчанию.
- **Live-view:** в админке кнопка "Watch" по любой станции — сервер ставит флаг `LiveViewUntil = +10 минут`, агент читает желаемый интервал из ответа на heartbeat и переключается на 2 секунды. Видимый индикатор: иконка в трее остаётся, плюс badge "live" в админке. Без ввода с админ-конца — только просмотр.
- **RDP shadow:** в админке кнопка "RDP shadow" формирует команду `mstsc /shadow:<sessionId> /v:<hostname> /control` для админ-машины — реальный session id берётся из последнего heartbeat'а. Подключение использует встроенный механизм Windows Remote Desktop Services Session Shadowing, требует политики "Set rules for remote control of Remote Desktop Services user sessions" в GPO. **Никакого собственного канала remote control в агенте нет** (см. "Что не входит в проект").
- **Foreground-окно:** через `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)`. По смене активного окна — пишется заголовок и имя процесса. (TODO)
- **Триггеры на скриншот по событию:** запуск Telegram (`Telegram.exe`), любого браузера, переключение на нерабочее ПО (список конфигурируется). (TODO)
- **Браузерные URL:** опционально, через UI Automation у `chrome.exe` / `msedge.exe` / `firefox.exe` — без установки расширений. (TODO)
- **Простой:** определяется через `GetLastInputInfo` (нет ввода >N минут — таймер ставится на паузу). (TODO)

---

## Стек

- **Язык:** C# 12 / .NET 8 (LTS) — единый стек на агенте, сервере и админке.
- **БД:** PostgreSQL 16 + EF Core 8 (`Npgsql.EntityFrameworkCore.PostgreSQL`).
- **Хранилище скриншотов:** локальная ФС в MVP; S3-совместимое (MinIO/AWS) для масштабирования.
- **Аутентификация админки:** `Microsoft.AspNetCore.Authentication.Negotiate` (Kerberos/NTLM), привязка к группам AD.
- **Аутентификация агента:** долгоживущий device-токен в `appsettings.json` + JWT для сессий.
- **UI админки:** Blazor Server (interactivity по WebSocket, минимум JS).

---

## Требования к окружению

| Компонент | Версия | Где |
|---|---|---|
| Windows 10 | 1809 (build 17763) и новее | На рабочих станциях с агентом — нужно для стабильной работы WTS API и UI Automation в современных браузерах. Поддерживается также Windows 11 |
| .NET 8 Runtime | 8.0.x (или self-contained publish) | На каждой рабочей станции с агентом |
| .NET 8 SDK | 8.0.x | Для сборки на dev-машине |
| .NET 8 Runtime (ASP.NET Core Hosting Bundle) | 8.0.x | На машине Server/Admin (Windows Server 2019+ или Linux с .NET 8) |
| PostgreSQL | 16+ | Один экземпляр на сервере БД |
| Active Directory | любая | Домен с группой `WorkTimeTracker-Admins`; рабочие станции должны быть членами домена для Kerberos между агентом и сервером (опционально — можно ограничиться device-токеном) |

---

## Сборка

```powershell
# из корня репозитория
dotnet restore
dotnet build -c Release
dotnet test
```

### Локальный запуск Server + Admin

```powershell
# консоль 1
cd src/WorkTimeTracker.Server
dotnet run

# консоль 2
cd src/WorkTimeTracker.Admin
dotnet run
```

### Запуск Agent в режиме консоли (отладка)

```powershell
cd src/WorkTimeTracker.Agent
dotnet run
```

### Сборка self-contained пакета агента

```powershell
dotnet publish src/WorkTimeTracker.Agent -c Release -r win-x64 --self-contained -o publish\Agent
```

В каталоге `publish\Agent` после этого лежит автономный `WorkTimeTracker.Agent.exe` со всеми зависимостями — его не нужно устанавливать вместе с .NET Runtime на каждую станцию.

### Установка Agent на одну Windows 10 машину (ручная)

> ⚠️ **Важно: Task Scheduler, а не Windows Service.** Windows Service запускается в Session 0, у которой нет интерактивного рабочего стола, поэтому скриншоты с неё получаются чёрные. Для Win10 (где одновременно активна одна сессия) правильный путь — Task Scheduler с триггером "при логоне любого пользователя", тогда процесс агента живёт внутри сессии пользователя и видит реальный экран.

Запустить от администратора на целевой машине:

```powershell
# 1. Скопировать publish\Agent в C:\WorkTimeTracker\Agent и поправить appsettings.json (ServerUrl, AgentToken).
# 2. Зарегистрировать запланированную задачу:
$action  = New-ScheduledTaskAction `
    -Execute "C:\WorkTimeTracker\Agent\WorkTimeTracker.Agent.exe" `
    -WorkingDirectory "C:\WorkTimeTracker\Agent"
$trigger = New-ScheduledTaskTrigger -AtLogOn
$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) `
    -ExecutionTimeLimit ([TimeSpan]::Zero)
$principal = New-ScheduledTaskPrincipal -GroupId "BUILTIN\Users" -RunLevel Limited

Register-ScheduledTask -TaskName "WorkTimeTrackerAgent" `
    -Action $action -Trigger $trigger -Settings $settings -Principal $principal `
    -Description "Учёт рабочего времени и активности — корпоративный мониторинг"

# Запустить немедленно для текущего пользователя (не дожидаясь следующего логона):
Start-ScheduledTask -TaskName "WorkTimeTrackerAgent"
```

> `RunLevel Limited` означает, что агент запускается с правами обычного пользователя — этого достаточно для скриншотов и отслеживания процессов в его собственной сессии. Повышенные права не нужны и не должны выдаваться.

### Массовое развёртывание на парк рабочих станций

В сценарии с десятками/сотнями Win10 машин выбирайте один из вариантов в зависимости от того, что уже используется в инфраструктуре. Все варианты ниже регистрируют именно Scheduled Task (а не сервис) — это критично для работы скриншотов на Win10.

1. **Active Directory + Group Policy.** Самый портативный путь — создать Group Policy Preference типа Scheduled Task (`Computer Configuration → Preferences → Control Panel Settings → Scheduled Tasks → New → Scheduled Task (At least Windows 7)`). Триггер `At log on of any user`, действие — путь к `WorkTimeTracker.Agent.exe`. Сами файлы агента предварительно раскатать на машины через GPO Files preference из сетевой шары `\\fileserver\worktime-agent\`. Привязать GPO к OU с целевыми компьютерами.
2. **PsExec (быстрое разворачивание без GPO).** Для разовой установки:
   ```powershell
   $hosts = Get-Content workstations.txt
   foreach ($h in $hosts) {
       robocopy publish\Agent \\$h\C$\WorkTimeTracker\Agent /MIR
       Copy-Item .\install-task.ps1 \\$h\C$\WorkTimeTracker\Agent\install-task.ps1 -Force
       psexec \\$h -h -s powershell -ExecutionPolicy Bypass -File C:\WorkTimeTracker\Agent\install-task.ps1
   }
   ```
   где `install-task.ps1` содержит блок `Register-ScheduledTask` из примера выше.
3. **Microsoft Intune / SCCM.** Упакуйте `publish\Agent` + `install-task.ps1` в `.intunewin` через [Intune Win32 App Packaging Tool](https://learn.microsoft.com/intune/intune-service/apps/apps-win32-app-management). Install-команда — `powershell -ExecutionPolicy Bypass -File install-task.ps1`. Detection rule — наличие задачи через `schtasks /Query /TN WorkTimeTrackerAgent`. Рекомендуется для Azure AD / Intune-управляемых парков.
4. **Chocolatey for Business / WinGet.** Если в компании уже стоит пакетный менеджер — соберите `.nupkg` или WinGet-манифест с тем же install-task.ps1.

После установки запись о машине автоматически появится в админке при первом heartbeat'е (раздел «Станции») или при первом загруженном скриншоте, на основе hostname. Привязка к сотруднику делается по `samAccountName` пользователя, выполнившего вход.

---

## Структура репозитория

```
worktime-tracker/
├── src/
│   ├── WorkTimeTracker.Shared/   # DTO, модели, контракты
│   ├── WorkTimeTracker.Agent/    # Worker Service для рабочих станций Windows 10
│   ├── WorkTimeTracker.Server/   # Web API
│   └── WorkTimeTracker.Admin/    # Blazor Server админка
├── tests/
│   └── WorkTimeTracker.Tests/    # xUnit
├── .github/workflows/ci.yml      # GitHub Actions: build + test
├── WorkTimeTracker.sln
├── LICENSE                        # MIT
└── README.md
```

---

## Дорожная карта (MVP → v1)

- [x] Скаффолдинг solution и CI (этап 0)
- [x] Agent: захват скриншотов всех мониторов и периодическая загрузка (этап 1)
- [x] Server: приём multipart-скриншотов и сохранение в БД + ФС (этап 1)
- [x] Agent: WTS-события через P/Invoke (`wtsapi32.dll` + message-only window) (этап 2)
- [x] Server: persistence событий с lifecycle сессий (открытие/закрытие) (этап 2)
- [x] Admin: список станций / сотрудников / RDP-сессий + галерея скриншотов с AD-аутентификацией (этап 4)
- [x] Tray-уведомление "Этот компьютер находится под наблюдением" — compliance (этап 5)
- [x] PowerShell инсталлятор Scheduled Task + Intune (этап 5)
- [x] Live-view (ускоренные скриншоты до 2 сек по запросу из админки) + интеграция с RDP shadow (этап 6)
- [ ] Agent: триггеры на скриншот по запуску процессов и смене foreground-окна
- [ ] Server: EF Core миграции вместо `EnsureCreated()`, аутентификация агентов по device-токену
- [ ] Отчёты по часам в Excel/CSV
- [ ] Интеграционные тесты на тестовом домене

## Что **не** входит в проект

Сознательно **не реализуется** перехват ввода с клавиатуры (keylogger), запись звука с микрофона, чтение содержимого буфера обмена и любые формы скрытой работы (агент виден в Task Scheduler и Task Manager, иконка в трее, уведомление при логоне). Если тебе нужны эти функции — это отдельный класс ПО (StaffCop / Veriato / Teramind), их продают по корпоративной подписке с собственным юр-сопровождением. Этот проект сознательно ограничен учётом времени, периодическими скриншотами и журналом активности на уровне процессов и URL.

---

## Лицензия

[MIT](LICENSE) © 2026
