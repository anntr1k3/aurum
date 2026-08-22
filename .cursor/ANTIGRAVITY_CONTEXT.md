# AURUM — ПОЛНЫЙ КОНТЕКСТ ПРОЕКТА ДЛЯ GOOGLE ANTIGRAVITY

> **Назначение документа:** Этот файл содержит исчерпывающий контекст, архитектуру, стек технологий, историю решений и инструкции для агента Antigravity на любом компьютере для мгновенного продолжения разработки без потери контекста.

---

## 1. 📌 О Проекте (Executive Summary)

* **Название:** Aurum (AtlasOS / Windows Companion)
* **Тип приложения:** Настольное приложение Windows (WPF + .NET 8, C# 12) + Статический промо-сайт/документация
* **Репозиторий:** `C:\Users\218kami\Documents\Aurum` (или локальный путь на новой машине)
* **Лицензия:** MIT License
* **Ключевая философия:** 
  1. **Никаких мифов и плацебо:** Отказ от непроверяемых рецептов, скрытых твиков и обещаний «+300% FPS».
  2. **100% детерминированный откат (Rollback):** Перед любым изменением снимается точный снимок исходного состояния (включая факт отсутствия ключа). Откат возвращает систему ровно в то состояние, в котором она была.
  3. **Локальность и приватность:** 0% телеметрии, 0 сетевых запросов при работе основных функций, работа без прав администратора там, где это возможно.
  4. **Безопасность реестра и ядра:** Транзакции с откатом при сбоях многосоставных твиков, контроль дрейфа (Drift Detection) без затирания исходного снапшота.
  5. **Двухуровневый UI (Simple vs Pro):** Переключение режима пользователя меняет только плотность и детализацию технической информации (GUID, Bus ID, hex-значения), сохраняя 100% функционала для всех.

---

## 2. 🏗️ Архитектура и Структура Проекта

Проект состоит из 3 основных слоев, полностью свободных от внешних NuGet-зависимостей во время выполнения (Standard Library / Native APIs):

```text
Aurum/
├── src/
│   ├── Aurum.Core/                     # Доменная модель, интерфейсы, транзакционный движок, каталоги
│   ├── Aurum.Infrastructure.Windows/   # Win32/P-Invoke, реестр, SCM, Power API, ntdll, JSON-хранилища
│   └── Aurum.App/                      # WPF-приложение (MVVM, DataBinding, XAML, шрифты, стили)
├── tests/
│   └── Aurum.Core.SelfTests/           # 61 автономных модульных и интеграционных тестов
├── website/                            # Статический промо-сайт (HTML, CSS, JS, WOFF2)
├── docs/                               # product, architecture, roadmap
├── tools/                              # Verify-Invariants.ps1
├── .cursor/                            # навыки агента и handover-контекст
├── build_release.ps1                   # Скрипт сборки релиза
└── Aurum.sln
```

### Зависимости и правила изоляции:
* `Aurum.Core` **не имеет зависимостей** от WPF, UI или Windows Registry API. Все операции с реестром, устройствами и системными вызовами скрыты за интерфейсами (`ISystemStore`, `IMsiDeviceInventory`, `ISystemTimerService`, `IPowerPlanStore` и т.д.).
* `Aurum.Infrastructure.Windows` реализует доступ к Windows API через нативные P/Invoke (`ntdll.dll`, `powrprof.dll`, `kernel32.dll`, `advapi32.dll`) и реестр.
* `Aurum.App` реализует чистый MVVM. ViewModels не вызывают Windows API напрямую, работая только через доменные менеджеры Core/Infrastructure.

---

## 3. 🧩 Полный Каталог Модулей и Функционала

### 1. Твики Реестра и Ядра (Tweak Engine)
* **Файлы:** `src/Aurum.Core/BuiltInTweakCatalog.cs`, `src/Aurum.Core/TweakEngine.cs`, `src/Aurum.Infrastructure.Windows/JsonTweakStateRepository.cs`
* **Возможности:**
  * Каталог из 35+ проверенных системных твиков (Gaming, Explorer, Privacy, Network, Kernel).
  * `Win32PrioritySeparation = 0x26 (38)` — квантовый приоритет активного игрового окна.
  * `DisablePagingExecutive = 1` — удержание ядра Windows и драйверов в RAM (запрет сброса в pagefile).
  * `SystemResponsiveness = 0` — 100% мощности процессора для активных игр (снятие 20% резерва).
  * `NetworkThrottlingIndex = 0xFFFFFFFF` — отключение искусственного троттлинга сетевых пакетов.
  * Классическое контекстное меню Windows 10 для Windows 11.
  * Профили: `gaming`, `privacy`, `balanced`, `full-audit`.
  * Детекция внешнего дрейфа и исправление (Repair).

### 2. Аппаратные Прерывания (MSI Mode Manager)
* **Файлы:** `src/Aurum.Core/MsiMode.cs`, `src/Aurum.Infrastructure.Windows/WindowsPciDeviceInventory.cs`, `src/Aurum.Infrastructure.Windows/JsonMsiStateRepository.cs`, `src/Aurum.App/ViewModels/MsiViewModel.cs`
* **Возможности:**
  * Инвентаризация шины PCI (`HKLM\SYSTEM\CurrentControlSet\Enum\PCI\...`).
  * Классификация: Видеокарты (GPU), Сеть (LAN/Wi-Fi), Звук (Audio), USB (xHCI), Диски (NVMe/SATA).
  * 1-клик пресет **«⚡ Оптимизировать для игр»**: включение MSI и приоритет `High` для GPU и сети для устранения очереди IRQ и минимизации DPC Latency.
  * 1-клик кнопка **«Вернуть исходные»** с откатом из JSON-хранилища `%LocalAppData%\Aurum\msi_state.json`.

### 3. Системный Таймер Высокого Разрешения (System Timer Resolution)
* **Файлы:** `src/Aurum.Core/SystemTimer.cs`, `src/Aurum.Infrastructure.Windows/WindowsSystemTimerService.cs`, `src/Aurum.App/ViewModels/SystemTimerViewModel.cs`
* **Возможности:**
  * Нативное управление через P/Invoke `ntdll.dll` (`NtQueryTimerResolution`, `NtSetTimerResolution`).
  * Пресеты: `0.5 мс` и `1.0 мс` по запросу процесса, сброс к значению Windows без перезагрузки. Это не обязательный шаг и не обещание FPS.
  * Отображение частоты опроса в реальном времени.
  * Управление параметром реестра `GlobalTimerResolutionRequests` (Windows 11).

### 4. Электропитание и Core Parking
* **Файлы:** `src/Aurum.Core/PowerPlans.cs`, `src/Aurum.Core/CoreParking.cs`, `src/Aurum.Infrastructure.Windows/WindowsPowerPlanStore.cs`, `src/Aurum.Infrastructure.Windows/WindowsCoreParkingStore.cs`
* **Возможности:**
  * Распарковка 100% ядер процессора (Unpark) для исключения задержек пробуждения ядер.
  * Создание изолированного клона схемы электропитания без модификации системных профилей Windows.
  * Настройка минимума и максимума ядер отдельно для AC (сеть) и DC (батарея).

### 5. Обслуживание Накопителей и Безопасный TRIM
* **Файлы:** `src/Aurum.Core/Storage.cs`, `src/Aurum.Infrastructure.Windows/WindowsStorageInventoryStore.cs`, `src/Aurum.Infrastructure.Windows/DefragStorageOptimizer.cs`
* **Возможности:**
  * Инвентаризация томов и физических дисков.
  * Отправка команды ReTrim **только на SSD / NVMe**.
  * **Аппаратная защита:** исключение вращающихся магнитных накопителей (HDD) от выполнения TRIM.
  * Настройка системных параметров NTFS (8.3 имена, время последнего доступа).

### 6. Управление Службами Windows (Services Safety Manager)
* **Файлы:** `src/Aurum.Core/WindowsServices.cs`, `src/Aurum.Infrastructure.Windows/WindowsServiceInventory.cs`, `src/Aurum.Infrastructure.Windows/WindowsServiceControlStore.cs`
* **Возможности:**
  * Построение обратного графа зависимостей служб в памяти.
  * Классификация безопасности на основе строгих белых списков (Allowlists).
  * Защита критически важных системных служб от отключения.

### 7. Сетевые Профили и Переключение DNS
* **Файлы:** `src/Aurum.Core/NetworkDiagnostics.cs`, `src/Aurum.Core/NetworkTuning.cs`, `src/Aurum.Infrastructure.Windows/WindowsNetworkProbe.cs`, `src/Aurum.Infrastructure.Windows/WindowsNetworkTuningStore.cs`
* **Возможности:**
  * Инвентаризация адаптеров (IPv4, IPv6, MTU, MAC, шлюзы).
  * 1-клик переключение DNS (Cloudflare 1.1.1.1, Google 8.8.8.8, AdGuard, Quad9).
  * 1-клик очистка кэша DNS (Flush DNS).
  * Управление профилями TCP Window Auto-Tuning (Normal / Disabled / Experimental).
  * Нативный ICMP пинг без вызова shell.

### 8. Мониторинг Ресурсов (HUD)
* **Файлы:** `src/Aurum.Core/Monitoring.cs`, `src/Aurum.Infrastructure.Windows/HardwareMonitorService.cs`
* **Возможности:**
  * Загрузка CPU, GPU, VRAM, RAM и дисковой активности.
  * Нативные вызовы (Performance Counters, `GlobalMemoryStatusEx`, DirectX/WDDM).
  * **0% CPU в фоне:** мониторинг полностью останавливается при переключении на другие вкладки.

### 9. Проверка Целостности AtlasOS (Offline Health)
* **Файлы:** `src/Aurum.Core/AtlasHealth.cs`, `src/Aurum.Infrastructure.Windows/AtlasHealthService.cs`
* **Возможности:**
  * Автономный анализ структуры файлов (`%WINDIR%\AtlasModules`, `%WINDIR%\AtlasDesktop`).
  * Сверка SHA-256 хэшей официальных компонентов AtlasOS без выполнения внешних скриптов.

### 10. Безопасная Очистка Диска (Safe Cleanup)
* **Файлы:** `src/Aurum.Core/Cleanup.cs`, `src/Aurum.Infrastructure.Windows/SystemCleanupService.cs`
* **Возможности:**
  * Двухфазная очистка: сначала сканирование и список кандидатов, затем ручное подтверждение.
  * Защита от Time-of-Check to Time-of-Use (TOCTOU): сверка размера и даты модификации перед удалением.
  * Разрешены только строго белые списки временных каталогов.

---

## 4. 🎨 Дизайн-Система и Шрифты

* **Палитра (Nordic Blue Dark Theme):**
  * Фон страницы / окна: `#070B10`
  * Боковая панель: `#0E141C`
  * Панели / Табы: `#0F1822`
  * Карточки: `#151D27`
  * Активные элементы / Hover: `#1C2734`
  * Границы: `#293646` / `#1D2A38`
  * Основной акцент: `#58A6E7` (Hover: `#79B9EC`, Dark: `#102C42`)
  * Текст: `#F4F8FC`, вторичный: `#C5D0DC`, приглушённый: `#9CABBc`
  * Успех / Ошибка: `#4CCB91` / `#EF6A72`
* **Типографика:**
  * Заголовки: `Space Grotesk`, `Unbounded`
  * Основной текст: `Plus Jakarta Sans`, `Onest`
  * Код / Метрики / Значения: `JetBrains Mono`

---

## 5. 🛠️ Команды Сборки, Тестирования и Запуска

Для работы требуется **.NET 8.0 SDK** (установлен в системе или локально в `.dotnet/`).

### 1. Запуск всех тестов (35 тестов):
```powershell
.\.dotnet\dotnet.exe run --project .\tests\Aurum.Core.SelfTests
```
*(Или глобальным dotnet: `dotnet run --project .\tests\Aurum.Core.SelfTests`)*

### 2. Сборка всего решения:
```powershell
.\.dotnet\dotnet.exe build .\Aurum.sln --configuration Release
```

### 3. Полный релизный билд в единый файл `dist/Aurum.exe`:
```powershell
powershell.exe -ExecutionPolicy Bypass -File .\build_release.ps1
```

### 4. Локальный запуск промо-сайта:
```powershell
python -m http.server 4173
# Открыть в браузере: http://localhost:4173/website/
```

---

## 6. 🌐 Структура Промо-Сайта (`website/`)

* `website/index.html` — Главная страница с интерактивной демонстрацией всех возможностей Aurum.
* `website/styles.css` — Стили темы Nordic Dark, адаптивная верстка под мобильные устройства и десктоп.
* `website/app.js` — Интерактивный переключатель возможностей (MSI, Timer, Tweaks, Core Parking, Network, Storage, Monitoring), мобильное меню и анимации.
* `website/fonts/` — Автономные шрифты Onest и Unbounded.
* `website/README.md` — Инструкция по локальному развертыванию сайта.

---

## 7. 💡 Советы для Агента Antigravity на Новом Компьютере

1. **Не затирайте snapshot-файлы:** Все откаты хранятся в `%LocalAppData%\Aurum\*.json`. При изменении логики менеджеров сохраняйте обратную совместимость JSON-схем.
2. **Сохраняйте нулевую зависимость от сторонних библиотек:** Вся мощь Aurum заключается в чистом C# и прямых нативных вызовах Win32/NT API без тяжелых зависимостей.
3. **Все новые модули должны сопровождаться тестами:** Добавляйте новые тесты в `tests/Aurum.Core.SelfTests/Program.cs` и запускайте перед каждым релизом.
