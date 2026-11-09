# Fidelity accounts

Explains how account scope is established for a Fidelity sync and what the provider can and cannot
enumerate.

## Inputs

- `FlushedYo:Fidelity:AccountKeys` — masked, stable account keys, one per account to sync.
- An authenticated session (see `login.md`).

## Rules

- The typed bridge of unit 011 exposes navigation, credential entry, manual-MFA waiting, and a typed
  statement download. It has **no page-text operation**, so account keys cannot be scraped live. The
  provider therefore requires the keys to be configured and never guesses an account.
- A configured key must be non-empty, at most 256 characters, and free of control characters.
  Duplicates are dropped case-sensitively.
- The key must equal the value `InstitutionIdentity.Fidelity.MaskAccount` derives from a row's
  `Account Number`. It is a SHA-256 hex digest of the raw number — stable across runs, never the raw
  number, and log-safe.
- `ListAccountsAsync` reports what the portal returned after login; today that is the configured set.
  A mismatch with the configured keys is a contract violation, not a discovery signal.

## Outputs

- One `Account { Institution = "fidelity", AccountKey }` per configured key, in configuration order.

## Permissions

- Read access to plugin configuration only. Account keys are opaque identifiers, never credentials.

## Errors and recovery

| Failure | Meaning | Recovery |
| --- | --- | --- |
| `FidelityAdapterException` "No account keys are configured" | Nothing to sync | Populate `AccountKeys`; there is no discovery fallback |
| `FidelityAdapterException` "not log-safe" | A key is empty, too long, or has control characters | Fix the configuration |
| `AmbiguousReason.AccountMismatch` | An export row belongs to a different account | Re-export that account; never widen the expected key |
