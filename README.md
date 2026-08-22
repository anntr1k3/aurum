# Aurum — компаньон AtlasOS (v1.0.1)

[![Build Status](https://github.com/anntr1k3/aurum/actions/workflows/build.yml/badge.svg)](https://github.com/anntr1k3/aurum/actions/workflows/build.yml)
[![GitHub Pages](https://github.com/anntr1k3/aurum/actions/workflows/pages.yml/badge.svg)](https://anntr1k3.github.io/aurum/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform: Windows 10/11](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20x64-0078D6.svg)](https://github.com/anntr1k3/aurum)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0%20(C%23%2012)-512BD4.svg)](https://dotnet.microsoft.com/)

[ 🇬🇧 **English** ](README.en.md) | [ 🌐 **Сайт** ](https://anntr1k3.github.io/aurum/) | [ ⬇ **Скачать** ](https://github.com/anntr1k3/aurum/releases/latest)

---

Aurum — открытый, прозрачный и полностью обратимый компаньон для **AtlasOS** и аккуратной настройки чистой Windows.

Вместо непрозрачных «магических» скриптов Aurum держится инженерных правил:

- **Без змеиного масла.** Только объяснимые изменения, без скрытых действий и без необоснованных обещаний FPS.
- **Отклик системы, а не маркетинг.** Прерывания (MSI), разрешение таймера, фон Windows, износ SSD и поддерживаемые механизмы питания.
- **Откат и дрейф.** Перед записью сохраняется исходное состояние в `%LOCALAPPDATA%\Aurum\`; его можно вернуть или восстановить, если Windows сдвинула настройку.
- **Без телеметрии.** Приложение работает офлайн, ничего не отправляет и не скачивает чужие скрипты.

Текущая сборка Windows x64 лежит в [GitHub Releases](https://github.com/anntr1k3/aurum/releases/latest): один файл `Aurum.exe`, без установщика.

---

## Возможности

### Каталог твиков

- **24 настройки** в восьми категориях: Проводник, Интерфейс, Ввод, Игры, Система, Ядро, Сеть, Конфиденциальность.
- В том числе спорные параметры (Defender realtime, UAC, обновления, VSS) с явным риском и точным откатом.
- Живой дрейф: если обновление Windows вернуло значение, доступна кнопка **Восстановить**.

### Службы

- Типы запуска через SCM, граф зависимостей, шесть групп (`telemetry`, `xbox`, `print`, `maps-location`, `touch`, `insider`).
- Пишутся только объявленные необязательные имена. Пакетное отключение отменяется, если снаружи пакета ещё запущена зависимость.

### Диски

- 8.3-имена NTFS, LastAccess, гибернация, SysMain — с снимком для отката.
- Analyze и ReTrim только для подходящих SSD, с предпросмотром команды Windows.

### Сеть

- Пресеты DNS (Cloudflare, Google, Quad9, AdGuard, DHCP), сброс кэша, TCP auto-tuning и ECN.
- Замер ICMP — только по явной кнопке. Откат DNS не применяется, если GUID адаптера сменился.

### Питание, MSI, таймер, мониторинг

- Схемы питания и Core Parking на клоне плана Aurum.
- MSI для PCI-устройств из живого инвентаря.
- Разрешение системного таймера по запросу.
- Живые графики CPU/RAM/диск/сеть без фонового агента. Очистка temp и кэша шейдеров с предпросмотром.

---

## Сборка

Нужны Windows 10/11 x64 и .NET 8 SDK (или портативный SDK в `.dotnet/`).

```powershell
powershell -ExecutionPolicy Bypass -File .\build_release.ps1
```

Скрипт проверяет инварианты, гоняет **59** самотестов и публикует `dist\Aurum.exe` (~69.5 МБ, self-contained).

```powershell
dotnet build .\Aurum.sln --configuration Release
dotnet run --project .\tests\Aurum.Core.SelfTests
dotnet run --project .\src\Aurum.App
```

Снимки (`SchemaVersion: 1`) лежат только локально: `state.json`, `services.json`, `storage_tuning.json`, `network_tuning.json`, `power_plan.json`, `core_parking.json`, `msi_state.json`, `audit.jsonl`.

---

## Документация

- [Кратко о продукте](docs/product.md)
- [Архитектура](docs/architecture.md)
- [Дорожная карта](docs/roadmap.md)
- [Участие в разработке](CONTRIBUTING.md)

Лицензия — **MIT**, см. [`LICENSE`](LICENSE).
