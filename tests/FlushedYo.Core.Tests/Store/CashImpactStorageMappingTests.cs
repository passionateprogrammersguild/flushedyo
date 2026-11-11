using FlushedYo.Core.Models;
using FlushedYo.Core.Store;

namespace FlushedYo.Core.Tests.Store;

/// <summary>
/// Table-driven round-trip coverage for the shared enum-to-storage-string mapper, including both
/// throw paths: an unknown enum value on write and an unknown storage string on read.
/// </summary>
public class CashImpactStorageMappingTests
{
    /// <summary>Dispatch from enum type to its generic round-trip action; shared by every test.</summary>
    public static readonly Dictionary<Type, Action<Enum>> RoundTripDispatch = new()
    {
        [typeof(CashImpact)] = static v => RoundTrip((CashImpact)(object)v),
        [typeof(CashStateStatus)] = static v => RoundTrip((CashStateStatus)(object)v),
        [typeof(CashStateReason)] = static v => RoundTrip((CashStateReason)(object)v),
    };

    /// <summary>Dispatch from enum type to its generic ToStorage action.</summary>
    public static readonly Dictionary<Type, Action<Enum>> ToStorageDispatch = new()
    {
        [typeof(CashImpact)] = static v => SqliteAccountLedgerStore.EnumStorage<CashImpact>.ToStorage((CashImpact)(object)v),
        [typeof(CashStateStatus)] = static v => SqliteAccountLedgerStore.EnumStorage<CashStateStatus>.ToStorage((CashStateStatus)(object)v),
        [typeof(CashStateReason)] = static v => SqliteAccountLedgerStore.EnumStorage<CashStateReason>.ToStorage((CashStateReason)(object)v),
    };

    /// <summary>Dispatch from enum type to its FromStorage call; each entry throws LedgerException for unknown strings.</summary>
    public static readonly Dictionary<Type, Action<string>> FromStorageDispatch = new()
    {
        [typeof(CashImpact)] = static s => SqliteAccountLedgerStore.EnumStorage<CashImpact>.FromStorage(s, static () => new LedgerException("The ledger contains an unknown cash impact.")),
        [typeof(CashStateStatus)] = static s => SqliteAccountLedgerStore.EnumStorage<CashStateStatus>.FromStorage(s, static () => new LedgerException("The ledger contains an unknown cash-state status.")),
        [typeof(CashStateReason)] = static s => SqliteAccountLedgerStore.EnumStorage<CashStateReason>.FromStorage(s, static () => new LedgerException("The ledger contains an unknown cash-state reason.")),
    };

    public static readonly object?[][] RoundTripData =
    {
        new object?[] { typeof(CashImpact), Enum.GetValues<CashImpact>().Cast<object?>().ToArray() },
        new object?[] { typeof(CashStateStatus), Enum.GetValues<CashStateStatus>().Cast<object?>().ToArray() },
        new object?[] { typeof(CashStateReason), Enum.GetValues<CashStateReason>().Cast<object?>().ToArray() },
    };

    public static readonly object?[][] ThrowTypeData =
    {
        new object?[] { typeof(CashImpact) },
        new object?[] { typeof(CashStateStatus) },
        new object?[] { typeof(CashStateReason) },
    };

    public static readonly object?[][] FromStorageData =
    {
        new object?[] { typeof(CashImpact), "not-a-impact", "The ledger contains an unknown cash impact." },
        new object?[] { typeof(CashStateStatus), "not-a-status", "The ledger contains an unknown cash-state status." },
        new object?[] { typeof(CashStateReason), "not-a-reason", "The ledger contains an unknown cash-state reason." },
    };

    public static readonly object?[][] StorageStringData =
    {
        new object?[] { CashImpact.CashAffecting, "cash" },
        new object?[] { CashImpact.NotCashAffecting, "non-cash" },
        new object?[] { CashImpact.Unknown, "unknown" },
        new object?[] { CashImpact.Correction, "correction" },
        new object?[] { CashImpact.Unsupported, "unsupported" },
    };

    [Theory]
    [MemberData(nameof(RoundTripData))]
    public void EnumMapping_RoundTripsEveryValue(Type enumType, object?[] values)
    {
        var roundTrip = RoundTripDispatch[enumType];
        foreach (var value in values)
        {
            roundTrip((Enum)value!);
        }
    }

    static void RoundTrip<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        var stored = SqliteAccountLedgerStore.EnumStorage<TEnum>.ToStorage(value);
        Assert.NotEmpty(stored);
        Assert.Equal(value, SqliteAccountLedgerStore.EnumStorage<TEnum>.FromStorage(
            stored,
            static () => throw new InvalidOperationException("unexpected unknown storage string")));
    }

    [Theory]
    [MemberData(nameof(ThrowTypeData))]
    public void EnumMapping_ToStorage_ThrowsOnUnknownEnumValue(Type enumType)
    {
        var value = CreateOutOfRangeValue(enumType);
        Assert.Throws<ArgumentOutOfRangeException>(() => ToStorageDispatch[enumType](value));
    }

    [Theory]
    [MemberData(nameof(FromStorageData))]
    public void EnumMapping_FromStorage_ThrowsInjectedExceptionOnUnknownString(Type enumType, string unknownStorage, string expectedMessage)
    {
        var action = () => FromStorageDispatch[enumType](unknownStorage);
        var observed = Assert.Throws<LedgerException>(action);
        Assert.Equal(expectedMessage, observed.Message);
    }

    static Enum CreateOutOfRangeValue(Type enumType) =>
        enumType switch
        {
            _ when enumType == typeof(CashImpact) => (CashImpact)999,
            _ when enumType == typeof(CashStateStatus) => (CashStateStatus)999,
            _ => (CashStateReason)999,
        };
}
