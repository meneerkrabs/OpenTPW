using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenTPW;
using OpenTPW.Tests;

// JSON-lines bridge for the PowerPC differential oracle (docs/reverse/ORACLE.md).
// Input: one request per line, {"fn": name, ...arguments}. Output: one JSON result per line, same order.
// {"fn": "capabilities"} lists the functions this build of OpenTPW can evaluate; the Python side compares
// a target against C# only when its function is listed, and against the plan's reference formula otherwise.
// Hooks for later implementations (DET-I2 world RNG, WALK-I speed, COAST-I rating) are added here as cases.

var functions = new Dictionary<string, Func<JsonObject, JsonNode?>>
{
	["queue_limit"] = QueueLimit,
	["can_change_cell_type"] = CanChangeCellType,
	["set_cell_type"] = SetCellType,
	["unbounce"] = Unbounce,
	["bounce"] = Bounce,
	["guest_speed_limits"] = GuestSpeedLimits,
	["guest_speed_smoothing"] = GuestSpeedSmoothing,
	["arrival_group"] = ArrivalGroup,
	["park_points"] = ParkPoints,
};

using var input = new StreamReader( Console.OpenStandardInput() );
using var output = new StreamWriter( Console.OpenStandardOutput() ) { AutoFlush = false };
string? line;
while ( (line = input.ReadLine()) != null )
{
	if ( string.IsNullOrWhiteSpace( line ) )
		continue;
	var request = JsonNode.Parse( line )!.AsObject();
	var name = (string)request["fn"]!;
	JsonNode? result;
	if ( name == "capabilities" )
		result = new JsonArray( functions.Keys.Select( key => (JsonNode)JsonValue.Create( key )! ).ToArray() );
	else if ( functions.TryGetValue( name, out var function ) )
	{
		// An exception is an outcome to compare (the original has no exceptions), not a bridge failure.
		try { result = function( request ); }
		catch ( Exception exception ) { result = new JsonObject { ["exception"] = (exception.InnerException ?? exception).GetType().Name }; }
	}
	else
		result = new JsonObject { ["error"] = $"unknown function {name}" };
	output.WriteLine( result?.ToJsonString() ?? "null" );
}
output.Flush();
return 0;

// RideVisitorBridge.ComputeQueueLimit; QWTC arrives as IEEE single bits so no decimal round trip intervenes.
static JsonNode? QueueLimit( JsonObject a )
{
	var qwtc = BitConverter.Int32BitsToSingle( (int)(uint)a["qwtc_bits"]! );
	var parameters = new QueueParameters( (bool)a["has_queue"]!, false, false, qwtc, (int)a["speed"]!, (int)a["init_speed"]!,
		(int)a["capacity"]!, (int)a["duration"]! );
	return JsonValue.Create( RideVisitorBridge.ComputeQueueLimit( parameters ) );
}

static JsonNode? CanChangeCellType( JsonObject a ) =>
	JsonValue.Create( ParkCellMap.CanChangeCellType( (byte)(int)a["old"]!, (byte)(int)a["type"]!, (bool)a["last_cell"]! ) );

// The order of ParkPathBuilder.Validate/Run for one written cell: CanChangeCellType, then the same nonzero type
// (placement bump, no charge), then the park economy's money test and spend (ParkEconomy.TrySpendCell).
static JsonNode? SetCellType( JsonObject a )
{
	var old = (byte)(int)a["old"]!;
	var type = (byte)(int)a["type"]!;
	if ( !ParkCellMap.CanChangeCellType( old, type, (bool)a["last_cell"]! ) )
		return Outcome( false, 0, false );
	if ( type != 0 && type == old )
		return Outcome( true, 0, true );
	CellPurchase? kind = type switch { 1 => CellPurchase.Path, 3 => CellPurchase.Queue, _ => null };
	if ( kind == null )
		return Outcome( true, 0, false );
	var text = EconomyTestData.StandardText( (int)a["balance"]! )
		.Replace( "Costs.QueueCell 75\nCosts.PathCell 20\n", $"Costs.QueueCell {(int)a["queue_cost"]!}\nCosts.PathCell {(int)a["path_cost"]!}\n" );
	var settings = new BalanceSettings( new SamSettings( new[] { SamDocument.Parse( text, "Standard.sam" ) } ), null, null, "jungle", false );
	var park = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
	var before = park.Balance;
	var result = park.TrySpendCell( kind.Value );
	return Outcome( result == ParkEconomy.PurchaseResult.Ok, before - park.Balance, false );
}

