# SurfOS Windows edition

A retro command-line operating-system simulator for Windows, built with C# and .NET 9. This branch contains the console application, its shared source, tests, Windows launch and publish scripts, and package seed data.

## Run

```powershell
dotnet run --project src/SurfOS.Console/SurfOS.csproj
```

## Test

```powershell
dotnet test src/SurfOS.Tests/SurfOS.Tests.csproj
```

See [command reference](docs/command-reference.md) and [architecture](docs/architecture.md).
