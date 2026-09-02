# Relay Manager To-Do

Use this checklist to continue the relay stabilization work. The relay is a
nested repository under `RedfurBot/relay`; run Git commands from this directory
and do not alter unrelated parent-repository changes.

## Project Layout (read this before choosing a check)

The relay is split into three projects:

| Project | Target | Runs on Linux? | Contains |
| --- | --- | --- | --- |
| `RedfurSync.Core` | `net10.0` | yes | `AppConfig`, `FileWatcherService`, `UploadService`, `UploadJob`, `ProgressStream`, `MasterMerchantSaleScanner`, `FidelityMode` |
| `ESOSyncClient` | `net10.0-windows` | build only | WinForms UI, tray, updater host, `Program.cs` |
| `RedfurSync.Tests` | `net10.0` | yes | xUnit tests, references Core only |

**`RedfurSync.Core` must never reference `System.Windows.Forms` or
`System.Drawing`.** That is what keeps `dotnet test` runnable here and in CI;
the release workflow enforces it with a grep gate. Put UI-dependent behaviour
behind a delegate injected from `Program.cs` (see `AppConfig.FaultReporter`).

## Current Baseline

The working tree contains uncommitted changes in:

- `RedfurSync.Core/AppConfig.cs`
- `RedfurSync.Core/FileWatcherService.cs`
- `RedfurSync.Core/UploadService.cs`
- `Program.cs`
- `RelayMainWindow.cs`
- `TrayApp.cs`
- `UploadProgressForm.cs`

These changes fix the v1.2.0 main-window crash, preserve file changes that
arrive during active uploads, prevent duplicate watcher registration, improve
cancellation and disposal, validate update sizes, dispose HTTP responses, and
restore the original executable after a failed update replacement.

The following checks passed on 2026-08-27:

```bash
/home/echo/.dotnet/dotnet build ESOSyncClient.sln -c Release -warnaserror
/home/echo/.dotnet/dotnet test RedfurSync.Tests/RedfurSync.Tests.csproj -c Release   # 5/5, stable over 5 runs
git diff --check
```

Two real defects were found by making the tests runnable on Linux:

1. `ProtectedData` (DPAPI) had no platform guard. `CA1416` surfaced once Core
   stopped targeting `-windows`. Now gated on `OperatingSystem.IsWindows()`,
   fail-closed.
2. `ProcessUploadAsync` raised `NotifyChanged()` with a terminal `Done` status
   *before* `CleanupSnapshot(job)`, so observers could see a finished job whose
   spool file still existed. Cleanup now precedes the notification on both the
   normal and "already synchronized" paths.

Windows runtime testing is still required for anything under the WinForms
project (forms, tray, DPI, updater exe replacement).

## Manager Rules

- Begin with `git status --short` and `git diff`; preserve all existing work.
- Deploy independent read-only agents before assigning edits. Give each agent
  a narrow ownership area and require file-and-line evidence.
- Keep edits under manager control. Do not allow several agents to modify the
  same file concurrently.
- Require one small change followed immediately by its nearest executable
  validation. Stop scope expansion when a check fails.
- After each worker edit, assign a different agent to review the resulting
  diff for regressions, races, unsafe assumptions, and missing tests.
- Challenge speculative findings. Only implement behavior with a concrete
  failure scenario and a discriminating check.
- Run `dotnet build ESOSyncClient.sln -c Release -warnaserror`, the test
  command above, and `git diff --check` after every work stream and before
  release sign-off.
- Prefer putting new logic in `RedfurSync.Core` so it is testable here. If a
  change must live in the WinForms project, say so and mark it
  Windows-validation-required.
- Do not commit, tag, publish, or deploy unless the user explicitly requests
  it. Never describe an unsigned build as publisher-verified.

## Priority 1: Protect Current Fixes With Tests

- [x] Assign one agent to design a Windows-compatible relay test project.
- [x] Add a fake HTTP handler and temporary filesystem fixtures.
- [x] Test a file changing from snapshot A to B while A is uploading. Verify B
      uploads afterward and is not discarded.
- [x] Test queued cancellation while all upload semaphore slots are occupied.
      Verify terminal `Cancelled` state and no unobserved exception.
      → `RedfurSync.Tests/UploadCancellationTests.cs`
