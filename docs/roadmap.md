# Aurum roadmap

This roadmap records the intended product direction. Items are not considered
implemented until they are present in the application and covered by checks.

## Current baseline

- Reversible HKCU tweak engine with drift detection and repair.
- Basic Windows, architecture, elevation, and AtlasOS marker information.
- Offline AtlasOS structure and selected binary hash checks.
- Preview-first cleanup for user temp files, shader cache, and crash dumps.

## 1. Hardware monitoring

**Status:** the first read-only slice is implemented. Aurum now shows CPU, the
active display adapter, installed/used memory, system-volume capacity, the
active network interface, uptime, and the current power plan. Live CPU, GPU
(when exposed through PDH), memory, disk-capacity, disk-throughput, and network
rates are sampled into a 60-point in-memory history. Sampling pauses outside the
monitoring tab and nothing is transmitted or persisted.

Driver version, VRAM, physical-drive media/model, CPU frequency history,
page-file usage, and disk active time remain planned refinements.

### Static inventory

- CPU model, topology, architecture, and logical processor count.
- GPU model, driver version, VRAM, and active adapter.
- Installed and available memory.
- System drive model, media type, capacity, free space, and file system.
- Network adapter and negotiated link information.
- Windows build, uptime, and current power plan.

### Live metrics

- CPU utilization and frequency.
- GPU and dedicated-memory utilization when exposed by Windows counters.
- RAM and page-file utilization.
- Disk throughput and active time.
- Network receive/transmit throughput.
- A short in-memory history without telemetry or background persistence.

Sampling should pause when the monitoring surface is not visible. Temperature
and fan data are optional because Windows does not expose reliable universal
sensors; vendor or third-party drivers must not become mandatory dependencies.

## 2. Storage health and SSD tuning

**Status:** the first diagnostic and maintenance slice is implemented. Aurum
enumerates fixed and removable volumes, maps them to physical disk numbers,
shows model, bus, SSD/HDD/virtual evidence, capacity, file system, device TRIM
support, and file-system delete notifications. Explicit Analyze and ReTrim
actions preview the exact supported Windows command, require confirmation and
existing administrator access, and retain the Windows report in memory.
The default storage surface now presents this as one plain-language SSD-care
decision; volume inventory, command previews, and evidence are kept behind a
technical-details control.

Optimize Drives history, SMART/health data, hibernation, page-file, indexing,
SysMain, 8.3-name, and last-access diagnostics remain planned. Aurum does not
yet change any of those settings.

This feature is inspired by the workflow of SSD Mini Tweaker but must be an
independent, auditable implementation. Aurum will not copy its code or blindly
reproduce recommendations designed for Windows XP/7-era storage stacks.

### Diagnostic-first features

- Detect SSD, HDD, NVMe, SATA, removable, virtual, and tiered volumes.
- Show TRIM support and the current delete-notify state.
- Show Optimize Drives status and the last successful optimization.
- Offer explicit analyze and retrim actions for supported SSD volumes.
- Show hibernation, page file, indexing, SysMain, 8.3 names, and last-access
  timestamp states with modern Windows-specific explanations.

### Safety exclusions

- Do not globally disable Optimize Drives: modern Windows selects retrim or
  media-appropriate optimization automatically.
- Do not disable the page file, System Restore, write-cache flushing, SysMain,
  or Prefetch in a recommended profile.
- Do not attempt to switch BIOS storage mode or force AHCI from the application.
- Do not make endurance or performance claims without repeatable measurements.

Every mutable setting needs exact detection, preview, elevation disclosure,
original-state persistence, drift checks, repair, and rollback.

## 3. Processor and power management

**Status:** power-plan inventory and reversible selection are implemented.
Aurum enumerates existing Windows schemes, identifies the active one, requires
confirmation before switching, saves the original GUID, detects external drift,
and offers repair or rollback. Aurum-owned cloned plans are now used by the
Core Parking workflow rather than modifying built-in schemes.

**Core Parking update:** inspection and advanced opt-in controls are now
implemented. Aurum reads `CPMINCORES` and `CPMAXCORES` for AC/DC, validates the
0–100% ranges, applies them only to a cloned scheme, detects plan/value drift,
and restores the original scheme on rollback. Heterogeneous-core topology
guidance and live parked-core visualization remain planned refinements.

- Enumerate all Windows power plans and identify the active plan.
- Allow selecting an existing plan while remembering the previous plan.
- Create an optional Aurum plan by cloning a Windows plan instead of modifying
  built-in plans in place.
- Expose core-parking minimum and maximum core percentages per AC/DC mode.
- Detect heterogeneous processors and warn when blanket unparking may interfere
  with the Windows scheduler, thermals, or battery life.
- Separate desktop/AC and laptop/battery recommendations.

Core parking must never be presented as a universally beneficial on/off switch.

## 4. Services

**Status:** inventory, dependency analysis, and reversible per-service
disable/revert/repair are implemented. Presets group optional services by
capability. Only declared optional names can be disabled or written from a
snapshot; protected names cannot. Workload-aware batch refusal and live
parked-core visualization remain future work.

- Inventory service state, startup type, dependencies, and dependants.
- Group optional services by capability rather than publish a universal
  "disable unnecessary services" list.
- Explain the affected Windows feature before any change.
- Refuse a batch when a required dependency or current workload is detected.
- Persist original startup type and running state for rollback and drift repair.

Security, update, recovery, networking core, anti-cheat, and hardware-management
services remain outside recommended disable profiles.

## 5. Network diagnostics and tuning

**Status:** the first read-only diagnostics slice is implemented. Aurum lists
network interfaces with operational state, type, negotiated speed, MTU, MAC,
IPv4/IPv6 addresses, gateways, and DNS servers. It reads the localized global
TCP report through the supported `netsh` query and runs a four-request ICMP
latency/loss sample only after an explicit click. Per-adapter RSS/RSC detail,
throughput benchmarks, DNS comparison, and all reversible tuning remain planned.
The default screen now offers only the latency/loss check and explains exactly
what it will do. Adapter and TCP data remain available on demand in the
technical-details view.

- Show adapter, link speed, DNS, MTU, RSS, ECN, receive-window autotuning, and
  relevant offload state.
- Measure latency, packet loss, and throughput before suggesting a change.
- Prefer supported Windows controls over undocumented registry folklore.
- Keep adapter-specific backups because Wi-Fi, Ethernet, VPN, and virtual
  adapters require different policies.
- Clearly separate privacy/DNS preferences from performance tuning.

## Delivery order

1. Read-only hardware inventory and live monitoring. **Initial slice delivered.**
2. Power-plan inventory and reversible selection. **Delivered.**
3. Storage diagnostics and safe retrim workflow. **Initial slice delivered.**
4. Core-parking inspection and advanced opt-in controls. **Initial slice delivered.**
5. Service dependency analyzer. **Read-only slice delivered.**
6. Network diagnostics before any tuning controls. **Read-only slice delivered.**
