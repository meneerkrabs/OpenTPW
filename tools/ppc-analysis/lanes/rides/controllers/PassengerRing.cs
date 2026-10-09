namespace OpenTPW.PpcEvidence;

/// <summary>
/// Reference buffer primitives from COAST rings, not a guest-admission controller.
/// Held-count transfer and release-boundary meaning remain external observed inputs.
/// </summary>
public sealed class PassengerRing
{
    private readonly int[] storage;
    public int Capacity => storage.Length;
    public int Queued { get; private set; }
    public int Held { get; }
    public int ConfiguredLimit { get; }
    public int ReadIndex { get; private set; }
    public int WriteIndex { get; private set; }

    private PassengerRing(int[] values, int queued, int held, int read, int write, int configured)
    {
        storage = values; Queued = queued; Held = held; ReadIndex = read; WriteIndex = write; ConfiguredLimit = configured;
    }

    /// <summary>Returns a ring from supplied observations; bounds are tool policy, not native validation.</summary>
    public static PassengerRing FromSnapshot(int[] storage, int queued, int held, int read, int write, int configured)
    {
        ArgumentNullException.ThrowIfNull(storage);
        if (storage.Length is < 1 or > 65536 || queued < 0 || held < 0 || (long)queued + held > storage.Length
            || read < 0 || read >= storage.Length || write < 0 || write >= storage.Length || configured < 0)
            throw new ArgumentException("invalid passenger-ring snapshot");
        return new((int[])storage.Clone(), queued, held, read, write, configured);
    }

    public static PassengerRing EmptySnapshot(int capacity, int configured)
    {
        if (capacity is < 1 or > 65536) throw new ArgumentOutOfRangeException(nameof(capacity));
        return FromSnapshot(new int[capacity], 0, 0, 0, 0, configured);
    }

    /// <summary>Native query uses signed min; a reduced configured limit can yield negative room.</summary>
    public int QueryRoom(int observedGlobalLimit, int? observedConfiguredLimit = null)
    {
        var configured = observedConfiguredLimit ?? ConfiguredLimit;
        if (observedGlobalLimit < 0 || configured < 0) throw new ArgumentException("unqualified limit domain");
        return Math.Min(observedGlobalLimit, configured - Queued - Held);
    }

    /// <summary>Appends an already-supplied visitor ID; no allocation, admission or host accounting is inferred.</summary>
    public PrimitiveResult<int> AppendKnownVisitor(int visitor)
    {
        if (visitor <= 0) return PrimitiveResult<int>.Reject(PrimitiveStatus.InvalidSnapshot, "visitor ID is outside the qualified positive domain");
        if (Queued + Held >= Capacity) return PrimitiveResult<int>.Reject(PrimitiveStatus.Full, "queued plus held reaches allocated ring capacity");
        storage[WriteIndex] = visitor;
        WriteIndex = (WriteIndex + 1) % Capacity;
        ++Queued;
        return PrimitiveResult<int>.Applied(visitor);
    }

    public PrimitiveResult<int> ReadReleased(int? observedBoundaryIndex)
    {
        if (Queued == 0) return PrimitiveResult<int>.Reject(PrimitiveStatus.Empty, "no queued passenger");
        if (observedBoundaryIndex is null) return PrimitiveResult<int>.Reject(PrimitiveStatus.UnsupportedSchema, "departure boundary has not been observed");
        if (observedBoundaryIndex < 0 || observedBoundaryIndex >= Capacity)
            return PrimitiveResult<int>.Reject(PrimitiveStatus.InvalidSnapshot, "boundary pointer is outside the allocated ring");
        if (ReadIndex == observedBoundaryIndex)
            return PrimitiveResult<int>.Reject(PrimitiveStatus.BlockedByObservedBoundary, "native read cursor equals its supplied release boundary");
        var visitor = storage[ReadIndex];
        if (visitor <= 0) return PrimitiveResult<int>.Reject(PrimitiveStatus.InvalidSnapshot, "queued slot lacks a qualified visitor ID");
        ReadIndex = (ReadIndex + 1) % Capacity;
        --Queued;
        // Native storage is not cleared; ownership is determined by counters/cursors.
        return PrimitiveResult<int>.Applied(visitor);
    }

    public PrimitiveResult<int> ChangeAllocatedCapacity(int requested) => PrimitiveResult<int>.Reject(PrimitiveStatus.UnsupportedLiveRebuild,
        $"native queue rebuild/transfers are unproved; requested {requested} must not drop or reinterpret held passengers");
}
