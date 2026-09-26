# SurfCloud audit — 2026-09-25

## Executive summary

**Overall: FAIL for production private publishing; PASS for the existing local regression suite; cloud isolation and cloud performance NOT TESTED.** An ignored but present service-account private key was found in the local project tree. Its validity and exposure history are unknown. The publisher validates a Google ID token and stores quota records under the token subject. Firestore rules deny all direct client access. However, the desktop `Cloud_Manager` uses an external Google credential file to create a Firestore server client, and the publisher accepts package bytes without validating the archive. The most urgent work is to rotate/invalidate the discovered key, remove privileged credentials from desktop execution, and validate packages on the server.

This is a repository and local test audit, not a security guarantee. No production or third-party endpoint was probed, no test account was used, and no cloud charge was generated. Existing unrelated working-tree changes were left in place.

## Scope, components, and flows

| Component | Verified role | Boundary and dependencies |
|---|---|---|
| `src/SurfOS.Features/Installer/Install_Setup.cs` | Collects an account name or email and marks local sign-in state | Local string is not a Google identity proof |
| `src/SurfOS.Infrastructure/Cloud/Cloud_Manager.cs` | Creates Firestore client from `GOOGLE_APPLICATION_CREDENTIALS`; writes machine heartbeat and account-name presence | Desktop to Firestore with Google credential privileges; `test_pings` and `artifacts/.../users` |
| `src/SurfOS.Domain/Services/ServiceManager.cs` | Starts presence heartbeat when local sign-in flag is set | Local flag to privileged Firestore writer |
| `src/SurfOS.Features/Shell/PackageCommands.cs` and `PackageBuilder.cs` | Build, validate, sign, and publish `.surfpkg`; create optional source ZIP | Local project/files to HTTP publisher |
| `src/SurfOS.Infrastructure/Cloud/SurfCloudPublisher.cs` | HTTPS multipart publish, optional `SURFCLOUD_TOKEN`, two-minute timeout | Desktop to configured API; token is read from environment |
| `src/SurfCloud.DrivePublisher/Program.cs` | Validates Google ID token audience, reserves quota in Firestore, uploads to Drive, commits metadata | Internet to API; API to Google identity, Firestore via ADC, and Drive via OAuth refresh token |
| `firestore.rules` | Denies all client reads and writes | Does not constrain Admin/server credentials |
| `src/SurfOS.Infrastructure/Cloud/CloudRepositoryManager.cs` | Fetches remote package manifest, caches JSON, downloads package bytes, verifies manifest SHA-256, installs/removes files | Remote HTTPS to local `apps/cache/packages.json`, `apps/packages`, and `Packages` |
| `src/SurfOS.Features/Store/SurfStore.cs`, `Package_Manager.cs` | UI and CLI for list/search/install/update | Read manifest and installed state; no separate cloud search API |
| `src/SurfOS.Features/Music/MusicPlayer.cs` | Separate cloud music manifest/cache/download path | Remote HTTPS to local media/cache |
| `surfcloud-seed/` | Bundled package and music manifests/assets | Local fallback data |

The publisher exposes `/health` and `/publish`; no list, download, deletion, metadata read, or synchronization API exists in this code. There is no verified private-package download authorization flow. “Synchronization” is limited to manifest refresh, local cache fallback, package update notifications, and presence heartbeats. Google Drive access is server-side upload/delete; no Drive ACL creation is shown. Package search is an in-memory filter after manifest retrieval.

Trust boundaries: local user input → local persisted account name; external manifest → install metadata and shell command; downloaded bytes → local package files; bearer ID token → publisher user ID; publisher service credentials → Firestore/Drive. The local `surfCloudSignedIn` flag is not equivalent to authenticated Google sign-in.

## Confirmed security findings

### F0 — Critical if active — Service-account private key present locally

**Affected:** `src/SurfOS.Console/secrets.json`; `.gitignore:370`; `src/SurfOS.Console/SurfOS.csproj:63`.

**Evidence/reproduction:** Inspect file existence and JSON keys only; do not print the key: `Test-Path src/SurfOS.Console/secrets.json` and `git check-ignore -v src/SurfOS.Console/secrets.json`. The file is present, declares `type: service_account`, and contains a PEM private key for the SurfOS Firebase project. It is ignored and is **not tracked in the current Git index** (`git ls-files --error-unmatch` fails). The project file contains a stale bundled-credential check. The key's validity, past distribution, and IAM permissions were not tested.

**Impact:** Any person or process with access to this local file may be able to act as the service account if the key is active. A Firebase Admin service account could bypass Firestore rules, depending on IAM grants. The private key itself is intentionally omitted from this report.

**Fix:** Treat the key as compromised until investigated. Use the Google Cloud console to identify and disable/rotate this exact service-account key, review audit logs and IAM roles, then remove local copies securely under the owner's retention policy. Keep CI/build checks that fail on bundled credentials. Do not commit the file. Rotation is an operational action and was not performed by this audit.