static JsonObject Outcome( bool ok, long charged, bool bumped ) => new() { ["ok"] = ok, ["charged"] = charged, ["bumped"] = bumped };

// RideVisitorBridge.TakeUnbounced (the UNBOUNCE scan) through reflection: it is internal and static. The slot
// table arrives whole, free slots (visitor 0) included; "remaining" counts the occupied slots (+108).
static JsonNode? Unbounce( JsonObject a )
{
	var method = typeof( RideVisitorBridge ).GetMethod( "TakeUnbounced", BindingFlags.NonPublic | BindingFlags.Static )
		?? throw new MissingMethodException( "RideVisitorBridge.TakeUnbounced" );
	var slots = a["slots"]!.AsArray().Select( slot => ((int)slot![0]!, (double)(int)slot[1]!, (double)(int)slot[2]!) ).ToList();
	var guest = (int)method.Invoke( null, new object[] { slots, (double)(int)a["now"]!, false } )!;
	return new JsonObject { ["guest"] = guest, ["remaining"] = slots.Count( slot => slot.Item1 != 0 ) };
}

// RideVisitorBridge.TakeBounceSlot (the BOUNCE slot search) through reflection on the whole table; the deadline is
// computed as the bridge's BOUNCE case does (now + dur × 1000 ms). The result lists the table padded to its size.
static JsonNode? Bounce( JsonObject a )
{
	var method = typeof( RideVisitorBridge ).GetMethod( "TakeBounceSlot", BindingFlags.NonPublic | BindingFlags.Static )
		?? throw new MissingMethodException( "RideVisitorBridge.TakeBounceSlot" );
	var slots = a["slots"]!.AsArray().Select( slot => ((int)slot![0]!, (double)(int)slot[1]!, (double)(int)slot[2]!) ).ToList();
	var capacity = slots.Count;
	var now = (double)(int)a["now"]!;
	var ok = (bool)method.Invoke( null, new object[] { slots, capacity, (int)a["visitor"]!, now + (int)a["dur"]! * 1000.0, now } )!;
	while ( slots.Count < capacity )
		slots.Add( default );
	var table = new JsonArray( slots.Select( slot => (JsonNode)new JsonArray( slot.Item1, (long)slot.Item2, (long)slot.Item3 ) ).ToArray() );
	return new JsonObject { ["ok"] = ok ? 1 : 0, ["count"] = slots.Count( slot => slot.Item1 != 0 ), ["slots"] = table };
}

// GuestSpeed.Caps (0xffe38): max speed +28 and max force +24 for a smoothed speed s, passed as IEEE single bits.
static JsonNode? GuestSpeedLimits( JsonObject a )
{
	var (speed, force) = GuestSpeed.Caps( BitConverter.Int32BitsToSingle( (int)(uint)a["s_bits"]! ) );
	return new JsonObject { ["force"] = force, ["speed"] = speed };
}

// GuestSpeed.Update (0xe6b28 with 0xffe38): the smoothed s (as bits), the decayed boost and the caps.
static JsonNode? GuestSpeedSmoothing( JsonObject a )
{
	var (s, boost, speed, force) = GuestSpeed.Update( BitConverter.Int32BitsToSingle( (int)(uint)a["s_bits"]! ), (int)a["base"]!,
		(int)a["bonus"]!, (ushort)(int)a["boost"]! );
	return new JsonObject { ["s_bits"] = (uint)BitConverter.SingleToInt32Bits( s ), ["boost"] = (int)boost, ["force"] = force, ["speed"] = speed };
}

