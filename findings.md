# Unit 008 findings log: in-process trusted browser bridge

Branch `factory/008`, based on `fidelity-download` (`cee40a5`). `main` is an empty placeholder and was not used.

## Result

Implemented and verified locally. An independent security review ran (see "Security review" below): no blockers, one major (fixed). The performance and systemic panels have NOT been run; the full high-risk panel is still required before merge (spec "Done when").

| Check | Result |
|---|---|
| `dotnet build flushedyo.slnx` | 0 warnings, 0 errors |
| `tests/FlushedYo.Core.Tests` | 513 total, 512 passed, 1 skipped (case-sensitive filesystem skip, pre-existing) |
| `tests/FlushedYo.ArchitectureTests` | 15 passed |
| `tests/FlushedYo.IntegrationTests --filter "Category!=Live&Category!=Network"` | 113 passed, 0 failed, 0 skipped |
| Real-browser suite (`Browser/`, 15 tests, one serial `RealBrowser` collection) | 15 passed, 0 skipped |
| `dotnet format flushedyo.slnx --verify-no-changes` | exit 0 |
| Live and Network categories | not run (need vault / `npx`; unrelated to this unit) |

Tested versions: Microsoft.Playwright 1.58.0, Chrome for Testing 145.0.7632.6 (chromium-1208), macOS 12.7.6 x86_64, .NET 10.

## Why there is no Playwright MCP handoff

The unit-006 fallback clause was invoked, not skipped. `--cdp-endpoint` shares the logged-in context but is an unauthenticated loopback listener. `--remote-endpoint` is authenticated but its clients get isolated contexts. Playwright for .NET has no `LaunchServerAsync`, so .NET cannot host an authenticated server either. A private, authenticated, same-context MCP handoff is therefore not attainable, and the bridge owns the browser in-process (Design A).

## Trust boundary (as implemented)

- Origin comes only from structured CDP state (`Target.getTargetInfo`, `Page.getFrameTree`, `Page.getNavigationHistory`, `Target.getTargets`). All sources must agree, exactly one page target must exist, and the security version must be stable across the read. No page title, DOM, or accessibility text is read anywhere.
- Credentials are fetched from `ISecretStore` inside the bridge. Providers pass a secret key name and a CSS selector only. The value lives in a local for the duration of the call.
- Document navigation is frozen during focus and fill. The snapshot is re-verified before and after every DOM step.
- Browser is launched by Playwright over its default pipe transport. Isolated non-persistent context, sandbox on, no tracing, screenshots, HAR or video, no `--remote-debugging-port`.
- `DisposeAsync` acquires the operation gate before closing; teardown is awaited and bounded; a timeout force-closes the browser. Cancellation unwinds inside the gate.

## Deviations and decisions made during implementation

1. **Submit is the one allowed navigation.** The first design kept navigation frozen through submit, so the form's own POST was aborted by the route handler and every real login failed. Now: freeze through fill, release immediately before the submit keypress, then run a distinct post-submit check (`VerifyPostSubmitSnapshotAsync`). That check requires the same target and top-level frame and an origin allowed by policy. It deliberately does not require the same document or loader. Disallowed destinations are still aborted by the route policy, so a form posting to another origin fails. `IBrowserSessionDriver.EnterCredentialAsync` gained a `verifyAfterSubmit` callback for this.
2. Hermetic test `RedirectAfterSubmit` was rewritten. A same-origin new document after submit now succeeds; a disallowed origin or a target swap faults and closes the session.
3. The real-browser origin/frame swap tests are driven by a gated endpoint (page fetches `/gate`, test releases it) rather than a timer, and wait for the swap to appear in the bridge snapshot before attempting entry. The timer version raced: the swap could land before the test took its expected snapshot.
4. Real-browser tests share one xUnit collection. Running them in parallel started several Chromium instances at once and tripped the bounded teardown on the success path. Test-option teardown bound is 15 s.
5. Driver ignores Chromium's infrastructure `browser` target when counting security-relevant targets; any additional `page` target still faults the session.

## Known limits (assumptions, not controls)

