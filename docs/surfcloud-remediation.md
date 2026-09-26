# SurfCloud remediation — 2026-09-25

This report tracks the findings in `docs/surfcloud-audit.md`. Work was performed on `codex/surfcloud-remediation`. Pre-existing Cosmos, avatar, launcher, and audit working-tree changes were preserved. No production credential, Google Cloud resource, or Drive file was accessed during local verification.

## Finding status

| Finding | Status | Local result and remaining boundary |
|---|---|---|
| F0 service-account key | **BLOCKED** | Build, Docker context, Git, and CI containment added. The ignored owner key remains untouched. Its validity, historical exposure, IAM, and rotation require owner action. |
| F1 privileged desktop Firestore | **PARTIAL** | Desktop Firestore client and package dependency removed; presence and operation status use backend endpoints scoped to a validated Google subject. External ID-token acquisition remains necessary; no interactive OAuth flow or emulator two-account test exists yet. |
| F2 unvalidated uploads | **PARTIAL** | Server validates bounded `.surfpkg` and optional source ZIPs before reservation or Drive upload, and persists verified archive digest and embedded metadata. Malformed-archive tests pass. End-to-end publisher tests with Drive/Firestore mocks or emulators remain outstanding. |
| F3 unsigned distribution | **PARTIAL** | Ed25519 signed catalog envelope, embedded production public-key lookup, sequence rollback check, signed cache, and explicit third-party launch permission implemented. No production keys are provisioned, so remote catalog installations fail closed. A curated catalog file and an authorized package download route still need production design and testing. |
| F4 Drive orphan on second upload failure | **PARTIAL** | Each returned Drive ID is tracked immediately, independently cleaned on failure, and tagged with an operation ID for later Drive search. A mocked cleanup failure test passes. Full partial-upload and process-crash fault injection is not complete. |
| F5 retry/uncertain outcomes | **PARTIAL** | Subject-scoped durable operation states, deterministic idempotency keys, status lookup, leases, expiration reconciliation, token caching, and bounded transient retries added. Firestore emulator and mocked Drive fault tests for timeouts, concurrency, and crash recovery remain outstanding. |
| F6 shared local account state | **PARTIAL** | Only verified token subjects reach backend presence and operation status; client private session token/subject are cleared on sign-out. Current package installations and signed public catalog cache remain **system-wide**. No private package read route or private cache exists; per-user installations and host ACLs must be designed before private downloads are enabled. |

No finding is declared production verified. Firestore rules remain deny-all for direct clients.

## Changes by phase

### Phase 0 — credential containment

- `.gitignore` and `.dockerignore` exclude credential-like files, local development keys, build trees, and local environment files. The publisher Dockerfile's `COPY . .` now receives a filtered context.
- `src/SurfOS.Console/SurfOS.csproj` excludes credential files from default project items, removes the stale delete-on-build mechanism, and fails a build or publish if credential-like output exists. `scripts/Publish-SurfOS.cmd` fails instead of deleting a credential file.
- `scripts/check-secret-artifacts.py` scans tracked files and optional output directories for private-key markers, service-account JSON structure, and credential filenames. `.github/workflows/secret-artifacts.yml` runs it before and after a desktop publish. The scanner reports paths/categories only.
- The owner's ignored `src/SurfOS.Console/secrets.json` was not opened, changed, copied, or deleted. The development Ed25519 key pair is separately ignored in `.local/catalog-dev-keys/`; it is not a production key.

### Phase 1 — authentication and isolation

- `src/SurfOS.Infrastructure/Cloud/Cloud_Manager.cs`, `src/SurfOS.Domain/Services/ServiceManager.cs`, `src/SurfOS.Features/Installer/Install_Setup.cs`, and `src/SurfOS.Features/Shell/AdditionalCommands.cs` remove desktop Firestore ADC use and locally asserted cloud sign-in. `surfcloud signin` reads an externally acquired ID token from `SURFCLOUD_TOKEN`, verifies it through `/session`, and keeps it only in process memory. `surfcloud signout` clears the session and suppresses automatic re-sign-in until an explicit sign-in. Publishing requires this verified in-memory session, so a token remaining in the environment cannot authorize a new publish after sign-out. Offline presence is omitted.
- `src/SurfCloud.DrivePublisher/Program.cs` adds `/session`, `/presence`, and subject-scoped `/operations/{id}`. Every route validates a Google ID token with the configured audience and checks issuer, expiration, and nonempty subject. Presence and operation lookup derive their Firestore path from that subject, never from a request account name.
- No private package read, delete, or synchronization endpoint is present. Current package installation state is system-wide; the signed catalog cache contains public metadata only.

