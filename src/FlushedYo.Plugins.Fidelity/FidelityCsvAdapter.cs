namespace FlushedYo.Plugins.Fidelity;

/// <summary>
/// Maps a Fidelity transaction CSV artifact to canonical transactions. Ambiguous rows are
/// refused with value-free reasons, never guessed and never silently dropped. The adapter
/// reads only the decoded text of an artifact produced by the bridge's typed download operation.
/// </summary>
public sealed class FidelityCsvAdapter
{
    private readonly InstitutionIdentity _identity;

    /// <summary>Initializes the adapter for the given account-scoped identity policy.</summary>
    public FidelityCsvAdapter(InstitutionIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        _identity = identity;
    }

    /// <summary>Adapts one CSV export artifact.</summary>
    /// <param name="csv">The artifact contents as text, decoded by the caller.</param>
    /// <returns>The mapped and ambiguous rows, in file order.</returns>
    /// <exception cref="FidelityAdapterException">The artifact has no rows or no readable header.</exception>
    public CsvAdapterResult Adapt(string csv) => Adapt(csv, expectedAccountKey: null);

    /// <summary>
    /// Adapts one CSV export artifact, keeping only rows that belong to <paramref name="expectedAccountKey"/>.
    /// A row whose account number masks to another account is refused as ambiguous rather than misfiled.
    /// </summary>
    /// <param name="csv">The artifact contents as text, decoded by the caller.</param>
    /// <param name="expectedAccountKey">The masked account key being imported, or <see langword="null"/> to accept any.</param>
    /// <returns>The mapped and ambiguous rows, in file order.</returns>
    /// <exception cref="FidelityAdapterException">The artifact has no rows or no readable header.</exception>
    public CsvAdapterResult Adapt(string csv, string? expectedAccountKey)
    {
        ArgumentNullException.ThrowIfNull(csv);
        return AdaptScoped(csv, AccountScope.For(expectedAccountKey));
    }

    private CsvAdapterResult AdaptScoped(string csv, AccountScope scope)
    {
        var document = FidelityCsvDocument.Parse(csv);
        var context = new MappingContext(document.Header, scope);
        for (var index = 0; index < document.RecordCount; index++)
        {
            if (!document.IsBlank(index))
            {
                MapRow(document, index, context);
            }
        }

        return new CsvAdapterResult
        {
            Transactions = context.Transactions,
            Ambiguous = context.Ambiguous,
        };
    }

    private void MapRow(FidelityCsvDocument document, int recordIndex, MappingContext context)
    {
        var fields = document.SplitRecord(recordIndex);
        if (fields.Length != context.Header.Count)
        {
            context.Refuse(recordIndex, AmbiguousReason.MalformedRow);
            return;
        }

        var row = new FidelityCsvRow(fields, context.Header);
        var failure = TryMap(row, context, out var transaction);
        if (failure is null)
        {
            context.Transactions.Add(transaction);
        }
        else
        {
            context.Refuse(recordIndex, failure.Value);
        }
    }

    private AmbiguousReason? TryMap(FidelityCsvRow row, MappingContext context, out Transaction transaction)
    {
        transaction = null!;
        if (TryReadFields(row, context.Scope, out var fields) is { } failure)
        {
            return failure;
        }

        transaction = new Transaction
        {
            Institution = _identity.Institution,
            ExternalId = _identity.StableId(fields.AccountKey, fields.Date, fields.Amount, fields.Memo),
            Amount = fields.Amount,
            Date = fields.Date,
            Payee = fields.Memo,
            Type = fields.Action ?? string.Empty,
        };
        return null;
    }

    /// <summary>Reads and validates every mapped field of one row, refusing on the first defect.</summary>
    private AmbiguousReason? TryReadFields(FidelityCsvRow row, AccountScope scope, out RowFields fields)
    {
        fields = default;
        if (!row.TryGetDate(FidelityCsvContract.DateHeaderPreference, out var date))
        {
            return AmbiguousReason.MissingDate;
        }

        if (row.TryGetSignedAmount(out var amount) is { } amountFailure)
        {
            return amountFailure;
        }

        if (TryReadAccountKey(row, scope, out var accountKey) is { } accountFailure)
        {
            return accountFailure;
        }

        if (!row.TryGetMemo(out var memo))
        {
            return AmbiguousReason.MissingPayee;
        }

        fields = new RowFields(accountKey, date, amount, memo, row.Action);
        return null;
    }

    private AmbiguousReason? TryReadAccountKey(FidelityCsvRow row, AccountScope scope, out string accountKey)
    {
        accountKey = string.Empty;
        if (!row.TryGetTrimmed(FidelityCsvContract.AccountNumber, out var accountNumber))
        {
            return AmbiguousReason.MissingAccount;
        }

        accountKey = _identity.MaskAccount(accountNumber);
        return scope.Accepts(accountKey) ? null : AmbiguousReason.AccountMismatch;
    }

    /// <summary>Per-run mapping state: the parsed header, the account scope, and the rows collected so far.</summary>
    private sealed class MappingContext(Dictionary<string, int> header, AccountScope scope)
    {
        public Dictionary<string, int> Header { get; } = header;

        public AccountScope Scope { get; } = scope;

        public List<Transaction> Transactions { get; } = [];

        public List<AmbiguousRow> Ambiguous { get; } = [];

        /// <summary>Records one refused record by its zero-based file line number.</summary>
        public void Refuse(int recordIndex, AmbiguousReason reason) =>
            Ambiguous.Add(new AmbiguousRow { LineNumber = recordIndex + 1, Reason = reason });
    }
}
