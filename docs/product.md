# Aurum product brief

## Positioning

Aurum is an open-source post-install companion for AtlasOS, not an AtlasOS
installer or replacement playbook. It helps people inspect, apply, and reverse
optional Windows preferences after the base system is installed.

Aurum's defining position is that there are no secret Windows performance
switches. It rejects experimental scheduler, timer, service, registry, and
network recipes whose effects cannot be explained, measured, and safely
reversed. The product should help a user reach the practical potential of their
hardware through supported mechanisms: current drivers, correct power and
thermal behavior, controlled background load, and settings chosen for the
actual machine. It must not turn that principle into a universal percentage,
FPS, frame-time, or input-latency guarantee.

The primary interaction is a clearly named action with a plain-language answer
to three questions: what it does, when it is useful, and what it can affect. A
user should normally need only to decide whether to press the button. Raw device
properties, command previews, dependency graphs, and protocol details belong to
an explicitly opened technical layer and must not dominate the default screen.

## Users

- AtlasOS users who want optional gaming and usability preferences.
- Windows enthusiasts who want to understand every modification.
- Maintainers who need versioned, reviewable tweak definitions.

## MVP success criteria

- A user can understand a tweak before applying it.
- A failed multi-value tweak leaves the system unchanged.
- A successfully applied tweak can restore its exact original state.
- Drift is visible after reopening the app and repair does not destroy rollback data.
- AtlasOS health can be checked without network access or executing Atlas scripts.
- Cleanup has preview, explicit confirmation, allowlisted roots, and race checks.
- The app works without network access or telemetry.
- The source builds without third-party runtime dependencies.

## Explicit non-goals

- Guaranteed FPS or latency improvements.
- A universal percentage-of-maximum-performance claim.
- Disabling Windows security and recovery features.
- Running community-provided scripts.
- Replacing AtlasOS Playbooks or AME Wizard.
- Cleaning the registry, RAM, or other placebo optimization.

## Next milestones

1. MVP: registry tweaks, persistent health, Atlas checks, safe cleanup, individual rollback.
2. Preview: expand the delivered hardware, power-plan, and storage diagnostics, then add an audit log.
3. Beta: signed manifests, expand the delivered core-parking, service, and network analyzers.
4. Stable: signed releases, reproducible builds, migration guarantees.

The detailed feature backlog and safety constraints are maintained in
[`roadmap.md`](roadmap.md).