### Phase 2 — package validation

- `src/SurfCloud.DrivePublisher/PackageArchiveValidator.cs` checks ZIP magic, compressed and expanded sizes, entry counts, compression ratio, paths, duplicates, Unix entry types, embedded manifest fields, submitted/embedded identity, own-app install path, payload name, and payload SHA-256. Source ZIPs have separate bounds and reject credential-like names/content.
- `src/SurfCloud.DrivePublisher/Program.cs` validates before quota reservation, stores the SHA-256 of the entire accepted archive plus verified manifest fields, and records the verified publisher subject.
- `src/SurfCloud.DrivePublisher.Tests/Program.cs` tests valid output and malformed traversal, duplicate, symlink, hash, manifest mismatch, compression, source, and unsupported-format inputs.

### Phase 3 — trusted distribution

- `src/SurfCloud.DrivePublisher/CatalogSigning.cs` signs the sequence and base64 catalog payload with Ed25519. `/catalog` reads an operator-curated manifest file and a private signing seed from an external file path. It does not publish a development key automatically.
- `src/SurfOS.Infrastructure/Cloud/CatalogTrust.cs` verifies Ed25519 using only public keys embedded in the SurfOS binary as `Trust/catalog-public-prod-<key-id>.pub`. There are no production pins now, so remote catalogs are rejected. `src/SurfOS.Infrastructure/Cloud/CloudRepositoryManager.cs` verifies both downloaded and cached envelopes, persists the highest accepted sequence, and accepts remote install metadata only from a verified catalog. It checks the full archive digest, ZIP structure, embedded manifest against signed execution metadata, and extracted payload digest before installing. Bundled seed packages and URL-free local entries remain available.
- `src/SurfOS.Features/Shell/AppCommands.cs` executes the exact trusted built-in `surfcode-ide` command normally. Any third-party launch command needs `app --open <id> --allow-third-party`, which grants the command the user's existing SurfOS permissions. This is an explicit permission step, **not a sandbox**.
- `scripts/generate-dev-catalog-key.py` created an ignored local Ed25519 development pair. `scripts/test-dev-catalog-key.py` tests it without printing key material. Development public keys are excluded from the production embedded-resource pattern.

### Phase 4 — reliable publishing

- `src/SurfCloud.DrivePublisher/Program.cs` records each returned Drive ID before the next upload. `src/SurfCloud.DrivePublisher/DriveCleanup.cs` attempts every deletion independently under a deadline; cleanup errors are logged without replacing the original publish failure.
- The publisher records `reserved`, `uploading`, `committed`, `failed`, `expired`, and `reconciled` operation states under the verified subject. Requests with the same idempotency key and content return the current result or status. Commit checks an unexpired lease. Before cleanup after an ambiguous failure, the server checks whether commit already succeeded.
- Drive uploads carry a private `surfcloudOperation` app property. `src/SurfCloud.DrivePublisher/PublishReconciler.cs` checks expired operations, releases stale reservations, lists tagged Drive objects, and retries cleanup. The client in `src/SurfOS.Infrastructure/Cloud/SurfCloudPublisher.cs` derives the same idempotency key for an identical request and queries status after a timeout.

### Phase 5 — performance and hardening