- [x] Test update size mismatch for manifest size, `Content-Length`, and final
      streamed byte count. → `RedfurSync.Tests/UpdateDownloadValidationTests.cs`
      (also covers untrusted host and hash mismatch)
- [x] Review test quality. Both new suites were mutation-checked: disabling the
      guard they target makes them fail, so they discriminate rather than mirror.
- [ ] Call `StartAsync()` repeatedly and verify only one watcher set and one
      update timer callback remain active.
      **Blocked on two small Core seams** (~15 lines, `FileWatcherService.cs`):
      an `internal` watch-root provider (paths are hard-coded to
      `SpecialFolder.MyDocuments` in three places) and an
      `internal int ActiveWatcherCount`/injectable startup delay. Assert on
      observed HTTP requests and job count, not on a "one timer" check —
      `_updateTimer` is a single field and such a test would pass against any
      implementation.
- [x] Inject failures between executable backup, replacement, and launch.
      Verify the original executable remains recoverable.
      → `RedfurSync.Core/UpdateInstaller.cs` (extracted from `TrayApp.ApplyUpdate`):
      `Apply(exePath, stagedPath)` with an injected `IUpdateFileSystem` + launch
      delegate; rollback restores the `.old` backup and guards on its existence
      (a vanished backup never deletes the validated new exe). `TrayApp.cs` now
      constructs `new UpdateInstaller(new PhysicalUpdateFileSystem(), path => Process.Start(path))`.
      → `RedfurSync.Tests/UpdateInstallerTests.cs` (12 tests): success op-order,
      stale-`.old` delete-before-backup, backup/replace/launch fault injection,
      rollback restore-fail + delete-fail + backup-missing, empty paths, and a
      real-filesystem cross-directory apply. Mutation-checked (rollback removed →
      4 tests fail). `Program.cs` stale-`.old` cleanup moved after the
      single-instance mutex gate so a second instance can never delete the
      backup mid-update.
      Acceptance passed: `dotnet build ESOSyncClient.sln -c Release -warnaserror`
      (0/0), `dotnet test RedfurSync.Tests/RedfurSync.Tests.csproj -c Release`
      (28/28), `git diff --check` clean.

Known uncovered by design: the in-loop 500 MB trip in `UploadService` would
need a half-gigabyte fixture; it is shadowed by the `Content-Length` bound
check for any honest server. Left uncovered deliberately.

Acceptance gate:

```bash
/home/echo/.dotnet/dotnet test RedfurSync.Tests/RedfurSync.Tests.csproj -c Release
/home/echo/.dotnet/dotnet build ESOSyncClient.sln -c Release -warnaserror
```

Do not use `dotnet test ESOSyncClient.sln` — it drags the WinForms project into
the run and aborts with a missing `Microsoft.WindowsDesktop.App` runtime, which
does not exist on Linux and never will.

## Priority 2: Make Async Shutdown Correct

- [ ] Assign a lifecycle/concurrency agent to map every fire-and-forget task,
      timer callback, upload, reconciliation, and delayed update check.
- [ ] Add a service-lifetime cancellation token and reject new work after
      shutdown begins.
- [ ] Track active background operations instead of discarding their tasks.
- [ ] Implement an awaited shutdown path, preferably `IAsyncDisposable`, that
      stops timers/watchers, cancels work, awaits continuations, and only then
      disposes HTTP clients and synchronization primitives.
- [ ] Update `TrayApp` ownership so UI subscriptions are removed before service
      shutdown and controls are not called after disposal.
- [ ] Add tests that dispose during a blocked upload, update check, restart,
      and reconciliation. Assert no callbacks or unobserved exceptions occur.
- [ ] Have an independent reviewer inspect lock ordering and disposal races.

Do not partially dispose semaphores or HTTP clients while continuations may
still release or use them.

## Priority 3: Fix Job Collection Ownership

- [ ] Assign a concurrency agent to replace direct mutable
      `ObservableCollection` access with service-owned state.
- [ ] Expose immutable/locked snapshots for UI reads.
- [ ] Add service methods for clearing and removing jobs so snapshot files are
      cleaned consistently.
- [ ] Marshal UI notifications through the captured WinForms synchronization
      context.
- [ ] Verify `RelayMainWindow`, `TrayApp`, and `UploadProgressForm` no longer
      enumerate or mutate jobs concurrently.
