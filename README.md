# SurfOS

SurfOS is a retro-style command-line OS simulator built with C# and .NET 9.

It isn’t a real operating system or kernel. Instead, it recreates the feel of one with boot screens, user accounts, a BIOS-style setup, virtual files, packages, system services, recovery tools, music, and a built-in assistant called SurfAI.

## Features

* Retro boot sequence, BIOS, safe mode, recovery, and diagnostics
* User accounts, recovery codes, mail, alarms, calendar, calculator, and to-do list
* Virtual filesystem with commands like `ls`, `cd`, `mkdir`, `cat`, `cp`, and `mv`
* Process and service commands such as `ps`, `top`, `kill`, `service`, and `dmesg`
* Surf Store package manager and SurfCloud support
* Themes, cloud music, playlists, and downloads
* SurfAI for package help, troubleshooting, and SurfCloud information
* SurfCode IDE as an optional developer package
* Kernel logs, backups, and crash reports

## Requirements

* Windows 10/11 or macOS 12+
* .NET 9 SDK
* Windows Terminal or the macOS Terminal app recommended

## Build & Run

```powershell
dotnet restore src/SurfOS.Console/SurfOS.csproj
dotnet build src/SurfOS.Console/SurfOS.csproj
dotnet run --project src/SurfOS.Console/SurfOS.csproj
```

You can also use `scripts/Launch-SurfOS.cmd` on Windows or
`scripts/Launch-SurfOS.sh` on macOS for the intended console experience.

On first launch, SurfOS offers **Guided** or **Advanced** setup. Windows creates a
VHDX virtual system disk, formats it as NTFS, and mounts it as a normal drive—preferably
`S:`. macOS uses a normal directory (by default under the user's Application Support
folder) with the same simulated SurfFS capacity controls; it does not create or mount a
disk image.

Advanced setup lets you choose disk sizes from 4 GB to 512 GB, system components, appearance, networking, privacy, power options, and more.

## Useful Commands

```text
help
surfai
surf store
surf search <name>
surf install <package>
surf list
code
music
music cloud
dmesg
service list
backup MyBackup
backup list
```

## SurfCloud

SurfCloud handles packages, themes, and music. It supports downloads, updates, searches, caching, and offline fallback.

Bundled cloud URLs are encoded to keep raw links out of the project files. This is only obfuscation, not a security feature.

## SurfAI

SurfAI currently runs locally and does not use an external AI API.

It can help with packages, themes, connection checks, SurfCloud errors, updates, and general SurfOS troubleshooting.

```text
surfai packages
surfai themes
surfai install neon-tide
surfai online
surfai explain Invalid SHA-256
```

## Distribution

For another PC, use:

```text
Publish-SurfOS.cmd
```

This creates a self-contained Windows x64 ZIP in `dist`, so the destination PC does not need .NET installed.

For Windows ARM:

```text
Publish-SurfOS.cmd win-arm64
```

For a self-contained macOS archive, run:

```sh
./scripts/Publish-SurfOS.sh
```

The script selects `osx-arm64` or `osx-x64` for the current Mac. You can also pass
either runtime explicitly, for example `./scripts/Publish-SurfOS.sh osx-arm64`.

## Project Structure

```text
src/
├── Accounts/
├── Apps/
├── Boot/
├── Cloud/
├── Core/
├── Installer/
├── Media/
├── Packages/
├── Shell/
└── UI/
```

## Contributing

Keep SurfCloud URLs encoded, avoid committing generated files from `bin/`, `obj/`, `.vs/`, or local SurfOS data, and run a build before committing.

## License

There is currently no license file. Add one before treating SurfOS as reusable open-source software.
