# Fidelity incremental transaction download

Downloads each configured account's transaction export through the trusted bridge, maps it with the
Fidelity CSV adapter, and imports it account-scoped so duplicates and same-day overlap stay safe.

## Inputs

- An authenticated session (`login.md`) and configured account keys (`accounts.md`).
- `ArtifactDirectory` — a coordinator-owned, fully-qualified directory the bridge may write into.
- `MaxArtifactBytes`, `DownloadTimeout`, `LookbackDays`.

## Steps (per account)

1. `DownloadStatementAsync(accountKey)` — triggers exactly one provider export control. The bridge
   pins the verified origin, enforces the byte cap and bounded wait, and deletes partial artifacts on
   failure. The artifact stays in the coordinator directory; it must not be deleted before its durable
   import completes.
2. `FidelityArtifactReader.Read(path, MaxArtifactBytes)` — rejects missing, empty, oversized, or
   non-UTF-8 artifacts with a value-free message.
3. `FidelityCsvAdapter.Adapt(csv, accountKey)` — header-driven mapping. `Run Date` (or `Action Date`),
   `Amount $`, `Account Number`, and `Name`/`Description`/`Symbol` are required. Amounts need an
   explicit sign or parentheses; an unsigned amount is refused, not guessed. Rows whose account number
   masks to another key are refused as `AccountMismatch`.
4. If **any** row is ambiguous the account fails closed and nothing is imported — a partial import
   would advance the inclusive watermark past rows it never stored.
5. `AccountTransactionImporter.ImportAsync(account, rows, pluginVersion)` — validates, inserts
   atomically, and advances the account's inclusive safe-date watermark in the same transaction.
   Duplicates are identified by the account-scoped SHA-256 hash, so re-downloading is safe.

## Outputs

- `FidelityDownloadResult` — one `FidelityAccountDownload` per account with `Mapped`, `Ambiguous`,
  `Import` (inserted/skipped/rejected/safe date/cash state), and a value-free `Failure` when it failed.
- The next run excludes nothing: the safe date is an **inclusive** overlap, never an exclusive filter,
  so late-arriving records are still captured.

## Permissions

- One download per configured account. No holdings download: the provider declares only
  `download-transactions` and returns no holdings.
- No log line ever contains a payee, amount, account number, external ID, hash, or secret.

## Errors and recovery

| Failure | Meaning | Recovery |
| --- | --- | --- |
| `FidelityAdapterException` (reader) | Artifact missing/empty/oversized/non-UTF-8 | Re-run the account; if it repeats, re-check the export trigger selector |
| `FidelityAdapterException` "no header row" / "lacks the required amount column" | The portal changed its CSV dialect | Update the adapter contract; fail closed until then |
| Ambiguous rows counted | Unreadable dates, amounts, signs, accounts, or payees | Inspect the export out-of-band; nothing was imported, so a clean re-run is safe |
| `LedgerException` | Ambiguous source identity in the ledger | Investigate before retrying; the whole import rolled back |
| `BridgePolicyException` / `BridgeSnapshotException` | Origin or document identity changed | Restart the run; the bridge already closed the browser |

Live Fidelity behavior is unverified until an operator runs it against the real portal with explicit
consent. Never present a synthetic test result as a real download.
