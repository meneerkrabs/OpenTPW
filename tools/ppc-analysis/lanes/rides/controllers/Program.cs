using System.Text.Json;
using OpenTPW.PpcEvidence;

var contracts = CommandContracts.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "controller-contracts.json")));
var tests = new (string Name, Action Run)[]
{
    ("empty ring and missing release boundary", Empty),
    ("allocated capacity and held-count full guard", Full),
    ("cursor wrapping with observed boundaries", Wrap),
    ("configured capacity reductions retain native room floor", CapacityChange),
    ("capacity 3/3/4 and eligible car separation", Distribute),
    ("per-vehicle and total rider counts", Population),
    ("BUMP64 guard without invented allocation", BumpLimit),
    ("reverse TOUR departure slots", TourOrder),
    ("all command operand/accumulator directions", Directions),
    ("unavailable effect is not a zero response", Unavailable),
    ("typed COS, direction and seat schema diagnostics", UnsupportedShapes),
    ("path sample endpoints and XYZ interpolation", PathSample),
    ("height-dependent uphill downhill and floor", HeightTarget),
    ("height target retains single fused rounding", HeightRounding),
    ("mixed single double sampled distance", SampleDistance),
    ("unsupported motion and exceptional scalar domains", MotionBoundary),
};
foreach (var test in tests) { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
Console.WriteLine($"{tests.Length} controller reference tests passed.");
if (args.Length == 1) File.WriteAllText(args[0], JsonSerializer.Serialize(new { schema = 1,
    scope = "synthetic reference primitives and sampled-motion scalars; no production controllers, serialized COS layout, admission or full motion",
    tests = tests.Select(t => t.Name), contractCount = contracts.Count, vehicleCapProof = "BUMP only; COAST implicit64 unsupported" },
    new JsonSerializerOptions { WriteIndented = true }));
else if (args.Length > 1) throw new ArgumentException("Usage: ControllersWitness [synthetic-metadata.json]");

static void Equal<T>(T expected, T actual) where T : IEquatable<T>
{ if (!expected.Equals(actual)) throw new Exception($"Expected {expected}; observed {actual}."); }
static void Status<T>(PrimitiveStatus expected, PrimitiveResult<T> result) where T : struct
{
    if (expected != result.Status || result.HasValue || string.IsNullOrWhiteSpace(result.Detail)) throw new Exception("Expected typed rejection without a manufactured value.");
    try { result.RequireValue(); } catch (NotSupportedException) { return; }
    throw new Exception("Rejection silently produced a value.");
}
static void Empty()
{
    var ring = PassengerRing.EmptySnapshot(4, 4);
    Status(PrimitiveStatus.Empty, ring.ReadReleased(null));
    Equal(10, ring.AppendKnownVisitor(10).RequireValue());
    Status(PrimitiveStatus.UnsupportedSchema, ring.ReadReleased(null));
    Status(PrimitiveStatus.BlockedByObservedBoundary, ring.ReadReleased(0));
    Equal(10, ring.ReadReleased(1).RequireValue());
    Status(PrimitiveStatus.Empty, ring.ReadReleased(1));
}
static void Full()
{
    var ring = PassengerRing.EmptySnapshot(2, 2);
    ring.AppendKnownVisitor(10).RequireValue(); ring.AppendKnownVisitor(20).RequireValue();
    Status(PrimitiveStatus.Full, ring.AppendKnownVisitor(30));
    Equal(2, ring.Queued); Equal(0, ring.WriteIndex);
    var held = PassengerRing.FromSnapshot(new[] { 10, 0 }, 1, 1, 0, 1, 2);
    Status(PrimitiveStatus.Full, held.AppendKnownVisitor(20)); Equal(100, held.QueryRoom(100));
}
static void Wrap()
{
    var ring = PassengerRing.EmptySnapshot(3, 3);
    foreach (var id in new[] { 10, 20, 30 }) ring.AppendKnownVisitor(id).RequireValue();
    Equal(10, ring.ReadReleased(2).RequireValue()); Equal(20, ring.ReadReleased(2).RequireValue());
    Status(PrimitiveStatus.BlockedByObservedBoundary, ring.ReadReleased(2));
    ring.AppendKnownVisitor(40).RequireValue(); ring.AppendKnownVisitor(50).RequireValue();
    Equal(30, ring.ReadReleased(0).RequireValue()); Equal(0, ring.ReadIndex);
    Equal(40, ring.ReadReleased(2).RequireValue()); Equal(50, ring.ReadReleased(2).RequireValue());
    Equal(0, ring.Queued);
}
static void CapacityChange()
{
    var ring = PassengerRing.FromSnapshot(new[] { 10, 20, 30, 0 }, 3, 0, 0, 3, 4);
    Equal(100, ring.QueryRoom(100, 2));
    Equal(0, ring.QueryRoom(0, 2));
    Equal(1, ring.QueryRoom(0, 4));
    Status(PrimitiveStatus.UnsupportedLiveRebuild, ring.ChangeAllocatedCapacity(2));
    Equal(4, ring.Capacity); Equal(3, ring.Queued);
}
static void Distribute()
{
    var plan = CoasterVehiclePrimitives.PlanAllowance(10, 0, 100, 100, new[] { false, true, true, true, false }).RequireValue();
    Equal("0,3,3,4,0", string.Join(',', plan.PerVehicle)); Equal(10, plan.TotalAllowance);
    var raised = CoasterVehiclePrimitives.PlanAllowance(10, 100, 100, 100, new[] { true, true, true }).RequireValue();
    Equal(100, raised.TotalAllowance); Equal("33,33,34", string.Join(',', raised.PerVehicle));
    var definitionCap = CoasterVehiclePrimitives.PlanAllowance(100, 10, 50, 100, new[] { true }).RequireValue();
    Equal(50, definitionCap.TotalAllowance);
    var reduced = CoasterVehiclePrimitives.PlanAllowance(20, 15, 10, 8, new[] { true, true, true }).RequireValue();
    Equal("2,3,3", string.Join(',', reduced.PerVehicle)); Equal(8, reduced.TotalAllowance);
    Status(PrimitiveStatus.UnsupportedSchema, CoasterVehiclePrimitives.PlanAllowance(1, 1, 1, 1, Array.Empty<bool>()));
}
static void Population()
{
    var observed = new[] { new CoasterVehicleState(1, 2, 2), new CoasterVehicleState(2, 2, 1) };
    Equal(2, observed.Length); Equal(3, CoasterVehiclePrimitives.TotalPassengers(observed).RequireValue());
    var reduced = new[] { new CoasterVehicleState(1, 1, 2), new CoasterVehicleState(2, 1, 1) };
    Equal(3, CoasterVehiclePrimitives.TotalPassengers(reduced).RequireValue());
    Status(PrimitiveStatus.InvalidSnapshot, CoasterVehiclePrimitives.TotalPassengers(new[] { observed[0], observed[0] }));
    var plan = CoasterVehiclePrimitives.PlanAllowance(2, 2, 2, 2, new[] { true, true }).RequireValue();
    Status(PrimitiveStatus.UnsupportedLiveRebuild, CoasterVehiclePrimitives.ApplyLivePlan(plan, observed));
}
static void BumpLimit()
{
    Equal(true, CoasterVehiclePrimitives.PassesBumpLaunchGuard(63, 100, 1).RequireValue());
    Equal(false, CoasterVehiclePrimitives.PassesBumpLaunchGuard(64, 100, 1).RequireValue());
    Equal(false, CoasterVehiclePrimitives.PassesBumpLaunchGuard(3, 3, 1).RequireValue());
    Equal(false, CoasterVehiclePrimitives.PassesBumpLaunchGuard(0, 100, 0).RequireValue());
}
static void TourOrder()
{
    var boarding = new[] { 10, 20, 30 };
    var leaving = new List<int>();
    for (var occupancy = -3; occupancy < 0; ++occupancy) leaving.Add(boarding[CoasterVehiclePrimitives.TourDepartureSlot(occupancy).RequireValue()]);
    Equal("30,20,10", string.Join(',', leaving));
    Status(PrimitiveStatus.InvalidSnapshot, CoasterVehiclePrimitives.TourDepartureSlot(0));
}
void Directions()
{
    Equal(39, contracts.Count);
    foreach (var contract in contracts.Values)
    {
        var projection = CommandContracts.Project(contract, true, -7, ReferenceFlags.Zero, 5).RequireValue();
        Equal(contract.Family == "COAST" && contract.RawCommand == 7, projection.NoOperation);
        if (contract.Accumulator == AccumulatorDirection.Preserve) Equal(ReferenceFlags.Zero.ToString(), projection.Flags.ToString());
        else if (contract.Accumulator == AccumulatorDirection.OriginalInput) Equal(ReferenceFlags.Sign.ToString(), projection.Flags.ToString());
        else Equal(ReferenceFlags.None.ToString(), projection.Flags.ToString());
        var output = contract.Parameter is ParameterDirection.RequiredVariableOutput or ParameterDirection.OptionalVariableOutput;
        Equal(output, projection.ParameterOutput.HasValue);
        var literal = CommandContracts.Project(contract, false, -7, ReferenceFlags.Zero, 5);
        if (contract.Parameter is ParameterDirection.RequiredVariableInput or ParameterDirection.RequiredVariableOutput)
            Status(PrimitiveStatus.SkippedOperandGate, literal);
        else if (literal.RequireValue().ParameterOutput is not null) throw new Exception("Literal parameter cannot receive a write.");
    }
}
void Unavailable()
{
    var query = contracts.Find("COAST", 3).RequireValue();
    Status(PrimitiveStatus.UnsupportedEffect, CommandContracts.Project(query, true, 77, ReferenceFlags.Sign, null));
    Status(PrimitiveStatus.UnsupportedSchema, contracts.Find("BUMP", 15));
}
static void UnsupportedShapes()
{
    foreach (var missing in Enum.GetValues<UnsupportedShape>()) Status(PrimitiveStatus.UnsupportedSchema, TrackSchemaBoundary.Require(missing));
}
static void PathSample()
{
    var a = new SampledPosition(1, 2, 3); var b = new SampledPosition(5, 10, 15);
    Equal(a, SampledMotionPrimitives.Interpolate(a, b, 0).RequireValue());
    Equal(b, SampledMotionPrimitives.Interpolate(a, b, 1).RequireValue());
    Equal(new SampledPosition(2, 4, 6), SampledMotionPrimitives.Interpolate(a, b, .25f).RequireValue());
}
static void HeightTarget()
{
    var settings = new HeightAcceleration(2, .5f, 1.5f);
    Equal(8f, SampledMotionPrimitives.HeightTarget(10, 0, 2, 3, settings).RequireValue());
    Equal(16f, SampledMotionPrimitives.HeightTarget(10, 2, 0, 3, settings).RequireValue());
    Equal(3f, SampledMotionPrimitives.HeightTarget(1, 0, 2, 3, settings).RequireValue());
    Equal(10f, SampledMotionPrimitives.HeightTarget(10, 2, 2, 3, settings).RequireValue());
}
static void HeightRounding()
{
    var epsilon = MathF.ScaleB(1f, -23);
    var settings = new HeightAcceleration(1, 1 + epsilon, 1);
    var actual = SampledMotionPrimitives.HeightTarget(1, 0, 1 - epsilon, 0, settings).RequireValue();
    Equal(BitConverter.SingleToInt32Bits(MathF.ScaleB(1f, -46)), BitConverter.SingleToInt32Bits(actual));
    Equal(0f, (1 + epsilon) * (-1 + epsilon) + 1);
}
static void SampleDistance()
{
    Equal(5f, SampledMotionPrimitives.Distance(new(0, 0, 0), new(3, 4, 0)).RequireValue());
    Equal(13f, SampledMotionPrimitives.Distance(new(0, 0, 0), new(3, 4, 12)).RequireValue());
    Equal(0f, SampledMotionPrimitives.Distance(new(2, 3, 4), new(2, 3, 4)).RequireValue());
    var point = new SampledPosition(100000000, 1, .125f);
    Equal(BitConverter.SingleToInt32Bits(100000000f), BitConverter.SingleToInt32Bits(SampledMotionPrimitives.Distance(point, new(0, 0, 0)).RequireValue()));
}
static void MotionBoundary()
{
    Status(PrimitiveStatus.InvalidSnapshot, SampledMotionPrimitives.Interpolate(new(0, 0, 0), new(1, 1, 1), -1));
    Status(PrimitiveStatus.InvalidSnapshot, SampledMotionPrimitives.Interpolate(new(float.NaN, 0, 0), new(1, 1, 1), 0));
    Status(PrimitiveStatus.InvalidSnapshot, SampledMotionPrimitives.HeightTarget(1, 0, 1, -1, new(1, 1, 1)));
    Status(PrimitiveStatus.InvalidSnapshot, SampledMotionPrimitives.HeightTarget(1, -float.MaxValue, float.MaxValue, 0, new(1, 1, 1)));
    Status(PrimitiveStatus.InvalidSnapshot, SampledMotionPrimitives.Distance(new(0, float.MaxValue, 0), new(0, 0, 0)));
    Status(PrimitiveStatus.UnsupportedSchema, SampledMotionPrimitives.AdvanceTrain());
}
