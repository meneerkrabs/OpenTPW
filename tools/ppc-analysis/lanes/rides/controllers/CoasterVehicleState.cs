namespace OpenTPW.PpcEvidence;

/// <summary>Observed logical rider allowance/count; no physical seat or track-position schema is implied.</summary>
public readonly record struct CoasterVehicleState(int VehicleId, int RiderAllowance, int PassengerCount);
public readonly record struct CapacityPlan(int TotalAllowance, IReadOnlyList<int> PerVehicle);

public static class CoasterVehiclePrimitives
{
    public static PrimitiveResult<int> TotalPassengers(IReadOnlyList<CoasterVehicleState> observedVehicles)
    {
        if (observedVehicles.Any(v => v.VehicleId <= 0 || v.RiderAllowance < 0 || v.PassengerCount < 0)
            || observedVehicles.Select(v => v.VehicleId).Distinct().Count() != observedVehicles.Count)
            return PrimitiveResult<int>.Reject(PrimitiveStatus.InvalidSnapshot, "vehicle/rider observation is outside the qualified domain");
        // A capacity reduction can leave existing riders above a new allowance; retain that observation.
        var sum = observedVehicles.Sum(v => (long)v.PassengerCount);
        return sum <= int.MaxValue ? PrimitiveResult<int>.Applied((int)sum)
            : PrimitiveResult<int>.Reject(PrimitiveStatus.InvalidSnapshot, "total passenger count overflows the qualified signed domain");
    }

    public static PrimitiveResult<CapacityPlan> PlanAllowance(int requested, int globalLimit, int definitionLimit,
        int trainLimit, IReadOnlyList<bool> observedEligibility)
    {
        if (requested < 0 || globalLimit < 0 || definitionLimit < 0 || trainLimit < 0)
            return PrimitiveResult<CapacityPlan>.Reject(PrimitiveStatus.InvalidSnapshot, "capacity limits require nonnegative observed inputs");
        var total = Math.Min(Math.Min(requested, globalLimit), Math.Min(definitionLimit, trainLimit));
        var eligible = observedEligibility.Count(v => v);
        if (eligible == 0 && total != 0)
            return PrimitiveResult<CapacityPlan>.Reject(PrimitiveStatus.UnsupportedSchema, "positive capacity has no observed eligible car");
        var result = new int[observedEligibility.Count];
        var remaining = total;
        for (var index = 0; index < result.Length; ++index)
            if (observedEligibility[index])
            {
                result[index] = remaining / eligible;
                remaining -= result[index];
                --eligible;
            }
        return PrimitiveResult<CapacityPlan>.Applied(new(total, Array.AsReadOnly(result)));
    }

    /// <summary>The actual BUMP launch predicate; passing it is not proof of allocation/admission success.</summary>
    public static PrimitiveResult<bool> PassesBumpLaunchGuard(int activeVehicles, int configuredVehicles, int observedPhase)
    {
        if (activeVehicles < 0 || configuredVehicles < 0)
            return PrimitiveResult<bool>.Reject(PrimitiveStatus.InvalidSnapshot, "vehicle counts are outside the qualified nonnegative domain");
        return PrimitiveResult<bool>.Applied(activeVehicles < configuredVehicles && activeVehicles < 64 && observedPhase != 0);
    }

    public static PrimitiveResult<CapacityPlan> ApplyLivePlan(CapacityPlan plan, IReadOnlyList<CoasterVehicleState> observedVehicles) =>
        PrimitiveResult<CapacityPlan>.Reject(PrimitiveStatus.UnsupportedLiveRebuild,
            $"plan {plan.TotalAllowance} across {observedVehicles.Count} observed vehicles requires unproved teardown, redistribution and native rebuilding");

    public static PrimitiveResult<int> TourDepartureSlot(int observedSignedOccupancy)
    {
        if (observedSignedOccupancy >= 0 || observedSignedOccupancy == int.MinValue)
            return PrimitiveResult<int>.Reject(PrimitiveStatus.InvalidSnapshot, "normal TOUR departure needs a qualified negative occupancy");
        return PrimitiveResult<int>.Applied(-(observedSignedOccupancy + 1));
    }
}
