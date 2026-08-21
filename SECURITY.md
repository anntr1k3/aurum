# Security policy

## Supported versions

Aurum is pre-release software. Only the latest commit is supported during the
MVP phase.

## Reporting a vulnerability

Please do not open a public issue for a vulnerability that could enable local
privilege escalation, arbitrary command execution, or unsafe registry writes.
Use GitHub private vulnerability reporting once the repository is published.

Include the affected tweak identifier, Windows build, reproduction steps, and
the least-sensitive relevant portion of `%LOCALAPPDATA%\Aurum\state.json`.

## Non-negotiable rules

- No remote scripts or commands are downloaded and executed.
- No tweak may contain arbitrary PowerShell or command-line payloads.
- Registry locations are strongly typed and visible in the interface.
- Original values are captured before the first write.
- Partial application is rolled back in reverse order.
- Repair never replaces the initial rollback snapshot.
- Cleanup is previewed, allowlisted, non-recursive, and rejects changed files.
- Security and policy-related tweaks are explicit, opt-in, strongly typed, and 100% reversible.
- State files must not contain credentials or other secrets.