- `src/SurfCloud.DrivePublisher/HttpRetry.cs` retries selected transient HTTP statuses and connection failures with bounded exponential backoff, jitter, and `Retry-After`. Drive OAuth access tokens are reused until shortly before expiry. Upload payload PUTs are not blindly replayed after an uncertain result.
- `CloudRepositoryManager` reuses an HTTP client and streams bounded downloads to disk. `src/SurfOS.Core/Application/PathSafety.cs` and package installation/removal reject currently visible filesystem links and junctions in package paths. Host-level time-of-check/time-of-use races remain possible when an untrusted local process can mutate installation directories.
- `BouncyCastle.Cryptography 2.7.0` supplies Ed25519 for both .NET 9 projects. On 2026-09-25, `dotnet list ... package --vulnerable --include-transitive` reported no known vulnerable packages from the configured NuGet sources for the desktop and publisher. This is a point-in-time advisory query, not a guarantee. No unrelated package versions were changed.

## Local verification

Run from the repository root. The exact commands below were executed locally:

| Command | Result |
|---|---|
| `dotnet run --project src/SurfOS.Tests/SurfOS.Tests.csproj --no-restore` | PASS; existing regression suite plus explicit third-party permission, sign-out, verified archive extraction, and malformed archive checks. Symlink creation fixture skipped because this host denied it. |
| `dotnet run --project src/SurfCloud.DrivePublisher.Tests/SurfCloud.DrivePublisher.Tests.csproj --no-restore` | PASS; archive rejection, independent cleanup, Ed25519 tamper/rollback/no-pin, and deterministic `Retry-After` checks. |
| `dotnet build src/SurfOS.Console/SurfOS.csproj --no-restore` | PASS, 0 warnings/errors. |
| `dotnet build src/SurfCloud.DrivePublisher/SurfCloud.DrivePublisher.csproj --no-restore` | PASS, 0 warnings/errors. |
| `dotnet publish src/SurfOS.Console/SurfOS.csproj -c Release -o dist/surfos --no-restore` | PASS after correcting the output guard's path glob. |
| Bundled Python `scripts/check-secret-artifacts.py dist/surfos` | PASS; tracked files and published output scanned. The ignored owner key was intentionally not scanned into output. |
| Bundled Python `scripts/test-dev-catalog-key.py` | PASS; ignored local development pair verifies and tampering fails. |
| `dotnet list ... package --vulnerable --include-transitive` for desktop and publisher | PASS; no advisories reported by configured sources at the time checked. |
| `git diff --check` | PASS for changed tracked files; Git emitted line-ending conversion notices. |

The Firebase CLI and Firestore emulator were unavailable. No two-account emulator test, live Google ID-token test, mocked end-to-end Drive/Firestore fault suite, Docker image build, or staging deployment was run. Firestore IAM, Drive ACLs, key status, and cloud logs were not inspected.

### Repeatable local benchmark

Run `dotnet run --project src/SurfCloud.DrivePublisher.Tests/SurfCloud.DrivePublisher.Tests.csproj --no-restore -- --benchmark`. Full results are in `docs/surfcloud-benchmark-local.csv`: 200 operations per workload at 1, 10, 50, and 100 simulated clients, fixed ZIP/catalog/64 KiB download bytes, p50/p95/p99, throughput, error rate, process CPU time, process peak working set, and mock HTTP/Drive counts. This is **C# code with in-process mock transport**, not a Firestore/Drive service benchmark. CPU rows below timer resolution appear as 0 ms; peak working set is process-wide and cumulative. Mock publish excludes Firestore transactions and real Drive transfer latency. No comparable pre-change C# measurements exist, so no before/after speedup is claimed. The old Python surrogate is not used.

## Required owner actions and deployment

### F0 key containment and investigation — blocked pending owner confirmation

1. In a secure local viewer, identify the service-account email and key ID from the ignored credential file **without pasting or logging the private key**. Match that key ID in Google Cloud IAM → Service Accounts → Keys. Confirm whether it is active.
2. Disable the matching key immediately under the owner's incident process. If any legitimate workload still uses it, migrate that workload to a new scoped identity or workload identity, then rotate and delete the old key according to retention policy. Do not use a desktop-distributed service-account credential. The repository work did **not** disable, rotate, or delete it.
3. Search Cloud Audit Logs for uses of that service account/key, unexpected principal/IP/time patterns, Firestore and IAM operations, and access to affected data over the available retention period. Check Git history, release archives, Docker build caches, backups, and shared artifacts for copies. Preserve investigation evidence under the owner's policy.
4. Review least-privilege IAM. The Cloud Run runtime should have only required Firestore permissions and the configured Drive OAuth scope; remove broad Owner/Editor grants and unused keys. Revoke/rotate any additional credential found exposed.
5. After owner confirmation, remove local copies securely under the owner's retention procedure. Re-run the tracked-file/output scanner and a Docker build-context check before release.

