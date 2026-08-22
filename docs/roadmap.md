# Aurum roadmap

This roadmap records the intended product direction. Items are not considered
implemented until they are present in the application and covered by checks.

## Current baseline

- Reversible HKCU tweak engine with drift detection, repair, and a local audit log.
- Elevation warning and restart-as-administrator before writes.
- Service, MSI, power, Core Parking, DNS, and storage mutations with rollback snapshots.
- Offline AtlasOS structure checks and preview-first cleanup.
- Hardware monitoring slice and a published single-file Windows build (`v1.0.1`).

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

Hibernation, SysMain, 8.3 names, and last-access timestamps can already be
toggled with rollback snapshots. Optimize Drives history, SMART/health data,
and page-file/indexing diagnostics remain planned.

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
snapshot; protected names cannot. A batch disable is refused when a running
service outside the batch still depends on a target. Workload heuristics beyond
live reverse-dependants remain future work.

- Inventory service state, startup type, dependencies, and dependants.
- Group optional services by capability rather than publish a universal
  "disable unnecessary services" list.
- Explain the affected Windows feature before any change.
- Refuse a batch when a required dependency or current workload is detected.
- Persist original startup type and running state for rollback and drift repair.

Security, update, recovery, networking core, anti-cheat, and hardware-management
services remain outside recommended disable profiles.

## 5. Network diagnostics and tuning

**Status:** adapter inventory, an explicit ICMP probe, reversible DNS presets,
DNS flush, and TCP auto-tuning/ECN writes are implemented. Revert refuses a DNS
snapshot when the live adapter id no longer matches. Per-adapter RSS/RSC detail,
throughput benchmarks, and DNS comparison remain planned.

- Show adapter, link speed, DNS, MTU, RSS, ECN, receive-window autotuning, and
  relevant offload state.
- Measure latency, packet loss, and throughput before suggesting a change.
- Prefer supported Windows controls over undocumented registry folklore.
- Keep adapter-specific backups because Wi-Fi, Ethernet, VPN, and virtual
  adapters require different policies.
- Clearly separate privacy/DNS preferences from performance tuning.

## Delivery order

Shipped through `v1.0.1`: monitoring, power plans, Core Parking, storage retrim,
service mutations, network diagnostics and DNS/TCP tuning, MSI, system timer,
audit log for tweaks, snapshot allowlists.

Next:

1. Hygiene around the released product: crash-log open, audit remaining managers,
   canonical site URLs, Node 24 GitHub Actions.
2. Trust on existing write paths: service batch dependants, exclusive cleanup
   deletes, DNS adapter-id matching.
3. Site delivery: OG cover, font subset, tag-driven releases.
4. No new tweak categories until the items above stay green.
