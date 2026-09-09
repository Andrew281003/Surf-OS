# SurfOS architecture

SurfOS uses feature-oriented clean architecture inside one executable assembly. Keeping a single assembly preserves the existing internal APIs while the codebase is migrated toward explicit interfaces and independently testable libraries.

## Areas

- `SurfOS.Core` coordinates application startup, shared events, exceptions, interfaces, and cross-cutting models.
- `SurfOS.Domain` contains the operating-system concepts: accounts, files, packages, processes, services, settings, and themes.
- `SurfOS.Infrastructure` integrates with Windows, VHDX storage, persistence, networking, cloud services, audio, and security.
- `SurfOS.Features` implements complete user-facing workflows such as installation, recovery, the shell, Store, SurfAI, and SurfCode.
- `SurfOS.Console` owns the executable entry point, menus, commands, and terminal rendering.
- `SurfOS.Tests` mirrors important behavior by subsystem.

## Dependency direction

New code should prefer `Console -> Features -> Core/Domain`, with Infrastructure implementing interfaces declared by Core or Domain. Existing static dependencies will be migrated incrementally to avoid behavior-changing rewrites.

