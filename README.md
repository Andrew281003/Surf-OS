# SurfOS

**SurfOS is a retro command-line operating-system simulator, built with C# and .NET 9.**
It models the experience of booting, configuring, and using an OS; it is not a real kernel
or a replacement for macOS, Windows, or Linux.

## What you can do

- Start a simulated boot sequence and configure a virtual system.
- Work with accounts, a virtual filesystem, services, processes, recovery tools, and backups.
- Install packages, themes, music, and developer tooling through Surf Store and SurfCloud.
- Use SurfAI for local help and troubleshooting—no external AI API is required.

## Quick start (macOS)

**Requires:** macOS 12+, the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0), and a terminal.

```sh
git clone --branch 'SurfOS(MacOS)' https://github.com/Andrew281003/Surf-OS.git
cd Surf-OS
./scripts/Launch-SurfOS.sh
```

Or run it directly with .NET:

```sh
dotnet run --project src/SurfOS.Console/SurfOS.csproj
```

At first launch, choose **Guided** setup for the fastest path or **Advanced** setup to
configure the simulated disk, services, appearance, networking, privacy, and power options.
On macOS, SurfOS stores its virtual filesystem in a regular application-support directory;
it does not create or mount a disk image.

## Try these commands

```text
help
ls
surf store
surf search <name>
surf install <package>
music
surfai packages
dmesg
service list
backup MyBackup
```

See the full [command reference](docs/command-reference.md).

## Build, test, and package

```sh
dotnet restore src/SurfOS.Console/SurfOS.csproj
dotnet build src/SurfOS.Console/SurfOS.csproj --no-restore
dotnet test src/SurfOS.Tests/SurfOS.Tests.csproj
./scripts/Publish-SurfOS.sh
```

`SurfOS.Cosmos` is an experimental Cosmos-based project and currently depends on packages
that are not published on NuGet. It is included in the solution for development, but is not
part of the normal macOS build path.

`Publish-SurfOS.sh` creates a self-contained macOS archive in `dist/`. It selects the
current Mac architecture automatically, or accepts `osx-arm64` or `osx-x64` explicitly.

## Repository map

```text
src/
├── SurfOS.Console/        # Executable entry point and console presentation
├── SurfOS.Cosmos/         # Boot, kernel, shell, installer, storage, and runtime
├── SurfOS.Core/           # Shared application primitives and events
├── SurfOS.Domain/         # Domain services and virtual filesystem models
├── SurfOS.Features/       # Optional user-facing features and commands
├── SurfOS.Infrastructure/ # Persistence, cloud, platform, and system integrations
├── SurfOS.Tests/          # Automated tests
└── SurfCloud.DrivePublisher/ # Publishes SurfCloud data to Google Drive

scripts/                   # Launch, publish, and development helper scripts
docs/                      # Architecture, recovery, package, and SurfCloud documentation
surfcloud-seed/            # Local package, theme, and music seed data
```

## Key documentation

- [Architecture](docs/architecture.md)
- [Recovery](docs/recovery.md)
- [Package format](docs/package-format.md)
- [SurfCloud repository](docs/surfcloud-repository.md)
- [Google Drive setup](docs/surfcloud-google-drive-setup.md)

## Platform notes

This branch is the **macOS** version. The shared application model lives in the source tree;
platform-specific launch and distribution behavior belongs in `scripts/` and infrastructure
code. The Windows branch is maintained separately.

## Contributing

Before opening a change, build the solution and run the tests. Do not commit generated
`bin/`, `obj/`, `dist/`, IDE metadata, or local SurfOS data. Keep secrets and credentials
out of the repository.

## License

No license has been chosen yet. Until one is added, do not treat this project as reusable
open-source software.