- Same-user/same-OS trust: a local process with the same privileges can read process memory or the browser.
- A malicious site that is on the allowlist can read credentials entered into it.
- Managed strings cannot be zeroed; the secret exists in memory until collected.
- Chromium is trusted. Between unfreeze and the submit keypress, page script could navigate; the secret is already in the original-origin form, and any navigation lands only on allowlisted origins (route policy) and is checked afterwards.
- A legitimate iframe on the login page is treated as a security-state change and may fail closed.
- Manual MFA is user-operated and never collected; only the structured origin is polled.
- Real-browser tests need a local Chromium and auto-skip without one. The headed MFA variant was not exercised; MFA is tested headless with a scripted "user" transition.
- Wiring vault-session acquisition (`NativeMasterPasswordReader`, salvaged) into the bridge is intentionally out of scope.

## Code Health (unit 010, measured 2026-11-03 after the step-4 refactor, commits through `088f73e`)

| File | Baseline 2026-10-29 | Score 2026-11-03 |
|---|---|---|
| `BrowserSecuritySnapshot.cs` | 7.0 | **10.0** |
| `PlaywrightBrowserSessionDriver.cs` (+ partials) | 7.81 | **9.68 / 10.0 / 10.0 / 10.0** |
| `TrustedBrowserBridge.cs` | 9.09 | **10.0** |

All files meet the adjusted unit-010 gate (≥ 9.5). One residual finding, explicitly
justified: `PlaywrightBrowserSessionDriver.cs` 9.68 carries "Excess Number of Function
Arguments" on `EnterCredentialAsync` (6 args) — spec 010 step 3 mandates keeping that
public driver signature so the hermetic fake needs no change; changing it would alter
the driver contract. Accepted as a deliberate design decision, not debt.

Fresh gate table (2026-11-03, worktree 008, head `088f73e`):

| Check | Result |
|---|---|
| `dotnet build flushedyo.slnx` | 0 warnings |
| `dotnet test --project tests/FlushedYo.Core.Tests` | 574 total, 573 passed, 1 skipped (pre-existing case-sensitive-filesystem skip) |
| `dotnet test --project tests/FlushedYo.ArchitectureTests` | 15 passed |
| `dotnet test --project tests/FlushedYo.IntegrationTests --filter "Category!=Live&Category!=Network"` | 113 passed |
| Real-browser suite (`FullyQualifiedName~Browser`) | 15 passed, 0 skipped — **two consecutive passes** (pass 1 and pass 2) |
| `dotnet format flushedyo.slnx --verify-no-changes` | exit 0 |

CodeScene `pre_commit_code_health_safeguard` on the step-4 diff: passed
(driver + bridge both "improved", zero degradations).

## Absence criteria (grep over `src/FlushedYo.Core/AgentBridge/`)

No matches for: `cdp-endpoint`, `remote-endpoint`, `remote-debugging-port`, `ModelContextProtocol`, `playwright/mcp`, `connectOverCDP`, `LaunchServer`, `Regex`, `TitleAsync`, `InnerText`, `TextContent`, `ContentAsync`, `EvaluateAsync`, `Screenshot`, `StartTracing`, `RecordHar`, `RecordVideo`. `NoEndpointTests` additionally inspects live Chromium command lines (`--remote-debugging-pipe` present; no `--remote-debugging-port`/`-address`) and asserts no browser process holds a TCP listener.

## Security review (independent evaluator, read-only)

No blockers. Both prior rejection reasons confirmed fixed: no page-derived text reaches any trust decision, and the operation gate is held through teardown. Findings and disposition:

