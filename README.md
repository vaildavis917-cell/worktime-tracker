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
- **Foreground-окно:** через `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)`. По смене активного окна — пишется заголовок и имя процесса.
- **Триггеры на скриншот:** запуск Telegram (`Telegram.exe`), любого браузера, переключение на нерабочее ПО (список конфигурируется), плюс периодический раз в N минут.
- **Браузерные URL:** опционально, через UI Automation у `chrome.exe` / `msedge.exe` / `firefox.exe` — без установки расширений.
- **Простой:** определяется через `GetLastInputInfo` (нет ввода >N минут — таймер ставится на паузу).

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

Запустить от администратора:

```powershell
# скопировать содержимое publish\Agent на целевую машину в C:\WorkTimeTracker\Agent
sc.exe create WorkTimeTrackerAgent binPath= "C:\WorkTimeTracker\Agent\WorkTimeTracker.Agent.exe" start= auto displayname= "WorkTime Tracker Agent"
sc.exe description WorkTimeTrackerAgent "Учёт рабочего времени и активности — корпоративный мониторинг"
sc.exe failure WorkTimeTrackerAgent reset= 86400 actions= restart/60000/restart/60000/restart/60000
sc.exe start WorkTimeTrackerAgent
```

> Перед запуском пропишите в `C:\WorkTimeTracker\Agent\appsettings.json` корректные `ServerUrl` и `AgentToken` (выдаётся через админку при регистрации новой машины).

### Массовое развёртывание на парк рабочих станций

В сценарии с десятками/сотнями Win10 машин выбирайте один из вариантов в зависимости от того, что уже используется в инфраструктуре:

1. **Active Directory + Group Policy (GPO).** Положите publish-каталог на сетевую шару `\\fileserver\worktime-agent\`, создайте Computer Startup Script (`Computer Configuration → Policies → Windows Settings → Scripts → Startup`) с PowerShell, который копирует файлы в `C:\WorkTimeTracker\Agent` и регистрирует службу через `sc.exe create`. Привяжите GPO к OU с целевыми компьютерами. Сценарий выполняется при загрузке от `LocalSystem` — нужных прав хватает.
2. **PsExec (быстрое разворачивание без GPO).** Для разовой установки:
   ```powershell
   $hosts = Get-Content workstations.txt
   foreach ($h in $hosts) {
       robocopy publish\Agent \\$h\C$\WorkTimeTracker\Agent /MIR
       psexec \\$h -h -s sc.exe create WorkTimeTrackerAgent binPath= "C:\WorkTimeTracker\Agent\WorkTimeTracker.Agent.exe" start= auto
       psexec \\$h -h -s sc.exe start WorkTimeTrackerAgent
   }
   ```
3. **Microsoft Intune / SCCM.** Упакуйте `publish\Agent` в `.intunewin` через [Intune Win32 App Packaging Tool](https://learn.microsoft.com/intune/intune-service/apps/apps-win32-app-management) и опубликуйте как Win32-приложение с install-командой `install.ps1` (копирует файлы + `sc.exe create + start`) и detection rule по наличию службы `WorkTimeTrackerAgent`. Это рекомендуемый путь для Azure AD / Intune-управляемых парков.
4. **Chocolatey for Business / WinGet.** Если в компании уже стоит пакетный менеджер — соберите `.nupkg` или WinGet-манифест и пушите его через корпоративный канал.

После установки служба должна попадать в админку (раздел «Серверы / станции») с автоматически сформированной записью на основе hostname. Привязка к сотруднику делается на стороне админки по `samAccountName` пользователя, выполнившего вход.

---

## Структура репозитория

```
worktime-tracker/
├── src/
│   ├── WorkTimeTracker.Shared/   # DTO, модели, контракты
│   ├── WorkTimeTracker.Agent/    # Worker Service для терминальных серверов
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

- [x] Скаффолдинг solution и CI
- [ ] Agent: подписка на WTS-события, базовая отправка heartbeat
- [ ] Agent: скриншот-сервис с триггерами по процессам
- [ ] Server: EF Core миграции, контроллеры приёма событий
- [ ] Admin: страница списка сотрудников + AD-аутентификация
- [ ] Admin: таймлайн RDP-сессий и галерея скриншотов
- [ ] Отчёты по часам в Excel/CSV
- [ ] Уведомление пользователю при входе в RDP (compliance)
- [ ] Интеграционные тесты на тестовом домене

---

## Лицензия

[MIT](LICENSE) © 2026
