# SurfOS Cosmos architecture

## Milestone boundary

Surf Kernel v0.1 is a real x86 Cosmos kernel. `Kernel.cs` contains only the Cosmos lifecycle bridge and panic boundary. `BootCoordinator` is the composition root; the runtime and shell depend on narrow service interfaces rather than Cosmos internals.

```text
PC / virtual hardware
        |
Cosmos HAL + CosmosVFS
        |
Surf Kernel boot, logging, permissions, power
        |
configuration, users, sessions, filesystem
        |
command registry + shell
        |
future SurfOS environment / other .sos environments
```

The Windows console project remains a legacy prototype and migration source. It is not used by `SurfOS.Cosmos` and is not included in the ISO.

## Boot lifecycle

1. Cosmos transfers control to `SurfOS.Kernel`.
2. `BootCoordinator` starts buffered logging and reports hardware console readiness.
3. `CosmosFileSystem` registers `CosmosVFS` once and detects mounted FAT volumes.
4. If no volume exists, `StorageInstaller` can erase one explicitly selected 33 MB–128 GB disk, create a nearly full-disk MBR/FAT32 partition, and mount it. SurfOS uses one-sector clusters to avoid a directory-allocation defect in the pinned Cosmos 2022 FAT implementation. It never formats without the typed `ERASE <index>` confirmation.
5. The required filesystem layout is created.
6. Logging switches from memory to `/Logs/kernel.log`.
7. First boot creates a device configuration and administrator account.
8. Configuration, users, and the authenticated session are loaded.
9. The command registry and shell start.

An exception escaping boot or the runtime enters a visible kernel panic and halts the CPU. Ordinary command errors are caught at the shell boundary and logged without stopping the OS.

## Storage and paths

The user-facing root `/` maps to the selected Cosmos volume (normally `0:\`). Commands never call `System.IO` directly. They use `IFileSystemService`, which resolves `.`, `..`, absolute Surf paths, and Cosmos drive paths without allowing traversal above the mounted root.

The v0.1 layout is:

```text
/
├── System/
├── Users/
├── Apps/
├── Packages/
├── Config/
├── Logs/
└── Temp/
```

Cosmos 2022 has FAT-oriented limitations. Short ASCII names are the safest choice, writes are not transactional, and FAT provides no native ownership or ACLs. SurfOS therefore enforces protected paths through `PermissionManager` before the filesystem operation. This is kernel policy, not a claim of hardware-backed isolation.

## Authentication and `--force`

Account records contain an ID, role, home directory, per-account salt, and a versioned iterated verifier; plaintext passwords are never saved. The current hash is deliberately isolated behind `IPasswordHasher`. Cosmos 2022 does not expose a supported cryptographic random generator in this build, so v0.1 credentials are better than plaintext but are not production-strength. A future CSPRNG/KDF implementation can replace version `v1` without changing callers.

For protected paths and power operations the flow is:

```text
command -> protected-operation detection -> administrator role -> --force -> password prompt -> operation
```

`--force` does not add permissions and does not bypass authentication. The filesystem root cannot be removed at all.

## Platform evolution

The kernel/platform boundary should eventually become a versioned Surf Kernel SDK containing capability-scoped filesystem, display, input, network, account, package, process, and lifecycle contracts. SurfOS, recovery tools, and custom environments should consume only that SDK. Package loading, signature validation, permission grants, and process isolation must be implemented below the environment boundary before third-party `.sos` environments are considered safe.

Networking, SurfCloud, packages, GUI, paging, and application loading are intentionally absent from v0.1. No command reports those services as active. Their future implementations belong behind dedicated service interfaces and must not be added to `Kernel.cs`.