| # | Severity | Finding | Disposition |
|---|---|---|---|
| 1 | major | `SubmitCredentialAsync` could report success with no evidence a submission happened (page swallowing Enter, or same-URL re-render) | Fixed: post-submit check now requires a new committed document (loader id changed); otherwise `BridgeOperationException` and the session closes. Same-document/SPA logins therefore fail closed by design. Tests: hermetic `SubmitWithoutObservedNavigation_IsNotReportedAsSuccess`; real browser `PageThatSwallowsTheSubmitKeypress_IsNotReportedAsSubmitted`. |
| 2 | minor | Post-submit check accepted any allow-listed origin, not the original | Fixed: must equal the pre-submit origin. A deliberate cross-origin post-login hop needs a future explicit parameter. Test: `NavigationToDifferentAllowedOriginAfterSubmit_FaultsAndClosesSession`. |
| 3 | minor | `DEBUG=pw:api` / `PWDEBUG` could log the filled secret | Fixed: `StartAsync` rejects `PWDEBUG`, `DEBUG` containing `pw:` or `*`. Test: `PlaywrightDebugLogging_IsRejectedBeforeAnyBrowserStarts` (in the serial `RealBrowser` collection because it mutates a process-wide variable that only Playwright users read). |
| 6 | minor | `_closeFailed` / `_faultReason` non-volatile | Fixed: both volatile. |
| 4 | minor | `BrowserExecutablePath` executed unvalidated | Fixed: new `AllowBrowserExecutableOverride` (default false); `AgentBridgeOptions.Validate()` (run by the bridge constructor) rejects a configured path without it, without echoing the path. Tests: `AgentBridgeOptionsTests` (4). Real-browser test site opts in explicitly. |
| 5 | minor | Unbounded browser-event channel | Fixed: bounded channel (capacity 8, `Wait`); a failed `TryWrite` faults the session. Test: `TargetFloodDuringEntry_FaultsAndTearsDownWithoutHanging` (40 `window.open` calls). |
| 7a | minor (test) | Popup test could pass for the wrong reason | Fixed: `Fault()` is now first-fault-wins and the driver's entry error includes the recorded value-free fault category; the test asserts `additional page target`. (Asserting that the popup reached the server was tried and rejected: the session closes the target before it issues a request, which is the safe behaviour.) |
| 7b | minor (test) | No structural test of launch/context hardening | Fixed: launch and context options are built by `BuildLaunchOptions` / `BuildContextOptions` and asserted in `PlaywrightLaunchHardeningTests` (sandbox on; no `--no-sandbox`, remote-debugging, `disable-web-security`, `user-data-dir`; no downloads/traces/proxy; service workers blocked; no HAR, video, storage state, permissions, client certificates; browser env carries no `BW_SESSION`, `DEBUG`, `PWDEBUG`). |
| 7c | minor (test) | Missing negatives: form action to a second allowed origin; `beforeunload` during submit; disposal racing a submit | PARTIAL. Second origin: the review exposed a real gap (the secret could be POSTed to a second allow-listed origin before the post-submit check noticed). Fixed in production: while a submit is in flight, document requests are pinned to the verified origin and anything else is aborted and faults. Test: `FormActionRetargetedToAnotherAllowedOrigin_NeverDeliversTheSecret` (no request reaches the other origin, no secret stored). STILL OPEN: `beforeunload` during submit and disposal racing a submit have no dedicated test. |

The reviewer did not read `ManualMfaBrowserTests.cs`, the hermetic test files, or the csproj/props diff, and did not run anything; those remain covered only by the implementor's runs above.

## Not done

- Performance and systemic review panels.
- Dedicated tests for `beforeunload` during submit and for disposal racing a submit (the only remaining open review items).
- Merge, and later cleanup of the frozen 002/006 worktrees.
- Repeated-run stability check of the browser suite beyond the passes recorded above.

---

# Unit 011 findings: provider CSV/download capability

## Spike (recorded in `specs/011-bridge-download.md`)

Playwright 1.58.0 **does** surface a `Download` event with `AcceptDownloads = false`; the context
option does not suppress the event. `AcceptDownloads = true` + `SaveAsAsync` lands the artifact in a
caller-owned directory. A per-operation CDP `Browser.setDownloadBehavior(allowAndName)` toggle with
the context option left `false` produced the event but never a usable file, so that path was
rejected. Design shipped: browser-level CDP download window (`allowAndName`, scoped to the single
context id, reverted to `deny` on every exit) + move-from-private-scratch into the caller directory
under a hard byte cap.

## Design as implemented

- `IBrowserSessionDriver.DownloadAsync(DownloadRequest, verifySecurityState, ct)` and
  `TrustedBrowserBridge.DownloadStatementAsync(cssSelector, expectedSnapshot, DownloadStatementOptions, ct)`.
- The window opens only for the operation: a dedicated `ICDPSession` on the **browser** target, one
  context id resolved and pinned, `allowAndName` with a private temp scratch `downloadPath`, and a
  `deny` restore in `finally` plus a session detach and scratch-directory delete.
- Verify-before / verify-after discipline: `verifySecurityState` runs before the window opens, after
  selector resolution, after the click, and after the artifact is claimed.
