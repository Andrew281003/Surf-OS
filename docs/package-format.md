# SurfCloud package format

SurfCloud packages are described by JSON manifests in `assets/surfcloud`. A package includes an identifier, display metadata, version and compatibility information, payload metadata, and an encoded download URL.

Bundled manifests must not contain raw public storage links. Use the corresponding encoded URL field and provide a SHA-256 value when the package manager should verify the downloaded payload.

## Package Builder

Run `builder` to open the local package workspace. The builder scaffolds themes, games, programs, and extensions under `apps/PackageBuilder`, validates their metadata and payload, and exports a `.surfpkg` archive to `apps/PackageBuilder/.exports`.

Each exported archive contains:

- `manifest.json`: a SurfCloud-compatible `StorePackageManifest`, including the payload SHA-256.
- `payload/<file>`: the file to upload and reference from the SurfCloud manifest.

Useful non-interactive commands are `builder new <type> <id>`, `builder list`, `builder validate <id>`, and `builder export <id>`.