### F1 — High — Privileged Firestore credential path in desktop client

**Affected:** `src/SurfOS.Infrastructure/Cloud/Cloud_Manager.cs:44-55,76-91`; `src/SurfOS.Features/Installer/Install_Setup.cs:419-424`; `firestore.rules`.

**Evidence/reproduction:** Inspect the cited code: the desktop reads `GOOGLE_APPLICATION_CREDENTIALS`, constructs `FirestoreDb.Create`, and writes documents keyed by a locally supplied account string. Firestore rules deny client SDK access, but server credentials bypass those rules. If a credential with broad project permissions is supplied on a desktop, possession of that credential permits operations outside a user's identity. No live credential was inspected or used.

**Impact:** A stolen or shared desktop credential can bypass account isolation. The repository cannot establish that one user cannot read/modify another user's private data under this deployment model.

**Fix:** Remove Firestore server credentials from distributed clients. Use verified user authentication and a narrow backend endpoint for presence, or a client SDK plus user-scoped rules. Keep service credentials only on the server and enforce least privilege in IAM.

### F2 — High — Publisher accepts unvalidated package and source content

**Affected:** `src/SurfCloud.DrivePublisher/Program.cs:36-61,94-96`; compare `src/SurfOS.Features/Shell/PackageCommands.cs:27-50`.

**Evidence/reproduction:** The endpoint checks field presence, total bytes, visibility, and package ID, then uploads submitted bytes. Its checks do not parse `.surfpkg`, verify the payload hash, compare the embedded manifest to the submitted manifest, or validate the source ZIP. The desktop's archive validation can be bypassed by any direct HTTP client with a valid token. This conclusion follows from the endpoint code; no live request was sent.

**Impact:** Authenticated users can publish corrupt, misleading, or malicious package bytes and optional source snapshots. Whether these become available to other users depends on an unimplemented or external distribution path.

**Fix:** Validate archive structure, bounded decompression, embedded manifest, paths, hashes, and source snapshot on the server before reserving/uploading. Bind metadata to the verified package digest.

### F3 — High — Manifest hash does not authenticate publisher identity

**Affected:** `src/SurfOS.Infrastructure/Cloud/CloudRepositoryManager.cs:96-149,233-336,440-448`; `src/SurfOS.Features/Shell/AppCommands.cs:66-85`.

**Evidence/reproduction:** The remote manifest supplies both the download URL and expected SHA-256. The installer verifies bytes against that same manifest and stores its `Command`; `app --open` executes that stored command through the shell. No trusted manifest signature or pinned publisher key is checked in this flow. `sign verify` checks a detached public key supplied with the signature, without store trust anchoring.

**Impact:** Control of the manifest distribution point can redirect installation and supply a matching malicious hash and launch command. HTTPS protects transport to its endpoint, but does not establish the package author's identity.

**Fix:** Authenticate the catalog with a pinned signing key and signed package metadata; enforce command/payload permissions or sandbox execution. Validate actual `.surfpkg` before installation and separate trusted built-in commands from third-party commands.

### F4 — Medium — Failed second Drive upload can orphan the first file

**Affected:** `src/SurfCloud.DrivePublisher/Program.cs:91-96,140-150`.

**Evidence/reproduction:** `uploaded` is assigned only after both awaited uploads complete. If package upload succeeds and source upload fails, `uploaded` remains null, so catch cleanup skips deleting the package file. The reservation release is attempted, but Drive storage can leak. This is a deterministic control-flow finding, not a live Drive test.

**Impact:** Orphaned files consume Drive storage and are not represented in quota metadata. Repeated failures can exhaust storage.

**Fix:** Record each file ID immediately after creation and delete all created IDs in a bounded cleanup path; add reconciliation for abandoned uploads and reservations.

### F5 — Medium — Retry and cancellation recovery are incomplete

**Affected:** `src/SurfCloud.DrivePublisher/Program.cs:68-152,197-252`; `src/SurfOS.Infrastructure/Cloud/SurfCloudPublisher.cs:51-97`.

**Evidence/reproduction:** No idempotency key, resumable upload continuation, retry/backoff policy, reservation lease, or periodic reconciliation is implemented. An exception in Drive cleanup or reservation release can escape the catch block. A client timeout after commit leaves publish outcome unknown and retry may create another Drive object.

**Impact:** Partial failures can leave stale reservations/orphans, duplicate transfers, or confusing client status.

**Fix:** Add durable upload states, idempotency key, reservation expiry/reconciliation, bounded retries with jitter for retryable status codes, and deterministic cleanup. Return an operation ID for status lookup.

### F6 — Medium — Local package cache and state have no account boundary