- `_submitOriginPin` is set to the verified origin for the trigger and cleared in `finally`.
- `DownloadArtifactPolicy.ResolvePath` rejects a suggested filename that would escape the caller
  directory; the byte cap is enforced before the move and again after it; every failure path deletes
  the destination file (`ArtifactCopy`).
- No file content is read into a string or a log. The returned `BrowserArtifact` carries path, size,
  and origin only.
- `AgentBridgeOptions` and `BuildContextOptions` are unchanged: `AcceptDownloads = false`,
  `ServiceWorkers = Block`. Downloads are never enabled globally; the CDP window is per operation.

## Deviations from the spec

1. **`CredentialRequest` changed shape.** It is now `(SubmitCredentialArgs Args, bool Submit)` with
   `Entry(...)` / `Submission(...)` factories, and `SubmitCredentialAsync` takes a public
   `SubmitCredentialArgs` record. Reason: CodeScene flagged `CredentialRequest.Create` and
   `SubmitCredentialArgs.Create` as duplicate validation and the file dropped to 9.09. Folding the
   validation into one factory raised `BrowserOperationRequests.cs` back to **10.0**. Behaviour,
   exception types, and wording are unchanged; the public bridge signature for `SubmitCredentialAsync`
   is now a single record instead of three positional arguments.
2. The spec's "hash" mention in step 3 (artifact "has expected size/hash") is satisfied by size plus
   the caller's own verification; the bridge does not hash file content because that would mean
   reading financial data into the process for a value the coordinator can compute itself.

## Defects found and fixed while re-running the suite

- `BrowserAttackTests` hung indefinitely (no Chromium involved: the whole run sat at 0 tests for
  >2 min). Two causes, both test-only:
  1. A regression from the harness refactor: `MapGate`/`SignalWhenRequested`/`MapHtml` were moved to
     **after** `SetupEntryAttackAsync` had already navigated, so the page's `fetch('/gate')` hit an
     unmapped path, the gate never existed, and `await swapped.WaitAsync(Token)` — which used the
     unrelated test-run token, not a bounded wait — never completed. Fixed by registering the gate,
     the signal, and the swapped HTML inside the setup callback (via a new
     `Func<SyntheticSite, string> renderHtml` overload) so they exist before navigation.
  2. The origin-swap test's HTML used a plain `"""` raw string while containing `{{site.Port}}`, so
     the literal placeholder was served and the swap target was an unresolvable host — Chromium
     committed `chrome-error://chromewebdata/`, which the snapshot validator correctly rejected as
     `DocumentIdentity`. Fixed by rendering the template with the live port.
- No production code changed for either.

## Gate table (2026-11-04, worktree 008, unit-011 staged diff)

| Check | Result |
|---|---|
| `dotnet build flushedyo.slnx` | 0 warnings, 0 errors |
| `dotnet test --project tests/FlushedYo.Core.Tests` | 575 total, 574 passed, 1 skipped (pre-existing case-sensitive-filesystem skip) |
| `dotnet test --project tests/FlushedYo.ArchitectureTests` | 15 passed |
| `dotnet test --project tests/FlushedYo.IntegrationTests --filter "Category!=Live&Category!=Network"` | 113 passed, 0 failed |
| Real-browser suite (`FullyQualifiedName~Browser`) | 15 passed, 0 skipped — **two consecutive passes** after the fix |
| `dotnet format flushedyo.slnx --verify-no-changes` | exit 0 |
| `pre_commit_code_health_safeguard` (staged diff) | **passed**, zero degraded files |
| `BrowserOperationRequests.cs` Code Health | 9.09 → **10.0** |

## Unit 003 mapping

A "Fidelity export flow" on the bridge primitive is: navigate to the account export page → snapshot
(verify-before) → `DownloadStatementAsync` on the export control → artifact lands in a
coordinator-owned directory under the size cap and bounded wait → snapshot (verify-after, origin
pinned via `_submitOriginPin`) → hand the path to the coordinator for durable import. Touches no
Fidelity specifics; file contents never enter strings or logs.

## Not done in this unit

- The performance and systemic panels (unit 009).
- The two previously-open review edge cases are only partly closed: origin-swap mid-download and
  teardown racing an active download now have code paths that delete the partial artifact and fault
  the session, but the dedicated real-browser tests for the beforeunload-during-click and
  disposal-racing-a-download cases are still outstanding.
