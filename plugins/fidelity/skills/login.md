# Fidelity login

Opens the trusted, in-process browser bridge, navigates to the Fidelity login page, and drives the
provider's typed credential operations. It never reads, types, or returns a credential or an MFA code.

## Inputs

- Secret-store keys only: `fidelity/username`, `fidelity/password` (never values).
- Login URL and post-login origin from the plugin options.
- An operator present to complete MFA by hand if the portal asks for it.

## Steps

1. `OpenAsync(loginUrl)` — snapshot-verified navigation through `TrustedBrowserBridge.NavigateAsync`.
   Any origin outside the configured allowlist fails closed.
2. `EnterCredentialAsync(usernameKey, usernameSelector, snapshot)` — the bridge resolves the username
   itself and fills one control without submitting.
3. Re-read the snapshot, then `SubmitCredentialAsync(passwordKey, passwordSelector, snapshot)` — the
   bridge resolves the password, fills it, and submits. A target, document, origin, or security-state
   change through submission fails closed.
4. `WaitForManualMfaAsync(postLoginOrigin, mfaTimeout)` — polls structured browser state only. It never
   collects a code and never bypasses a challenge.

## Outputs

- A verified browser snapshot on the allowed post-login origin.
- A count of account keys the portal reported (count only, never a value).

## Permissions

- Browser navigation to allow-listed origins only.
- Secret-store reads by key. No secret value ever reaches the model, a log, or a transcript.

## Errors and recovery

| Failure | Meaning | Recovery |
| --- | --- | --- |
| `BridgePolicyException` | Destination origin not allowed | Fix `AgentBridge:AllowedOrigins`; do not widen it casually |
| `BridgeSnapshotException` | Identity changed mid-operation | Restart the run; the bridge already closed the browser |
| `BridgeMfaException` | Manual MFA did not finish in time | Re-run with the operator present; raise `MfaTimeout` only within the bridge maximum |
| `BridgeOperationException` | The operation failed safely | Re-run; if it repeats, inspect the portal manually before changing selectors |

Never paste a credential into a chat, a command, or a file. If a selector no longer matches, update
`UsernameSelector` / `PasswordSelector` in the plugin options — never script a workaround that types
credentials by hand.
