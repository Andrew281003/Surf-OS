# Surf Kernel roadmap

## v0.1 — bootable foundation

- Cosmos x86 ISO and hardware loop
- CosmosVFS disk discovery, explicit first-boot FAT32 preparation, and persistent layout
- buffered/disk kernel logging and panic screen
- persisted configuration and non-plaintext local accounts
- authenticated sessions and `--force` protected-operation flow
- registry-based shell with basic file, identity, and power commands

Before declaring v0.1 stable, complete the interactive VM matrix on a disposable disk: first format, reboot persistence, login failure/success, every file command, path traversal boundaries, protected writes, incorrect/correct reauthentication, logout/login, reboot, and shutdown.

## v0.2 — reliability and packages

- recoverable configuration writes and filesystem consistency checks
- stronger Cosmos-supported password KDF and entropy source
- command history and bounded text I/O
- package manifest/version/dependency model and offline repository
- signed package verification design; unsigned developer mode must be explicit
- versioned Surf Kernel SDK boundary for hosted environments

## v0.3 — networking and cloud

- adapter, DHCP/static IP, DNS, TCP/UDP, and HTTP services where supported by Cosmos
- offline-first repository cache and update checks
- SurfCloud and SurfStore clients above the network/package services
- firewall policy only after enforceable packet hooks are available

## Later milestones

- package builder and signed `.sos` environment format
- capability-scoped environment loader and crash containment
- graphics/input services, compositor/window manager, reusable controls, themes
- Terminal, Settings, File Manager, Code Editor, SurfStore, and system-info apps
- supported-hardware test matrix and installer media strategy

Kernel paging, GPT manipulation, secure boot, hardware-backed key storage, strong process isolation, and broad driver support depend on Cosmos capabilities. They must remain explicitly unavailable until implemented and tested.
