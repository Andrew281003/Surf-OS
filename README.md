# SurfOS

SurfOS is a retro CLI operating-system simulator built in C# and .NET. It boots into a themed terminal environment with accounts, recovery tools, a BIOS-style setup screen, a package store, music playback, cloud-aware features, and a local assistant called SurfAI.

It is a playground OS, not a real kernel. The goal is to make the terminal feel alive: boot screens, services, package installs, virtual files, system logs, crash reports, themes, and little apps all working together in one executable.

## Highlights

- Retro boot sequence, BIOS setup, safe mode, recovery mode, and diagnostics
- User login, recovery code support, local mailbox, to-do list, calculator, clock, calendar, and alarms
- Virtual file system commands: `pwd`, `ls`, `cd`, `mkdir`, `touch`, `cat`, `rm`, `cp`, and `mv`
- Process and service tools: `ps`, `top`, `kill`, `service`, and `dmesg`
- Surf Store package browser plus `surf` / `surfos` package commands
- SurfCloud package/theme/music manifest support with local cache fallback
- SurfAI local assistant for SurfCloud help, package lists, theme lists, online checks, and error explanations
- SurfOS Music Player with local playback, cloud music listing/downloads, and playlists
- SurfCode IDE available as an installable SurfCloud developer tool
- Kernel log and panic report system for easier debugging

## Requirements

- Windows
- .NET 9 SDK
- A terminal that can run a Windows console app

The project targets `net9.0` and uses Windows-specific console features, so Windows is the expected runtime.

## Build And Run

Clone the repo, then from the project folder:

```powershell
dotnet restore
dotnet build
dotnet run
```

On first launch, SurfOS offers two installation experiences. Guided Setup asks for identity, region, location, appearance, and optional SurfCloud identity details while applying recommended system defaults. Advanced Setup exposes account security, region and input, networking, privacy, updates, power, optional components, appearance, and storage controls. The selected mode and every resolved setting are saved in `options.json`.

The installer creates a fixed-size VHDX system disk with a 15 GB default capacity. Advanced users can choose 8, 15, 32, or 64 GB, or enter a custom size from 4 to 512 GB (subject to host free space). Windows formats the VHDX as NTFS, labels it `SURFOS`, and mounts it under an available drive letter—preferably `S:`—so it appears as `SurfOS (S:)` in This PC. SurfOS installs its system files and folders directly at the drive root (`S:\`) while the backing VHDX safely consumes the selected amount of storage from its host drive without modifying the physical Windows partition table.

An administrator-level startup task remounts the VHDX after Windows restarts. SurfOS also verifies and remounts the disk during application startup as a fallback. The `sudo uninstall` flow verifies the backing image, removes the startup task, detaches the drive, and deletes the VHDX. A first-boot preparation sequence runs after the installation files and selected components are written.

### Windows Console Host

For the most compatible interactive console experience, run [`Launch-SurfOS.cmd`](Launch-SurfOS.cmd). It builds SurfOS if needed and starts the compiled executable in a separate Windows console session.

If Windows opens that session in Windows Terminal instead, open **Settings > Privacy & security > For developers > Terminal** and select **Windows Console Host** as the default terminal application. Windows controls the console host choice; SurfOS remains a standard Windows console application.

## Distributing To Another PC

Do not copy the development `SurfOS2.exe` by itself. Normal Debug and Release builds are framework-dependent and require their adjacent `.dll`, `.json`, and runtime files plus .NET 9 on the destination computer.

Run `Publish-SurfOS.cmd` on the development PC to create a self-contained Windows x64 ZIP under `dist`. Extract the complete ZIP on the destination PC and launch `Launch-SurfOS.cmd`; the destination computer does not need the .NET runtime installed. For Windows on ARM, run `Publish-SurfOS.cmd win-arm64` instead.

If startup still fails, the launcher keeps the window open and reports the exit code. Managed startup failures are also written to `%LOCALAPPDATA%\SurfOS\startup-crash.log`, with early kernel messages under `%LOCALAPPDATA%\SurfOS\logs\kernel.log`.

## Main Commands

Inside SurfOS, run:

```text
help
```

Useful starting points:

```text
surfai
surf store
surf search <name>
surf install <package>
surf list
surf install surfcode-ide
code
music
music cloud
dmesg
service list
backup MyBackup
backup list
```

## System Footprint And Recovery

Advanced Setup offers Compact, Standard, and Complete system footprints when the selected partition has enough capacity. Guided Setup uses Standard by default. These profiles provision actual files for virtual memory, the SurfOS component store, an offline package cache, language metadata, and the font catalog. Standard uses roughly 5 GB after its factory recovery snapshot; the exact amount depends on the selected swap size.

The installer creates `preVersions\Factory-Recovery.surfbak` as an uncompressed factory snapshot. Use `backup <name>` to create another snapshot or `backup list` to inspect existing backups. Snapshots contain the SurfOS partition files but exclude `preVersions` itself, the volatile swap file, and Windows volume metadata. Excluding `preVersions` prevents a backup from recursively archiving itself and every older backup.

## SurfCloud

SurfCloud is SurfOS's cloud package and media layer. It supports:

- Package manifest refresh and local cache fallback
- Package install/remove/update/search/info commands
- Theme packages
- Cloud music metadata and downloads
- Encoded cloud URLs in bundled manifests

SurfCloud link fields are intentionally stored as encoded values in the seed data and decoded at runtime. This hides raw public storage links from casual browsing. It is obfuscation, not a full security boundary; a production version should use signed URLs or a small backend service.

## SurfAI

SurfAI is local-only right now. It does not call an AI API.

It answers from SurfOS data and built-in rules, including:

- Installing packages from SurfCloud
- Explaining cloud download errors
- Checking whether SurfOS appears online or offline
- Listing available cloud packages
- Listing available cloud themes
- Explaining update errors
- Helping with SurfCloud public-link issues

Try:

```text
surfai packages
surfai themes
surfai install neon-tide
surfai online
surfai explain Invalid SHA-256
surfai surfcloud public link
```

## Project Layout

```text
src/
├── Accounts/                 Login and account management
├── Apps/                     SurfCode and installable app implementations
├── Boot/                     BIOS and boot manager
├── Cloud/                    SurfCloud, package manager, store, and SurfAI
├── Core/                     Entry point, shared state, storage, logging, and panic handling
├── Installer/                Setup, VHDX, recovery, backup, and footprint provisioning
├── Media/                    Music player and playlists
├── Packages/                 Built-in package and theme definitions
├── Shell/                    CLI, virtual filesystem, processes, services, and time tools
└── UI/                       Console rendering and animation helpers

surfcloud-seed/               Seed packages, themes, and music metadata
Properties/                   Windows project metadata
app.manifest                  Windows privileges and application manifest
SurfOS2.csproj                .NET project configuration
Launch-SurfOS.cmd             Local launcher
Publish-SurfOS.cmd            Self-contained distribution builder
```

## Notes For Contributors

- Keep user-facing cloud wording as `SurfCloud`.
- Do not add raw public cloud provider links to code or seed manifests.
- Use `encodedDownloadUrl`, `encodedFileUrl`, or the matching encoded payload field for bundled cloud data.
- Run `dotnet build --no-restore` before committing when dependencies are already restored.
- Avoid committing local install output from `bin/`, `obj/`, `.vs/`, or generated SurfOS user data.

## License

No license file is included yet. Add one before treating this as open-source reusable code.
