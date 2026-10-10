namespace OpenTPW.PpcEvidence;

public enum PrimitiveStatus { Applied, Empty, Full, BlockedByObservedBoundary, InvalidSnapshot, UnsupportedSchema, UnsupportedLiveRebuild, UnsupportedEffect, SkippedOperandGate }
public readonly record struct PrimitiveResult<T>(PrimitiveStatus Status, T? Value, string Detail) where T : struct
{
    public bool HasValue => Value.HasValue;
    public T RequireValue() => HasValue ? Value!.Value : throw new NotSupportedException($"{Status}: {Detail}");
    public static PrimitiveResult<T> Applied(T value) => new(PrimitiveStatus.Applied, value, "qualified local primitive");
    public static PrimitiveResult<T> Reject(PrimitiveStatus status, string detail) => new(status, null, detail);
}

public enum UnsupportedShape { TpiCosSerialization, TrackPositions, DirectionSchema, PhysicalSeatNodes, CoasterImplicit64VehicleLimit }
public static class TrackSchemaBoundary
{
    public static PrimitiveResult<int> Require(UnsupportedShape missing) => PrimitiveResult<int>.Reject(PrimitiveStatus.UnsupportedSchema,
        missing switch
        {
            UnsupportedShape.TpiCosSerialization => "TPI COS magic/body grouping is metadata; no memory-to-file record contract is proved",
            UnsupportedShape.TrackPositions => "track position/spline data input schema is unproved",
            UnsupportedShape.DirectionSchema => "TrackInfo.Direction enum/axis binding is unproved",
            UnsupportedShape.PhysicalSeatNodes => "logical riders per car do not establish physical paired-seat/node binding",
            _ => "64-vehicle guard is proved for BUMP, not an implicit COAST fleet limit"
        });
}
