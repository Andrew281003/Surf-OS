# SurfCloud repository

The local development repository is stored in `surfcloud-seed` and contains package, theme, and music manifests plus seed media. SurfOS refreshes remote manifests when available and uses its local cache when offline.

Encoded links are obfuscation rather than an authorization boundary. A production repository should distribute short-lived signed URLs through a backend service.

App publishing is performed through the HTTPS endpoint in `SURFCLOUD_PUBLISH_URL`, with an optional bearer credential in `SURFCLOUD_TOKEN`. New uploads default to private. The API receives a validated `.surfpkg`, its manifest, visibility, and an optional `.surfignore`-filtered source snapshot. Private package authorization and signed download links must be enforced by that service; visibility is not implemented by obscuring a storage URL.
