# SurfOS

SurfOS is a retro console operating-system simulator built in C# and .NET. It boots into a themed terminal environment with accounts, recovery tools, a BIOS-style setup screen, a desktop launcher, a package store, music playback, cloud-aware features, and a local assistant called SurfAI.

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
- Desktop Environment with launchers for built-in apps and installed packages
- SurfCode IDE app under `os-Apps/`
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

On first launch, SurfOS starts the installer and creates its local installation folder. After that, it finds the existing SurfOS install and boots through the boot manager.

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
music
music cloud
startx
dmesg
service list
```

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
Program.cs                    Boot entry and startup flow
CLI_Engine.cs                 Main shell command router
BIOS.cs                       BIOS setup utility
Boot_Manager.cs               Boot menu and diagnostics
CloudRepositoryManager.cs     SurfCloud package manifests and installs
Package_Manager.cs            surf/surfos command implementation
SurfStore.cs                  Interactive package store
SurfAI.cs                     Local SurfOS cloud assistant
MusicPlayer.cs                Music player, cloud library, playlists
ServiceManager.cs             Background service runtime
ProcessManager.cs             Simulated process table
VirtualFileSystem.cs          Virtual filesystem layer
KernelLog.cs                  Kernel-style logging
KernelPanic.cs                Crash screen and crash reports
Desktop_Environment.cs        Console desktop launcher
os-Apps/                      Built-in app code
surfcloud-seed/               Seed packages, themes, and music metadata
```

## Notes For Contributors

- Keep user-facing cloud wording as `SurfCloud`.
- Do not add raw public cloud provider links to code or seed manifests.
- Use `encodedDownloadUrl`, `encodedFileUrl`, or the matching encoded payload field for bundled cloud data.
- Run `dotnet build --no-restore` before committing when dependencies are already restored.
- Avoid committing local install output from `bin/`, `obj/`, `.vs/`, or generated SurfOS user data.

## License

No license file is included yet. Add one before treating this as open-source reusable code.
