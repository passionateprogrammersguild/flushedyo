# Tool: read-run-result

Reads the structured result of a Fidelity run without exposing any financial value.

## Inputs

- The `FidelityDownloadResult` returned by `FidelityProviderPlugin.DownloadAccountsAsync`.

## Output shape

```json
{
  "accounts": [
    {
      "accountKey": "<masked key>",
      "mapped": 12,
      "ambiguous": 0,
      "imported": { "inserted": 10, "skipped": 2, "rejected": 0, "latestSafeDate": "2026-11-01" },
      "failure": null
    }
  ],
  "succeeded": 1,
  "failed": 0
}
```

## Rules

- Emit **only** the fields above: masked account key, counts, the inclusive safe date, and a
  value-free failure reason.
- Never emit a payee, amount, raw account number, external ID, hash, or secret — not even in an error
  message or a debug line.
- `failure` is a fixed reason from the adapter/importer vocabulary. If it is non-null, no watermark
  advanced for that account.

## Errors

- A null result means the run was cancelled or threw before returning; report the exception type only.
