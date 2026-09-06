# Recovery

SurfOS creates a factory snapshot at `preVersions/Factory-Recovery.surfbak`. User snapshots can be created with `backup <name>` and inspected with `backup list`.

Snapshots exclude `preVersions`, the volatile swap file, and Windows volume metadata. Recovery and uninstall operations validate the configured VHDX before changing or removing it.