**Affected:** `src/SurfOS.Infrastructure/Cloud/CloudRepositoryManager.cs:455-465,618-625`; installed state under `apps/installed_packages.json`.

**Evidence/reproduction:** Cache and installed-state paths derive from the shared SurfOS install root, with no account subject in the path. The present code has no private-package download route, so cross-account private data exposure through this cache is not confirmed.

**Impact:** If private package downloads are added to this cache, one local account could encounter another's metadata/files. Current shared installed state can affect another local account's packages.

**Fix:** Define whether installation is system-wide or per-user. For private data, key storage by verified account ID and enforce OS filesystem permissions; clear/switch cache on sign-out.

## Suspected risks and limits

| Status | Risk / evidence needed |
|---|---|
| NOT TESTED | Actual cross-user reads/writes/deletes: requires isolated emulator/service IAM setup and two authenticated identities. Static Firestore rule is deny-all, but Admin credentials bypass it. |
| NOT TESTED | Drive folder sharing, file ACLs, and OAuth scope: deployment configuration and live IAM/Drive policy were not inspected. |
| NOT TESTED | Dependency advisories: local package versions are `Google.Cloud.Firestore 4.2.0`, `Google.Apis.Auth 1.73.0`, and `System.Windows.Extensions 9.0.0`; no advisory feed was queried. |
| NOT TESTED | Encryption at rest for deployed Drive, Firestore, and local install files; repository code does not configure application-level encryption. |
| FAIL (local) | A service-account private key is present in ignored `src/SurfOS.Console/secrets.json`. No key was found in tracked source/docs/seed search; historical exposure was not assessed. |
| PASS (static) | Direct Firestore client rule is `allow read, write: if false`; server IAM remains a separate trust boundary. |
| PASS (local regression) | Existing tests reject simple traversal IDs/install paths and a tampered uninstall path, plus package hash mismatch. Symbolic links/junctions in install targets and package runtime sandboxing remain untested. |
| FAIL (new local security check) | `scripts/test-surfcloud-local.py`: 2 checks passed; the credential-presence check failed because the ignored private key is present. The test prints only the path. |

## Performance baseline

**Actual measured baseline is only an offline Python surrogate** over the 1,792-byte bundled `packages.json`, on this Windows host, 1,000 operations per row. It measures JSON parsing/listing, JSON parsing/name search, and SHA-256 of the seed bytes. It does **not** time C# application code, login, remote repository calls, package transfers, Firestore, Drive, or synchronization. Thread counts are simulated local workers; Python runtime overhead and GIL affect throughput. Peak memory is `tracemalloc` Python allocations, not process RSS. CPU is process CPU seconds for each row. Network bytes and database operations are zero by construction.

| Local operation | Workers | p50 ms | p95 ms | p99 ms | ops/s | Error | CPU s | Peak traced bytes |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| listing | 1 | .0296 | .0380 | .0605 | 12,008.5 | 0% | .0625 | 2,023,881 |
| listing | 10 | .0300 | .0400 | .0666 | 15,954.3 | 0% | .0625 | 1,955,991 |
| listing | 50 | .0308 | .0530 | .0849 | 12,521.1 | 0% | .1094 | 2,022,343 |
| listing | 100 | .0300 | .0569 | .0753 | 11,274.4 | 0% | .1250 | 2,202,831 |
| search | 1 | .0329 | .0417 | .0676 | 14,923.3 | 0% | .0625 | 1,864,388 |
| search | 10 | .0337 | .0465 | .0830 | 13,282.9 | 0% | .0781 | 1,915,595 |
| search | 50 | .0370 | .0658 | .0868 | 11,456.0 | 0% | .0938 | 2,044,975 |
| search | 100 | .0356 | .0681 | .0888 | 10,187.0 | 0% | .0938 | 2,210,999 |
| seed SHA-256 | 1 | .0026 | .0032 | .0036 | 31,009.6 | 0% | .0312 | 1,773,988 |
| seed SHA-256 | 10 | .0027 | .0036 | .0093 | 27,993.6 | 0% | .0312 | 1,808,367 |
| seed SHA-256 | 50 | .0027 | .0073 | .0097 | 21,170.7 | 0% | .0312 | 1,992,546 |
| seed SHA-256 | 100 | .0031 | .0087 | .0104 | 17,264.6 | 0% | .0781 | 2,139,420 |

All rows process 1,792,000 input bytes, 0 network bytes, and 0 database operations. End-to-end bottlenecks cannot be ranked from this surrogate. Code-level likely bottlenecks are a fresh `HttpClient` and full in-memory download per request, a full manifest fetch for each `surf` command, token refresh for every Drive operation, serial package/source upload, and two Firestore transactions per publish. These are hypotheses pending service measurements.

