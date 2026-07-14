# Redfur Relay

Redfur Relay is the Windows desktop client that watches ESO addon data and
sends it to the Redfur ingestion service. The current application is a .NET
10 WinForms tray client. It is being migrated from a filename-based upload
utility into a durable, authenticated sync client.

This directory is a nested Git repository. Relay releases and the parent
RedfurBot release therefore need to be coordinated explicitly.

## Current Baseline

The existing client is split across a tray shell, watcher, HTTP uploader, and
configuration singleton:

- [`FileWatcherService.cs`](FileWatcherService.cs) watches ESO SavedVariables,
  TTC, and LibEsoHubPrices folders, debounces events, hashes files in memory,
  and starts uploads.
- [`UploadService.cs`](UploadService.cs) sends one multipart file at a time to
  the legacy `/upload` route and handles retries, progress, and updates.
- [`AppConfig.cs`](AppConfig.cs) persists JSON settings under the user's
  application data directory.
- [`TrayApp.cs`](TrayApp.cs) owns notifications, settings entry points, startup,
  and the progress window.
- The server adapter is [`../src/web/uploadHandler.js`](../src/web/uploadHandler.js).
  It atomically renames each accepted file into `GSData` and queues parsing in
  the parent process.

The legacy protocol is still accepted for compatibility. Its limitations are
important inputs to the rewrite:

- A shared API key authenticates the client, not the person using it.
- `displayName` is client supplied and must not be treated as identity.
- A canonical filename is shared by all uploaders, so concurrent uploads are
  last-write-wins.
- The server acknowledges after the file rename, before parsing is durable.
- The client has no durable local outbox, startup reconciliation, or watcher
  overflow recovery.
- The old client health check used the upload route and could report 404/500 as
  connected.

## Work Staged So Far

### Stage 0: Baseline and containment

- Removed embedded endpoint and API-key defaults from new relay configs.
- Configuration now requires an HTTPS endpoint, with HTTP allowed only for
  loopback development endpoints.
- Added an authenticated `/upload/health` response with a protocol version.
- The client health probe now calls that route and rejects non-success status
  codes.
- Update retries now use the update-download path instead of sending an update
  job through the data-upload path; failed versions are eligible for retry.
- Added [`../tests/test-relay-health.js`](../tests/test-relay-health.js) covering
  missing, invalid, and valid API keys.

The exposed legacy credential still needs to be rotated in every server and
client deployment. Removing the source default does not revoke a credential
that may already exist in configuration files or old binaries.

### Stage 1: Define the new protocol

Design and test a versioned relay contract before changing the desktop client.
The preferred shape is:

```text
POST /api/relay/v1/sessions
POST /api/relay/v1/sessions/{sessionId}/files
POST /api/relay/v1/sessions/{sessionId}/complete
GET  /api/relay/v1/sessions/{sessionId}
GET  /api/relay/v1/health
```

The contract must define:

- Per-user or per-device enrollment and revocation
- Session and file idempotency keys
- SHA-256 content hashes and file metadata
- Supported file types and size limits
- Server-side validation and schema versions
- Processing states separate from receipt states
- Partial failure and retry behavior
- Rate limits, audit records, and privacy handling
- A compatibility sunset for the legacy `/upload` route

The recommended identity model is a user-authorized pairing flow that creates a
revocable device credential. The server should resolve the Redfur user from
the credential; the client should send a display name only as presentation
metadata.

### Stage 2: Rewrite the client core

Separate the application into testable layers:

```text
RedfurSync.Core
  File classification and stable-file detection
  Sync session orchestration
  Content hashing and idempotency
  Retry policy and cancellation
  Protocol DTOs

RedfurSync.Infrastructure
  SQLite local outbox
  FileSystemWatcher and reconciliation scans
  HTTP API client
  Windows startup and credential storage
  Structured logging

RedfurSync.Desktop
  Tray lifecycle
  Sync dashboard
  Settings and pairing
  History, diagnostics, and updates
```

The local outbox is the reliability boundary. A file event must be recorded
locally before network delivery begins. It must survive process restarts,
network outages, locked files, duplicate watcher events, and server retries.

### Stage 3: Migrate server ingestion

Add the versioned API beside the legacy route. Both paths should enter one
server-side ingestion service, but new uploads should be stored as immutable
jobs with their hash, captured identity, receipt time, and processing state.
Parsing should consume the immutable job path rather than the current canonical
filename.

The migration is complete only after the new path has integration coverage for
concurrent uploads, process interruption, malformed data, duplicate requests,
partial batches, and replay after restart.

### Stage 4: Redesign the desktop experience

Keep the Redfur/Fissal character, but make operational state primary. The main
window should show:

- Authenticated Redfur account and enrolled device
- Connection and service health
- Waiting, active, completed, and failed work
- Last successful sync and current session progress
- Retry, cancel, and diagnostic actions
- Watched folders and file policy
- Privacy, logging, and update settings

High-DPI behavior, keyboard navigation, reduced motion, readable error states,
and safe handling of long paths and server messages are release requirements.

The current custom-painted dashboard is not a suitable foundation for this
stage. It is primarily mouse-driven, has no meaningful accessibility tree, and
uses visual effects to encode state. The rewrite should use native focusable
controls with text-plus-icon state labels. Personality belongs in headings and
small decorative details, not in error severity, identity, or progress state.

The updater is also a release gate. Before the current update UI can be
retained, downloads need signed manifests, hash and size verification,
staged replacement, rollback, and a recoverable failed-install state. Until
then, an update should never be presented as verified merely because a file
finished downloading.

### Stage 5: Package and release safely

Current release blocker: no code-signing certificate or signing secret is
configured in GitHub Actions. The tag workflow verifies SHA-256 and size and
publishes from a draft only after artifact checks pass, but the executable,
zip, and manifest remain unsigned. Do not describe them as publisher-verified.

- Versioned configuration migrations
- Windows Credential Manager or DPAPI for secrets
- Signed installer and executable
- Reproducible CI build and smoke-install job
- Signed update manifest with rollback
- Rotating structured logs without file contents or credentials
- Clean upgrade, uninstall, and single-instance behavior
- Release notes and compatibility window for the old protocol

Obfuscation is optional defense-in-depth. It is not a replacement for signing,
credential storage, HTTPS, authorization, or server-side validation.

### Stage 6: Canary rollout

1. Local fake Redfur API with deterministic failures
2. Local testing-ground server and disposable GSData/database
3. Internal relay build
4. One-uploader canary
5. Small pilot group with old and new result comparison
6. Gradual migration and legacy route deprecation
7. Legacy route removal only after adoption and replay evidence

## Acceptance Gates

A stage is not complete because the project compiles. It must also have the
nearest executable checks and a recorded environment result.

- Core: unit tests for watcher events, stable files, hashing, retries, and
  restart recovery
- API: integration tests for auth, idempotency, limits, immutable jobs, and
  processing state
- Client: Windows build, clean install, upgrade, offline queue, and startup
  reconciliation
- UX: high-DPI, keyboard, error recovery, and reduced-motion checks
- Release: signed artifact, clean-machine install, rollback, and canary logs

Current local validation:

```text
node tests/test-relay-health.js       # passed 2026-07-14
node --check src/web/uploadHandler.js  # passed 2026-07-14
git -C relay diff --check             # passed 2026-07-14
```

The current Linux workspace does not have a .NET SDK or C# compiler, so the
Windows relay build remains an explicit environment prerequisite for the next
client-code stage. No production deployment has been performed as part of
this baseline work.
