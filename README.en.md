# Aurum — Open-Source AtlasOS Companion (v1.0.2)

[![Build Status](https://github.com/anntr1k3/aurum/actions/workflows/build.yml/badge.svg)](https://github.com/anntr1k3/aurum/actions/workflows/build.yml)
[![GitHub Pages](https://github.com/anntr1k3/aurum/actions/workflows/pages.yml/badge.svg)](https://anntr1k3.github.io/aurum/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform: Windows 10/11](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20x64-0078D6.svg)](https://github.com/anntr1k3/aurum)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0%20(C%23%2012)-512BD4.svg)](https://dotnet.microsoft.com/)

[ 🇷🇺 **Русская версия (README.md)** ](README.md) | [ 🌐 **Web Landing Page** ](https://anntr1k3.github.io/aurum/) | [ ⬇ **Download** ](https://github.com/anntr1k3/aurum/releases/latest)

---

Aurum is a modern, transparent, auditable, and fully reversible Windows companion designed specifically for **AtlasOS** and clean Windows optimization.

Instead of opaque "magic" scripts or destructive tweaks, Aurum adheres to a strict engineering philosophy:
- **No Snake-Oil:** Transparent system optimizations, zero hidden actions, and no ungrounded claims.
- **Measurable System Responsiveness:** Focuses on interrupt handling (MSI Mode), timer resolution, DPC queue reduction, SSD wear prevention, and clean OS defaults.
- **Full Reversibility & Drift Detection:** Every modification captures the original pre-Aurum state in `%LOCALAPPDATA%\Aurum\` snapshots and can be restored or repaired at any time.
- **Nordic Blue Design:** Clean, distraction-free modern UI with **Unbounded** & **Onest** typography.
- **Zero Telemetry:** Completely offline, no background telemetry, and no remote script execution.

The current Windows x64 build is on [GitHub Releases](https://github.com/anntr1k3/aurum/releases/latest): a single `Aurum.exe`, no installer.

---

## 🚀 Key Modules & Capabilities

### 1. 🎛 Tweak Catalog & Granular Controls
- **24 Granular Tweaks** across 8 categories: `Explorer`, `Interface`, `Gaming`, `Privacy`, `Input`, `System`, `Kernel`, `Network`.
- Includes power-user options for real-time Defender I/O scanning, UAC prompt reduction, background Windows Updates, and System Restore (VSS), each with clear risk ratings and exact rollback.
- Automatic live drift detection: highlights if Windows Update reverted any settings, with a 1-click **Repair** button.
- 4 built-in 1-click profiles: `balanced`, `gaming`, `privacy`, `laptop`.

### 2. ⚙️ Services Management & 6 Safe Group Presets
- **Win32 SCM Native Integration:** Safe startup type configuration and service stop/start.
- **Dependency Graph:** Shows reverse dependants and services in memory to prevent breaking essential system features.
- **6 Built-in Presets:**
  - `telemetry` (DiagTrack, dmwappushservice, weridsvc)
  - `xbox` (XboxGipSvc, XblAuthManager, XblGameSave, XboxNetApiSvc)
  - `print` (Spooler, Fax)
  - `maps-location` (MapsBroker, lfsvc)
  - `touch` (TabletInputService)
  - `insider` (wisvc, RetailDemo)

### 3. 💾 Storage & SSD Optimization
- **NTFS 8.3 Names:** Disables legacy DOS short name generation (`PROGRA~1`), accelerating directory traversal and preventing MFT bloat.
- **LastAccess Time:** Disables file last access timestamp updates, significantly reducing write wear on SSD/NVMe drives.
- **Hibernation Manager:** Toggles hibernation (`powercfg -h off/on`), freeing **8 to 32+ GB** of space on the system drive by deleting `C:\hiberfil.sys`.
- **SysMain (SuperFetch):** Disables redundant RAM prefetching for high-speed SSDs.
- **Windows ReTrim & Analysis:** Native `defrag /L` (TRIM) and `defrag /A` on selected solid-state volumes with hardware HDD protection.

### 4. 🌐 Network Tuning & DNS Switcher
- **1-Click DNS Profiles:**
  - ⚡ **Cloudflare DNS** (`1.1.1.1` / `1.0.0.1`) — Ultra-low latency and strict privacy.
  - 🌍 **Google Public DNS** (`8.8.8.8` / `8.8.4.4`) — Global reliability and speed.
  - 🛡 **Quad9 Security** (`9.9.9.9` / `149.112.112.112`) — Malware, botnet, and phishing blocking.
  - 🚫 **AdGuard DNS** (`94.140.14.14` / `94.140.15.15`) — Ad, tracker, and banner blocking.
  - 🔄 **DHCP Reset** — Restores automatic DNS from local router/ISP.
- **DNS Resolver Cache Flush:** Native `ipconfig /flushdns` in one click.
- **TCP Stack Auto-Tuning:** Controls `Receive Window Auto-Tuning Level` via `netsh`.
- **Live Latency & Loss Probe:** 4-sample ICMP ping test with latency statistics.

### 5. ⚡ Power Plans & Core Parking
- Safe scheme switching via Win32 Power API.
- Core Parking management (`CPMINCORES`, `CPMAXCORES`) on isolated scheme clones with exact rollback.

### 6. 📊 Hardware Monitoring & Safe Cleanup
- Real-time CPU, RAM, Disk, and Network utilization with live sparklines and 0% background overhead.
- Safe 2-phase temp file and DirectX shader cache scanner with TOCTOU race condition protection.

---

## 🛠 Building from Source & Single-File Release

### Prerequisites
- Windows 10/11 (x64)
- .NET 8.0 SDK (or portable SDK in `.dotnet/`)

### Automated Release Build (Single-File Standalone EXE)
Run the automated build script:
```powershell
powershell -ExecutionPolicy Bypass -File .\build_release.ps1
```
This script will:
1. Verify static project invariants (XAML resource keys resolve, every self-test is registered).
2. Run the entire suite of **63 self-tests**.
3. Compile and publish a standalone single-file binary: `dist\Aurum.exe` (~69.5 MB, self-contained with embedded runtime).

### Manual Build
```powershell
# Build solution
dotnet build .\Aurum.sln --configuration Release

# Run self-tests (63/63 tests)
dotnet run --project .\tests\Aurum.Core.SelfTests

# Run app
dotnet run --project .\src\Aurum.App
```

---

## 🔒 Deterministic Rollback Architecture

- All tweaks are strongly typed, transparent, and opt-in with zero automated blind scripts.
- Every modification creates an exact pre-mutation snapshot enabling 1-click restore.
- All configuration snapshots are versioned (`SchemaVersion: 1`) and stored locally in `%LOCALAPPDATA%\Aurum\`:
  - `state.json` (Registry tweaks)
  - `services.json` (Services state)
  - `storage_tuning.json` (SSD settings)
  - `network_tuning.json` (DNS settings)
  - `power_plan.json` (Power plans)
  - `core_parking.json` (Core parking)
  - `msi_state.json` (MSI device modes)

---

## 📄 License
Distributed under the **MIT License**. See [`LICENSE`](LICENSE) for details.
