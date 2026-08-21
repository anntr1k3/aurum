# Architecture

## Components

```text
Aurum.App (WPF)
    |
    +-- Aurum.Core
    |     tweak definitions, evaluation, transactions, rollback
    |
    +-- Aurum.Infrastructure.Windows
          Windows registry adapter, JSON state repository, system diagnostics
```

The core has no dependency on WPF or the Windows Registry API. System access is
behind `ISystemStore`, which makes transactional behavior testable without
changing the machine.

## Apply transaction

1. Reject an unknown or already tracked tweak.
2. Read and retain every original value.
3. Apply mutations in declaration order.
4. If a write fails, restore completed mutations in reverse order.
5. Persist the original snapshot only after all writes succeed.

## Revert transaction

1. Load the original snapshot from the local repository.
2. Restore values in reverse order.
3. Delete the snapshot only after every restoration succeeds.

If the revert itself fails, the snapshot is retained so the user can retry.

## Drift and repair

Every tracked tweak is evaluated against live registry values at startup and on
demand. If one or more desired values differ, the tweak becomes `Drifted`.
`RepairAsync` captures the immediate pre-repair state for transactional failure
recovery, but deliberately retains the original first-apply snapshot as the
authoritative user rollback point.

## Power-plan transaction

Power-plan selection uses a separate transaction boundary. Aurum records the
active plan before switching, activates an existing Windows plan through the
Power API, and then persists the original and desired GUIDs. If persistence
fails, the original plan is restored immediately. External plan changes are
reported as drift; repair returns to the desired plan without replacing the
original rollback point. Built-in plan parameters are never edited.

## Core Parking isolation

Core Parking is an advanced opt-in transaction. Aurum duplicates the active
scheme, gives the clone an Aurum-owned name, writes only the documented minimum
and maximum unparked-processor percentages for AC and DC, activates the clone,
and persists both GUIDs. Failure during creation restores the original plan and
removes the incomplete clone. Revert activates the original before deleting the
managed clone. The regular power-plan tracker and Core Parking tracker are
mutually exclusive to prevent competing rollback owners.

## Service analysis boundary

Service inventory requests query-only access from the Service Control Manager.
Aurum reads live process state, startup configuration, delayed-auto-start flags,
descriptions, and direct dependencies, then builds reverse dependants in memory.
Classification is allowlist-based: critical names are protected, a small set of
feature services is context-dependent, and everything else remains unclassified.
This slice contains no start, stop, or configuration-changing service handles.

## Network diagnostics boundary

Adapter inventory uses `System.Net.NetworkInformation` and does not open a
configuration session. Global TCP state is parsed from the read-only `netsh
interface tcp show global` report so the application does not require elevation
merely to inspect it. Latency measurement accepts only validated IP addresses or
DNS host names and calls the .NET ICMP API directly; no shell receives user
input. DNS, MTU, RSS, RSC, offload, and TCP values are never written in this
slice.

## AtlasOS health

The AtlasOS checker is read-only and offline. It validates independent evidence:

- `%WINDIR%\AtlasModules` and `%WINDIR%\AtlasDesktop`;
- the scripts directory;
- Atlas version OEM metadata;
- the v0.5 component-state registry branch;
- selected Atlas utility SHA-256 values published upstream.

No single missing marker is treated as proof that the entire system is invalid.
Hash checks are versioned evidence and must be updated from the official AtlasOS
repository when upstream binaries change.

## Cleanup boundary

Cleanup is a two-phase operation. Scanning produces immutable candidates with
path, size, and last-write timestamp. Execution verifies that every path remains
inside its declared allowlisted root and that size and timestamp are unchanged.
Reparse points and inaccessible files are skipped. Only files are deleted; Aurum
does not recursively delete directories.

## Storage boundary

Volume inventory uses read-only Windows storage property queries, PnP device
metadata as a least-privilege fallback, and a read-only `fsutil` query for file
system delete notifications. Analyze and ReTrim are allowlisted operations over
an inventory-selected local drive-letter volume. Aurum passes arguments directly
to `defrag.exe` without a command shell and never offers ReTrim for confirmed
rotational media, unsupported file systems, disabled delete notifications, or a
device that has not confirmed TRIM support. Administrator access must already be
present; there is no silent or automatic elevation.

## Trust boundaries

Built-in definitions are trusted code reviewed with the application. A future
manifest format must be schema-validated, signed, version constrained, and
limited to an allowlist of reversible operation types. Arbitrary commands are
not a valid operation type.