Firestore operation estimate for one successful publish: reservation transaction reads usage and package record (2 document reads) and writes usage (1); commit transaction reads both (2 reads) and writes package record plus usage (2 writes): **at least 4 reads and 3 writes**, excluding transaction retries, indexes, and ancillary service calls. A rejected quota request uses at least 2 reads and no committed write. Failures can add a usage read/write during release. This is an operation estimate, not a billed-cost quote; rates depend on deployment and retry behavior.

| Requested benchmark | Status | Reason |
|---|---|---|
| Google login | NOT TESTED | No implemented interactive Google login flow found; local account field is not ID-token login. |
| Cloud initialization, metadata reads/writes, upload/download | BLOCKED | No local Firestore/Drive emulators or mocked service seams; no staging authorization. |
| Remote repository listing, search, sync | NOT TESTED | Only offline surrogate was run; remote calls were not made. |
| Package upload/download throughput at 1/10/50/100 | BLOCKED | Requires local HTTP/Drive/Firestore fixtures or authorized staging. |

## Reliability and scalability

| Case | Status | Evidence/result |
|---|---|---|
| Invalid package IDs and install traversal | PASS | Existing offline tests passed. |
| Corrupt package payload hash | PASS | Existing archive test rejected mismatch. |
| Offline network policy | PASS | Existing test blocks fetching; repository code falls back to cache/seed. |
| HTTP timeout | NOT TESTED | 12 s manifest, 30 s download, and 2 min publish timeout configured; recovery not measured. |
| Rate limit / 429 / 503 / network disconnect | NOT TESTED | No retries/backoff found; no local fault-injection run. |
| Invalid Google credential | NOT TESTED | Token-validation branch is present; no test identity/emulator run. |
| Concurrent writes and duplicate/lost data | NOT TESTED | Firestore transactions exist; reservation and Drive cleanup edge cases require fault-injection tests. |
| Partial upload | FAIL (static) | F4 identifies a deterministic orphan path. |
| Service unavailable and stale reservations | FAIL (static) | F5 identifies no lease/reconciliation. |
| 100 cloud clients | NOT TESTED | Local Python worker test does not model cloud scaling. |

## Reproduction and prerequisites

Run from repository root. These commands make no cloud requests:

```powershell
dotnet run --project src/SurfOS.Tests/SurfOS.Tests.csproj --no-restore
dotnet build src/SurfCloud.DrivePublisher/SurfCloud.DrivePublisher.csproj --no-restore
& 'C:\Users\quake\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe' scripts/test-surfcloud-local.py
& 'C:\Users\quake\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe' scripts/bench-surfcloud-local.py --iterations 1000 --clients 1 10 50 100
rg -n 'GOOGLE_APPLICATION_CREDENTIALS|FirestoreDb.Create|RegisterUserHeartbeatAsync' src/SurfOS.Infrastructure/Cloud/Cloud_Manager.cs
rg -n 'ReadFormAsync|UploadAsync|uploaded is not null|ValidateArchive' src/SurfCloud.DrivePublisher/Program.cs src/SurfOS.Features/Shell/PackageCommands.cs
rg -n 'DownloadFileAsync|ValidateSha256|Command = package.Command|ExecuteCommand' src/SurfOS.Infrastructure/Cloud/CloudRepositoryManager.cs src/SurfOS.Features/Shell/AppCommands.cs
```

`dotnet` 10.0.400 was used. The regression command passed, the publisher build had 0 warnings/0 errors, and the new local security script returned 2 PASS/1 FAIL as described above. The Firebase CLI was not installed, so Firestore rules emulator tests could not run. To close the gaps, provision a disposable Firebase project/emulator suite, mock Drive OAuth/upload responses, and create two separate test identities; run no production tests without explicit authorization and charge limits. A repeatable service benchmark should capture process RSS/CPU, HTTP bytes, Firestore emulator operation counts, retries, and p50/p95/p99 for 1/10/50/100 clients with fixed payload sizes.

## Prioritized implementation plan

1. **Critical / low to medium effort:** Investigate and rotate/disable the discovered service-account key; review its IAM scope and audit history. Confirm no build artifact contains it.
2. **High / medium effort:** Remove desktop server credentials and implement verified user-scoped backend access. Add two-identity isolation tests for read, write, delete, and presence.
3. **High / medium effort:** Validate packages in the publisher and installer; sign and verify the catalog with a pinned key before accepting hashes or launch commands.
4. **Medium / medium effort:** Introduce durable idempotent upload state, immediate Drive ID tracking, reservation expiry, cleanup/reconciliation, and fault-injection integration tests.
5. **Medium / low effort:** Bound and stream package downloads, reuse HTTP clients, cache token refresh, and add explicit retry/backoff for transient responses.
6. **Medium / medium effort:** Add local emulator/mocked API tests and end-to-end benchmarks before making scalability claims or cost commitments.

No major architectural changes were made as part of this audit.
