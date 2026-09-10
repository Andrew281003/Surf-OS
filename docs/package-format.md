# SurfCloud package format

SurfCloud packages are described by JSON manifests in `surfcloud-seed`. A package includes an identifier, display metadata, version and compatibility information, payload metadata, and an encoded download URL.

Bundled manifests must not contain raw public storage links. Use the corresponding encoded URL field and provide a SHA-256 value when the package manager should verify the downloaded payload.

## Package Builder

Run `builder` to open the local package workspace. The builder scaffolds themes, games, programs, and extensions under `apps/PackageBuilder`, validates their metadata and payload, and exports a `.surfpkg` archive to `apps/PackageBuilder/.exports`.

Every new project includes `.surfignore`. It uses gitignore-style blank lines, `#` comments, `!` negation, directory names, and `*`/`?` wildcards. Matching files are excluded from the optional source snapshot sent during publishing. The installable payload named by `EntryFile` is always packaged, so a C# project can point `EntryFile` at its compiled executable or DLL while excluding `bin`, `obj`, symbols, secrets, and local settings from the source upload.

Each exported archive contains:

- `manifest.json`: a SurfCloud-compatible `StorePackageManifest`, including the payload SHA-256.
- `payload/<file>`: the file to upload and reference from the SurfCloud manifest.

Useful non-interactive commands are `builder new <type> <id>`, `builder list`, `builder validate <id>`, and `builder export <id>`.

## Publishing

`publish <builder-project> [--private|--public]` validates and exports a builder project, creates the filtered source snapshot, and uploads both parts. `publish <file.surfpkg> [--private|--public]` uploads an already-built archive without a source snapshot. Visibility defaults to private.

Configure the uploader outside SurfOS:

- `SURFCLOUD_PUBLISH_URL`: HTTPS endpoint that accepts a multipart POST containing `visibility`, `manifest`, `package`, and (for builder projects) `source`.
- `SURFCLOUD_TOKEN`: optional bearer token issued by the SurfCloud service. Never store it in the project or package.

The endpoint may return JSON containing `message` and an HTTPS `url`. Update publication uses the same package ID with a higher manifest version. SurfOS compares cloud and installed versions at session start and displays one notification for every newly discovered app version. `surf updates` lists all currently available updates.
