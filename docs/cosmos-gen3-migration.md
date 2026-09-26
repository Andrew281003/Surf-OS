# Cosmos Gen3 migration

`src/SurfOS.Cosmos` now uses the Cosmos Gen3 SDK (`3.0.82`) and targets .NET 10.
This project is the future bare-metal kernel path; it is separate from the normal macOS
console simulator in `src/SurfOS.Console`.

## Toolchain

Install the Cosmos tools once, then confirm the environment from a new terminal:

```sh
dotnet tool install -g Cosmos.Tools
cosmos install
cosmos check
```

Build the kernel from its project directory:

```sh
cd src/SurfOS.Cosmos
cosmos build
```

When the migration is complete, `cosmos run` will boot the generated ISO in QEMU.

## Current migration status

The project file has been upgraded from the old local Gen2 packages to:

```xml
<Project Sdk="Cosmos.Sdk/3.0.82">
  <TargetFramework>net10.0</TargetFramework>
  <PackageReference Include="Cosmos.Kernel" Version="3.0.82" />
  <PackageReference Include="Cosmos.Kernel.System" Version="3.0.82" />
</Project>
```

The source still contains Gen2 API usage and therefore does not build yet. The main areas
to port are:

- `Kernel.cs` and the boot/power code, which use the former `Cosmos.System` and
  `Cosmos.Core` APIs.
- `Storage/CosmosFileSystem.cs`, which is built around Gen2 `CosmosVFS`,
  `ManagedPartition`, and filesystem classes.
- Any direct hardware calls, which must use Gen3 kernel and HAL APIs instead.

Port these areas incrementally: begin with a minimal Gen3 kernel that prints to the console,
then add boot logic, then storage, and finally the remaining shell and feature layers. Keep
the macOS console app runnable throughout; it does not depend on the Cosmos project.