- [ ] Stress-test completion, refresh, retry, cancellation, and clear actions
      with `Control.CheckForIllegalCrossThreadCalls = true`.
- [ ] Ask a separate reviewer to inspect for deadlocks and UI-thread blocking.

## Priority 4: Secure the Updater

- [ ] Assign a security agent to define the update trust model before editing.
- [ ] Obtain a release signing decision from the user if no certificate or
      offline manifest-signing key is available. Do not invent credentials.
- [ ] Sign the executable and preferably the manifest in the release workflow.
- [ ] Embed only the verification key or pin the expected publisher identity in
      the client. Fail closed for unsigned or differently signed artifacts.
- [ ] Disable automatic redirects or validate every redirect hop and final URI
      against an explicit release-host allowlist.
- [ ] Retain the `.old` executable until the replacement signals successful
      startup. Add a marker, named event, or equivalent startup handshake.
- [ ] Test modified manifests, modified binaries, unknown signers, cross-origin
      redirects, redirect loops, launch failure, and crash-on-first-start.
- [ ] Have a second security reviewer verify that hash checking is not being
      confused with publisher authenticity.

Release blocker: do not market or label the updater as verified until signature
validation and rollback tests pass on Windows.

## Priority 5: Watcher Recovery And Spool Hygiene

- [ ] Recover from `FileSystemWatcher.Error` by serializing a full
      reconciliation and recreating the affected watcher when needed.
- [ ] Prevent concurrent overflow recoveries from starting duplicate scans.
- [ ] Define failed/cancelled snapshot retention. Keep files only while a job is
      intentionally retryable; delete them when users discard jobs.
- [ ] Clean orphaned spool files at startup using a conservative age policy.
- [ ] Check cancellation before and after Master Merchant preflight scanning.
- [ ] Add cancellation support and bounded concurrency to large MM scans.
- [ ] Test watcher overflow, repeated failure/cancel/clear cycles, restart
      recovery, and a large cancellable MM file.

## Priority 6: Configuration And Privacy

- [ ] Make configuration save failure observable to callers. Never report
      success when the atomic write failed.
- [ ] Roll back in-memory assistant/config changes when persistence fails.
- [ ] Remove the legacy API key if compatibility allows; otherwise protect it
      with DPAPI. Do not persist pairing codes in plaintext.
- [ ] Normalize loaded `DebounceMs`, `MaxLogsKept`, and `AppScale` values.
- [ ] Restrict assistant-opened links to approved web schemes and display the
      destination host.
- [ ] Reduce assistant diagnostics: omit machine name and absolute paths unless
      the user explicitly opts in.
- [ ] Test read-only/locked config files, malformed values, secret serialization,
      unsafe URL schemes, and captured assistant request metadata.

## Priority 7: Windows UX Validation

- [ ] Assign a UI agent to test the actual WinForms build on Windows.
- [ ] Verify every tab, tray action, retry/cancel flow, setup action, and update
      prompt at 100%, 150%, and 200% scaling.
- [ ] Fix runtime theme switching so all controls and tray rendering use the
      selected palette consistently.
- [ ] Make UI scaling apply immediately or clearly require/reopen the window.
- [ ] Ensure async button handlers restore enabled state in `finally` and do not
      touch disposed controls.
- [ ] Validate keyboard navigation, readable error states, long paths, reduced
      motion, and screen-reader semantics.
- [ ] Require screenshots and concrete interaction evidence from the UI agent;
      have another agent review the evidence before sign-off.

## Final Release Gate

- [ ] All focused client tests pass on Windows.
- [ ] Release build passes with warnings as errors.
- [ ] Clean-machine install and startup pass.
- [ ] Upgrade, rollback, offline startup, and watcher reconciliation pass.
- [ ] No plaintext bearer credentials or pairing codes are present under the
      application data directory.
- [ ] Signed artifact and signed-manifest checks pass, or automatic update
      application remains disabled and the limitation is documented.
- [ ] One-uploader canary completes before wider rollout.
- [ ] A final independent agent reviews the entire release diff and reports no
      unresolved critical or high findings.
- [ ] Record exact commands, environments, and results in the release handoff.

If an agent asks for product policy, signing credentials, production access,
or a decision that changes user-visible compatibility, pause that work stream
and ask the user. Resolve normal implementation questions from repository
evidence and existing behavior without unnecessary escalation.