### Ed25519 production key provisioning — not yet done

1. Generate a **new production** Ed25519 32-byte seed in an approved key-management process. Do not use `.local/catalog-dev-keys/` or its private key. Keep the base64-encoded seed in a protected file or secret store **outside the repository**. Restrict read access to the signing service identity and audit access to it.
2. Derive the matching raw 32-byte public key, encode it as base64, and add it to `src/SurfOS.Console/Trust/catalog-public-prod-<key-id>.pub`. The key ID must match `SURFCLOUD_CATALOG_KEY_ID`. Review the public-key fingerprint out of band before building. This public file is intentionally embedded in the SurfOS binary. No production pin is present in this checkout.
3. Mount the private seed through Secret Manager or equivalent as a read-only file and set `SURFCLOUD_CATALOG_SIGNING_KEY_FILE` to that absolute external path. Set `SURFCLOUD_CATALOG_MANIFEST_PATH` to an operator-curated public catalog JSON file, and set a strictly increasing `SURFCLOUD_CATALOG_SEQUENCE`. Never put the private seed in an image layer, Git, a desktop build, a command-line argument, or a log.
4. Deploy the publisher with `GOOGLE_CLOUD_PROJECT`, `SURFCLOUD_DRIVE_FOLDER_ID`, `SURFCLOUD_AUTH_AUDIENCE`, Drive OAuth settings, and the catalog settings. Set SurfOS `SURFCLOUD_API_URL` to the API root, `SURFCLOUD_PUBLISH_URL` to `/publish`, and `SURFCLOUD_CATALOG_URL` to `/catalog`, all over HTTPS. A Google ID token from an approved external sign-in flow is supplied to SurfOS through `SURFCLOUD_TOKEN`; full interactive sign-in integration remains a blocker.
5. Validate the signed catalog and package download policy in staging with two separate Google identities before enabling remote installs. The current curated file is not generated from verified publish records, and the server has no authorized package download endpoint. Do not list private packages in the public catalog file.

For rotation, ship clients with both old and new **production public** pins first, then switch the server key ID/private key and increment the catalog sequence. After clients are updated, retire the old public pin and revoke the old private key. To roll back server code, retain a valid current signing key and use a sequence **at least** as high as the highest value clients have accepted. Restoring an older signed envelope is rejected. If a client build lacks the active pin, remote catalog installs remain unavailable until a compatible build is deployed.

### Data and deployment rollback

Back up Firestore `surfcloudUsers` package, quota, and operation documents and inventory Drive objects before deployment. Deploy the new publisher before a client that requires idempotency keys. If deployment fails, stop new publishing, inspect `/operations/{id}` and reconciliation logs, and reconcile expired reservations/Drive objects before reverting server code. Old clients without an `Idempotency-Key` receive a 400 and must be upgraded; do not bypass that gate in production. Keep `firestore.rules` deny-all. Do not delete operation records until quota and Drive object state are reconciled.

## Production-readiness blockers and risks

- Owner key invalidation/rotation and exposure investigation are unconfirmed.
- Production Ed25519 signing key/public pin, curated catalog governance, and authorized package download flow are absent. Remote catalog installation intentionally fails closed.
- External Google ID-token acquisition needs a supported sign-in UX. There is no private package read path or per-user installation store. System-wide installed packages remain visible across local SurfOS accounts.
- Emulator-based two-identity authorization tests, actual Drive fault injection, concurrency/cancellation tests, and staging reconciliation have not run. Drive list visibility and app-property query behavior need staging verification.
- The package path link check is best-effort against a local process racing the filesystem. The host denied the symlink test fixture.
- Current published packages may have been created without server archive validation or catalog signatures; review and republish them through the new path. Existing installed launch commands require local trust review.
- Local benchmark rows exclude real network, Firestore, Drive, and Google token latency. Production capacity and cost are not verified.

Local tests passing does not establish production security.
