# Recovery

SurfOS creates a factory snapshot at `preVersions/Factory-Recovery.surfbak`. User snapshots can be created with `backup <name>` and inspected with `backup list`.

Snapshots exclude `preVersions`, the volatile swap file, and host volume metadata. On
Windows, recovery and uninstall operations validate the configured VHDX before changing
or removing it. On macOS, SurfOS uses and validates its directory-backed installation.