// GuestSimulation.ArrivalGroupSize (0xcb4e8) on a fresh simulation. World mode 4 halts arrivals; the counted guests fill
// the guest list (a field) through reflection (only its count is read). The thing +48 word and the GameType mode word
// have no C# counterpart (ARRIVAL-002, F9), so they are not passed.
static JsonNode? ArrivalGroup( JsonObject a )
{
	var settings = new GuestSettings
	{
		ArrivalMinPeople = (int)a["min_people"]!,
		ArrivalNewParkBonus = (int)a["bonus"]!,
		ArrivalPointsPerVisitor = (int)a["per_visitor"]!,
	};
	var points = (int)a["points"]!;
	var halted = (int)a["world_mode"]! == 4;
	var simulation = new GuestSimulation( new GuestPathGrid( 1, 1 ), settings, 1 ) { ParkPoints = () => points, ArrivalsHalted = () => halted };
	var field = typeof( GuestSimulation ).GetField( "guests", BindingFlags.NonPublic | BindingFlags.Instance )
		?? throw new MissingFieldException( "GuestSimulation.guests" );
	((List<Guest>)field.GetValue( simulation )!).AddRange( Enumerable.Repeat<Guest>( null!, (int)a["guests"]! ) );
	return JsonValue.Create( simulation.ArrivalGroupSize() );
}

// ParkEconomy.ParkPoints (0xc24c0) on an economy built from the input: one synthetic object type per input type (kind 0 -> Ride,
// 2 -> Sideshow, any other kind -> Shop, the C# reading ARRIVAL-003; AttractionValue, NewAttractionDecayTime, NewBonus and
// Bumper.WhichTrackType (type +0x9C) from the input), one RegisterExisting object per thing. The economy clock is a read-only
// property, so its setter is reached through reflection to put each object's BuiltTick on the first tick of its build turn and the
// economy on the first tick of turn "now". The type's first-purchase turn (park record +28) is then set to the input's value, as a
// loaded save restores its counters after its objects: it can stem from a sold copy or be unset (0) with later copies standing;
// ParkPointsTests covers how purchases set it. The original's counts-test (0xdc834, the input's open flag) has no C# counterpart
// (ARRIVAL-003, F10), so it is not passed.
static JsonNode? ParkPoints( JsonObject a )
{
	var infos = a["types"]!.AsArray().Select( type =>
	{
		var kind = (int)type!["kind"]! switch { 0 => ParkObjectKind.Ride, 2 => ParkObjectKind.Sideshow, _ => ParkObjectKind.Shop };
		return new EconomyObjectInfo( (int)type["id"]!, $"Type {(int)type["id"]!}", kind, ResearchCategory.Ride, 0,
			new[] { new UpgradeLevelInfo( 0, 0, 0, 0, 0, new[] { 0 } ) }, null, null, null, 0, 0, 0, 0, 0, (int)type["value"]!, "synthetic.wad",
			0, 0, (int)type["decay"]!, type["bonus"]!.AsArray().Select( bonus => (int)bonus! ).ToArray(), (int)type["track_type"]! );
	} ).ToList();
	var park = new ParkEconomy( EconomyTestData.Settings(), new EconomyObjectCatalog( infos ), ParkGameMode.FullSimulation, 1 );
	var tick = typeof( ParkEconomy ).GetProperty( "Tick" )!.GetSetMethod( nonPublic: true )
		?? throw new MissingMethodException( "ParkEconomy.Tick setter" );
	foreach ( var thing in a["things"]!.AsArray() )
	{
		tick.Invoke( park, new object[] { ParkCalendar.TickOfTurn( (long)(int)thing!["built"]! ) } );
		park.RegisterExisting( (int)thing["type"]! );
	}
	foreach ( var type in a["types"]!.AsArray() )
	{
		var key = ParkCounters.FirstPurchaseTurn( (int)type!["id"]! );
		park.Counters.Add( key, (long)(uint)(int)type["first"]! - park.Counters[key] );
	}
	tick.Invoke( park, new object[] { ParkCalendar.TickOfTurn( (long)(int)a["now"]! ) } );
	return JsonValue.Create( park.ParkPoints() );
}
