# AGENTS.md — guidance for agents working on flushedyo

**flushedyo** is a personal-finance CLI (C# / .NET 10) that downloads transactions from multiple financial institutions through a plugin system, resolves credentials via a swappable secret-store abstraction (Bitwarden MCP or environment variables), and deduplicates transactions into a local SQLite ledger. Phases 1–3 are complete: scaffold + secret store, plugin discovery (`plugins list`), and the canonical-hash dedup pipeline over a lazily created SQLite ledger. No CLI command writes to the ledger yet, and plugins are manifest-only descriptors (no executable `IProviderPlugin` implementations). **`plan.md` is the authoritative roadmap** — read the relevant phase before starting work.

## Commands

Run from the repo root. All verified working.

| Task          | Command                                                       |
| ------------- | ------------------------------------------------------------- |
| Build         | `dotnet build flushedyo.slnx` (expect 0 warnings)              |
| Test          | `dotnet test --solution flushedyo.slnx` (xUnit v3 + MTP; all must pass) |
| Format check  | `dotnet format flushedyo.slnx --verify-no-changes`             |
| Unit tests    | `dotnet test --project tests/FlushedYo.Core.Tests`            |
| Integration   | `dotnet test --project tests/FlushedYo.IntegrationTests --filter "Category!=Live&Category!=Network"` |
| Live (real Bitwarden) | `dotnet test --project tests/FlushedYo.IntegrationTests --filter "Category=Live"` |
| Network (real MCP server + stub `bw`) | `dotnet test --project tests/FlushedYo.IntegrationTests --filter "Category=Network"` |
| Run CLI       | `dotnet run --project src/FlushedYo.Cli -- <args>`            |
| Get a secret  | `dotnet run --project src/FlushedYo.Cli -- get-secret <key>`  |
| List plugins  | `dotnet run --project src/FlushedYo.Cli -- plugins list`      |
| Plugin tests  | `cd .opencode/plugins/bw-tests && node --test tests/*.test.ts` |

**Definition of done for any change:** build succeeds with 0 warnings, `dotnet test --solution flushedyo.slnx` passes, and the format check is clean. Run the tests after every code change — the task is not complete until they pass.

`--solution` includes the Live tests, which **fail** (not skip) without an exported `BW_SESSION`. Without an unlocked vault, verify with the Unit and Integration rows above instead (the `bitwarden_tests` plugin's `all` preset unlocks first). A passing `Unit` + filtered `Integration` run, a clean build, and a clean format check are the equivalent bar when the vault is locked.

**OpenCode test plugin.** `.opencode/plugins/bw-tests/` auto-loads an OpenCode server plugin that exposes `bitwarden_unlock`, `bitwarden_status`, `bitwarden_lock`, and `bitwarden_tests`. It captures the Bitwarden master password only through a masked, OS-native dialog (Keychain/`osascript`, libsecret/GUI pickers/`systemd-ask-password`, or PowerShell), never a tool argument, and holds the session key in memory only. `bitwarden_tests` runs the presets above with `BW_SESSION` injected into the `dotnet test` child alone and redacts it from output; `live` and `all` unlock first. Its own `node --test` suite is hermetic — no GUI, vault, or network. See `.opencode/plugins/bw-tests/README.md` for prompt ordering and environment overrides.

## Architecture

```
flushedyo.slnx                 # solution: Cli, Core, Core.Tests, FakeMcpServer, IntegrationTests — add new projects here
src/FlushedYo.Cli/            # entry point: Program.cs = DI composition + command routing
src/FlushedYo.Core/
  Configuration/              # FlushedYoOptions ("FlushedYo" section); RepositoryPathResolver (relative path -> repo root)
  SecretStore/                # ISecretStore + BitwardenMcpSecretStore, EnvironmentVariableSecretStore
  Models/                     # Transaction, Holding (canonical records plugins produce)
  Plugin/                     # IProviderPlugin; plugin.json/config.json models + PluginFileParser;
                              #   PluginLoader (scan + validate), IPluginCatalog/PluginCatalog (cached), PluginTableFormatter
  Tracking/                   # CanonicalTransactionHasher (validate/normalize/SHA-256), TransactionTracker, TrackingResult
  Store/                      # LedgerDatabase (lazy SQLite open + migrations), ITransactionStore, SqliteTransactionStore, TransactionAudit
tests/FlushedYo.Core.Tests/   # xUnit tests mirroring Core's structure
tests/FlushedYo.Tests.FakeMcpServer/   # stdio fake MCP server; first arg selects a mode (ok, not-found, locked, hang-on-init, ...)
tests/FlushedYo.IntegrationTests/      # store + CLI end-to-end tests; the build copies the CLI, fake server, and Fixtures/ (dummy plugin) into the output (see CopyHelpers target)
plugins/  data/  src/FlushedYo.Plugins.*   # not created yet: real plugins arrive in Phases 5–6; data/ is created by the ledger on first use
.opencode/plugins/bw-tests/   # OpenCode server plugin: masked Bitwarden unlock + dotnet test presets
```

- Core holds all contracts and shared logic; the CLI stays a thin composition root. Put new functionality in Core (with tests in Core.Tests); put wiring in `Program.cs`.
- Current CLI surface: `get-secret <key> [--raw]` and `plugins list`. `login`, `download`, `status` arrive in Phase 4.
- Plugin directory and ledger path are relative to the **repository root** (nearest ancestor containing `flushedyo.slnx`), not the working directory or `bin/`; absolute paths are used as-is. Override with `FLUSHEDYO__PLUGINDIRECTORY` / `FLUSHEDYO__LEDGERPATH`.
- The ledger is opened **lazily**: `LedgerDatabase` touches the disk only on first `OpenAsync`, so `get-secret` and `plugins list` never create `data/`.
- When adding a project, add it to `flushedyo.slnx` and follow the one-test-project-per-src-project pattern.

## Hard rules

1. **Secrets are sacred.** Never log, print, persist, or commit raw secret values. Every `ISecretStore` implementation must uphold this. CLI output masks secrets unless `--raw` is passed.
2. **Extensibility invariant** (plan.md Concern A): adding a new institution must require only a new plugin folder — never changes to core code.
3. **Idempotency invariant** (Concern C): downloading the same transaction twice must never create a duplicate. Dedup keys are canonical SHA-256 hashes of normalized fields. **The hash is a permanent contract:** `CanonicalTransactionHasher` rules (and its pinned known-answer test vectors) must not change without a ledger migration, because stored hashes must keep matching. `type` is deliberately not hashed. Insert through `TransactionTracker`/`ITransactionStore` only; the atomic `INSERT OR IGNORE` result is the source of truth, never check-then-insert.
4. Never hand-edit anything in `bin/` or `obj/` — they are generated build output (gitignored); rebuild with `dotnet build` instead.
5. `plan.md` is the plan of record — don't flip phase checkboxes or status notes unless the task explicitly includes it.

## Code conventions

- File-scoped namespaces; `sealed` classes by default; nullable and implicit usings enabled everywhere.
- Modern C# idioms are in use and welcome (primary constructors, collection expressions, `GeneratedRegex`).
- Async methods take `CancellationToken cancellationToken = default` as the last parameter.
- XML doc comments on all public members and every bound configuration property.
- Test seams: `internal` constructors/helpers plus `<InternalsVisibleTo Include="FlushedYo.Core.Tests" />` (already wired in Core's csproj).
- DI registrations live in `Program.cs`; the `ISecretStore` implementation is selected by the `SecretStore:Provider` option value, compared case-insensitively against the `SecretStoreProviders` constants.
- Use `[LoggerMessage]` source-generated log methods (classes are `partial`); the analyzers are strict and `TreatWarningsAsErrors` is on.
- **All SQL is parameterized.** Never concatenate field values into SQL; the only formatted statement is the constant `PRAGMA user_version = N` in migrations. Schema changes are new entries in `LedgerMigrations.All` (versions contiguous from 1, tracked by `PRAGMA user_version`); never edit an applied migration.
- **Never log payee, amount, external ID, institution, hash, or any secret value** — log counts, batch indexes, and value-free reasons only (validation errors are deliberately value-free).
- Plugin discovery reads JSON and checks files exist; it must never call `ISecretStore`, start an MCP server, or instantiate plugin code. Invalid plugins warn on stderr and are skipped; they never fail a command.
- Namespaces: tracker and hasher types live in `FlushedYo.Core.Tracking` (not `...TransactionTracker`) because a type named like its namespace trips analyzer CA1724 (an error here).

## Testing conventions

- xUnit v3; `using Xunit;` is a global using in the test project — do not add it to files. Prefer many small test classes over one large one: xUnit parallelizes by collection, and each class is its own collection.
- Name test files `<Subject>Tests.cs` in folders mirroring Core.
- Tests that set environment variables must restore them in a `finally` block.
- MCP tests run an in-process MCP server over in-memory pipes (`Pipe` + `StreamServerTransport`/`StreamClientTransport`) — no real Bitwarden, network access, or credentials in tests.
- Test the `ISecretStore` contract generically; Bitwarden specifics belong only in `BitwardenMcpSecretStoreTests`.
- Shared test helpers (internal, in Core.Tests): `TempPluginDirectory` (temp plugin tree), `TempLedger` (temp SQLite file in a nested dir), `TransactionTestData` (`Tx`/`Audit` builders, raw row readers), `CapturingLogger<T>`, `FixedTimeProvider`, `TrackerHarness` (+ `FailingStore`, `HasCountingStore`). Reuse them; don't duplicate.
- SQLite tests use a **temp-directory database file** and delete it afterwards. In-memory databases are unsupported (connections are unpooled, so each would see its own empty DB). Pass the clock in via `TimeProvider`; never assert on wall-clock time or use `Task.Delay`.
- CLI integration tests pass settings to the child process through `CliRunner`'s `env` dictionary (e.g. `FLUSHEDYO__PLUGINDIRECTORY`, `FLUSHEDYO__LEDGERPATH`) instead of mutating the test process environment, and always use absolute temp paths. `DOTNET_ENVIRONMENT=Development` makes the host validate the whole DI container at startup — one test relies on this to catch broken registrations.
- Concurrency tests (parallel inserts) must be stable: run them repeatedly before declaring done. Tests that need a case-sensitive filesystem use `Assert.SkipWhen` (the duplicate-plugin-name test skips on default macOS).

## Integration, Live and Network tests

- Integration tests launch the fake MCP server and the built CLI as child processes via `HelperPaths`; run `dotnet build flushedyo.slnx` first. They use the public `BitwardenMcpSecretStore` constructor.
- **Live** (`[LiveBitwardenFact]`, `Category=Live`) hits the real vault through `npx @bitwarden/mcp-server`. It needs a Login item named `testconnection` with a password and an exported `BW_SESSION`. Policy: unset `BW_SESSION` or a locked vault **fails** the test (with an unlock hint) rather than skipping. Skipped only if `node`/`npx`/`bw` are missing.
- **Network** (`Category=Network`) runs the real `@bitwarden/mcp-server` against a stub `bw` script on PATH; skipped without `npx` and on Windows.
- Tests that mutate process environment use `[Collection("EnvironmentMutation")]` — in both test projects, since the variables are process-wide and collections otherwise run concurrently.
- stdout is reserved for command output; logs and errors go to stderr.

## Gotchas (past regressions — do not reintroduce)

- **Env-var overrides** use the standard `__` delimiter with no prefix, e.g. `FLUSHEDYO__SECRETSTORE__PROVIDER`, registered via `AddEnvironmentVariables()` with no prefix argument. Using `AddEnvironmentVariables("FLUSHEDYO_")` with single-underscore names does **not** bind — this broke once already.
- **MCP C# SDK is pinned at 2.3.0.** For in-memory tests use `StreamClientTransport` / `StreamServerTransport` from `ModelContextProtocol.Protocol`; `StdioClientTransport(Stream, Stream)` does not exist in v2.3.0 — also a past breakage.
- **Config binding regression (fixed):** `FlushedYoOptions` must bind from `builder.Configuration`, never a separate environment-variables-only `ConfigurationBuilder` — that silently skipped `appsettings.json`. `Program.cs` also sets the host content root to `AppContext.BaseDirectory` so the copied `appsettings.json` loads regardless of the current working directory; preserve both when refactoring `Program.cs`.
- **Don't give `List<string>` options non-empty defaults** — the configuration binder appends to them. Put defaults in `appsettings.json` (that's why `BitwardenMcpOptions.Arguments` defaults to empty).
- **SDK server tools must throw `McpException` to surface error text**; other exceptions are masked as "An error occurred invoking 'x'". Tool parameter names must match argument names (`object`, `id`).
- Env-var secret store key mapping: `fidelity/username` → `FLUSHEDYO_SECRET_FIDELITY_USERNAME` (see `EnvironmentVariableSecretStore.ToVariableName`).
- Keep package versions aligned across projects: `Microsoft.Extensions.*` 10.0.14, `ModelContextProtocol` 2.3.0, `xunit.v3` 4.0.2.
- **Plugin names are lowercase-only** (`^[a-z0-9][a-z0-9-]*$`, case-sensitive) and the folder name must equal the manifest `name` case-insensitively. `skills`/`tools` entries are plain names (no `/`, `\`, `..`) resolved as `<entry>` or `<entry>.md` inside the plugin's `skills/`/`tools/`.
- **`Task.WaitAsync` does not observe the token on an already-completed task**; `PluginCatalog.GetPluginsAsync` calls `ThrowIfCancellationRequested()` first for that reason.
- **Two processes migrating a brand-new ledger at the same instant can race** (documented, accepted for a single-user tool); tests that share one ledger across instances open it once first.
- `new LedgerDatabase(null!, logger)` is ambiguous between the public (`IOptions<>`) and internal (`string`) constructors — cast the argument. DI only considers the public constructor; keep the internal one internal.
- **Tests run on xUnit v3 + Microsoft.Testing.Platform** (opted in via `global.json` → `test.runner`). Consequences:
  - `dotnet test` no longer accepts a positional path: use `--project <dir>` or `--solution <file>`. A bare `dotnet test tests/FlushedYo.Core.Tests` fails with "should be via '--project' or '--solution'".
  - Test projects are executables (`OutputType=Exe`) and reference only `xunit.v3`; do **not** add `Microsoft.NET.Test.Sdk` or `xunit.runner.visualstudio`.
  - `--filter` still accepts VSTest syntax (`Category!=Live&Category!=Network`), so the trait filters above work unchanged.
  - Analyzer xUnit1051 is enforced as an error: pass `TestContext.Current.CancellationToken` to any call that accepts a token (the v3 test-abort token).
  - Custom `FactAttribute` subclasses must forward `[CallerFilePath]`/`[CallerLineNumber]` to the base constructor (analyzer xUnit3003).

## Doing phase work (Phases 4–7)

1. Read the target phase in `plan.md`; complete phases in order — each is a self-contained increment. Note where the plan's contracts were adapted during Phases 2–3: `ITransactionStore.RecordTransactionAsync` is `Task<bool> (Transaction, TransactionAudit, ct)` (returns `false` for a duplicate), not the plan §4 signature, and `IProviderPlugin` uses `cancellationToken` with a default.
2. Implement against the contracts in `plan.md` §4 (`IProviderPlugin`, `ITransactionStore`, `ISecretStore`).
3. Verify the phase's "Definition of Done" explicitly before reporting completion.
4. Re-check that plan.md's high-level concerns A–E still hold after your changes.
