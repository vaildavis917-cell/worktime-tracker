# WorkTimeTracker

Корпоративная система учёта рабочего времени и активности сотрудников на терминальных серверах Windows. Аналог по функциональности продуктам класса Mirabase / StaffCop / Kickidler — фиксирует время RDP-сессий, делает скриншоты по событиям, ведёт журнал запусков процессов и посещений сайтов, выгружает данные в централизованную админку с авторизацией через Active Directory.

> ⚠️ **Юридическое предупреждение.** Скрытое наблюдение за работниками без их письменного уведомления и согласия в большинстве юрисдикций (РФ — ст. 86 ТК, ЕС — GDPR, США — ECPA) является нарушением закона. Перед развёртыванием обязательно: (1) включите пункт о мониторинге в трудовой договор / ЛНА; (2) разместите видимое уведомление при входе в RDP-сессию; (3) согласуйте обработку персональных данных с DPO/юристом. Поддержка скрытого режима в этом репозитории не реализуется.

---

## Архитектура

Решение состоит из четырёх .NET 8 проектов:

| Проект | Тип | Где разворачивается | Назначение |
|---|---|---|---|
| `WorkTimeTracker.Agent` | Worker Service | На каждом терминальном сервере Windows | Слушает события RDP-сессий (WTSAPI32), считает время активности, делает скриншоты, мониторит процессы и URL-адреса, отправляет батчи на сервер |
| `WorkTimeTracker.Server` | ASP.NET Core Web API | Один сервер в домене (можно за nginx/IIS) | Принимает данные от агентов, хранит в PostgreSQL и файловом хранилище, выдаёт API для админки |
| `WorkTimeTracker.Admin` | Blazor Server | Тот же сервер (или отдельный IIS) | Веб-интерфейс админа с AD/Kerberos-аутентификацией: список сотрудников, таймлайн RDP-сессий, галерея скриншотов, отчёты |
| `WorkTimeTracker.Shared` | Class Library | Зависимость для Agent/Server/Admin | Общие DTO и модели, контракты HTTP API |

### Поток данных

```
[Терминальный сервер #1] ──┐
[Терминальный сервер #2] ──┼─► HTTPS + JWT ─► [Server Web API] ─► PostgreSQL
[Терминальный сервер #N] ──┘                                   └─► Screenshot storage (FS / S3)
                                                                       ▲
                                                              Kerberos │
                                                                       │
                                                            [Admin Blazor] ◄── Domain admin (browser)
```

### События, которые фиксирует агент

- **RDP-сессии:** `SESSION_LOGON`, `SESSION_LOGOFF`, `SESSION_LOCK`, `SESSION_UNLOCK`, `REMOTE_CONNECT`, `REMOTE_DISCONNECT` через `WTSRegisterSessionNotification` (Win32 API).
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
| Windows Server | 2019 / 2022 | На терминальных серверах (Agent) |
| .NET 8 SDK | 8.0.x | Для сборки |
| .NET 8 Runtime (Hosting Bundle) | 8.0.x | На сервере где крутится Server/Admin |
| PostgreSQL | 16+ | Один экземпляр на сервере БД |
| Active Directory | любая | Домен с группой `WorkTimeTracker-Admins` |

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

### Установка Agent как Windows Service

```powershell
dotnet publish src/WorkTimeTracker.Agent -c Release -r win-x64 --self-contained -o C:\WorkTimeTracker\Agent
sc.exe create WorkTimeTrackerAgent binPath="C:\WorkTimeTracker\Agent\WorkTimeTracker.Agent.exe" start=auto
sc.exe start WorkTimeTrackerAgent
```

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
